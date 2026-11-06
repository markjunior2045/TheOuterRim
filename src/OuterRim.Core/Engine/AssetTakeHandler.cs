#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// "Take a droid crew from another player in your space" (IG-88): the owner picks the player (a
    /// defeated player cannot be chosen, R-8.14) and the asset; it then joins the owner's assets as a
    /// gained asset (slots and limits apply, R-14.4). Fails when nobody has a matching asset.
    /// </summary>
    internal sealed class AssetTakeHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.asset_take";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var filter = (JObject)node["filter"]!["asset"]!;
            switch (frame.Step)
            {
                case 0:
                {
                    if (node["from"]!.Type == JTokenType.String)
                    {
                        // "Take ... from your opponent" (Tatooine-Nal Hutta): a player named by the context.
                        var named = Targeting.Player(ctx.State, owner, node["from"], FrameContext.Of(frame));
                        if (named == null || named.Id == owner.Id || Matching(ctx, named, filter).Count == 0)
                        {
                            ctx.Return("failed", "true");
                            return StepResult.Done();
                        }
                        frame.Set("victim", named.Id);
                        frame.Step = 1;
                        return StepResult.Continue();
                    }
                    var choose = (JObject)node["from"]!["choose"]!;
                    var options = new List<DecisionOption>();
                    foreach (var p in ctx.State.Players)
                    {
                        if (p.Id == owner.Id || p.Defeated) continue;
                        if (!Conditions.Eval(ctx.Content, ctx.State, owner, choose["where"], p)) continue;
                        if (Matching(ctx, p, filter).Count == 0) continue;
                        options.Add(new DecisionOption(p.Id, p.Id));
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        return StepResult.Done();
                    }
                    frame.Step = 1;
                    if (options.Count == 1)
                    {
                        frame.Set("victim", options[0].Id);
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_player", options, "victim"));
                }
                case 1:
                {
                    var victim = ctx.State.FindPlayer(frame.Get("victim"))!;
                    var matching = Matching(ctx, victim, filter);
                    frame.Step = 2;
                    if (matching.Count == 1)
                    {
                        frame.Set("asset", matching[0].CardId);
                        return StepResult.Continue();
                    }
                    var options = new List<DecisionOption>();
                    foreach (var a in matching) options.Add(new DecisionOption(a.CardId, a.CardId));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_asset", options, "asset"));
                }
                case 2:
                {
                    var victim = ctx.State.FindPlayer(frame.Get("victim"))!;
                    var asset = Assets.Find(victim, frame.Get("asset"))!;
                    victim.Assets.Remove(asset);
                    ctx.Events.Add(new AssetDiscardedEvent(victim.Id, asset.CardId, asset.Kind, "taken"));
                    var gain = new Frame(Assets.GainHandler);
                    gain.Set("player", owner.Id);
                    gain.Set("card", asset.CardId);
                    gain.Set("kind", asset.Kind);
                    gain.Set("cost", asset.Cost.ToString(CultureInfo.InvariantCulture));
                    gain.Set("traits", string.Join(",", asset.Traits));
                    frame.Step = 3;
                    return StepResult.Call(gain);
                }
                default:
                    return StepResult.Done();
            }
        }

        private static List<AssetState> Matching(ResolutionContext ctx, PlayerState p, JObject filter)
        {
            var result = new List<AssetState>();
            foreach (var a in p.Assets)
            {
                if (Selectors.AssetMatches(ctx.Content, a, filter)) result.Add(a);
            }
            return result;
        }
    }
}
