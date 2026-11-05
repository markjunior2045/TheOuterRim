#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>Ground or ship combat (R-7.2).</summary>
    public enum CombatType
    {
        Ground,
        Ship,
    }

    /// <summary>Combat arithmetic and damage (R-7, R-8).</summary>
    public static class Combat
    {
        /// <summary>
        /// Resolution handler id of a combat. Frame variables in:
        /// "type" (ground | ship); "attacker" (player id); "defender_kind" (player | npc | patrol);
        /// "defender" (player id, enemy name or patrol faction); "defender_value" (dice for an npc or
        /// patrol, or "infinite" for a level-4 patrol, R-11.6); optional "outcome" (handler id run after
        /// the winner is known and before damage, R-7.13). Returned: "ret.winner" (attacker | defender |
        /// none), "ret.attacker_damage", "ret.defender_damage".
        /// </summary>
        public const string Handler = "rules.combat";

        public const string Infinite = "infinite";

        /// <summary>Who rolls, as shown in events and logs: "Ketsu Onyo (Player 1)".</summary>
        public static string PlayerLabel(GameContent content, PlayerState player)
        {
            return player.CharacterId.Length > 0
                ? content.Name(player.CharacterId) + " (" + player.Name + ")"
                : player.Name;
        }

        /// <summary>An enemy the game rolls for (DECISIONS §4.2): "Hutt Patrol Lvl 2 (Game)", "TIE fighters (Game)".</summary>
        public static string GameLabel(string enemy)
        {
            return enemy + " (Game)";
        }

        public static string PatrolName(Faction faction, int level)
        {
            return faction + " Patrol Lvl " + level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>R-7.11: each hit deals 1 damage, each crit 2; focus and blank deal nothing.</summary>
        public static int DamageOf(IEnumerable<DieFace> faces)
        {
            int total = 0;
            foreach (var f in faces)
            {
                if (f == DieFace.Hit)
                {
                    total += 1;
                }
                else if (f == DieFace.Crit)
                {
                    total += 2;
                }
            }
            return total;
        }

        /// <summary>R-7.13: more damage wins; on a tie the attacker wins.</summary>
        public static bool AttackerWins(int attackerDamage, int defenderDamage)
        {
            return attackerDamage >= defenderDamage;
        }

        public static CombatType ParseType(string type)
        {
            switch (type)
            {
                case "ground": return CombatType.Ground;
                case "ship": return CombatType.Ship;
                default: throw new FormatException("Unknown combat type '" + type + "'.");
            }
        }

        /// <summary>R-7.5: player versus player is ground combat if both are on planets, ship combat otherwise.</summary>
        public static CombatType BetweenPlayers(GameContent content, PlayerState a, PlayerState b)
        {
            return content.Map[a.SpaceId].IsPlanet && content.Map[b.SpaceId].IsPlanet ? CombatType.Ground : CombatType.Ship;
        }

        /// <summary>Combat value: ground combat on the character, ship combat on the ship (R-7.10), plus passive bonuses (R-7.19).</summary>
        public static int Value(GameContent content, GameState state, PlayerState player, CombatType type)
        {
            return type == CombatType.Ground
                ? content.Character(player.CharacterId).GroundCombat + Modifiers.StatBonus(content, state, player, "ground_combat")
                : content.ShipSide(player.ShipSideId).ShipCombat + Modifiers.StatBonus(content, state, player, "ship_combat");
        }

        /// <summary>Character health (R-8.2) plus passive bonuses ("Gain +2 {HEALTH}").</summary>
        public static int Health(GameContent content, GameState state, PlayerState player)
        {
            return content.Character(player.CharacterId).Health + Modifiers.StatBonus(content, state, player, "health");
        }

        /// <summary>Ship hull (R-8.3) plus passive bonuses ("Gain +1 {HULL}").</summary>
        public static int Hull(GameContent content, GameState state, PlayerState player)
        {
            return content.ShipSide(player.ShipSideId).Hull + Modifiers.StatBonus(content, state, player, "hull");
        }

        /// <summary>
        /// Puts damage on the character (ground) or ship (ship). Damage beyond health or hull is ignored
        /// (R-8.4); reaching it makes the player defeated (R-8.2, R-8.3). Returns the damage actually placed.
        /// The consequences of becoming defeated (R-8.8) are applied by the caller (PLAN T1.7).
        /// </summary>
        public static int Suffer(GameContent content, GameState state, PlayerState player, CombatType target, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }
            int health = Health(content, state, player);
            int hull = Hull(content, state, player);
            int limit = target == CombatType.Ground ? health : hull;
            int current = target == CombatType.Ground ? player.CharacterDamage : player.ShipDamage;
            int placed = Math.Min(amount, Math.Max(0, limit - current));
            if (target == CombatType.Ground)
            {
                player.CharacterDamage += placed;
            }
            else
            {
                player.ShipDamage += placed;
            }
            if (player.CharacterDamage >= health || player.ShipDamage >= hull)
            {
                player.Defeated = true;
            }
            return placed;
        }
    }
}
