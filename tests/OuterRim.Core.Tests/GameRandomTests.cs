using System.Collections.Generic;
using OuterRim.Core.Random;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class GameRandomTests
    {
        private static List<uint> Take(GameRandom r, int n)
        {
            var list = new List<uint>();
            for (int i = 0; i < n; i++)
            {
                list.Add(r.NextUInt());
            }
            return list;
        }

        [Fact]
        public void SameSeed_GivesSameSequence()
        {
            Assert.Equal(Take(new GameRandom(42), 1000), Take(new GameRandom(42), 1000));
        }

        [Fact]
        public void DifferentSeeds_GiveDifferentSequences()
        {
            Assert.NotEqual(Take(new GameRandom(42), 20), Take(new GameRandom(43), 20));
        }

        [Fact]
        public void KnownSeeds_GiveThePcg32ReferenceValues()
        {
            // Pins the algorithm. Seed 42, stream 54 is the PCG32 reference demo (0xa15c02b7, 0x7b47f409);
            // seed 12345 was computed independently. If these change, every save and golden test changes too.
            Assert.Equal(new uint[] { 0xa15c02b7u, 0x7b47f409u }, Take(new GameRandom(42), 2));
            Assert.Equal(new uint[] { 4127262169u, 3468701408u, 2272573525u, 4086873097u }, Take(new GameRandom(12345), 4));
        }

        [Fact]
        public void Clone_ContinuesIndependently()
        {
            var r = new GameRandom(3);
            var c = r.Clone();
            Assert.Equal(Take(r, 10), Take(c, 10));
        }

        [Fact]
        public void RestoredState_ContinuesTheSameSequence()
        {
            var r = new GameRandom(7);
            Take(r, 57);
            var restored = GameRandom.FromState(r.StateHex, r.IncrementHex);
            Assert.Equal(Take(r, 500), Take(restored, 500));
        }

        [Fact]
        public void NextInt_StaysInRange_AndHitsEveryValue()
        {
            var r = new GameRandom(99);
            var seen = new bool[8];
            for (int i = 0; i < 10000; i++)
            {
                int v = r.NextInt(8);
                Assert.InRange(v, 0, 7);
                seen[v] = true;
            }
            Assert.All(seen, Assert.True);
        }

        [Fact]
        public void Shuffle_IsAPermutation_AndDeterministic()
        {
            var a = new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var b = new List<int>(a);
            new GameRandom(5).Shuffle(a);
            new GameRandom(5).Shuffle(b);
            Assert.Equal(a, b);
            var sorted = new List<int>(a);
            sorted.Sort();
            Assert.Equal(new List<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, sorted);
        }
    }
}
