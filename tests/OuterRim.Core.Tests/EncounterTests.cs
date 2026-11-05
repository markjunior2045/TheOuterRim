using System.Collections.Generic;
using System.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class EncounterTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        private static GameState Game(string space, ulong seed = 131)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            P1(s).SpaceId = space;
            foreach (var p in s.Patrols) p.SpaceId = "stack-" + p.Faction.ToString().ToLowerInvariant();
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            return s;
        }

        private static PlayerState P1(GameState s) => s.FindPlayer("p1")!;

        private static void Top(GameState s, string card)
        {
            var deck = Encounters.Deck(s, (string)C.Card(card)["deck"]!);
            deck.Cards.Remove(card);
            deck.Cards.Insert(0, card);
        }

        private static CommandResult EncounterSpace(GameState s)
        {
            return GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space));
        }

        private static CommandResult Answer(GameState s, string option)
        {
            return GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending!.PlayerId, s.Pending.Id, option));
        }

        [Fact]
        public void R15_73_SevenDecksOfTen_AndEachSpaceHasItsDeck()
        {
            var s = Game("kessel");
            Assert.Equal(7, s.EncounterDecks.Count);
            Assert.All(s.EncounterDecks, d => Assert.Equal(10, d.Cards.Count));
            Assert.Equal("navpoint", C.EncounterDeckFor("nav-kessel-3"));
            Assert.Equal("kessel-maelstrom", C.EncounterDeckFor("maelstrom"));
            Assert.Equal("kessel-maelstrom", C.EncounterDeckFor("kessel"));
            Assert.Equal("ring-of-kafrene-takodana", C.EncounterDeckFor("takodana"));
        }

        [Fact]
        public void R15_77_TheVariant_FollowsReputation_WhenTheCardIsDrawn()
        {
            const string card = "base-encounter-tatooine-nal-hutta-08"; // Nal Hutta: positive/neutral or negative Hutt
            var s = Game("nal-hutta");
            Top(s, card);
            var r = EncounterSpace(s);
            Assert.Equal("sections[1].variants[0]", r.Events.OfType<EncounterCardDrawnEvent>().Single().Section);
            var t = Game("nal-hutta");
            P1(t).Reputation.Hutt = Reputation.Negative;
            P1(t).Credits = 10000; // can pay, so the player is asked
            Top(t, card);
            r = EncounterSpace(t);
            Assert.Equal("sections[1].variants[1]", r.Events.OfType<EncounterCardDrawnEvent>().Single().Section);
            Assert.Equal("effects.unless", t.Pending!.Prompt); // fight enforcers unless you spend 5,000
        }

        [Fact]
        public void R15_84_AfterResolving_TheCardGoesToTheBottom_AndTheTurnEnds()
        {
            const string card = "base-encounter-tatooine-nal-hutta-08";
            var s = Game("nal-hutta");
            Top(s, card);
            int credits = P1(s).Credits;
            EncounterSpace(s);
            Answer(s, "yes"); // lose 1 Hutt reputation to gain 5,000
            Assert.Equal(credits + 5000, P1(s).Credits);
            Assert.Equal(Reputation.Negative, P1(s).Reputation.Hutt);
            Assert.Equal(card, Encounters.Deck(s, "tatooine-nal-hutta").Cards.Last());
            Assert.Equal("p2", s.CurrentPlayerId); // R-4.4
        }

        [Fact]
        public void R15_75_OnANavpoint_TheSectionDependsOnAPatrolBeingThere()
        {
            const string card = "base-encounter-navpoint-09";
            var s = Game("nav-kessel-3");
            Top(s, card);
            Assert.Equal("sections[0].variants[0]", EncounterSpace(s).Events.OfType<EncounterCardDrawnEvent>().Single().Section);
            var t = Game("nav-kessel-3");
            Patrols.Of(t, Faction.Hutt).SpaceId = "nav-kessel-3";
            P1(t).Reputation.Hutt = Reputation.Positive; // not forced to encounter it (R-4.14)
            Top(t, card);
            Assert.Equal("sections[1].variants[0]", EncounterSpace(t).Events.OfType<EncounterCardDrawnEvent>().Single().Section);
        }

        [Fact]
        public void R15_82_ASecret_IsKeptFacedown_AndItsAbilitiesAreTheHolders()
        {
            const string card = "base-encounter-tatooine-nal-hutta-08"; // Tatooine: a secret
            var s = Game("tatooine");
            Top(s, card);
            var r = EncounterSpace(s);
            Assert.True(r.Events.OfType<EncounterCardDrawnEvent>().Single().Secret);
            Assert.Equal(new[] { card + "#sections[0].variants[0]" }, P1(s).Secrets);
            Assert.DoesNotContain(card, Encounters.Deck(s, "tatooine-nal-hutta").Cards);
            Assert.Contains(AbilitySources.Of(C, P1(s)), a => a.Key == card + "#sections[0].variants[0].abilities[0]");
            Assert.Equal("p2", s.CurrentPlayerId);
        }

        [Fact]
        public void R15_89_ADefeatedPlayersSecrets_GoBackToTheBottomOfTheirDecks()
        {
            const string card = "base-encounter-tatooine-nal-hutta-08";
            var s = Game("tatooine");
            Top(s, card);
            EncounterSpace(s);
            PlayerRules.SufferDamage(C, s, P1(s), CombatType.Ground, 9, new List<GameEvent>());
            Assert.Empty(P1(s).Secrets);
            Assert.Equal(card, Encounters.Deck(s, "tatooine-nal-hutta").Cards.Last());
        }

        [Fact]
        public void R15_78_AnEmbeddedAsset_CostsNothing_AndKeepsItsCardOutOfTheDeck_UntilDiscarded()
        {
            const string card = "base-encounter-cantonica-ord-mantell-05"; // Ord Mantell: spend 1,000 for Thermal Detonator
            var s = Game("ord-mantell");
            Top(s, card);
            int credits = P1(s).Credits;
            EncounterSpace(s);
            Answer(s, "yes");
            var gear = P1(s).Assets.Single(a => a.CardId == card + "-asset");
            Assert.Equal(0, gear.Cost); // cannot be bartered (R-15.78)
            Assert.Equal("gear", gear.Kind);
            Assert.Equal(credits - 1000, P1(s).Credits);
            Assert.DoesNotContain(card, Encounters.Deck(s, "cantonica-ord-mantell").Cards);
            Assets.Discard(C, s, P1(s), gear, "test", new List<GameEvent>());
            Assert.Equal(card, Encounters.Deck(s, "cantonica-ord-mantell").Cards.Last());
        }

        [Fact]
        public void R15_78_DecliningTheEmbeddedAsset_SendsTheCardBack()
        {
            const string card = "base-encounter-cantonica-ord-mantell-05";
            var s = Game("ord-mantell");
            Top(s, card);
            EncounterSpace(s);
            Answer(s, "no");
            Assert.Empty(P1(s).Assets);
            Assert.Equal(card, Encounters.Deck(s, "cantonica-ord-mantell").Cards.Last());
        }

        [Fact]
        public void R15_84_TheEncounterDecks_SurviveSaveAndLoad()
        {
            var s = Game("ord-mantell");
            var copy = Persistence.SaveSerializer.Load(Persistence.SaveSerializer.Save(s));
            Assert.Equal(s.EncounterDecks.Select(d => string.Join(",", d.Cards)), copy.EncounterDecks.Select(d => string.Join(",", d.Cards)));
        }
    }
}
