#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Market decks (R-16.41 to R-16.48) and the market action (R-16.17 to R-16.40).</summary>
    public static class Market
    {
        public const string BuyHandler = "rules.market_buy";
        public const string UsedShipHandler = "rules.used_ship_for_sale";

        public static readonly string[] Decks = { "bounty", "cargo", "gear", "job", "luxury", "ship" };

        /// <summary>R-3.16.a: each market deck shuffled, its top card faceup (top first in the list).</summary>
        public static void Setup(GameContent content, GameState state)
        {
            state.MarketDecks.Clear();
            foreach (var deck in Decks)
            {
                var cards = content.MarketCards(deck);
                state.Random.Shuffle(cards);
                state.MarketDecks.Add(new MarketDeck { Deck = deck, Cards = cards });
            }
        }

        public static MarketDeck Deck(GameState state, string deck)
        {
            foreach (var d in state.MarketDecks)
            {
                if (d.Deck == deck) return d;
            }
            throw new KeyNotFoundException("Unknown market deck '" + deck + "'.");
        }

        /// <summary>Price of the top card after bartering, or a rejection reason (R-16.22, R-16.31 to R-16.39, R-15.6).</summary>
        internal static string? Check(GameContent content, GameState state, PlayerState player, string deck, IReadOnlyList<string> barterIds,
            out int price)
        {
            price = 0;
            var d = Deck(state, deck);
            if (d.Cards.Count == 0)
            {
                return "That deck is empty.";
            }
            var card = content.Card(d.Cards[0]);
            if ((string?)card["cannot_buy_on"] == player.SpaceId)
            {
                return "R-15.6: this card cannot be bought on this planet.";
            }
            var bartered = new List<AssetState>();
            foreach (var id in barterIds)
            {
                var a = Assets.Find(player, id);
                if (a == null) return "You do not hold " + id + ".";
                bartered.Add(a);
            }
            var value = Assets.BarterValue(bartered, out var reason);
            if (value == null) return reason;
            string kind = (string)card["kind"]!;
            if (kind == "special")
            {
                return null; // Used Ship for Sale: the price depends on the sheet chosen
            }
            int cost = (int?)card["cost"] ?? 0;
            if (kind == "ship")
            {
                var sheet = (string)card["sheet"]!;
                if (!state.ShipSupply.Contains(sheet)) return "R-17.8: that ship sheet is not in the supply.";
                value += content.ShipSide(player.ShipSideId).Cost ?? 0; // R-16.38: the current ship is bartered
            }
            price = Assets.PriceAfterBarter(cost, value.Value);
            return player.Credits < price ? "R-9.8: the card costs " + price.ToString(CultureInfo.InvariantCulture) + " after barter." : null;
        }
    }

    /// <summary>
    /// Buying the top card of a market deck: pay (R-16.22) after bartering (R-16.31), then the card goes in
    /// a slot (R-16.23, R-14.4), or replaces the ship sheet (R-16.27 to R-16.30), or resolves (Used Ship for
    /// Sale). Then the new top card is revealed and its patrol icon moves that patrol toward the buyer
    /// (R-11.13, R-16.25). Vars: "player", "deck", "barter" (ids joined by ',').
    /// </summary>
    internal sealed class MarketBuyHandler : IResolutionHandler
    {
        public string Id => Market.BuyHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var deck = Market.Deck(ctx.State, frame.Get("deck"));
            switch (frame.Step)
            {
                case 0:
                {
                    var cardId = deck.Cards[0];
                    var card = ctx.Content.Card(cardId);
                    string kind = (string)card["kind"]!;
                    frame.Set("card", cardId);
                    var barterIds = frame.Get("barter").Length == 0 ? new string[0] : frame.Get("barter").Split(',');
                    if (kind == "special")
                    {
                        frame.Step = 1;
                        var used = new Frame(Market.UsedShipHandler);
                        used.Set("player", player.Id);
                        used.Set("card", cardId);
                        used.Set("barter", frame.Get("barter"));
                        return StepResult.Call(used);
                    }
                    Market.Check(ctx.Content, ctx.State, player, deck.Deck, barterIds, out int price);
                    PlayerRules.TrySpend(player, price, ctx.Events);
                    foreach (var id in barterIds)
                    {
                        Assets.Discard(player, Assets.Find(player, id)!, "barter", ctx.Events);
                        ReturnToDeck(ctx, id);
                    }
                    deck.Cards.RemoveAt(0);
                    ctx.Events.Add(new MarketCardBoughtEvent(player.Id, deck.Deck, cardId, price));
                    frame.Step = 1;
                    if (kind == "ship")
                    {
                        // R-16.27: the ship's card is removed from the game; the sheet replaces the player's.
                        var buy = new Frame(Assets.BuyShipHandler);
                        buy.Set("player", player.Id);
                        buy.Set("sheet", (string)card["sheet"]!);
                        buy.Set("barter", "");
                        buy.Set("paid", "true");
                        return StepResult.Call(buy);
                    }
                    var gain = new Frame(Assets.GainHandler);
                    gain.Set("player", player.Id);
                    gain.Set("card", cardId);
                    gain.Set("kind", kind);
                    gain.Set("cost", ((int?)card["cost"] ?? 0).ToString(CultureInfo.InvariantCulture));
                    gain.Set("traits", Traits(card));
                    return StepResult.Call(gain);
                }
                case 1:
                {
                    // R-16.25, R-11.13: reveal the new top card; a patrol icon moves that patrol toward the buyer.
                    frame.Step = 2;
                    if (deck.Cards.Count == 0)
                    {
                        return StepResult.Continue();
                    }
                    var top = ctx.Content.Card(deck.Cards[0]);
                    ctx.Events.Add(new MarketCardRevealedEvent(deck.Deck, deck.Cards[0]));
                    if (top["patrol_icon"] is JObject icon)
                    {
                        var move = new Frame(Patrols.MoveHandler);
                        move.Set("faction", (string)icon["faction"]!);
                        move.Set("distance", (int)icon["distance"]!);
                        return StepResult.Call(move);
                    }
                    return StepResult.Continue();
                }
                default:
                    return StepResult.Done();
            }
        }

        internal static string Traits(JObject card)
        {
            var list = new List<string>();
            if (card["traits"] is JArray t)
            {
                foreach (var x in t) list.Add((string)x!);
            }
            return string.Join(",", list);
        }

        /// <summary>R-22.1: a discarded market card goes to the bottom of its deck (cards without a deck, such as encounter assets, are left aside).</summary>
        internal static void ReturnToDeck(ResolutionContext ctx, string cardId)
        {
            if (!ctx.Content.HasCard(cardId) || ctx.Content.Card(cardId)["deck"] == null)
            {
                return;
            }
            Market.Deck(ctx.State, (string)ctx.Content.Card(cardId)["deck"]!).Cards.Add(cardId);
        }
    }

    /// <summary>
    /// "Used Ship for Sale" (EFFECTS.md §8, R-16.40): buy any ship sheet from the supply costing more than
    /// the current ship, bartering the current ship (mandatory), placed on its ship goal side with 3 damage;
    /// remove that ship's card from the ship deck; shuffle this card back into the deck.
    /// </summary>
    internal sealed class UsedShipForSaleHandler : IResolutionHandler
    {
        public string Id => Market.UsedShipHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var barterIds = frame.Get("barter").Length == 0 ? new string[0] : frame.Get("barter").Split(',');
            switch (frame.Step)
            {
                case 0:
                {
                    int current = ctx.Content.ShipSide(player.ShipSideId).Cost ?? 0;
                    var bartered = new List<AssetState>();
                    foreach (var id in barterIds) bartered.Add(Assets.Find(player, id)!);
                    int barter = (Assets.BarterValue(bartered, out _) ?? 0) + current;
                    var options = new List<DecisionOption>();
                    var seen = new HashSet<string>();
                    foreach (var sheet in ctx.State.ShipSupply)
                    {
                        if (!seen.Add(sheet) || ctx.Content.ShipSide(sheet).Role == "starter") continue;
                        int cost = ctx.Content.ShipGoalSide(sheet).Cost ?? 0;
                        if (cost > current && player.Credits >= Assets.PriceAfterBarter(cost, barter))
                        {
                            options.Add(new DecisionOption(sheet, sheet));
                        }
                    }
                    frame.Step = 1;
                    if (options.Count == 0)
                    {
                        frame.Set("sheet", "");
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "market.used_ship", options, "sheet"));
                }
                case 1:
                {
                    var deck = Market.Deck(ctx.State, "ship");
                    var cardId = frame.Get("card");
                    deck.Cards.Remove(cardId);
                    frame.Step = 2;
                    var sheet = frame.Get("sheet");
                    if (sheet.Length == 0)
                    {
                        Shuffle(ctx, deck, cardId);
                        return StepResult.Done(); // nothing affordable: the card just goes back
                    }
                    // Remove that ship's card from the game, if it is still in the deck.
                    foreach (var id in new List<string>(deck.Cards))
                    {
                        if ((string?)ctx.Content.Card(id)["sheet"] == sheet) deck.Cards.Remove(id);
                    }
                    Shuffle(ctx, deck, cardId);
                    var buy = new Frame(Assets.BuyShipHandler);
                    buy.Set("player", player.Id);
                    buy.Set("sheet", sheet);
                    buy.Set("barter", frame.Get("barter"));
                    return StepResult.Call(buy);
                }
                case 2:
                {
                    // "...and the ship suffers 3 damage."
                    frame.Step = 3;
                    if (frame.Get("sheet").Length == 0) return StepResult.Done();
                    return StepResult.Call(SufferDamageHandler.For(player.Id, CombatType.Ship, 3));
                }
                default:
                    return StepResult.Done();
            }
        }

        private static void Shuffle(ResolutionContext ctx, MarketDeck deck, string cardId)
        {
            deck.Cards.Add(cardId);
            ctx.State.Random.Shuffle(deck.Cards);
            ctx.Events.Add(new MarketDeckShuffledEvent(deck.Deck));
        }
    }
}
