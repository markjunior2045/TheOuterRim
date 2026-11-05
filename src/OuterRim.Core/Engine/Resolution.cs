#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Model;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// One level of the resolution stack: which handler runs, where it stopped and its local values.
    /// Everything is plain data, so a game paused mid-effect saves and loads like any other state
    /// (PLAN T1.2, RULES §26.2).
    /// </summary>
    public sealed class Frame
    {
        public Frame()
        {
        }

        public Frame(string handler)
        {
            Handler = handler;
        }

        /// <summary>Id of the <see cref="IResolutionHandler"/> that runs this frame.</summary>
        public string Handler { get; set; } = "";

        /// <summary>Where the handler resumes. Handlers advance it before pausing or calling another frame.</summary>
        public int Step { get; set; }

        /// <summary>Local values, including answers to decisions and values returned by called frames.</summary>
        public SortedDictionary<string, string> Vars { get; set; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public string Get(string key)
        {
            return Vars.TryGetValue(key, out var v) ? v : throw new InvalidOperationException(
                "Frame " + Handler + " has no value '" + key + "'.");
        }

        public int GetInt(string key)
        {
            return int.Parse(Get(key), System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Set(string key, string value)
        {
            Vars[key] = value;
        }

        public void Set(string key, int value)
        {
            Vars[key] = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>What kind of answer a decision needs.</summary>
    public enum DecisionKind
    {
        /// <summary>Pick one option (a "may", an "either", a target...). Answered with <see cref="ChooseOptionCommand"/>.</summary>
        ChooseOption,

        /// <summary>Tied candidates; the current player picks one (R-5.30, R-21.13). Answered with <see cref="ResolveTieCommand"/>.</summary>
        ResolveTie,

        /// <summary>Simultaneous abilities; the current player orders them (R-21.10). Answered with <see cref="ChooseOrderCommand"/>.</summary>
        ChooseOrder,
    }

    public sealed class DecisionOption
    {
        public DecisionOption()
        {
        }

        public DecisionOption(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; set; } = "";

        /// <summary>Localization key or plain id for the interface.</summary>
        public string Label { get; set; } = "";
    }

    /// <summary>A question the engine is waiting on. While one is pending, no other game command is accepted.</summary>
    public sealed class PendingDecision
    {
        public int Id { get; set; }

        public DecisionKind Kind { get; set; }

        /// <summary>The player who must answer: the current player unless a card says otherwise (R-15.35, R-21.13).</summary>
        public string PlayerId { get; set; } = "";

        /// <summary>Localization key of the question.</summary>
        public string Prompt { get; set; } = "";

        public List<DecisionOption> Options { get; set; } = new List<DecisionOption>();

        /// <summary>Frame variable that receives the answer (an option id, or ids joined by ',' for an order).</summary>
        public string ResultVar { get; set; } = "";
    }

    /// <summary>What a handler wants after one step.</summary>
    public sealed class StepResult
    {
        private StepResult(StepKind kind, Frame? call, PendingDecision? decision)
        {
            Kind = kind;
            CallFrame = call;
            Decision = decision;
        }

        internal enum StepKind
        {
            Continue,
            Done,
            Call,
            Await,
        }

        internal StepKind Kind { get; }

        internal Frame? CallFrame { get; }

        internal PendingDecision? Decision { get; }

        /// <summary>Run this frame again (its Step has been advanced).</summary>
        public static StepResult Continue()
        {
            return new StepResult(StepKind.Continue, null, null);
        }

        /// <summary>This frame is finished; the caller resumes.</summary>
        public static StepResult Done()
        {
            return new StepResult(StepKind.Done, null, null);
        }

        /// <summary>Run another frame first; this one resumes afterwards with the values it returned.</summary>
        public static StepResult Call(Frame frame)
        {
            return new StepResult(StepKind.Call, frame, null);
        }

        /// <summary>Pause until a player answers. Build the decision with <see cref="ResolutionContext"/>.</summary>
        public static StepResult Await(PendingDecision decision)
        {
            return new StepResult(StepKind.Await, null, decision);
        }
    }

    /// <summary>What a handler can see and do while it runs.</summary>
    public sealed class ResolutionContext
    {
        internal ResolutionContext(GameState state, List<GameEvent> events)
        {
            State = state;
            Events = events;
        }

        public GameState State { get; }

        public List<GameEvent> Events { get; }

        /// <summary>The frame that called the running one, if any; used to return values.</summary>
        internal Frame? Caller { get; set; }

        /// <summary>Stores a value in the calling frame as "ret.&lt;key&gt;".</summary>
        public void Return(string key, string value)
        {
            Caller?.Set("ret." + key, value);
        }

        /// <summary>A decision for a player, with options; the answer lands in <paramref name="resultVar"/>.</summary>
        public PendingDecision Ask(DecisionKind kind, string playerId, string prompt, IEnumerable<DecisionOption> options,
            string resultVar)
        {
            var d = new PendingDecision
            {
                Id = State.NextDecisionId,
                Kind = kind,
                PlayerId = playerId,
                Prompt = prompt,
                ResultVar = resultVar,
            };
            d.Options.AddRange(options);
            if (d.Options.Count == 0)
            {
                throw new InvalidOperationException("A decision needs at least one option.");
            }
            return d;
        }

        /// <summary>R-5.30, R-21.13: the current player picks one of several tied candidates.</summary>
        public PendingDecision AskTie(string prompt, IEnumerable<DecisionOption> candidates, string resultVar)
        {
            return Ask(DecisionKind.ResolveTie, State.CurrentPlayerId, prompt, candidates, resultVar);
        }

        /// <summary>R-21.10: the current player orders simultaneous abilities.</summary>
        public PendingDecision AskOrder(string prompt, IEnumerable<DecisionOption> items, string resultVar)
        {
            return Ask(DecisionKind.ChooseOrder, State.CurrentPlayerId, prompt, items, resultVar);
        }
    }

    /// <summary>A resumable piece of rules logic. Step is called repeatedly; it reads and advances frame.Step.</summary>
    public interface IResolutionHandler
    {
        string Id { get; }

        StepResult Step(ResolutionContext context, Frame frame);
    }

    /// <summary>Known handlers by id. Rules register theirs; a frame naming an unknown handler is an error.</summary>
    public static class ResolutionHandlers
    {
        private static readonly Dictionary<string, IResolutionHandler> Handlers =
            new Dictionary<string, IResolutionHandler>(StringComparer.Ordinal);

        private static readonly object Lock = new object();

        /// <summary>Registers a handler. Registering the same instance type again under the same id is allowed.</summary>
        public static void Register(IResolutionHandler handler)
        {
            lock (Lock)
            {
                if (Handlers.TryGetValue(handler.Id, out var existing) && existing.GetType() != handler.GetType())
                {
                    throw new InvalidOperationException("Handler id '" + handler.Id + "' is already used.");
                }
                Handlers[handler.Id] = handler;
            }
        }

        public static IResolutionHandler Get(string id)
        {
            lock (Lock)
            {
                if (Handlers.TryGetValue(id, out var h))
                {
                    return h;
                }
            }
            throw new InvalidOperationException("Unknown resolution handler '" + id + "'.");
        }
    }

    /// <summary>Runs the resolution stack until it is empty or waiting for a decision.</summary>
    internal static class Resolver
    {
        /// <summary>Guards against a handler that never finishes.</summary>
        private const int MaxSteps = 100000;

        public static void Run(GameState state, List<GameEvent> events)
        {
            var context = new ResolutionContext(state, events);
            int steps = 0;
            while (state.Stack.Count > 0 && state.Pending == null)
            {
                if (++steps > MaxSteps)
                {
                    throw new InvalidOperationException("Resolution did not finish after " + MaxSteps + " steps.");
                }
                var frame = state.Stack[state.Stack.Count - 1];
                context.Caller = state.Stack.Count > 1 ? state.Stack[state.Stack.Count - 2] : null;
                var result = ResolutionHandlers.Get(frame.Handler).Step(context, frame);
                switch (result.Kind)
                {
                    case StepResult.StepKind.Continue:
                        break;
                    case StepResult.StepKind.Done:
                        state.Stack.RemoveAt(state.Stack.Count - 1);
                        break;
                    case StepResult.StepKind.Call:
                        state.Stack.Add(result.CallFrame!);
                        break;
                    case StepResult.StepKind.Await:
                        var decision = result.Decision!;
                        state.NextDecisionId++;
                        state.Pending = decision;
                        events.Add(new DecisionRequestedEvent(decision));
                        break;
                }
            }
        }
    }
}
