#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Map;

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

    /// <summary>
    /// Static content of a game: the assembled map and card data. Not saved; rebuilt from the packs
    /// and the map pieces recorded in the state. Grows as card kinds are loaded (PLAN T2.3 onward).
    /// </summary>
    public sealed class GameContent
    {
        private readonly Dictionary<string, ShipSide> _shipSides;

        public GameContent(MapGraph map, IEnumerable<ShipSide> shipSides)
        {
            Map = map;
            _shipSides = new Dictionary<string, ShipSide>(StringComparer.Ordinal);
            foreach (var s in shipSides)
            {
                _shipSides[s.Id] = s;
            }
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
