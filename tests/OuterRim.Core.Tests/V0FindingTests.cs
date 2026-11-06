using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>
    /// Rules and cards the V0 games (PLAN T3.1) found broken or never triggered, each with the fix's test:
    /// Ketsu Onyo's range, "at the start of combat", "when you lose a combat", "when you become defeated",
    /// "at the end of your action step", Lobot, Chromium Hull, effects that last a while, a databank crew
    /// declined, and the limits that hold at all times (R-8.4, R-14.18).
    /// </summary>
    public class V0FindingTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private const string Ketsu = "base-character-00";

        private static GameState Game(string space = "kessel", ulong seed = 311, string character = Ketsu)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            P(s, "p1").SpaceId = space;
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            return s;
        }

        private static PlayerState P(GameState s, string id) => s.FindPlayer(id)!;

        private static void Give(GameState s, string card, string kind, string player = "p1")
        {
            P(s, player).Assets.Add(new AssetState { CardId = card, Kind = kind });
            foreach (var d in s.MarketDecks) d.Cards.Remove(card);
        }

        private static CommandResult Answer(GameState s, string option)
        {
            Assert.NotNull(s.Pending);
            Assert.Contains(s.Pending!.Options, o => o.Id == option);
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, option));
        }

        private static void ToEncounter(GameState s)
        {
            Assert.True(GameEngine.Execute(C, s, new MoveCommand("p1", new string[0])).Accepted);
            Assert.True(GameEngine.Execute(C, s, new EndActionStepCommand("p1")).Accepted);
        }

        private static Frame GroundCombat(int enemy)
        {
            var combat = new Frame(Combat.Handler);
            combat.Set("type", "ground");
            combat.Set("attacker", "p1");
            combat.Set("defender_kind", "npc");
            combat.Set("defender", "test.enemy");
            combat.Set("defender_value", enemy);
            return combat;
        }

        // ---- Ketsu Onyo: "a patrol or contact token that is within 3 spaces of you" -------------------

        [Fact]
        public void R5_29_Ketsu_EncountersAPatrolUpTo3SpacesAway()
        {
            var s = Game("kessel");
            Patrols.Of(s, Faction.Hutt).SpaceId = "nav-kessel-1"; // 2 spaces from Kessel
            ToEncounter(s);
            Assert.True(GameEngine.Execute(C, s, new UseAbilityCommand("p1", Ketsu, "sides[0].abilities[0]")).Accepted);
            var r = TestHelpers.DeclineAll(C, s, Answer(s, "0"));
            var combat = r.Events.OfType<CombatStartedEvent>().Single();
            Assert.Equal("patrol", combat.DefenderKind);
            Assert.Equal("hutt", combat.Defender);
        }

        [Fact]
        public void R5_29_Ketsu_EncountersAContactFromANavpoint_UpTo3SpacesAway()
        {
            var s = Game("nav-kessel-3"); // next to Kessel, whose contact tokens are 1 space away
            ToEncounter(s);
            Assert.True(GameEngine.Execute(C, s, new UseAbilityCommand("p1", Ketsu, "sides[0].abilities[0]")).Accepted);
            var r = Answer(s, "1");
            Assert.True(r.Accepted, r.Reason);
            var kessel = s.Contacts.Where(t => t.OnMap && t.SpaceId == "kessel").Select(t => t.Id).ToList();
            Assert.NotEmpty(kessel);
            Assert.Equal("rules.choose_contact", s.Pending!.Prompt);
            Assert.All(s.Pending.Options, o => Assert.Contains(o.Id, s.Contacts.Where(t => t.OnMap
                && C.Map.Distance("nav-kessel-3", t.SpaceId) <= 3).Select(t => t.Id)));
            Assert.Contains(s.Pending.Options, o => kessel.Contains(o.Id));
        }

        // ---- start of combat (R-7.18) and effects "for that combat" -----------------------------------

        [Fact]
        public void R7_18_BlackKrrsantan_MaySpend2000_ForPlus2GroundCombat_InThatCombat()
        {
            var s = Game();
            Give(s, "base-databank-004-asset", "crew");
            P(s, "p1").Credits = 5000;
            int value = Combat.Value(C, s, P(s, "p1"), CombatType.Ground);
            var r = GameEngine.StartResolution(C, s, GroundCombat(2));
            Assert.Equal("rules.use_ability", s.Pending!.Prompt);
            r = TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            Assert.Equal(value + 2, r.Events.OfType<CombatStartedEvent>().Single().AttackerValue);
            Assert.Equal(3000, P(s, "p1").Credits);
            Assert.Equal(value, Combat.Value(C, s, P(s, "p1"), CombatType.Ground)); // only for that combat
        }

        [Fact]
        public void R7_18_BlackKrrsantan_IsNotOffered_InShipCombat()
        {
            var s = Game();
            Give(s, "base-databank-004-asset", "crew");
            var combat = GroundCombat(2);
            combat.Set("type", "ship");
            TestHelpers.PassDiceWindows(C, s, GameEngine.StartResolution(C, s, combat));
            Assert.True(s.Pending == null || s.Pending.Prompt != "rules.use_ability");
        }

        [Fact]
        public void R7_18_DoctorEvazan_ChoosesADieResult_AtTheStartOfGroundCombat()
        {
            var s = Game();
            Give(s, "base-databank-009-asset", "crew");
            GameEngine.StartResolution(C, s, GroundCombat(2));
            Assert.Equal("custom.choose_face", s.Pending!.Prompt);
            var combat = s.Stack.Single(f => f.Handler == Combat.Handler);
            TestHelpers.PassDiceWindows(C, s, Answer(s, "crit"));
            Assert.Equal("crit", combat.Get("evazan"));
        }

        // ---- "when you lose a ship combat", "when you gain fame": Chromium Hull ----------------------

        [Fact]
        public void ChromiumHull_IsDiscarded_WhenItsOwnerLosesAShipCombat()
        {
            for (ulong seed = 0; seed < 40; seed++)
            {
                var s = Game(seed: seed);
                Give(s, "base-market-luxury-01", "mod");
                var combat = GroundCombat(12);
                combat.Set("type", "ship");
                var r = TestHelpers.DeclineAll(C, s, GameEngine.StartResolution(C, s, combat));
                if (r.Events.OfType<CombatResolvedEvent>().Single().Winner != "defender") continue;
                Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == "base-market-luxury-01");
                return;
            }
            Assert.Fail("No lost combat in 40 seeds.");
        }

        [Fact]
        public void R18_15_ChromiumHull_GivesOneAdditionalFame_OnEveryFameGain()
        {
            var s = Game();
            Give(s, "base-market-luxury-01", "mod");
            var events = new List<GameEvent>();
            PlayerRules.GainFame(C, s, P(s, "p1"), 1, events);
            Assert.Equal(2, P(s, "p1").Fame);
            PlayerRules.GainFame(C, s, P(s, "p2"), 1, events);
            Assert.Equal(1, P(s, "p2").Fame);
        }

        // ---- "when you become defeated": Gift for Jabba ------------------------------------------------

        [Fact]
        public void R8_8_GiftForJabba_DiscardsACrew_WhenItsOwnerBecomesDefeated()
        {
            var s = Game();
            Give(s, "base-market-luxury-02", "cargo");
            Give(s, "base-databank-018-asset", "crew");
            TestHelpers.DeclineAll(C, s, GameEngine.StartResolution(C, s, SufferDamageHandler.For("p1", CombatType.Ground, 99)));
            Assert.True(P(s, "p1").Defeated);
            Assert.DoesNotContain(P(s, "p1").Assets, a => a.Kind == "crew");
            Assert.Contains(P(s, "p1").Assets, a => a.CardId == "base-market-luxury-02");
        }

        // ---- "at the end of your action step": Unrefined Coaxium ---------------------------------------

        [Fact]
        public void R4_2_UnrefinedCoaxium_RollsADie_AtTheEndOfTheActionStep_ACritCosts4ShipDamage()
        {
            bool sawCrit = false, sawOther = false;
            for (ulong seed = 0; seed < 60 && !(sawCrit && sawOther); seed++)
            {
                var s = Game(seed: seed);
                Give(s, "base-encounter-kessel-maelstrom-09-asset", "cargo");
                GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
                var r = TestHelpers.PassDiceWindows(C, s, GameEngine.Execute(C, s, new EndActionStepCommand("p1")));
                Assert.True(r.Accepted, r.Reason);
                var roll = r.Events.OfType<EffectDiceRolledEvent>().Single();
                bool crit = roll.Faces.Contains(DieFace.Crit);
                Assert.Equal(crit, !P(s, "p1").Assets.Any(a => a.CardId == "base-encounter-kessel-maelstrom-09-asset"));
                if (crit)
                {
                    sawCrit = true;
                    Assert.True(P(s, "p1").ShipDamage == 4 || P(s, "p1").Defeated);
                }
                else
                {
                    sawOther = true;
                    Assert.Equal(TurnStep.Encounter, s.Step);
                }
            }
            Assert.True(sawCrit && sawOther);
        }

        // ---- "when you choose to gain credits during the planning phase": Lobot ------------------------

        [Fact]
        public void R4_6_Lobot_Adds1000PerPositiveReputation_ToThePlanningCredits()
        {
            var s = Game();
            Give(s, "base-databank-018-asset", "crew");
            P(s, "p1").Reputation = new ReputationTrack { Rebel = Reputation.Positive, Hutt = Reputation.Positive, Imperial = Reputation.Negative };
            int before = P(s, "p1").Credits;
            Assert.True(GameEngine.Execute(C, s, new GainCreditsCommand("p1")).Accepted);
            Assert.Equal(before + 2000 + 2000, P(s, "p1").Credits);
        }

        // ---- "+4 {HYPERDRIVE} until the end of your turn" ------------------------------------------------

        [Fact]
        public void R5_19_ABonusUntilTheEndOfTheTurn_LastsUntilTheTurnEnds()
        {
            var s = Game();
            Give(s, "base-encounter-lothal-mon-calamari-01-asset", "mod");
            int hyperdrive = GameEngine.Hyperdrive(C, s, P(s, "p1"));
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-encounter-lothal-mon-calamari-01-asset", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            Assert.Equal(hyperdrive + 4, GameEngine.Hyperdrive(C, s, P(s, "p1")));
            Assert.DoesNotContain(P(s, "p1").Assets, a => a.CardId == "base-encounter-lothal-mon-calamari-01-asset");
            var copy = Persistence.SaveSerializer.Load(Persistence.SaveSerializer.Save(s));
            Assert.Equal(hyperdrive + 4, GameEngine.Hyperdrive(C, copy, P(copy, "p1")));
            GameEngine.EndTurn(s, new List<GameEvent>());
            Assert.Equal(hyperdrive, GameEngine.Hyperdrive(C, s, P(s, "p1")));
        }

        // ---- a databank card's crew declined for lack of a slot ----------------------------------------

        [Fact]
        public void R14_5_DecliningADatabankCardsCrew_LeavesNoDeckToReturnItTo()
        {
            var s = Game();
            var p = P(s, "p1");
            for (int i = 0; Assets.Valid(C, p, Assets.With(p.Assets, new AssetState { CardId = "x", Kind = "crew" })); i++)
            {
                p.Assets.Add(new AssetState { CardId = "test-crew-" + i, Kind = "crew" });
            }
            var gain = new Frame(GainEmbeddedHandler.HandlerId);
            gain.Set("owner", "p1");
            gain.Set("card", "base-databank-004");
            GameEngine.StartResolution(C, s, gain);
            var r = Answer(s, "decline");
            Assert.True(r.Accepted, r.Reason);
            Assert.Contains(r.Events, e => e is AssetDeclinedEvent);
        }

        // ---- coverage: every event a card waits for is raised by the engine ----------------------------

        [Fact]
        public void EveryTimingEvent_InTheCardData_IsRaisedByTheEngine()
        {
            // The effect coverage test checks ops; this one checks the events abilities wait for ("trigger",
            // "replacement" and goal counters). Six of them were never raised until the V0 games (T3.1).
            var used = new SortedSet<string>();
            void Walk(JToken t)
            {
                if (t is JObject o)
                {
                    if (o["timing"] is JObject timing && timing["event"]?.Type == JTokenType.String) used.Add((string)timing["event"]!);
                    if ((string?)o["timing"]?["type"] == "goal" && o["timing"]!["progress"] is JObject progress)
                    {
                        foreach (var kind in progress.Properties())
                        {
                            if (kind.Value is JObject k && k["event"]?.Type == JTokenType.String) used.Add((string)k["event"]!);
                        }
                    }
                    foreach (var prop in o.Properties()) Walk(prop.Value);
                }
                else if (t is JArray a)
                {
                    foreach (var x in a) Walk(x);
                }
            }
            var pack = JObject.Parse(Repo.Read("data/packs/base/pack.json"));
            foreach (var file in (JArray)pack["cards"]!) Walk(JObject.Parse(Repo.Read((string)file!)));

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src", "OuterRim.Core"))) dir = dir.Parent;
            Assert.NotNull(dir);
            var code = string.Concat(Directory.GetFiles(Path.Combine(dir!.FullName, "src", "OuterRim.Core"), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));
            var missing = used.Where(e => !code.Contains("\"" + e + "\"")).ToList();
            Assert.True(missing.Count == 0, "Timing events no rule raises: " + string.Join(", ", missing));
        }

        // ---- limits that hold at all times, settled after each operation -------------------------------

        [Fact]
        public void R8_4_R8_2_LosingAHealthBonus_RemovesTheExcessDamage_AndDefeatsAtTheLimit()
        {
            var s = Game();
            Give(s, "base-market-gear-03", "gear"); // Armored Vest: +2 health
            var p = P(s, "p1");
            int health = Combat.Health(C, s, p);
            p.CharacterDamage = health - 1;
            p.Assets.RemoveAll(a => a.CardId == "base-market-gear-03"); // as if a card had made them discard it
            var r = GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            Assert.True(r.Accepted, r.Reason);
            Assert.Equal(health - 2, p.CharacterDamage);
            Assert.Equal(1, r.Events.OfType<ExcessDamageRemovedEvent>().Single().Character);
            Assert.True(p.Defeated);
            Assert.Contains(r.Events, e => e is PlayerDefeatedEvent);
        }

        [Fact]
        public void R14_18_LosingASlotMod_MakesThePlayerDiscard_UntilEverythingFits()
        {
            var s = Game();
            var p = P(s, "p1");
            Give(s, "base-market-cargo-08", "mod"); // Smuggling Compartment: one more cargo
            for (int i = 0; Assets.Valid(C, p, Assets.With(p.Assets, new AssetState { CardId = "x", Kind = "cargo" })); i++)
            {
                p.Assets.Add(new AssetState { CardId = "test-cargo-" + i, Kind = "cargo" });
            }
            p.Assets.RemoveAll(a => a.CardId == "base-market-cargo-08");
            Assert.True(GameEngine.Execute(C, s, new MoveCommand("p1", new string[0])).Accepted);
            Assert.Equal("rules.too_many_assets", s.Pending!.Prompt);
            Answer(s, s.Pending.Options.First(o => o.Id.StartsWith("test-cargo-")).Id);
            Assert.Null(s.Pending);
            Assert.True(Assets.AllFit(C, p));
        }
    }
}
