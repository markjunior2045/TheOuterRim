#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>What an effect node acts on once its selectors are settled: the player, the faction, the amount.</summary>
    internal sealed class Resolved
    {
        public Resolved(PlayerState target, Faction? faction, int? amount, IReadOnlyDictionary<string, string> context)
        {
            Target = target;
            Faction = faction;
            Amount = amount;
            Context = context;
        }

        public PlayerState Target { get; }

        /// <summary>A faction chosen for the node, or null to read the node's own selector.</summary>
        public Faction? Faction { get; }

        /// <summary>An amount chosen for the node ("up to"), or null to read the node's own amount.</summary>
        public int? Amount { get; }

        public IReadOnlyDictionary<string, string> Context { get; }
    }

    /// <summary>
    /// Selectors of effect nodes (registry "types"): which player ("player" or "from"), which faction and how
    /// much. Selectors that need no decision are settled here; the others are asked by <see cref="SelectHandler"/>.
    /// Context keys: "participant" (set by player.choose), "opponent" (the other player of a combat), "spent"
    /// and "cost" (from the cost of a "to").
    /// </summary>
    internal static class Targeting
    {
        private static readonly Faction[] All = { Model.Faction.Rebel, Model.Faction.Imperial, Model.Faction.Hutt, Model.Faction.Syndicate };

        public static string PlayerKey(JObject node) => node["from"] != null ? "from" : "player";

        /// <summary>A player selector that needs no decision, or null when it does (an object such as "choose").</summary>
        public static PlayerState? Player(GameState state, PlayerState owner, JToken? selector, IReadOnlyDictionary<string, string> context)
        {
            if (selector == null || selector.Type == JTokenType.Null) return owner;
            if (selector.Type != JTokenType.String) return null;
            switch ((string)selector!)
            {
                case "self": return owner;
                case "current": return state.FindPlayer(state.CurrentPlayerId);
                case "participant":
                case "opponent":
                    return context.TryGetValue((string)selector!, out var id) ? state.FindPlayer(id)
                        : throw new InvalidOperationException("No " + (string)selector! + " in this effect's context.");
                default: throw new NotSupportedException("Player selector '" + (string)selector! + "' is not implemented yet.");
            }
        }

        /// <summary>The players a "choose" selector offers: others or anyone, filtered by "where" (candidate as subject).</summary>
        public static List<PlayerState> Candidates(GameContent content, GameState state, PlayerState owner, JObject choose)
        {
            var list = new List<PlayerState>();
            bool others = (string?)choose["who"] == "other";
            foreach (var p in state.Players)
            {
                if (others && p.Id == owner.Id) continue;
                if (Conditions.Eval(content, state, owner, choose["where"], p)) list.Add(p);
            }
            return list;
        }

        /// <summary>Faction choices for selectors that ask, or null when the selector needs no decision.</summary>
        public static List<Faction>? FactionOptions(GameState state, PlayerState target, JToken? selector, IReadOnlyDictionary<string, string> context)
        {
            if (selector == null) return null;
            if (selector is JObject o && o["choose"] is JObject choose)
            {
                var states = choose["from_rep"] is JArray a ? a.ToObject<List<string>>()! : new List<string> { "pos", "neu", "neg" };
                var list = new List<Faction>();
                foreach (var f in All)
                {
                    if (!states.Contains(Conditions.RepId(target.Reputation.Get(f)))) continue;
                    if ((bool?)choose["other"] == true && context.TryGetValue("last_faction", out var last) && Patrols.Id(f) == last) continue;
                    list.Add(f);
                }
                return list;
            }
            if (selector.Type != JTokenType.String) return null;
            switch ((string)selector!)
            {
                case "choice":
                case "other_player_choice":
                    return new List<Faction>(All);
                case "patrol_in_space":
                case "chosen_token":
                {
                    // R-12.13: contact tokens with a faction icon count once contacts are on the map (T2.6 part 3).
                    var list = new List<Faction>();
                    foreach (var p in state.PatrolsIn(target.SpaceId))
                    {
                        if (!list.Contains(p.Faction)) list.Add(p.Faction);
                    }
                    return (string)selector! == "patrol_in_space" && list.Count <= 1 ? null : list;
                }
                default:
                    return null;
            }
        }

        /// <summary>The player who picks a faction: the owner, or the other player for "of that player's choice".</summary>
        public static string FactionChooser(JToken? selector, PlayerState owner, IReadOnlyDictionary<string, string> context)
        {
            if (selector != null && selector.Type == JTokenType.String && (string)selector! == "other_player_choice")
            {
                if (context.TryGetValue("participant", out var p)) return p;
                if (context.TryGetValue("opponent", out var o)) return o;
            }
            return owner.Id;
        }

        /// <summary>Whether the node's amount is "up to N" (asked) and the largest value offered.</summary>
        public static int? UpTo(JToken? amount)
        {
            return amount is JObject o && o["up_to"] is JObject u ? (int)u["max"]! : (int?)null;
        }

        /// <summary>An amount that needs no decision: integers, counters, and multiples of what was spent or discarded.</summary>
        public static int Amount(GameContent content, PlayerState you, JToken amount, IReadOnlyDictionary<string, string> context)
        {
            if (amount is JObject o && o["times"] is JObject times)
            {
                string of = (string)times["of"]!;
                int baseValue = context.TryGetValue(of, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : 0;
                return (int)times["factor"]! * baseValue;
            }
            return Selectors.Amount(content, you, amount);
        }

        /// <summary>Settles a node's selectors without asking, or returns null when one needs a decision.</summary>
        public static Resolved? Simple(GameContent content, GameState state, PlayerState owner, JObject node, IReadOnlyDictionary<string, string> context)
        {
            var target = Player(state, owner, node[PlayerKey(node)], context);
            if (target == null) return null;
            if (FactionOptions(state, (string?)node["op"] == "credits.take" ? owner : target, node["faction"], context) != null) return null;
            if (UpTo(node["amount"]) != null) return null;
            return new Resolved(target, null, null, context);
        }
    }

    /// <summary>
    /// Asks what an effect node's selectors leave open, in order: the player ("choose"), the faction ("of your
    /// choice", "a faction you have {REP_POS} with", a patrol in your space when there are several) and the
    /// amount ("up to"). Then applies the node. Vars: "owner", "card", "path" (the node).
    /// </summary>
    internal sealed class SelectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.select";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var context = FrameContext.Of(frame);
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var key = Targeting.PlayerKey(node);
                    if (Targeting.Player(ctx.State, owner, node[key], context) != null) return StepResult.Continue();
                    var options = new List<DecisionOption>();
                    foreach (var p in Targeting.Candidates(ctx.Content, ctx.State, owner, (JObject)node[key]!["choose"]!))
                    {
                        options.Add(new DecisionOption(p.Id, p.Id));
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true"); // nobody to choose (R-4.18)
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    if (options.Count == 1)
                    {
                        frame.Set("sel_player", options[0].Id);
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_player", options, "sel_player"));
                }
                case 1:
                {
                    frame.Step = 2;
                    var target = TargetOf(ctx, frame, node, owner, context);
                    var options = Targeting.FactionOptions(ctx.State, (string?)node["op"] == "credits.take" ? owner : target, node["faction"], context);
                    if (options == null) return StepResult.Continue();
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        return StepResult.Done();
                    }
                    if (options.Count == 1)
                    {
                        frame.Set("sel_faction", Patrols.Id(options[0]));
                        return StepResult.Continue();
                    }
                    var choices = new List<DecisionOption>();
                    foreach (var f in options) choices.Add(new DecisionOption(Patrols.Id(f), "faction." + Patrols.Id(f)));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, Targeting.FactionChooser(node["faction"], owner, context),
                        "effects.choose_faction", choices, "sel_faction"));
                }
                case 2:
                {
                    frame.Step = 3;
                    int? max = Targeting.UpTo(node["amount"]);
                    if (max == null) return StepResult.Continue();
                    int step = max.Value >= 1000 ? 1000 : 1;
                    var choices = new List<DecisionOption>();
                    for (int v = 0; v <= max.Value; v += step)
                    {
                        // R-9.8: an amount to spend must be affordable.
                        if ((string?)node["op"] == "credits.spend" && v > owner.Credits) break;
                        var id = v.ToString(CultureInfo.InvariantCulture);
                        choices.Add(new DecisionOption(id, id));
                    }
                    if (choices.Count == 1)
                    {
                        frame.Set("sel_amount", "0");
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_amount", choices, "sel_amount"));
                }
                case 3:
                {
                    frame.Step = 4;
                    var target = TargetOf(ctx, frame, node, owner, context);
                    Faction? faction = frame.Vars.ContainsKey("sel_faction") ? Patrols.Parse(frame.Get("sel_faction")) : (Faction?)null;
                    int? amount = frame.Vars.ContainsKey("sel_amount") ? frame.GetInt("sel_amount") : (int?)null;
                    var resolved = new Resolved(target, faction, amount, context);
                    var effects = new Instant.Outcome();
                    if (!Instant.Apply(ctx, owner, frame.Get("card"), node, resolved, effects))
                    {
                        ctx.Return("failed", "true");
                    }
                    effects.ReturnTo(ctx);
                    return StepResult.Done();
                }
                default:
                    return StepResult.Done();
            }
        }

        private static PlayerState TargetOf(ResolutionContext ctx, Frame frame, JObject node, PlayerState owner, IReadOnlyDictionary<string, string> context)
        {
            return frame.Vars.ContainsKey("sel_player")
                ? ctx.State.FindPlayer(frame.Get("sel_player"))!
                : Targeting.Player(ctx.State, owner, node[Targeting.PlayerKey(node)], context)!;
        }
    }

    /// <summary>
    /// "Choose a player ... That player ..." (player.choose): the owner picks one of the candidates, then the
    /// effects run with that player as "participant". With no candidate the effect fails (R-4.18).
    /// </summary>
    internal sealed class PlayerChooseHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.player_choose";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var options = new List<DecisionOption>();
                    foreach (var p in Targeting.Candidates(ctx.Content, ctx.State, owner, node))
                    {
                        options.Add(new DecisionOption(p.Id, p.Id));
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    if (options.Count == 1)
                    {
                        frame.Set("pick", options[0].Id);
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_player", options, "pick"));
                }
                case 1:
                {
                    frame.Step = 2;
                    var list = EffectHandlers.ListFrame(owner.Id, frame.Get("card"), frame.Get("path") + ".effects", frame);
                    list.Set("ctx.participant", frame.Get("pick"));
                    return StepResult.Call(list);
                }
                default:
                    EffectHandlers.MarkFailedFromChild(frame);
                    EffectHandlers.ReturnFailed(ctx, frame);
                    return StepResult.Done();
            }
        }
    }
}
