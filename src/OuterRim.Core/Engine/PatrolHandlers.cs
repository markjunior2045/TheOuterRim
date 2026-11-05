#nullable enable
using System.Collections.Generic;
using System.Globalization;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Moving a patrol toward the current player (R-11.15 to R-11.20).</summary>
    internal sealed class PatrolMoveHandler : IResolutionHandler
    {
        public string Id => Patrols.MoveHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var faction = Patrols.Parse(frame.Get("faction"));
            var patrol = Patrols.Of(ctx.State, faction);
            switch (frame.Step)
            {
                case 0:
                {
                    var target = ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!;
                    var ends = Patrols.Destinations(ctx.Content.Map, patrol.SpaceId, target.SpaceId, frame.GetInt("distance"));
                    frame.Step = 1;
                    if (ends.Count == 1)
                    {
                        frame.Set("to", ends[0]);
                        return StepResult.Continue();
                    }
                    var options = new List<DecisionOption>();
                    foreach (var e in ends)
                    {
                        options.Add(new DecisionOption(e, e));
                    }
                    // R-11.16: the current player chooses the patrol's path.
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, ctx.State.CurrentPlayerId,
                        "rules.patrol_destination", options, "to"));
                }
                default:
                {
                    var from = patrol.SpaceId;
                    patrol.SpaceId = frame.Get("to");
                    ctx.Events.Add(new PatrolMovedEvent(faction, from, patrol.SpaceId, false));
                    return StepResult.Done();
                }
            }
        }
    }

    /// <summary>
    /// Encountering a patrol: a ship combat against it (R-11.25), with its level's value; a level-4
    /// patrol has an infinite value (R-11.6). The outcome resolves before damage (R-11.29).
    /// </summary>
    internal sealed class PatrolEncounterHandler : IResolutionHandler
    {
        public string Id => Patrols.EncounterHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            if (frame.Step == 0)
            {
                var faction = Patrols.Parse(frame.Get("target"));
                var patrol = Patrols.Of(ctx.State, faction);
                var token = ctx.Content.PatrolToken(faction, patrol.Level);
                var combat = new Frame(Combat.Handler);
                combat.Set("type", "ship");
                combat.Set("attacker", ctx.State.CurrentPlayerId);
                combat.Set("defender_kind", "patrol");
                combat.Set("defender", Patrols.Id(faction));
                combat.Set("defender_value", token.Combat.HasValue
                    ? token.Combat.Value.ToString(CultureInfo.InvariantCulture)
                    : Combat.Infinite);
                combat.Set("outcome", PatrolOutcomeHandler.HandlerId);
                frame.Step = 1;
                return StepResult.Call(combat);
            }
            ctx.Events.Add(new EncounterResolvedEvent(ctx.State.CurrentPlayerId, "patrol", frame.Get("target")));
            return StepResult.Done();
        }
    }

    /// <summary>
    /// R-11.7: losing, the player moves the patrol to an adjacent space of their choice. R-11.8: winning,
    /// the player gains the token's reward, loses 1 reputation with the faction, and the patrol is
    /// eliminated and replaced from the stack.
    /// </summary>
    internal sealed class PatrolOutcomeHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.patrol_outcome";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var faction = Patrols.Parse(frame.Get("defender"));
            var patrol = Patrols.Of(ctx.State, faction);
            var player = ctx.State.FindPlayer(frame.Get("attacker"))!;
            if (frame.Get("winner") == "attacker")
            {
                var token = ctx.Content.PatrolToken(faction, patrol.Level);
                PlayerRules.GainCredits(player, token.RewardCredits, ctx.Events);
                PlayerRules.GainFame(player, token.RewardFame, ctx.Events);
                PlayerRules.LoseReputation(player, faction, 1, ctx.Events);
                Patrols.EliminateAndSpawn(ctx.Content, ctx.State, faction, ctx.Events);
                return StepResult.Done();
            }
            switch (frame.Step)
            {
                case 0:
                {
                    var options = new List<DecisionOption>();
                    foreach (var n in ctx.Content.Map.Neighbours(patrol.SpaceId))
                    {
                        options.Add(new DecisionOption(n, n));
                    }
                    frame.Step = 1;
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.patrol_push", options, "to"));
                }
                default:
                {
                    var from = patrol.SpaceId;
                    patrol.SpaceId = frame.Get("to");
                    ctx.Events.Add(new PatrolMovedEvent(faction, from, patrol.SpaceId, false));
                    return StepResult.Done();
                }
            }
        }
    }
}
