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
            "test.skill_grant", "move.no_stop", "custom",
            // Passive rule changes read by the rules that check them.
            "slot.modify", "limit.trait", "rule.modify",
            // Replacement-only ops, applied by the damage frame.
            "damage.cap",
            // Passive-only ops, read as queries by Modifiers or by the rules that check them.
            "stat.modify", "dice.opponent_fewer",
        };

        /// <summary>Custom handlers (EFFECTS.md §8) this engine runs; patrol_redirect runs inside patrol movement.</summary>
        public static readonly HashSet<string> CustomImplemented = new HashSet<string>(StringComparer.Ordinal)
        {
            "tractor_beam", "patrol_redirect",
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
        }

        /// <summary>A frame that runs the effect list at <paramref name="path"/> of a card for its owner.</summary>
        public static Frame ListFrame(string owner, string card, string path)
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
            return f;
        }

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
        }

        internal static void ReturnFailed(ResolutionContext ctx, Frame frame)
        {
            if (frame.Vars.TryGetValue("failed", out var f) && f == "true")
            {
                ctx.Return("failed", "true");
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
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".cost"));
                    }
                    return StepResult.Continue();
                case 1:
                    EffectHandlers.MarkFailedFromChild(frame);
                    frame.Step = 2;
                    if (frame.Vars.ContainsKey("failed"))
                    {
                        return StepResult.Continue();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".effects"));
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
                    bool holds = Conditions.Eval(ctx.Content, ctx.State, owner, node["cond"]);
                    string branch = holds ? "then" : "else";
                    if (node[branch] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"), nodePath + "." + branch));
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
                case "damage.suffer":
                {
                    // Through the damage frame, so caps and "would be defeated" replacements apply.
                    var amount = Selectors.Amount(ctx.Content, owner, node["amount"]!);
                    string target = (string)node["target"]!;
                    if (target == "both")
                    {
                        frame.Set("i", i); // run this node again for the ship half
                        if (!frame.Vars.ContainsKey("both_ship"))
                        {
                            frame.Set("both_ship", "1");
                            return StepResult.Call(SufferDamageHandler.For(owner.Id, CombatType.Ground, amount));
                        }
                        frame.Vars.Remove("both_ship");
                        frame.Set("i", i + 1);
                        return StepResult.Call(SufferDamageHandler.For(owner.Id, CombatType.Ship, amount));
                    }
                    return StepResult.Call(SufferDamageHandler.For(owner.Id, target == "ship" ? CombatType.Ship : CombatType.Ground, amount));
                }
                default:
                    if (!Instant.Apply(ctx, owner, frame.Get("card"), node))
                    {
                        frame.Set("failed", "true");
                    }
                    return StepResult.Continue();
            }
        }
    }

    /// <summary>Effects that resolve at once. Returns false when the effect could not be fully done (a payment short of credits).</summary>
    internal static class Instant
    {
        public static bool Apply(ResolutionContext ctx, PlayerState owner, string card, JObject node)
        {
            var content = ctx.Content;
            var state = ctx.State;
            var events = ctx.Events;
            string op = (string)node["op"]!;
            if (node["player"] != null && node["player"]!.Type != JTokenType.String)
            {
                // Effects aimed at another player ("you both gain ...") are not built yet; never apply them to the owner.
                throw new NotSupportedException("Effect '" + op + "' on a chosen player is not implemented yet.");
            }
            switch (op)
            {
                case "credits.gain":
                    PlayerRules.GainCredits(owner, Selectors.Amount(content, owner, node["amount"]!), events);
                    return true;
                case "credits.spend":
                    return PlayerRules.TrySpend(owner, Selectors.Amount(content, owner, node["amount"]!), events);
                case "credits.lose":
                    PlayerRules.LoseCredits(owner, Selectors.Amount(content, owner, node["amount"]!), events);
                    return true;
                case "fame.gain":
                    PlayerRules.GainFame(owner, Selectors.Amount(content, owner, node["amount"]!), events);
                    return true;
                case "fame.lose":
                    return PlayerRules.LoseFame(owner, Selectors.Amount(content, owner, node["amount"]!), events)
                        == Selectors.Amount(content, owner, node["amount"]!);
                case "rep.gain":
                case "rep.lose":
                {
                    var faction = Selectors.Faction(content, state, owner, node["faction"]!);
                    if (faction == null)
                    {
                        return false; // no planet faction: nothing to gain or lose (R-5.5)
                    }
                    int n = node["amount"] != null ? Selectors.Amount(content, owner, node["amount"]!) : 1;
                    var before = owner.Reputation.Get(faction.Value);
                    if (op == "rep.gain")
                    {
                        PlayerRules.GainReputation(owner, faction.Value, n, events);
                    }
                    else
                    {
                        PlayerRules.LoseReputation(owner, faction.Value, n, events);
                    }
                    return owner.Reputation.Get(faction.Value) != before; // R-21.4: "lose X to ..." needs a real loss
                }
                case "damage.suffer":
                    foreach (var target in Targets((string)node["target"]!))
                    {
                        PlayerRules.SufferDamage(content, state, owner, target, Selectors.Amount(content, owner, node["amount"]!), events);
                    }
                    return true;
                case "damage.recover":
                    foreach (var target in Targets((string)node["target"]!))
                    {
                        int amount = node["amount"] is JObject all && all["all"] != null
                            ? int.MaxValue
                            : Selectors.Amount(content, owner, node["amount"]!);
                        PlayerRules.RecoverDamage(content, state, owner, target, amount, events);
                    }
                    return true;
                case "card.discard_self":
                {
                    var asset = Assets.Find(owner, card);
                    if (asset == null)
                    {
                        return false;
                    }
                    Assets.Discard(owner, asset, "effect", events);
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
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, frame.Get("owner"), "effects.choice", options, "pick"));
                }
                case 1:
                    frame.Step = 2;
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"),
                        frame.Get("path") + ".options[" + frame.Get("pick") + "]"));
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
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".effects"));
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
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".cost"));
                case 1:
                    EffectHandlers.MarkFailedFromChild(frame);
                    frame.Step = 2;
                    if (frame.Vars.ContainsKey("failed"))
                    {
                        EffectHandlers.ReturnFailed(ctx, frame);
                        return StepResult.Done();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + ".then"));
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
                    test.Set("player", frame.Get("owner"));
                    test.Set("skills", string.Join(",", skills));
                    frame.Step = 1;
                    return StepResult.Call(test);
                }
                case 1:
                {
                    string branch = frame.Get("ret.passed") == "true" ? "pass" : "fail";
                    frame.Step = 2;
                    if (node[branch] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"), frame.Get("path") + "." + branch));
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
                        return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"), frame.Get("path") + ".after"));
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
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("ctx.owner"), card, path + "." + branch));
                }
            }
            return StepResult.Done();
        }
    }
}
