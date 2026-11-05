#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Map;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>Patrol setup, spawning, movement and elimination (R-3.5, R-3.7, R-11).</summary>
    public static class Patrols
    {
        /// <summary>Resolution handler: move a patrol toward the current player. Vars: "faction", "distance".</summary>
        public const string MoveHandler = "rules.patrol_move";

        /// <summary>Resolution handler: encounter (fight) a patrol. Vars: "target" (faction).</summary>
        public const string EncounterHandler = "encounter.patrol";

        public static string Id(Faction faction)
        {
            return faction.ToString().ToLowerInvariant();
        }

        public static Faction Parse(string id)
        {
            switch (id)
            {
                case "rebel": return Faction.Rebel;
                case "imperial": return Faction.Imperial;
                case "hutt": return Faction.Hutt;
                case "syndicate": return Faction.Syndicate;
                default: throw new FormatException("Unknown faction '" + id + "'.");
            }
        }

        public static PatrolState Of(GameState state, Faction faction)
        {
            foreach (var p in state.Patrols)
            {
                if (p.Faction == faction)
                {
                    return p;
                }
            }
            throw new InvalidOperationException("R-11.9: no " + faction + " patrol on the map.");
        }

        /// <summary>R-3.5, R-3.7: level-1 patrols on their spawn spaces; stacks of levels 2, 3, 4 (top first).</summary>
        public static void Setup(GameContent content, GameState state)
        {
            state.Patrols.Clear();
            state.PatrolStacks.Clear();
            foreach (Faction f in new[] { Faction.Rebel, Faction.Syndicate, Faction.Imperial, Faction.Hutt })
            {
                state.Patrols.Add(new PatrolState { Faction = f, Level = 1, SpaceId = content.Map.SpawnSpace(f) });
                state.PatrolStacks.Add(new PatrolStack { Faction = f, Levels = new List<int> { 2, 3, 4 } });
            }
        }

        /// <summary>
        /// R-11.8, R-11.10: the defeated patrol is removed from the game and the top token of its faction's
        /// stack spawns on the space next to the stack. A level-4 patrol is never eliminated (R-11.6).
        /// </summary>
        public static void EliminateAndSpawn(GameContent content, GameState state, Faction faction, List<GameEvent> events)
        {
            var patrol = Of(state, faction);
            if (patrol.Level >= 4)
            {
                throw new InvalidOperationException("R-11.6: a level-4 patrol cannot be eliminated.");
            }
            PatrolStack? stack = null;
            foreach (var s in state.PatrolStacks)
            {
                if (s.Faction == faction)
                {
                    stack = s;
                }
            }
            events.Add(new PatrolEliminatedEvent(faction, patrol.Level, patrol.SpaceId));
            patrol.Level = stack!.Levels[0];
            stack.Levels.RemoveAt(0);
            patrol.SpaceId = content.Map.SpawnSpace(faction);
            events.Add(new PatrolSpawnedEvent(faction, patrol.Level, patrol.SpaceId));
        }

        /// <summary>R-11.23: moving a patrol "directly" places it on the space with no intervening spaces.</summary>
        public static void MoveDirect(GameState state, Faction faction, string spaceId, List<GameEvent> events)
        {
            var patrol = Of(state, faction);
            var from = patrol.SpaceId;
            patrol.SpaceId = spaceId;
            events.Add(new PatrolMovedEvent(faction, from, spaceId, true));
        }

        /// <summary>
        /// Where a patrol may end when it moves <paramref name="distance"/> spaces toward
        /// <paramref name="target"/> (R-11.15 to R-11.22). Distances never count through the Core Worlds
        /// (R-11.22).
        /// <list type="bullet">
        /// <item>If the target's space is within reach, the patrol must go there and stop (R-11.17, R-11.20).</item>
        /// <item>Otherwise it moves exactly that many spaces; each step goes to a space no farther from the
        /// target than the one it leaves, never revisiting a space in the same move. Every such route
        /// heads toward the target and can turn back at an intersection when that brings it closer
        /// (R-11.18, R-11.19). The current player picks among the possible end spaces (R-11.16); the
        /// route itself does not matter because patrols pass through everything (R-11.21).</item>
        /// </list>
        /// Returned in ordinal order.
        /// </summary>
        public static List<string> Destinations(MapGraph map, string from, string target, int distance)
        {
            var toTarget = map.DistancesFrom(target, throughCoreWorlds: false);
            if (!toTarget.TryGetValue(from, out var start))
            {
                return new List<string> { from };
            }
            if (start <= distance)
            {
                return new List<string> { target };
            }
            var ends = new SortedSet<string>(StringComparer.Ordinal);
            var path = new HashSet<string>(StringComparer.Ordinal) { from };
            Walk(map, toTarget, from, distance, path, ends);
            return new List<string>(ends);
        }

        private static void Walk(MapGraph map, Dictionary<string, int> toTarget, string at, int left,
            HashSet<string> path, SortedSet<string> ends)
        {
            if (left == 0)
            {
                ends.Add(at);
                return;
            }
            foreach (var next in map.Neighbours(at))
            {
                if (path.Contains(next) || !toTarget.TryGetValue(next, out var d) || d > toTarget[at])
                {
                    continue;
                }
                if (map[next].Type == SpaceType.CoreWorlds && left > 1)
                {
                    continue; // R-11.22: may stop on a Core Worlds space but not pass through it
                }
                path.Add(next);
                Walk(map, toTarget, next, left - 1, path, ends);
                path.Remove(next);
            }
        }
    }
}
