using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;
using Xunit;

namespace OuterRim.Core.Tests
{
    /// <summary>Every encounter section resolves to the end, for several reputations and both ways of answering.</summary>
    public class EncounterSweepTests
    {
        private static readonly Content.GameContent C = TestContent.Base;

        /// <summary>Effects still planned (PLAN T2.6 parts 3 and 4): a section that reaches one is skipped, not failed.</summary>
        internal static readonly string[] Planned = { "databank", "sabacc", "test.pass" };

        internal static readonly HashSet<string> Skipped = new HashSet<string>();

        public static List<string> Run(bool accept)
        {
            var problems = new List<string>();
            foreach (var deck in C.EncounterDeckIds())
            {
                foreach (var card in C.EncounterCards(deck))
                {
                    foreach (JObject section in (JArray)C.Card(card)["sections"]!)
                    {
                        var space = (string)section["space"]! == "navpoint" ? "nav-kessel-3" : (string)section["space"]!;
                        foreach (var rep in new[] { Reputation.Positive, Reputation.Neutral, Reputation.Negative })
                        {
                            for (ulong seed = 0; seed < 3; seed++)
                            {
                                try
                                {
                                    Once(card, space, (bool?)section["patrol"] == true, rep, accept, seed);
                                }
                                catch (NotSupportedException e) when (Planned.Any(p => e.Message.Contains(p)))
                                {
                                    Skipped.Add(card + ": " + e.Message);
                                }
                                catch (Exception e)
                                {
                                    problems.Add(card + " @" + space + " " + rep + " " + (accept ? "accept" : "decline") + ": " + e.GetType().Name + ": " + e.Message);
                                }
                            }
                        }
                    }
                }
            }
            return problems.Distinct().ToList();
        }

