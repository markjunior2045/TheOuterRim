#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Map;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Content
{
    /// <summary>One side of a ship sheet: the values the rules read (data/packs/*/cards/ship-sheets.json).</summary>
    public sealed class ShipSide
    {
        public ShipSide(string id, string sheetId, string role, int hyperdrive, int shipCombat, int hull)
        {
            Id = id;
            SheetId = sheetId;
            Role = role;
            Hyperdrive = hyperdrive;
            ShipCombat = shipCombat;
            Hull = hull;
        }

        /// <summary>The side's image id, unique per side.</summary>
        public string Id { get; }

        public string SheetId { get; }

        /// <summary>ship_goal, flipped or starter.</summary>
        public string Role { get; }

        public int Hyperdrive { get; }

        public int ShipCombat { get; }

        public int Hull { get; }
    }

    /// <summary>A character card: the values the rules read (data/packs/*/cards/characters.json).</summary>
    public sealed class CharacterCard
    {
        public CharacterCard(string id, int groundCombat, int health, IReadOnlyCollection<Skill> skills)
        {
            Id = id;
            GroundCombat = groundCombat;
            Health = health;
            Skills = new HashSet<Skill>(skills);
        }

        public string Id { get; }

        public int GroundCombat { get; }

        public int Health { get; }

        /// <summary>R-6.4, R-6.5: skills on the character card, never lost.</summary>
        public HashSet<Skill> Skills { get; }
    }

    /// <summary>
    /// Static content of a game: the assembled map and card data. Not saved; rebuilt from the packs
    /// and the map pieces recorded in the state. Grows as card kinds are loaded (PLAN T2.3 onward).
    /// </summary>
    public sealed class GameContent
    {
        private static readonly HashSet<Skill> NoSkills = new HashSet<Skill>();

        private readonly Dictionary<string, ShipSide> _shipSides;
        private readonly Dictionary<string, CharacterCard> _characters = new Dictionary<string, CharacterCard>(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<Skill>> _crewSkills = new Dictionary<string, HashSet<Skill>>(StringComparer.Ordinal);

        public GameContent(MapGraph map, IEnumerable<ShipSide> shipSides, IEnumerable<CharacterCard>? characters = null)
        {
            Map = map;
            _shipSides = new Dictionary<string, ShipSide>(StringComparer.Ordinal);
            foreach (var s in shipSides)
            {
                _shipSides[s.Id] = s;
            }
            foreach (var c in characters ?? new CharacterCard[0])
            {
                _characters[c.Id] = c;
            }
        }

        public CharacterCard Character(string id)
        {
            return _characters.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException("Unknown character '" + id + "'.");
        }

        public bool HasCharacter(string id)
        {
            return _characters.ContainsKey(id);
        }

        private readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Loads display names ("&lt;card id&gt;.name") from a pack's strings file (format outer-rim-loc/0.1).
        /// Used for event labels and logs; the interface reads its own language file.
        /// </summary>
        public void AddNames(string locJson)
        {
            var strings = (JObject?)JObject.Parse(locJson)["strings"];
            if (strings == null)
            {
                return;
            }
            foreach (var kv in strings)
            {
                if (kv.Key.EndsWith(".name", StringComparison.Ordinal) && kv.Key.IndexOf('.') == kv.Key.Length - 5)
                {
                    _names[kv.Key.Substring(0, kv.Key.Length - 5)] = (string)kv.Value!;
                }
            }
        }

        /// <summary>Display name of a card, or its id when no name is loaded.</summary>
        public string Name(string cardId)
        {
            return _names.TryGetValue(cardId, out var n) ? n : cardId;
        }

        /// <summary>Registers the skills of a crew card (R-6.4). Crew cards are loaded with their decks.</summary>
        public void AddCrew(string cardId, IEnumerable<Skill> skills)
        {
            _crewSkills[cardId] = new HashSet<Skill>(skills);
        }

        /// <summary>Skills printed on a crew card; none for unknown cards.</summary>
        public HashSet<Skill> CrewSkills(string cardId)
        {
            return _crewSkills.TryGetValue(cardId, out var s) ? s : NoSkills;
        }

        /// <summary>Reads the characters of a card data file (format outer-rim-data/0.1).</summary>
        public static List<CharacterCard> ReadCharacters(string json)
        {
            var root = JObject.Parse(json);
            if ((string?)root["format"] != "outer-rim-data/0.1")
            {
                throw new FormatException("Unsupported card data format '" + (string?)root["format"] + "'.");
            }
            var result = new List<CharacterCard>();
            foreach (JObject card in (JArray)root["cards"]!)
            {
                if ((string?)card["kind"] != "character")
                {
                    continue;
                }
                var skills = new List<Skill>();
                foreach (var sk in (JArray)card["skills"]!)
                {
                    skills.Add(SkillTests.Parse((string)sk!));
                }
                result.Add(new CharacterCard((string)card["id"]!, (int)card["ground_combat"]!, (int)card["health"]!, skills));
            }
            return result;
        }

        public MapGraph Map { get; }

        public ShipSide ShipSide(string id)
        {
            return _shipSides.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException("Unknown ship side '" + id + "'.");
        }

        public bool HasShipSide(string id)
        {
            return _shipSides.ContainsKey(id);
        }

        /// <summary>Reads the ship sheets of a card data file (format outer-rim-data/0.1).</summary>
        public static List<ShipSide> ReadShipSheets(string json)
        {
            var root = JObject.Parse(json);
            if ((string?)root["format"] != "outer-rim-data/0.1")
            {
                throw new FormatException("Unsupported card data format '" + (string?)root["format"] + "'.");
            }
            var result = new List<ShipSide>();
            foreach (JObject card in (JArray)root["cards"]!)
            {
                if ((string?)card["kind"] != "ship_sheet")
                {
                    continue;
                }
                var sheet = (string)card["id"]!;
                foreach (JObject side in (JArray)card["sides"]!)
                {
                    var stats = (JObject)side["stats"]!;
                    result.Add(new ShipSide((string)side["image"]!, sheet, (string)side["role"]!,
                        (int)stats["hyperdrive"]!, (int)stats["ship_combat"]!, (int)stats["hull"]!));
                }
            }
            return result;
        }
    }
}
