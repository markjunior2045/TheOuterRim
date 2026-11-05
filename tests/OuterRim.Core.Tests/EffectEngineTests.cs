using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class EffectEngineTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private const string Ketsu = "base-character-00", Lando = "base-character-01", Jyn = "base-character-02",
            Bossk = "base-character-03", Han = "base-character-04", Ig88 = "base-character-07";

        /// <summary>Test-only cards that exercise the control nodes one at a time.</summary>
        private const string TestCards = @"{ ""format"": ""outer-rim-data/0.1"", ""pack"": ""test"", ""cards"": [
          { ""id"": ""test-planning"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""step"", ""step"": ""planning"" }, ""effects"": [ { ""op"": ""credits.gain"", ""amount"": 3000 } ] } ] },
          { ""id"": ""test-encounter"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""step"", ""step"": ""encounter"" }, ""effects"": [ { ""op"": ""fame.gain"", ""amount"": 1 } ] } ] },
          { ""id"": ""test-flow"", ""kind"": ""gear"", ""abilities"": [
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [
              { ""op"": ""to"", ""cost"": [ { ""op"": ""credits.spend"", ""amount"": 5000 } ], ""then"": [ { ""op"": ""fame.gain"", ""amount"": 1 } ] } ] },
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [
              { ""op"": ""choice"", ""options"": [ [ { ""op"": ""credits.gain"", ""amount"": 1000 } ], [ { ""op"": ""fame.gain"", ""amount"": 1 } ] ] } ] },
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [
              { ""op"": ""may"", ""effects"": [ { ""op"": ""credits.gain"", ""amount"": 2000 } ] } ] },
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [
              { ""op"": ""if"", ""cond"": { ""cond"": ""credits_at_least"", ""amount"": 100000 },
                ""then"": [ { ""op"": ""fame.gain"", ""amount"": 5 } ], ""else"": [ { ""op"": ""credits.gain"", ""amount"": 1 } ] } ] },
            { ""timing"": { ""type"": ""step"", ""step"": ""action"" }, ""effects"": [
              { ""op"": ""rep.lose"", ""faction"": ""hutt"" },
              { ""op"": ""to"", ""cost"": [ { ""op"": ""rep.lose"", ""faction"": ""hutt"" } ], ""then"": [ { ""op"": ""fame.gain"", ""amount"": 1 } ] } ] }
          ] } ] }";

        static EffectEngineTests()
        {
            C.AddCards(TestCards);
        }

        private static GameState Game(string character, string start = "kessel", ulong seed = 71, string? second = null)
        {
            var players = new List<PlayerSetup> { new PlayerSetup("p1", "One", "green", start, TestContent.G9Rigger, character) };
            if (second != null)
            {
                players.Add(new PlayerSetup("p2", "Two", "red", start, TestContent.G9Rigger, second));
            }
            var s = GameEngine.NewGame(C, new GameSetup(seed, players, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Hold(GameState s, string card, string kind, int cost = 1000)
        {
            P1(s).Assets.Add(new AssetState { CardId = card, Kind = kind, Cost = cost });
        }

        private static void ToActionStep(GameState s)
        {
            Assert.True(GameEngine.Execute(C, s, new MoveCommand("p1", new string[0])).Accepted);
        }

        private static CommandResult Use(GameState s, string card, string path)
        {
            return TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new UseAbilityCommand("p1", card, path)));
        }

        // ---- passive modifiers -----------------------------------------------------------------

        [Fact]
        public void R5_19_HanSolo_GainsTwoHyperdrive()
        {
            var s = Game(Han);
            Assert.Equal(8, GameEngine.Hyperdrive(C, s, P1(s))); // G9 Rigger 6 + 2
        }

        [Fact]
        public void R7_19_GearBonuses_Stack()
        {
            var s = Game(Ketsu);
            Assert.Equal(3, Combat.Value(C, s, P1(s), CombatType.Ground));
            Hold(s, "base-market-gear-02", "gear"); // DL-44: +1
            Hold(s, "base-market-gear-04", "gear"); // MPL-57: +2
            Assert.Equal(6, Combat.Value(C, s, P1(s), CombatType.Ground));
        }

        [Fact]
        public void HealthAndHull_Bonuses_RaiseTheDefeatLimit()
        {
            var s = Game(Ketsu);
            Hold(s, "base-market-gear-03", "gear"); // Armored Vest: +2 health
            Hold(s, "base-market-gear-13", "mod");  // Shield Upgrade: +1 hull
            Assert.Equal(6, Combat.Health(C, s, P1(s)));
            Assert.Equal(4, Combat.Hull(C, s, P1(s)));
            PlayerRules.SufferDamage(C, s, P1(s), CombatType.Ground, 5, new List<GameEvent>());
            Assert.False(P1(s).Defeated);
        }

        [Fact]
        public void ConditionalBonus_OnlyWhenTheConditionHolds()
        {
            var ketsu = Game(Ketsu); // stealth: Vibroknife gives +1
            Hold(ketsu, "base-market-gear-01", "gear");
            Assert.Equal(4, Combat.Value(C, ketsu, P1(ketsu), CombatType.Ground));
            var lando = Game(Lando); // neither stealth nor strength
            Hold(lando, "base-market-gear-01", "gear");
            Assert.Equal(2, Combat.Value(C, lando, P1(lando), CombatType.Ground));
        }

        [Fact]
        public void ScaledBonus_ArkAngel_GainsOneHyperdrivePerJob()
        {
            var s = Game(Ketsu);
            P1(s).ShipSheetId = "base-ship-heavy-duty-lifter";
            P1(s).ShipSideId = "base-ship-heavy-duty-lifter-back"; // Ark Angel
            Assert.Equal(6, GameEngine.Hyperdrive(C, s, P1(s)));
            Hold(s, "job-a", "job");
            Hold(s, "job-b", "job");
            Assert.Equal(8, GameEngine.Hyperdrive(C, s, P1(s)));
        }

        [Fact]
        public void FlippedShipAbilities_ApplyOnlyOnTheirSide()
        {
            var s = Game(Ketsu);
            P1(s).ShipSheetId = "base-ship-hwk-290-freighter";
            P1(s).ShipSideId = "base-ship-hwk-290-freighter";      // HWK-290, ship goal side
            Assert.Equal(3, Combat.Value(C, s, P1(s), CombatType.Ground));
            P1(s).ShipSideId = "base-ship-hwk-290-freighter-back"; // Moldy Crow: +1 ground combat
            Assert.Equal(4, Combat.Value(C, s, P1(s), CombatType.Ground));
        }

        // ---- step abilities ---------------------------------------------------------------------

        [Fact]
        public void R4_11_Bossk_RecoversDamage_OncePerActionStep()
        {
            var s = Game(Bossk);
            P1(s).CharacterDamage = 3;
            P1(s).ShipDamage = 2;
            ToActionStep(s);
            Assert.True(Use(s, Bossk, "sides[0].abilities[0]").Accepted);
            Assert.Equal(1, P1(s).CharacterDamage);
            Assert.Equal(0, P1(s).ShipDamage);
            Assert.Contains("R-4.11", Use(s, Bossk, "sides[0].abilities[0]").Reason);
        }

        [Fact]
        public void R4_2_AnActionAbility_OnlyInTheActionStep()
        {
            var s = Game(Bossk);
            Assert.Contains("R-4.2", Use(s, Bossk, "sides[0].abilities[0]").Reason);
        }

        [Fact]
        public void AnAssetsAbility_ShieldUpgrade_RecoversOneShipDamage()
        {
            var s = Game(Ketsu);
            Hold(s, "base-market-gear-13", "mod");
            P1(s).ShipDamage = 2;
            ToActionStep(s);
            Assert.True(Use(s, "base-market-gear-13", "abilities[1]").Accepted);
            Assert.Equal(1, P1(s).ShipDamage);
        }

        [Fact]
        public void BactaSupplies_DiscardsItself_AndIsBarredToDroids()
        {
            var s = Game(Ketsu);
            Hold(s, "base-market-cargo-07", "cargo");
            P1(s).CharacterDamage = 3;
            ToActionStep(s);
            Assert.True(Use(s, "base-market-cargo-07", "abilities[0]").Accepted);
            Assert.Equal(0, P1(s).CharacterDamage);
            Assert.Empty(P1(s).Assets);

            var droid = Game(Ig88);
            Hold(droid, "base-market-cargo-07", "cargo");
            ToActionStep(droid);
            Assert.Contains("condition", Use(droid, "base-market-cargo-07", "abilities[0]").Reason);
        }

        [Fact]
        public void R9_8_R21_4_LandosBack_PaysThenTests_AndGainsThePlanetsReputation()
        {
            int passed = 0;
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game(Lando, "lothal", seed);
                P1(s).CharacterSide = 1;
                P1(s).Credits = 5000;
                ToActionStep(s);
                var r = Use(s, Lando, "sides[1].abilities[1]");
                Assert.True(r.Accepted);
                Assert.Equal(4000, P1(s).Credits); // R-9.8: 1,000 spent (Lando moved 0 spaces: no planning credits)
                var test = r.Events.OfType<SkillTestRolledEvent>().Single();
                Assert.Equal("influence", test.Skill);
                Assert.Equal(test.Passed ? Reputation.Positive : Reputation.Neutral, P1(s).Reputation.Imperial); // Lothal is Imperial
                if (test.Passed) passed++;
            }
            Assert.True(passed > 0);

            var broke = Game(Lando, "lothal");
            P1(broke).CharacterSide = 1;
            P1(broke).Credits = 500;
            ToActionStep(broke);
            Assert.Contains("R-9.8", Use(broke, Lando, "sides[1].abilities[1]").Reason);
        }

        [Fact]
        public void R25_14_JynErso_PlacesAMarker_OnAnImperialPlanet()
        {
            var off = Game(Jyn, "kessel");
            ToActionStep(off);
            Assert.Contains("condition", Use(off, Jyn, "sides[0].abilities[1]").Reason);

            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game(Jyn, "lothal", seed);
                ToActionStep(s);
                var r = Use(s, Jyn, "sides[0].abilities[1]");
                if (r.Events.OfType<SkillTestRolledEvent>().Single().Passed)
                {
                    Assert.Single(s.PlanetMarkers);
                    Assert.Equal("lothal", s.PlanetMarkers[0].SpaceId);
                    return;
                }
                Assert.Empty(s.PlanetMarkers);
            }
            Assert.Fail("No seed passed the test.");
        }

        [Fact]
        public void R4_7_APlanningAbility_ReplacesThePlanningOption()
        {
            var s = Game(Ketsu);
            Hold(s, "test-planning", "gear");
            int before = P1(s).Credits;
            Assert.True(Use(s, "test-planning", "abilities[0]").Accepted);
            Assert.Equal(before + 3000, P1(s).Credits);
            Assert.Equal(TurnStep.Action, s.Step);
            Assert.Contains("R-4.6", GameEngine.Execute(C, s, new GainCreditsCommand("p1")).Reason);
        }

        [Fact]
        public void R4_16_AnEncounterAbility_IsTheTurnsEncounter()
        {
            var s = Game(Ketsu, second: Lando);
            Hold(s, "test-encounter", "gear");
            ToActionStep(s);
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            Assert.True(Use(s, "test-encounter", "abilities[0]").Accepted);
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal("p2", s.CurrentPlayerId);
        }

        [Fact]
        public void R4_17_NoEncounterAbility_WhenAPatrolMustBeEncountered()
        {
            var s = Game(Ketsu);
            Hold(s, "test-encounter", "gear");
            ToActionStep(s);
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            Patrols.Of(s, Faction.Hutt).SpaceId = P1(s).SpaceId;
            P1(s).Reputation.Hutt = Reputation.Negative;
            Assert.Contains("R-4.17", Use(s, "test-encounter", "abilities[0]").Reason);
        }

        [Fact]
        public void BosskBack_FightsARicherPlayerInHisSpace_AndGainsFameOnAWin()
        {
            for (ulong seed = 0; seed < 100; seed++)
            {
                var s = Game(Bossk, "kessel", seed, second: Lando);
                P1(s).CharacterSide = 1;
                s.FindPlayer("p2")!.Fame = 2;
                ToActionStep(s);
                GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
                var r = Use(s, Bossk, "sides[1].abilities[1]");
                Assert.True(r.Accepted);
                var start = r.Events.OfType<CombatStartedEvent>().Single();
                Assert.Equal("p2", start.Defender);
                Assert.Equal("ground", start.Type); // R-7.5: both on a planet
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner == "attacker")
                {
                    Assert.Equal(1, P1(s).Fame);
                    return;
                }
            }
            Assert.Fail("No seed gave a win.");
        }

        // ---- control nodes ---------------------------------------------------------------------------

        [Fact]
        public void R21_4_To_NeedsTheCostFullyPaid()
        {
            var s = Game(Ketsu);
            Hold(s, "test-flow", "gear");
            P1(s).Credits = 4000;
            ToActionStep(s);
            Use(s, "test-flow", "abilities[0]");
            Assert.Equal(0, P1(s).Fame);
            Assert.Equal(4000, P1(s).Credits);
        }

        [Fact]
        public void R21_4_LosingReputationToGainFame_FailsWhenAlreadyNegative()
        {
            var s = Game(Ketsu);
            Hold(s, "test-flow", "gear");
            ToActionStep(s);
            Use(s, "test-flow", "abilities[4]"); // lose 1 Hutt (to negative), then "lose 1 Hutt to gain 1 fame"
            Assert.Equal(Reputation.Negative, P1(s).Reputation.Hutt);
            Assert.Equal(0, P1(s).Fame);
        }

        [Fact]
        public void Choice_AsksTheOwner_AndSurvivesSaveAndLoad()
        {
            var s = Game(Ketsu);
            Hold(s, "test-flow", "gear");
            ToActionStep(s);
            Use(s, "test-flow", "abilities[1]");
            Assert.Equal(new[] { "0", "1" }, s.Pending!.Options.Select(o => o.Id));
            var copy = SaveSerializer.Load(SaveSerializer.Save(s));
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending.Id, "1"));
            GameEngine.Execute(C, copy, new ChooseOptionCommand("p1", copy.Pending!.Id, "1"));
            Assert.Equal(1, P1(s).Fame);
            Assert.Equal(SaveSerializer.Save(s), SaveSerializer.Save(copy));
        }

        [Fact]
        public void R21_2_May_LetsTheOwnerDecline()
        {
            var s = Game(Ketsu);
            Hold(s, "test-flow", "gear");
            ToActionStep(s);
            int before = P1(s).Credits;
            Use(s, "test-flow", "abilities[2]");
            GameEngine.Execute(C, s, new ChooseOptionCommand("p1", s.Pending!.Id, "no"));
            Assert.Equal(before, P1(s).Credits);
        }

        [Fact]
        public void If_TakesTheMatchingBranch()
        {
            var s = Game(Ketsu);
            Hold(s, "test-flow", "gear");
            ToActionStep(s);
            int before = P1(s).Credits;
            Use(s, "test-flow", "abilities[3]");
            Assert.Equal(before + 1, P1(s).Credits);
            Assert.Equal(0, P1(s).Fame);
        }

        // ---- coverage --------------------------------------------------------------------------------

        /// <summary>Effects and conditions the encoded cards use that are built in later subtasks.</summary>
        private static readonly Dictionary<string, string> Pending = new Dictionary<string, string>
        {
            // T2.6: encounters, contacts, jobs and databank procedures
            ["encounter.contact"] = "T2.6", ["contact.look"] = "T2.6",
            ["job.complete"] = "T2.6", ["databank.resolve"] = "T2.6",
            ["custom:wanted"] = "T2.6", ["custom:sabacc"] = "T2.6", ["custom:reward_for_fame"] = "T2.6",
            // Left over from T2.5 (market), built with T2.6
            ["market.extra_action"] = "T2.6", ["custom:stolen_jewels"] = "T2.6", ["custom:undercover_agent"] = "T2.6",
            ["custom:dejarik"] = "T2.6", ["custom:chromium_hull"] = "T2.6",
            // T2.6: encounter card sections and their embedded assets
            ["asset.gain_databank"] = "T2.6", ["bounty.deliver"] = "T2.6", ["cond:within_patrol"] = "T2.6",
            ["contact.move"] = "T2.6", 
            ["custom:look_and_exchange"] = "T2.6", 
            
            
            
            ["test.pass"] = "T2.6", 
        };

        [Fact]
        public void EveryEffectAndCondition_InTheCardData_IsImplementedOrPlanned()
        {
            var used = new SortedSet<string>();
            void Walk(JToken t)
            {
                if (t is JObject o)
                {
                    if (o["op"]?.Type == JTokenType.String) used.Add((string)o["op"]!);
                    if ((string?)o["op"] == "custom") used.Add("custom:" + (string)o["handler"]!);
                    if (o["cond"]?.Type == JTokenType.String) used.Add("cond:" + (string)o["cond"]!);
                    foreach (var p in o.Properties()) Walk(p.Value);
                }
                else if (t is JArray a)
                {
                    foreach (var x in a) Walk(x);
                }
            }
            foreach (var file in new[] { "market-gear", "market-cargo", "market-job", "market-bounty", "market-luxury", "market-ship",
                "characters", "ship-sheets", "patrols", "contacts", "player-boards", "databank-starting", "databank-cargo-check",
                "encounter-cantonica-ord-mantell", "encounter-kessel-maelstrom", "encounter-lothal-mon-calamari", "encounter-navpoint", "encounter-ring-of-kafrene-takodana", "encounter-ryloth-naboo", "encounter-tatooine-nal-hutta" })
            {
                Walk(JObject.Parse(Repo.Read("data/packs/base/cards/" + file + ".json")));
            }
            var missing = used.Where(u => Pending.ContainsKey(u) ? false
                    : u.StartsWith("cond:") ? !Conditions.Implemented.Contains(u.Substring(5))
                    : u.StartsWith("custom:") ? !EffectHandlers.CustomImplemented.Contains(u.Substring(7))
                    : !EffectHandlers.Implemented.Contains(u))
                .ToList();
            Assert.True(missing.Count == 0, "Not implemented and not planned: " + string.Join(", ", missing));
        }
    }
}
