using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Simulation;

namespace OuterRim.Terminal
{
    /// <summary>
    /// V0 (PLAN T3.1): plays seeded games between random bots and checks the rule invariants after every
    /// command (RULES §26.3). One game prints its full log; many games print a summary and every failing seed.
    /// Exit code 0 when every game was sound, 1 otherwise, 2 for bad arguments.
    /// </summary>
    internal static class Program
    {
        private const string Usage =
            "Usage: outer-rim-v0 [--games N] [--seed S] [--players 1-4] [--rounds R] [--fame F]\n" +
            "                    [--jobs J] [--log] [--check-rejections] [--data <repository root>]\n" +
            "  --games N            games to play, seeds S to S+N-1 (default 1)\n" +
            "  --seed S             first seed (default 1)\n" +
            "  --players K          players per game; 0 or omitted: from the seed\n" +
            "  --rounds R           round limit per game (default 1000)\n" +
            "  --fame F             fame requirement (default 10)\n" +
            "  --jobs J             games played at once (default: processor count; 1 with --log)\n" +
            "  --log                print every command and event (default for a single game)\n" +
            "  --check-rejections   verify that rejected commands change nothing (slow)\n" +
            "  --data DIR           repository root (default: found above the current folder)";

        private static int Main(string[] args)
        {
            int games = 1, players = 0, rounds = 1000, fame = 10, jobs = Environment.ProcessorCount;
            ulong seed = 1;
            bool? log = null;
            bool checkRejections = false;
            string? root = null;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--games": games = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--seed": seed = ulong.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--players": players = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--rounds": rounds = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--fame": fame = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--jobs": jobs = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                        case "--log": log = true; break;
                        case "--check-rejections": checkRejections = true; break;
                        case "--data": root = args[++i]; break;
                        case "--help": case "-h": Console.WriteLine(Usage); return 0;
                        default: throw new ArgumentException("Unknown argument " + args[i]);
                    }
                }
                if (games < 1 || players < 0 || players > GameEngine.MaxPlayers || rounds < 1 || jobs < 1) throw new ArgumentException("Out of range.");
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is IndexOutOfRangeException || e is OverflowException)
            {
                Console.Error.WriteLine(e.Message);
                Console.Error.WriteLine(Usage);
                return 2;
            }

            root ??= FindRoot();
            if (root == null)
            {
                Console.Error.WriteLine("Repository root not found (no data/packs/base/pack.json above this folder); use --data.");
                return 2;
            }
            var content = PackLoader.Load(p => File.ReadAllText(Path.Combine(root, p)), new[] { "data/packs/base/pack.json" });
            bool printLog = log ?? games == 1;

            if (printLog) jobs = 1;
            var results = new SimulationResult[games];
            int done = 0;
            var clock = Stopwatch.StartNew();
            // Each game owns its state and generators; the content is shared and only read.
            Parallel.For(0, games, new ParallelOptions { MaxDegreeOfParallelism = jobs }, g =>
            {
                var options = new SimulationOptions
                {
                    Seed = seed + (ulong)g,
                    Players = players,
                    MaxRounds = rounds,
                    FameRequirement = fame,
                    CheckRejections = checkRejections,
                };
                if (printLog)
                {
                    options.OnCommand = (s, c) => Console.WriteLine(Prefix(s) + "> " + Describe(c));
                    options.OnEvent = (s, e) => Console.WriteLine(Prefix(s) + "  " + Describe(e));
                }
                results[g] = GameRunner.Run(content, options);
                int n = Interlocked.Increment(ref done);
                if (!printLog && games >= 20 && n % (games / 10) == 0)
                {
                    Console.Error.WriteLine("  " + n + "/" + games + " games");
                }
            });
            clock.Stop();
            foreach (var r in results)
            {
                if (printLog || !r.Sound) PrintGame(content, r);
            }
            if (games > 1) PrintSummary(content, results.ToList(), clock.Elapsed);
            return results.All(r => r.Sound) ? 0 : 1;
        }

        private static string? FindRoot()
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "packs", "base", "pack.json"))) return dir.FullName;
            }
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "packs", "base", "pack.json"))) return dir.FullName;
            }
            return null;
        }

        private static void PrintGame(GameContent content, SimulationResult r)
        {
            Console.WriteLine();
            Console.WriteLine("Seed " + r.Seed + ": " + r.Outcome + (r.WinnerId.Length > 0 ? ", winner " + r.WinnerId : "")
                + ", " + r.Rounds + " rounds, " + r.Commands + " commands, " + r.Rejections + " rejected candidates");
            Console.WriteLine("  Players: " + string.Join(", ", r.Characters.Select((c, i) => "p" + (i + 1) + " " + content.Name(c))));
            if (r.FinalState != null)
            {
                foreach (var p in r.FinalState.Players)
                {
                    Console.WriteLine("  " + p.Id + ": fame " + OuterRim.Core.Rules.PlayerRules.Fame(content, r.FinalState, p) + ", credits " + p.Credits
                        + ", assets " + p.Assets.Count + ", on " + p.SpaceId + (p.Defeated ? ", defeated" : ""));
                }
            }
            foreach (var problem in r.Problems) Console.WriteLine("  PROBLEM " + problem);
        }

        private static void PrintSummary(GameContent content, List<SimulationResult> results, TimeSpan elapsed)
        {
            Console.WriteLine();
            Console.WriteLine(results.Count + " games, seeds " + results[0].Seed + " to " + results[results.Count - 1].Seed
                + ", in " + elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
            foreach (var group in results.GroupBy(r => r.Outcome).OrderBy(g => g.Key))
            {
                Console.WriteLine("  " + group.Key + ": " + group.Count());
            }
            var won = results.Where(r => r.Outcome == SimulationOutcome.Won).ToList();
            if (won.Count > 0)
            {
                Console.WriteLine("  Rounds to win: average " + won.Average(r => r.Rounds).ToString("0.0", CultureInfo.InvariantCulture)
                    + ", min " + won.Min(r => r.Rounds) + ", max " + won.Max(r => r.Rounds));
                Console.WriteLine("  Wins by character:");
                foreach (var g in won.GroupBy(r => r.Characters[int.Parse(r.WinnerId.Substring(1), CultureInfo.InvariantCulture) - 1])
                    .OrderByDescending(g => g.Count()))
                {
                    Console.WriteLine("    " + content.Name(g.Key) + ": " + g.Count());
                }
            }
            Console.WriteLine("  Commands: " + results.Sum(r => (long)r.Commands) + ", rejected candidates: " + results.Sum(r => (long)r.Rejections));
            var bad = results.Where(r => !r.Sound).ToList();
            Console.WriteLine(bad.Count == 0 ? "  Every game was sound."
                : "  Failing seeds (" + bad.Count + "): " + string.Join(", ", bad.Take(40).Select(r => r.Seed)) + (bad.Count > 40 ? ", ..." : ""));
        }

        private static string Prefix(GameState s)
        {
            return "[R" + s.Round + " " + s.CurrentPlayerId + " " + s.Step + "] ";
        }

        /// <summary>A command or event as its type name and public properties, for the log.</summary>
        private static string Describe(object o)
        {
            var name = o.GetType().Name;
            foreach (var suffix in new[] { "Event", "Command" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);
            }
            var sb = new StringBuilder(name);
            foreach (var prop in o.GetType().GetProperties())
            {
                var value = prop.GetValue(o);
                if (value == null) continue;
                string text = value is string s ? s
                    : value is IEnumerable list ? "[" + string.Join(", ", list.Cast<object>().Select(Format)) + "]"
                    : Format(value);
                if (text.Length == 0) continue;
                sb.Append(' ').Append(char.ToLowerInvariant(prop.Name[0])).Append(prop.Name.Substring(1)).Append('=').Append(text);
            }
            return sb.ToString();
        }

        private static string Format(object value)
        {
            return value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? "";
        }
    }
}
