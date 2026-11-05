using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using Xunit;

namespace OuterRim.Core.Tests
{
    internal static class Repo
    {
        /// <summary>Finds a file by its path from the repository root, walking up from the test binaries.</summary>
        public static string Read(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
                dir = dir.Parent;
            }
            throw new FileNotFoundException("Not found above the test folder: " + relative);
        }

        public static MapLoader Maps()
        {
            return new MapLoader().Add(Read("data/map/base-map.json")).Add(Read("data/map/expansion-map.json"));
        }
    }

    /// <summary>Base game content for tests: standard map and the ship sheets.</summary>
    internal static class TestContent
    {
        public const string G9Rigger = "base-ship-g9-rigger";
        public const string G1AStarfighter = "base-ship-g9-rigger-back";

        public static readonly Content.GameContent Base = Build();

        private static Content.GameContent Build()
        {
            var content = new Content.GameContent(
                Repo.Maps().Build("standard"),
                Content.GameContent.ReadShipSheets(Repo.Read("data/packs/base/cards/ship-sheets.json")),
                Content.GameContent.ReadCharacters(Repo.Read("data/packs/base/cards/characters.json")));
            content.AddNames(Repo.Read("data/packs/base/loc/en.json"));
            return content;
        }
    }

    public class MapGraphTests
    {
        private static readonly MapLoader Loader = Repo.Maps();
        private static readonly MapGraph Standard = Loader.Build("standard");
        private static readonly MapGraph CoreWorlds = Loader.Build("core-worlds");

        [Fact]
        public void R3_2_StandardLayout_HasTheBoardsSpaces()
        {
            Assert.Equal(11, Standard.Spaces.Count(s => s.Type == SpaceType.Planet));
            Assert.Equal(34, Standard.Spaces.Count(s => s.Type == SpaceType.Navpoint));
            Assert.Single(Standard.Spaces, s => s.Type == SpaceType.Maelstrom);
            Assert.Equal(51, Standard.EdgeCount);
            Assert.Equal(59, CoreWorlds.EdgeCount);
        }

        [Fact]
        public void R12_4_ContactSpaces_MatchTheBaseContacts()
        {
            var byClass = Standard.ContactSpaces.GroupBy(c => c.Class).ToDictionary(g => g.Key, g => g.Count());
            Assert.Equal(6, byClass["white"]);
            Assert.Equal(7, byClass["green"]);
            Assert.Equal(9, byClass["yellow"]);
            Assert.All(Standard.Spaces.Where(s => s.Type == SpaceType.Planet),
                p => Assert.Equal(2, Standard.ContactSpacesOf(p.Id).Count()));
            Assert.Equal(Standard.ContactSpaces.Count, Standard.ContactSpaces.Select(c => c.Id).Distinct().Count());
        }

        [Fact]
        public void R5_5_PlanetFactions_FollowTheIcons()
        {
            Assert.Equal(Faction.Syndicate, Standard["kessel"].Faction);
            Assert.Equal(Faction.Imperial, Standard["lothal"].Faction);
            Assert.Equal(Faction.Rebel, Standard["mon-calamari"].Faction);
            Assert.Equal(Faction.Hutt, Standard["tatooine"].Faction);
            Assert.Null(Standard["cantonica"].Faction);
            Assert.Null(Standard["takodana"].Faction);
            Assert.Null(Standard["ring-of-kafrene"].Faction);
        }

        [Fact]
        public void R5_2_Paths_AreSymmetric()
        {
            foreach (var graph in new[] { Standard, CoreWorlds })
            {
                foreach (var s in graph.Spaces)
                {
                    foreach (var n in graph.Neighbours(s.Id))
                    {
                        Assert.True(graph.AreAdjacent(n, s.Id), s.Id + " -> " + n + " has no way back");
                    }
                }
            }
        }

        [Fact]
        public void EverySpace_IsReachable()
        {
            foreach (var graph in new[] { Standard, CoreWorlds })
            {
                var reach = graph.DistancesFrom("kessel");
                Assert.Equal(graph.Spaces.Count(), reach.Count);
            }
        }

        [Fact]
        public void R3_3_AnyTileOrder_GivesAConnectedMap()
        {
            var layout = Loader.Layout("standard");
            var tiles = layout.Skip(1).Take(layout.Count - 2).ToList();
            var rng = new OuterRim.Core.Random.GameRandom(3);
            for (int i = 0; i < 50; i++)
            {
                rng.Shuffle(tiles);
                var order = new List<string> { layout[0] };
                order.AddRange(tiles);
                order.Add(layout[layout.Count - 1]);
                var graph = Loader.Build(order);
                Assert.Equal(graph.Spaces.Count(), graph.DistancesFrom("maelstrom").Count);
            }
        }

        [Fact]
        public void R3_5_R5_3_PatrolsSpawnNextToTheirStacks()
        {
            Assert.Equal("nav-cantonica-1", Standard.SpawnSpace(Faction.Rebel));
            Assert.Equal("nav-cantonica-2", Standard.SpawnSpace(Faction.Syndicate));
            Assert.Equal("nav-takodana-6", Standard.SpawnSpace(Faction.Hutt));
            Assert.Equal("nav-takodana-5", Standard.SpawnSpace(Faction.Imperial));
            Assert.All(new[] { Faction.Rebel, Faction.Syndicate, Faction.Hutt, Faction.Imperial },
                f => Assert.Equal(SpaceType.Navpoint, Standard[Standard.SpawnSpace(f)].Type));
        }

        [Fact]
        public void R3_6_CoreWorldsEndcaps_ShareASpawnSpacePerPair()
        {
            Assert.Equal(CoreWorlds.SpawnSpace(Faction.Rebel), CoreWorlds.SpawnSpace(Faction.Syndicate));
            Assert.Equal(CoreWorlds.SpawnSpace(Faction.Hutt), CoreWorlds.SpawnSpace(Faction.Imperial));
            Assert.NotEqual(CoreWorlds.SpawnSpace(Faction.Rebel), CoreWorlds.SpawnSpace(Faction.Hutt));
        }

        [Fact]
        public void R5_4_CoreWorlds_AreNotAdjacent()
        {
            Assert.False(CoreWorlds.AreAdjacent("spinward-core", "trailing-core"));
            Assert.True(CoreWorlds.Distance("spinward-core", "trailing-core") > 1);
            Assert.True(CoreWorlds["spinward-core"].IsPlanet);
        }

        [Fact]
        public void R5_18_Distance_CountsSpacesAlongPaths()
        {
            Assert.Equal(0, Standard.Distance("kessel", "kessel"));
            Assert.Equal(1, Standard.Distance("kessel", "maelstrom"));
            Assert.Equal(2, Standard.Distance("kessel", "nav-kessel-1"));
            Assert.Equal(Standard.Distance("tatooine", "lothal"), Standard.Distance("lothal", "tatooine"));
        }

        [Fact]
        public void Within_IncludesTheStartingSpace()
        {
            var near = Standard.Within("kessel", 1);
            Assert.Equal(new[] { "kessel", "maelstrom", "nav-kessel-3" }, near);
        }

        [Fact]
        public void R5_31_ACandidateInTheSameSpace_IsTheNearest()
        {
            Assert.Equal(new[] { "kessel" }, Standard.Nearest("kessel", new[] { "tatooine", "kessel", "lothal" }));
        }

        [Fact]
        public void R5_29_R5_30_Nearest_ReturnsEveryTiedCandidate()
        {
            // From the Maelstrom, Kessel and nav-kessel-1 are both one space away: a tie for the current player.
            Assert.Equal(new[] { "kessel", "nav-kessel-1" }, Standard.Nearest("maelstrom", new[] { "nav-kessel-1", "kessel", "tatooine" }));
            // Planets nearest to the Maelstrom: only Kessel.
            var planets = Standard.Spaces.Where(s => s.IsPlanet).Select(s => s.Id);
            Assert.Equal(new[] { "kessel" }, Standard.Nearest("maelstrom", planets));
        }

        [Fact]
        public void R5_33_Nearest_DoesNotCountThroughTheCoreWorlds()
        {
            // Small map: a - core - b is short, a - x - y - b is long. Nearest must take the long way.
            const string json = @"{ ""format"": ""outer-rim-map/0.1"", ""pack"": ""test"", ""pieces"": [ {
                ""id"": ""t"", ""image"": ""t"", ""contact_spaces"": [], ""patrol_stacks"": [], ""ports"": {},
                ""spaces"": [
                  { ""id"": ""a"", ""type"": ""navpoint"", ""name"": null, ""faction"": null },
                  { ""id"": ""core"", ""type"": ""core_worlds"", ""name"": ""Core"", ""faction"": null },
                  { ""id"": ""b"", ""type"": ""planet"", ""name"": ""B"", ""faction"": null },
                  { ""id"": ""x"", ""type"": ""navpoint"", ""name"": null, ""faction"": null },
                  { ""id"": ""y"", ""type"": ""navpoint"", ""name"": null, ""faction"": null } ],
                ""edges"": [ [""a"", ""core""], [""core"", ""b""], [""a"", ""x""], [""x"", ""y""], [""y"", ""b""] ] } ],
              ""layouts"": [ { ""id"": ""t"", ""pieces"": [""t""] } ] }";
            var g = new MapLoader().Add(json).Build("t");
            Assert.Equal(2, g.Distance("a", "b"));
            Assert.Equal(3, g.Distance("a", "b", throughCoreWorlds: false));
            Assert.Equal(new[] { "core" }, g.Nearest("a", new[] { "core", "b" }));
            Assert.Equal(new[] { "b" }, g.Nearest("x", new[] { "b" }));
        }

        [Fact]
        public void UnknownSpace_IsAnError()
        {
            Assert.Throws<KeyNotFoundException>(() => Standard.Distance("coruscant", "kessel"));
        }

        [Fact]
        public void WrongFormat_IsRejected()
        {
            Assert.Throws<FormatException>(() => new MapLoader().Add(@"{ ""format"": ""outer-rim-map/9"", ""pieces"": [] }"));
        }
    }
}
