using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class CharacterSetupTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(ulong seed, params string[] characters)
        {
            var colors = new[] { "green", "red", "yellow", "white" };
            var players = characters.Select((c, i) => new PlayerSetup("p" + (i + 1), "P" + (i + 1), colors[i], "", TestContent.G9Rigger, c)).ToList();
            return GameEngine.NewGame(C, new GameSetup(seed, players, new[] { new PackRef("base", "0.1.0") }), out _);
        }

        [Fact]
        public void R3_15_Ketsu_TakesACard90Bounty_StartsOnItsPlanet_PositiveWithTheSyndicate()
        {
            var s = Game(1, "base-character-00");
            var p = s.FindPlayer("p1")!;
            var asset = Assert.Single(p.Assets);
            Assert.StartsWith("base-databank-090", asset.CardId);
            Assert.Equal("bounty", asset.Kind);
            Assert.Equal((string)C.Card(asset.CardId)["starting_planet"]!, p.SpaceId);
            Assert.Equal(Reputation.Positive, p.Reputation.Syndicate);
            Assert.Equal(Reputation.Neutral, p.Reputation.Hutt);
            Assert.Null(s.Pending);
        }

        [Fact]
        public void R3_15a_ACargoStartingCard_GoesInACargoSlot()
        {
            var s = Game(2, "base-character-04"); // Han Solo: #91 cargo, negative with the Hutts
            var p = s.FindPlayer("p1")!;
            Assert.Equal("cargo", p.Assets.Single().Kind);
            Assert.Equal(Reputation.Negative, p.Reputation.Hutt);
            Assert.True(Rules.Assets.AllFit(C, p));
        }

        [Fact]
        public void R3_15c_Lando_ChoosesTheFactionOfHisPositiveReputation_BeforeTheFirstTurn()
        {
            var s = Game(3, "base-character-01", "base-character-00");
            Assert.Equal("p1", s.Pending!.PlayerId);
            Assert.Equal(new[] { "rebel", "imperial", "hutt", "syndicate" }, s.Pending.Options.Select(o => o.Id));
            Assert.Contains("Waiting", GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId)).Reason);
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "rebel"));
            Assert.Equal(Reputation.Positive, s.FindPlayer("p1")!.Reputation.Rebel);
            Assert.Null(s.Pending);
            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "rebel"));
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }

        [Fact]
        public void R13_3_PlayersSharingANumber_EachTakeADifferentCard()
        {
            // Ketsu, Bossk, Boba Fett and IG-88 all start with card #90, which has four versions.
            for (ulong seed = 0; seed < 20; seed++)
            {
                var s = Game(seed, "base-character-00", "base-character-03", "base-character-06", "base-character-07");
                var cards = s.Players.Select(p => p.Assets.Single().CardId).ToList();
                Assert.Equal(4, cards.Distinct().Count());
                Assert.All(cards, c => Assert.StartsWith("base-databank-090", c));
                Assert.Equal(Reputation.Neutral, s.FindPlayer("p4")!.Reputation.Hutt); // IG-88: no starting reputation
            }
        }

        [Fact]
        public void TheStartingCard_IsRandom_ButFixedBySeed()
        {
            var cards = new HashSet<string>();
            for (ulong seed = 0; seed < 30; seed++)
            {
                cards.Add(Game(seed, "base-character-02").FindPlayer("p1")!.Assets.Single().CardId);
            }
            Assert.Equal(3, cards.Count); // Jyn's #92 has three versions
            Assert.Equal(Game(9, "base-character-02").FindPlayer("p1")!.Assets.Single().CardId,
                Game(9, "base-character-02").FindPlayer("p1")!.Assets.Single().CardId);
        }

        [Fact]
        public void AStartSpaceGiven_SkipsTheSetupLine()
        {
            var s = GameEngine.NewGame(C, new GameSetup(4, new[] { new PlayerSetup("p1", "P1", "green", "kessel", TestContent.G9Rigger, "base-character-00") },
                new[] { new PackRef("base", "0.1.0") }), out _);
            var p = s.FindPlayer("p1")!;
            Assert.Equal("kessel", p.SpaceId);
            Assert.Empty(p.Assets);
            Assert.Equal(Reputation.Neutral, p.Reputation.Syndicate);
        }
    }
}
