#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// A market action outside the action step (market.extra_action): Doctor Aphra's back ("Planning: perform a market
    /// action") and W1-LE ("Encounter: perform a market action, even if you are at a navpoint or performed this
    /// action during your action step"). The same two steps as the action (R-16.18 to R-16.25): up to two discards
    /// from one deck, then one purchase with barter; it does not use up the action step's market action.
    /// </summary>
    internal sealed class MarketSessionHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.market_session";
        private const string Done = "done";
        private const string Cancel = "cancel";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var owner = state.FindPlayer(frame.Get("owner"))!;
            if (frame.Step == 1)
            {
                // A purchase finished: the session is over (R-16.21).
                return StepResult.Done();
            }
            if (frame.Vars.TryGetValue("barter_pick", out var bp))
            {
                frame.Vars.Remove("barter_pick");
                if (bp == Cancel)
                {
                    // Back to the market menu with nothing bartered: a choice of barter can make the price unpayable.
                    frame.Vars.Remove("barter");
                    frame.Vars.Remove("buy_deck");
                    return StepResult.Continue();
                }
                if (bp != Done)
                {
                    frame.Set("barter", frame.Vars.TryGetValue("barter", out var b) && b.Length > 0 ? b + "," + bp : bp);
                    return AskBarter(ctx, frame, owner);
                }
                frame.Step = 1;
                var buy = new Frame(Market.BuyHandler);
                buy.Set("player", owner.Id);
                buy.Set("deck", frame.Get("buy_deck"));
                buy.Set("barter", frame.Vars.TryGetValue("barter", out var all) ? all : "");
                return StepResult.Call(buy);
            }
            if (frame.Vars.TryGetValue("pick", out var pick))
            {
                frame.Vars.Remove("pick");
                if (pick == Done) return StepResult.Done();
                var parts = pick.Split(':');
                var deck = Market.Deck(state, parts[1]);
                if (parts[0] == "discard")
                {
                    // R-16.18, R-16.19: to the bottom, a second one only from the same deck; no icons (R-16.20).
                    var card = deck.Cards[0];
                    deck.Cards.RemoveAt(0);
                    deck.Cards.Add(card);
                    frame.Set("discards", (frame.Vars.ContainsKey("discards") ? frame.GetInt("discards") : 0) + 1);
                    frame.Set("discard_deck", deck.Deck);
                    ctx.Events.Add(new MarketCardDiscardedEvent(deck.Deck, card));
                    ctx.Events.Add(new MarketCardRevealedEvent(deck.Deck, deck.Cards[0]));
                }
                else
                {
                    frame.Set("buy_deck", deck.Deck);
                    return AskBarter(ctx, frame, owner);
                }
            }
            var options = new List<DecisionOption>();
            int discards = frame.Vars.ContainsKey("discards") ? frame.GetInt("discards") : 0;
            foreach (var d in state.MarketDecks)
            {
                if (d.Cards.Count == 0) continue;
                bool sameDeck = !frame.Vars.ContainsKey("discard_deck") || frame.Get("discard_deck") == d.Deck;
                if (discards < 2 && sameDeck) options.Add(new DecisionOption("discard:" + d.Deck, "market.discard"));
                if (Affordable(ctx, owner, d.Deck)) options.Add(new DecisionOption("buy:" + d.Deck, "market.buy"));
            }
            options.Add(new DecisionOption(Done, "market.done"));
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "market.session", options, "pick"));
        }

        private static List<string> Barterable(PlayerState owner, Frame frame)
        {
            var chosen = frame.Vars.TryGetValue("barter", out var b) && b.Length > 0 ? new List<string>(b.Split(',')) : new List<string>();
            var list = new List<string>();
            foreach (var a in owner.Assets)
            {
                if (!chosen.Contains(a.CardId) && Assets.BarterValue(new[] { a }, out _) != null) list.Add(a.CardId);
            }
            return list;
        }

        private static bool Affordable(ResolutionContext ctx, PlayerState owner, string deck)
        {
            var all = new List<string>();
            foreach (var a in owner.Assets)
            {
                if (Assets.BarterValue(new[] { a }, out _) != null) all.Add(a.CardId);
            }
            return Market.Check(ctx.Content, ctx.State, owner, deck, all, out _) == null
                || Market.Check(ctx.Content, ctx.State, owner, deck, new string[0], out _) == null;
        }

        /// <summary>R-16.31: barter one asset at a time; "done" once the price can be paid.</summary>
        private static StepResult AskBarter(ResolutionContext ctx, Frame frame, PlayerState owner)
        {
            var chosen = frame.Vars.TryGetValue("barter", out var b) && b.Length > 0 ? b.Split(',') : new string[0];
            bool payable = Market.Check(ctx.Content, ctx.State, owner, frame.Get("buy_deck"), chosen, out int price) == null;
            var options = new List<DecisionOption>();
            if (price > 0)
            {
                foreach (var id in Barterable(owner, frame)) options.Add(new DecisionOption(id, id));
            }
            if (payable) options.Add(new DecisionOption(Done, "effects.barter_done"));
            if (options.Count == 1 && payable)
            {
                frame.Set("barter_pick", Done);
                return StepResult.Continue();
            }
            options.Add(new DecisionOption(Cancel, "effects.barter_cancel"));
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.barter", options, "barter_pick"));
        }
    }

    /// <summary>"Gain card #90" (Nal Hutta-5): a starting bounty not held by anyone; with none, the "else" effects.</summary>
    internal sealed class GainDatabankHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.gain_databank";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            if (frame.Step > 0) return StepResult.Done();
            frame.Step = 1;
            var candidates = ctx.Content.DatabankCards((int)node["number"]!);
            foreach (var p in ctx.State.Players)
            {
                foreach (var a in p.Assets) candidates.Remove(a.CardId);
            }
            if (candidates.Count == 0)
            {
                if (node["else"] is JArray)
                {
                    return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"), frame.Get("path") + ".else", frame));
                }
                ctx.Return("failed", "true");
                return StepResult.Done();
            }
            var cardId = candidates[ctx.State.Random.NextInt(candidates.Count)];
            var gain = new Frame(Assets.GainHandler);
            gain.Set("player", owner.Id);
            gain.Set("card", cardId);
            gain.Set("kind", (string)ctx.Content.Card(cardId)["asset_kind"]!);
            gain.Set("cost", "0");
            gain.Set("traits", "");
            return StepResult.Call(gain);
        }
    }

    /// <summary>Card rules for crew and luxury assets (EFFECTS.md §8) that need decisions.</summary>
    internal sealed class AssetCustomHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.asset_custom";

        public static readonly HashSet<string> Handlers = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "stolen_jewels", "undercover_agent", "dejarik", "evazan",
        };

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var state = ctx.State;
            switch ((string)node["handler"]!)
            {
                case "stolen_jewels":
                    // "Choose another player in your space ... You both gain 7,000 and 1 fame."
                    if (frame.Step == 0)
                    {
                        frame.Step = 1;
                        return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_player", Others(state, owner, null), "target");
                    }
                    foreach (var p in new[] { owner, state.FindPlayer(frame.Get("target"))! })
                    {
                        PlayerRules.GainCredits(p, 7000, ctx.Events);
                        PlayerRules.GainFame(ctx.Content, state, p, 1, ctx.Events);
                    }
                    return StepResult.Done();
                case "undercover_agent":
                    // "...make that player lose 1 reputation of your choice" (the player with more fame than you).
                    if (frame.Step == 0)
                    {
                        frame.Step = 1;
                        var options = Others(state, owner, p => PlayerRules.Fame(ctx.Content, state, p) > PlayerRules.Fame(ctx.Content, state, owner));
                        if (options.Count == 0) return Fail(ctx);
                        return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_player", options, "target");
                    }
                    if (frame.Step == 1)
                    {
                        frame.Step = 2;
                        return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_faction",
                            new List<string> { "rebel", "imperial", "hutt", "syndicate" }, "faction");
                    }
                    PlayerRules.LoseReputation(state.FindPlayer(frame.Get("target"))!, Patrols.Parse(frame.Get("faction")), 1, ctx.Events);
                    return StepResult.Done();
                case "dejarik":
                {
                    // "Roll 1 die for each crew you have. For each {HIT} or {FOCUS} result, gain 1,000."
                    int crew = Selectors.Counter(owner, "crew");
                    var faces = DiceRoller.Roll(state.Random, crew);
                    ctx.Events.Add(new EffectDiceRolledEvent(owner.Id, Combat.PlayerLabel(ctx.Content, owner), frame.Get("card"), faces));
                    int n = 0;
                    foreach (var f in faces)
                    {
                        if (f == DieFace.Hit || f == DieFace.Focus) n++;
                    }
                    PlayerRules.GainCredits(owner, 1000 * n, ctx.Events);
                    return StepResult.Done();
                }
                case "evazan":
                {
                    // "At the start of ground combat, choose a die result. During the combat, both players may turn up
                    // to 2 of their blank dice to the chosen result."
                    if (frame.Step == 0)
                    {
                        frame.Step = 1;
                        return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "custom.choose_face", new List<string> { "crit", "hit", "focus" }, "face");
                    }
                    var combat = Jobs.Nearest(state, Combat.Handler);
                    combat?.Set("evazan", frame.Get("face"));
                    return StepResult.Done();
                }
                default:
                    throw new System.NotSupportedException("Custom handler '" + (string)node["handler"]! + "' is not implemented yet.");
            }
        }

        private static List<string> Others(GameState state, PlayerState owner, System.Func<PlayerState, bool>? where)
        {
            var list = new List<string>();
            foreach (var p in state.Players)
            {
                if (p.Id != owner.Id && p.SpaceId == owner.SpaceId && (where == null || where(p))) list.Add(p.Id);
            }
            return list;
        }

        private static StepResult Fail(ResolutionContext ctx)
        {
            ctx.Return("failed", "true");
            return StepResult.Done();
        }
    }

    /// <summary>
    /// Bounty against crew as the turn's encounter (R-4.19, R-15.56 to R-15.64): your own crew matching your bounty,
    /// or another player's in your space. Their owner may protect it and fight you instead (R-15.61); otherwise the
    /// game rolls for the crew (R-15.63, DECISIONS §4.2). Winning discards the crew, and its contact is eliminated or
    /// captured (R-15.57, R-15.62). Losing against your own crew discards it and its token returns to the map (R-15.58).
    /// Vars: "player", "target" ("bounty|crew owner|crew card").
    /// </summary>
    internal sealed class BountyCrewHandler : IResolutionHandler
    {
        public const string HandlerId = "encounter.bounty_crew";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Vars.ContainsKey("player") ? frame.Get("player") : state.CurrentPlayerId)!;
            var parts = frame.Get("target").Split('|');
            var bountyId = parts[0];
            var holder = state.FindPlayer(parts[1])!;
            var crewId = parts[2];
            var bounty = ctx.Content.Card(bountyId);
            var token = Contacts.Token(state, (string)bounty["target"]!);
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    if (holder.Id == player.Id)
                    {
                        frame.Set("protect", "no");
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, holder.Id, "rules.protect_crew",
                        new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "protect"));
                case 1:
                {
                    frame.Step = 2;
                    var combat = new Frame(Combat.Handler);
                    combat.Set("attacker", player.Id);
                    if (frame.Get("protect") == "yes")
                    {
                        // R-15.61: ground combat on a planet, ship combat otherwise.
                        combat.Set("type", Combat.BetweenPlayers(ctx.Content, player, holder) == CombatType.Ground ? "ground" : "ship");
                        combat.Set("defender_kind", "player");
                        combat.Set("defender", holder.Id);
                        combat.Set("defender_value", "0");
                    }
                    else
                    {
                        combat.Set("type", (string)bounty["combat"]!["type"]!);
                        combat.Set("defender_kind", "contact");
                        combat.Set("defender", token.Id);
                        combat.Set("defender_value", ((int)bounty["combat"]!["value"]!).ToString(CultureInfo.InvariantCulture));
                    }
                    // R-15.59: the crew being fought lends no skill or ability to its owner meanwhile.
                    combat.Set("ignore_asset", crewId);
                    return StepResult.Call(combat);
                }
                case 2:
                {
                    bool won = frame.Get("ret.winner") == "attacker";
                    var crew = Assets.Find(holder, crewId);
                    if (!won)
                    {
                        frame.Step = 9;
                        // R-15.58: against your own crew, losing discards it and its token returns faceup; R-15.64: nothing else.
                        if (holder.Id == player.Id && crew != null) Assets.Discard(ctx.Content, state, holder, crew, "bounty_lost", ctx.Events);
                        return StepResult.Continue();
                    }
                    if (crew != null)
                    {
                        holder.Assets.Remove(crew); // the token does not go back to the map: it is the bounty's now
                        ctx.Events.Add(new AssetDiscardedEvent(holder.Id, crewId, "crew", "bounty"));
                        Market.ReturnToDeck(ctx.Content, state, crewId);
                    }
                    frame.Step = 3;
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.eliminate_or_capture",
                        new[] { new DecisionOption("eliminate", "rules.eliminate"), new DecisionOption("capture", "rules.capture") }, "how"));
                }
                case 3:
                    if (frame.Get("how") == "capture")
                    {
                        token.Pool = false;
                        token.SpaceId = "";
                        token.Slot = "";
                        token.Bounty = bountyId;
                        token.CapturedThisTurn = true;
                        ctx.Events.Add(new ContactCapturedEvent(player.Id, token.Id, bountyId));
                        frame.Step = 9;
                        return StepResult.Continue();
                    }
                    frame.Step = 4;
                    return StepResult.Call(EffectHandlers.ListFrame(player.Id, bountyId, "elimination_reward"));
                case 4:
                    frame.Step = 9;
                    token.Pool = false;
                    Bounties.RemoveFromGame(ctx, player, bountyId, token);
                    ctx.Events.Add(new ContactEliminatedEvent(player.Id, token.Id, bountyId));
                    return StepResult.Call(TriggersHandler.For("bounty_reward", player.Id));
                default:
                    ctx.Events.Add(new EncounterResolvedEvent(player.Id, "bounty", bountyId));
                    return StepResult.Done();
            }
        }

        /// <summary>The crew card of a contact, if a player holds it: crew printed on the contact's databank card.</summary>
        internal static AssetState? CrewOf(Content.GameContent content, PlayerState player, string token)
        {
            int number = (int)content.Card(token)["databank"]!;
            foreach (var a in player.Assets)
            {
                if (a.Kind != "crew" || !content.HasCard(a.CardId)) continue;
                var host = (string?)content.Card(a.CardId)["embedded_in"];
                if (host != null && content.HasCard(host) && (int?)content.Card(host)["number"] == number) return a;
            }
            return null;
        }
    }

    /// <summary>Chromium Hull: "A player in your space can fight a ship combat against you as their encounter."</summary>
    internal sealed class ChallengeHandler : IResolutionHandler
    {
        public const string HandlerId = "encounter.player";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!;
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var combat = new Frame(Combat.Handler);
                combat.Set("type", "ship");
                combat.Set("attacker", player.Id);
                combat.Set("defender_kind", "player");
                combat.Set("defender", frame.Get("target"));
                combat.Set("defender_value", "0");
                return StepResult.Call(combat);
            }
            ctx.Events.Add(new EncounterResolvedEvent(player.Id, "player", frame.Get("target")));
            return StepResult.Done();
        }

        /// <summary>Whether another player offers this encounter (holds an asset with the chromium_hull rule).</summary>
        internal static bool Offers(Content.GameContent content, PlayerState other)
        {
            foreach (var a in AbilitySources.Of(content, other))
            {
                foreach (JObject e in (JArray)a.Ability["effects"]!)
                {
                    if ((string?)e["op"] == "custom" && (string?)e["handler"] == "chromium_hull") return true;
                }
            }
            return false;
        }
    }
}
