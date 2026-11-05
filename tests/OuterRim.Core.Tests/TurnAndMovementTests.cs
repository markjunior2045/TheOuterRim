using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class TurnAndMovementTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        /// <summary>A chain of seven adjacent spaces on the Kessel tile, starting next to Kessel.</summary>
        private static readonly string[] KesselLoop =
            { "nav-kessel-3", "nav-kessel-5", "nav-kessel-4", "nav-kessel-7", "nav-kessel-6", "nav-kessel-2", "nav-kessel-1" };

        private static GameState Game(ulong seed = 21, int players = 3)
        {
            return GameEngine.NewGame(C, GameEngineTests.Setup(seed, players), out _);
        }

        private static PlayerState Current(GameState s)
        {
            return s.FindPlayer(s.CurrentPlayerId)!;
        }

        private static CommandResult Move(GameState s, params string[] path)
        {
            return GameEngine.Execute(C, s, new MoveCommand(s.CurrentPlayerId, path));
        }

        private static void At(GameState s, string space)
        {
            Current(s).SpaceId = space;
        }

        private static void PatrolAt(GameState s, Faction f, string space)
        {
            s.Patrols.Single(p => p.Faction == f).SpaceId = space;
        }

        // ---- setup ---------------------------------------------------------------------

        [Fact]
        public void R3_5_LevelOnePatrols_StartOnTheirSpawnSpaces()
        {
            var s = Game();
            Assert.Equal(4, s.Patrols.Count);
            Assert.All(s.Patrols, p => Assert.Equal(1, p.Level));
            Assert.All(s.Patrols, p => Assert.Equal(C.Map.SpawnSpace(p.Faction), p.SpaceId));
        }

        [Fact]
        public void R3_11e_R3_15b_PlayersStartWithTheStarterShip_OnTheirPlanet()
        {
            var s = Game();
            Assert.All(s.Players, p => Assert.Equal("base-ship-g9-rigger", p.ShipSheetId));
            Assert.Equal("lothal", s.Players[0].SpaceId);
            Assert.Equal(new List<string>(C.Map.Pieces), s.MapPieces);
        }

        // ---- movement --------------------------------------------------------------------

        [Fact]
        public void R5_17_Move_UpToTheHyperdrive()
        {
            var s = Game();
            At(s, "kessel");
            Assert.Contains("R-5.17", Move(s, KesselLoop).Reason);
            Assert.True(Move(s, KesselLoop.Take(6).ToArray()).Accepted);
            Assert.Equal("nav-kessel-2", Current(s).SpaceId);
            Assert.Equal(TurnStep.Action, s.Step);
        }

        [Fact]
        public void R5_17_TheG1AStarfighterSide_HasHyperdriveFive()
        {
            var s = GameEngine.NewGame(C, new GameSetup(4, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G1AStarfighter),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            Assert.Contains("R-5.17", Move(s, KesselLoop.Take(6).ToArray()).Reason);
            Assert.True(Move(s, KesselLoop.Take(5).ToArray()).Accepted);
        }

        [Fact]
        public void R5_17_MovingZeroSpaces_IsAllowed()
        {
            var s = Game();
            var before = Current(s).SpaceId;
            Assert.True(Move(s).Accepted);
            Assert.Equal(before, Current(s).SpaceId);
            Assert.Equal(TurnStep.Action, s.Step);
        }

        [Fact]
        public void R5_18_EachStep_MustBeAnAdjacentSpace()
        {
            var s = Game();
            At(s, "kessel");
            Assert.Contains("R-5.18", Move(s, "nav-kessel-5").Reason);
            Assert.Contains("R-5.18", Move(s, "coruscant").Reason);
            Assert.Equal("kessel", Current(s).SpaceId);
        }

        [Fact]
        public void R5_11_EnteringTheMaelstrom_EndsMovement()
        {
            var s = Game();
            At(s, "kessel");
            Assert.Contains("R-5.11", Move(s, "maelstrom", "nav-kessel-1").Reason);
            Assert.True(Move(s, "maelstrom").Accepted);
        }

        [Fact]
        public void R5_22_StartingInTheMaelstrom_YouCanLeave()
        {
            var s = Game();
            At(s, "maelstrom");
            Assert.True(Move(s, "nav-kessel-1", "nav-kessel-2").Accepted);
        }

        [Fact]
        public void R5_20_EnteringAPatrolsSpace_EndsMovement()
        {
            var s = Game();
            At(s, "kessel");
            PatrolAt(s, Faction.Imperial, "nav-kessel-3");
            Assert.Contains("R-5.20", Move(s, "nav-kessel-3", "nav-kessel-5").Reason);
            Assert.True(Move(s, "nav-kessel-3").Accepted);
        }

        [Fact]
        public void R5_20_R5_21_PositiveReputation_LetsYouPassThePatrol()
        {
            var s = Game();
            At(s, "kessel");
            PatrolAt(s, Faction.Imperial, "nav-kessel-3");
            Current(s).Reputation.Set(Faction.Imperial, Reputation.Positive);
            Assert.True(Move(s, "nav-kessel-3", "nav-kessel-5").Accepted);
        }

        [Fact]
        public void R5_20_TwoPatrols_YouMustBePositiveWithBoth()
        {
            var s = Game();
            At(s, "kessel");
            PatrolAt(s, Faction.Imperial, "nav-kessel-3");
            PatrolAt(s, Faction.Hutt, "nav-kessel-3");
            Current(s).Reputation.Set(Faction.Imperial, Reputation.Positive);
            Assert.Contains("R-5.20", Move(s, "nav-kessel-3", "nav-kessel-5").Reason);
        }

        [Fact]
        public void R5_21_OtherPlayers_DoNotStopMovement()
        {
            var s = Game();
            At(s, "kessel");
            s.Players.First(p => p.Id != s.CurrentPlayerId).SpaceId = "nav-kessel-3";
            Assert.True(Move(s, "nav-kessel-3", "nav-kessel-5").Accepted);
        }

        [Fact]
        public void R5_22_StartingInAPatrolsSpace_YouCanLeave()
        {
            var s = Game();
            At(s, "nav-kessel-3");
            PatrolAt(s, Faction.Imperial, "nav-kessel-3");
            Assert.True(Move(s, "nav-kessel-5", "nav-kessel-4").Accepted);
        }

        [Fact]
        public void R4_8_ADefeatedPlayer_CannotMove()
        {
            var s = Game();
            Current(s).Defeated = true;
            Assert.Contains("R-4.8", Move(s).Reason);
        }

        [Fact]
        public void R4_6_OnlyOnePlanningOption()
        {
            var s = Game();
            Assert.True(Move(s).Accepted);
            Assert.Contains("R-4.6", GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId)).Reason);
            Assert.Contains("R-4.6", Move(s).Reason);
        }

        [Fact]
        public void R5_23_MovingDirectly_IgnoresPatrolsAndTheMaelstrom()
        {
            var s = Game();
            At(s, "lothal");
            var events = new List<GameEvent>();
            GameEngine.MoveDirect(C, s, Current(s), "ring-of-kafrene", events);
            Assert.Equal("ring-of-kafrene", Current(s).SpaceId);
            Assert.True(Assert.IsType<PlayerMovedEvent>(events.Single()).Direct);
        }

        // ---- turn loop --------------------------------------------------------------------

        private static void ToEncounterStep(GameState s)
        {
            Assert.True(GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId)).Accepted);
            Assert.True(GameEngine.Execute(C, s, new EndActionStepCommand(s.CurrentPlayerId)).Accepted);
            Assert.Equal(TurnStep.Encounter, s.Step);
        }

        [Fact]
        public void R4_2_StepsComeInOrder()
        {
            var s = Game();
            Assert.Contains("R-4.2", GameEngine.Execute(C, s, new EndActionStepCommand(s.CurrentPlayerId)).Reason);
            Assert.Contains("R-4.2", GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Space)).Reason);
        }

        [Fact]
        public void R4_12_R4_4_TheEncounterEndsTheTurn_AndTheNextPlayerBegins()
        {
            var s = Game();
            var first = s.CurrentPlayerId;
            ToEncounterStep(s);
            var r = GameEngine.Execute(C, s, new EncounterCommand(first, EncounterKind.Space));
            Assert.True(r.Accepted);
            Assert.Contains(r.Events, e => e is EncounterResolvedEvent);
            var next = s.Players[(s.SeatOf(first) + 1) % s.Players.Count].Id;
            Assert.Equal(next, s.CurrentPlayerId);
            Assert.Equal(TurnStep.Planning, s.Step);
            Assert.Equal(1, s.Round);
        }

        [Fact]
        public void R4_1_R4_4_AfterEveryoneHasPlayed_ANewRoundStarts()
        {
            var s = Game(players: 3);
            for (int i = 0; i < 3; i++)
            {
                ToEncounterStep(s);
                GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Space));
            }
            Assert.Equal(2, s.Round);
            Assert.Equal(s.FirstPlayerId, s.CurrentPlayerId);
        }

        [Fact]
        public void R4_14_R11_26_ANegativePatrol_MustBeEncountered()
        {
            var s = Game();
            ToEncounterStep(s);
            PatrolAt(s, Faction.Hutt, Current(s).SpaceId);
            Current(s).Reputation.Set(Faction.Hutt, Reputation.Negative);
            Assert.Contains("R-4.14", GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Space)).Reason);
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Patrol, "hutt")).Accepted);
        }

        [Fact]
        public void R11_27_ANeutralPatrol_MayBeEncounteredOrNot()
        {
            var s = Game();
            ToEncounterStep(s);
            PatrolAt(s, Faction.Hutt, Current(s).SpaceId);
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Space)).Accepted);
            Assert.True(GameEngine.Execute(C, copy, new EncounterCommand(copy.CurrentPlayerId, EncounterKind.Patrol, "hutt")).Accepted);
        }

        [Fact]
        public void R11_28_WithSeveralPatrols_YouMustPickANegativeOne()
        {
            var s = Game();
            ToEncounterStep(s);
            var here = Current(s).SpaceId;
            PatrolAt(s, Faction.Hutt, here);
            PatrolAt(s, Faction.Rebel, here);
            Current(s).Reputation.Set(Faction.Rebel, Reputation.Negative);
            Assert.Contains("R-11.28", GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Patrol, "hutt")).Reason);
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Patrol, "rebel")).Accepted);
        }

        [Fact]
        public void R4_13_ThePatrol_MustBeInYourSpace()
        {
            var s = Game();
            ToEncounterStep(s);
            Assert.Contains("R-4.13", GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Patrol, "imperial")).Reason);
        }

        [Fact]
        public void SameSeedAndCommands_GiveTheSameGame_OverSeveralTurns()
        {
            string Play()
            {
                var s = Game(99, 4);
                for (int i = 0; i < 6; i++)
                {
                    GameEngine.Execute(C, s, new MoveCommand(s.CurrentPlayerId, new string[0]));
                    GameEngine.Execute(C, s, new EndActionStepCommand(s.CurrentPlayerId));
                    GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Space));
                }
                return SaveSerializer.Save(s);
            }
            Assert.Equal(Play(), Play());
        }
    }
}
