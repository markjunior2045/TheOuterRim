#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Model;

namespace OuterRim.Core.Effects
{
    /// <summary>
    /// Passive abilities read as queries (EFFECTS.md §3.1, §11): nothing caches a modified value.
    /// R-5.19, R-7.19: "Gain +X" on a held card adds to the matching value, and these stack.
    /// </summary>
    public static class Modifiers
    {
        /// <summary>Sum of the passive "stat.modify" bonuses for a stat: ground_combat, ship_combat, hyperdrive, hull, health.</summary>
        public static int StatBonus(GameContent content, GameState state, PlayerState player, string stat)
        {
            int total = 0;
            foreach (var ability in AbilitySources.Of(content, player))
            {
                if (ability.TimingType != "passive")
                {
                    continue;
                }
                if (!Conditions.Eval(content, state, player, ability.Ability["condition"]))
                {
                    continue;
                }
                foreach (JObject effect in (JArray)ability.Ability["effects"]!)
                {
                    if ((string?)effect["op"] == "stat.modify" && (string?)effect["stat"] == stat && effect["duration"] == null)
                    {
                        total += Selectors.Amount(content, player, effect["by"]!);
                    }
                }
            }
            foreach (var m in state.TurnModifiers)
            {
                if (m.PlayerId == player.Id && m.Stat == stat) total += m.By;
            }
            return total;
        }

        /// <summary>
        /// "When you gain fame, gain 1 additional fame" (Chromium Hull): extra fame added to every gain, read from
        /// the player's "fame_gained" abilities. Applied by <see cref="Rules.PlayerRules.GainFame(GameContent, GameState, PlayerState, int, List{Engine.GameEvent})"/>.
        /// </summary>
        public static int FameGainBonus(GameContent content, GameState state, PlayerState player)
        {
            int total = 0;
            foreach (var ability in AbilitySources.Of(content, player))
            {
                if (ability.TimingType != "trigger" || (string?)ability.Ability["timing"]!["event"] != "fame_gained") continue;
                if (!Conditions.Eval(content, state, player, ability.Ability["condition"])) continue;
                foreach (JObject effect in (JArray)ability.Ability["effects"]!)
                {
                    if ((string?)effect["op"] == "fame.gain") total += (int)effect["amount"]!;
                }
            }
            return total;
        }

        /// <summary>"While ..., you have N additional fame" (Extravagant Wardrobe, Dejarik Holotable; R-18.15, R-25.3).</summary>
        public static int ExtraFame(GameContent content, GameState state, PlayerState player)
        {
            int total = 0;
            foreach (var ability in AbilitySources.Of(content, player))
            {
                if (ability.TimingType != "passive" || !Conditions.Eval(content, state, player, ability.Ability["condition"]))
                {
                    continue;
                }
                foreach (JObject effect in (JArray)ability.Ability["effects"]!)
                {
                    if ((string?)effect["op"] == "fame.extra") total += (int)effect["amount"]!;
                }
            }
            return total;
        }

        /// <summary>K-2SO: the value of the opponent's {CRIT} results in this combat, for the player holding the card.</summary>
        public static int OpponentCritValue(GameContent content, GameState state, PlayerState player, string combatType)
        {
            int value = 2;
            var context = new Dictionary<string, string> { ["combat_type"] = combatType };
            foreach (var a in AbilitySources.Of(content, player))
            {
                if (a.TimingType != "passive" || !Conditions.Eval(content, state, player, a.Ability["condition"], null, context)) continue;
                foreach (JObject e in (JArray)a.Ability["effects"]!)
                {
                    if ((string?)e["op"] == "damage.crit_value") value = Math.Min(value, (int)e["value"]!);
                }
            }
            return value;
        }

        /// <summary>Ponda Baba: an ability that wins the combat when the player's own results match its filter.</summary>
        public static bool AutoWins(GameContent content, GameState state, PlayerState player, string combatType, IReadOnlyList<DieFace> faces)
        {
            var context = new Dictionary<string, string> { ["combat_type"] = combatType, ["dice"] = Dice.DicePool.Encode(faces) };
            string scope = combatType == "ship" ? "ship_combat" : "ground_combat";
            foreach (var a in AbilitySources.Of(content, player))
            {
                var timing = a.Ability["timing"]!;
                if ((string?)timing["type"] != "window" || !((JArray)timing["scope"]!).ToObject<List<string>>()!.Contains(scope)) continue;
                bool wins = false;
                foreach (JObject e in (JArray)a.Ability["effects"]!)
                {
                    wins |= (string?)e["op"] == "combat.auto_win";
                }
                if (wins && Conditions.Eval(content, state, player, timing["filter"], null, context)) return true;
            }
            return false;
        }

        /// <summary>Whether a list of payments ("credits.spend") can be paid in full now (R-9.8).</summary>
        public static bool CanPay(GameContent content, PlayerState player, JArray effects)
        {
            int credits = 0;
            foreach (JObject e in effects)
            {
                // R-9.8: "unless you lose X" offered as a way out is a payment too.
                if ((string?)e["op"] == "credits.spend" || (string?)e["op"] == "credits.lose") credits += Selectors.Amount(content, player, e["amount"]!);
            }
            return player.Credits >= credits;
        }

        /// <summary>
        /// Whether a held card changes a core rule for the player (EFFECTS.md §3.1 "rule.modify"):
        /// "no_reputation_loss_on_patrol_win" (IG-2000), "not_forced_to_encounter_negative_patrols" (Valorous).
        /// </summary>
        public static bool HasRule(GameContent content, GameState state, PlayerState player, string rule)
        {
            foreach (var ability in AbilitySources.Of(content, player))
            {
                if (ability.TimingType != "passive" || !Conditions.Eval(content, state, player, ability.Ability["condition"]))
                {
                    continue;
                }
                foreach (JObject effect in (JArray)ability.Ability["effects"]!)
                {
                    if ((string?)effect["op"] == "rule.modify" && (string?)effect["rule"] == rule)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Dice the opponent of <paramref name="player"/> rolls fewer in a combat of this type
        /// ("During ground combat, your opponent rolls 1 fewer die": Plastoid Armor, Maneuvering Thrusters).
        /// </summary>
        public static int OpponentFewer(GameContent content, GameState state, PlayerState player, string combatType)
        {
            int total = 0;
            foreach (var ability in AbilitySources.Of(content, player))
            {
                if (ability.TimingType != "passive" || !Conditions.Eval(content, state, player, ability.Ability["condition"]))
                {
                    continue;
                }
                foreach (JObject effect in (JArray)ability.Ability["effects"]!)
                {
                    if ((string?)effect["op"] == "dice.opponent_fewer" && (string?)effect["combat"] == combatType)
                    {
                        total += (int)effect["count"]!;
                    }
                }
            }
            return total;
        }
    }
}
