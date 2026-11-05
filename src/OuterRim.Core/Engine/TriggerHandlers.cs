#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Dice;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Context variables ("ctx.*") of a frame, for conditions about what is happening.</summary>
    internal static class FrameContext
    {
        public static Dictionary<string, string> Of(Frame frame)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in frame.Vars)
            {
                if (kv.Key.StartsWith("ctx.", StringComparison.Ordinal))
                {
                    d[kv.Key.Substring(4)] = kv.Value;
                }
            }
            return d;
        }

        public static void Copy(Frame from, Frame to)
        {
            foreach (var kv in from.Vars)
            {
                if (kv.Key.StartsWith("ctx.", StringComparison.Ordinal))
                {
                    to.Set(kv.Key, kv.Value);
                }
            }
        }

        /// <summary>Whether the cost of an ability can be paid now: credits (R-9.8) and assets to discard.</summary>
        public static bool CostPayable(GameContent content, PlayerState player, JObject ability)
        {
            if (!(ability["cost"] is JArray cost))
            {
                return true;
            }
            int credits = 0;
            foreach (JObject c in cost)
            {
                switch ((string?)c["op"])
                {
                    case "credits.spend":
                        credits += Selectors.Amount(content, player, c["amount"]!);
                        break;
                    case "asset.discard":
                        int matches = 0;
                        foreach (var a in player.Assets)
                        {
                            if (Selectors.AssetMatches(content, a, (JObject)c["filter"]!["asset"]!)) matches++;
                        }
                        if (matches < ((int?)c["count"] ?? 1)) return false;
                        break;
                }
            }
            return player.Credits >= credits;
        }
    }

    /// <summary>
    /// Runs the "trigger" abilities a player has for an event ("After you win a combat, gain 4,000"),
    /// with the event's context. Several at once: the current player orders them (R-21.10). Optional
    /// ones ask first (R-21.2). Vars: "event", "player", "ctx.*".
    /// </summary>
    internal sealed class TriggersHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.triggers";

        public string Id => HandlerId;

        public static Frame For(string evt, string playerId, IDictionary<string, string>? context = null)
        {
            var f = new Frame(HandlerId);
            f.Set("event", evt);
            f.Set("player", playerId);
            if (context != null)
            {
                foreach (var kv in context) f.Set("ctx." + kv.Key, kv.Value);
            }
            return f;
        }

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    var context = FrameContext.Of(frame);
                    var keys = new List<string>();
                    foreach (var a in AbilitySources.Of(ctx.Content, player))
                    {
                        var timing = a.Ability["timing"]!;
                        var progress = Goals.Progress(a);
                        if (progress != null)
                        {
                            // Goals (R-18.1 to R-18.5): a counted event adds a goal token; enough tokens, or the
                            // event of an "on_event" goal, completes it.
                            if (progress["count"] is JObject count && (string?)count["event"] == frame.Get("event")
                                && Conditions.Eval(ctx.Content, ctx.State, player, count["filter"], null, context))
                            {
                                int n = Goals.AddToken(player, a.Key);
                                ctx.Events.Add(new GoalProgressEvent(player.Id, a.CardId, a.Path, n, (int)count["times"]!));
                                if (n >= (int)count["times"]!) keys.Add(a.Key);
                            }
                            else if (progress["on_event"] is JObject on && (string?)on["event"] == frame.Get("event")
                                && Conditions.Eval(ctx.Content, ctx.State, player, on["filter"], null, context)
                                && Conditions.Eval(ctx.Content, ctx.State, player, a.Ability["condition"], null, context)
                                && FrameContext.CostPayable(ctx.Content, player, a.Ability))
                            {
                                keys.Add(a.Key);
                            }
                            continue;
                        }
                        if ((string?)timing["type"] != "trigger" || (string?)timing["event"] != frame.Get("event")) continue;
                        if (!Conditions.Eval(ctx.Content, ctx.State, player, timing["filter"], null, context)) continue;
                        if (!Conditions.Eval(ctx.Content, ctx.State, player, a.Ability["condition"], null, context)) continue;
                        if (!FrameContext.CostPayable(ctx.Content, player, a.Ability)) continue;
                        if ((string?)a.Ability["limit"] == "once_per_turn" && ctx.State.UsedAbilities.Contains("turn:" + a.Key)) continue;
                        keys.Add(a.Key);
                    }
                    if (keys.Count == 0)
                    {
                        return StepResult.Done();
                    }
                    frame.Set("queue", string.Join("|", keys));
                    frame.Step = 2;
                    if (keys.Count > 1)
                    {
                        frame.Step = 1;
                        var options = new List<DecisionOption>();
                        foreach (var k in keys) options.Add(new DecisionOption(k, k));
                        return StepResult.Await(ctx.AskOrder("rules.order_abilities", options, "order"));
                    }
                    return StepResult.Continue();
                }
                case 1:
                    frame.Set("queue", frame.Get("order").Replace(",", "|"));
                    frame.Step = 2;
                    return StepResult.Continue();
                case 2:
                {
                    var queue = new List<string>(frame.Get("queue").Split('|'));
                    if (queue.Count == 0 || queue[0].Length == 0)
                    {
                        return StepResult.Done();
                    }
                    var key = queue[0];
                    queue.RemoveAt(0);
                    frame.Set("queue", string.Join("|", queue));
                    frame.Set("current", key);
                    var ability = Find(ctx.Content, player, key);
                    if (ability == null)
                    {
                        return StepResult.Continue(); // the card left play meanwhile
                    }
                    if ((bool?)ability.Ability["optional"] ?? false)
                    {
                        frame.Step = 3;
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.use_ability",
                            new[] { new DecisionOption("yes", key), new DecisionOption("no", key) }, "use"));
                    }
                    return Run(frame, player, ability, ctx.State);
                }
                default:
                {
                    frame.Step = 2;
                    var ability = Find(ctx.Content, player, frame.Get("current"));
                    if (frame.Get("use") != "yes" || ability == null)
                    {
                        return StepResult.Continue();
                    }
                    return Run(frame, player, ability, ctx.State);
                }
            }
        }

        private static StepResult Run(Frame frame, PlayerState player, AbilityRef ability, GameState? state = null)
        {
            frame.Step = 2;
            if (state != null && (string?)ability.Ability["limit"] == "once_per_turn")
            {
                state.UsedAbilities.Add("turn:" + ability.Key); // R-21.6
            }
            var use = new Frame(EffectHandlers.Ability);
            use.Set("owner", player.Id);
            use.Set("card", ability.CardId);
            use.Set("path", ability.Path);
            FrameContext.Copy(frame, use);
            return StepResult.Call(use);
        }

        internal static AbilityRef? Find(GameContent content, PlayerState player, string key)
        {
            foreach (var a in AbilitySources.Of(content, player))
            {
                if (a.Key == key) return a;
            }
            return null;
        }
    }

    /// <summary>
    /// Suffering damage (R-8.1 to R-8.4, R-8.8) as a frame, so replacements can step in: caps ("you suffer
    /// a maximum of 2 damage", applied before the damage), and "when you would become defeated ... instead"
    /// (IG-88, Hound's Tooth), offered once the damage would reach health or hull. If the player is still
    /// at the limit afterwards, they become defeated. Vars: "player", "target" (ground | ship), "amount", "ctx.*".
    /// </summary>
    internal sealed class SufferDamageHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.suffer_damage";
        private const string Decline = "decline";

        public string Id => HandlerId;

        public static Frame For(string playerId, CombatType target, int amount, IDictionary<string, string>? context = null)
        {
            var f = new Frame(HandlerId);
            f.Set("player", playerId);
            f.Set("target", target == CombatType.Ground ? "ground" : "ship");
            f.Set("amount", amount);
            if (context != null)
            {
                foreach (var kv in context) f.Set("ctx." + kv.Key, kv.Value);
            }
            return f;
        }

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var target = frame.Get("target") == "ground" ? CombatType.Ground : CombatType.Ship;
            var context = FrameContext.Of(frame);
            switch (frame.Step)
            {
                case 0:
                {
                    if (player.Defeated)
                    {
                        return StepResult.Done(); // already defeated: tokens stay at the limit (R-8.4)
                    }
                    int amount = Math.Min(frame.GetInt("amount"), Cap(ctx, player, context));
                    int placed = Combat.Place(ctx.Content, ctx.State, player, target, amount);
                    if (amount > 0)
                    {
                        ctx.Events.Add(new DamageSufferedEvent(player.Id, target == CombatType.Ground ? "character" : "ship",
                            frame.GetInt("amount"), placed, false));
                    }
                    if (!Combat.AtLimit(ctx.Content, ctx.State, player))
                    {
                        return StepResult.Done();
                    }
                    var replacements = Replacements(ctx, player, context);
                    frame.Step = 2;
                    if (replacements.Count == 0)
                    {
                        return StepResult.Continue();
                    }
                    foreach (var r in replacements)
                    {
                        if (!((bool?)r.Ability["optional"] ?? false))
                        {
                            return Run(frame, player, r); // mandatory: Hound's Tooth rolls
                        }
                    }
                    var options = new List<DecisionOption>();
                    foreach (var r in replacements) options.Add(new DecisionOption(r.Key, r.CardId + ":" + r.Path));
                    options.Add(new DecisionOption(Decline, "rules.decline"));
                    frame.Step = 1;
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.would_be_defeated", options, "choice"));
                }
                case 1:
                {
                    frame.Step = 2;
                    if (frame.Get("choice") == Decline)
                    {
                        return StepResult.Continue();
                    }
                    var ability = TriggersHandler.Find(ctx.Content, player, frame.Get("choice"));
                    return ability == null ? StepResult.Continue() : Run(frame, player, ability);
                }
                default:
                    if (Combat.AtLimit(ctx.Content, ctx.State, player) && !player.Defeated)
                    {
                        PlayerRules.BecomeDefeated(ctx.State, player, ctx.Events);
                    }
                    return StepResult.Done();
            }
        }

        private static StepResult Run(Frame frame, PlayerState player, AbilityRef ability)
        {
            frame.Step = 2;
            var use = new Frame(EffectHandlers.Ability);
            use.Set("owner", player.Id);
            use.Set("card", ability.CardId);
            use.Set("path", ability.Path);
            FrameContext.Copy(frame, use);
            return StepResult.Call(use);
        }

        /// <summary>"When you win a ground combat, you suffer a maximum of 2 damage" (IQA-11 Blaster Rifle).</summary>
        private static int Cap(ResolutionContext ctx, PlayerState player, Dictionary<string, string> context)
        {
            int cap = int.MaxValue;
            foreach (var a in AbilitySources.Of(ctx.Content, player))
            {
                var timing = a.Ability["timing"]!;
                if ((string?)timing["type"] != "replacement" || (string?)timing["event"] != "damage_suffered") continue;
                if (!Conditions.Eval(ctx.Content, ctx.State, player, timing["filter"], null, context)) continue;
                foreach (JObject e in (JArray)a.Ability["effects"]!)
                {
                    if ((string?)e["op"] == "damage.cap") cap = Math.Min(cap, (int)e["max"]!);
                }
            }
            return cap;
        }

        private static List<AbilityRef> Replacements(ResolutionContext ctx, PlayerState player, Dictionary<string, string> context)
        {
            var result = new List<AbilityRef>();
            foreach (var a in AbilitySources.Of(ctx.Content, player))
            {
                var timing = a.Ability["timing"]!;
                if ((string?)timing["type"] != "replacement" || (string?)timing["event"] != "would_be_defeated") continue;
                if (!Conditions.Eval(ctx.Content, ctx.State, player, timing["filter"], null, context)) continue;
                if (!Conditions.Eval(ctx.Content, ctx.State, player, a.Ability["condition"], null, context)) continue;
                if (!FrameContext.CostPayable(ctx.Content, player, a.Ability)) continue;
                result.Add(a);
            }
            return result;
        }
    }

    /// <summary>"Roll a die. On a {FOCUS} or {HIT} result, ...": rolls and runs every outcome that matches.</summary>
    internal sealed class EffectDiceRollHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.dice_roll";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var outcomes = (JArray)node["outcomes"]!;
            if (frame.Step == 0)
            {
                var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
                var faces = DiceRoller.Roll(ctx.State.Random, Selectors.Amount(ctx.Content, owner, node["count"]!));
                frame.Set("faces", DicePool.Encode(faces));
                ctx.Events.Add(new EffectDiceRolledEvent(owner.Id, Combat.PlayerLabel(ctx.Content, owner), frame.Get("card"), faces));
                frame.Set("k", 0);
                frame.Step = 1;
            }
            var rolled = DicePool.Effective(DicePool.Decode(frame.Get("faces")));
            int k = frame.GetInt("k");
            while (k < outcomes.Count)
            {
                var outcome = (JObject)outcomes[k];
                k++;
                frame.Set("k", k);
                if (Matches(outcome, rolled))
                {
                    return StepResult.Call(EffectHandlers.ListFrame(frame.Get("owner"), frame.Get("card"),
                        frame.Get("path") + ".outcomes[" + (k - 1).ToString(CultureInfo.InvariantCulture) + "].effects"));
                }
            }
            return StepResult.Done();
        }

        private static bool Matches(JObject outcome, List<DieFace> rolled)
        {
            if (outcome["faces"] is JArray faces)
            {
                foreach (var f in faces)
                {
                    if (rolled.Contains(DicePool.Parse((string)f!))) return true;
                }
                return false;
            }
            if (outcome["min_hits"] != null)
            {
                return DiceRoller.Count(rolled, DieFace.Hit) >= (int)outcome["min_hits"]!;
            }
            return true;
        }
    }

    /// <summary>"Discard 1 of your crew": the owner picks which matching asset; fails if there is none.</summary>
    internal sealed class AssetDiscardHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.asset_discard";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            if (frame.Step == 1)
            {
                Assets.Discard(owner, Assets.Find(owner, frame.Get("pick"))!, "effect", ctx.Events);
                return StepResult.Done();
            }
            var filter = (JObject)node["filter"]!["asset"]!;
            var options = new List<DecisionOption>();
            foreach (var a in owner.Assets)
            {
                if (Selectors.AssetMatches(ctx.Content, a, filter)) options.Add(new DecisionOption(a.CardId, a.CardId));
            }
            if (options.Count == 0)
            {
                ctx.Return("failed", "true");
                return StepResult.Done();
            }
            frame.Step = 1;
            if (options.Count == 1)
            {
                frame.Set("pick", options[0].Id);
                return StepResult.Continue();
            }
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.choose_asset", options, "pick"));
        }
    }
}
