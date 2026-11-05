#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Model;

namespace OuterRim.Core.Map
{
    /// <summary>
    /// Reads map files (docs/MAP_FORMAT.md, format outer-rim-map/0.1) and assembles a layout into a
    /// <see cref="MapGraph"/>. The core takes the JSON text; reading files is the caller's job, so the
    /// same code runs in tests and in Unity.
    /// </summary>
    public sealed class MapLoader
    {
        public const string Format = "outer-rim-map/0.1";

        /// <summary>Port pairs joined when two pieces touch: right side of the left piece to left side of the right one.</summary>
        private static readonly string[][] PortPairs = { new[] { "R1", "L1" }, new[] { "R2", "L2" } };

        private readonly Dictionary<string, JObject> _pieces = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        /// <summary>Adds one map file (one per pack).</summary>
        public MapLoader Add(string json)
        {
            var root = JObject.Parse(json);
            var format = (string?)root["format"];
            if (format != Format)
            {
                throw new FormatException("Unsupported map format '" + format + "', expected '" + Format + "'.");
            }
            foreach (JObject piece in (JArray)root["pieces"]!)
            {
                var id = (string)piece["id"]!;
                if (_pieces.ContainsKey(id))
                {
                    throw new FormatException("Duplicate map piece '" + id + "'.");
                }
                _pieces[id] = piece;
            }
            foreach (JObject layout in (JArray?)root["layouts"] ?? new JArray())
            {
                var list = new List<string>();
                foreach (var p in (JArray)layout["pieces"]!)
                {
                    list.Add((string)p!);
                }
                _layouts[(string)layout["id"]!] = list;
            }
            return this;
        }

        /// <summary>Piece order of a named layout ("standard" R-3.2, "core-worlds" R-3.4).</summary>
        public IReadOnlyList<string> Layout(string id)
        {
            return _layouts.TryGetValue(id, out var l) ? l : throw new KeyNotFoundException("Unknown layout '" + id + "'.");
        }

        public MapGraph Build(string layoutId)
        {
            return Build(Layout(layoutId));
        }

        /// <summary>Assembles pieces placed left to right (any tile order is valid, R-3.3).</summary>
        public MapGraph Build(IReadOnlyList<string> order)
        {
            var spaces = new Dictionary<string, Space>(StringComparer.Ordinal);
            var adjacent = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var stacks = new Dictionary<string, List<Faction>>(StringComparer.Ordinal);
            var stackEdges = new List<KeyValuePair<string, string>>();
            var contactSpaces = new List<ContactSpace>();
            var portNode = new Dictionary<string, string>(StringComparer.Ordinal);

            void Link(string a, string b)
            {
                if (a == b)
                {
                    throw new FormatException("Path from a space to itself: " + a);
                }
                if (stacks.ContainsKey(a) || stacks.ContainsKey(b))
                {
                    stackEdges.Add(new KeyValuePair<string, string>(a, b));
                    return;
                }
                Adj(adjacent, a).Add(b);
                Adj(adjacent, b).Add(a);
            }

            foreach (var pid in order)
            {
                if (!_pieces.TryGetValue(pid, out var piece))
                {
                    throw new KeyNotFoundException("Unknown map piece '" + pid + "'.");
                }
                foreach (JObject s in (JArray)piece["spaces"]!)
                {
                    var id = (string)s["id"]!;
                    if (spaces.ContainsKey(id))
                    {
                        throw new FormatException("Duplicate space '" + id + "'.");
                    }
                    spaces[id] = new Space(id, ParseType((string)s["type"]!), (string?)s["name"],
                        ParseFaction((string?)s["faction"]), pid);
                    Adj(adjacent, id);
                }
                foreach (JObject st in (JArray?)piece["patrol_stacks"] ?? new JArray())
                {
                    var factions = new List<Faction>();
                    foreach (var f in (JArray)st["factions"]!)
                    {
                        factions.Add(ParseFaction((string)f!)!.Value);
                    }
                    stacks[(string)st["id"]!] = factions;
                }
                var perPlanet = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (JObject c in (JArray?)piece["contact_spaces"] ?? new JArray())
                {
                    var planet = (string)c["planet"]!;
                    perPlanet.TryGetValue(planet, out var n);
                    perPlanet[planet] = n + 1;
                    contactSpaces.Add(new ContactSpace(planet + "-contact-" + (n + 1), planet, (string)c["class"]!["color"]!));
                }
                var ports = (JObject?)piece["ports"] ?? new JObject();
                foreach (JArray e in (JArray)piece["edges"]!)
                {
                    string a = (string)e[0]!, b = (string)e[1]!;
                    bool aPort = ports.ContainsKey(a), bPort = ports.ContainsKey(b);
                    if (aPort && bPort)
                    {
                        throw new FormatException("Path between two ports on " + pid);
                    }
                    if (aPort || bPort)
                    {
                        portNode[pid + "/" + (aPort ? a : b)] = aPort ? b : a;
                    }
                    else
                    {
                        Link(a, b);
                    }
                }
            }
            for (int i = 0; i + 1 < order.Count; i++)
            {
                foreach (var pair in PortPairs)
                {
                    if (portNode.TryGetValue(order[i] + "/" + pair[0], out var a)
                        && portNode.TryGetValue(order[i + 1] + "/" + pair[1], out var b))
                    {
                        Link(a, b);
                    }
                }
            }

            var spawn = new Dictionary<Faction, string>();
            foreach (var kv in stacks)
            {
                var targets = new List<string>();
                foreach (var e in stackEdges)
                {
                    if (e.Key == kv.Key && spaces.ContainsKey(e.Value))
                    {
                        targets.Add(e.Value);
                    }
                    else if (e.Value == kv.Key && spaces.ContainsKey(e.Key))
                    {
                        targets.Add(e.Key);
                    }
                }
                if (targets.Count != 1)
                {
                    throw new FormatException("Patrol stack " + kv.Key + " must lead to exactly one space (R-5.3).");
                }
                foreach (var f in kv.Value)
                {
                    spawn[f] = targets[0];
                }
            }
            return new MapGraph(order, spaces, adjacent, spawn, contactSpaces);
        }

        private static SortedSet<string> Adj(Dictionary<string, SortedSet<string>> adjacent, string id)
        {
            if (!adjacent.TryGetValue(id, out var set))
            {
                set = new SortedSet<string>(StringComparer.Ordinal);
                adjacent[id] = set;
            }
            return set;
        }

        private static SpaceType ParseType(string type)
        {
            switch (type)
            {
                case "planet": return SpaceType.Planet;
                case "navpoint": return SpaceType.Navpoint;
                case "maelstrom": return SpaceType.Maelstrom;
                case "core_worlds": return SpaceType.CoreWorlds;
                default: throw new FormatException("Unknown space type '" + type + "'.");
            }
        }

        private static Faction? ParseFaction(string? faction)
        {
            switch (faction)
            {
                case null: return null;
                case "rebel": return Faction.Rebel;
                case "imperial": return Faction.Imperial;
                case "hutt": return Faction.Hutt;
                case "syndicate": return Faction.Syndicate;
                default: throw new FormatException("Unknown faction '" + faction + "'.");
            }
        }
    }
}
