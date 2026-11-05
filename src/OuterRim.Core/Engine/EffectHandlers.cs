#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// The effect interpreter (PLAN T2.3): runs effect lists from card data on the resolution stack.
    /// A frame points at a list by card id and JSON path, so a card effect paused for a decision
    /// saves and loads like any other state. Child lists report "ret.failed" when something in them
    /// could not be fully done, which "to" uses (R-21.4).
    /// </summary>
    internal static class EffectHandlers
    {
        public const string Ability = "effects.ability";
        public const string List = "effects.list";
        public const string Choice = "effects.choice";
        public const string May = "effects.may";
        public const string To = "effects.to";
        public const string Test = "effects.test";
        public const string Fight = "effects.combat";
        public const string FightOutcome = "effects.combat_outcome";

        /// <summary>Effect ops this engine runs. The coverage test checks card data against it.</summary>
        public static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "choice", "may", "to", "test", "combat",
            "credits.gain", "credits.spend", "credits.lose", "fame.gain", "fame.lose", "rep.gain", "rep.lose",
            "damage.suffer", "damage.recover", "card.discard_self", "sheet.flip", "planet.marker",
            "dice.reroll", "dice.turn", "dice.cancel", "dice.add", "dice.roll", "asset.discard", "asset.take",
            "test.skill_grant", "move.no_stop", "custom", "cargo.deliver", "cargo.discard", "turn.end", "step.end", "unless",
            "asset.gain_embedded", "encounter.space", "player.choose", "credits.take", "turn.additional",
            "move.player", "move.player_direct", "move.patrol", "move.patrol_direct", "encounter.patrol",
            "market.buy", "market.take", "encounter_deck.peek", "damage.prevent",
            "contact.discard_token", "contact.relocate_self", "contact.place_facedown", "contact.look", "contact.move",
            "encounter.contact", "bounty.deliver", "databank.resolve", "job.complete", "job.fail", "job.discard",
            "step.goto", "step.repeat", "sabacc.win", "sabacc.stake", "sabacc.duel", "reward.modify",
            "market.extra_action", "asset.gain_databank", "test.pass", "combat.auto_win", "damage.crit_value",
            // Passive rule changes read by the rules that check them.
            "slot.modify", "limit.trait", "rule.modify",
            // Replacement-only ops, applied by the damage frame.
            "damage.cap",
            // Passive-only ops, read as queries by Modifiers or by the rules that check them.
            "stat.modify", "dice.opponent_fewer", "fame.extra",
        };

        /// <summary>Custom handlers (EFFECTS.md §8) this engine runs; patrol_redirect runs inside patrol movement.</summary>
        public static readonly HashSet<string> CustomImplemented = new HashSet<string>(StringComparer.Ordinal)
        {
            "tractor_beam", "patrol_redirect", "used_ship_for_sale",
            "double_or_nothing", "dice_gamble", "side_bets", "name_a_number", "lose_any_reputation",
            "buy_any_sheet", "replace_with_starter", "pay_for_goal", "patrol_hunt", "look_and_exchange", "sabacc", "wanted", "reward_for_fame",
            "stolen_jewels", "undercover_agent", "dejarik", "evazan", "chromium_hull",
        };

        public static void Register()
        {
            ResolutionHandlers.Register(new AbilityHandler());
            ResolutionHandlers.Register(new ListHandler());
            ResolutionHandlers.Register(new ChoiceHandler());
            ResolutionHandlers.Register(new MayHandler());
            ResolutionHandlers.Register(new ToHandler());
            ResolutionHandlers.Register(new TestHandler());
            ResolutionHandlers.Register(new FightHandler());
            ResolutionHandlers.Register(new FightOutcomeHandler());
            ResolutionHandlers.Register(new DiceWindowHandler());
            ResolutionHandlers.Register(new DiceOpHandler());
            ResolutionHandlers.Register(new TriggersHandler());
            ResolutionHandlers.Register(new SufferDamageHandler());
            ResolutionHandlers.Register(new EffectDiceRollHandler());
            ResolutionHandlers.Register(new AssetDiscardHandler());
            ResolutionHandlers.Register(new AssetTakeHandler());
            ResolutionHandlers.Register(new MoveHandler());
            ResolutionHandlers.Register(new DeliverHandler());
            ResolutionHandlers.Register(new DeliverCargoHandler());
            ResolutionHandlers.Register(new CargoCheckHandler());
            ResolutionHandlers.Register(new UnlessHandler());
            ResolutionHandlers.Register(new SelectHandler());
            ResolutionHandlers.Register(new PlayerChooseHandler());
            ResolutionHandlers.Register(new MovePlayerEffectHandler());
            ResolutionHandlers.Register(new MoveDirectHandler());
            ResolutionHandlers.Register(new MovePatrolEffectHandler());
            ResolutionHandlers.Register(new EncounterPatrolEffectHandler());
            ResolutionHandlers.Register(new MarketEffectBuyHandler());
            ResolutionHandlers.Register(new MarketTakeHandler());
            ResolutionHandlers.Register(new CustomFrameHandler());
            ResolutionHandlers.Register(new EncounterPeekHandler());
            ResolutionHandlers.Register(new ContactEncounterHandler());
            ResolutionHandlers.Register(new BountyOutcomeHandler());
            ResolutionHandlers.Register(new DeliverBountyHandler());
            ResolutionHandlers.Register(new PlaceContactHandler());
            ResolutionHandlers.Register(new ContactEffectHandler());
            ResolutionHandlers.Register(new LookAndExchangeHandler());
            ResolutionHandlers.Register(new BountyDeliverEffectHandler());
            ResolutionHandlers.Register(new JobAttemptHandler());
            ResolutionHandlers.Register(new DatabankResolveHandler());
            ResolutionHandlers.Register(new StepsHandler());
            ResolutionHandlers.Register(new SabaccHandler());
            ResolutionHandlers.Register(new SabaccStakeHandler());
            ResolutionHandlers.Register(new SabaccDuelHandler());
            ResolutionHandlers.Register(new WantedHandler());
            ResolutionHandlers.Register(new MarketSessionHandler());
            ResolutionHandlers.Register(new GainDatabankHandler());
            ResolutionHandlers.Register(new AssetCustomHandler());
            ResolutionHandlers.Register(new BountyCrewHandler());
            ResolutionHandlers.Register(new ChallengeHandler());
        }

        /// <summary>A frame that runs the effect list at <paramref name="path"/> of a card for its owner.</summary>
        public static Frame ListFrame(string owner, string card, string path)
        {
            return ListFrame(owner, card, path, null);
        }

        /// <summary>As above, carrying the context ("ctx.*") of <paramref name="parent"/> for conditions.</summary>
        public static Frame ListFrame(string owner, string card, string path, Frame? parent)
        {
            var frame = NewList(owner, card, path);
            if (parent != null) FrameContext.Copy(parent, frame);
            return frame;
        }

        private static Frame NewList(string owner, string card, string path)
        {
            var f = new Frame(List);
            f.Set("owner", owner);
            f.Set("card", card);
            f.Set("path", path);
            f.Set("i", 0);
            return f;
        }

        internal static JToken Token(ResolutionContext ctx, Frame frame, string pathVar = "path")
        {
            var token = ctx.Content.Card(frame.Get("card")).SelectToken(frame.Get(pathVar));
            return token ?? throw new InvalidOperationException("No data at " + frame.Get("card") + " " + frame.Get(pathVar) + ".");
        }

        internal static Frame Child(string handler, Frame parent, string path)
        {
            var f = new Frame(handler);
            f.Set("owner", parent.Get("owner"));
            f.Set("card", parent.Get("card"));
            f.Set("path", path);
            FrameContext.Copy(parent, f);
            return f;
        }

        /// <summary>What a list's effects produced that a later "to ... then" may refer to (Targeting context keys).</summary>
        internal static readonly string[] Outputs = { "spent", "cost", "last_faction", "moved_patrol" };

        /// <summary>"character", "ship" or "both" for what lost damage tokens, or null if nothing did.</summary>
        internal static string? Recovered(int character, int ship)
        {
            if (character > 0 && ship > 0) return "both";
            if (ship > 0) return "ship";
            if (character > 0) return "character";
            return null;
        }

        internal static void MarkFailedFromChild(Frame frame)
        {
            if (frame.Vars.TryGetValue("ret.failed", out var f) && f == "true")
            {
                frame.Set("failed", "true");
            }
            frame.Vars.Remove("ret.failed");
            foreach (var key in Outputs)
            {
                if (frame.Vars.TryGetValue("ret." + key, out var v))
                {
                    frame.Set("out." + key, v);
                    frame.Set("ctx." + key, v); // later effects of the same list see it ("that patrol")
                    frame.Vars.Remove("ret." + key);
                }
            }
        }

        internal static void ReturnFailed(ResolutionContext ctx, Frame frame)
        {
            if (frame.Vars.TryGetValue("failed", out var f) && f == "true")
            {
                ctx.Return("failed", "true");
            }
            foreach (var key in Outputs)
            {
                if (frame.Vars.TryGetValue("out." + key, out var v)) ctx.Return(key, v);
            }
        }
    }

    /// <summary>Uses one ability: pay the cost list, then, if it was fully paid, run the effects (R-21.4).</summary>
    internal sealed class AbilityHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.Ability;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var ability = (JObject)EffectHandlers.Token(ctx, frame);
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    if (ability["cost"] is JArray cost && cost.Count > 0)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".cost", frame));
                    }
                    return StepResult.Continue();
                case 1:
                    EffectHandlers.MarkFailedFromChild(frame);
                    frame.Step = 2;
                    if (frame.Vars.ContainsKey("failed"))
                    {
                        return StepResult.Continue();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".effects", frame));
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>Runs a list of effect nodes in order ("then", R-21.3; "and" as far as possible, R-21.5).</summary>
    internal sealed class ListHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.List;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            EffectHandlers.MarkFailedFromChild(frame);
            var list = (JArray)EffectHandlers.Token(ctx, frame);
            int i = frame.GetInt("i");
            if (i >= list.Count)
            {
                EffectHandlers.ReturnFailed(ctx, frame);
                return StepResult.Done();
            }
            frame.Set("i", i + 1);
            var node = (JObject)list[i];
            string nodePath = frame.Get("path") + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            string op = (string)node["op"]!;
            switch (op)
            {
                case "if":
                {
                    bool holds = Conditions.Eval(ctx.Content, ctx.State, owner, node["cond"], null, FrameContext.Of(frame));
                    string branch = holds ? "then" : "else";
                    if (node[branch] is JArray)
                    {
                        var child = EffectHandlers.ListFrame(owner.Id, frame.Get("card"), nodePath + "." + branch, frame);
                        FrameContext.Copy(frame, child);
                        return StepResult.Call(child);
                    }
                    return StepResult.Continue();
                }
                case "choice":
                    return StepResult.Call(EffectHandlers.Child(EffectHandlers.Choice, frame, nodePath));
                case "may":
                    return StepResult.Call(EffectHandlers.Child(EffectHandlers.May, frame, nodePath));
                case "to":
                    return StepResult.Call(EffectHandlers.Child(EffectHandlers.To, frame, nodePath));
                case "test":
                    return StepResult.Call(EffectHandlers.Child(EffectHandlers.Test, frame, nodePath));
                case "combat":
                    return StepResult.Call(EffectHandlers.Child(EffectHandlers.Fight, frame, nodePath));
                case "dice.reroll":
                case "dice.turn":
                case "dice.cancel":
                case "dice.add":
                    return StepResult.Call(EffectHandlers.Child(DiceOpHandler.HandlerId, frame, nodePath));
                case "dice.roll":
                    return StepResult.Call(EffectHandlers.Child(EffectDiceRollHandler.HandlerId, frame, nodePath));
                case "damage.recover":
                {
                    // Recover, then "when you recover damage" (YV-666 ship goal).
                    int ship = owner.ShipDamage, character = owner.CharacterDamage;
                    Instant.Apply(ctx, owner, frame.Get("card"), node);
                    string? recovered = EffectHandlers.Recovered(character - owner.CharacterDamage, ship - owner.ShipDamage);
                    if (recovered != null)
                    {
                        return StepResult.Call(TriggersHandler.For("damage_recovered", owner.Id,
                            new Dictionary<string, string> { ["recovered"] = recovered }));
                    }
                    return StepResult.Continue();
                }
                case "asset.discard":
                    return StepResult.Call(EffectHandlers.Child(AssetDiscardHandler.HandlerId, frame, nodePath));
                case "asset.take":
                    return StepResult.Call(EffectHandlers.Child(AssetTakeHandler.HandlerId, frame, nodePath));
                case "unless":
                    return StepResult.Call(EffectHandlers.Child(UnlessHandler.HandlerId, frame, nodePath));
                case "asset.gain_embedded":
                    return StepResult.Call(EffectHandlers.Child(GainEmbeddedHandler.HandlerId, frame, nodePath));
                case "player.choose":
                    return StepResult.Call(EffectHandlers.Child(PlayerChooseHandler.HandlerId, frame, nodePath));
                case "move.player":
                    return StepResult.Call(EffectHandlers.Child(MovePlayerEffectHandler.HandlerId, frame, nodePath));
                case "move.player_direct":
                case "move.patrol_direct":
                    return StepResult.Call(EffectHandlers.Child(MoveDirectHandler.HandlerId, frame, nodePath));
                case "move.patrol":
                    return StepResult.Call(EffectHandlers.Child(MovePatrolEffectHandler.HandlerId, frame, nodePath));
                case "encounter.patrol":
                    return StepResult.Call(EffectHandlers.Child(EncounterPatrolEffectHandler.HandlerId, frame, nodePath));
                case "market.buy":
                    return StepResult.Call(EffectHandlers.Child(MarketEffectBuyHandler.HandlerId, frame, nodePath));
                case "market.take":
                    return StepResult.Call(EffectHandlers.Child(MarketTakeHandler.HandlerId, frame, nodePath));
                case "encounter_deck.peek":
                    return StepResult.Call(EffectHandlers.Child(EncounterPeekHandler.HandlerId, frame, nodePath));
                case "contact.discard_token":
                case "contact.relocate_self":
                case "contact.place_facedown":
                case "contact.look":
                case "contact.move":
                case "encounter.contact":
                    return StepResult.Call(EffectHandlers.Child(ContactEffectHandler.HandlerId, frame, nodePath));
                case "bounty.deliver":
                    return StepResult.Call(EffectHandlers.Child(BountyDeliverEffectHandler.HandlerId, frame, nodePath));
                case "databank.resolve":
                    return StepResult.Call(EffectHandlers.Child(Jobs.ResolveHandler, frame, nodePath));
                case "market.extra_action":
                    return StepResult.Call(EffectHandlers.Child(MarketSessionHandler.HandlerId, frame, nodePath));
                case "asset.gain_databank":
                    return StepResult.Call(EffectHandlers.Child(GainDatabankHandler.HandlerId, frame, nodePath));
                case "custom" when AssetCustomHandler.Handlers.Contains((string)node["handler"]!):
                    return StepResult.Call(EffectHandlers.Child(AssetCustomHandler.HandlerId, frame, nodePath));
                case "sabacc.stake":
                    return StepResult.Call(EffectHandlers.Child(SabaccStakeHandler.HandlerId, frame, nodePath));
                case "sabacc.duel":
                    return StepResult.Call(EffectHandlers.Child(SabaccDuelHandler.HandlerId, frame, nodePath));
                case "custom" when (string)node["handler"]! == "sabacc":
                    return StepResult.Call(EffectHandlers.Child(Jobs.SabaccHandler, frame, nodePath));
                case "custom" when (string)node["handler"]! == "wanted":
                    return StepResult.Call(EffectHandlers.Child(WantedHandler.HandlerId, frame, nodePath));
                case "custom" when (string)node["handler"]! == "look_and_exchange":
                    return StepResult.Call(EffectHandlers.Child(LookAndExchangeHandler.HandlerId, frame, nodePath));
                case "custom" when CustomFrameHandler.Handlers.Contains((string)node["handler"]!):
                    return StepResult.Call(EffectHandlers.Child(CustomFrameHandler.HandlerId, frame, nodePath));
                case "encounter.space":
                {
                    // "Resolve another Takodana encounter card" (Takodana-5).
                    var another = new Frame(TurnHandlers.EncounterSpace);
                    another.Set("player", owner.Id);
                    another.Set("deck", (string)node["deck"]!);
                    return StepResult.Call(another);
                }
                case "cargo.deliver":
                {
                    // "Deliver this cargo": the cargo being delivered by the deliver action.
                    var deliver = DeliverHandler.Nearest(ctx.State);
                    if (deliver == null)
                    {
                        frame.Set("failed", "true");
                        return StepResult.Continue();
                    }
                    return StepResult.Call(DeliverCargoHandler.For(owner.Id, deliver.Get("current")));
                }
                case "damage.suffer":
                {
                    // Through the damage frame, so caps and "would be defeated" replacements apply.
                    var victim = Targeting.Player(ctx.State, owner, node["player"], FrameContext.Of(frame));
                    if (victim == null)
                    {
                        return StepResult.Call(EffectHandlers.Child(SelectHandler.HandlerId, frame, nodePath));
                    }
                    var amount = Selectors.Amount(ctx.Content, owner, node["amount"]!);
                    string target = (string)node["target"]!;
                    if (target == "both")
                    {
                        frame.Set("i", i); // run this node again for the ship half
                        if (!frame.Vars.ContainsKey("both_ship"))
                        {
                            frame.Set("both_ship", "1");
                            return StepResult.Call(SufferDamageHandler.For(victim.Id, CombatType.Ground, amount));
                        }
                        frame.Vars.Remove("both_ship");
                        frame.Set("i", i + 1);
                        return StepResult.Call(SufferDamageHandler.For(victim.Id, CombatType.Ship, amount));
                    }
                    return StepResult.Call(SufferDamageHandler.For(victim.Id, target == "ship" ? CombatType.Ship : CombatType.Ground, amount));
                }
                default:
                {
                    var context = FrameContext.Of(frame);
                    var resolved = Targeting.Simple(ctx.Content, ctx.State, owner, node, context);
                    if (resolved == null)
                    {
                        return StepResult.Call(EffectHandlers.Child(SelectHandler.HandlerId, frame, nodePath)); // a choice first
                    }
                    var outcome = new Instant.Outcome();
                    if (!Instant.Apply(ctx, owner, frame.Get("card"), node, resolved, outcome))
                    {
                        frame.Set("failed", "true");
                    }
                    outcome.Into(frame);
                    return StepResult.Continue();
                }
            }
        }
    }

    /// <summary>Effects that resolve at once. Returns false when the effect could not be fully done (a payment short of credits).</summary>
    internal static class Instant
    {
        /// <summary>Values an effect produced for later effects of the same card ("twice the amount you spent").</summary>
        internal sealed class Outcome
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

            public void Into(Frame list)
            {
                foreach (var kv in Values) list.Set("out." + kv.Key, kv.Value);
            }

            public void ReturnTo(ResolutionContext ctx)
            {
                foreach (var kv in Values) ctx.Return(kv.Key, kv.Value);
            }
        }

        public static bool Apply(ResolutionContext ctx, PlayerState owner, string card, JObject node)
        {
            if (node["player"] != null && node["player"]!.Type != JTokenType.String)
            {
                throw new InvalidOperationException("Effect '" + (string)node["op"]! + "' on a chosen player needs its selector settled first.");
            }
            return Apply(ctx, owner, card, node, new Resolved(owner, null, null, new Dictionary<string, string>()), new Outcome());
        }

        public static bool Apply(ResolutionContext ctx, PlayerState owner, string card, JObject node, Resolved r, Outcome outcome)
        {
            var content = ctx.Content;
            var state = ctx.State;
            var events = ctx.Events;
            string op = (string)node["op"]!;
            var target = r.Target;
            int Amount() => r.Amount ?? Targeting.Amount(content, owner, node["amount"]!, r.Context);
            switch (op)
            {
                case "credits.gain":
                    PlayerRules.GainCredits(target, Amount(), events);
                    return true;
                case "credits.spend":
                {
                    int n = Amount();
                    if (!PlayerRules.TrySpend(owner, n, events)) return false;
                    outcome.Values["spent"] = n.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
                case "credits.lose":
                    PlayerRules.LoseCredits(target, Amount(), events);
                    return true;
                case "credits.take":
                {
                    // "Take N from a player": they lose it (as much as they have, R-9.8) and you gain what they lost.
                    int lost = PlayerRules.LoseCredits(target, Amount(), events);
                    PlayerRules.GainCredits(owner, lost, events);
                    return lost > 0;
                }
                case "fame.gain":
                    PlayerRules.GainFame(target, Amount(), events);
                    return true;
                case "fame.lose":
                    return PlayerRules.LoseFame(target, Amount(), events) == Amount();
                case "rep.gain":
                case "rep.lose":
                {
                    var faction = r.Faction ?? Selectors.Faction(content, state, target, node["faction"]!);
                    if (faction == null)
                    {
                        return false; // no planet faction or no patrol here: nothing to gain or lose (R-5.5)
                    }
                    int n = node["amount"] != null ? Amount() : 1;
                    var before = target.Reputation.Get(faction.Value);
                    if (op == "rep.gain")
                    {
                        PlayerRules.GainReputation(target, faction.Value, n, events);
                    }
                    else
                    {
                        PlayerRules.LoseReputation(target, faction.Value, n, events);
                    }
                    outcome.Values["last_faction"] = Patrols.Id(faction.Value);
                    return target.Reputation.Get(faction.Value) != before; // R-21.4: "lose X to ..." needs a real loss
                }
                case "damage.prevent":
                {
                    // R-8.6, R-7.13: the win effect resolves before damage, so it lowers the damage still to come.
                    for (int i = state.Stack.Count - 1; i >= 0; i--)
                    {
                        var combat = state.Stack[i];
                        if (combat.Handler != Combat.Handler || combat.Get("attacker") != owner.Id) continue;
                        int d = combat.GetInt("defender_damage");
                        int prevent = node["amount"] is JObject all && all["all"] != null ? d : Amount();
                        combat.Set("defender_damage", System.Math.Max(0, d - prevent));
                        return true;
                    }
                    return false;
                }
                case "step.goto":
                case "step.repeat":
                case "job.complete":
                case "job.fail":
                case "sabacc.win":
                    return StepsHandler.Control(state, op, node); // R-15.36 to R-15.38
                case "job.discard":
                    return JobDiscard.Apply(ctx, owner, r.Context);
                case "test.pass":
                {
                    // Savvy Pilot: the skill test that would fail is passed instead.
                    var test = Jobs.Nearest(state, SkillTests.Handler);
                    test?.Set("forced_pass", "1");
                    return test != null;
                }
                case "custom" when (string)node["handler"]! == "reward_for_fame":
                {
                    // Sy Snootles, Mandalorian Armor (R-25.11): the reward's credits become 1 additional fame.
                    int credits = r.Context.TryGetValue("reward_credits", out var rc) ? int.Parse(rc, CultureInfo.InvariantCulture) : 0;
                    if (credits <= 0) return false;
                    PlayerRules.LoseCredits(owner, credits, events);
                    PlayerRules.GainFame(owner, 1, events);
                    return true;
                }
                case "turn.additional":
                    // R-5.14: a full turn after this one; none while already in an additional turn.
                    if (!state.InAdditionalTurn && !state.AdditionalTurn)
                    {
                        state.AdditionalTurn = true;
                        events.Add(new AdditionalTurnGainedEvent(owner.Id));
                    }
                    return true;
                case "damage.suffer":
                    foreach (var part in Targets((string)node["target"]!))
                    {
                        PlayerRules.SufferDamage(content, state, target, part, Amount(), events);
                    }
                    return true;
                case "damage.recover":
                    foreach (var part in Targets((string)node["target"]!))
                    {
                        // "Recover up to N": recovering is never worse, so the full N.
                        int amount = node["amount"] is JObject all && all["all"] != null ? int.MaxValue
                            : Targeting.UpTo(node["amount"]) ?? Amount();
                        PlayerRules.RecoverDamage(content, state, target, part, amount, events);
                    }
                    return true;
                case "card.discard_self":
                {
                    var asset = Assets.Find(owner, card);
                    if (asset == null)
                    {
                        return Encounters.DiscardSecret(content, state, owner, card, events); // a secret (R-15.86)
                    }
                    Assets.Discard(ctx.Content, ctx.State, owner, asset, "effect", events);
                    return true;
                }
                case "sheet.flip":
                    if ((string)node["sheet"]! == "character")
                    {
                        Goals.ClearTokens(owner, owner.CharacterId); // R-18.5
                        owner.CharacterSide = 1 - owner.CharacterSide;
                        events.Add(new SheetFlippedEvent(owner.Id, "character", owner.CharacterSide));
                    }
                    else
                    {
                        Goals.ClearTokens(owner, owner.ShipSheetId); // R-18.5
                        var other = content.OtherShipSide(owner.ShipSideId);
                        owner.ShipSideId = other.Id;
                        events.Add(new SheetFlippedEvent(owner.Id, "ship", 0));
                    }
                    return true;
                case "test.skill_grant":
                    // R-6.8: one more instance for the skill test about to be rolled.
                    for (int i = state.Stack.Count - 1; i >= 0; i--)
                    {
                        if (state.Stack[i].Handler == SkillTests.Handler)
                        {
                            var test = state.Stack[i];
                            test.Set("granted", (test.Vars.ContainsKey("granted") ? test.GetInt("granted") : 0) + 1);
                            return true;
                        }
                    }
                    return false;
                case "cargo.discard":
                {
                    var deliver = DeliverHandler.Nearest(state);
                    var asset = deliver == null ? null : Assets.Find(owner, deliver.Get("current"));
                    if (asset == null) return false;
                    Assets.Discard(ctx.Content, ctx.State, owner, asset, "effect", events);
                    return true;
                }
                case "turn.end":
                    // R-4.3: the turn ends once what is resolving finishes; no further deliveries (R-16.6).
                    state.TurnEndsAfterStep = true;
                    return true;
                case "step.end":
                {
                    var deliver = DeliverHandler.Nearest(state);
                    deliver?.Set("end_step", "1");
                    return deliver != null;
                }
                case "move.no_stop":
                {
                    // "You do not have to end your movement in that space" (Han Solo, Slave I).
                    var move = MoveHandler.Nearest(state);
                    move?.Set("no_stop", "1");
                    return move != null;
                }
                case "custom":
                    return Custom(ctx, owner, node);
                case "planet.marker":
                    // R-25.14: a marker for Jyn Erso's goal, one per planet.
                    foreach (var m in state.PlanetMarkers)
                    {
                        if (m.PlayerId == owner.Id && m.SpaceId == owner.SpaceId)
                        {
                            return true;
                        }
                    }
                    state.PlanetMarkers.Add(new PlanetMarker { PlayerId = owner.Id, SpaceId = owner.SpaceId });
                    events.Add(new PlanetMarkerPlacedEvent(owner.Id, owner.SpaceId));
                    return true;
                default:
                    throw new NotSupportedException("Effect '" + op + "' is not implemented yet (PLAN T2.3b, T2.3c, T2.5, T2.6).");
            }
        }

        /// <summary>Card-specific rules (EFFECTS.md §8) that need no decision once triggered.</summary>
        private static bool Custom(ResolutionContext ctx, PlayerState owner, JObject node)
        {
            string handler = (string)node["handler"]!;
            switch (handler)
            {
                case "tractor_beam":
                {
                    // Shadow Caster: "When another player moves into your space, you may force them to end their movement."
                    var move = MoveHandler.Nearest(ctx.State);
                    move?.Set("forced_stop", "1");
                    return move != null;
                }
                default:
                    throw new NotSupportedException("Custom handler '" + handler + "' is not implemented yet.");
            }
        }

        private static IEnumerable<CombatType> Targets(string target)
        {
            if (target != "ship")
            {
                yield return CombatType.Ground;
            }
            if (target != "character")
            {
                yield return CombatType.Ship;
            }
        }
    }

    /// <summary>"Either A or B": the owner picks one option list.</summary>
    internal sealed class ChoiceHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.Choice;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            switch (frame.Step)
            {
                case 0:
                {
                    var options = new List<DecisionOption>();
                    var lists = (JArray)node["options"]!;
                    for (int k = 0; k < lists.Count; k++)
                    {
                        var id = k.ToString(CultureInfo.InvariantCulture);
                        options.Add(new DecisionOption(id, frame.Get("card") + ":" + frame.Get("path") + ".options[" + id + "]"));
                    }
                    frame.Step = 1;
                    var chooser = Targeting.Player(ctx.State, ctx.State.FindPlayer(frame.Get("owner"))!, node["chooser"], FrameContext.Of(frame));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, (chooser ?? ctx.State.FindPlayer(frame.Get("owner"))!).Id,
                        "effects.choice", options, "pick"));
                }
                case 1:
                    frame.Step = 2;
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"),
                        frame.Get("path") + ".options[" + frame.Get("pick") + "]", frame));
                default:
                    EffectHandlers.MarkFailedFromChild(frame);
                    EffectHandlers.ReturnFailed(ctx, frame);
                    return StepResult.Done();
            }
        }
    }

    /// <summary>"May" (R-21.2): the owner chooses whether to resolve the effects.</summary>
    internal sealed class MayHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.May;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, frame.Get("owner"), "effects.may",
                        new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "answer"));
                case 1:
                    frame.Step = 2;
                    if (frame.Get("answer") == "yes")
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".effects", frame));
                    }
                    // "If you do not ..." (EFFECTS: may with else).
                    if (EffectHandlers.Token(ctx, frame)["else"] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".else", frame));
                    }
                    return StepResult.Continue();
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>"A to B" (R-21.4): B resolves only if A was completely resolved.</summary>
    internal sealed class ToHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.To;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".cost", frame));
                case 1:
                    EffectHandlers.MarkFailedFromChild(frame);
                    frame.Step = 2;
                    if (frame.Vars.ContainsKey("failed"))
                    {
                        EffectHandlers.ReturnFailed(ctx, frame);
                        return StepResult.Done();
                    }
                {
                    var then = EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".then", frame);
                    foreach (var key in EffectHandlers.Outputs)
                    {
                        if (frame.Vars.TryGetValue("out." + key, out var v)) then.Set("ctx." + key, v);
                    }
                    return StepResult.Call(then);
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>"Test X. If you pass ... If you fail ..." (R-6.9, R-6.11).</summary>
    internal sealed class TestHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.Test;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            switch (frame.Step)
            {
                case 0:
                {
                    var skills = new List<string>();
                    foreach (var s in (JArray)node["skill"]!) skills.Add((string)s!);
                    var test = new Frame(SkillTests.Handler);
                    var tester = Targeting.Player(ctx.State, ctx.State.FindPlayer(frame.Get("owner"))!, node["player"], FrameContext.Of(frame));
                    test.Set("player", (tester ?? ctx.State.FindPlayer(frame.Get("owner"))!).Id);
                    test.Set("skills", string.Join(",", skills));
                    frame.Step = 1;
                    return StepResult.Call(test);
                }
                case 1:
                {
                    var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
                    if (frame.Get("ret.passed") != "true" && node["buy_pass"] != null && !frame.Vars.ContainsKey("bought")
                        && owner.Credits >= (int)node["buy_pass"]!)
                    {
                        frame.Set("bought", "asked");
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.buy_pass",
                            new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "buy"));
                    }
                    if (frame.Vars.TryGetValue("buy", out var buy) && buy == "yes")
                    {
                        frame.Vars.Remove("buy");
                        PlayerRules.TrySpend(owner, (int)node["buy_pass"]!, ctx.Events);
                        frame.Set("ret.passed", "true");
                    }
                    string branch = frame.Get("ret.passed") == "true" ? "pass" : "fail";
                    frame.Step = 2;
                    if (node[branch] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + "." + branch, frame));
                    }
                    return StepResult.Continue();
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// "Fight a combat against X": against a card enemy with a printed value, or another player picked by
    /// the owner among those matching the selector (R-4.18: none, and nothing happens). The win and lose
    /// lists run before damage (R-7.13); "after" runs once the combat is over.
    /// </summary>
    internal sealed class FightHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.Fight;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var opponent = (JObject)node["opponent"]!;
            switch (frame.Step)
            {
                case 0:
                {
                    if (opponent["player"] is JObject named && named["who"]!.Type == JTokenType.String)
                    {
                        frame.Step = 1;
                        var fixedOpponent = Targeting.Player(ctx.State, owner, named["who"], FrameContext.Of(frame));
                        if (fixedOpponent == null || fixedOpponent.Id == owner.Id)
                        {
                            frame.Set("failed", "true");
                            EffectHandlers.ReturnFailed(ctx, frame);
                            return StepResult.Done();
                        }
                        frame.Set("defender", fixedOpponent.Id);
                        return StepResult.Continue();
                    }
                    if (opponent["player"] is JObject who)
                    {
                        var choose = (JObject)who["who"]!["choose"]!;
                        var options = new List<DecisionOption>();
                        foreach (var p in ctx.State.Players)
                        {
                            if (p.Id == owner.Id || p.Defeated) continue; // R-8.12, R-8.14
                            if (Conditions.Eval(ctx.Content, ctx.State, owner, choose["where"], p))
                            {
                                options.Add(new DecisionOption(p.Id, p.Id));
                            }
                        }
                        frame.Step = 1;
                        if (options.Count == 0)
                        {
                            frame.Set("failed", "true");
                            EffectHandlers.ReturnFailed(ctx, frame);
                            return StepResult.Done();
                        }
                        if (options.Count == 1)
                        {
                            frame.Set("defender", options[0].Id);
                            return StepResult.Continue();
                        }
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_player", options, "defender"));
                    }
                    frame.Step = 1;
                    return StepResult.Continue();
                }
                case 1:
                {
                    var combat = new Frame(Combat.Handler);
                    combat.Set("attacker", owner.Id);
                    string type = (string)node["type"]!;
                    if (opponent["player"] != null)
                    {
                        var defender = ctx.State.FindPlayer(frame.Get("defender"))!;
                        combat.Set("type", type == "by_space" ? (Combat.BetweenPlayers(ctx.Content, owner, defender) == CombatType.Ground ? "ground" : "ship") : type);
                        combat.Set("defender_kind", "player");
                        combat.Set("defender", defender.Id);
                        combat.Set("defender_value", "0");
                    }
                    else if (opponent["npc"] is JObject npc)
                    {
                        combat.Set("type", type);
                        combat.Set("defender_kind", "npc");
                        combat.Set("defender", (string)npc["name_key"]!);
                        combat.Set("defender_value", ((int)npc["value"]!).ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        throw new NotSupportedException("Combat opponent " + opponent.ToString(Newtonsoft.Json.Formatting.None) + " is not implemented yet.");
                    }
                    combat.Set("outcome", EffectHandlers.FightOutcome);
                    combat.Set("ctx.owner", owner.Id);
                    combat.Set("ctx.card", frame.Get("card"));
                    combat.Set("ctx.path", frame.Get("path"));
                    frame.Step = 2;
                    return StepResult.Call(combat);
                }
                case 2:
                    frame.Step = 3;
                    if (node["after"] is JArray)
                    {
                        // "If you win and you are not defeated, ..." (cargo check, jobs).
                        var after = EffectHandlers.ListFrame(owner.Id, frame.Get("card"), frame.Get("path") + ".after", frame);
                        after.Set("ctx.combat_type", frame.Get("ret.winner") == "none" ? "none" : (string)node["type"]!);
                        after.Set("ctx.combat_result", frame.Get("ret.winner") == "attacker" ? "won" : "lost");
                        return StepResult.Call(after);
                    }
                    return StepResult.Continue();
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>Runs the card's "win" or "lose" list for the attacker, before damage (R-7.13, R-8.15).</summary>
    internal sealed class FightOutcomeHandler : IResolutionHandler
    {
        public string Id => EffectHandlers.FightOutcome;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var card = frame.Get("ctx.card");
                var path = frame.Get("ctx.path");
                var node = (JObject)ctx.Content.Card(card).SelectToken(path)!;
                string branch = frame.Get("winner") == "attacker" ? "win" : "lose";
                if (node[branch] is JArray)
                {
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("ctx.owner"), card, path + "." + branch, frame));
                }
            }
            return StepResult.Done();
        }
    }
}
