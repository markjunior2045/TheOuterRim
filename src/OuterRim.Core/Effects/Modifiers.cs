#nullable enable
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
