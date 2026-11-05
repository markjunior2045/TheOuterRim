using System;
using System.IO;
using System.Runtime.CompilerServices;
using OuterRim.Core.Dice;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Persistence;
using Xunit;

namespace OuterRim.Core.Tests
{
    public class SaveSerializerTests
    {
        private const string GoldenName = "save-0.8.json";

        private static GameState GoldenGame()
        {
            var state = GameEngine.NewGame(TestContent.Base, GameEngineTests.Setup(12345, 3), out _);
            GameEngine.Execute(TestContent.Base, state, new GainCreditsCommand(state.CurrentPlayerId));
            var someone = state.Players[1];
            someone.Fame = 3;
            someone.CharacterDamage = 2;
            someone.Reputation.Set(Faction.Hutt, Reputation.Negative);
            someone.Reputation.Set(Faction.Rebel, Reputation.Positive);

            // Paused mid-effect (T1.2): two frames on the stack and a tie-break waiting for the current player.
            TestHandlers.Register();
            var frame = new Frame("test.tie_parent");
            frame.Set("candidates", "p1,p2,p3");
            GameEngine.StartResolution(TestContent.Base, state, frame);
            return state;
        }

        [Fact]
        public void State_RoundTripsThroughSaveAndLoad_Unchanged()
        {
            var state = GoldenGame();
            var text = SaveSerializer.Save(state);
            var loaded = SaveSerializer.Load(text);
            Assert.Equal(text, SaveSerializer.Save(loaded));
        }

        [Fact]
        public void LoadedGame_ContinuesWithTheSameRandomResults()
        {
            var state = GoldenGame();
            var loaded = SaveSerializer.Load(SaveSerializer.Save(state));
            Assert.Equal(DiceRoller.Roll(state.Random, 50), DiceRoller.Roll(loaded.Random, 50));
        }

        [Fact]
        public void Save_CarriesTheFormatVersionAndPacks()
        {
            var text = SaveSerializer.Save(GoldenGame());
            Assert.Contains("\"format\": \"" + SaveSerializer.Format + "\"", text);
            var packs = SaveSerializer.ReadPacks(text);
            Assert.Single(packs);
            Assert.Equal("base", packs[0].Id);
            Assert.Equal("0.1.0", packs[0].Version);
        }

        [Fact]
        public void LargeSeed_SurvivesTheRoundTrip()
        {
            var state = GameEngine.NewGame(TestContent.Base, GameEngineTests.Setup(ulong.MaxValue - 3, 2), out _);
            var loaded = SaveSerializer.Load(SaveSerializer.Save(state));
            Assert.Equal(ulong.MaxValue - 3, loaded.Seed);
        }

        [Fact]
        public void Load_RejectsAnUnknownFormat()
        {
            var text = SaveSerializer.Save(GoldenGame()).Replace(SaveSerializer.Format, "outer-rim-save/9.9");
            var e = Assert.Throws<SaveFormatException>(() => SaveSerializer.Load(text));
            Assert.Contains("9.9", e.Message);
        }

        [Fact]
        public void Load_RejectsUnknownFields()
        {
            var text = SaveSerializer.Save(GoldenGame()).Replace("\"fame\": 3", "\"fame\": 3, \"midichlorians\": 9000");
            Assert.Throws<SaveFormatException>(() => SaveSerializer.Load(text));
        }

        [Fact]
        public void Load_RejectsGarbage()
        {
            Assert.Throws<SaveFormatException>(() => SaveSerializer.Load("not json"));
        }

        /// <summary>
        /// The golden file pins the save format: loading it and saving again must give the same text.
        /// If the format changes on purpose, bump SaveSerializer.Format and regenerate with
        /// OUTERRIM_UPDATE_GOLDEN=1 dotnet test.
        /// </summary>
        [Fact]
        public void GoldenSave_LoadsAndResavesIdentically()
        {
            var sourceFile = Path.Combine(SourceDir(), "Golden", GoldenName);
            if (Environment.GetEnvironmentVariable("OUTERRIM_UPDATE_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
                File.WriteAllText(sourceFile, SaveSerializer.Save(GoldenGame()));
            }
            var golden = File.ReadAllText(sourceFile).Replace("\r\n", "\n");
            Assert.Equal(golden, SaveSerializer.Save(SaveSerializer.Load(golden)));
            Assert.Equal(golden, SaveSerializer.Save(GoldenGame()));
        }

        private static string SourceDir([CallerFilePath] string path = "")
        {
            return Path.GetDirectoryName(path)!;
        }
    }
}
