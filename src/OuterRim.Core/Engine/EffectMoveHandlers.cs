#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Spaces offered by space selectors (registry "space_sel").</summary>
    internal static class SpaceSelect
    {
        /// <summary>The spaces a selector allows for <paramref name="you"/>; several mean the owner chooses.</summary>
        public static List<string> Options(GameContent content, GameState state, PlayerState you, JToken selector)
        {
            var map = content.Map;
            var result = new List<string>();
            if (selector.Type == JTokenType.String)
            {
                switch ((string)selector!)
                {
                    case "your_space":
                        result.Add(you.SpaceId);
                        return result;
                    case "any_space":
                        foreach (var s in map.Spaces) result.Add(s.Id);
                        result.Sort(System.StringComparer.Ordinal);
                        return result;
                    case "adjacent":
                        result.AddRange(map.Neighbours(you.SpaceId));
                        result.Sort(System.StringComparer.Ordinal);
                        return result;
                    case "nearest_planet":
                    {
                        var planets = new List<string>();
                        foreach (var s in map.Spaces)
                        {
                            if (s.IsPlanet) planets.Add(s.Id);
                        }
                        return map.Nearest(you.SpaceId, planets); // R-5.30: a tie is the current player's choice
                    }
                    case "maelstrom":
                        foreach (var s in map.Spaces)
                        {
                            if (s.Type == SpaceType.Maelstrom) result.Add(s.Id);
                        }
                        return result;
                }
            }
            else if (selector is JObject o)
            {
                if (o["space"] is JObject sp)
                {
                    result.Add((string)sp["id"]!);
                    return result;
                }
                if (o["patrol_space"] is JObject ps)
                {
                    result.Add(Patrols.Of(state, Patrols.Parse((string)ps["faction"]!)).SpaceId);
                    return result;
                }
                if (o["planet_where"] is JObject pw)
                {
                    foreach (var space in map.Spaces)
                    {
                        if (space.IsPlanet && PlanetHolds(content, state, you, space.Id, (JObject)pw["cond"]!)) result.Add(space.Id);
                    }
                    result.Sort(System.StringComparer.Ordinal);
                    return result;
                }
            }
            throw new System.NotSupportedException("Space selector " + selector.ToString(Newtonsoft.Json.Formatting.None) + " is not implemented yet.");
        }

        /// <summary>Conditions about a planet: "within 3 spaces of the Hutt patrol", "a planet of a faction you are {REP_POS} with".</summary>
        internal static bool PlanetHolds(GameContent content, GameState state, PlayerState owner, string planet, JObject cond)
        {
            switch ((string)cond["cond"]!)
            {
                case "within_patrol":
                {
                    var patrol = Patrols.Of(state, Patrols.Parse((string)cond["faction"]!));
                    int d = content.Map.Distance(patrol.SpaceId, planet);
                    return d >= 0 && d <= (int)cond["distance"]!;
                }
                case "rep" when (string?)cond["faction"] == "planet":
                {
                    var f = content.Map[planet].Faction;
                    if (f == null) return false;
                    var states = ((JArray)cond["in"]!).ToObject<List<string>>()!;
                    return states.Contains(Conditions.RepId(owner.Reputation.Get(f.Value)));
                }
                default:
                    throw new System.NotSupportedException("Planet condition '" + (string)cond["cond"]! + "' is not implemented yet.");
            }
        }

        /// <summary>The patrols a patrol selector allows ("any", "in_your_space", a faction, "within N spaces of you").</summary>
        public static List<Faction> Patrols_(GameContent content, GameState state, PlayerState you, JToken selector)
        {
            var list = new List<Faction>();
            if (selector.Type == JTokenType.String)
            {
                var s = (string)selector!;
                if (s == "any")
                {
                    foreach (var p in state.Patrols) list.Add(p.Faction);
                }
                else if (s == "in_your_space" || s == "chosen_in_your_space")
                {
                    foreach (var p in state.PatrolsIn(you.SpaceId)) list.Add(p.Faction);
                }
                else
                {
                    list.Add(Patrols.Parse(s));
                }
                return list;
            }
            if (selector is JObject o && o["each_with_rep"] is JObject each)
            {
                foreach (var p in state.Patrols)
                {
                    if (Conditions.RepId(you.Reputation.Get(p.Faction)) == (string)each["state"]!) list.Add(p.Faction);
                }
                return list;
            }
            if (selector is JObject w && w["within"] is JObject within)
            {
                // Ketsu Onyo: "a patrol ... that is within 3 spaces of you" (R-5.29: the shortest distance along paths).
                var near = new HashSet<string>(content.Map.Within(you.SpaceId, (int)within["distance"]!));
                foreach (var p in state.Patrols)
                {
                    if (near.Contains(p.SpaceId)) list.Add(p.Faction);
                }
                return list;
            }
            throw new System.NotSupportedException("Patrol selector " + selector.ToString(Newtonsoft.Json.Formatting.None) + " is not implemented yet.");
        }

        /// <summary>True when a patrol selector reaches beyond the player's space ("within N spaces of you").</summary>
        public static bool ReachesAway(JToken selector)
        {
            return selector is JObject o && o["within"] != null;
        }

        public static StepResult AskOrSet(ResolutionContext ctx, Frame frame, string playerId, string prompt, List<string> options, string var)
        {
            if (options.Count == 1)
            {
                frame.Set(var, options[0]);
                return StepResult.Continue();
            }
            var choices = new List<DecisionOption>();
            foreach (var o in options) choices.Add(new DecisionOption(o, o));
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, playerId, prompt, choices, var));
        }
    }

    /// <summary>
    /// "Move N spaces" / "Move up to N spaces" outside the planning step: one space at a time, ending on
    /// entering a patrol's space unless positive with it (R-5.20), never through the Maelstrom (R-5.12).
    /// The owner picks the end space; the path is a shortest one under those limits.
    /// </summary>
    internal sealed class MovePlayerEffectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.move_player";

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
                    int? upTo = Targeting.UpTo(node["distance"]);
                    int d = upTo ?? Selectors.Amount(ctx.Content, owner, node["distance"]!);
                    var paths = Reachable(ctx.Content, ctx.State, owner, d, exact: upTo == null);
                    var ends = new List<string>(paths.Keys);
                    ends.Sort(System.StringComparer.Ordinal);
                    if (upTo != null) ends.Insert(0, owner.SpaceId); // zero spaces
                    frame.Set("paths", Encode(paths));
                    if (ends.Count == 0) return StepResult.Continue();
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.move_to", ends, "to");
                }
                case 1:
                {
                    frame.Step = 2;
                    var paths = Decode(frame.Get("paths"));
                    if (!frame.Vars.ContainsKey("to") || !paths.TryGetValue(frame.Get("to"), out var path)) return StepResult.Continue();
                    var move = new Frame(MoveHandler.HandlerId);
                    move.Set("player", owner.Id);
                    move.Set("path", path);
                    move.Set("from", owner.SpaceId);
                    move.Set("effect", "1");
                    return StepResult.Call(move);
                }
                default:
                    return StepResult.Done();
            }
        }

        /// <summary>End space → path (ids joined by ',') for every space reachable in 1..d moves (or exactly d).</summary>
        internal static Dictionary<string, string> Reachable(GameContent content, GameState state, PlayerState player, int d, bool exact)
        {
            var map = content.Map;
            var paths = new Dictionary<string, string>(System.StringComparer.Ordinal);
            var frontier = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(player.SpaceId, "") };
            var seen = new HashSet<string> { player.SpaceId };
            for (int step = 1; step <= d; step++)
            {
                var next = new List<KeyValuePair<string, string>>();
                foreach (var kv in frontier)
                {
                    foreach (var n in map.Neighbours(kv.Key))
                    {
                        if (seen.Contains(n)) continue;
                        seen.Add(n);
                        var path = kv.Value.Length == 0 ? n : kv.Value + "," + n;
                        if (!exact || step == d) paths[n] = path;
                        bool stops = map[n].Type == SpaceType.Maelstrom || GameEngine.StopsMovement(state, player, n);
                        if (!stops) next.Add(new KeyValuePair<string, string>(n, path));
                        else if (exact && step < d) paths[n] = path; // movement ends there early
                    }
                }
                frontier = next;
            }
            return paths;
        }

        private static string Encode(Dictionary<string, string> paths)
        {
            var parts = new List<string>();
            foreach (var kv in paths) parts.Add(kv.Key + "=" + kv.Value);
            parts.Sort(System.StringComparer.Ordinal);
            return string.Join(";", parts);
        }

        private static Dictionary<string, string> Decode(string s)
        {
            var d = new Dictionary<string, string>(System.StringComparer.Ordinal);
            if (s.Length == 0) return d;
            foreach (var part in s.Split(';'))
            {
                int i = part.IndexOf('=');
                d[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return d;
        }
    }

    /// <summary>R-5.23: "move directly" — the player (or a patrol) is placed in the space, passing through nothing.</summary>
    internal sealed class MoveDirectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.move_direct";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            bool patrol = (string)node["op"]! == "move.patrol_direct";
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    if (!patrol) return StepResult.Continue();
                    var options = new List<string>();
                    foreach (var f in SpaceSelect.Patrols_(ctx.Content, ctx.State, owner, node["patrol"]!)) options.Add(Patrols.Id(f));
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_patrol", options, "patrol");
                }
                case 1:
                {
                    frame.Step = 2;
                    var options = SpaceSelect.Options(ctx.Content, ctx.State, owner, node["to"]!);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.move_to", options, "to");
                }
                case 2:
                {
                    frame.Step = 3;
                    var to = frame.Get("to");
                    if (patrol)
                    {
                        var faction = Patrols.Parse(frame.Get("patrol"));
                        var p = Patrols.Of(ctx.State, faction);
                        var from = p.SpaceId;
                        p.SpaceId = to;
                        ctx.Events.Add(new PatrolMovedEvent(faction, from, to, true));
                        // Remembered for "...and encounter it" / "that patrol" later in the same effect.
                        ctx.Return("moved_patrol", frame.Get("patrol"));
                    }
                    else
                    {
                        var from = owner.SpaceId;
                        owner.SpaceId = to;
                        ctx.Events.Add(new PlayerMovedEvent(owner.Id, from, new List<string> { to }, true));
                    }
                    return StepResult.Done();
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// "Move the X patrol N spaces toward you" / "up to N spaces in any direction" (move.patrol), once per
    /// selected patrol ("for each faction you have {REP_NEG} reputation with").
    /// </summary>
    internal sealed class MovePatrolEffectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.move_patrol";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            if (frame.Step == 0)
            {
                var list = new List<string>();
                foreach (var f in SpaceSelect.Patrols_(ctx.Content, ctx.State, owner, node["patrol"]!)) list.Add(Patrols.Id(f));
                frame.Set("queue", string.Join(",", list));
                frame.Step = 1;
            }
            var queue = frame.Get("queue").Length == 0 ? new List<string>() : new List<string>(frame.Get("queue").Split(','));
            if (queue.Count == 0) return StepResult.Done();
            frame.Set("queue", string.Join(",", queue.GetRange(1, queue.Count - 1)));
            int? upTo = Targeting.UpTo(node["distance"]);
            var move = new Frame(Patrols.MoveHandler);
            move.Set("faction", queue[0]);
            move.Set("distance", upTo ?? Selectors.Amount(ctx.Content, owner, node["distance"]!));
            if (upTo != null) move.Set("up_to", "1");
            move.Set("toward_player", owner.Id);
            if ((string)node["toward"]! == "any_direction") move.Set("any_direction", "1");
            return StepResult.Call(move);
        }
    }

    /// <summary>"Encounter it" / "encounter a patrol in your space" (encounter.patrol): a patrol combat for the owner.</summary>
    internal sealed class EncounterPatrolEffectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.encounter_patrol";

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
                    var options = new List<string>();
                    bool away = SpaceSelect.ReachesAway(node["patrol"]!);
                    foreach (var f in SpaceSelect.Patrols_(ctx.Content, ctx.State, owner, node["patrol"]!))
                    {
                        if (away || Patrols.Of(ctx.State, f).SpaceId == owner.SpaceId) options.Add(Patrols.Id(f));
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_patrol", options, "target");
                }
                case 1:
                {
                    frame.Step = 2;
                    var encounter = new Frame(Patrols.EncounterHandler);
                    encounter.Set("target", frame.Get("target"));
                    encounter.Set("player", owner.Id);
                    if (node["bonus"] is JArray bonus)
                    {
                        foreach (JObject b in bonus)
                        {
                            if ((string?)b["op"] == "dice.add") encounter.Set("attacker_add", (string)b["face"]! + ":" + (int)b["count"]!);
                        }
                    }
                    return StepResult.Call(encounter);
                }
                default:
                    return StepResult.Done();
            }
        }
    }
}
