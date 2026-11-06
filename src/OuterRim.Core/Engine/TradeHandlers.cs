#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Trading (RULES §16.2, R-9.6, R-9.7; owner decisions of 2026-11-06, DECISIONS §4.2). Two digital forms of
    /// table talk: credit requests ("Ask Credits" / "Send Credits") between the current player and another player
    /// at any moment of the turn (R-16.15, R-16.16), which the other player accepts or refuses; and the trade
    /// action, an offer of credits and cards the partner accepts or refuses as a whole (R-16.8 to R-16.14). Only
    /// what changes hands at once counts (R-9.7, R-16.11): there are no promises.
    /// </summary>
    public static class Trades
    {
        public const string Handler = "rules.trade";

        /// <summary>
        /// Owner decision (DECISIONS §4.2): a credit request unanswered for this many seconds disappears and is
        /// refused automatically. The interface times it and sends <see cref="TimeOutCreditRequestCommand"/>.
        /// </summary>
        public const int RequestTimeoutSeconds = 15;

        /// <summary>R-4.9: the trade action, once per action step; marked used only by a trade carried out.</summary>
        public const string ActionKey = "action:trade";

        /// <summary>
        /// R-9.6, R-16.15: a credit request between the current player and another player, at any point of the
        /// turn, even in the middle of an ability, an encounter or a combat (R-16.16). Not an action; no shared
        /// space. Owner decisions (DECISIONS §4.2): it holds nothing up, at most one is open between the same two
        /// players, and the payer must have the amount when it is made (so "Ask Credits" asks at most what the other
        /// player has).
        /// </summary>
        internal static CommandResult Request(GameState state, PlayerState player, RequestCreditsCommand command)
        {
            var other = state.FindPlayer(command.ToPlayerId);
            if (other == null || other.Id == player.Id)
            {
                return CommandResult.Rejected("Make a credit request to another player.");
            }
            if (player.Id != state.CurrentPlayerId && other.Id != state.CurrentPlayerId)
            {
                return CommandResult.Rejected("R-9.6, R-16.15: credits are exchanged with the current player.");
            }
            if (command.Kind != CreditRequestKind.Ask && command.Kind != CreditRequestKind.Send)
            {
                return CommandResult.Rejected("Ask for credits or send them.");
            }
            if (command.Amount <= 0)
            {
                return CommandResult.Rejected("Request a positive amount.");
            }
            var payer = command.Kind == CreditRequestKind.Ask ? other : player;
            if (payer.Credits < command.Amount)
            {
                return CommandResult.Rejected("R-9.8: " + payer.Id + " has " + payer.Credits + " credits.");
            }
            foreach (var open in state.CreditRequests)
            {
                if ((open.FromId == player.Id && open.ToId == other.Id) || (open.FromId == other.Id && open.ToId == player.Id))
                {
                    return CommandResult.Rejected("A credit request between " + player.Id + " and " + other.Id + " is already open.");
                }
            }
            var request = new CreditRequest
            {
                Id = state.NextCreditRequestId++,
                FromId = player.Id,
                ToId = other.Id,
                Kind = command.Kind,
                Amount = command.Amount,
            };
            state.CreditRequests.Add(request);
            return CommandResult.Ok(new GameEvent[] { new CreditRequestedEvent(request.Id, request.FromId, request.ToId, request.Kind, request.Amount) });
        }

        /// <summary>
        /// The other player accepts or refuses. On acceptance the credits move at once; if the payer no longer has
        /// the full amount (spent or given away meanwhile), nothing changes hands (R-9.7).
        /// </summary>
        internal static CommandResult Answer(GameState state, PlayerState player, AnswerCreditRequestCommand command)
        {
            var request = state.CreditRequests.Find(r => r.Id == command.RequestId);
            if (request == null)
            {
                return CommandResult.Rejected("No open credit request " + command.RequestId + ".");
            }
            if (request.ToId != player.Id)
            {
                return CommandResult.Rejected("Credit request " + request.Id + " is for " + request.ToId + " to answer.");
            }
            state.CreditRequests.Remove(request);
            var events = new List<GameEvent>();
            if (!command.Accept)
            {
                events.Add(new CreditRequestClosedEvent(request.Id, request.FromId, request.ToId, CreditRequestOutcome.Refused));
                return CommandResult.Ok(events);
            }
            var payer = state.FindPlayer(request.PayerId)!;
            var payee = state.FindPlayer(request.PayeeId)!;
            if (payer.Credits < request.Amount)
            {
                events.Add(new CreditRequestClosedEvent(request.Id, request.FromId, request.ToId, CreditRequestOutcome.Failed));
                return CommandResult.Ok(events);
            }
            payer.Credits -= request.Amount;
            payee.Credits += request.Amount;
            events.Add(new CreditRequestClosedEvent(request.Id, request.FromId, request.ToId, CreditRequestOutcome.Accepted));
            events.Add(new CreditsGivenEvent(payer.Id, payee.Id, request.Amount));
            return CommandResult.Ok(events);
        }

        /// <summary>
        /// No answer in <see cref="RequestTimeoutSeconds"/> seconds: the request is refused automatically. Either of
        /// the two players' seats may report it (the interface keeps the clock).
        /// </summary>
        internal static CommandResult TimeOut(GameState state, PlayerState player, TimeOutCreditRequestCommand command)
        {
            var request = state.CreditRequests.Find(r => r.Id == command.RequestId);
            if (request == null)
            {
                return CommandResult.Rejected("No open credit request " + command.RequestId + ".");
            }
            if (request.FromId != player.Id && request.ToId != player.Id)
            {
                return CommandResult.Rejected("Credit request " + request.Id + " is between " + request.FromId + " and " + request.ToId + ".");
            }
            state.CreditRequests.Remove(request);
            return CommandResult.Ok(new GameEvent[] { new CreditRequestClosedEvent(request.Id, request.FromId, request.ToId, CreditRequestOutcome.TimedOut) });
        }

        /// <summary>The requester withdraws an unanswered request (owner decision, DECISIONS §4.2).</summary>
        internal static CommandResult Cancel(GameState state, PlayerState player, CancelCreditRequestCommand command)
        {
            var request = state.CreditRequests.Find(r => r.Id == command.RequestId);
            if (request == null)
            {
                return CommandResult.Rejected("No open credit request " + command.RequestId + ".");
            }
            if (request.FromId != player.Id)
            {
                return CommandResult.Rejected("Only " + request.FromId + " may cancel credit request " + request.Id + ".");
            }
            state.CreditRequests.Remove(request);
            return CommandResult.Ok(new GameEvent[] { new CreditRequestClosedEvent(request.Id, request.FromId, request.ToId, CreditRequestOutcome.Cancelled) });
        }

        /// <summary>
        /// R-16.15: credits are exchanged during the current player's turn, so requests still open when it ends
        /// are cancelled automatically (owner decision, DECISIONS §4.2).
        /// </summary>
        internal static void ExpireRequests(GameState state, List<GameEvent> events)
        {
            foreach (var r in state.CreditRequests)
            {
                events.Add(new CreditRequestClosedEvent(r.Id, r.FromId, r.ToId, CreditRequestOutcome.Expired));
            }
            state.CreditRequests.Clear();
        }

        /// <summary>Checks an offer of the trade action (R-16.8 to R-16.14); null when it is valid.</summary>
        public static string? Validate(GameState state, PlayerState player, string partnerId,
            int giveCredits, IReadOnlyList<string> giveCards, int takeCredits, IReadOnlyList<string> takeCards)
        {
            if (state.Step != TurnStep.Action)
            {
                return "R-16.8: trading is an action of the action step.";
            }
            if (state.UsedAbilities.Contains(ActionKey))
            {
                return "R-4.9: one trade action per action step.";
            }
            var partner = state.FindPlayer(partnerId);
            if (partner == null || partner.Id == player.Id)
            {
                return "Trade with another player.";
            }
            if (partner.SpaceId != player.SpaceId)
            {
                return "R-16.8: you trade with a player in your space.";
            }
            if (giveCredits < 0 || takeCredits < 0)
            {
                return "Credits in a trade are not negative.";
            }
            if (giveCredits == 0 && takeCredits == 0 && giveCards.Count == 0 && takeCards.Count == 0)
            {
                return "An empty trade.";
            }
            if (player.Credits < giveCredits || partner.Credits < takeCredits)
            {
                return "R-9.8, R-16.11: a trade is paid in full at once.";
            }
            return CardsError(player, giveCards) ?? CardsError(partner, takeCards);
        }

        /// <summary>R-16.12, R-16.14: assets (jobs, bounties, cargo, crew, mods, gear) the owner holds, each once; no secrets or sheets.</summary>
        private static string? CardsError(PlayerState owner, IReadOnlyList<string> cards)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in cards)
            {
                var asset = Assets.Find(owner, id);
                if (asset == null || !seen.Add(id))
                {
                    return "R-16.12, R-16.14: " + owner.Id + " holds no tradeable card " + id + ".";
                }
                if (Array.IndexOf(Assets.Kinds, asset.Kind) < 0)
                {
                    return "R-16.12: " + id + " cannot be traded.";
                }
            }
            return null;
        }

        /// <summary>
        /// Starts the offer and asks the partner. The trade action is not used yet: a refused offer may be followed
        /// by another, to the same or another player in the space (R-16.8, R-4.9 reading in RULES §16.2).
        /// </summary>
        internal static CommandResult Offer(GameContent content, GameState state, PlayerState player, OfferTradeCommand command)
        {
            var error = Validate(state, player, command.PartnerId, command.GiveCredits, command.GiveCards, command.TakeCredits, command.TakeCards);
            if (error != null)
            {
                return CommandResult.Rejected(error);
            }
            var frame = new Frame(Handler);
            frame.Set("player", player.Id);
            frame.Set("partner", command.PartnerId);
            frame.Set("give_credits", command.GiveCredits);
            frame.Set("give_cards", string.Join(",", command.GiveCards));
            frame.Set("take_credits", command.TakeCredits);
            frame.Set("take_cards", string.Join(",", command.TakeCards));
            return GameEngine.StartResolution(content, state, frame);
        }

        internal static List<string> Split(string list)
        {
            return list.Length == 0 ? new List<string>() : new List<string>(list.Split(','));
        }
    }

    /// <summary>
    /// The trade action as a frame: the partner accepts or refuses the whole offer (R-16.10: trades need not be
    /// equal); on acceptance credits and cards change hands at once, a captured contact going with its bounty card
    /// (R-16.13) and a rotated asset keeping its orientation (R-14.11), and the trade action is used (R-4.9); then
    /// "when you gain" abilities and goals of both sides see the received cards (R-14.2). Slots are settled once the
    /// command ends, each side settling its own (R-14.18). A refusal or a trade that falls through (R-9.7) leaves the
    /// action unused.
    /// </summary>
    internal sealed class TradeHandler : IResolutionHandler
    {
        public string Id => Trades.Handler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Get("player"))!;
            var partner = state.FindPlayer(frame.Get("partner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    ctx.Events.Add(new TradeOfferedEvent(player.Id, partner.Id, frame.GetInt("give_credits"), Trades.Split(frame.Get("give_cards")),
                        frame.GetInt("take_credits"), Trades.Split(frame.Get("take_cards"))));
                    var options = new[] { new DecisionOption("accept", "rules.trade_accept"), new DecisionOption("refuse", "rules.trade_refuse") };
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, partner.Id, "rules.trade_offer", options, "answer"));
                }
                case 1:
                {
                    frame.Step = 2;
                    bool accepted = frame.Get("answer") == "accept";
                    ctx.Events.Add(new TradeAnsweredEvent(player.Id, partner.Id, accepted));
                    if (!accepted)
                    {
                        return StepResult.Done();
                    }
                    // R-9.7, R-16.11: only what can change hands now counts. Credits may have been given away
                    // while the partner was deciding (R-16.16); a trade that cannot be paid in full falls through,
                    // and the action stays unused.
                    if (player.Credits < frame.GetInt("give_credits") || partner.Credits < frame.GetInt("take_credits")
                        || !Holds(player, Trades.Split(frame.Get("give_cards"))) || !Holds(partner, Trades.Split(frame.Get("take_cards"))))
                    {
                        ctx.Events.Add(new TradeFailedEvent(player.Id, partner.Id));
                        return StepResult.Done();
                    }
                    state.UsedAbilities.Add(Trades.ActionKey); // R-4.9: only a trade carried out uses the action
                    var gained = new List<string>();
                    Move(ctx, player, partner, frame.GetInt("give_credits"), Trades.Split(frame.Get("give_cards")), gained);
                    Move(ctx, partner, player, frame.GetInt("take_credits"), Trades.Split(frame.Get("take_cards")), gained);
                    frame.Set("gained", string.Join("|", gained));
                    return StepResult.Continue();
                }
                case 2:
                {
                    // R-14.2: a traded asset is gained, so "when you gain" abilities and goals see it (GX1 Short Hauler).
                    var queue = frame.Get("gained").Length == 0 ? new List<string>() : new List<string>(frame.Get("gained").Split('|'));
                    if (queue.Count == 0)
                    {
                        return StepResult.Done();
                    }
                    var next = queue[0].Split(':');
                    queue.RemoveAt(0);
                    frame.Set("gained", string.Join("|", queue));
                    return StepResult.Call(TriggersHandler.For("asset_gained", next[0],
                        new Dictionary<string, string> { ["gained_type"] = next[1] }));
                }
                default:
                    return StepResult.Done();
            }
        }

        private static bool Holds(PlayerState owner, List<string> cards)
        {
            foreach (var id in cards)
            {
                if (Assets.Find(owner, id) == null) return false;
            }
            return true;
        }

        private static void Move(ResolutionContext ctx, PlayerState from, PlayerState to, int credits, List<string> cards, List<string> gained)
        {
            if (credits > 0)
            {
                from.Credits -= credits;
                to.Credits += credits;
                ctx.Events.Add(new CreditsGivenEvent(from.Id, to.Id, credits));
            }
            foreach (var id in cards)
            {
                var asset = Assets.Find(from, id)!;
                from.Assets.Remove(asset);
                to.Assets.Add(asset);
                ctx.Events.Add(new AssetTradedEvent(from.Id, to.Id, id, asset.Kind));
                gained.Add(to.Id + ":" + asset.Kind);
            }
        }
    }
}
