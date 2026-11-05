#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OuterRim.Core.Model;
using OuterRim.Core.Random;

namespace OuterRim.Core.Persistence
{
    /// <summary>
    /// Saves and loads a game as JSON (DECISIONS §6: save and resume midway; §7: JSON with a format
    /// version). A save records the content packs it used, so a lobby can check them before loading.
    /// </summary>
    public static class SaveSerializer
    {
        /// <summary>Save file format. Bump it when a saved field changes meaning or is removed.</summary>
        public const string Format = "outer-rim-save/0.13";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Property names in camelCase; dictionary keys (frame variables) are kept exactly as written.
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
            },
            // Collections created by constructors (ordinal-sorted frame variables) are filled, not replaced.
            ObjectCreationHandling = ObjectCreationHandling.Auto,
            Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()), new GameRandomConverter() },
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Include,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        /// <summary>Serializes the state. The same state always gives the same text.</summary>
        public static string Save(GameState state)
        {
            var file = new SaveFile { Format = Format, Packs = state.Packs, State = state };
            return JsonConvert.SerializeObject(file, Settings).Replace("\r\n", "\n");
        }

        /// <summary>Reads a save. Throws <see cref="SaveFormatException"/> for an unknown format or inconsistent file.</summary>
        public static GameState Load(string json)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new SaveFormatException("Not a valid save file: " + e.Message, e);
            }
            var format = (string?)root["format"];
            if (format != Format)
            {
                throw new SaveFormatException("Unsupported save format '" + format + "', expected '" + Format + "'.");
            }
            SaveFile? file;
            try
            {
                file = root.ToObject<SaveFile>(JsonSerializer.Create(Settings));
            }
            catch (JsonException e)
            {
                throw new SaveFormatException("Save file does not match the format: " + e.Message, e);
            }
            if (file?.State == null)
            {
                throw new SaveFormatException("Save file has no state.");
            }
            if (!SamePacks(file.Packs, file.State.Packs))
            {
                throw new SaveFormatException("Save file pack list does not match its state.");
            }
            return file.State;
        }

        /// <summary>Reads only the pack list of a save, without loading the game.</summary>
        public static List<PackRef> ReadPacks(string json)
        {
            var root = JObject.Parse(json);
            return root["packs"]?.ToObject<List<PackRef>>(JsonSerializer.Create(Settings)) ?? new List<PackRef>();
        }

        private static bool SamePacks(List<PackRef> a, List<PackRef> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id || a[i].Version != b[i].Version)
                {
                    return false;
                }
            }
            return true;
        }

        private sealed class SaveFile
        {
            public string Format { get; set; } = "";

            public List<PackRef> Packs { get; set; } = new List<PackRef>();

            public GameState? State { get; set; }
        }

        /// <summary>Writes the generator as its two hexadecimal state words.</summary>
        private sealed class GameRandomConverter : JsonConverter<GameRandom>
        {
            public override void WriteJson(JsonWriter writer, GameRandom? value, JsonSerializer serializer)
            {
                if (value == null)
                {
                    writer.WriteNull();
                    return;
                }
                writer.WriteStartObject();
                writer.WritePropertyName("state");
                writer.WriteValue(value.StateHex);
                writer.WritePropertyName("increment");
                writer.WriteValue(value.IncrementHex);
                writer.WriteEndObject();
            }

            public override GameRandom ReadJson(JsonReader reader, Type objectType, GameRandom? existingValue,
                bool hasExistingValue, JsonSerializer serializer)
            {
                var o = JObject.Load(reader);
                var stateHex = (string?)o["state"];
                var incrementHex = (string?)o["increment"];
                if (stateHex == null || incrementHex == null)
                {
                    throw new JsonSerializationException("Random state needs 'state' and 'increment'.");
                }
                try
                {
                    return GameRandom.FromState(stateHex, incrementHex);
                }
                catch (FormatException e)
                {
                    throw new JsonSerializationException("Invalid random state: " + e.Message, e);
                }
            }
        }
    }

    /// <summary>A save file that cannot be loaded.</summary>
    public sealed class SaveFormatException : Exception
    {
        public SaveFormatException(string message) : base(message)
        {
        }

        public SaveFormatException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
