#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Buying from a market deck through an effect (market.buy): the top card, one of the top N revealed (the
    /// faceup card counts, R-16.45), or any card searched for; with a discount (R-9.4) or for free; bartering is
    /// allowed as in a market purchase (R-16.34). After revealing or searching, the deck is shuffled (R-16.44);
    /// the new top card is revealed and its patrol icon moves that patrol toward the buyer (R-11.13, R-11.14).
    /// </summary>
    internal sealed class MarketEffectBuyHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.market_buy";
        private const string None = "none";
        private const string Done = "done";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var decks = new List<string>();
                    foreach (var d in (JArray)node["deck"]!) decks.Add((string)d!);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_deck", decks, "deck");
                }
                case 1:
                {
                    frame.Step = 2;
                    var deck = Market.Deck(ctx.State, frame.Get("deck"));
                    string source = (string)node["source"]!;
                    int n = source == "top" ? 1 : source == "reveal" ? (int)node["reveal"]! : deck.Cards.Count;
                    var options = new List<DecisionOption>();
                    var seen = new List<string>();
                    for (int i = 0; i < n && i < deck.Cards.Count; i++)
                    {
                        var id = deck.Cards[i];
                        seen.Add(id);
                        if (Fits(ctx, node, id) && Affordable(ctx, owner, node, id))
                        {
                            options.Add(new DecisionOption(id, id));
                        }
                    }
                    if (source != "top") ctx.Events.Add(new MarketCardsRevealedEvent(owner.Id, deck.Deck, seen));
                    bool mandatory = source == "top" && (bool?)node["free"] == true; // "you buy the top card ... for free"
                    if (!mandatory || options.Count == 0) options.Add(new DecisionOption(None, "effects.buy_none"));
                    if (options.Count == 1)
                    {
                        frame.Set("pick", options[0].Id);
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.market_buy", options, "pick"));
                }
                case 2:
                {
                    // R-16.31: barter one asset at a time, until "done".
                    var pick = frame.Get("pick");
                    if (pick == None)
                    {
                        frame.Step = 4;
                        return StepResult.Continue();
                    }
                    if (frame.Vars.TryGetValue("barter_pick", out var b))
                    {
                        frame.Vars.Remove("barter_pick");
                        if (b == Done)
                        {
                            frame.Step = 3;
                            return StepResult.Continue();
                        }
                        frame.Set("barter", frame.Vars.TryGetValue("barter", out var so_far) && so_far.Length > 0 ? so_far + "," + b : b);
                    }
                    int price = Price(ctx, owner, node, pick, Bartered(owner, frame));
                    var options = new List<DecisionOption>();
                    if (price > 0)
                    {
                        foreach (var a in owner.Assets)
                        {
                            if (Bartered(owner, frame).Contains(a)) continue;
                            if (Assets.BarterValue(new[] { a }, out _) != null) options.Add(new DecisionOption(a.CardId, a.CardId));
                        }
                    }
                    if (options.Count == 0)
                    {
                        frame.Step = 3;
                        return StepResult.Continue();
                    }
                    if (owner.Credits >= price) options.Add(new DecisionOption(Done, "effects.barter_done"));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.barter", options, "barter_pick"));
                }
                case 3:
                {
                    frame.Step = 4;
                    var pick = frame.Get("pick");
                    var deck = Market.Deck(ctx.State, frame.Get("deck"));
                    var bartered = Bartered(owner, frame);
                    int price = Price(ctx, owner, node, pick, bartered);
                    if (!PlayerRules.TrySpend(owner, price, ctx.Events))
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    foreach (var a in bartered) Assets.Discard(ctx.Content, ctx.State, owner, a, "barter", ctx.Events);
                    deck.Cards.Remove(pick);
                    ctx.Events.Add(new MarketCardBoughtEvent(owner.Id, deck.Deck, pick, price));
                    ctx.Return("spent", price.ToString(CultureInfo.InvariantCulture));
                    return StepResult.Call(GainFrame(ctx, owner, pick));
                }
                case 4:
                {
                    frame.Step = 5;
                    var deck = Market.Deck(ctx.State, frame.Get("deck"));
                    if ((string)node["source"]! != "top")
                    {
                        ctx.State.Random.Shuffle(deck.Cards); // "Then shuffle the deck" (R-16.44)
                        ctx.Events.Add(new MarketDeckShuffledEvent(deck.Deck));
                    }
                    if (frame.Get("pick") == None && (string)node["source"]! == "top") return StepResult.Done();
                    var icon = Market.RevealTop(ctx, deck, owner.Id);
                    return icon != null ? StepResult.Call(icon) : StepResult.Continue();
                }
                default:
                    return StepResult.Done();
            }
        }

        private static bool Fits(ResolutionContext ctx, JObject node, string cardId)
        {
            if (!(node["filter"]?["asset"] is JObject filter)) return true;
            var card = ctx.Content.Card(cardId);
            var probe = new AssetState { CardId = cardId, Kind = (string)card["kind"]!, Traits = MarketBuyHandler.Traits(card).Length == 0
                ? new List<string>() : new List<string>(MarketBuyHandler.Traits(card).Split(',')) };
            return Selectors.AssetMatches(ctx.Content, probe, filter);
        }

        private static int Cost(ResolutionContext ctx, JObject node, string cardId)
        {
            if ((bool?)node["free"] == true) return 0;
            var card = ctx.Content.Card(cardId);
            int cost = (string?)card["kind"] == "ship" ? ctx.Content.ShipGoalSide((string)card["sheet"]!).Cost ?? 0 : (int?)card["cost"] ?? 0;
            return System.Math.Max(0, cost - ((int?)node["discount"] ?? 0));
        }

        private static int Price(ResolutionContext ctx, PlayerState owner, JObject node, string cardId, List<AssetState> bartered)
        {
            int barter = Assets.BarterValue(bartered, out _) ?? 0;
            if ((string?)ctx.Content.Card(cardId)["kind"] == "ship") barter += ctx.Content.ShipSide(owner.ShipSideId).Cost ?? 0; // R-16.38
            return Assets.PriceAfterBarter(Cost(ctx, node, cardId), barter);
        }

        /// <summary>Whether the owner could pay for the card, bartering every barterable asset if needed.</summary>
        private static bool Affordable(ResolutionContext ctx, PlayerState owner, JObject node, string cardId)
        {
            var kind = (string?)ctx.Content.Card(cardId)["kind"];
            if (kind == "special") return false; // Used Ship for Sale is only bought by the market action
            if (kind == "ship" && !ctx.State.ShipSupply.Contains((string)ctx.Content.Card(cardId)["sheet"]!)) return false;
            var all = new List<AssetState>();
            foreach (var a in owner.Assets)
            {
                if (Assets.BarterValue(new[] { a }, out _) != null) all.Add(a);
            }
            return owner.Credits >= Price(ctx, owner, node, cardId, all);
        }

        private static List<AssetState> Bartered(PlayerState owner, Frame frame)
        {
            var list = new List<AssetState>();
            if (!frame.Vars.TryGetValue("barter", out var ids) || ids.Length == 0) return list;
            foreach (var id in ids.Split(','))
            {
                var a = Assets.Find(owner, id);
                if (a != null) list.Add(a);
            }
            return list;
        }

        /// <summary>The frame that puts a market card into play for a player: a ship sheet, or an asset in a slot.</summary>
        internal static Frame GainFrame(ResolutionContext ctx, PlayerState player, string cardId)
        {
            var card = ctx.Content.Card(cardId);
            string kind = (string)card["kind"]!;
            if (kind == "ship")
            {
                var buy = new Frame(Assets.BuyShipHandler);
                buy.Set("player", player.Id);
                buy.Set("sheet", (string)card["sheet"]!);
                buy.Set("barter", "");
                buy.Set("paid", "true");
                return buy;
            }
            var gain = new Frame(Assets.GainHandler);
            gain.Set("player", player.Id);
            gain.Set("card", cardId);
            gain.Set("kind", kind);
            gain.Set("cost", ((int?)card["cost"] ?? 0).ToString(CultureInfo.InvariantCulture));
            gain.Set("traits", MarketBuyHandler.Traits(card));
            return gain;
        }
    }

    /// <summary>
    /// "Take the 'Syndicate Favor' job card from another player or the {DECK_JOB} market deck. If you take it
    /// from the deck, shuffle that deck." (market.take). The owner picks where it comes from.
    /// </summary>
    internal sealed class MarketTakeHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.market_take";
        private const string FromDeck = "deck";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var cardId = (string)node["name"]!;
            var deckId = (string)ctx.Content.Card(cardId)["deck"]!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var options = new List<string>();
                    if (Market.Deck(ctx.State, deckId).Cards.Contains(cardId)) options.Add(FromDeck);
                    foreach (var p in ctx.State.Players)
                    {
                        if (p.Id != owner.Id && Assets.Find(p, cardId) != null) options.Add(p.Id);
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.take_from", options, "from");
                }
                case 1:
                {
                    frame.Step = 2;
                    var from = frame.Get("from");
                    if (from == FromDeck)
                    {
                        var deck = Market.Deck(ctx.State, deckId);
                        deck.Cards.Remove(cardId);
                        ctx.State.Random.Shuffle(deck.Cards);
                        ctx.Events.Add(new MarketDeckShuffledEvent(deck.Deck));
                        frame.Set("reveal", "1");
                    }
                    else
                    {
                        var other = ctx.State.FindPlayer(from)!;
                        var a = Assets.Find(other, cardId)!;
                        other.Assets.Remove(a); // taken, not discarded: it does not go back to its deck
                        ctx.Events.Add(new AssetDiscardedEvent(other.Id, cardId, a.Kind, "taken"));
                    }
                    return StepResult.Call(MarketEffectBuyHandler.GainFrame(ctx, owner, cardId));
                }
                case 2:
                {
                    frame.Step = 3;
                    if (!frame.Vars.ContainsKey("reveal")) return StepResult.Done();
                    var icon = Market.RevealTop(ctx, Market.Deck(ctx.State, deckId), owner.Id);
                    return icon != null ? StepResult.Call(icon) : StepResult.Continue();
                }
                default:
                    return StepResult.Done();
            }
        }
    }
}
