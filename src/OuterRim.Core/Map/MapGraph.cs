#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Model;

namespace OuterRim.Core.Map
{
    /// <summary>Space types (R-5.1; Core Worlds [UB] R-5.8).</summary>
    public enum SpaceType
    {
        Planet,
        Navpoint,
        Maelstrom,
        CoreWorlds,
    }

    /// <summary>A space of the assembled map.</summary>
    public sealed class Space
    {
        public Space(string id, SpaceType type, string? name, Faction? faction, string piece)
        {
            Id = id;
            Type = type;
            Name = name;
            Faction = faction;
            Piece = piece;
        }

        public string Id { get; }

        public SpaceType Type { get; }

        /// <summary>Printed name for planets and the Maelstrom; null for navpoints.</summary>
        public string? Name { get; }

        /// <summary>The planet's faction icon, if any (R-5.5).</summary>
        public Faction? Faction { get; }

        /// <summary>The map piece the space is printed on.</summary>
        public string Piece { get; }

        /// <summary>R-5.9: Core Worlds spaces are planets for all purposes.</summary>
        public bool IsPlanet => Type == SpaceType.Planet || Type == SpaceType.CoreWorlds;
    }

    /// <summary>A contact space: a slot for one contact token on a planet, with a class (R-12.3, R-12.4).</summary>
    public sealed class ContactSpace
    {
        public ContactSpace(string id, string planet, string contactClass)
        {
            Id = id;
            Planet = planet;
            Class = contactClass;
        }

        /// <summary>Stable id: "&lt;planet&gt;-contact-&lt;n&gt;", numbered left to right as printed.</summary>
        public string Id { get; }

        public string Planet { get; }

        /// <summary>white, green, yellow (orange contacts sit on the planet itself [UB], R-12.4).</summary>
        public string Class { get; }
    }

    /// <summary>
    /// The assembled map: spaces, paths and patrol spawn spaces for one layout (R-3.2, R-3.3).
    /// Static data, rebuilt from the packs and the layout when a game is loaded; never saved.
    /// </summary>
    public sealed class MapGraph
    {
        private readonly Dictionary<string, Space> _spaces;
        private readonly Dictionary<string, SortedSet<string>> _adjacent;
        private readonly Dictionary<Faction, string> _spawn;
        private readonly List<ContactSpace> _contactSpaces;

        internal MapGraph(IReadOnlyList<string> pieces, Dictionary<string, Space> spaces,
            Dictionary<string, SortedSet<string>> adjacent, Dictionary<Faction, string> spawn,
            List<ContactSpace> contactSpaces)
        {
            Pieces = pieces;
            _spaces = spaces;
            _adjacent = adjacent;
            _spawn = spawn;
            _contactSpaces = contactSpaces;
        }

        /// <summary>Map pieces left to right.</summary>
        public IReadOnlyList<string> Pieces { get; }

        public IEnumerable<Space> Spaces => _spaces.Values;

        public IReadOnlyList<ContactSpace> ContactSpaces => _contactSpaces;

        public Space this[string id] => _spaces.TryGetValue(id, out var s)
            ? s
            : throw new KeyNotFoundException("Unknown space '" + id + "'.");

        public bool Contains(string id)
        {
            return _spaces.ContainsKey(id);
        }

        /// <summary>R-5.2: two spaces are adjacent if a path connects them.</summary>
        public bool AreAdjacent(string a, string b)
        {
            return _adjacent.TryGetValue(a, out var n) && n.Contains(b);
        }

        /// <summary>Adjacent spaces, in ordinal id order (stable for tests and saves).</summary>
        public IReadOnlyCollection<string> Neighbours(string id)
        {
            return _adjacent.TryGetValue(id, out var n) ? (IReadOnlyCollection<string>)n : new string[0];
        }

        /// <summary>R-5.3, R-11.10: the space where a faction's patrols spawn.</summary>
        public string SpawnSpace(Faction faction)
        {
            return _spawn[faction];
        }

        /// <summary>Contact spaces of a planet, left to right.</summary>
        public IEnumerable<ContactSpace> ContactSpacesOf(string planet)
        {
            foreach (var c in _contactSpaces)
            {
                if (c.Planet == planet)
                {
                    yield return c;
                }
            }
        }

        /// <summary>
        /// Shortest distance in spaces, or -1 if unreachable (R-5.18, R-5.29). With
        /// <paramref name="throughCoreWorlds"/> false, paths may end on a Core Worlds space but not pass
        /// through one (R-5.33 for "nearest", R-11.22 for patrols).
        /// </summary>
        public int Distance(string from, string to, bool throughCoreWorlds = true)
        {
            var d = DistancesFrom(from, throughCoreWorlds);
            return d.TryGetValue(to, out var n) ? n : -1;
        }

        /// <summary>Breadth-first distances from one space to every reachable space.</summary>
        public Dictionary<string, int> DistancesFrom(string from, bool throughCoreWorlds = true)
        {
            if (!_spaces.ContainsKey(from))
            {
                throw new KeyNotFoundException("Unknown space '" + from + "'.");
            }
            var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [from] = 0 };
            var queue = new Queue<string>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!throughCoreWorlds && current != from && _spaces[current].Type == SpaceType.CoreWorlds)
                {
                    continue;
                }
                foreach (var next in Neighbours(current))
                {
                    if (!dist.ContainsKey(next))
                    {
                        dist[next] = dist[current] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return dist;
        }

        /// <summary>Spaces at most <paramref name="distance"/> away, the starting space included ("within N spaces").</summary>
        public List<string> Within(string from, int distance, bool throughCoreWorlds = true)
        {
            var result = new List<string>();
            foreach (var kv in DistancesFrom(from, throughCoreWorlds))
            {
                if (kv.Value <= distance)
                {
                    result.Add(kv.Key);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>
        /// "Nearest" (R-5.29 to R-5.33): the candidate spaces at the shortest distance from
        /// <paramref name="from"/>, never counting through the Core Worlds. A candidate in the same
        /// space is at distance 0 and therefore nearest (R-5.31). Several results mean a tie that the
        /// current player resolves (R-5.30). Excluding oneself from "nearest player" (R-5.32) is the
        /// caller's job: it simply does not pass its own space as a candidate for itself.
        /// </summary>
        public List<string> Nearest(string from, IEnumerable<string> candidates)
        {
            var dist = DistancesFrom(from, throughCoreWorlds: false);
            int best = int.MaxValue;
            var result = new List<string>();
            foreach (var c in candidates)
            {
                if (!dist.TryGetValue(c, out var d) || d > best)
                {
                    continue;
                }
                if (d < best)
                {
                    best = d;
                    result.Clear();
                }
                if (!result.Contains(c))
                {
                    result.Add(c);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>Number of undirected paths between spaces.</summary>
        public int EdgeCount
        {
            get
            {
                int n = 0;
                foreach (var kv in _adjacent)
                {
                    n += kv.Value.Count;
                }
                return n / 2;
            }
        }
    }
}
