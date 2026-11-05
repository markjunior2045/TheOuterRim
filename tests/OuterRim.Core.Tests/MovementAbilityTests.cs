using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class MovementAbilityTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string Ketsu = "base-character-00", Jyn = "base-character-02", Han = "base-character-04";

        private static GameState Game(string character, ulong seed = 91, string? second = null, string secondSpace = "kessel")
        {
            var players = new List<PlayerSetup> { new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character) };
            if (second != null) players.Add(new PlayerSetup("p2", "Two", "red", secondSpace, TestContent.G9Rigger, second));
            var s = GameEngine.NewGame(C, new GameSetup(seed, players, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static CommandResult Answer(GameState s, string option)
        {
            return TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending!.PlayerId, s.Pending.Id, option)));
        }

        // ---- Han Solo (back) ------------------------------------------------------------------------

        [Fact]
        public void HanSoloBack_PassesPiloting_ToMoveOnThroughAPatrol()
        {
            int through = 0, stopped = 0;
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game(Han, seed);
                P(s, "p1").CharacterSide = 1;
                Patrols.Of(s, Faction.Imperial).SpaceId = "nav-kessel-3";
                Assert.True(GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" })).Accepted);
                Assert.Equal("rules.use_ability", s.Pending!.Prompt);
                var r = Answer(s, "yes");
                bool passed = r.Events.OfType<SkillTestRolledEvent>().Single().Passed;
                Assert.Equal(passed ? "nav-kessel-5" : "nav-kessel-3", P(s, "p1").SpaceId);
                Assert.Equal(TurnStep.Action, s.Step);
                if (passed) through++; else stopped++;
            }
            Assert.True(through > 0 && stopped > 0);
        }

        [Fact]
        public void HanSoloBack_NotUsingTheAbility_StopsAtThePatrol()
        {
            var s = Game(Han);
            P(s, "p1").CharacterSide = 1;
            Patrols.Of(s, Faction.Imperial).SpaceId = "nav-kessel-3";
            GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" }));
            var r = Answer(s, "no");
            Assert.Equal("nav-kessel-3", P(s, "p1").SpaceId);
            Assert.Equal(new[] { "nav-kessel-3" }, r.Events.OfType<PlayerMovedEvent>().Single().Path);
        }

        [Fact]
        public void R5_20_WithoutSuchAnAbility_ThePathIsRejected()
        {
            var s = Game(Han); // front side: no movement ability
            Patrols.Of(s, Faction.Imperial).SpaceId = "nav-kessel-3";
            Assert.Contains("R-5.20", GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" })).Reason);
        }

        [Fact]
        public void AMovementPausedForADecision_SurvivesSaveAndLoad()
        {
            var s = Game(Han, 7);
            P(s, "p1").CharacterSide = 1;
            Patrols.Of(s, Faction.Imperial).SpaceId = "nav-kessel-3";
            GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" }));
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            Answer(s, "yes");
            Answer(copy, "yes");
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }

        // ---- Tractor Beam (Shadow Caster) ----------------------------------------------------------------

        private static GameState TractorGame()
        {
            var s = Game(Ketsu, second: Ketsu, secondSpace: "kessel");
            var p2 = P(s, "p2");
            p2.SpaceId = "nav-kessel-3";
            p2.ShipSheetId = "base-ship-lancer-class-pursuit-craft";
            p2.ShipSideId = "base-ship-lancer-class-pursuit-craft-back"; // Shadow Caster
            return s;
        }

        [Fact]
        public void TractorBeam_TheOwnerMayForceTheMoverToStop()
        {
            var s = TractorGame();
            GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" }));
            Assert.Equal("p2", s.Pending!.PlayerId); // the decision belongs to the Shadow Caster's owner
            Answer(s, "yes");
            Assert.Equal("nav-kessel-3", P(s, "p1").SpaceId);
        }

        [Fact]
        public void TractorBeam_Declined_TheMoverGoesOn()
        {
            var s = TractorGame();
            GameEngine.Execute(C, s, new MoveCommand("p1", new[] { "nav-kessel-3", "nav-kessel-5" }));
            Answer(s, "no");
            Assert.Equal("nav-kessel-5", P(s, "p1").SpaceId);
        }

        // ---- Jyn Erso (back): redirecting a patrol ------------------------------------------------------------

        private static CommandResult MovePatrol(GameState s, string faction, int distance)
        {
            var f = new Frame(Patrols.MoveHandler);
            f.Set("faction", faction);
            f.Set("distance", distance);
            return GameEngine.StartResolution(C, s, f);
        }

        [Fact]
        public void JynErsoBack_SendsANearbyPatrol_TowardAnotherPlayer()
        {
            var s = Game(Jyn, second: Ketsu, secondSpace: "tatooine");
            P(s, "p1").CharacterSide = 1;
            Patrols.Of(s, Faction.Hutt).SpaceId = "nav-kessel-1"; // two spaces from Kessel
            MovePatrol(s, "hutt", 2);
            Assert.Equal("rules.patrol_redirect", s.Pending!.Prompt);
            Assert.Equal(new[] { "p1", "p2" }, s.Pending.Options.Select(o => o.Id));
            Answer(s, "p2");
            if (s.Pending != null) Answer(s, s.Pending.Options[0].Id); // several destinations: pick one
            var hutt = Patrols.Of(s, Faction.Hutt).SpaceId;
            Assert.True(C.Map.Distance(hutt, "tatooine") < C.Map.Distance("nav-kessel-1", "tatooine"));
        }

        [Fact]
        public void JynErsoBack_DoesNotApply_ToAFarPatrol()
        {
            var s = Game(Jyn, second: Ketsu, secondSpace: "tatooine");
            P(s, "p1").CharacterSide = 1;
            Patrols.Of(s, Faction.Hutt).SpaceId = "nav-takodana-6";
            MovePatrol(s, "hutt", 2);
            Assert.True(s.Pending == null || s.Pending.Prompt != "rules.patrol_redirect");
        }
    }
}
