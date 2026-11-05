using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class PatrolTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private static MapGraph Map => C.Map;

        private static GameState Game(ulong seed = 51)
        {
            var s = GameEngine.NewGame(C, GameEngineTests.Setup(seed, 3), out _);
            return s;
        }

        private static PlayerState Current(GameState s)
        {
            return s.FindPlayer(s.CurrentPlayerId)!;
        }

        private static PatrolState Patrol(GameState s, Faction f)
        {
            return Patrols.Of(s, f);
        }

        // ---- setup and spawning ---------------------------------------------------------------

        [Fact]
        public void R3_7_EachFactionHasAStack_LevelTwoOnTop_LevelFourAtTheBottom()
        {
            var s = Game();
            Assert.Equal(4, s.PatrolStacks.Count);
            Assert.All(s.PatrolStacks, st => Assert.Equal(new[] { 2, 3, 4 }, st.Levels));
        }

        [Fact]
        public void R11_4_R11_5_PatrolTokens_ComeFromTheData()
        {
            var l1 = C.PatrolToken(Faction.Hutt, 1);
            Assert.Equal(3, l1.Combat);
            Assert.Equal(5000, l1.RewardCredits);
            Assert.Equal(1, C.PatrolToken(Faction.Rebel, 3).RewardFame);
            Assert.Null(C.PatrolToken(Faction.Imperial, 4).Combat);
        }

        [Fact]
        public void R11_8_R11_9_R11_10_EliminatingAPatrol_SpawnsTheNextLevel_NextToTheStack()
        {
            var s = Game();
            Patrol(s, Faction.Hutt).SpaceId = "kessel";
            var events = new List<GameEvent>();
            Patrols.EliminateAndSpawn(C, s, Faction.Hutt, events);
            Assert.Equal(2, Patrol(s, Faction.Hutt).Level);
            Assert.Equal(Map.SpawnSpace(Faction.Hutt), Patrol(s, Faction.Hutt).SpaceId);
            Assert.Equal(new[] { 3, 4 }, s.PatrolStacks.Single(st => st.Faction == Faction.Hutt).Levels);
            Assert.Single(s.Patrols, p => p.Faction == Faction.Hutt);
            Assert.Equal(4, s.Patrols.Count);
        }

        [Fact]
        public void R11_6_ALevelFourPatrol_CannotBeEliminated()
        {
            var s = Game();
            Patrol(s, Faction.Rebel).Level = 4;
            Assert.Throws<System.InvalidOperationException>(() => Patrols.EliminateAndSpawn(C, s, Faction.Rebel, new List<GameEvent>()));
        }

        // ---- movement ---------------------------------------------------------------------------

        [Fact]
        public void R11_17_R11_20_IfThePlayerIsWithinReach_ThePatrolGoesThereAndStops()
        {
            Assert.Equal(new[] { "kessel" }, Patrols.Destinations(Map, "nav-kessel-1", "kessel", 4));
        }

        [Fact]
        public void R11_21_PatrolsPassThroughTheMaelstrom()
        {
            // nav-kessel-1 -> maelstrom -> kessel: two spaces.
            Assert.Equal(new[] { "kessel" }, Patrols.Destinations(Map, "nav-kessel-1", "kessel", 2));
        }

        [Fact]
        public void R11_15_R11_18_EveryDestination_IsTheRightDistanceAway_AndNoFartherFromThePlayer()
        {
            var spaces = Map.Spaces.Select(sp => sp.Id).ToList();
            var rng = new Random.GameRandom(8);
            for (int i = 0; i < 300; i++)
            {
                var from = spaces[rng.NextInt(spaces.Count)];
                var target = spaces[rng.NextInt(spaces.Count)];
                int distance = 1 + rng.NextInt(4);
                int start = Map.Distance(from, target, throughCoreWorlds: false);
                var ends = Patrols.Destinations(Map, from, target, distance);
                Assert.NotEmpty(ends);
                if (start <= distance)
                {
                    Assert.Equal(new[] { target }, ends);
                    continue;
                }
                foreach (var end in ends)
                {
                    Assert.NotEqual(from, end);
                    Assert.InRange(Map.Distance(from, end), 1, distance); // reached in exactly `distance` steps of a simple route
                    int left = Map.Distance(end, target, throughCoreWorlds: false);
                    Assert.InRange(left, start - distance, start);
                }
            }
        }

        [Fact]
        public void R11_16_SeveralRoutes_LetTheCurrentPlayerChoose_EvenAWayThatIsNotTheShortest()
        {
            // From the left side of Kessel toward Tatooine, four spaces: both lanes and their crossings qualify.
            var ends = Patrols.Destinations(Map, "nav-kessel-1", "tatooine", 4);
            Assert.True(ends.Count > 1);
            var nearest = ends.Min(e => Map.Distance(e, "tatooine"));
            Assert.Contains(ends, e => Map.Distance(e, "tatooine") > nearest);
        }

        [Fact]
        public void R11_22_PatrolsDoNotPassThroughTheCoreWorlds()
        {
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
            Assert.Equal(new[] { "y" }, Patrols.Destinations(g, "a", "b", 2));
            Assert.Equal(new[] { "b" }, Patrols.Destinations(g, "a", "b", 3));
        }

        [Fact]
        public void R11_23_MovingAPatrolDirectly_JumpsToTheSpace()
        {
            var s = Game();
            var events = new List<GameEvent>();
            Patrols.MoveDirect(s, Faction.Imperial, "maelstrom", events);
            Assert.Equal("maelstrom", Patrol(s, Faction.Imperial).SpaceId);
            Assert.True(Assert.IsType<PatrolMovedEvent>(events.Single()).Direct);
        }

        [Fact]
        public void PatrolMove_AsksTheCurrentPlayer_WhenThereAreChoices_AndSurvivesSaveAndLoad()
        {
            var s = Game();
            Current(s).SpaceId = "tatooine";
            Patrol(s, Faction.Hutt).SpaceId = "nav-kessel-1";
            var move = new Frame(Patrols.MoveHandler);
            move.Set("faction", "hutt");
            move.Set("distance", 4);
            GameEngine.StartResolution(C, s, move);
            Assert.Equal(s.CurrentPlayerId, s.Pending!.PlayerId);
            var choice = s.Pending.Options[1].Id;
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand(s.CurrentPlayerId, s.Pending.Id, choice));
            GameEngine.Execute(C, copy, new ChooseOptionCommand(copy.CurrentPlayerId, copy.Pending!.Id, choice));
            Assert.Equal(choice, Patrol(s, Faction.Hutt).SpaceId);
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }

        [Fact]
        public void PatrolMove_WithOneDestination_NeedsNoDecision()
        {
            var s = Game();
            Current(s).SpaceId = "kessel";
            Patrol(s, Faction.Rebel).SpaceId = "nav-kessel-1";
            var move = new Frame(Patrols.MoveHandler);
            move.Set("faction", "rebel");
            move.Set("distance", 3);
            var r = GameEngine.StartResolution(C, s, move);
            Assert.Null(s.Pending);
            Assert.Equal("kessel", Patrol(s, Faction.Rebel).SpaceId);
            Assert.False(r.Events.OfType<PatrolMovedEvent>().Single().Direct);
        }

        // ---- encountering a patrol ------------------------------------------------------------------

        private static (GameState, CommandResult) Encounter(ulong seed, Faction faction, int level = 1)
        {
            var s = Game(seed);
            GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId));
            GameEngine.Execute(C, s, new EndActionStepCommand(s.CurrentPlayerId));
            var p = Patrol(s, faction);
            p.Level = level;
            p.SpaceId = Current(s).SpaceId;
            var r = TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new EncounterCommand(s.CurrentPlayerId, EncounterKind.Patrol, Patrols.Id(faction))));
            return (s, r);
        }

        [Fact]
        public void R11_25_EncounteringAPatrol_IsAShipCombat_AgainstItsLevelValue()
        {
            var (_, r) = Encounter(1, Faction.Hutt, 2);
            var start = r.Events.OfType<CombatStartedEvent>().Single();
            Assert.Equal("ship", start.Type);
            Assert.Equal("5", start.DefenderValue);
        }

        [Fact]
        public void R11_8_WinningAgainstAPatrol_Rewards_CostsReputation_AndReplacesIt()
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var (s, r) = Encounter(seed, Faction.Syndicate);
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "attacker")
                {
                    continue;
                }
                var player = s.FindPlayer(r.Events.OfType<CombatStartedEvent>().Single().AttackerId)!;
                Assert.Contains(r.Events, e => e is CreditsGainedEvent c && c.Amount == 5000);
                Assert.Equal(Reputation.Negative, player.Reputation.Syndicate);
                Assert.Equal(2, Patrol(s, Faction.Syndicate).Level);
                Assert.Equal(Map.SpawnSpace(Faction.Syndicate), Patrol(s, Faction.Syndicate).SpaceId);
                Assert.Contains(r.Events, e => e is PatrolEliminatedEvent);
                return;
            }
            Assert.Fail("No seed gave a win.");
        }

        [Fact]
        public void R11_7_LosingToAPatrol_ThePlayerPushesItToAnAdjacentSpace()
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var (s, r) = Encounter(seed, Faction.Imperial);
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "defender")
                {
                    continue;
                }
                var player = r.Events.OfType<CombatStartedEvent>().Single().AttackerId;
                var here = s.FindPlayer(player)!.SpaceId;
                Assert.Equal(player, s.Pending!.PlayerId);
                Assert.Equal(Map.Neighbours(here).ToList(), s.Pending.Options.Select(o => o.Id).ToList());
                var to = s.Pending.Options[0].Id;
                GameEngine.Execute(C, s, new ChooseOptionCommand(player, s.Pending.Id, to));
                Assert.Equal(to, Patrol(s, Faction.Imperial).SpaceId);
                Assert.Equal(1, Patrol(s, Faction.Imperial).Level);
                Assert.NotEqual(player, s.CurrentPlayerId); // the encounter ended the turn
                return;
            }
            Assert.Fail("No seed gave a loss.");
        }

        [Fact]
        public void R11_6_ALevelFourPatrol_AlwaysWins_AndDefeatsThePlayer()
        {
            var (s, r) = Encounter(3, Faction.Hutt, 4);
            Assert.Equal("defender", r.Events.OfType<CombatResolvedEvent>().Single().Winner);
            var player = s.FindPlayer(r.Events.OfType<CombatStartedEvent>().Single().AttackerId)!;
            Assert.NotNull(s.Pending); // pushing the patrol away comes before the damage (R-11.29)
            GameEngine.Execute(C, s, new ChooseOptionCommand(player.Id, s.Pending!.Id, s.Pending.Options[0].Id));
            Assert.True(player.Defeated);
            Assert.Equal(4, Patrol(s, Faction.Hutt).Level);
        }

        [Fact]
        public void R11_29_APlayerCanWin_AndBeDefeated_InTheSameCombat()
        {
            for (ulong seed = 0; seed < 400; seed++)
            {
                var s = Game(seed);
                GameEngine.Execute(C, s, new GainCreditsCommand(s.CurrentPlayerId));
                GameEngine.Execute(C, s, new EndActionStepCommand(s.CurrentPlayerId));
                var player = Current(s);
                player.ShipDamage = 2; // G9 Rigger hull 3: one more damage defeats
                Patrol(s, Faction.Rebel).SpaceId = player.SpaceId;
                var r = TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new EncounterCommand(player.Id, EncounterKind.Patrol, "rebel")));
                var result = r.Events.OfType<CombatResolvedEvent>().Single();
                if (result.Winner != "attacker" || result.DefenderDamage == 0)
                {
                    continue;
                }
                Assert.True(player.Defeated);
                Assert.Equal(2, Patrol(s, Faction.Rebel).Level); // the win still eliminated the patrol
                Assert.Contains(r.Events, e => e is CreditsGainedEvent c && c.Amount == 5000);
                return;
            }
            Assert.Fail("No seed gave a win with damage.");
        }
    }
}
