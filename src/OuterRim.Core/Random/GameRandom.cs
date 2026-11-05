using System;
using System.Collections.Generic;
using System.Globalization;

namespace OuterRim.Core.Random
{
    /// <summary>
    /// Seeded random number generator with serializable state (PCG32, XSH-RR variant).
    /// System.Random is not used: its sequence is not guaranteed to match between .NET and
    /// Unity's runtime, and the core must give the same results everywhere for the same seed
    /// (DECISIONS §7: seeded randomness, serializable state).
    /// </summary>
    public sealed class GameRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _increment;

        /// <summary>Creates a generator for a seed. The same seed always gives the same sequence.</summary>
        public GameRandom(ulong seed, ulong stream = 54UL)
        {
            _state = 0UL;
            _increment = (stream << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        private GameRandom()
        {
        }

        /// <summary>Internal state as two hexadecimal strings, for saves.</summary>
        public string StateHex => _state.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>Stream increment as a hexadecimal string, for saves.</summary>
        public string IncrementHex => _increment.ToString("x16", CultureInfo.InvariantCulture);

        /// <summary>Restores a generator from <see cref="StateHex"/> and <see cref="IncrementHex"/>.</summary>
        public static GameRandom FromState(string stateHex, string incrementHex)
        {
            var r = new GameRandom
            {
                _state = ulong.Parse(stateHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                _increment = ulong.Parse(incrementHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            };
            if ((r._increment & 1UL) == 0UL)
            {
                throw new FormatException("Random stream increment must be odd.");
            }
            return r;
        }

        /// <summary>An independent copy that continues the same sequence.</summary>
        public GameRandom Clone()
        {
            return new GameRandom { _state = _state, _increment = _increment };
        }

        /// <summary>Next 32-bit value.</summary>
        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>), without modulo bias.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");
            }
            uint bound = (uint)maxExclusive;
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                {
                    return (int)(r % bound);
                }
            }
        }

        /// <summary>Fisher-Yates shuffle in place.</summary>
        public void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
