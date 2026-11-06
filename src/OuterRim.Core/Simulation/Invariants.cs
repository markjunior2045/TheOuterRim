#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Simulation
{
    /// <summary>
    /// Checks of the state that must hold between commands (RULES §26.3), plus structural checks of the
    /// engine itself. Each returns a readable message per violation; an empty list means the state is sound.
    /// </summary>
    public static class Invariants
    {
        private static readonly Faction[] Factions = { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate };

        public static List<string> Check(GameContent content, GameState state)
        {
            var errors = new List<string>();
            foreach (var p in state.Players)
            {
                // R-8.4: damage never exceeds health or hull. A health bonus lost mid-operation (its card
                // discarded) leaves the excess until the operation ends and the engine settles it.
                bool settled = state.Pending == null && state.Phase == GamePhase.Playing;
                int health = content.HasCharacter(p.CharacterId) ? Combat.Health(content, state, p) : int.MaxValue;
                int hull = Combat.Hull(content, state, p);
                if (p.CharacterDamage < 0 || (settled && p.CharacterDamage > health))
                {
                    errors.Add("R-8.4: " + p.Id + " has " + p.CharacterDamage + " character damage, health " + health + ".");
                }
                if (p.ShipDamage < 0 || (settled && p.ShipDamage > hull))
                {
                    errors.Add("R-8.4: " + p.Id + " has " + p.ShipDamage + " ship damage, hull " + hull + ".");
                }
                // R-18.14: fame never goes below 0.
                if (p.Fame < 0)
                {
                    errors.Add("R-18.14: " + p.Id + " has " + p.Fame + " fame.");
                }
                // R-9.8: credits are never negative.
                if (p.Credits < 0)
                {
                    errors.Add("R-9.8: " + p.Id + " has " + p.Credits + " credits.");
                }
                // R-10.4: reputation is positive, neutral or negative with each faction.
                foreach (var f in Factions)
                {
                    if (!Enum.IsDefined(typeof(Reputation), p.Reputation.Get(f)))
                    {
                        errors.Add("R-10.4: " + p.Id + " has reputation " + (int)p.Reputation.Get(f) + " with " + f + ".");
                    }
                }
                if (!content.Map.Contains(p.SpaceId))
                {
                    errors.Add("R-5: " + p.Id + " is on an unknown space '" + p.SpaceId + "'.");
                }
                // R-14.18: every asset sits in a valid slot at the end of any operation. While a decision is
                // pending the operation is still running (the player may be choosing what to discard). A game
                // won mid-effect (R-18.8) stops at once, before a goal's sheet flip can make room (GX1).
                if (settled && !Assets.AllFit(content, p))
                {
                    errors.Add("R-14.18: " + p.Id + "'s assets do not fit their slots.");
                }
            }

            // R-11.9: exactly one patrol of each faction on the map at all times.
            foreach (var f in Factions)
            {
                int count = 0;
                foreach (var patrol in state.Patrols)
                {
                    if (patrol.Faction != f) continue;
                    count++;
                    if (!content.Map.Contains(patrol.SpaceId))
                    {
                        errors.Add("R-11.9: the " + f + " patrol is on an unknown space '" + patrol.SpaceId + "'.");
                    }
                    if (patrol.Level < 1 || patrol.Level > 4)
                    {
                        errors.Add("R-11.3: the " + f + " patrol has level " + patrol.Level + ".");
                    }
                }
                if (count != 1)
                {
                    errors.Add("R-11.9: " + count + " " + f + " patrols on the map.");
                }
            }
            if (state.Patrols.Count != Factions.Length)
            {
                errors.Add("R-11.9: " + state.Patrols.Count + " patrols on the map.");
            }

            // A card is in one place only: a deck, a player's assets, or a player's secrets.
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var deck in state.MarketDecks)
            {
                foreach (var card in deck.Cards) Place(seen, card, "market deck " + deck.Deck, errors);
            }
            foreach (var deck in state.EncounterDecks)
            {
                foreach (var card in deck.Cards) Place(seen, card, "encounter deck " + deck.Deck, errors);
            }
            foreach (var p in state.Players)
            {
                foreach (var a in p.Assets) Place(seen, a.CardId, p.Id + "'s assets", errors);
            }

            // The engine: between commands the resolution stack is empty unless it waits on a decision (T1.2).
            if (state.Phase == GamePhase.Playing)
            {
                if (state.Pending == null && state.Stack.Count > 0)
                {
                    errors.Add("Engine: " + state.Stack.Count + " frames left on the stack with no pending decision.");
                }
                if (state.Pending != null && state.FindPlayer(state.Pending.PlayerId) == null)
                {
                    errors.Add("Engine: decision " + state.Pending.Id + " is for unknown player '" + state.Pending.PlayerId + "'.");
                }
                if (state.Pending != null && state.Pending.Options.Count == 0 && state.Pending.Kind != DecisionKind.ChooseOrder)
                {
                    errors.Add("Engine: decision " + state.Pending.Id + " (" + state.Pending.Prompt + ") has no options.");
                }
                if (state.FindPlayer(state.CurrentPlayerId) == null)
                {
                    errors.Add("R-4.1: unknown current player '" + state.CurrentPlayerId + "'.");
                }
                // R-9.6, R-16.15: an open credit request is between two players, one of them the current player;
                // requests expire with the turn, so none survives it.
                var pairs = new HashSet<string>(StringComparer.Ordinal);
                foreach (var r in state.CreditRequests)
                {
                    if (state.FindPlayer(r.FromId) == null || state.FindPlayer(r.ToId) == null || r.FromId == r.ToId || r.Amount <= 0)
                    {
                        errors.Add("R-9.6: credit request " + r.Id + " is malformed.");
                    }
                    if (r.FromId != state.CurrentPlayerId && r.ToId != state.CurrentPlayerId)
                    {
                        errors.Add("R-16.15: credit request " + r.Id + " does not involve the current player " + state.CurrentPlayerId + ".");
                    }
                    var a = string.CompareOrdinal(r.FromId, r.ToId) < 0 ? r.FromId + "|" + r.ToId : r.ToId + "|" + r.FromId;
                    if (!pairs.Add(a))
                    {
                        errors.Add("Two credit requests are open between " + a + ".");
                    }
                }
            }
            if (state.Phase == GamePhase.Ended)
            {
                // R-18.8: the game ends when a player reaches the fame requirement.
                var winner = state.FindPlayer(state.WinnerId);
                if (winner == null)
                {
                    errors.Add("R-18.8: the game ended without a winner.");
                }
                else if (PlayerRules.Fame(content, state, winner) < state.FameRequirement)
                {
                    errors.Add("R-18.8: the winner " + winner.Id + " has " + PlayerRules.Fame(content, state, winner) + " fame.");
                }
            }
            return errors;
        }

        /// <summary>Each player's credits, to compare across a command with <see cref="CreditsFollowEvents"/>.</summary>
        public static Dictionary<string, int> Credits(GameState state)
        {
            var credits = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var p in state.Players) credits[p.Id] = p.Credits;
            return credits;
        }

        /// <summary>
        /// Credits change only through their events: gained, lost or spent, and handed between players. Trades
        /// and gifts (R-9.6, R-16.8, R-16.15) therefore create and destroy no credits, and the interface can show
        /// every change.
        /// </summary>
        public static List<string> CreditsFollowEvents(Dictionary<string, int> before, GameState state, IReadOnlyList<GameEvent> events)
        {
            var expected = new Dictionary<string, int>(before, StringComparer.Ordinal);
            foreach (var e in events)
            {
                switch (e)
                {
                    case CreditsGainedEvent g: expected[g.PlayerId] += g.Amount; break;
                    case CreditsLostEvent l: expected[l.PlayerId] -= l.Amount; break;
                    case CreditsGivenEvent t:
                        expected[t.FromId] -= t.Amount;
                        expected[t.ToId] += t.Amount;
                        break;
                }
            }
            var errors = new List<string>();
            foreach (var p in state.Players)
            {
                if (expected[p.Id] != p.Credits)
                {
                    errors.Add("Credits: " + p.Id + " has " + p.Credits + ", but the events account for " + expected[p.Id] + ".");
                }
            }
            return errors;
        }

        private static void Place(Dictionary<string, string> seen, string card, string where, List<string> errors)
        {
            if (seen.TryGetValue(card, out var first))
            {
                errors.Add("Card " + card + " is both in " + first + " and in " + where + ".");
            }
            else
            {
                seen[card] = where;
            }
        }
    }

    /// <summary>
    /// Follows a game command by command and checks the invariants that span several commands
    /// (RULES §26.3): one encounter per turn (R-4.12) and each action at most once per action step (R-4.9).
    /// The encounter counted is the one the player chooses in the encounter step (R-4.13, R-4.16): an
    /// encounter a card resolves inside it ("encounter the patrol in your space") is part of that one.
    /// A turn may end before its encounter step (defeat R-8.8.4, or a card such as databank #1 that
    /// ends the turn); a turn that reaches the encounter step must end through an encounter.
    /// </summary>
    public sealed class InvariantMonitor
    {
        private string _turnPlayer = "";
        private int _encounters;
        private TurnStep _step = TurnStep.Planning;
        private int _deliveries;
        private int _buys;
        private int _discards;
        private int _trades;
        private readonly HashSet<string> _actionAbilities = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Reads the events of the game's creation.</summary>
        public List<string> Start(IReadOnlyList<GameEvent> events)
        {
            var errors = new List<string>();
            Read(events, errors);
            return errors;
        }

        /// <summary>Checks an accepted command against the step it was issued in, then reads its events.</summary>
        public List<string> After(Command command, CommandResult result)
        {
            var errors = new List<string>();
            if (!result.Accepted)
            {
                return errors;
            }
            if (_step == TurnStep.Encounter && (command is EncounterCommand || command is UseAbilityCommand))
            {
                // R-4.12: exactly one encounter per turn.
                if (++_encounters > 1) errors.Add("R-4.12: a second encounter in " + _turnPlayer + "'s turn.");
            }
            if (_step == TurnStep.Action)
            {
                // R-4.9, R-4.11, R-16.1, R-16.19: each action at most once per action step.
                switch (command)
                {
                    case DeliverCommand _:
                        if (++_deliveries > 1) errors.Add("R-4.9: a second deliver action in one action step.");
                        break;
                    case MarketBuyCommand _:
                        if (++_buys > 1) errors.Add("R-4.9: a second market purchase in one action step.");
                        break;
                    case MarketDiscardCommand _:
                        if (++_discards > 2) errors.Add("R-16.19: a third market discard in one action step.");
                        break;
                    case ChooseOptionCommand _:
                        // R-4.9: at most one trade carried out per action step (a refused or failed offer does not count).
                        bool carried = false;
                        foreach (var e in result.Events)
                        {
                            if (e is TradeAnsweredEvent t && t.Accepted) carried = true;
                            if (e is TradeFailedEvent) carried = false;
                        }
                        if (carried && ++_trades > 1) errors.Add("R-4.9: a second trade in one action step.");
                        break;
                    case UseAbilityCommand use:
                        if (!_actionAbilities.Add(use.CardId + "#" + use.Path))
                        {
                            errors.Add("R-4.11: Action ability " + use.CardId + " " + use.Path + " used twice in one action step.");
                        }
                        break;
                }
            }
            Read(result.Events, errors);
            return errors;
        }

        private void Read(IReadOnlyList<GameEvent> events, List<string> errors)
        {
            foreach (var e in events)
            {
                switch (e)
                {
                    case TurnStartedEvent t:
                        _turnPlayer = t.PlayerId;
                        _encounters = 0;
                        break;
                    case StepStartedEvent s:
                        _step = s.Step;
                        if (s.Step == TurnStep.Action)
                        {
                            _deliveries = 0;
                            _buys = 0;
                            _discards = 0;
                            _trades = 0;
                            _actionAbilities.Clear();
                        }
                        break;
                    case TurnEndedEvent end:
                        if (_step == TurnStep.Encounter && _encounters == 0)
                        {
                            errors.Add("R-4.12: " + end.PlayerId + "'s turn ended in the encounter step with no encounter.");
                        }
                        break;
                }
            }
        }
    }
}
