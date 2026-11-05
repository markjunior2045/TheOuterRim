using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class RuleModifierTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00", Aphra = "base-character-05", Ig88 = "base-character-07";

        private const string TestCards = @"{ ""format"": ""outer-rim-data/0.1"", ""pack"": ""test"", ""cards"": [
          { ""id"": ""test-droid-crew"", ""kind"": ""crew"", ""droid"": true, ""abilities"": [] },
          { ""id"": ""test-human-crew"", ""kind"": ""crew"", ""abilities"": [] } ] }";

        static RuleModifierTests()
        {
            C.AddCards(TestCards);
        }

        private static GameState Game(string character, ulong seed = 81, string? second = null)
        {
            var players = new List<PlayerSetup> { new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character) };
            if (second != null) players.Add(new PlayerSetup("p2", "Two", "red", "kessel", TestContent.G9Rigger, second));
            var s = GameEngine.NewGame(C, new GameSetup(seed, players, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static CommandResult Gain(GameState s, string card, string kind, string traits = "")
        {
            var f = new Frame(Assets.GainHandler);
            f.Set("player", "p1");
            f.Set("card", card);
            f.Set("kind", kind);
            f.Set("cost", 1000);
            f.Set("traits", traits);
            return GameEngine.StartResolution(C, s, f);
        }

        // ---- before a skill test --------------------------------------------------------------------

        [Fact]
        public void R6_8_DoctorAphra_GainsASkillForTheTest_OncePerTurn()
        {
            var s = Game(Aphra);
            var test = new Frame(SkillTests.Handler);
            test.Set("player", "p1");
            test.Set("skills", "strength"); // Aphra has knowledge, stealth, tech
            GameEngine.StartResolution(C, s, test);
            Assert.Equal("rules.use_ability", s.Pending!.Prompt);
            var r = TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "yes")));
            Assert.Equal(1, r.Events.OfType<SkillTestRolledEvent>().Single().Instances);

            var again = new Frame(SkillTests.Handler);
            again.Set("player", "p1");
            again.Set("skills", "strength");
            var second = TestHelpers.PassDiceWindows(C, s, GameEngine.StartResolution(C, s, again));
            Assert.Equal(0, second.Events.OfType<SkillTestRolledEvent>().Single().Instances);
        }

        // ---- taking an asset -------------------------------------------------------------------------

        [Fact]
        public void IG88_TakesADroidCrew_FromAPlayerInHisSpace()
        {
            var s = Game(Ig88, second: Ketsu);
            P(s, "p2").Assets.Add(new AssetState { CardId = "test-human-crew", Kind = "crew" });
            P(s, "p2").Assets.Add(new AssetState { CardId = "test-droid-crew", Kind = "crew" });
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            Assert.True(GameEngine.Execute(C, s, new UseAbilityCommand("p1", Ig88, "sides[0].abilities[1]")).Accepted);
            Assert.Equal(new[] { "test-droid-crew" }, P(s, "p1").Assets.Select(a => a.CardId));
            Assert.Equal(new[] { "test-human-crew" }, P(s, "p2").Assets.Select(a => a.CardId));
        }

        [Fact]
        public void IG88_TakesNothing_FromAPlayerElsewhere()
        {
            var s = Game(Ig88, second: Ketsu);
            P(s, "p2").SpaceId = "tatooine";
            P(s, "p2").Assets.Add(new AssetState { CardId = "test-droid-crew", Kind = "crew" });
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new UseAbilityCommand("p1", Ig88, "sides[0].abilities[1]"));
            Assert.Empty(P(s, "p1").Assets);
        }

        // ---- slots and limits --------------------------------------------------------------------------

        [Fact]
        public void LimitOneArmor_ASecondArmor_ReplacesTheFirstOrIsDeclined()
        {
            var s = Game(Ketsu);
            Gain(s, "base-market-gear-03", "gear", "armor"); // Armored Vest
            Assert.Null(s.Pending);
            Gain(s, "base-market-gear-09", "gear", "armor"); // Plastoid Armor: a free gear slot, but the limit applies
            Assert.Equal(new[] { "base-market-gear-03", "decline" }, s.Pending!.Options.Select(o => o.Id));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "base-market-gear-03"));
            Assert.Equal(new[] { "base-market-gear-09" }, P(s, "p1").Assets.Select(a => a.CardId));
        }

        [Fact]
        public void SmugglingCompartment_TakesACargoOrModSlot_AndHoldsOneMoreCargo()
        {
            var s = Game(Ketsu);
            var p = P(s, "p1");
            // The G9 Rigger has one cargo, one mod and one crew slot.
            var twoCargo = new List<AssetState> { new AssetState { CardId = "c1", Kind = "cargo" }, new AssetState { CardId = "c2", Kind = "cargo" } };
            Assert.False(Assets.Valid(C, p, twoCargo));
            var withCompartment = new List<AssetState>(twoCargo) { new AssetState { CardId = "base-market-cargo-08", Kind = "mod" } };
            Assert.True(Assets.Valid(C, p, withCompartment));
            withCompartment.Add(new AssetState { CardId = "m1", Kind = "mod" });
            Assert.False(Assets.Valid(C, p, withCompartment)); // the compartment uses the mod slot or the cargo slot
        }

        // ---- rule changes ------------------------------------------------------------------------------------

        [Fact]
        public void IG2000_WinningAgainstAPatrol_CostsNoReputation()
        {
            for (ulong seed = 0; seed < 300; seed++)
            {
                var s = Game(Ketsu, seed);
                var p = P(s, "p1");
                p.ShipSheetId = "base-ship-aggressor-class-assault-fighter";
                p.ShipSideId = "base-ship-aggressor-class-assault-fighter"; // IG-2000 (flipped side)
                GameEngine.Execute(C, s, new GainCreditsCommand("p1"));
                GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
                Patrols.Of(s, Faction.Hutt).SpaceId = p.SpaceId;
                var r = TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Patrol, "hutt")));
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "attacker") continue;
                Assert.Equal(Reputation.Neutral, p.Reputation.Hutt);
                Assert.Equal(2, Patrols.Of(s, Faction.Hutt).Level);
                return;
            }
            Assert.Fail("No seed gave a win.");
        }

        [Fact]
        public void Valorous_IsNotForcedToEncounterANegativePatrol()
        {
            var s = Game(Ketsu);
            var p = P(s, "p1");
            p.ShipSheetId = "base-ship-gx1-short-hauler";
            p.ShipSideId = "base-ship-gx1-short-hauler-back"; // Valorous
            GameEngine.Execute(C, s, new GainCreditsCommand("p1"));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            Patrols.Of(s, Faction.Hutt).SpaceId = p.SpaceId;
            p.Reputation.Hutt = Reputation.Negative;
            Assert.True(GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space)).Accepted);
        }
    }
}
