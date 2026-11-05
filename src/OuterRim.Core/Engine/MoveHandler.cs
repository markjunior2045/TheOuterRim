#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Planning-step movement, one space at a time (R-5.17 to R-5.22), so abilities can act on entering a
    /// space: "when you move into a patrol's space" (Han Solo, Slave I: pass a test to keep going) and
    /// "when another player moves into your space" (Tractor Beam: force them to stop). A patrol that is
    /// not let through ends the movement there (R-5.20). Vars: "player", "path" (ids joined by ','), "from".
    /// </summary>
    internal sealed class MoveHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.move";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Get("player"))!;
            var path = frame.Get("path").Length == 0 ? new string[0] : frame.Get("path").Split(',');
            int i = frame.Vars.ContainsKey("i") ? frame.GetInt("i") : 0;

            // Results of abilities that just ran for the space entered.
            if (frame.Vars.ContainsKey("forced_stop"))
            {
                return Finish(ctx, frame, player, i);
            }
            if (frame.Vars.TryGetValue("phase", out var phase))
            {
                frame.Vars.Remove("phase");
                if (phase == "patrol" && !frame.Vars.ContainsKey("no_stop"))
                {
                    return Finish(ctx, frame, player, i); // R-5.20: movement ends in the patrol's space
                }
                frame.Vars.Remove("no_stop");
                if (phase == "patrol")
                {
                    var tractor = TractorOwner(ctx, player, path[i - 1]);
                    if (tractor != null && i < path.Length)
                    {
                        frame.Set("phase", "tractor");
                        return StepResult.Call(TriggersHandler.For("other_moves_into", tractor.Id));
                    }
                }
            }
            if (i >= path.Length)
            {
                return Finish(ctx, frame, player, i);
            }

            var next = path[i];
            player.SpaceId = next;
            i++;
            frame.Set("i", i);
            bool last = i == path.Length;
            if (last)
            {
                return Finish(ctx, frame, player, i);
            }
            if (GameEngine.StopsMovement(state, player, next))
            {
                frame.Set("phase", "patrol");
                return StepResult.Call(TriggersHandler.For("move_into", player.Id,
                    new Dictionary<string, string> { ["moving_into"] = "patrol" }));
            }
            var owner = TractorOwner(ctx, player, next);
            if (owner != null)
            {
                frame.Set("phase", "tractor");
                return StepResult.Call(TriggersHandler.For("other_moves_into", owner.Id));
            }
            return StepResult.Continue();
        }

        /// <summary>Another player in the space with an "other_moves_into" ability (Tractor Beam).</summary>
        private static PlayerState? TractorOwner(ResolutionContext ctx, PlayerState mover, string space)
        {
            foreach (var p in ctx.State.Players)
            {
                if (p.Id == mover.Id || p.SpaceId != space) continue;
                foreach (var a in AbilitySources.Of(ctx.Content, p))
                {
                    if (a.TimingType == "trigger" && (string?)a.Ability["timing"]!["event"] == "other_moves_into") return p;
                }
            }
            return null;
        }

        private static StepResult Finish(ResolutionContext ctx, Frame frame, PlayerState player, int entered)
        {
            var path = frame.Get("path").Length == 0 ? new string[0] : frame.Get("path").Split(',');
            var taken = new List<string>();
            for (int k = 0; k < entered && k < path.Length; k++) taken.Add(path[k]);
            ctx.Events.Add(new PlayerMovedEvent(player.Id, frame.Get("from"), taken, false));
            if (!frame.Vars.ContainsKey("effect"))
            {
                GameEngine.StartStep(ctx.State, TurnStep.Action, ctx.Events); // planning movement only
            }
            return StepResult.Done();
        }

        /// <summary>The nearest movement on the stack, for "you do not have to end your movement" and Tractor Beam.</summary>
        internal static Frame? Nearest(GameState state)
        {
            for (int k = state.Stack.Count - 1; k >= 0; k--)
            {
                if (state.Stack[k].Handler == HandlerId) return state.Stack[k];
            }
            return null;
        }

        /// <summary>Whether the player has an ability that may let them go on after entering a patrol's space.</summary>
        internal static bool MayPassPatrols(ResolutionContext? ctx, Content.GameContent content, PlayerState player)
        {
            foreach (var a in AbilitySources.Of(content, player))
            {
                var timing = a.Ability["timing"]!;
                if ((string?)timing["type"] == "trigger" && (string?)timing["event"] == "move_into"
                    && (string?)timing["filter"]?["what"] == "patrol")
                {
                    return true;
                }
            }
            return false;
        }
    }
}
