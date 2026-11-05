#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Encounter decks (R-15.72 to R-15.84) and secrets (R-15.85 to R-15.91).</summary>
    public static class Encounters
    {
        /// <summary>R-3.x: each encounter deck shuffled facedown, top first.</summary>
        public static void Setup(GameContent content, GameState state)
        {
            state.EncounterDecks.Clear();
            foreach (var deck in content.EncounterDeckIds())
            {
                var cards = content.EncounterCards(deck);
                state.Random.Shuffle(cards);
                state.EncounterDecks.Add(new MarketDeck { Deck = deck, Cards = cards });
            }
        }

        public static MarketDeck Deck(GameState state, string deck)
        {
            foreach (var d in state.EncounterDecks)
            {
                if (d.Deck == deck) return d;
            }
            throw new KeyNotFoundException("Unknown encounter deck '" + deck + "'.");
        }

        /// <summary>A secret is held as "card id#path of its variant" (the variant is fixed when drawn, R-15.77).</summary>
        public static string SecretCard(string secret)
        {
            int i = secret.IndexOf('#');
            return i < 0 ? secret : secret.Substring(0, i);
        }

        public static string? SecretPath(string secret)
        {
            int i = secret.IndexOf('#');
            return i < 0 ? null : secret.Substring(i + 1);
        }

        /// <summary>R-15.84, R-15.89: the encounter card goes to the bottom of its deck.</summary>
        public static void ToBottom(GameContent content, GameState state, string cardId)
        {
            var card = content.Card(cardId);
            var deck = Deck(state, (string)card["deck"]!);
            deck.Cards.Remove(cardId);
            deck.Cards.Add(cardId);
        }

        /// <summary>Discards a held secret (R-15.86: resolving it reveals it), back to the bottom of its deck.</summary>
        public static bool DiscardSecret(GameContent content, GameState state, PlayerState player, string cardId, List<GameEvent> events)
        {
            foreach (var s in player.Secrets)
            {
                if (SecretCard(s) == cardId)
                {
                    player.Secrets.Remove(s);
                    ToBottom(content, state, cardId);
                    events.Add(new SecretDiscardedEvent(player.Id, cardId));
                    return true;
                }
            }
            return false;
        }

        /// <summary>R-15.89: on defeat, every secret goes to the bottom of its encounter deck, unrevealed.</summary>
        public static void DiscardAllSecrets(GameContent content, GameState state, PlayerState player)
        {
            foreach (var s in player.Secrets)
            {
                var id = SecretCard(s);
                if (content.HasCard(id) && content.Card(id)["deck"] != null) ToBottom(content, state, id);
            }
            player.Secrets.Clear();
        }

        /// <summary>
        /// The section a player resolves (R-15.77): the one for their space (navpoints: by whether a patrol is
        /// in their space), and its first variant whose reputation condition holds now. Null if none applies.
        /// </summary>
        public static string? SectionPath(GameContent content, GameState state, PlayerState player, JObject card, string space)
        {
            var sections = (JArray)card["sections"]!;
            bool navpoint = content.Map[space].Type == SpaceType.Navpoint;
            bool patrolHere = false;
            foreach (var p in state.Patrols)
            {
                if (p.SpaceId == space) patrolHere = true;
            }
            for (int i = 0; i < sections.Count; i++)
            {
                var section = (JObject)sections[i];
                bool fits = navpoint
                    ? (string?)section["space"] == "navpoint" && (bool?)section["patrol"] == patrolHere
                    : (string?)section["space"] == space;
                if (!fits) continue;
                var variants = (JArray)section["variants"]!;
                for (int j = 0; j < variants.Count; j++)
                {
                    var cond = variants[j]["condition"];
                    if (cond == null || cond.Type == JTokenType.Null || Conditions.Eval(content, state, player, cond))
                    {
                        return "sections[" + i + "].variants[" + j + "]";
                    }
                }
                return null;
            }
            return null;
        }
    }

    /// <summary>
    /// Encountering a space (R-15.79 to R-15.84): draw the top card of the space's deck, resolve the matching
    /// section; a secret is kept facedown (R-15.82); the card goes to the bottom of its deck unless it gave a
    /// secret or an asset (R-15.84). Vars: "player" (default: current), optional "deck" (another card from a
    /// given deck, Takodana-5).
    /// </summary>
    internal sealed class SpaceEncounterHandler : IResolutionHandler
    {
        public string Id => TurnHandlers.EncounterSpace;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Vars.ContainsKey("player") ? frame.Get("player") : state.CurrentPlayerId)!;
            switch (frame.Step)
            {
                case 0:
                {
                    string deckId = frame.Vars.ContainsKey("deck") && frame.Get("deck").Length > 0
                        ? frame.Get("deck")
                        : ctx.Content.EncounterDeckFor(player.SpaceId);
                    var deck = Encounters.Deck(state, deckId);
                    frame.Step = 2;
                    if (deck.Cards.Count == 0)
                    {
                        ctx.Events.Add(new EncounterResolvedEvent(player.Id, "space", ""));
                        return StepResult.Done();
                    }
                    var cardId = deck.Cards[0];
                    deck.Cards.RemoveAt(0);
                    frame.Set("card", cardId);
                    var path = Encounters.SectionPath(ctx.Content, state, player, ctx.Content.Card(cardId), player.SpaceId);
                    bool secret = path != null && (bool?)ctx.Content.Card(cardId).SelectToken(path)!["secret"] == true;
                    ctx.Events.Add(new EncounterCardDrawnEvent(player.Id, deckId, cardId, path ?? "", secret));
                    if (path == null)
                    {
                        return StepResult.Continue();
                    }
                    if (secret)
                    {
                        // R-15.82: taken facedown; its abilities are the player's until it is discarded.
                        player.Secrets.Add(cardId + "#" + path);
                        frame.Set("kept", "1");
                        return StepResult.Continue();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(player.Id, cardId, path + ".effects"));
                }
                default:
                {
                    var cardId = frame.Get("card");
                    if (!frame.Vars.ContainsKey("kept") && !HoldsEmbedded(ctx, cardId))
                    {
                        Encounters.ToBottom(ctx.Content, state, cardId);
                    }
                    ctx.Events.Add(new EncounterResolvedEvent(player.Id, "space", cardId));
                    return StepResult.Done();
                }
            }
        }

        /// <summary>The card is kept while someone holds the asset printed on it (R-15.84).</summary>
        private static bool HoldsEmbedded(ResolutionContext ctx, string cardId)
        {
            var asset = (string?)ctx.Content.Card(cardId)["asset"];
            if (asset == null) return false;
            foreach (var p in ctx.State.Players)
            {
                if (Assets.Find(p, asset) != null) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// "Gain the cargo below" (R-15.78): the asset printed on the card being resolved, at no cost. Gaining it
    /// from a secret ("gain the cargo above") turns the secret into that asset.
    /// </summary>
    internal sealed class GainEmbeddedHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.gain_embedded";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var cardId = frame.Get("card");
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var assetId = (string?)ctx.Content.Card(cardId)["asset"];
                if (assetId == null || Assets.Find(owner, assetId) != null)
                {
                    ctx.Return("failed", "true");
                    return StepResult.Done();
                }
                var asset = ctx.Content.Card(assetId);
                foreach (var s in new List<string>(owner.Secrets))
                {
                    if (Encounters.SecretCard(s) == cardId) owner.Secrets.Remove(s);
                }
                var gain = new Frame(Assets.GainHandler);
                gain.Set("player", owner.Id);
                gain.Set("card", assetId);
                gain.Set("kind", (string)asset["kind"]!);
                gain.Set("cost", "0");
                gain.Set("traits", MarketBuyHandler.Traits(asset));
                return StepResult.Call(gain);
            }
            // Declined for lack of a slot (R-14.5): a card left by a secret goes back to its deck.
            var held = Assets.Find(owner, (string)ctx.Content.Card(cardId)["asset"]!) != null;
            if (!held && !IsBeingDrawn(ctx, cardId))
            {
                Encounters.ToBottom(ctx.Content, ctx.State, cardId);
            }
            return StepResult.Done();
        }

        private static bool IsBeingDrawn(ResolutionContext ctx, string cardId)
        {
            foreach (var f in ctx.State.Stack)
            {
                if (f.Handler == TurnHandlers.EncounterSpace && f.Vars.TryGetValue("card", out var c) && c == cardId) return true;
            }
            return false;
        }
    }
}
