using System.Collections.Generic;
using OuterRim.Core.Model;
using OuterRim.Core.Random;

namespace OuterRim.Core.Dice
{
    /// <summary>Rolling the game's eight-sided dice. The core decides results; the UI only animates them (DECISIONS §7).</summary>
    public static class DiceRoller
    {
        /// <summary>R-6.1: each die has 8 faces: 3 hit, 1 crit, 2 focus, 2 blank.</summary>
        private static readonly DieFace[] Faces =
        {
            DieFace.Hit, DieFace.Hit, DieFace.Hit,
            DieFace.Crit,
            DieFace.Focus, DieFace.Focus,
            DieFace.Blank, DieFace.Blank,
        };

        public static List<DieFace> Roll(GameRandom random, int count)
        {
            var result = new List<DieFace>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(Faces[random.NextInt(Faces.Length)]);
            }
            return result;
        }

        public static int Count(IEnumerable<DieFace> faces, DieFace face)
        {
            int n = 0;
            foreach (var f in faces)
            {
                if (f == face)
                {
                    n++;
                }
            }
            return n;
        }
    }
}
