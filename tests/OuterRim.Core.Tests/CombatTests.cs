using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Winning effect of a test combat: the attacker gains 5,000 credits.</summary>
    internal sealed class RewardOnWin : IResolutionHandler
    {
        public string Id => "test.reward_on_win";

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            if (frame.Get("winner") == "attacker")
            {
                var p = ctx.State.FindPlayer(frame.Get("attacker"))!;
                p.Credits += 5000;
                ctx.Events.Add(new CreditsGainedEvent(p.Id, 5000));
            }
            return StepResult.Done();
        }
    }

    /// <summary>A winning effect that asks something, so a combat can be saved mid-way.</summary>
    internal sealed class AskOnWin : IResolutionHandler
    {
        public string Id => "test.ask_on_win";

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            if (frame.Step == 0)
            {
                frame.Step = 1;
                return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, frame.Get("attacker"), "test.loot",
                    new[] { new DecisionOption("credits", "c"), new DecisionOption("fame", "f") }, "loot"));
            }
            var p = ctx.State.FindPlayer(frame.Get("attacker"))!;
            if (frame.Get("loot") == "fame") p.Fame += 1; else p.Credits += 1000;
            return StepResult.Done();
        }
    }

    public class CombatTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00"; // ground combat 3, health 4
        private const string Lando = "base-character-01"; // ground combat 2, health 4

        public CombatTests()
        {
            ResolutionHandlers.Register(new RewardOnWin());
            ResolutionHandlers.Register(new AskOnWin());
        }

        private static GameState Game(ulong seed, int players = 2)
        {
            var list = new List<PlayerSetup>
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, Ketsu),
                new PlayerSetup("p2", "Two", "red", "kessel", TestContent.G9Rigger, Lando),
            };
            var s = GameEngine.NewGame(C, new GameSetup(seed, list.Take(players).ToList(), new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static CommandResult Fight(GameState s, string type, string kind, string defender, string value = "3", string outcome = "")
        {
            var f = new Frame(Combat.Handler);
            f.Set("type", type);
            f.Set("attacker", "p1");
            f.Set("defender_kind", kind);
            f.Set("defender", defender);
            f.Set("defender_value", value);
            f.Set("outcome", outcome);
            return GameEngine.StartResolution(C, s, f);
        }

        [Fact]
        public void R7_11_HitsDealOne_CritsDealTwo_FocusAndBlankNothing()
        {
            Assert.Equal(5, Combat.DamageOf(new[] { DieFace.Hit, DieFace.Crit, DieFace.Crit, DieFace.Focus, DieFace.Blank }));
            Assert.Equal(0, Combat.DamageOf(new[] { DieFace.Focus, DieFace.Blank }));
        }

        [Fact]
        public void R7_13_OnATie_TheAttackerWins()
        {
            Assert.True(Combat.AttackerWins(2, 2));
            Assert.True(Combat.AttackerWins(0, 0));
            Assert.False(Combat.AttackerWins(1, 2));
        }

        [Fact]
        public void R7_10_EachSideRolls_ItsCombatValue()
        {
            var ground = Fight(Game(1), "ground", "npc", "test.enemy", "4");
            var rolls = ground.Events.OfType<CombatRolledEvent>().ToList();
            Assert.Equal(3, rolls[0].Faces.Count); // Ketsu's ground combat
            Assert.Equal(4, rolls[1].Faces.Count); // the enemy's printed value

            var ship = Fight(Game(1), "ship", "npc", "test.enemy", "3");
            Assert.Equal(2, ship.Events.OfType<CombatRolledEvent>().First().Faces.Count); // G9 Rigger ship combat
        }

        [Fact]
        public void R7_13_R7_14_WinnerAndDamage_FollowTheDice_OverManySeeds()
        {
            int attackerWins = 0, defenderWins = 0;
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = Game(seed);
                var r = Fight(s, "ground", "npc", "test.enemy", "3");
                var result = r.Events.OfType<CombatResolvedEvent>().Single();
                Assert.Equal(Combat.AttackerWins(result.AttackerDamage, result.DefenderDamage) ? "attacker" : "defender", result.Winner);
                var p1 = s.FindPlayer("p1")!;
                Assert.Equal(System.Math.Min(result.DefenderDamage, 4), p1.CharacterDamage); // R-7.14 ground: the character; R-8.4 cap
                Assert.Equal(0, p1.ShipDamage);
                Assert.Equal(p1.CharacterDamage >= 4, p1.Defeated); // R-8.2
                if (result.Winner == "attacker") attackerWins++; else defenderWins++;
            }
            Assert.True(attackerWins > 0 && defenderWins > 0);
        }

        [Fact]
        public void R7_14_ShipCombat_DamagesTheShip()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                var s = Game(seed);
                var r = Fight(s, "ship", "npc", "test.enemy", "3");
                var d = r.Events.OfType<CombatResolvedEvent>().Single().DefenderDamage;
                Assert.Equal(System.Math.Min(d, 3), s.FindPlayer("p1")!.ShipDamage); // G9 Rigger hull 3
                Assert.Equal(0, s.FindPlayer("p1")!.CharacterDamage);
            }
        }

        [Fact]
        public void R7_9_TheGameRolls_ForCardEnemiesAndPatrols()
        {
            // Digital adaptation (DECISIONS §4.2): no player rolls for an enemy; players roll only their own dice.
            var r = Fight(Game(2), "ground", "npc", "test.enemy");
            Assert.Null(r.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender").RollerId);
            Assert.Equal("p1", r.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "attacker").RollerId);
            var patrol = Fight(Game(2), "ship", "patrol", "hutt", "5");
            Assert.Null(patrol.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender").RollerId);
        }

        [Fact]
        public void Rolls_AreLabelled_WithTheCharacterOrTheEnemy()
        {
            var s = Game(2);
            s.Patrols.Single(p => p.Faction == Faction.Hutt).Level = 2;
            var patrol = Fight(s, "ship", "patrol", "hutt", "5");
            var rolls = patrol.Events.OfType<CombatRolledEvent>().ToList();
            Assert.Equal("Ketsu Onyo (One)", rolls[0].RollerLabel);
            Assert.Equal("Hutt Patrol Lvl 2 (Game)", rolls[1].RollerLabel);

            var enemy = Fight(Game(2), "ship", "npc", "TIE fighters", "3");
            Assert.Equal("TIE fighters (Game)", enemy.Events.OfType<CombatRolledEvent>().Last().RollerLabel);

            var duel = Fight(Game(2), "ground", "player", "p2");
            Assert.Equal("Lando Calrissian (Two)", duel.Events.OfType<CombatRolledEvent>().Last().RollerLabel);
        }

        [Fact]
        public void R7_13_R8_15_TheWinningEffect_ResolvesBeforeDamage()
        {
            bool checkedOne = false;
            for (ulong seed = 0; seed < 100 && !checkedOne; seed++)
            {
                var s = Game(seed);
                var events = Fight(s, "ground", "npc", "test.enemy", "3", "test.reward_on_win").Events.ToList();
                var result = events.OfType<CombatResolvedEvent>().Single();
                if (result.Winner != "attacker" || result.DefenderDamage == 0)
                {
                    continue;
                }
                int reward = events.FindIndex(e => e is CreditsGainedEvent);
                int damage = events.FindIndex(e => e is DamageSufferedEvent);
                Assert.True(reward >= 0 && reward < damage);
                checkedOne = true;
            }
            Assert.True(checkedOne);
        }

        [Fact]
        public void R11_6_ALevelFourPatrol_RollsNoDice_AlwaysWins_AndDefeatsTheShip()
        {
            var s = Game(3);
            var r = Fight(s, "ship", "patrol", "imperial", Combat.Infinite, "test.reward_on_win");
            Assert.Single(r.Events.OfType<CombatRolledEvent>());
            Assert.Equal("defender", r.Events.OfType<CombatResolvedEvent>().Single().Winner);
            Assert.DoesNotContain(r.Events, e => e is CreditsGainedEvent);
            var p1 = s.FindPlayer("p1")!;
            Assert.Equal(3, p1.ShipDamage);
            Assert.True(p1.Defeated);
        }

        [Fact]
        public void R7_5_PlayerVersusPlayer_GroundOnPlanets_ShipOtherwise()
        {
            var s = Game(4);
            Assert.Equal(CombatType.Ground, Combat.BetweenPlayers(C, s.Players[0], s.Players[1]));
            s.Players[1].SpaceId = "nav-kessel-3";
            Assert.Equal(CombatType.Ship, Combat.BetweenPlayers(C, s.Players[0], s.Players[1]));
        }

        [Fact]
        public void R7_14_PlayerVersusPlayer_BothSuffer_AndTheDefenderRollsTheirOwnDice()
        {
            var s = Game(5);
            var r = Fight(s, "ground", "player", "p2");
            var rolls = r.Events.OfType<CombatRolledEvent>().ToList();
            Assert.Equal("p2", rolls[1].RollerId);
            Assert.Equal(2, rolls[1].Faces.Count); // Lando's ground combat
            var result = r.Events.OfType<CombatResolvedEvent>().Single();
            Assert.Equal(System.Math.Min(result.AttackerDamage, 4), s.FindPlayer("p2")!.CharacterDamage);
            Assert.Equal(System.Math.Min(result.DefenderDamage, 4), s.FindPlayer("p1")!.CharacterDamage);
        }

        [Fact]
        public void R8_12_ADefeatedPlayer_CannotBeAttacked()
        {
            var s = Game(6);
            s.FindPlayer("p2")!.Defeated = true;
            var r = Fight(s, "ground", "player", "p2");
            Assert.Equal("none", r.Events.OfType<CombatResolvedEvent>().Single().Winner);
            Assert.Empty(r.Events.OfType<CombatRolledEvent>());
        }

        [Fact]
        public void R8_2_R8_3_R8_4_DamageIsCapped_AndReachingTheLimitDefeats()
        {
            var s = Game(7);
            var p = s.FindPlayer("p1")!;
            Assert.Equal(3, Combat.Suffer(C, s, p, CombatType.Ground, 3));
            Assert.False(p.Defeated);
            Assert.Equal(1, Combat.Suffer(C, s, p, CombatType.Ground, 5));
            Assert.Equal(4, p.CharacterDamage);
            Assert.True(p.Defeated);

            var s2 = Game(7);
            var q = s2.FindPlayer("p1")!;
            Assert.Equal(3, Combat.Suffer(C, s2, q, CombatType.Ship, 9));
            Assert.True(q.Defeated);
        }

        [Fact]
        public void ACombat_PausedInItsWinningEffect_SurvivesSaveAndLoad()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var s = Game(seed);
                Fight(s, "ground", "npc", "test.enemy", "2", "test.ask_on_win");
                if (s.Pending == null)
                {
                    continue; // the attacker lost; try another seed
                }
                var copy = SaveSerializer.Load(SaveSerializer.Save(s));
                GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "fame"));
                GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "fame"));
                Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
                Assert.Equal(1, s.FindPlayer("p1")!.Fame);
                Assert.Empty(s.Stack);
                return;
            }
            Assert.Fail("No seed gave an attacker win.");
        }
    }
}
