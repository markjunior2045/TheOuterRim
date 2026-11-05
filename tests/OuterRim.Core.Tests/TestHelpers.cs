using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;

namespace OuterRim.Core.Tests
{
    internal static class TestHelpers
    {
        /// <summary>
        /// Answers "pass" to every dice window the result left pending (players with optional dice abilities,
        /// such as Lando or Jyn, are asked after each roll) and returns all events together.
        /// </summary>
        public static CommandResult PassDiceWindows(GameContent content, GameState state, CommandResult result)
        {
            if (!result.Accepted)
            {
                return result;
            }
            var events = new List<GameEvent>(result.Events);
            while (state.Pending != null && state.Pending.Prompt == "rules.dice_window")
            {
                var r = GameEngine.Execute(content, state, new ChooseOptionCommand(state.Pending.PlayerId, state.Pending.Id, "pass"));
                events.AddRange(r.Events);
            }
            return CommandResult.Ok(events);
        }

        /// <summary>
        /// Answers every pending decision: "no", "pass" or "decline" when offered, otherwise the first option.
        /// For tests that only care about what comes after an encounter.
        /// </summary>
        public static CommandResult DeclineAll(GameContent content, GameState state, CommandResult result)
        {
            if (!result.Accepted)
            {
                return result;
            }
            var events = new List<GameEvent>(result.Events);
            while (state.Pending != null)
            {
                var pick = state.Pending.Options[0].Id;
                foreach (var o in state.Pending.Options)
                {
                    if (o.Id == "no" || o.Id == "pass" || o.Id == "decline" || o.Id == "penalty") pick = o.Id;
                }
                events.AddRange(GameEngine.Execute(content, state, new ChooseOptionCommand(state.Pending.PlayerId, state.Pending.Id, pick)).Events);
            }
            return CommandResult.Ok(events);
        }
    }
}
