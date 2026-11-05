#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Jobs (R-15.25 to R-15.41) and the databank procedures they call (R-13, EFFECTS.md §5.4).</summary>
    public static class Jobs
    {
        public const string AttemptHandler = "encounter.job";
        public const string StepsHandler = "rules.databank_steps";
        public const string ResolveHandler = "rules.databank_resolve";
        public const string SabaccHandler = "effects.sabacc";

        /// <summary>R-15.29: whether the player is at the job's destination (another player there, when it names one: R-15.30).</summary>
        public static bool AtDestination(GameContent content, GameState state, PlayerState player, JObject job)
        {
            var dest = job["destination"];
            if (dest == null || dest.Type == JTokenType.Null) return true;
            if (dest is JObject o && o["player_space"] is JObject ps)
            {
                var choose = ps["player"]?["choose"] as JObject;
                foreach (var p in state.Players)
                {
                    if (p.Id == player.Id || p.SpaceId != player.SpaceId) continue;
                    if (choose == null || Conditions.Eval(content, state, player, choose["where"], p)) return true;
                }
                return false;
            }
            if (dest.Type == JTokenType.String && (string)dest! == "your_space") return true;
            if (dest.Type == JTokenType.String && content.Map.Contains((string)dest!)) return player.SpaceId == (string)dest!; // starting jobs (#92)
            return SpaceSelect.Options(content, state, player, dest).Contains(player.SpaceId);
        }

        /// <summary>The nearest frame of a handler on the stack (the job, procedure or sabacc game in progress).</summary>
        internal static Frame? Nearest(GameState state, string handler)
        {
            for (int i = state.Stack.Count - 1; i >= 0; i--)
            {
                if (state.Stack[i].Handler == handler) return state.Stack[i];
            }
            return null;
        }

        /// <summary>Credits a reward gives on top of its own: "increase the reward for each of your jobs by 2,000" (R-15.37).</summary>
        public static int RewardBonus(GameContent content, GameState state, PlayerState player, string kind)
        {
            int bonus = 0;
            foreach (var a in AbilitySources.Of(content, player))
            {
                if (a.TimingType != "passive") continue;
                foreach (JObject e in (JArray)a.Ability["effects"]!)
                {
                    if ((string?)e["op"] == "reward.modify" && (string?)e["asset"] == kind) bonus += (int)e["by"]!;
                }
            }
            return bonus;
        }
    }

    /// <summary>
    /// Attempting a job (R-15.29 to R-15.39) as the turn's encounter: its "Encounter" effects run; "complete
    /// this job" gives the reward and the card is removed or discarded (R-15.37); otherwise, or if the player
    /// is defeated during it (R-15.39), the job fails and the card is kept (R-15.38). "Claim this reward" (Wanted,
    /// sabacc) gives the reward without counting as completing the job (R-18.7). Vars: "player", "target" (job).
    /// </summary>
    internal sealed class JobAttemptHandler : IResolutionHandler
    {
        public string Id => Jobs.AttemptHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Vars.ContainsKey("player") ? frame.Get("player") : state.CurrentPlayerId)!;
            var job = frame.Get("target");
            var card = ctx.Content.Card(job);
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    ctx.Events.Add(new JobAttemptedEvent(player.Id, job));
                    var list = EffectHandlers.ListFrame(player.Id, job, "encounter");
                    list.Set("ctx.job", job);
                    return StepResult.Call(list);
                }
                case 1:
                {
                    bool done = frame.Vars.ContainsKey("completed") || frame.Vars.ContainsKey("claimed");
                    bool defeatedFails = player.Defeated && !frame.Vars.ContainsKey("keep_on_defeat"); // R-15.39, R-25.10
                    if (!done || defeatedFails || frame.Vars.ContainsKey("discarded"))
                    {
                        frame.Step = 9;
                        ctx.Events.Add(new JobFailedEvent(player.Id, job));
                        return StepResult.Continue();
                    }
                    frame.Step = 2;
                    frame.Set("credits_before", player.Credits);
                    if (frame.Vars.ContainsKey("reward_given")) return StepResult.Continue(); // the claim already paid it
                    var reward = EffectHandlers.ListFrame(player.Id, job, "reward");
                    reward.Set("ctx.job", job);
                    return StepResult.Call(reward);
                }
                case 2:
                {
                    frame.Step = 3;
                    int bonus = Jobs.RewardBonus(ctx.Content, state, player, "job");
                    if (bonus > 0 && !frame.Vars.ContainsKey("reward_given")) PlayerRules.GainCredits(player, bonus, ctx.Events);
                    int gained = System.Math.Max(0, player.Credits - frame.GetInt("credits_before"));
                    var asset = Assets.Find(player, job);
                    if (asset != null && !frame.Vars.ContainsKey("keep_card"))
                    {
                        if ((string?)card["end"] == "remove")
                        {
                            player.Assets.Remove(asset); // "remove this card from the game"
                            ctx.Events.Add(new AssetDiscardedEvent(player.Id, job, "job", "removed"));
                        }
                        else
                        {
                            Assets.Discard(ctx.Content, state, player, asset, "job_completed", ctx.Events);
                        }
                    }
                    ctx.Events.Add(new JobCompletedEvent(player.Id, job, frame.Vars.ContainsKey("completed")));
                    if (!frame.Vars.ContainsKey("completed")) return StepResult.Continue();
                    // R-18.7: only "complete this job" counts for goals; Sy Snootles may trade the credits for fame.
                    return StepResult.Call(TriggersHandler.For("job_completed", player.Id,
                        new Dictionary<string, string> { ["reward_credits"] = gained.ToString(CultureInfo.InvariantCulture) }));
                }
                default:
                    ctx.Events.Add(new EncounterResolvedEvent(player.Id, "job", job));
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// "Resolve card #N" (R-13.2, R-13.3): a random card of that number. Job procedures run step by step; a
    /// favor check reads the section of the FAVOR job being resolved; a contact card its effects.
    /// </summary>
    internal sealed class DatabankResolveHandler : IResolutionHandler
    {
        public string Id => Jobs.ResolveHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            if (frame.Step > 0)
            {
                EffectHandlers.MarkFailedFromChild(frame);
                EffectHandlers.ReturnFailed(ctx, frame);
                return StepResult.Done();
            }
            frame.Step = 1;
            var versions = ctx.Content.DatabankCards((int)node["number"]!);
            if (versions.Count == 0)
            {
                ctx.Return("failed", "true");
                return StepResult.Done();
            }
            var cardId = versions[ctx.State.Random.NextInt(versions.Count)];
            var card = ctx.Content.Card(cardId);
            ctx.Events.Add(new DatabankResolvedEvent(owner.Id, cardId));
            switch ((string)card["kind"]!)
            {
                case "job_steps":
                {
                    var steps = new Frame(Jobs.StepsHandler);
                    steps.Set("owner", owner.Id);
                    steps.Set("card", cardId);
                    FrameContext.Copy(frame, steps);
                    return StepResult.Call(steps);
                }
                case "favor_check":
                {
                    var context = FrameContext.Of(frame);
                    var job = context.TryGetValue("job", out var j) ? j : "";
                    var faction = job.Length > 0 ? (string?)ctx.Content.Card(job)["favor"] : null;
                    if (faction == null) return StepResult.Done();
                    return StepResult.Call(EffectHandlers.ListFrame(owner.Id, cardId, "sections." + faction, frame));
                }
                default:
                    return StepResult.Call(EffectHandlers.ListFrame(owner.Id, cardId, "effects", frame));
            }
        }
    }

    /// <summary>
    /// R-15.36: a procedure is read from step 1, then the next consecutive step, unless a step says "skip to /
    /// return to step N" or "repeat this step". It ends with its last step, with "complete this job" or "the job
    /// fails", when a sabacc game is won, or when the job's player is defeated (R-15.39).
    /// </summary>
    internal sealed class StepsHandler : IResolutionHandler
    {
        public string Id => Jobs.StepsHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var steps = (JArray)ctx.Content.Card(frame.Get("card"))["steps"]!;
            int i = frame.Vars.ContainsKey("i") ? frame.GetInt("i") : 0;
            if (frame.Step == 1)
            {
                // The step just ran.
                if (frame.Vars.TryGetValue("goto", out var g))
                {
                    i = int.Parse(g, CultureInfo.InvariantCulture) - 1;
                    frame.Vars.Remove("goto");
                }
                else if (frame.Vars.ContainsKey("repeat"))
                {
                    frame.Vars.Remove("repeat");
                }
                else
                {
                    i++;
                }
                frame.Set("i", i);
                var job = Jobs.Nearest(ctx.State, Jobs.AttemptHandler);
                if (owner.Defeated && job != null && !job.Vars.ContainsKey("keep_on_defeat")) frame.Set("stop", "1"); // R-15.39
            }
            if (frame.Vars.ContainsKey("stop") || i >= steps.Count) return StepResult.Done();
            frame.Step = 1;
            ctx.Events.Add(new DatabankStepEvent(owner.Id, frame.Get("card"), i + 1));
            return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"), "steps[" + i.ToString(CultureInfo.InvariantCulture) + "]", frame));
        }

        /// <summary>Applies a procedure-control effect; false when there is no procedure or job to act on.</summary>
        internal static bool Control(GameState state, string op, JObject node)
        {
            var steps = Jobs.Nearest(state, Jobs.StepsHandler);
            var job = Jobs.Nearest(state, Jobs.AttemptHandler);
            switch (op)
            {
                case "step.goto":
                    steps?.Set("goto", (int)node["step"]!);
                    return steps != null;
                case "step.repeat":
                    steps?.Set("repeat", "1");
                    return steps != null;
                case "job.complete":
                    steps?.Set("stop", "1");
                    job?.Set("completed", "1");
                    return job != null;
                case "job.fail":
                    steps?.Set("stop", "1");
                    job?.Set("failed", "1");
                    return job != null;
                case "sabacc.win":
                {
                    var game = Jobs.Nearest(state, Jobs.SabaccHandler);
                    steps?.Set("stop", "1");
                    game?.Set("winner", (string)node["who"]!);
                    return game != null;
                }
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// "If you have {REP_POS} Syndicate reputation, discard this job" (favor check #41a): the job being resolved is
    /// discarded, and so it fails.
    /// </summary>
    internal static class JobDiscard
    {
        public static bool Apply(ResolutionContext ctx, PlayerState owner, IReadOnlyDictionary<string, string> context)
        {
            var frame = Jobs.Nearest(ctx.State, Jobs.AttemptHandler);
            if (!context.TryGetValue("job", out var job)) return false;
            var asset = Assets.Find(owner, job);
            if (asset == null) return false;
            Assets.Discard(ctx.Content, ctx.State, owner, asset, "effect", ctx.Events);
            frame?.Set("discarded", "1");
            Jobs.Nearest(ctx.State, Jobs.StepsHandler)?.Set("stop", "1");
            return true;
        }
    }

    /// <summary>
    /// A game of sabacc (databank #40) between the owner and an opponent (EFFECTS.md §5.4). The caller supplies the
    /// opponent, the stake and the reward: Cantonica-9, Tatooine-6 (secret), Low Stakes Game of Sabacc (Nal
    /// Hutta-7) and Legendary Game of Sabacc-7. The winner gains the reward and any credits placed on the card.
    /// </summary>
    internal sealed class SabaccHandler : IResolutionHandler
    {
        public string Id => Jobs.SabaccHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var p = node["params"] as JObject ?? new JObject();
            var state = ctx.State;
            var owner = state.FindPlayer(frame.Get("owner"))!;
            int stake = (int?)p["stake"] ?? 0;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    bool anywhere = (string?)p["opponent"] == "invite_anywhere";
                    int min = System.Math.Max(stake, (int?)p["min_credits"] ?? 0);
                    var options = new List<string>();
                    foreach (var other in state.Players)
                    {
                        if (other.Id == owner.Id || other.Defeated) continue;
                        if (!anywhere && other.SpaceId != owner.SpaceId) continue;
                        if (other.Credits < min) continue; // "You cannot resolve this card if either player has less than ..."
                        options.Add(other.Id);
                    }
                    if (options.Count == 0 || owner.Credits < stake) return Fail(ctx, frame);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_player", options, "opponent");
                }
                case 1:
                    frame.Step = 2;
                    if ((string?)p["opponent"] != "invite_anywhere") return StepResult.Continue();
                    // Cantonica-9: "That player chooses whether or not to participate."
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, frame.Get("opponent"), "custom.sabacc_invite",
                        new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "joins"));
                case 2:
                {
                    frame.Step = 3;
                    var opponent = state.FindPlayer(frame.Get("opponent"))!;
                    if (frame.Vars.TryGetValue("joins", out var joins))
                    {
                        if (joins != "yes") return Fail(ctx, frame);
                        var to = (string?)p["move_to"];
                        if (to != null && opponent.SpaceId != to)
                        {
                            var from = opponent.SpaceId;
                            opponent.SpaceId = to; // "they move directly to Cantonica" (R-5.23)
                            ctx.Events.Add(new PlayerMovedEvent(opponent.Id, from, new List<string> { to }, true));
                        }
                    }
                    if (stake > 0)
                    {
                        PlayerRules.LoseCredits(owner, stake, ctx.Events); // "you each lose ..."
                        PlayerRules.LoseCredits(opponent, stake, ctx.Events);
                    }
                    var versions = ctx.Content.DatabankCards(40);
                    var card = versions[state.Random.NextInt(versions.Count)];
                    ctx.Events.Add(new DatabankResolvedEvent(owner.Id, card));
                    var steps = new Frame(Jobs.StepsHandler);
                    steps.Set("owner", owner.Id);
                    steps.Set("card", card);
                    FrameContext.Copy(frame, steps);
                    steps.Set("ctx.opponent", opponent.Id);
                    return StepResult.Call(steps);
                }
                case 3:
                {
                    frame.Step = 4;
                    if (!frame.Vars.TryGetValue("winner", out var who)) return StepResult.Done();
                    var opponent = state.FindPlayer(frame.Get("opponent"))!;
                    var winner = who == "you" ? owner : opponent;
                    var loser = who == "you" ? opponent : owner;
                    int pot = (frame.Vars.ContainsKey("pot") ? frame.GetInt("pot") : 0);
                    PlayerRules.GainCredits(winner, pot, ctx.Events); // "and all credits on this card"
                    ctx.Events.Add(new SabaccWonEvent(winner.Id, loser.Id));
                    frame.Set("winner_id", winner.Id);
                    frame.Set("loser_id", loser.Id);
                    var job = Jobs.Nearest(state, Jobs.AttemptHandler);
                    if (p["reward"] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(winner.Id, frame.Get("card"), frame.Get("path") + ".params.reward", frame));
                    }
                    if (p["credits"] != null)
                    {
                        // Legendary Game of Sabacc: "If the active player claims this reward, they also gain 1 fame and discard this card."
                        PlayerRules.GainCredits(winner, (int)p["credits"]!, ctx.Events);
                        if (winner.Id == owner.Id && job != null)
                        {
                            PlayerRules.GainFame(winner, (int?)p["active_player_fame"] ?? 0, ctx.Events);
                            job.Set("claimed", "1");
                            job.Set("reward_given", "1");
                        }
                        return StepResult.Done();
                    }
                    if ((string?)p["reward"] == "steal_6000_or_trade_sheets")
                    {
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, winner.Id, "custom.sabacc_reward",
                            new[] { new DecisionOption("credits", "custom.take_6000"), new DecisionOption("sheets", "custom.trade_sheets") }, "reward"));
                    }
                    // A job's own reward (Low Stakes Game of Sabacc): the job is claimed if you win (R-15.37);
                    // if your opponent wins, they gain the reward and you keep the job.
                    if (winner.Id == owner.Id)
                    {
                        job?.Set("claimed", "1");
                        return StepResult.Done();
                    }
                    var jobId = FrameContext.Of(frame).TryGetValue("job", out var j) ? j : "";
                    if (jobId.Length == 0) return StepResult.Done();
                    return StepResult.Call(EffectHandlers.ListFrame(winner.Id, jobId, "reward"));
                }
                default:
                {
                    if (frame.Vars.TryGetValue("reward", out var r))
                    {
                        var winner = state.FindPlayer(frame.Get("winner_id"))!;
                        var loser = state.FindPlayer(frame.Get("loser_id"))!;
                        frame.Vars.Remove("reward");
                        if (r == "credits")
                        {
                            PlayerRules.GainCredits(winner, PlayerRules.LoseCredits(loser, 6000, ctx.Events), ctx.Events);
                        }
                        else
                        {
                            TradeSheets(ctx, winner, loser);
                            var fit = new Frame(Assets.FitHandler);
                            fit.Set("player", winner.Id);
                            var fit2 = new Frame(Assets.FitHandler);
                            fit2.Set("player", loser.Id);
                            ctx.State.Stack.Add(fit2);
                            return StepResult.Call(fit);
                        }
                    }
                    return StepResult.Done();
                }
            }
        }

        private static StepResult Fail(ResolutionContext ctx, Frame frame)
        {
            ctx.Return("failed", "true");
            frame.Step = 9;
            return StepResult.Done();
        }

        /// <summary>R-25.13: sheets keep their side, damage and goal tokens; the assets stay with their owners.</summary>
        internal static void TradeSheets(ResolutionContext ctx, PlayerState a, PlayerState b)
        {
            var sheet = a.ShipSheetId;
            var side = a.ShipSideId;
            var damage = a.ShipDamage;
            a.ShipSheetId = b.ShipSheetId;
            a.ShipSideId = b.ShipSideId;
            a.ShipDamage = b.ShipDamage;
            b.ShipSheetId = sheet;
            b.ShipSideId = side;
            b.ShipDamage = damage;
            foreach (var t in new List<GoalToken>(a.GoalTokens))
            {
                if (t.Key.StartsWith(b.ShipSheetId + "#", System.StringComparison.Ordinal)) { a.GoalTokens.Remove(t); b.GoalTokens.Add(t); }
            }
            foreach (var t in new List<GoalToken>(b.GoalTokens))
            {
                if (t.Key.StartsWith(a.ShipSheetId + "#", System.StringComparison.Ordinal)) { b.GoalTokens.Remove(t); a.GoalTokens.Add(t); }
            }
            ctx.Events.Add(new ShipChangedEvent(a.Id, b.ShipSheetId, a.ShipSheetId, a.ShipSideId, 0));
            ctx.Events.Add(new ShipChangedEvent(b.Id, a.ShipSheetId, b.ShipSheetId, b.ShipSideId, 0));
        }
    }

    /// <summary>
    /// Sabacc #40a: "You may place any amount of your credits on this card" / "Your opponent may place the same
    /// amount (or all of their credits if they do not have enough)". If not, the "else" effects.
    /// </summary>
    internal sealed class SabaccStakeHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.sabacc_stake";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var game = Jobs.Nearest(ctx.State, Jobs.SabaccHandler);
            if (game == null)
            {
                ctx.Return("failed", "true");
                return StepResult.Done();
            }
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var opponent = ctx.State.FindPlayer(game.Get("opponent"))!;
            bool you = (string)node["who"]! == "you";
            var who = you ? owner : opponent;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    if (you)
                    {
                        var options = new List<DecisionOption>();
                        for (int v = 0; v <= who.Credits; v += 1000)
                        {
                            options.Add(new DecisionOption(v.ToString(CultureInfo.InvariantCulture), v.ToString(CultureInfo.InvariantCulture)));
                        }
                        return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, who.Id, "custom.sabacc_stake", options, "amount"));
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, who.Id, "custom.sabacc_match",
                        new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "match"));
                }
                case 1:
                {
                    frame.Step = 2;
                    int amount = you ? frame.GetInt("amount")
                        : frame.Get("match") == "yes" ? System.Math.Min(game.Vars.ContainsKey("staked") ? game.GetInt("staked") : 0, who.Credits) : 0;
                    bool placed = you ? amount > 0 : frame.Get("match") == "yes";
                    if (placed)
                    {
                        PlayerRules.LoseCredits(who, amount, ctx.Events);
                        game.Set("pot", (game.Vars.ContainsKey("pot") ? game.GetInt("pot") : 0) + amount);
                        if (you) game.Set("staked", amount);
                        return StepResult.Done();
                    }
                    if (node["else"] is JArray)
                    {
                        return StepResult.Call(EffectHandlers.ListFrame(owner.Id, frame.Get("card"), frame.Get("path") + ".else", frame));
                    }
                    return StepResult.Done();
                }
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// Sabacc #40a step 3: "You choose to test either Knowledge or Tactics. Then, your opponent chooses... If only 1
    /// player passes, that player gains the reward... If both players pass or both players fail, repeat this step."
    /// </summary>
    internal sealed class SabaccDuelHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.sabacc_duel";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var game = Jobs.Nearest(ctx.State, Jobs.SabaccHandler)!;
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var opponent = ctx.State.FindPlayer(game.Get("opponent"))!;
            var skills = ((JArray)node["skills"]!).ToObject<List<string>>()!;
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "custom.choose_skill", skills, "mine");
                case 1:
                    frame.Step = 2;
                    return SpaceSelect.AskOrSet(ctx, frame, opponent.Id, "custom.choose_skill", skills, "theirs");
                case 2:
                {
                    frame.Step = 3;
                    var test = new Frame(SkillTests.Handler);
                    test.Set("player", owner.Id);
                    test.Set("skills", frame.Get("mine"));
                    return StepResult.Call(test);
                }
                case 3:
                {
                    frame.Step = 4;
                    frame.Set("mine_passed", frame.Get("ret.passed"));
                    var test = new Frame(SkillTests.Handler);
                    test.Set("player", opponent.Id);
                    test.Set("skills", frame.Get("theirs"));
                    return StepResult.Call(test);
                }
                default:
                {
                    bool mine = frame.Get("mine_passed") == "true", theirs = frame.Get("ret.passed") == "true";
                    var control = new JObject { ["who"] = mine ? "you" : "opponent" };
                    if (mine != theirs) StepsHandler.Control(ctx.State, "sabacc.win", control);
                    else StepsHandler.Control(ctx.State, "step.repeat", control);
                    return StepResult.Done();
                }
            }
        }
    }

    /// <summary>
    /// "Wanted" (R-15.92, R-25.10): a combat against another player in your space with {REP_NEG} reputation; on a win
    /// the reward is claimed, even if you are defeated: 5,000 and 2 fame if they had more fame than you, else 1.
    /// </summary>
    internal sealed class WantedHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.wanted";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var options = new List<string>();
                    foreach (var p in ctx.State.Players)
                    {
                        if (p.Id != owner.Id && p.SpaceId == owner.SpaceId && !p.Defeated
                            && Conditions.Eval(ctx.Content, ctx.State, owner, JObject.Parse("{\"cond\":\"rep_count\",\"state\":\"neg\",\"min\":1}"), p))
                        {
                            options.Add(p.Id);
                        }
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_player", options, "target");
                }
                case 1:
                {
                    frame.Step = 2;
                    var target = ctx.State.FindPlayer(frame.Get("target"))!;
                    frame.Set("more_fame", PlayerRules.Fame(ctx.Content, ctx.State, target) > PlayerRules.Fame(ctx.Content, ctx.State, owner) ? "1" : "0");
                    var combat = new Frame(Combat.Handler);
                    combat.Set("type", Combat.BetweenPlayers(ctx.Content, owner, target) == CombatType.Ground ? "ground" : "ship");
                    combat.Set("attacker", owner.Id);
                    combat.Set("defender_kind", "player");
                    combat.Set("defender", target.Id);
                    combat.Set("defender_value", "0");
                    return StepResult.Call(combat);
                }
                case 2:
                {
                    frame.Step = 3;
                    if (frame.Get("ret.winner") != "attacker") return StepResult.Done();
                    PlayerRules.GainCredits(owner, 5000, ctx.Events);
                    PlayerRules.GainFame(owner, frame.Get("more_fame") == "1" ? 2 : 1, ctx.Events);
                    var job = Jobs.Nearest(ctx.State, Jobs.AttemptHandler);
                    job?.Set("claimed", "1");
                    job?.Set("reward_given", "1");
                    job?.Set("keep_on_defeat", "1"); // R-25.10
                    return StepResult.Done();
                }
                default:
                    return StepResult.Done();
            }
        }
    }
}
