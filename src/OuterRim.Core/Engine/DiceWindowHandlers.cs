#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Dice;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// A window after a roll where players may use abilities that change it (R-7.10 to R-7.12, R-7.15 to
    /// R-7.17; skill tests R-6.9). Vars: "target" (stack index of the frame holding the dice), "var" (its
    /// variable), "scope" (ground_combat, ship_combat, skill_test, delivery_roll), "roller" (player id, or
    /// empty when the game rolled), "players" (ids in the order they act: the roller first, R-7.11).
    /// Each player's mandatory abilities apply first; then they pick optional ones until they pass.
    /// Each ability once per window, plus its own limit (once per combat, test or turn).
    /// </summary>
    internal sealed class DiceWindowHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.dice_window";
        private const string Pass = "pass";

        /// <summary>Ops that change the roller's own dice, and ops that change an opponent's.</summary>
        private static readonly HashSet<string> OwnOps = new HashSet<string> { "dice.reroll", "dice.turn", "dice.add" };
        private static readonly HashSet<string> OpponentOps = new HashSet<string> { "dice.cancel" };

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var players = frame.Get("players").Length == 0 ? new string[0] : frame.Get("players").Split(',');
            int turn = frame.Vars.ContainsKey("turn") ? frame.GetInt("turn") : 0;
            if (frame.Vars.TryGetValue("choice", out var choice))
            {
                frame.Vars.Remove("choice");
                if (choice == Pass)
                {
                    frame.Set("turn", turn + 1);
                    return StepResult.Continue();
                }
                return Use(ctx, frame, players[turn], choice);
            }
            if (turn >= players.Length)
            {
                return StepResult.Done();
            }
            var owner = ctx.State.FindPlayer(players[turn])!;
            var usable = Usable(ctx, frame, owner);
            if (usable.Count == 0)
            {
                frame.Set("turn", turn + 1);
                return StepResult.Continue();
            }
            foreach (var a in usable)
            {
                if (!((bool?)a.Ability["optional"] ?? false))
                {
                    return Use(ctx, frame, owner.Id, a.Key); // mandatory: "cancel 1 of your opponent's hits"
                }
            }
            var options = new List<DecisionOption>();
            foreach (var a in usable)
            {
                options.Add(new DecisionOption(a.Key, a.CardId + ":" + a.Path));
            }
            options.Add(new DecisionOption(Pass, "rules.pass"));
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "rules.dice_window", options, "choice"));
        }

        private StepResult Use(ResolutionContext ctx, Frame frame, string ownerId, string key)
        {
            var owner = ctx.State.FindPlayer(ownerId)!;
            AbilityRef? ability = null;
            foreach (var a in AbilitySources.Of(ctx.Content, owner))
            {
                if (a.Key == key) ability = a;
            }
            MarkUsed(ctx, frame, ability!);
            return StepResult.Call(EffectHandlers.ListFrame(owner.Id, ability!.CardId, ability.Path + ".effects"));
        }

        internal static List<AbilityRef> Usable(ResolutionContext ctx, Frame frame, PlayerState owner)
        {
            var result = new List<AbilityRef>();
            string scope = frame.Get("scope");
            bool rollerIsOwner = frame.Get("roller") == owner.Id;
            foreach (var a in AbilitySources.Of(ctx.Content, owner))
            {
                var timing = a.Ability["timing"]!;
                string type = (string)timing["type"]!;
                bool fits = false;
                if (type == "window" && (string?)timing["window"] == "dice_modify")
                {
                    foreach (var s in (JArray)timing["scope"]!)
                    {
                        fits |= (string)s! == scope;
                    }
                    if (fits && timing["filter"] != null)
                    {
                        fits = Conditions.Eval(ctx.Content, ctx.State, owner, timing["filter"], null, FrameContext.Of(frame));
                    }
                }
                else if (type == "trigger" && (string?)timing["event"] == "dice_rolled")
                {
                    fits = rollerIsOwner; // "after you roll dice (for any reason)"
                }
                if (!fits || Used(ctx, frame, a))
                {
                    continue;
                }
                var ops = new HashSet<string>();
                CollectOps(a.Ability["effects"]!, ops);
                bool own = ops.Overlaps(OwnOps), opp = ops.Overlaps(OpponentOps);
                if ((own && !rollerIsOwner) || (opp && rollerIsOwner) || (!own && !opp))
                {
                    continue;
                }
                if (!Conditions.Eval(ctx.Content, ctx.State, owner, a.Ability["condition"]))
                {
                    continue;
                }
                if (!CanChangeSomething(ctx, frame, (JArray)a.Ability["effects"]!))
                {
                    continue; // e.g. "turn a focus" with no focus rolled: not worth asking
                }
                result.Add(a);
            }
            return result;
        }

        /// <summary>
        /// False only when every effect is a plain reroll, turn or cancel and none has a die to act on.
        /// Abilities with branches or added dice are always offered.
        /// </summary>
        private static bool CanChangeSomething(ResolutionContext ctx, Frame frame, JArray effects)
        {
            var dice = DicePool.Decode(ctx.State.Stack[frame.GetInt("target")].Get(frame.Get("var")));
            foreach (JObject e in effects)
            {
                string op = (string)e["op"]!;
                if (op != "dice.reroll" && op != "dice.turn" && op != "dice.cancel")
                {
                    return true;
                }
                if (DiceOpHandler.Candidates(e, dice, new List<int>()).Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static void CollectOps(JToken token, HashSet<string> ops)
        {
            if (token is JObject o)
            {
                if (o["op"]?.Type == JTokenType.String) ops.Add((string)o["op"]!);
                foreach (var p in o.Properties()) CollectOps(p.Value, ops);
            }
            else if (token is JArray arr)
            {
                foreach (var x in arr) CollectOps(x, ops);
            }
        }

        private static string LimitKey(ResolutionContext ctx, Frame frame, AbilityRef a, out Frame? holder)
        {
            string limit = (string?)a.Ability["limit"] ?? "";
            holder = null;
            switch (limit)
            {
                case "once_per_turn":
                    return "turn:" + a.Key;
                case "once_per_combat":
                case "once_per_test":
                    holder = ctx.State.Stack[frame.GetInt("target")];
                    return "used." + a.Key;
                default:
                    holder = frame;
                    return "used." + a.Key;
            }
        }

        private static bool Used(ResolutionContext ctx, Frame frame, AbilityRef a)
        {
            if (frame.Vars.ContainsKey("used." + a.Key))
            {
                return true; // each ability once per window
            }
            var key = LimitKey(ctx, frame, a, out var holder);
            return holder == null ? ctx.State.UsedAbilities.Contains(key) : holder.Vars.ContainsKey(key);
        }

        private static void MarkUsed(ResolutionContext ctx, Frame frame, AbilityRef a)
        {
            frame.Set("used." + a.Key, "1");
            var key = LimitKey(ctx, frame, a, out var holder);
            if (holder == null)
            {
                ctx.State.UsedAbilities.Add(key);
            }
            else
            {
                holder.Set(key, "1");
            }
        }

        /// <summary>The nearest dice window on the stack: the roll that dice ops change.</summary>
        internal static Frame Nearest(ResolutionContext ctx)
        {
            for (int i = ctx.State.Stack.Count - 1; i >= 0; i--)
            {
                if (ctx.State.Stack[i].Handler == HandlerId)
                {
                    return ctx.State.Stack[i];
                }
            }
            throw new InvalidOperationException("A dice effect needs a roll to change.");
        }
    }

    /// <summary>
    /// Changes the roll of the nearest dice window: reroll, turn, add, cancel (R-7.15 to R-7.17). Choosing
    /// which dice is a decision of the ability's owner; "up to" and "any number" let them stop early.
    /// </summary>
    internal sealed class DiceOpHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.dice";
        private const string Done = "done";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var window = DiceWindowHandler.Nearest(ctx);
            var target = ctx.State.Stack[window.GetInt("target")];
            var dice = DicePool.Decode(target.Get(window.Get("var")));
            string op = (string)node["op"]!;
            var owner = frame.Get("owner");

            if (op == "dice.add")
            {
                var face = DicePool.Parse((string)node["face"]!);
                for (int i = 0; i < (int)node["count"]!; i++)
                {
                    dice.Add(new Die(face, added: true));
                }
                Save(ctx, target, window, dice, owner, frame);
                return StepResult.Done();
            }

            // Selected dice so far.
            var picked = new List<int>();
            if (frame.Vars.TryGetValue("picked", out var p) && p.Length > 0)
            {
                foreach (var x in p.Split(',')) picked.Add(int.Parse(x, CultureInfo.InvariantCulture));
            }
            if (frame.Vars.TryGetValue("pick", out var pick))
            {
                frame.Vars.Remove("pick");
                if (pick == Done)
                {
                    return Apply(ctx, frame, node, target, window, dice, picked);
                }
                picked.Add(int.Parse(pick, CultureInfo.InvariantCulture));
                frame.Set("picked", string.Join(",", picked));
            }

            var candidates = Candidates(node, dice, picked);
            int max;
            bool optionalCount;
            CountOf(node["count"]!, out max, out optionalCount);
            if (picked.Count >= max || candidates.Count == 0)
            {
                return Apply(ctx, frame, node, target, window, dice, picked);
            }
            if (!optionalCount && candidates.Count <= max - picked.Count)
            {
                picked.AddRange(candidates); // every eligible die must be taken
                return Apply(ctx, frame, node, target, window, dice, picked);
            }
            var options = new List<DecisionOption>();
            foreach (var i in candidates)
            {
                options.Add(new DecisionOption(i.ToString(CultureInfo.InvariantCulture), DicePool.CodeOf(dice[i].Face).ToString()));
            }
            if (optionalCount || picked.Count > 0)
            {
                options.Add(new DecisionOption(Done, "rules.done"));
            }
            return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner, "effects.choose_die", options, "pick"));
        }

        private static void CountOf(JToken count, out int max, out bool optional)
        {
            if (count.Type == JTokenType.Integer)
            {
                max = (int)count;
                optional = false;
                return;
            }
            var o = (JObject)count;
            optional = true;
            max = o["up_to"] is JObject up ? (int)up["max"]! : int.MaxValue; // "any number"
        }

        internal static List<int> Candidates(JObject node, List<Die> dice, List<int> picked)
        {
            string op = (string)node["op"]!;
            var faces = new HashSet<DieFace>();
            var list = op == "dice.turn" ? node["from"] : op == "dice.cancel" ? node["face"] : node["faces"];
            if (list is JArray arr)
            {
                foreach (var f in arr) faces.Add(DicePool.Parse((string)f!));
            }
            var result = new List<int>();
            for (int i = 0; i < dice.Count; i++)
            {
                var d = dice[i];
                if (picked.Contains(i) || d.Canceled) continue;
                if (faces.Count > 0 && !faces.Contains(d.Face)) continue;
                if (op == "dice.reroll" && d.Added) continue;        // R-7.17
                if (op != "dice.cancel" && d.Turned) continue;       // R-7.16
                result.Add(i);
            }
            return result;
        }

        private static StepResult Apply(ResolutionContext ctx, Frame frame, JObject node, Frame target, Frame window,
            List<Die> dice, List<int> picked)
        {
            string op = (string)node["op"]!;
            foreach (var i in picked)
            {
                switch (op)
                {
                    case "dice.reroll":
                        dice[i].Face = DiceRoller.Roll(ctx.State.Random, 1)[0];
                        break;
                    case "dice.turn":
                        dice[i].Face = DicePool.Parse((string)node["to"]!);
                        dice[i].Turned = true;
                        break;
                    case "dice.cancel":
                        dice[i].Canceled = true;
                        break;
                }
            }
            Save(ctx, target, window, dice, frame.Get("owner"), frame);
            return StepResult.Done();
        }

        private static void Save(ResolutionContext ctx, Frame target, Frame window, List<Die> dice, string owner, Frame frame)
        {
            var before = target.Get(window.Get("var"));
            var after = DicePool.Encode(dice);
            if (before == after)
            {
                return; // nothing eligible: no change, no event
            }
            target.Set(window.Get("var"), after);
            ctx.Events.Add(new DiceModifiedEvent(owner, frame.Get("card"), (string)EffectHandlers.Token(ctx, frame)["op"]!, before, after));
        }
    }

    /// <summary>Starting a dice window from a rules frame (combat, skill test).</summary>
    internal static class DiceWindows
    {
        public static Frame For(GameState state, string var, string scope, string roller, IEnumerable<string> players)
        {
            var f = new Frame(DiceWindowHandler.HandlerId);
            f.Set("target", state.Stack.Count - 1); // the frame opening the window is on top
            f.Set("var", var);
            f.Set("scope", scope);
            f.Set("roller", roller);
            f.Set("players", string.Join(",", players));
            return f;
        }
    }
}
