using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class ContactTests
    {
        private static readonly Content.GameContent C = TestContent.Base;
        private const string SawBounty = "base-market-bounty-00"; // Saw Gerrera, ground 6, capture on Lothal

        private static GameState Game(string space = "kessel", ulong seed = 161)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            P(s).SpaceId = space;
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            return s;
        }

        private static PlayerState P(GameState s) => s.FindPlayer("p1")!;

        private static string TokenFor(int databank) =>
            C.ContactTokenIds().Single(id => (int)C.Card(id)["databank"]! == databank);

        /// <summary>Puts a token on the player's planet, swapping it with whatever was there.</summary>
        private static ContactToken Here(GameState s, string token)
        {
            var t = Contacts.Token(s, token);
            var here = Contacts.OnPlanet(s, P(s).SpaceId).FirstOrDefault();
            if (t.SpaceId == P(s).SpaceId) return t;
            if (here != null) Contacts.Exchange(t, here, new List<GameEvent>());
            else Contacts.Place(s, t, P(s).SpaceId, P(s).SpaceId, t.FaceUp, new List<GameEvent>());
            return t;
        }

        private static CommandResult Answer(GameState s, string option)
        {
            Assert.NotNull(s.Pending);
            Assert.Contains(s.Pending!.Options, o => o.Id == option);
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, option));
        }

        [Fact]
        public void R3_8_EveryContactSpace_HoldsAFacedownTokenOfItsClass()
        {
            var s = Game();
            Assert.Equal(22, s.Contacts.Count);
            Assert.Equal(C.Map.ContactSpaces.Count, s.Contacts.Count);
            foreach (var t in s.Contacts)
            {
                Assert.False(t.FaceUp);
                var space = C.Map.ContactSpaces.Single(cs => cs.Id == t.Slot);
                Assert.Equal(space.Planet, t.SpaceId);
                Assert.Equal(space.Class, (string)C.Card(t.Id)["class"]!);
            }
            Assert.NotEqual(Game(seed: 1).Contacts.Select(t => t.Id), Game(seed: 2).Contacts.Select(t => t.Id));
        }

        [Fact]
        public void R4_13_ThereMustBeAContactInYourSpace()
        {
            var s = Game("nav-kessel-3");
            Assert.Contains("R-4.13", GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact)).Reason);
        }

        [Fact]
        public void R12_15_R12_16_EncounteringTurnsTheTokenFaceup_AndResolvesItsCard_ThenGainingTheCrewDiscardsTheToken()
        {
            var s = Game();
            var boshek = Here(s, TokenFor(6)); // BoShek: spend 2,000 for the crew
            P(s).Credits = 5000;
            var r = GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, boshek.Id));
            Assert.Contains(r.Events, e => e is ContactRevealedEvent);
            Assert.Contains(r.Events, e => e is DatabankResolvedEvent d && d.CardId == "base-databank-006");
            TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            Assert.Contains(P(s).Assets, a => a.CardId == "base-databank-006-asset" && a.Kind == "crew");
            Assert.True(boshek.Pool);
            Assert.Equal("p2", s.CurrentPlayerId); // R-4.4
        }

        [Fact]
        public void R15_11_DiscardingAContactsCrew_PutsItsTokenFaceup_OnTheNearestPlanetWithRoom()
        {
            var s = Game();
            var boshek = Here(s, TokenFor(6));
            P(s).Credits = 5000;
            GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, boshek.Id));
            TestHelpers.DeclineAll(C, s, Answer(s, "yes"));
            // Discard it through an effect, so the resolver places the token.
            s.CurrentPlayerId = "p1";
            var crew = P(s).Assets.Single(a => a.Kind == "crew");
            Assets.Discard(C, s, P(s), crew, "test", new List<GameEvent>());
            Assert.Single(s.TokensToPlace);
            var r = TestHelpers.DeclineAll(C, s, GameEngine.StartResolution(C, s, new Frame(Contacts.PlaceHandler)));
            Assert.True(boshek.OnMap);
            Assert.True(boshek.FaceUp);
            Assert.Contains(r.Events, e => e is ContactPlacedEvent pl && pl.TokenId == boshek.Id && pl.FaceUpId == boshek.Id);
        }

        [Fact]
        public void BibFortuna_MovesHisTokenToTatooine()
        {
            var s = Game("lothal");
            var bib = Here(s, TokenFor(3));
            GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, bib.Id));
            TestHelpers.DeclineAll(C, s, Answer(s, "no"));
            Assert.Equal("tatooine", bib.SpaceId);
            Assert.True(bib.FaceUp); // R-12.6: it keeps its side
        }

        [Fact]
        public void R12_18_R15_52_ABountyHunter_MayFightInstead_AndCaptureTheContact()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed);
                P(s).Assets.Add(new AssetState { CardId = SawBounty, Kind = "bounty" });
                Market.Deck(s, "bounty").Cards.Remove(SawBounty);
                var saw = Here(s, "base-contact-21-saw-gerrera");
                GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, saw.Id));
                Assert.Equal("rules.contact_or_bounty", s.Pending!.Prompt);
                var r = TestHelpers.PassDiceWindows(C, s, Answer(s, SawBounty));
                if (s.Pending?.Prompt != "rules.eliminate_or_capture") continue; // lost: R-15.53
                Answer(s, "capture");
                Assert.Equal(SawBounty, saw.Bounty);
                Assert.False(saw.OnMap);
                Assert.Equal("p2", s.CurrentPlayerId); // the encounter ended the turn
                // R-16.7: not deliverable in the turn it was captured. On a later turn, on Lothal, the capture reward.
                s.CurrentPlayerId = "p1";
                s.Step = TurnStep.Action;
                P(s).SpaceId = "lothal";
                saw.CapturedThisTurn = true; // as if still in the turn it was captured
                Assert.Contains("R-16.7", GameEngine.Execute(C, s, new DeliverCommand("p1", new[] { SawBounty })).Reason);
                saw.CapturedThisTurn = false;
                P(s).Defeated = false;
                int credits = P(s).Credits;
                r = TestHelpers.DeclineAll(C, s, GameEngine.Execute(C, s, new DeliverCommand("p1", new[] { SawBounty })));
                Assert.True(r.Accepted, r.Reason);
                Assert.Equal(credits + 17000, P(s).Credits);
                Assert.True(saw.Removed);
                Assert.DoesNotContain(P(s).Assets, a => a.CardId == SawBounty);
                Assert.DoesNotContain(SawBounty, Market.Deck(s, "bounty").Cards); // R-15.55: removed, not discarded
                return;
            }
            Assert.Fail("No seed won the bounty combat.");
        }

        [Fact]
        public void R15_52_Eliminating_GivesTheReward_AndRemovesCardAndToken()
        {
            for (ulong seed = 0; seed < 80; seed++)
            {
                var s = Game("kessel", seed);
                P(s).Assets.Add(new AssetState { CardId = SawBounty, Kind = "bounty" });
                Market.Deck(s, "bounty").Cards.Remove(SawBounty);
                var saw = Here(s, "base-contact-21-saw-gerrera");
                GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, saw.Id));
                TestHelpers.PassDiceWindows(C, s, Answer(s, SawBounty));
                if (s.Pending?.Prompt != "rules.eliminate_or_capture") continue;
                int credits = P(s).Credits;
                var r = TestHelpers.DeclineAll(C, s, Answer(s, "eliminate"));
                Assert.Equal(credits + 8000, P(s).Credits);
                Assert.Equal(2, P(s).Fame);
                Assert.True(saw.Removed);
                Assert.Contains(r.Events, e => e is ContactEliminatedEvent);
                return;
            }
            Assert.Fail("No seed won the bounty combat.");
        }

        [Fact]
        public void R15_45_DiscardingABountyWithACapturedToken_ReturnsTheTokenFaceup()
        {
            var s = Game();
            P(s).Assets.Add(new AssetState { CardId = SawBounty, Kind = "bounty" });
            var saw = Contacts.Token(s, "base-contact-21-saw-gerrera");
            saw.SpaceId = "";
            saw.Slot = "";
            saw.Bounty = SawBounty;
            Assets.Discard(C, s, P(s), P(s).Assets.Single(), "test", new List<GameEvent>());
            TestHelpers.DeclineAll(C, s, GameEngine.StartResolution(C, s, new Frame(Contacts.PlaceHandler)));
            Assert.True(saw.OnMap);
            Assert.True(saw.FaceUp);
            Assert.Contains(SawBounty, Market.Deck(s, "bounty").Cards);
        }

        [Fact]
        public void R12_9_Garindan_SecretlyLooksAtAContact_WhichStaysFacedown()
        {
            var s = Game("tatooine");
            s.Step = TurnStep.Action;
            P(s).Assets.Add(new AssetState { CardId = "base-databank-010-asset", Kind = "crew" });
            int credits = P(s).Credits;
            var r = GameEngine.Execute(C, s, new UseAbilityCommand("p1", "base-databank-010-asset", "abilities[0]"));
            Assert.True(r.Accepted, r.Reason);
            var pick = s.Pending!.Options[0].Id;
            r = Answer(s, pick);
            Assert.Equal(pick, r.Events.OfType<ContactLookedEvent>().Single().TokenId);
            Assert.False(Contacts.Token(s, pick).FaceUp);
            Assert.Equal(credits - 1000, P(s).Credits);
        }

        [Fact]
        public void Takodana3_MovesAContactHere_OrExchangesWhenFull()
        {
            var s = Game("takodana");
            var deck = Encounters.Deck(s, "ring-of-kafrene-takodana");
            deck.Cards.Remove("base-encounter-ring-of-kafrene-takodana-07");
            deck.Cards.Insert(0, "base-encounter-ring-of-kafrene-takodana-07");
            var before = Contacts.OnPlanet(s, "takodana").Select(t => t.Id).ToList();
            GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space));
            var pick = s.Pending!.Options.First().Id;
            TestHelpers.DeclineAll(C, s, Answer(s, pick));
            Assert.Equal("takodana", Contacts.Token(s, pick).SpaceId);
            // Takodana has room on the planet space itself (R-15.13), so nothing had to be exchanged.
            Assert.All(before, id => Assert.Equal("takodana", Contacts.Token(s, id).SpaceId));
        }
    }
}
