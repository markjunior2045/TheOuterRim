#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Trading (RULES §16.2, R-9.6, R-9.7). Two digital forms of table talk (reading in docs/REVIEW.md):
    /// giving credits, one-sided, at any moment of the current player's turn between them and another player
    /// (R-16.15, R-16.16); and the trade action, an offer of credits and cards the partner accepts or refuses
    /// (R-16.8 to R-16.14). Only what changes hands at once counts (R-9.7, R-16.11): there are no promises.
    /// </summary>
    public static class Trades
    {
        public const string Handler = "rules.trade";

        /// <summary>R-4.9: the trade action, once per action step.</summary>
        public const string ActionKey = "action:trade";

        /// <summary>
        /// R-16.15: credits change hands between the current player and another player at any point of the turn,
        /// even in the middle of an ability, an encounter or a combat (R-16.16). Not an action.
        /// </summary>
        internal static CommandResult GiveCredits(GameState state, PlayerState giver, GiveCreditsCommand command)
        {
            var receiver = state.FindPlayer(command.ToPlayerId);
            if (receiver == null || receiver.Id == giver.Id)
            {
                return CommandResult.Rejected("Give credits to another player.");
            }
            if (giver.Id != state.CurrentPlayerId && receiver.Id != state.CurrentPlayerId)
            {
                return CommandResult.Rejected("R-9.6, R-16.15: credits are exchanged with the current player.");
            }
            if (command.Amount <= 0)
            {
                return CommandResult.Rejected("Give a positive amount.");
            }
            if (giver.Credits < command.Amount)
            {
                return CommandResult.Rejected("R-9.8: you have " + giver.Credits + " credits.");
            }
            giver.Credits -= command.Amount;
            receiver.Credits += command.Amount;
            return CommandResult.Ok(new GameEvent[] { new CreditsGivenEvent(giver.Id, receiver.Id, command.Amount) });
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

        /// <summary>Starts the offer: the trade action is used (R-4.9) and the partner is asked.</summary>
        internal static CommandResult Offer(GameContent content, GameState state, PlayerState player, OfferTradeCommand command)
        {
            var error = Validate(state, player, command.PartnerId, command.GiveCredits, command.GiveCards, command.TakeCredits, command.TakeCards);
            if (error != null)
            {
                return CommandResult.Rejected(error);
            }
            state.UsedAbilities.Add(ActionKey);
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
    /// The trade action as a frame: the partner accepts or refuses (R-16.10: trades need not be equal); on
    /// acceptance credits and cards change hands at once, a captured contact going with its bounty card (R-16.13)
    /// and a rotated asset keeping its orientation (R-14.11); then "when you gain" abilities of both sides. Slots
    /// are settled once the command ends (R-14.18).
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
                    // while the partner was deciding (R-16.16); a trade that cannot be paid in full falls through.
                    if (player.Credits < frame.GetInt("give_credits") || partner.Credits < frame.GetInt("take_credits")
                        || !Holds(player, Trades.Split(frame.Get("give_cards"))) || !Holds(partner, Trades.Split(frame.Get("take_cards"))))
                    {
                        ctx.Events.Add(new TradeFailedEvent(player.Id, partner.Id));
                        return StepResult.Done();
                    }
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
