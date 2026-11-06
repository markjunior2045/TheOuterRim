using System.Collections.Generic;
using NUnit.Framework;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Simulation;

namespace OuterRim.Client.Tests
{
    /// <summary>The session shell passes commands to the core and hands accepted events to the views (DECISIONS §7).</summary>
    public class GameSessionTests
    {
        private static GameSession NewSession()
        {
            var content = ContentLocator.Base;
            var options = new SimulationOptions { Seed = 7, Players = 2 };
            var session = GameSession.NewGame(content, GameRunner.Setup(content, options, new RandomBot(7).Random), out _);
            var bot = new RandomBot(7);
            while (session.State.Pending != null)
            {
                foreach (var c in bot.Candidates(content, session.State))
                {
                    if (session.Send(c).Accepted) break;
                }
            }
            return session;
        }

        [Test]
        public void R4_6_AnAcceptedCommand_RaisesItsEvents()
        {
            var session = NewSession();
            var received = new List<GameEvent>();
            session.EventsReceived += events => received.AddRange(events);
            var result = session.Send(new GainCreditsCommand(session.State.CurrentPlayerId));
            Assert.IsTrue(result.Accepted, result.Reason);
            Assert.AreEqual(TurnStep.Action, session.State.Step);
            Assert.IsTrue(received.Exists(e => e is CreditsGainedEvent));
        }

        [Test]
        public void R4_5_ARejectedCommand_RaisesNothing()
        {
            var session = NewSession();
            string other = session.State.Players.Find(p => p.Id != session.State.CurrentPlayerId).Id;
            int raised = 0;
            session.EventsReceived += _ => raised++;
            Assert.IsFalse(session.Send(new GainCreditsCommand(other)).Accepted);
            Assert.AreEqual(0, raised);
        }
    }
}
