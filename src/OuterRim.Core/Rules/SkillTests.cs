#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>The seven skills (R-6.4).</summary>
    public enum Skill
    {
        Influence,
        Knowledge,
        Piloting,
        Stealth,
        Strength,
        Tactics,
        Tech,
    }

    /// <summary>Skill level by number of instances (R-6.7, R-6.9).</summary>
    public enum SkillLevel
    {
        Unskilled = 0,
        Skilled = 1,
        HighlySkilled = 2,
    }

    /// <summary>Skill tests (R-6.4 to R-6.11).</summary>
    public static class SkillTests
    {
        /// <summary>Resolution handler id of a skill test (frame vars: player, skills, optional granted).</summary>
        public const string Handler = "rules.skill_test";

        /// <summary>R-6.9: a skill test rolls two dice.</summary>
        public const int Dice = 2;

        public static Skill Parse(string id)
        {
            switch (id)
            {
                case "influence": return Skill.Influence;
                case "knowledge": return Skill.Knowledge;
                case "piloting": return Skill.Piloting;
                case "stealth": return Skill.Stealth;
                case "strength": return Skill.Strength;
                case "tactics": return Skill.Tactics;
                case "tech": return Skill.Tech;
                default: throw new FormatException("Unknown skill '" + id + "'.");
            }
        }

        public static string Id(Skill skill)
        {
            return skill.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// R-6.6, R-6.7: instances of a skill on the character card and on each crew, plus instances
        /// granted for this test by an ability (R-6.8, which may add to a skill the player already has).
        /// </summary>
        public static int Instances(GameContent content, PlayerState player, Skill skill, int granted = 0)
        {
            int n = granted;
            if (player.CharacterId.Length > 0 && content.Character(player.CharacterId).Skills.Contains(skill))
            {
                n++;
            }
            foreach (var crew in player.Crew)
            {
                if (content.CrewSkills(crew).Contains(skill))
                {
                    n++;
                }
            }
            return n;
        }

        public static SkillLevel Level(int instances)
        {
            return instances <= 0 ? SkillLevel.Unskilled : instances == 1 ? SkillLevel.Skilled : SkillLevel.HighlySkilled;
        }

        /// <summary>
        /// R-6.9: unskilled passes with a crit; skilled with a hit or crit; highly skilled with a hit, crit
        /// or focus. Blanks never pass.
        /// </summary>
        public static bool Passes(SkillLevel level, IEnumerable<DieFace> faces)
        {
            foreach (var f in faces)
            {
                switch (f)
                {
                    case DieFace.Crit:
                        return true;
                    case DieFace.Hit when level >= SkillLevel.Skilled:
                        return true;
                    case DieFace.Focus when level >= SkillLevel.HighlySkilled:
                        return true;
                }
            }
            return false;
        }
    }
}
