using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using OuterRim.Core.Content;
using OuterRim.Core.Persistence;
using OuterRim.Core.Simulation;

namespace OuterRim.Core.SharedTests
{
    /// <summary>
    /// Tests that run unchanged under dotnet test (tests/OuterRim.Core.NUnitTests) and under Unity's Test Runner
    /// (this folder is a local package). They prove that Unity runs the same core with the same results:
    /// a seed must replay the same game in .NET and in Unity (DECISIONS §7, random numbers).
    /// </summary>
    public class CrossRuntimeTests
    {
        /// <summary>
        /// SHA-256 of the save after <see cref="Rounds"/> rounds of the seeded V0 game below. Any change to the
        /// rules or the save format changes it on purpose: then update it from the dotnet run's failure message,
        /// and Unity must then agree.
        /// </summary>
        private const string ExpectedSaveHash = "ef5d7bccf509acc96807f367a2f9a9a12ce3c997def8911a7309a5f4e5b48925";

        private const ulong Seed = 2026;
        private const int Rounds = 30;

        private static GameContent _content;

        private static GameContent Content
        {
            get
            {
                if (_content == null)
                {
                    string root = RepositoryRoot();
                    _content = PackLoader.Load(p => File.ReadAllText(Path.Combine(root, p)), new[] { "data/packs/base/pack.json" });
                }
                return _content;
            }
        }

        /// <summary>The repository root, found above the working folder (the Unity project or the test binaries).</summary>
        private static string RepositoryRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "data", "packs", "base", "pack.json"))) return dir.FullName;
                }
            }
            throw new FileNotFoundException("data/packs/base/pack.json not found above " + Directory.GetCurrentDirectory());
        }

        [Test]
        public void TheBasePack_Loads()
        {
            Assert.IsTrue(Content.HasCharacter("base-character-00"));
            int planets = 0;
            foreach (var space in Content.Map.Spaces) if (space.IsPlanet) planets++;
            Assert.AreEqual(11, planets); // R-3.2 standard layout
        }

        [Test]
        public void ASeededGame_IsSound_AndEndsInTheSameState_OnEveryRuntime()
        {
            var result = GameRunner.Run(Content, new SimulationOptions { Seed = Seed, MaxRounds = Rounds });
            Assert.IsTrue(result.Sound, result.Outcome + ": " + string.Join(" | ", result.Problems));
            string hash = Sha256(SaveSerializer.Save(result.FinalState));
            Assert.AreEqual(ExpectedSaveHash, hash, "Save hash after " + result.Commands + " commands.");
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
