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
    }
}
