#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>
    /// Personal and ship goals (R-18.1 to R-18.7, EFFECTS.md §3.5). A goal is an ability with a "goal"
    /// timing; its "progress" says how it is met: a count of events (goal tokens, R-18.5), an event with a
    /// condition, a state, or a step ability. Completing it runs its effects: fame and flipping the card.
    /// </summary>
    public static class Goals
    {
        public static JObject? Progress(AbilityRef ability)
        {
            var timing = ability.Ability["timing"]!;
            return (string?)timing["type"] == "goal" ? (JObject?)timing["progress"] : null;
        }

        /// <summary>The step of a goal used like a step ability ("Ship Goal – Action:"), or null.</summary>
        public static string? StepOf(AbilityRef ability)
        {
            return (string?)Progress(ability)?["step"]?["step"];
        }

        public static int Tokens(PlayerState player, string key)
        {
            foreach (var t in player.GoalTokens)
            {
                if (t.Key == key) return t.Count;
            }
            return 0;
        }

        /// <summary>R-18.5: a goal token for one more step toward the goal. Returns the new count.</summary>
        public static int AddToken(PlayerState player, string key)
        {
            foreach (var t in player.GoalTokens)
            {
                if (t.Key == key)
                {
                    t.Count++;
                    return t.Count;
                }
            }
            player.GoalTokens.Add(new GoalToken { Key = key, Count = 1 });
            return 1;
        }

        /// <summary>R-18.5: flipping the card discards its goal tokens; R-18.2: a replaced ship's progress is lost.</summary>
        public static void ClearTokens(PlayerState player, string cardId)
        {
            player.GoalTokens.RemoveAll(t => t.Key.StartsWith(cardId + "#", System.StringComparison.Ordinal));
        }

        /// <summary>The first "have X" goal whose state holds, for any player (seat order), with its owner.</summary>
        public static bool FindStateGoal(GameContent content, GameState state, out PlayerState? owner, out AbilityRef? goal)
        {
            foreach (var p in state.Players)
            {
                foreach (var a in AbilitySources.Of(content, p))
                {
                    var progress = Progress(a);
                    if (progress?["state"] is JObject s && Conditions.Eval(content, state, p, s["cond"]))
                    {
                        owner = p;
                        goal = a;
                        return true;
                    }
                }
            }
            owner = null;
            goal = null;
            return false;
        }
    }
}
