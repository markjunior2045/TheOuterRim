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
        public ShipSide(string id, string sheetId, string role, int hyperdrive, int shipCombat, int hull,
            int? cost = null, IReadOnlyList<SlotSpec>? slots = null)
        {
            Id = id;
            SheetId = sheetId;
            Role = role;
            Hyperdrive = hyperdrive;
            ShipCombat = shipCombat;
            Hull = hull;
            Cost = cost;
            Slots = slots ?? new SlotSpec[0];
        }

        /// <summary>Price and barter value (R-17.6); null for the starter sheet.</summary>
        public int? Cost { get; }

        /// <summary>Slots on this side (R-17.5).</summary>
        public IReadOnlyList<SlotSpec> Slots { get; }

        /// <summary>The side's image id, unique per side.</summary>
        public string Id { get; }

        public string SheetId { get; }

        /// <summary>ship_goal, flipped or starter.</summary>
        public string Role { get; }

        public int Hyperdrive { get; }

        public int ShipCombat { get; }

        public int Hull { get; }
    }

    /// <summary>A patrol token: combat value (null for level 4, R-11.6) and reward (R-11.4).</summary>
    public sealed class PatrolToken
    {
        public PatrolToken(OuterRim.Core.Model.Faction faction, int level, int? combat, int rewardCredits, int rewardFame)
        {
            Faction = faction;
            Level = level;
            Combat = combat;
            RewardCredits = rewardCredits;
            RewardFame = rewardFame;
        }

        public OuterRim.Core.Model.Faction Faction { get; }

        public int Level { get; }

        public int? Combat { get; }

        public int RewardCredits { get; }

        public int RewardFame { get; }
    }

    /// <summary>A character card: the values the rules read (data/packs/*/cards/characters.json).</summary>
    public sealed class CharacterCard
    {
        public CharacterCard(string id, int groundCombat, int health, IReadOnlyCollection<Skill> skills, bool droid = false)
        {
            Id = id;
            GroundCombat = groundCombat;
            Health = health;
            Skills = new HashSet<Skill>(skills);
            Droid = droid;
        }

        /// <summary>The character carries the droid icon (IG-88).</summary>
        public bool Droid { get; }

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

        /// <summary>Slots of the player board (all four colours alike): 2 gear, 2 job or bounty.</summary>
        public IReadOnlyList<SlotSpec> BoardSlots { get; private set; } = new SlotSpec[0];

        /// <summary>Reads the player board slots from a card data file; all boards must be identical.</summary>
        public void SetBoardSlots(string json)
        {
            var root = JObject.Parse(json);
            List<SlotSpec>? first = null;
            foreach (JObject card in (JArray)root["cards"]!)
            {
                if ((string?)card["kind"] != "player_board")
                {
                    continue;
                }
                var slots = ReadSlots((JArray)card["slots"]!);
                if (first == null)
                {
                    first = slots;
                }
                else if (slots.Count != first.Count)
                {
                    throw new FormatException("Player boards differ in slots.");
                }
            }
            BoardSlots = first ?? throw new FormatException("No player board in the file.");
        }

        private static List<SlotSpec> ReadSlots(JArray slots)
        {
            var result = new List<SlotSpec>();
            foreach (JObject s in slots)
            {
                var types = new List<string>();
                foreach (var t in (JArray)s["types"]!)
                {
                    types.Add((string)t!);
                }
                result.Add(new SlotSpec(types, (int)s["count"]!));
            }
            return result;
        }

        /// <summary>The ship goal side of a buyable sheet: where a bought ship starts (R-16.27, R-17.9).</summary>
        public ShipSide ShipGoalSide(string sheetId)
        {
            foreach (var s in _shipSides.Values)
            {
                if (s.SheetId == sheetId && s.Role == "ship_goal")
                {
                    return s;
                }
            }
            throw new KeyNotFoundException("Ship sheet '" + sheetId + "' has no ship goal side.");
        }

        /// <summary>Every sheet id, once.</summary>
        public List<string> ShipSheetIds()
        {
            var ids = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var s in _shipSides.Values)
            {
                ids.Add(s.SheetId);
            }
            return new List<string>(ids);
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
        private readonly Dictionary<string, JObject> _cards = new Dictionary<string, JObject>(StringComparer.Ordinal);

        /// <summary>
        /// Loads every card of a card data file (format outer-rim-data/0.1) as data, for the effect engine
        /// to read abilities from (DATA_FORMAT.md). Typed views (ship sides, characters, patrols) are loaded
        /// separately.
        /// </summary>
        public void AddCards(string json)
        {
            var root = JObject.Parse(json);
            if ((string?)root["format"] != "outer-rim-data/0.1")
            {
                throw new FormatException("Unsupported card data format '" + (string?)root["format"] + "'.");
            }
            foreach (JObject card in (JArray)root["cards"]!)
            {
                _cards[(string)card["id"]!] = card;
            }
        }

        public bool HasCard(string id)
        {
            return _cards.ContainsKey(id);
        }

        public JObject Card(string id)
        {
            return _cards.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException("Unknown card '" + id + "'.");
        }

        /// <summary>The other side of the sheet a side belongs to (flipping, R-18.3).</summary>
        public ShipSide OtherShipSide(string sideId)
        {
            var side = ShipSide(sideId);
            foreach (var s in _shipSides.Values)
            {
                if (s.SheetId == side.SheetId && s.Id != side.Id)
                {
                    return s;
                }
            }
            throw new KeyNotFoundException("Ship side '" + sideId + "' has no other side.");
        }
        private readonly Dictionary<string, PatrolToken> _patrols = new Dictionary<string, PatrolToken>(StringComparer.Ordinal);

        public void AddPatrols(IEnumerable<PatrolToken> tokens)
        {
            foreach (var t in tokens)
            {
                _patrols[t.Faction + "/" + t.Level] = t;
            }
        }

        public PatrolToken PatrolToken(OuterRim.Core.Model.Faction faction, int level)
        {
            return _patrols.TryGetValue(faction + "/" + level, out var t)
                ? t
                : throw new KeyNotFoundException("No " + faction + " patrol token of level " + level + ".");
        }

        /// <summary>Reads the patrol tokens of a card data file (format outer-rim-data/0.1).</summary>
        public static List<PatrolToken> ReadPatrols(string json)
        {
            var root = JObject.Parse(json);
            var result = new List<PatrolToken>();
            foreach (JObject card in (JArray)root["cards"]!)
            {
                if ((string?)card["kind"] != "patrol")
                {
                    continue;
                }
                int credits = 0, fame = 0;
                foreach (JObject e in (JArray)card["reward"]!)
                {
                    var op = (string)e["op"]!;
                    if (op == "credits.gain")
                    {
                        credits += (int)e["amount"]!;
                    }
                    else if (op == "fame.gain")
                    {
                        fame += (int)e["amount"]!;
                    }
                    else
                    {
                        throw new FormatException("Unsupported patrol reward '" + op + "'.");
                    }
                }
                var faction = OuterRim.Core.Rules.Patrols.Parse((string)card["faction"]!);
                result.Add(new PatrolToken(faction, (int)card["level"]!, (int?)card["combat"], credits, fame));
            }
            return result;
        }

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
                result.Add(new CharacterCard((string)card["id"]!, (int)card["ground_combat"]!, (int)card["health"]!, skills,
                    (bool?)card["droid"] ?? false));
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
                        (int)stats["hyperdrive"]!, (int)stats["ship_combat"]!, (int)stats["hull"]!,
                        (int?)side["cost"], ReadSlots((JArray)side["slots"]!)));
                }
            }
            return result;
        }
    }
}
