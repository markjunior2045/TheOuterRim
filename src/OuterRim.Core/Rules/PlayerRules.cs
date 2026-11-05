#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>Credits, reputation, fame, damage and defeat: the player-state rules every effect goes through.</summary>
    public static class PlayerRules
    {
        /// <summary>R-8.8.1: credits lost on becoming defeated.</summary>
        public const int DefeatCredits = 3000;

        // ---- credits (R-9) ------------------------------------------------------------------

        public static void GainCredits(PlayerState player, int amount, List<GameEvent> events)
        {
            if (amount <= 0)
            {
                return;
            }
            player.Credits += amount;
            events.Add(new CreditsGainedEvent(player.Id, amount));
        }

        /// <summary>R-9.8: spending is a payment and needs the full amount. Returns false, changing nothing, otherwise.</summary>
        public static bool TrySpend(PlayerState player, int amount, List<GameEvent> events)
        {
            if (amount < 0 || player.Credits < amount)
            {
                return false;
            }
            player.Credits -= amount;
            events.Add(new CreditsLostEvent(player.Id, amount, true));
            return true;
        }

        /// <summary>R-9.8, R-21.5: losing credits removes as much as the player has, down to 0. Returns the amount lost.</summary>
        public static int LoseCredits(PlayerState player, int amount, List<GameEvent> events)
        {
            int lost = Math.Min(Math.Max(amount, 0), player.Credits);
            player.Credits -= lost;
            if (lost > 0)
            {
                events.Add(new CreditsLostEvent(player.Id, lost, false));
            }
            return lost;
        }

        // ---- reputation (R-10) ----------------------------------------------------------------

        /// <summary>R-10.5: each gain moves one step toward positive; at positive, further gains do nothing.</summary>
        public static void GainReputation(PlayerState player, Faction faction, int amount, List<GameEvent> events)
        {
            Shift(player, faction, Math.Max(amount, 0), events);
        }

        /// <summary>R-10.6: each loss moves one step toward negative; at negative, further losses do nothing.</summary>
        public static void LoseReputation(PlayerState player, Faction faction, int amount, List<GameEvent> events)
        {
            Shift(player, faction, -Math.Max(amount, 0), events);
        }

        /// <summary>R-10.7: setting a reputation moves it straight to the value.</summary>
        public static void SetReputation(PlayerState player, Faction faction, Reputation value, List<GameEvent> events)
        {
            var before = player.Reputation.Get(faction);
            if (before != value)
            {
                player.Reputation.Set(faction, value);
                events.Add(new ReputationChangedEvent(player.Id, faction, before, value));
            }
        }

        private static void Shift(PlayerState player, Faction faction, int steps, List<GameEvent> events)
        {
            var before = player.Reputation.Get(faction);
            int value = Math.Max(-1, Math.Min(1, (int)before + steps));
            SetReputation(player, faction, (Reputation)value, events);
        }

        // ---- fame (R-18) ------------------------------------------------------------------------

        public static void GainFame(PlayerState player, int amount, List<GameEvent> events)
        {
            if (amount <= 0)
            {
                return;
            }
            player.Fame += amount;
            events.Add(new FameChangedEvent(player.Id, amount, player.Fame));
        }

        /// <summary>R-18.14: fame never goes below zero. Returns the fame actually lost.</summary>
        public static int LoseFame(PlayerState player, int amount, List<GameEvent> events)
        {
            int lost = Math.Min(Math.Max(amount, 0), player.Fame);
            if (lost > 0)
            {
                player.Fame -= lost;
                events.Add(new FameChangedEvent(player.Id, -lost, player.Fame));
            }
            return lost;
        }

        /// <summary>
        /// Fame that counts for victory and comparisons: printed fame plus "you have X additional fame"
        /// from assets (R-18.15), which the effect engine adds (PLAN T2.3).
        /// </summary>
        public static int Fame(PlayerState player)
        {
            return player.Fame;
        }

        /// <summary>R-18.17: the player with the most fame; on a tie, every tied player is the most famous.</summary>
        public static bool IsMostFamous(GameState state, PlayerState player)
        {
            foreach (var other in state.Players)
            {
                if (Fame(other) > Fame(player))
                {
                    return false;
                }
            }
            return true;
        }

        // ---- damage and defeat (R-8) ----------------------------------------------------------

        /// <summary>
        /// Puts damage on the character or ship (R-8.1, R-8.4) and applies defeat if the player reaches
        /// their health or hull (R-8.2, R-8.3, R-8.8). Returns the damage placed.
        /// </summary>
        public static int SufferDamage(GameContent content, GameState state, PlayerState player, CombatType target, int amount,
            List<GameEvent> events)
        {
            bool wasDefeated = player.Defeated;
            int placed = Combat.Suffer(content, state, player, target, amount);
            if (amount > 0)
            {
                events.Add(new DamageSufferedEvent(player.Id, target == CombatType.Ground ? "character" : "ship",
                    amount, placed, player.Defeated && !wasDefeated));
            }
            if (player.Defeated && !wasDefeated)
            {
                BecomeDefeated(state, player, events);
            }
            return placed;
        }

        /// <summary>
        /// R-8.8: lose 3,000 credits (or all), discard all secrets (R-15.89), lay the standee down; the
        /// current player then finishes the current action or step and their turn ends.
        /// </summary>
        public static void BecomeDefeated(GameState state, PlayerState player, List<GameEvent> events)
        {
            player.Defeated = true;
            events.Add(new PlayerDefeatedEvent(player.Id));
            LoseCredits(player, DefeatCredits, events);
            if (player.Secrets.Count > 0)
            {
                // R-15.89: secrets go to the bottom of their encounter decks, unrevealed (decks: PLAN T2.6).
                events.Add(new SecretsDiscardedEvent(player.Id, new List<string>(player.Secrets)));
                player.Secrets.Clear();
            }
            if (player.Id == state.CurrentPlayerId && state.Phase == GamePhase.Playing)
            {
                state.TurnEndsAfterStep = true;
            }
        }

        /// <summary>
        /// Removes damage. R-8.10: once below both health and hull, the player is no longer defeated,
        /// even outside the planning step.
        /// </summary>
        public static void RecoverDamage(GameContent content, GameState state, PlayerState player, CombatType target, int amount, List<GameEvent> events)
        {
            int before = target == CombatType.Ground ? player.CharacterDamage : player.ShipDamage;
            int removed = Math.Min(Math.Max(amount, 0), before);
            if (target == CombatType.Ground)
            {
                player.CharacterDamage -= removed;
            }
            else
            {
                player.ShipDamage -= removed;
            }
            if (removed > 0)
            {
                events.Add(new DamageRecoveredEvent(player.Id, target == CombatType.Ground ? removed : 0,
                    target == CombatType.Ship ? removed : 0));
            }
            if (player.Defeated && player.CharacterDamage < Combat.Health(content, state, player)
                && player.ShipDamage < Combat.Hull(content, state, player))
            {
                player.Defeated = false;
                events.Add(new PlayerRecoveredEvent(player.Id));
            }
        }
    }

    /// <summary>Winning the game (R-18.8, R-18.16).</summary>
    public static class Victory
    {
        /// <summary>
        /// Ends the game if any player has reached the fame requirement (R-18.8). Called after every
        /// resolution step, so players who reach it in the same step reached it simultaneously and the
        /// tiebreak applies (R-18.16). Returns true when the game has ended.
        /// </summary>
        public static bool Check(GameState state, List<GameEvent> events)
        {
            if (state.Phase != GamePhase.Playing)
            {
                return state.Phase == GamePhase.Ended;
            }
            var reached = new List<PlayerState>();
            foreach (var p in state.Players)
            {
                if (PlayerRules.Fame(p) >= state.FameRequirement)
                {
                    reached.Add(p);
                }
            }
            if (reached.Count == 0)
            {
                return false;
            }
            var winner = Tiebreak(state, reached);
            state.Phase = GamePhase.Ended;
            state.WinnerId = winner.Id;
            state.Stack.Clear();
            state.Pending = null;
            events.Add(new GameWonEvent(winner.Id, reached.Count > 1));
            return true;
        }

        /// <summary>R-18.16: most credits; still tied, the player seated farthest clockwise from the first player.</summary>
        public static PlayerState Tiebreak(GameState state, List<PlayerState> tied)
        {
            int firstSeat = state.SeatOf(state.FirstPlayerId);
            PlayerState best = tied[0];
            foreach (var p in tied)
            {
                if (p.Credits > best.Credits)
                {
                    best = p;
                }
                else if (p.Credits == best.Credits && Distance(state, firstSeat, p) > Distance(state, firstSeat, best))
                {
                    best = p;
                }
            }
            return best;
        }

        private static int Distance(GameState state, int firstSeat, PlayerState p)
        {
            return (state.SeatOf(p.Id) - firstSeat + state.Players.Count) % state.Players.Count;
        }
    }
}
