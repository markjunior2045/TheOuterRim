using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Random;
using OuterRim.Core.Persistence;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class GameEngineTests
    {
        internal static readonly string[] StartSpaces = { "lothal", "tatooine", "naboo", "takodana" };

        internal static GameSetup Setup(ulong seed, int players = 3)
        {
            var colors = new[] { "green", "red", "yellow", "white" };
            var list = new List<PlayerSetup>();
            for (int i = 0; i < players; i++)
            {
                list.Add(new PlayerSetup("p" + (i + 1), "Player " + (i + 1), colors[i], StartSpaces[i], TestContent.G9Rigger, "base-character-0" + i));
            }
            return new GameSetup(seed, list, new[] { new PackRef("base", "0.1.0") });
        }

        [Fact]
        public void R6_1_DieFaces_FollowTheEightFaceDistribution()
        {
            var faces = DiceRoller.Roll(new GameRandom(1), 80000);
            Assert.InRange(DiceRoller.Count(faces, DieFace.Hit), 29000, 31000);   // 3/8
            Assert.InRange(DiceRoller.Count(faces, DieFace.Crit), 9400, 10600);   // 1/8
            Assert.InRange(DiceRoller.Count(faces, DieFace.Focus), 19300, 20700); // 2/8
            Assert.InRange(DiceRoller.Count(faces, DieFace.Blank), 19300, 20700); // 2/8
        }

        [Fact]
        public void R3_11_FirstPlayer_HasTheMostHitsAndCrits_TiesReroll()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                GameEngine.NewGame(TestContent.Base, Setup(seed, 4), out var events);
                var rolls = events.OfType<FirstPlayerRolledEvent>().ToList();
                var chosen = events.OfType<FirstPlayerChosenEvent>().Single().PlayerId;
                // The last round of rolls only includes the tied leaders of the round before.
                var lastRound = new List<FirstPlayerRolledEvent>();
                var contenders = new HashSet<string> { "p1", "p2", "p3", "p4" };
                int i = 0;
                while (i < rolls.Count)
                {
                    lastRound = rolls.Skip(i).Take(contenders.Count).ToList();
                    i += contenders.Count;
                    int best = lastRound.Max(r => r.Score);
                    contenders = new HashSet<string>(lastRound.Where(r => r.Score == best).Select(r => r.PlayerId));
                }
                Assert.Single(contenders);
                Assert.Equal(chosen, contenders.Single());
                Assert.All(rolls, r => Assert.Equal(GameEngine.FirstPlayerDice, r.Faces.Count));
            }
        }

        [Fact]
        public void R3_11g_StartingCredits_FollowTurnOrder()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(11, 4), out _);
            int first = state.SeatOf(state.FirstPlayerId);
            var expected = new[] { 4000, 6000, 8000, 10000 };
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(expected[i], state.Players[(first + i) % 4].Credits);
            }
        }

        [Fact]
        public void R3_11d_PlayersStartNeutralWithZeroFame()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(3), out _);
            foreach (var p in state.Players)
            {
                Assert.Equal(0, p.Fame);
                foreach (Faction f in new[] { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate })
                {
                    Assert.Equal(Reputation.Neutral, p.Reputation.Get(f));
                }
            }
        }

        [Fact]
        public void NewGame_StartsWithTheFirstPlayersPlanningStep()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(8), out _);
            Assert.Equal(GamePhase.Playing, state.Phase);
            Assert.Equal(state.FirstPlayerId, state.CurrentPlayerId);
            Assert.Equal(TurnStep.Planning, state.Step);
            Assert.Equal(1, state.Round);
        }

        [Fact]
        public void R4_6_GainCredits_Adds2000_AndStartsTheActionStep()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(5), out _);
            var player = state.FindPlayer(state.CurrentPlayerId)!;
            int before = player.Credits;
            var result = GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(player.Id));
            Assert.True(result.Accepted);
            Assert.Equal(before + 2000, player.Credits);
            Assert.Equal(TurnStep.Action, state.Step);
            Assert.IsType<CreditsGainedEvent>(result.Events[0]);
        }

        [Fact]
        public void R4_6_PlanningOption_OnlyOncePerTurn()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(5), out _);
            var id = state.CurrentPlayerId;
            Assert.True(GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(id)).Accepted);
            var second = GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(id));
            Assert.False(second.Accepted);
            Assert.Contains("R-4.6", second.Reason);
        }

        [Fact]
        public void R4_5_OnlyTheCurrentPlayerActs()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(5), out _);
            var other = state.Players.First(p => p.Id != state.CurrentPlayerId);
            var before = SaveSerializer.Save(state);
            var result = GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(other.Id));
            Assert.False(result.Accepted);
            Assert.Contains("R-4.5", result.Reason);
            Assert.Equal(before, SaveSerializer.Save(state));
        }

        [Fact]
        public void R4_8_DefeatedPlayer_CannotGainCredits_ButCanRecover()
        {
            var state = GameEngine.NewGame(TestContent.Base, Setup(5), out _);
            var player = state.FindPlayer(state.CurrentPlayerId)!;
            player.Defeated = true;
            player.CharacterDamage = 4;
            player.ShipDamage = 2;
            var rejected = GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(player.Id));
            Assert.False(rejected.Accepted);
            Assert.Contains("R-4.8", rejected.Reason);
            var recovered = GameEngine.Execute(TestContent.Base, state, new RecoverDamageCommand(player.Id));
            Assert.True(recovered.Accepted);
            Assert.Equal(0, player.CharacterDamage);
            Assert.Equal(0, player.ShipDamage);
            Assert.False(player.Defeated);
        }

        [Fact]
        public void SameSeedAndCommands_GiveTheSameGame()
        {
            string Play(ulong seed)
            {
                var state = GameEngine.NewGame(TestContent.Base, Setup(seed, 4), out _);
                GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(state.CurrentPlayerId));
                return SaveSerializer.Save(state);
            }
            Assert.Equal(Play(2024), Play(2024));
            Assert.NotEqual(Play(2024), Play(2025));
        }
    }
}
