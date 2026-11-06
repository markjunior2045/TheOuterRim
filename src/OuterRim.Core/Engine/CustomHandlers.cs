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
    /// Card-specific rules that need decisions (EFFECTS.md §8), run as frames. The node's "params" carry the
    /// card's numbers. Each handler names the card it serves.
    /// </summary>
    internal sealed class CustomFrameHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.custom";

        /// <summary>Custom handlers run here (the others are instant, in <see cref="Instant"/>).</summary>
        public static readonly HashSet<string> Handlers = new HashSet<string>(System.StringComparer.Ordinal)
        {
            "double_or_nothing", "dice_gamble", "side_bets", "name_a_number", "lose_any_reputation",
            "buy_any_sheet", "replace_with_starter", "pay_for_goal", "patrol_hunt",
        };

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var p = node["params"] as JObject ?? new JObject();
            switch ((string)node["handler"]!)
            {
                case "double_or_nothing": return DoubleOrNothing(ctx, frame, owner, p);
                case "dice_gamble": return DiceGamble(ctx, frame, owner, p);
                case "side_bets": return SideBets(ctx, frame, owner, p);
                case "name_a_number": return NameANumber(ctx, frame, owner, p);
                case "lose_any_reputation": return LoseAnyReputation(ctx, frame, owner, p);
                case "buy_any_sheet": return BuyAnySheet(ctx, frame, owner);
                case "replace_with_starter": return ReplaceWithStarter(ctx, frame, owner);
                case "pay_for_goal": return PayForGoal(ctx, frame, owner);
                case "patrol_hunt": return PatrolHunt(ctx, frame, owner, p);
                default: throw new System.NotSupportedException("Custom handler '" + (string)node["handler"]! + "' is not implemented yet.");
            }
        }

        private static int Hits(IReadOnlyList<DieFace> faces, params DieFace[] which)
        {
            int n = 0;
            foreach (var f in faces)
            {
                if (System.Array.IndexOf(which, f) >= 0) n++;
            }
            return n;
        }

        private static IReadOnlyList<DieFace> Roll(ResolutionContext ctx, PlayerState owner, int count)
        {
            var faces = DiceRoller.Roll(ctx.State.Random, count);
            ctx.Events.Add(new EffectDiceRolledEvent(owner.Id, Combat.PlayerLabel(ctx.Content, owner), "", faces));
            return faces;
        }

        private static StepResult Fail(ResolutionContext ctx)
        {
            ctx.Return("failed", "true");
            return StepResult.Done();
        }

        /// <summary>Cantonica-1: roll 2 dice; on a {HIT}, take 4,000 or roll again; a {HIT} again gives 8,000.</summary>
        private static StepResult DoubleOrNothing(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            int dice = (int?)p["dice"] ?? 2;
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    if (Hits(Roll(ctx, owner, dice), DieFace.Hit) == 0) return StepResult.Done();
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "custom.double_or_nothing",
                        new[] { new DecisionOption("take", "custom.take_winnings"), new DecisionOption("again", "custom.roll_again") }, "answer"));
                default:
                    if (frame.Get("answer") == "take")
                    {
                        PlayerRules.GainCredits(owner, (int?)p["first"] ?? 4000, ctx.Events);
                    }
                    else if (Hits(Roll(ctx, owner, dice), DieFace.Hit) > 0)
                    {
                        PlayerRules.GainCredits(owner, (int?)p["second"] ?? 8000, ctx.Events);
                    }
                    return StepResult.Done();
            }
        }

        /// <summary>Nal Hutta-6: spend up to 3,000; 1 die per 1,000; with no {HIT} or {CRIT}, gain triple.</summary>
        private static StepResult DiceGamble(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            int max = (int?)p["max"] ?? 3000, step = (int?)p["step"] ?? 1000;
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var options = new List<DecisionOption>();
                for (int v = 0; v <= max && v <= owner.Credits; v += step)
                {
                    options.Add(new DecisionOption(v.ToString(CultureInfo.InvariantCulture), v.ToString(CultureInfo.InvariantCulture)));
                }
                if (options.Count == 1) return StepResult.Done();
                return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_amount", options, "spend"));
            }
            int spent = frame.GetInt("spend");
            if (spent == 0) return StepResult.Done();
            PlayerRules.TrySpend(owner, spent, ctx.Events);
            if (Hits(Roll(ctx, owner, spent / step), DieFace.Hit, DieFace.Crit) == 0)
            {
                PlayerRules.GainCredits(owner, spent * ((int?)p["factor"] ?? 3), ctx.Events);
            }
            return StepResult.Done();
        }

        /// <summary>
        /// Cantonica-8: you choose a die result; each other player, in turn order, may give you 1,000 to also
        /// choose one; then you roll 1 die and everyone who chose the rolled result gains its credits.
        /// </summary>
        private static StepResult SideBets(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            var state = ctx.State;
            var bettors = new List<string> { owner.Id };
            int seat = state.SeatOf(owner.Id);
            for (int k = 1; k < state.Players.Count; k++) bettors.Add(state.Players[(seat + k) % state.Players.Count].Id);
            int i = frame.Vars.ContainsKey("i") ? frame.GetInt("i") : 0;
            if (frame.Vars.TryGetValue("answer", out var answer))
            {
                frame.Vars.Remove("answer");
                if (answer != "pass")
                {
                    var bettor = state.FindPlayer(bettors[i])!;
                    if (bettor.Id != owner.Id)
                    {
                        PlayerRules.TrySpend(bettor, (int?)p["bet"] ?? 1000, ctx.Events);
                        PlayerRules.GainCredits(owner, (int?)p["bet"] ?? 1000, ctx.Events);
                    }
                    frame.Set("bet." + bettor.Id, answer);
                }
                i++;
                frame.Set("i", i);
            }
            while (i < bettors.Count)
            {
                var who = state.FindPlayer(bettors[i])!;
                bool you = who.Id == owner.Id;
                if (!you && who.Credits < ((int?)p["bet"] ?? 1000))
                {
                    i++;
                    frame.Set("i", i);
                    continue;
                }
                var options = new List<DecisionOption>
                {
                    new DecisionOption("crit", "face.crit"), new DecisionOption("focus", "face.focus"), new DecisionOption("hit", "face.hit"),
                };
                if (!you) options.Add(new DecisionOption("pass", "custom.no_bet"));
                return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, who.Id, "custom.side_bets", options, "answer"));
            }
            var face = DeliverHandler.FaceId(Roll(ctx, owner, 1)[0]);
            foreach (var id in bettors)
            {
                if (frame.Vars.TryGetValue("bet." + id, out var bet) && bet == face)
                {
                    PlayerRules.GainCredits(state.FindPlayer(id)!, (int?)p[face] ?? 0, ctx.Events);
                }
            }
            return StepResult.Done();
        }

        /// <summary>Cantonica-10: name a number, roll 5 dice; at least that many {HIT}s gives the number × 2,000.</summary>
        private static StepResult NameANumber(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            int dice = (int?)p["dice"] ?? 5;
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var options = new List<DecisionOption>();
                for (int n = 1; n <= dice; n++)
                {
                    options.Add(new DecisionOption(n.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture)));
                }
                return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "custom.name_a_number", options, "number"));
            }
            int named = frame.GetInt("number");
            if (Hits(Roll(ctx, owner, dice), DieFace.Hit) >= named)
            {
                PlayerRules.GainCredits(owner, named * ((int?)p["per_hit"] ?? 2000), ctx.Events);
            }
            return StepResult.Done();
        }

        /// <summary>Kessel-10: lose any amount of reputation with each listed faction, one faction at a time.</summary>
        private static StepResult LoseAnyReputation(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            var factions = ((JArray)p["factions"]!).ToObject<List<string>>()!;
            int i = frame.Vars.ContainsKey("i") ? frame.GetInt("i") : 0;
            if (frame.Vars.TryGetValue("amount", out var amount))
            {
                frame.Vars.Remove("amount");
                PlayerRules.LoseReputation(owner, Patrols.Parse(factions[i]), int.Parse(amount, CultureInfo.InvariantCulture), ctx.Events);
                i++;
                frame.Set("i", i);
            }
            while (i < factions.Count)
            {
                int room = (int)owner.Reputation.Get(Patrols.Parse(factions[i])) + 1; // steps down to negative
                if (room == 0)
                {
                    i++;
                    frame.Set("i", i);
                    continue;
                }
                var options = new List<DecisionOption>();
                for (int n = 0; n <= room; n++) options.Add(new DecisionOption(n.ToString(CultureInfo.InvariantCulture), n.ToString(CultureInfo.InvariantCulture)));
                return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "custom.lose_reputation." + factions[i], options, "amount"));
            }
            return StepResult.Done();
        }

        /// <summary>
        /// Ryloth-6: buy any ship sheet from the supply for its cost (bartering the current ship, R-16.38), on
        /// its ship goal side; then remove that ship's card from the ship deck and reshuffle it.
        /// </summary>
        private static StepResult BuyAnySheet(ResolutionContext ctx, Frame frame, PlayerState owner)
        {
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    int current = ctx.Content.ShipSide(owner.ShipSideId).Cost ?? 0;
                    var options = new List<string>();
                    foreach (var sheet in ctx.State.ShipSupply)
                    {
                        if (options.Contains(sheet) || ctx.Content.ShipSide(sheet).Role == "starter") continue;
                        int cost = ctx.Content.ShipGoalSide(sheet).Cost ?? 0;
                        if (owner.Credits >= Assets.PriceAfterBarter(cost, current)) options.Add(sheet);
                    }
                    if (options.Count == 0) return Fail(ctx);
                    options.Sort(System.StringComparer.Ordinal);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "custom.choose_sheet", options, "sheet");
                }
                case 1:
                {
                    frame.Step = 2;
                    var buy = new Frame(Assets.BuyShipHandler);
                    buy.Set("player", owner.Id);
                    buy.Set("sheet", frame.Get("sheet"));
                    buy.Set("barter", "");
                    return StepResult.Call(buy);
                }
                default:
                {
                    var deck = Market.Deck(ctx.State, "ship");
                    foreach (var id in new List<string>(deck.Cards))
                    {
                        if ((string?)ctx.Content.Card(id)["sheet"] == frame.Get("sheet")) deck.Cards.Remove(id);
                    }
                    ctx.State.Random.Shuffle(deck.Cards);
                    ctx.Events.Add(new MarketDeckShuffledEvent(deck.Deck));
                    if (deck.Cards.Count > 0) ctx.Events.Add(new MarketCardRevealedEvent(deck.Deck, deck.Cards[0]));
                    return StepResult.Done();
                }
            }
        }

        /// <summary>Ryloth-10: replace the ship sheet with a STARTER SHIP sheet from the supply (no cost, no barter).</summary>
        private static StepResult ReplaceWithStarter(ResolutionContext ctx, Frame frame, PlayerState owner)
        {
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var options = new List<string>();
                foreach (var sheet in ctx.State.ShipSupply)
                {
                    if (!options.Contains(sheet) && ctx.Content.ShipSide(sheet).Role == "starter") options.Add(sheet);
                }
                if (options.Count == 0) return Fail(ctx);
                options.Sort(System.StringComparer.Ordinal);
                return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "custom.choose_sheet", options, "sheet");
            }
            if (frame.Step == 1)
            {
                frame.Step = 2;
                var sheet = frame.Get("sheet");
                ctx.State.ShipSupply.Remove(sheet);
                ctx.State.ShipSupply.Add(owner.ShipSheetId); // R-16.29: its market card does not come back
                Goals.ClearTokens(owner, owner.ShipSheetId);
                var old = owner.ShipSheetId;
                owner.ShipSheetId = sheet;
                owner.ShipSideId = sheet;
                owner.ShipDamage = 0;
                ctx.Events.Add(new ShipChangedEvent(owner.Id, old, sheet, sheet, 0));
                var fit = new Frame(Assets.FitHandler);
                fit.Set("player", owner.Id);
                return StepResult.Call(fit); // R-16.30: assets that no longer fit are discarded
            }
            return StepResult.Done();
        }

        /// <summary>
        /// Navpoint-6: "choose a player who has {REP_NEG} reputation with a patrol in your space. Move that patrol
        /// directly to that player's space, and they must encounter that patrol." Navpoint-9 (secret): the patrol
        /// just moved to your space is encountered by a player in your space who is negative with it. That
        /// player fights it now, as its attacker.
        /// </summary>
        private static StepResult PatrolHunt(ResolutionContext ctx, Frame frame, PlayerState owner, JObject p)
        {
            var state = ctx.State;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var options = new List<string>();
                    if ((string?)p["patrol"] == "moved")
                    {
                        if (frame.Vars.TryGetValue("ctx.moved_patrol", out var moved)) options.Add(moved);
                    }
                    else
                    {
                        foreach (var patrol in state.PatrolsIn(owner.SpaceId)) options.Add(Patrols.Id(patrol.Faction));
                    }
                    if (options.Count == 0) return Fail(ctx);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_patrol", options, "patrol");
                }
                case 1:
                {
                    frame.Step = 2;
                    var faction = Patrols.Parse(frame.Get("patrol"));
                    bool here = (string?)p["target"] == "negative_in_your_space";
                    var options = new List<string>();
                    foreach (var player in state.Players)
                    {
                        if (player.Id == owner.Id || player.Reputation.Get(faction) != Reputation.Negative) continue; // R-25.19
                        if (here && player.SpaceId != owner.SpaceId) continue;
                        options.Add(player.Id);
                    }
                    if (options.Count == 0) return Fail(ctx);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_player", options, "target");
                }
                case 2:
                {
                    frame.Step = 3;
                    var faction = Patrols.Parse(frame.Get("patrol"));
                    var target = state.FindPlayer(frame.Get("target"))!;
                    if ((bool?)p["move_patrol"] == true)
                    {
                        var patrol = Patrols.Of(state, faction);
                        var from = patrol.SpaceId;
                        patrol.SpaceId = target.SpaceId; // directly (R-5.23)
                        ctx.Events.Add(new PatrolMovedEvent(faction, from, target.SpaceId, true));
                    }
                    var encounter = new Frame(Patrols.EncounterHandler);
                    encounter.Set("target", frame.Get("patrol"));
                    encounter.Set("player", target.Id);
                    return StepResult.Call(encounter);
                }
                default:
                    return StepResult.Done();
            }
        }

        /// <summary>Ring of Kafrene-10: complete your personal goal or ship goal, gaining fame as normal.</summary>
        private static StepResult PayForGoal(ResolutionContext ctx, Frame frame, PlayerState owner)
        {
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var options = new List<string>();
                foreach (var a in AbilitySources.Of(ctx.Content, owner))
                {
                    if (Goals.Progress(a) != null) options.Add(a.Key);
                }
                if (options.Count == 0) return Fail(ctx);
                return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "custom.choose_goal", options, "goal");
            }
            if (frame.Step == 1)
            {
                frame.Step = 2;
                var key = frame.Get("goal");
                int hash = key.IndexOf('#');
                var use = new Frame(EffectHandlers.Ability);
                use.Set("owner", owner.Id);
                use.Set("card", key.Substring(0, hash));
                use.Set("path", key.Substring(hash + 1));
                return StepResult.Call(use);
            }
            return StepResult.Done();
        }
    }

    /// <summary>
    /// GA-97: "secretly look at the top card of any encounter deck. Then place that card on the top or bottom
    /// of that deck." (encounter_deck.peek). The look is private (only its owner sees the card id).
    /// </summary>
    internal sealed class EncounterPeekHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.encounter_peek";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var decks = new List<string>();
                    foreach (var d in ctx.State.EncounterDecks)
                    {
                        if (d.Cards.Count > 0) decks.Add(d.Deck);
                    }
                    if (decks.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_deck", decks, "deck");
                }
                case 1:
                {
                    frame.Step = 2;
                    var deck = Encounters.Deck(ctx.State, frame.Get("deck"));
                    ctx.Events.Add(new EncounterCardPeekedEvent(owner.Id, deck.Deck, deck.Cards[0]));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.top_or_bottom",
                        new[] { new DecisionOption("top", "effects.top"), new DecisionOption("bottom", "effects.bottom") }, "where"));
                }
                default:
                {
                    if (frame.Get("where") == "bottom")
                    {
                        var deck = Encounters.Deck(ctx.State, frame.Get("deck"));
                        var top = deck.Cards[0];
                        deck.Cards.RemoveAt(0);
                        deck.Cards.Add(top);
                    }
                    return StepResult.Done();
                }
            }
        }
    }
}
