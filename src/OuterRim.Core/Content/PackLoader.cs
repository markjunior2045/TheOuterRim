#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Map;

namespace OuterRim.Core.Content
{
    /// <summary>
    /// Builds a <see cref="GameContent"/> from pack manifests (format outer-rim-pack/0.1, DATA_FORMAT.md):
    /// the map files, every card file and the English names. File access is the caller's, so the same
    /// loader serves the tests, the terminal runner and Unity.
    /// </summary>
    public static class PackLoader
    {
        public const string ManifestFormat = "outer-rim-pack/0.1";

        /// <summary>
        /// Loads the packs whose manifests are at <paramref name="manifestPaths"/>, in order.
        /// <paramref name="read"/> returns the text of a file given its path from the repository root,
        /// as written in the manifests. <paramref name="extraMaps"/> are added to the map loader as well
        /// (the expansion map, for layouts that use its pieces).
        /// </summary>
        public static GameContent Load(Func<string, string> read, IEnumerable<string> manifestPaths, string layout = "standard",
            IEnumerable<string>? extraMaps = null, string language = "en")
        {
            var maps = new MapLoader();
            var cardFiles = new List<string>();
            var names = new List<string>();
            foreach (var path in manifestPaths)
            {
                var manifest = JObject.Parse(read(path));
                if ((string?)manifest["format"] != ManifestFormat)
                {
                    throw new FormatException("Unsupported pack format '" + (string?)manifest["format"] + "' in " + path + ".");
                }
                if (manifest["map"] is JValue map)
                {
                    maps.Add(read((string)map!));
                }
                foreach (var file in (JArray?)manifest["cards"] ?? new JArray())
                {
                    cardFiles.Add(read((string)file!));
                }
                if (manifest["localization"]?[language] is JValue loc)
                {
                    names.Add(read((string)loc!));
                }
            }
            foreach (var path in extraMaps ?? new string[0])
            {
                maps.Add(read(path));
            }

            // Typed views first: the content is built around the ship sheets and characters.
            var ships = new List<ShipSide>();
            var characters = new List<CharacterCard>();
            foreach (var json in cardFiles)
            {
                switch (KindOf(json))
                {
                    case "ship_sheet":
                        ships.AddRange(GameContent.ReadShipSheets(json));
                        break;
                    case "character":
                        characters.AddRange(GameContent.ReadCharacters(json));
                        break;
                }
            }
            var content = new GameContent(maps.Build(layout), ships, characters);
            foreach (var loc in names)
            {
                content.AddNames(loc);
            }
            foreach (var json in cardFiles)
            {
                switch (KindOf(json))
                {
                    case "patrol":
                        content.AddPatrols(GameContent.ReadPatrols(json));
                        break;
                    case "player_board":
                        content.SetBoardSlots(json);
                        break;
                    default:
                        content.AddCards(json);
                        break;
                }
            }
            return content;
        }

        /// <summary>The kind of the first card of a card data file.</summary>
        private static string KindOf(string json)
        {
            var cards = (JArray?)JObject.Parse(json)["cards"];
            return cards != null && cards.Count > 0 ? (string?)cards[0]["kind"] ?? "" : "";
        }
    }
}