        private static void Once(string card, string space, bool patrol, Reputation rep, bool accept, ulong seed)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            var p1 = s.FindPlayer("p1")!;
            p1.SpaceId = space;
            p1.Credits = 20000;
            s.FindPlayer("p2")!.SpaceId = space;
            foreach (Faction f in new[] { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate }) p1.Reputation.Set(f, rep);
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            if (patrol) Patrols.Of(s, Faction.Rebel).SpaceId = space;
            p1.Reputation.Rebel = patrol && rep == Reputation.Negative ? Reputation.Neutral : p1.Reputation.Rebel; // not forced to fight it
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            var deck = Encounters.Deck(s, (string)C.Card(card)["deck"]!);
            deck.Cards.Remove(card);
            deck.Cards.Insert(0, card);
            var r = GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Space));
            Assert.True(r.Accepted, r.Reason);
            for (int guard = 0; s.Pending != null && guard < 60; guard++)
            {
                var options = s.Pending.Options;
                var pick = options[0].Id;
                foreach (var o in options)
                {
                    if (accept ? o.Id == "yes" || o.Id == "avoid" : o.Id == "no" || o.Id == "pass" || o.Id == "none" || o.Id == "decline" || o.Id == "penalty") pick = o.Id;
                }
                var a = GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, pick));
                Assert.True(a.Accepted, a.Reason);
            }
            Assert.True(s.Pending == null || s.Step == TurnStep.Planning, "still waiting: " + s.Pending?.Prompt);
        }

        [Fact]
        public void EveryEncounterSection_Resolves_WhenAccepting()
        {
            var problems = Run(true);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }

        [Fact]
        public void EveryEncounterSection_Resolves_WhenDeclining()
        {
            var problems = Run(false);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
            var report = Environment.GetEnvironmentVariable("OUTERRIM_SWEEP_REPORT");
            if (!string.IsNullOrEmpty(report))
            {
                System.IO.File.WriteAllLines(report, Skipped.OrderBy(x => x)); // what is still planned, for review
            }
        }

        /// <summary>Every contact card resolves when its token is encountered, for both ways of answering.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EveryContact_Resolves(bool accept)
        {
            var problems = new List<string>();
            foreach (var token in C.ContactTokenIds())
            {
                foreach (var rep in new[] { Reputation.Positive, Reputation.Neutral, Reputation.Negative })
                {
                    foreach (var character in new[] { "base-character-00", "base-character-01" })
                    {
                        for (ulong seed = 0; seed < 3; seed++)
                        {
                            try
                            {
                                Contact(token, rep, accept, seed, character);
                            }
                            catch (NotSupportedException e) when (Planned.Any(p => e.Message.Contains(p)))
                            {
                                Skipped.Add(token + ": " + e.Message);
                            }
                            catch (Exception e)
                            {
                                problems.Add(token + " " + rep + " " + character + " " + (accept ? "accept" : "decline") + ": " + e.GetType().Name + ": " + e.Message);
                            }
                        }
                    }
                }
            }
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
        }

        private static void Contact(string token, Reputation rep, bool accept, ulong seed, string character)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, character),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            var p1 = s.FindPlayer("p1")!;
            p1.Credits = 20000;
            foreach (Faction f in new[] { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate }) p1.Reputation.Set(f, rep);
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            var t = Contacts.Token(s, token);
            p1.SpaceId = t.SpaceId;
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            var r = GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Contact, token));
            Assert.True(r.Accepted, r.Reason);
            for (int guard = 0; s.Pending != null && guard < 60; guard++)
            {
                var pick = s.Pending.Options[0].Id;
                foreach (var o in s.Pending.Options)
                {
                    if (accept ? o.Id == "yes" || o.Id == "avoid" : o.Id == "no" || o.Id == "pass" || o.Id == "none" || o.Id == "decline" || o.Id == "penalty") pick = o.Id;
                }
                var a = GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, pick));
                Assert.True(a.Accepted, a.Reason);
            }
            Assert.True(s.Pending == null || s.Step == TurnStep.Planning, "still waiting: " + s.Pending?.Prompt);
        }

        /// <summary>Every job resolves when attempted at its destination, for both ways of answering.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EveryJob_Resolves(bool accept)
        {
            var problems = new List<string>();
            var jobs = C.CardIds().Where(id => (string?)C.Card(id)["kind"] == "job").ToList();
            Assert.True(jobs.Count >= 20, "jobs: " + jobs.Count);
            foreach (var job in jobs)
            {
                for (ulong seed = 0; seed < 6; seed++)
                {
                    try
                    {
                        Job(job, accept, seed);
                    }
                    catch (NotSupportedException e) when (Planned.Any(p => e.Message.Contains(p)))
                    {
                        Skipped.Add(job + ": " + e.Message);
                    }
                    catch (Exception e)
                    {
                        problems.Add(job + " seed " + seed + " " + (accept ? "accept" : "decline") + ": " + e.GetType().Name + ": " + e.Message);
                    }
                }
            }
            Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
        }

        private static void Job(string job, bool accept, ulong seed)
        {
            var s = GameEngine.NewGame(C, new GameSetup(seed, new[]
            {
                new PlayerSetup("p1", "One", "green", "kessel", TestContent.G9Rigger, "base-character-00"),
                new PlayerSetup("p2", "Two", "red", "tatooine", TestContent.G9Rigger, "base-character-03"),
            }, new[] { new PackRef("base", "0.1.0") }), out _);
            s.CurrentPlayerId = "p1";
            var p1 = s.FindPlayer("p1")!;
            var p2 = s.FindPlayer("p2")!;
            p1.Credits = 20000;
            p2.Credits = 20000;
            p2.Reputation.Hutt = Reputation.Negative; // a target for Wanted
            foreach (var p in s.Patrols) p.SpaceId = "nav-tatooine-1";
            var card = C.Card(job);
            var dest = card["destination"];
            string space = "kessel";
            if (dest is JObject o && o["space"] is JObject sp) space = (string)sp["id"]!;
            else if (dest is JObject o2 && o2["patrol_space"] is JObject ps) space = Patrols.Of(s, Patrols.Parse((string)ps["faction"]!)).SpaceId;
            else if (dest is JObject o3 && o3["planet_where"] != null)
            {
                foreach (Faction f in new[] { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate }) p1.Reputation.Set(f, Reputation.Positive);
                space = SpaceSelect.Options(C, s, p1, dest).First();
            }
            p1.SpaceId = space;
            p2.SpaceId = space;
            p1.Assets.Add(new AssetState { CardId = job, Kind = "job" });
            GameEngine.Execute(C, s, new MoveCommand("p1", new string[0]));
            GameEngine.Execute(C, s, new EndActionStepCommand("p1"));
            var r = GameEngine.Execute(C, s, new EncounterCommand("p1", EncounterKind.Job, job));
            Assert.True(r.Accepted, job + ": " + r.Reason);
            for (int guard = 0; s.Pending != null && guard < 200; guard++)
            {
                var pick = s.Pending.Options[0].Id;
                foreach (var opt in s.Pending.Options)
                {
                    if (accept ? opt.Id == "yes" || opt.Id == "avoid" : opt.Id == "no" || opt.Id == "pass" || opt.Id == "none" || opt.Id == "decline" || opt.Id == "penalty") pick = opt.Id;
                }
                var a = GameEngine.Execute(C, s, new ChooseOptionCommand(s.Pending.PlayerId, s.Pending.Id, pick));
                Assert.True(a.Accepted, a.Reason);
            }
            Assert.True(s.Pending == null || s.Step == TurnStep.Planning, "still waiting: " + s.Pending?.Prompt);
        }
    }
}
