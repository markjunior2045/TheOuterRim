#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// The deliver action (R-16.1 to R-16.7): cargo is delivered one at a time in the player's order. An
    /// ILLEGAL cargo first rolls a die (with a dice window, scope delivery_roll): on {HIT} it is delivered,
    /// on anything else a random databank card #1 is resolved instead (R-16.3). If a delivery ends the turn,
    /// the rest cannot be made (R-16.6). Vars: "player", "queue" (cargo ids joined by '|').
    /// </summary>
    internal sealed class DeliverHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.deliver";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Get("player"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    // Next cargo, unless the turn or the step was ended by a previous delivery.
                    var queue = new List<string>(frame.Get("queue").Split('|'));
                    if (state.TurnEndsAfterStep || frame.Vars.ContainsKey("end_step") || queue.Count == 0 || queue[0].Length == 0)
                    {
                        frame.Step = 9;
                        return StepResult.Continue();
                    }
                    var cargo = queue[0];
                    queue.RemoveAt(0);
                    frame.Set("queue", string.Join("|", queue));
                    frame.Set("current", cargo);
                    var card = ctx.Content.Card(cargo);
                    if (card["delivery_check"] is JObject check)
                    {
                        // R-16.3: roll one die before delivering.
                        frame.Set("faces", DicePool.Encode(DiceRoller.Roll(state.Random, 1)));
                        frame.Set("success", string.Join(",", ((JArray)check["success"]!).ToObject<List<string>>()!));
                        frame.Set("databank", (int)check["else_databank"]!);
                        frame.Step = 1;
                        var window = DiceWindows.For(state, "faces", "delivery_roll", player.Id, new[] { player.Id });
                        window.Set("ctx.delivering_traits", MarketBuyHandler.Traits(card));
                        return StepResult.Call(window);
                    }
                    frame.Step = 0;
                    return StepResult.Call(DeliverCargoHandler.For(player.Id, cargo));
                }
                case 1:
                {
                    var die = DicePool.Effective(DicePool.Decode(frame.Get("faces")));
                    var face = die.Count > 0 ? die[0] : DieFace.Blank;
                    ctx.Events.Add(new DeliveryRolledEvent(player.Id, frame.Get("current"), face));
                    frame.Step = 0;
                    if (System.Array.IndexOf(frame.Get("success").Split(','), FaceId(face)) >= 0)
                    {
                        return StepResult.Call(DeliverCargoHandler.For(player.Id, frame.Get("current")));
                    }
                    // Otherwise a random version of databank card #1 (R-13.3), keyed by the die result.
                    var versions = ctx.Content.DatabankCards(frame.GetInt("databank"));
                    var check = new Frame(CargoCheckHandler.HandlerId);
                    check.Set("player", player.Id);
                    check.Set("card", versions[state.Random.NextInt(versions.Count)]);
                    check.Set("face", FaceId(face));
                    ctx.Events.Add(new DatabankResolvedEvent(player.Id, check.Get("card")));
                    return StepResult.Call(check);
                }
                default:
                    if (frame.Vars.ContainsKey("end_step") && !state.TurnEndsAfterStep)
                    {
                        GameEngine.StartStep(state, TurnStep.Encounter, ctx.Events); // "end your action step"
                    }
                    return StepResult.Done();
            }
        }

        internal static string FaceId(DieFace face)
        {
            return face == DieFace.Hit ? "hit" : face == DieFace.Crit ? "crit" : face == DieFace.Focus ? "focus" : "blank";
        }

        internal static Frame? Nearest(GameState state)
        {
            for (int i = state.Stack.Count - 1; i >= 0; i--)
            {
                if (state.Stack[i].Handler == HandlerId) return state.Stack[i];
            }
            return null;
        }
    }

    /// <summary>Delivers one cargo: its reward, then it is discarded (R-16.2), then "cargo delivered" fires.</summary>
    internal sealed class DeliverCargoHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.deliver_cargo";

        public string Id => HandlerId;

        public static Frame For(string player, string cargo)
        {
            var f = new Frame(HandlerId);
            f.Set("player", player);
            f.Set("cargo", cargo);
            return f;
        }

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var cargo = frame.Get("cargo");
            var asset = Assets.Find(player, cargo);
            switch (frame.Step)
            {
                case 0:
                    if (asset == null)
                    {
                        return StepResult.Done(); // already gone (discarded by the cargo check)
                    }
                    frame.Step = 1;
                    return StepResult.Call(EffectHandlers.ListFrame(player.Id, cargo, "reward"));
                case 1:
                {
                    frame.Step = 2;
                    var traits = MarketBuyHandler.Traits(ctx.Content.Card(cargo));
                    if (asset != null)
                    {
                        Assets.Discard(ctx.Content, ctx.State, player, asset, "delivered", ctx.Events);
                    }
                    ctx.Events.Add(new CargoDeliveredEvent(player.Id, cargo));
                    return StepResult.Call(TriggersHandler.For("cargo_delivered", player.Id,
                        new Dictionary<string, string> { ["delivering_traits"] = traits }));
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// Databank card #1, the cargo check: its intro first ("If you have {REP_NEG} reputation with a patrol in
    /// your space, end your action step"), then the section matching the die rolled for the cargo.
    /// Vars: "player", "card", "face".
    /// </summary>
    internal sealed class CargoCheckHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.cargo_check";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("player"), frame.Get("card"), "intro"));
                case 1:
                {
                    frame.Step = 2;
                    var deliver = DeliverHandler.Nearest(ctx.State);
                    if (deliver != null && deliver.Vars.ContainsKey("end_step"))
                    {
                        return StepResult.Done();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("player"), frame.Get("card"), frame.Get("face")));
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>"X unless you spend Y": the player chooses to pay Y (if they can) or suffer X.</summary>
    internal sealed class UnlessHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.unless";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    // R-9.8: paying needs the full amount; otherwise the penalty applies.
                    if (!Effects.Modifiers.CanPay(ctx.Content, owner, (JArray)node["avoid"]!))
                    {
                        frame.Set("answer", "penalty");
                        return StepResult.Continue();
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.unless",
                        new[] { new DecisionOption("avoid", "pay"), new DecisionOption("penalty", "penalty") }, "answer"));
                }
                case 1:
                    frame.Step = 2;
                    return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"),
                        frame.Get("path") + (frame.Get("answer") == "avoid" ? ".avoid" : ".penalty")));
                default:
                    return StepResult.Done();
            }
        }
    }
}
