using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class TriggerTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00", Ig88 = "base-character-07";
        private const string HoundsTooth = "base-ship-yv-666-light-freighter-back"; // hull 5

        private const string TestCards = @"{ ""format"": ""outer-rim-data/0.1"", ""pack"": ""test"", ""cards"": [
          { ""id"": ""test-win-a"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""trigger"", ""event"": ""combat_won"" }, ""effects"": [ { ""op"": ""credits.gain"", ""amount"": 100 } ] } ] },
          { ""id"": ""test-win-b"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""trigger"", ""event"": ""combat_won"" }, ""effects"": [ { ""op"": ""fame.gain"", ""amount"": 1 } ] } ] },
          { ""id"": ""test-hurt-both"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [ { ""op"": ""damage.suffer"", ""target"": ""both"", ""amount"": 1 } ] } ] } ] }";

        static TriggerTests()
        {
            C.AddCards(TestCards);
        }

        private static GameState Game(string character, ulong seed)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[] { new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character) },
                new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Hold(GameState s, string card, string kind)
        {
            P1(s).Assets.Add(new AssetState { CardId = card, Kind = kind, Cost = 1000 });
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
            return TestHelpers.PassDiceWindows(C, s, GameEngine.StartResolution(C, s, f));
        }

        [Fact]
        public void KetsuBack_AfterWinningACombat_Gains4000()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var s = Game(Ketsu, seed);
                P1(s).CharacterSide = 1;
                int before = P1(s).Credits;
                var r = Fight(s, "ground", 1);
                var won = r.Events.OfType<CombatResolvedEvent>().Single().Winner == "attacker";
                Assert.Equal(before + (won ? 4000 : 0), P1(s).Credits);
                if (won)
                {
                    // The trigger runs after damage: the reward event comes after the combat's damage.
                    int reward = r.Events.ToList().FindLastIndex(e => e is CreditsGainedEvent);
                    int resolved = r.Events.ToList().FindIndex(e => e is CombatResolvedEvent);
                    Assert.True(reward > resolved);
                    return;
                }
            }
            Assert.Fail("No seed gave a win.");
        }

        [Fact]
        public void IQA11_WinningAGroundCombat_CapsTheDamageAtTwo()
        {
            for (ulong seed = 0; seed < 400; seed++)
            {
                var s = Game(Ketsu, seed);
                Hold(s, "base-market-gear-00", "gear"); // +1 ground combat, damage cap on a win
                var r = Fight(s, "ground", 6);
                var result = r.Events.OfType<CombatResolvedEvent>().Single();
                if (result.Winner == "attacker" && result.DefenderDamage >= 3)
                {
                    Assert.Equal(2, P1(s).CharacterDamage);
                    return;
                }
                if (result.Winner == "defender")
                {
                    Assert.Equal(System.Math.Min(result.DefenderDamage, 4), P1(s).CharacterDamage); // no cap when losing
                }
            }
            Assert.Fail("No seed gave a win with 3+ damage taken.");
        }

        [Fact]
        public void IG88Back_WouldBeDefeated_DiscardsACrew_ToRecoverAllDamageInstead()
        {
            var s = Game(Ig88, 1);
            P1(s).CharacterSide = 1;
            Hold(s, "some-crew", "crew");
            P1(s).CharacterDamage = 3;
            var hurt = new Frame("rules.suffer_damage");
            hurt.Set("player", "p1");
            hurt.Set("target", "ground");
            hurt.Set("amount", 2);
            GameEngine.StartResolution(C, s, hurt);
            Assert.Equal("rules.would_be_defeated", s.Pending!.Prompt);
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, s.Pending.Options[0].Id));
            Assert.False(P1(s).Defeated);
            Assert.Equal(0, P1(s).CharacterDamage);
            Assert.Empty(P1(s).Assets);

            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "decline"));
            Assert.True(copy.FindPlayer("p1")!.Defeated);
        }

        [Fact]
        public void IG88Back_IsNotOffered_WithoutACrewToDiscard()
        {
            var s = Game(Ig88, 1);
            P1(s).CharacterSide = 1;
            P1(s).CharacterDamage = 3;
            var hurt = new Frame("rules.suffer_damage");
            hurt.Set("player", "p1");
            hurt.Set("target", "ground");
            hurt.Set("amount", 2);
            GameEngine.StartResolution(C, s, hurt);
            Assert.Null(s.Pending);
            Assert.True(P1(s).Defeated);
        }

        [Fact]
        public void HoundsTooth_WouldBeDefeatedInShipCombat_RollsToRecoverTwo()
        {
            int saved = 0, lost = 0;
            for (ulong seed = 0; seed < 60; seed++)
            {
                var s = Game(Ketsu, seed);
                P1(s).ShipSheetId = "base-ship-yv-666-light-freighter";
                P1(s).ShipSideId = HoundsTooth;
                P1(s).ShipDamage = 4;
                var r = Fight(s, "ship", 6);
                var result = r.Events.OfType<CombatResolvedEvent>().Single();
                if (result.DefenderDamage == 0) continue;
                var roll = r.Events.OfType<EffectDiceRolledEvent>().Single();
                bool recovers = roll.Faces.Any(f => f == DieFace.Focus || f == DieFace.Hit);
                Assert.Equal(!recovers, P1(s).Defeated);
                Assert.Equal(recovers ? 3 : 5, P1(s).ShipDamage);
                if (recovers) saved++; else lost++;
            }
            Assert.True(saved > 0 && lost > 0);
        }

        [Fact]
        public void HoundsTooth_DoesNotHelp_OutsideShipCombat()
        {
            var s = Game(Ketsu, 2);
            P1(s).ShipSheetId = "base-ship-yv-666-light-freighter";
            P1(s).ShipSideId = HoundsTooth;
            P1(s).CharacterDamage = 3;
            var r = Fight(s, "ground", 6);
            if (r.Events.OfType<CombatResolvedEvent>().Single().DefenderDamage > 0)
            {
                Assert.Empty(r.Events.OfType<EffectDiceRolledEvent>());
                Assert.True(P1(s).Defeated);
            }
        }

        [Fact]
        public void R21_10_SimultaneousTriggers_AreOrderedByTheCurrentPlayer()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var s = Game(Ketsu, seed);
                Hold(s, "test-win-a", "gear");
                Hold(s, "test-win-b", "gear");
                Fight(s, "ground", 1);
                if (s.Pending == null) continue; // lost: no trigger
                Assert.Equal(DecisionKind.ChooseOrder, s.Pending.Kind);
                GameEngine.Execute(C, s, new ChooseOrderCommand("p1", s.Pending.Id, new[] { "test-win-b#abilities[0]", "test-win-a#abilities[0]" }));
                Assert.Equal(1, P1(s).Fame);
                Assert.Null(s.Pending);
                return;
            }
            Assert.Fail("No seed gave a win.");
        }

        [Fact]
        public void DamageToBoth_HitsCharacterAndShip()
        {
            var s = Game(Ketsu, 3);
            Hold(s, "test-hurt-both", "gear");
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new UseAbilityCommand("p1", "test-hurt-both", "abilities[0]"));
            Assert.Equal(1, P1(s).CharacterDamage);
            Assert.Equal(1, P1(s).ShipDamage);
        }
    }
}
