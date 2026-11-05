using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class DiceWindowTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00", Lando = "base-character-01", Jyn = "base-character-02", Ig88 = "base-character-07";

        private const string TestCards = @"{ ""format"": ""outer-rim-data/0.1"", ""pack"": ""test"", ""cards"": [
          { ""id"": ""test-add-hit"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""window"", ""window"": ""dice_modify"", ""scope"": [""ground_combat""] }, ""optional"": true,
              ""effects"": [ { ""op"": ""dice.add"", ""face"": ""hit"", ""count"": 1 } ] } ] },
          { ""id"": ""test-reroll-all"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""window"", ""window"": ""dice_modify"", ""scope"": [""ground_combat""] }, ""optional"": true,
              ""effects"": [ { ""op"": ""dice.reroll"", ""count"": { ""any"": {} } } ] } ] } ] }";

        static DiceWindowTests()
        {
            C.AddCards(TestCards);
        }

        private static GameState Game(string character, ulong seed, string? second = null)
        {
            var players = new List<PlayerSetup> { new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character) };
            if (second != null) players.Add(new PlayerSetup("p2", "Two", "red", "kessel", TestContent.G9Rigger, second));
            var s = GameEngine.NewGame(C, new GameSetup(seed, players, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static void Hold(GameState s, string card, string kind)
        {
            s.FindPlayer("p1")!.Assets.Add(new AssetState { CardId = card, Kind = kind, Cost = 1000 });
        }

        private static CommandResult Fight(GameState s, string type, int enemyValue)
        {
            var f = new Frame(Combat.Handler);
            f.Set("type", type);
            f.Set("attacker", "p1");
            f.Set("defender_kind", "npc");
            f.Set("defender", "test enemy");
            f.Set("defender_value", enemyValue);
            f.Set("outcome", "");
            return GameEngine.StartResolution(C, s, f);
        }

        private static CommandResult Answer(GameState s, string option)
        {
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending!.PlayerId, s.Pending.Id, option));
        }

        private static string Ability(GameState s) => s.Pending!.Options.First(o => o.Id != "pass").Id;

        private static string Faces(GameState s, string var)
        {
            var window = s.Stack.Last(f => f.Handler == "rules.dice_window");
            return s.Stack[window.GetInt("target")].Get(var);
        }

        // ---- the pool ---------------------------------------------------------------------------------

        [Fact]
        public void R7_15_ACancelledResult_DoesNotCount()
        {
            var dice = DicePool.Decode("h,cx,c,fa");
            Assert.Equal(new[] { DieFace.Hit, DieFace.Crit, DieFace.Focus }, DicePool.Effective(dice));
            Assert.Equal(3, Combat.DamageOf(DicePool.Effective(dice)));
            Assert.Equal("h,cx,c,fa", DicePool.Encode(dice));
        }

        // ---- optional own-dice abilities ----------------------------------------------------------------

        [Fact]
        public void Vibroknife_TurnsAFocusIntoAHit_InGroundCombat()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = Game(Ketsu, seed);
                Hold(s, "base-market-gear-01", "gear");
                Fight(s, "ground", 1);
                if (s.Pending == null) continue; // no focus rolled: the knife is not offered
                Assert.Equal("rules.dice_window", s.Pending.Prompt);
                var before = DicePool.Decode(Faces(s, "attacker_faces"));
                if (!before.Any(d => d.Face == DieFace.Focus)) continue;
                var r = Answer(s, Ability(s));
                if (s.Pending?.Prompt == "effects.choose_die") r = Answer(s, s.Pending.Options[0].Id);
                var after = DicePool.Decode(r.Events.OfType<DiceModifiedEvent>().Single().After);
                Assert.Equal(before.Count(d => d.Face == DieFace.Focus) - 1, after.Count(d => d.Face == DieFace.Focus));
                Assert.Equal(before.Count(d => d.Face == DieFace.Hit) + 1, after.Count(d => d.Face == DieFace.Hit));
                Assert.Single(after, d => d.Turned);
                return;
            }
            Assert.Fail("No seed rolled a focus.");
        }

        [Fact]
        public void R7_10_TargetingComputer_RerollsOnlyBlanks_OncePerCombat()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = Game(Ketsu, seed);
                s.FindPlayer("p1")!.ShipSideId = "base-ship-g9-rigger-back"; // G-1A: ship combat 3
                Hold(s, "base-market-gear-07", "mod");
                Fight(s, "ship", 1);
                if (s.Pending?.Prompt != "rules.dice_window") continue; // no blank: nothing to offer
                var before = DicePool.Decode(Faces(s, "attacker_faces"));
                if (before.Count(d => d.Face == DieFace.Blank) < 1) { continue; }
                Answer(s, Ability(s));
                Assert.Equal("effects.choose_die", s.Pending!.Prompt);
                Assert.All(s.Pending.Options.Where(o => o.Id != "done"), o => Assert.Equal(DieFace.Blank, before[int.Parse(o.Id)].Face));
                Answer(s, s.Pending.Options[0].Id);
                if (s.Pending?.Prompt == "effects.choose_die") Answer(s, "done");
                // Back in the attacker's window: the computer is spent for this combat; the defender's window offers nothing either.
                Assert.True(s.Pending == null || s.Pending.Options.All(o => o.Id == "pass" || !o.Label.Contains("gear-07")));
                return;
            }
            Assert.Fail("No seed rolled a blank.");
        }

        [Fact]
        public void R7_17_AnAddedResult_CannotBeRerolled()
        {
            var s = Game(Ketsu, 5);
            Hold(s, "test-add-hit", "gear");
            Hold(s, "test-reroll-all", "gear");
            Fight(s, "ground", 1);
            Answer(s, "test-add-hit#abilities[0]");
            var withAdded = DicePool.Decode(Faces(s, "attacker_faces"));
            Assert.True(withAdded.Last().Added);
            Answer(s, "test-reroll-all#abilities[0]");
            Assert.DoesNotContain(s.Pending!.Options, o => o.Id == (withAdded.Count - 1).ToString());
        }

        [Fact]
        public void JynErso_TurnsAFocusIntoACrit_OncePerTurn()
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var s = Game(Jyn, seed);
                var test = new Frame(SkillTests.Handler);
                test.Set("player", "p1");
                test.Set("skills", "tech");
                GameEngine.StartResolution(C, s, test);
                if (s.Pending == null) { continue; } // no focus rolled: nothing to offer
                var before = DicePool.Decode(Faces(s, "faces"));
                Assert.Contains(before, d => d.Face == DieFace.Focus);
                var r = Answer(s, "base-character-02#sides[0].abilities[0]");
                if (s.Pending?.Prompt == "effects.choose_die") r = Answer(s, s.Pending.Options[0].Id);
                var rolled = r.Events.OfType<SkillTestRolledEvent>().Single();
                Assert.True(rolled.Passed); // a crit passes even unskilled (R-6.9)

                var again = new Frame(SkillTests.Handler);
                again.Set("player", "p1");
                again.Set("skills", "tech");
                GameEngine.StartResolution(C, s, again);
                Assert.Null(s.Pending); // once per turn: no second offer
                return;
            }
            Assert.Fail("No seed rolled a focus.");
        }

        [Fact]
        public void Lando_MayRerollOneDie_AfterRollingForATest()
        {
            for (ulong seed = 0; seed < 50; seed++)
            {
                var s = Game(Lando, seed);
                var test = new Frame(SkillTests.Handler);
                test.Set("player", "p1");
                test.Set("skills", "stealth");
                GameEngine.StartResolution(C, s, test);
                Assert.Equal("rules.dice_window", s.Pending!.Prompt);
                Answer(s, "base-character-01#sides[0].abilities[0]");
                Assert.Equal("effects.choose_die", s.Pending!.Prompt);
                var r = Answer(s, "1");
                Assert.Single(r.Events.OfType<SkillTestRolledEvent>());
                if (!r.Events.Any(e => e is DiceModifiedEvent)) continue; // rerolled the same face: no change to report
                Assert.Contains(r.Events, e => e is DiceModifiedEvent m && m.Op == "dice.reroll");
                return;
            }
            Assert.Fail("No seed changed the rerolled die.");
        }

        // ---- mandatory abilities on the opponent's dice ---------------------------------------------------

        [Fact]
        public void IonCannon_CancelsOneEnemyHit_WithoutAsking()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = Game(Ketsu, seed);
                Hold(s, "base-market-gear-06", "mod");
                var r = TestHelpers.PassDiceWindows(C, s, Fight(s, "ship", 4));
                var enemy = r.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender");
                var mod = r.Events.OfType<DiceModifiedEvent>().SingleOrDefault(e => e.CardId == "base-market-gear-06");
                if (mod == null) continue; // no hit to cancel
                var before = DicePool.Decode(mod.Before);
                var after = DicePool.Decode(mod.After);
                Assert.Single(after, d => d.Canceled);
                Assert.Equal(DieFace.Hit, after.First(d => d.Canceled).Face);
                Assert.Equal(Combat.DamageOf(DicePool.Effective(before)) - 1, enemy.Damage);
                return;
            }
            Assert.Fail("No seed rolled a hit for the enemy.");
        }

        [Fact]
        public void IonCannon_WithTactics_OffersHitOrCrit()
        {
            for (ulong seed = 0; seed < 200; seed++)
            {
                var s = Game(Jyn, seed); // Jyn has Tactics
                Hold(s, "base-market-gear-06", "mod");
                Fight(s, "ship", 4);
                while (s.Pending != null && s.Pending.Prompt == "rules.dice_window") Answer(s, "pass");
                if (s.Pending?.Prompt == "effects.choice")
                {
                    Assert.Equal(new[] { "0", "1" }, s.Pending.Options.Select(o => o.Id));
                    return;
                }
            }
            Assert.Fail("The choice never came up.");
        }

        // ---- fewer dice -----------------------------------------------------------------------------------

        [Fact]
        public void PlastoidArmor_WithStrength_TheEnemyRollsOneFewerDie()
        {
            var ketsu = Game(Ketsu, 9); // Strength
            Hold(ketsu, "base-market-gear-09", "gear");
            var r = TestHelpers.PassDiceWindows(C, ketsu, Fight(ketsu, "ground", 4));
            Assert.Equal(3, r.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender").Faces.Count);

            var lando = Game(Lando, 9); // no Strength
            Hold(lando, "base-market-gear-09", "gear");
            var l = TestHelpers.PassDiceWindows(C, lando, Fight(lando, "ground", 4));
            Assert.Equal(4, l.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender").Faces.Count);
        }

        [Fact]
        public void ManeuveringThrusters_WithTactics_TheEnemyRollsOneFewerDie_InShipCombat()
        {
            var s = Game(Jyn, 9);
            Hold(s, "base-market-gear-12", "mod");
            var r = TestHelpers.PassDiceWindows(C, s, Fight(s, "ship", 4));
            Assert.Equal(3, r.Events.OfType<CombatRolledEvent>().Single(e => e.Side == "defender").Faces.Count);
        }

        [Fact]
        public void APausedDiceWindow_SurvivesSaveAndLoad()
        {
            var s = Game(Lando, 3);
            var test = new Frame(SkillTests.Handler);
            test.Set("player", "p1");
            test.Set("skills", "stealth");
            GameEngine.StartResolution(C, s, test);
            Answer(s, "base-character-01#sides[0].abilities[0]");
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            Answer(s, "0");
            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "0"));
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }
    }
}
