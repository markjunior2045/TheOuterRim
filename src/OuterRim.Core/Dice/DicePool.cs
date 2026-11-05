#nullable enable
using System.Collections.Generic;
using System.Text;
using OuterRim.Core.Model;

namespace OuterRim.Core.Dice
{
    /// <summary>One die of a roll, with what abilities did to it.</summary>
    public sealed class Die
    {
        public Die(DieFace face, bool added = false, bool turned = false, bool canceled = false)
        {
            Face = face;
            Added = added;
            Turned = turned;
            Canceled = canceled;
        }

        public DieFace Face { get; set; }

        /// <summary>R-7.17: an added result cannot be rerolled.</summary>
        public bool Added { get; set; }

        /// <summary>R-7.16: a turned die takes no further changes meant for its original result.</summary>
        public bool Turned { get; set; }

        /// <summary>R-7.15: a cancelled result is ignored: no damage, no effect on the winner or the test.</summary>
        public bool Canceled { get; set; }
    }

    /// <summary>
    /// A roll as stored in a frame variable: one code per die, comma-separated. The first letter is the
    /// face (h hit, c crit, f focus, b blank), followed by flags (a added, t turned, x cancelled).
    /// </summary>
    public static class DicePool
    {
        public static List<Die> Decode(string text)
        {
            var result = new List<Die>();
            if (text.Length == 0)
            {
                return result;
            }
            foreach (var code in text.Split(','))
            {
                var die = new Die(FaceOf(code[0]));
                for (int i = 1; i < code.Length; i++)
                {
                    switch (code[i])
                    {
                        case 'a': die.Added = true; break;
                        case 't': die.Turned = true; break;
                        case 'x': die.Canceled = true; break;
                    }
                }
                result.Add(die);
            }
            return result;
        }

        public static string Encode(IEnumerable<Die> dice)
        {
            var parts = new List<string>();
            foreach (var d in dice)
            {
                var sb = new StringBuilder();
                sb.Append(CodeOf(d.Face));
                if (d.Added) sb.Append('a');
                if (d.Turned) sb.Append('t');
                if (d.Canceled) sb.Append('x');
                parts.Add(sb.ToString());
            }
            return string.Join(",", parts);
        }

        public static string Encode(IEnumerable<DieFace> faces)
        {
            var dice = new List<Die>();
            foreach (var f in faces)
            {
                dice.Add(new Die(f));
            }
            return Encode(dice);
        }

        /// <summary>The results that count: every die not cancelled (R-7.15).</summary>
        public static List<DieFace> Effective(IEnumerable<Die> dice)
        {
            var faces = new List<DieFace>();
            foreach (var d in dice)
            {
                if (!d.Canceled)
                {
                    faces.Add(d.Face);
                }
            }
            return faces;
        }

        public static DieFace FaceOf(char code)
        {
            switch (code)
            {
                case 'h': return DieFace.Hit;
                case 'c': return DieFace.Crit;
                case 'f': return DieFace.Focus;
                default: return DieFace.Blank;
            }
        }

        public static char CodeOf(DieFace face)
        {
            switch (face)
            {
                case DieFace.Hit: return 'h';
                case DieFace.Crit: return 'c';
                case DieFace.Focus: return 'f';
                default: return 'b';
            }
        }

        public static DieFace Parse(string face)
        {
            switch (face)
            {
                case "hit": return DieFace.Hit;
                case "crit": return DieFace.Crit;
                case "focus": return DieFace.Focus;
                default: return DieFace.Blank;
            }
        }
    }
}
