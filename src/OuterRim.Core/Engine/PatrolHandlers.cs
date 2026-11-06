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
                    // Jyn Erso (back): "When a patrol within 3 spaces of you would move during your market
                    // action, you may move the patrol toward another player instead of toward you."
                    var current = ctx.State.FindPlayer(ctx.State.CurrentPlayerId)!;
                    frame.Step = 2;
                    if (frame.Vars.ContainsKey("toward_player"))
                    {
                        // A card effect: "toward you" is its owner, and Jyn's market-action redirect does not apply.
                        frame.Set("toward", frame.Get("toward_player"));
                        return StepResult.Continue();
                    }
                    if (ctx.State.Players.Count > 1 && CanRedirect(ctx, current, patrol))
                    {
                        var choices = new List<DecisionOption> { new DecisionOption(current.Id, "rules.toward_you") };
                        foreach (var p in ctx.State.Players)
                        {
                            if (p.Id != current.Id) choices.Add(new DecisionOption(p.Id, p.Id));
                        }
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, current.Id, "rules.patrol_redirect", choices, "toward"));
                    }
                    frame.Set("toward", current.Id);
                    return StepResult.Continue();
                }
                case 2:
                {
                    var target = ctx.State.FindPlayer(frame.Get("toward"))!;
                    int distance = frame.GetInt("distance");
                    List<string> ends;
                    if (frame.Vars.ContainsKey("any_direction"))
                    {
                        // "Up to N spaces in any direction" (Lothal-5).
                        ends = ctx.Content.Map.Within(patrol.SpaceId, distance, throughCoreWorlds: false);
                    }
                    else if (frame.Vars.ContainsKey("up_to"))
                    {
                        ends = new List<string>();
                        for (int k = 0; k <= distance; k++)
                        {
                            foreach (var e in Patrols.Destinations(ctx.Content.Map, patrol.SpaceId, target.SpaceId, k))
                            {
                                if (!ends.Contains(e)) ends.Add(e);
                            }
                        }
                    }
                    else
                    {
                        ends = Patrols.Destinations(ctx.Content.Map, patrol.SpaceId, target.SpaceId, distance);
                    }
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

        /// <summary>Whether the current player may send this patrol toward someone else (Jyn Erso back, within 3 spaces).</summary>
        private static bool CanRedirect(ResolutionContext ctx, PlayerState player, PatrolState patrol)
        {
            foreach (var a in Effects.AbilitySources.Of(ctx.Content, player))
            {
                var timing = a.Ability["timing"]!;
                if ((string?)timing["type"] != "replacement" || (string?)timing["event"] != "patrol_would_move") continue;
                foreach (Newtonsoft.Json.Linq.JObject e in (Newtonsoft.Json.Linq.JArray)a.Ability["effects"]!)
                {
                    if ((string?)e["handler"] != "patrol_redirect") continue;
                    int within = (int?)e["params"]?["within"] ?? 3;
                    int d = ctx.Content.Map.Distance(player.SpaceId, patrol.SpaceId);
                    if (d >= 0 && d <= within) return true;
                }
            }
            return false;
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
                combat.Set("attacker", frame.Vars.ContainsKey("player") ? frame.Get("player") : ctx.State.CurrentPlayerId);
                combat.Set("defender_kind", "patrol");
                combat.Set("defender", Patrols.Id(faction));
                combat.Set("defender_value", token.Combat.HasValue
                    ? token.Combat.Value.ToString(CultureInfo.InvariantCulture)
                    : Combat.Infinite);
                combat.Set("outcome", PatrolOutcomeHandler.HandlerId);
                if (frame.Vars.TryGetValue("attacker_add", out var add)) combat.Set("attacker_add", add);
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
                PlayerRules.GainFame(ctx.Content, ctx.State, player, token.RewardFame, ctx.Events);
                if (!Effects.Modifiers.HasRule(ctx.Content, ctx.State, player, "no_reputation_loss_on_patrol_win"))
                {
                    PlayerRules.LoseReputation(player, faction, 1, ctx.Events); // R-11.8, unless IG-2000
                }
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
