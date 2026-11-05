#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Dice;
using OuterRim.Core.Model;
using OuterRim.Core.Random;

namespace OuterRim.Core.Engine
{
    /// <summary>Who sits at the table, before the game starts.</summary>
    public sealed class PlayerSetup
    {
        public PlayerSetup(string id, string name, string color, string startSpaceId, string starterShipSideId)
        {
            Id = id;
            Name = name;
            Color = color;
            StartSpaceId = startSpaceId;
            StarterShipSideId = starterShipSideId;
        }

        public string Id { get; }

        public string Name { get; }

        public string Color { get; }

        /// <summary>
        /// Starting planet (R-3.15.b). Comes from the character's starting databank card once character
        /// setup is built; given directly until then.
        /// </summary>
        public string StartSpaceId { get; }

        /// <summary>The chosen side of the starter sheet: G9 Rigger or G-1A Starfighter (R-3.11.e, R-3.12).</summary>
        public string StarterShipSideId { get; }
    }

    /// <summary>Parameters of a new game.</summary>
    public sealed class GameSetup
    {
        public GameSetup(ulong seed, IReadOnlyList<PlayerSetup> players, IReadOnlyList<PackRef> packs)
        {
            Seed = seed;
            Players = players;
            Packs = packs;
        }

        public ulong Seed { get; }

        /// <summary>Players in seating order, clockwise.</summary>
        public IReadOnlyList<PlayerSetup> Players { get; }

        public IReadOnlyList<PackRef> Packs { get; }

        public int FameRequirement { get; set; } = 10;
    }

    /// <summary>
    /// Entry point of the rules: creates games and applies commands. Stateless: the state is passed in
    /// and changed in place only when a command is accepted. Static content (map, card data) comes in
    /// a <see cref="GameContent"/>.
    /// </summary>
    public static class GameEngine
    {
        public const int MinPlayers = 1;
        public const int MaxPlayers = 4;

        /// <summary>R-3.11.g: starting credits by turn order.</summary>
        private static readonly int[] StartingCredits = { 4000, 6000, 8000, 10000 };

        /// <summary>R-4.6: credits gained by the planning option.</summary>
        public const int PlanningCredits = 2000;

        /// <summary>R-3.11: dice each player rolls to decide the first player.</summary>
        public const int FirstPlayerDice = 6;

        static GameEngine()
        {
            TurnHandlers.Register();
        }

        /// <summary>
        /// Creates a game: first player by dice (R-3.11), neutral reputation (R-3.11.d), fame 0 (R-3.11.c),
        /// credits by turn order (R-3.11.g), starter ship side (R-3.11.e), starting space (R-3.15.b),
        /// level-1 patrols on their spawn spaces (R-3.5).
        /// </summary>
        public static GameState NewGame(GameContent content, GameSetup setup, out IReadOnlyList<GameEvent> events)
        {
            if (setup.Players.Count < MinPlayers || setup.Players.Count > MaxPlayers)
            {
                throw new ArgumentException("A game has 1 to 4 players (DECISIONS §1).", nameof(setup));
            }
            var seen = new HashSet<string>();
            foreach (var p in setup.Players)
            {
                if (!seen.Add(p.Id))
                {
                    throw new ArgumentException("Duplicate player id " + p.Id, nameof(setup));
                }
                if (!content.Map.Contains(p.StartSpaceId) || !content.Map[p.StartSpaceId].IsPlanet)
                {
                    throw new ArgumentException("R-3.15.b: " + p.Id + " must start on a planet, not '" + p.StartSpaceId + "'.", nameof(setup));
                }
                if (!content.HasShipSide(p.StarterShipSideId) || content.ShipSide(p.StarterShipSideId).Role != "starter")
                {
                    throw new ArgumentException("R-3.11.e: " + p.Id + " must start with a starter ship side.", nameof(setup));
                }
            }

            var state = new GameState
            {
                Seed = setup.Seed,
                Random = new GameRandom(setup.Seed),
                Packs = new List<PackRef>(setup.Packs),
                FameRequirement = setup.FameRequirement,
                MapPieces = new List<string>(content.Map.Pieces),
            };
            foreach (var p in setup.Players)
            {
                var side = content.ShipSide(p.StarterShipSideId);
                state.Players.Add(new PlayerState
                {
                    Id = p.Id,
                    Name = p.Name,
                    Color = p.Color,
                    SpaceId = p.StartSpaceId,
                    ShipSheetId = side.SheetId,
                    ShipSideId = side.Id,
                });
            }
            foreach (Faction f in new[] { Faction.Rebel, Faction.Syndicate, Faction.Imperial, Faction.Hutt })
            {
                state.Patrols.Add(new PatrolState { Faction = f, Level = 1, SpaceId = content.Map.SpawnSpace(f) });
            }

            var log = new List<GameEvent>();
            string first = RollForFirstPlayer(state, log);
            state.FirstPlayerId = first;
            log.Add(new FirstPlayerChosenEvent(first));

            int firstSeat = state.SeatOf(first);
            for (int i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[(firstSeat + i) % state.Players.Count];
                player.Credits = StartingCredits[i];
                log.Add(new CreditsGainedEvent(player.Id, StartingCredits[i]));
            }

            state.Phase = GamePhase.Playing;
            state.Round = 1;
            state.CurrentPlayerId = first;
            state.Step = TurnStep.Planning;
            log.Add(new TurnStartedEvent(first, 1));
            log.Add(new StepStartedEvent(first, TurnStep.Planning));
            events = log;
            return state;
        }

        /// <summary>R-3.11: everyone rolls six dice; most hits plus crits goes first; tied players reroll.</summary>
        private static string RollForFirstPlayer(GameState state, List<GameEvent> log)
        {
            var contenders = new List<string>();
            foreach (var p in state.Players)
            {
                contenders.Add(p.Id);
            }
            while (contenders.Count > 1)
            {
                int best = -1;
                var leaders = new List<string>();
                foreach (var id in contenders)
                {
                    var faces = DiceRoller.Roll(state.Random, FirstPlayerDice);
                    int score = DiceRoller.Count(faces, DieFace.Hit) + DiceRoller.Count(faces, DieFace.Crit);
                    log.Add(new FirstPlayerRolledEvent(id, faces, score));
                    if (score > best)
                    {
                        best = score;
                        leaders.Clear();
                    }
                    if (score == best)
                    {
                        leaders.Add(id);
                    }
                }
                contenders = leaders;
            }
            return contenders[0];
        }

        /// <summary>Validates and applies a command. A rejected command changes nothing.</summary>
        public static CommandResult Execute(GameContent content, GameState state, Command command)
        {
            if (state.Phase != GamePhase.Playing)
            {
                return CommandResult.Rejected("The game is not in progress.");
            }
            var player = state.FindPlayer(command.PlayerId);
            if (player == null)
            {
                return CommandResult.Rejected("Unknown player " + command.PlayerId + ".");
            }
            if (state.Pending != null)
            {
                return command is AnswerCommand answer
                    ? Answer(content, state, answer)
                    : CommandResult.Rejected("Waiting for decision " + state.Pending.Id + " from " + state.Pending.PlayerId + ".");
            }
            if (command is AnswerCommand)
            {
                return CommandResult.Rejected("No decision is pending.");
            }
            if (player.Id != state.CurrentPlayerId)
            {
                return CommandResult.Rejected("R-4.5: only the current player acts during their turn.");
            }

            switch (command)
            {
                case GainCreditsCommand _:
                    return GainCredits(state, player);
                case RecoverDamageCommand _:
                    return RecoverDamage(state, player);
                case MoveCommand move:
                    return Move(content, state, player, move.Path);
                case EndActionStepCommand _:
                    return EndActionStep(state, player);
                case EncounterCommand encounter:
                    return Encounter(content, state, player, encounter);
                default:
                    return CommandResult.Rejected("Command not supported yet: " + command.GetType().Name + ".");
            }
        }

        /// <summary>
        /// Starts rules logic as a frame on the resolution stack and runs it until it finishes or
        /// pauses for a decision. Card effects, combats and jobs enter the engine through here.
        /// </summary>
        public static CommandResult StartResolution(GameContent content, GameState state, Frame frame)
        {
            if (state.Pending != null || state.Stack.Count > 0)
            {
                return CommandResult.Rejected("R-21.12: another resolution is in progress.");
            }
            ResolutionHandlers.Get(frame.Handler);
            var events = new List<GameEvent>();
            state.Stack.Add(frame);
            Resolver.Run(content, state, events);
            return CommandResult.Ok(events);
        }

        /// <summary>Checks an answer against the pending decision, stores it in the waiting frame and resumes.</summary>
        private static CommandResult Answer(GameContent content, GameState state, AnswerCommand command)
        {
            var d = state.Pending!;
            if (command.DecisionId != d.Id)
            {
                return CommandResult.Rejected("Decision " + command.DecisionId + " is not the pending one (" + d.Id + ").");
            }
            if (command.PlayerId != d.PlayerId)
            {
                return CommandResult.Rejected("Decision " + d.Id + " is for " + d.PlayerId + " (R-15.35, R-21.13).");
            }
            string answer;
            switch (command)
            {
                case ChooseOptionCommand c when d.Kind == DecisionKind.ChooseOption:
                    answer = c.OptionId;
                    break;
                case ResolveTieCommand t when d.Kind == DecisionKind.ResolveTie:
                    answer = t.OptionId;
                    break;
                case ChooseOrderCommand o when d.Kind == DecisionKind.ChooseOrder:
                    if (!IsPermutation(o.Order, d.Options))
                    {
                        return CommandResult.Rejected("R-21.10: the order must list every option exactly once.");
                    }
                    answer = string.Join(",", o.Order);
                    break;
                default:
                    return CommandResult.Rejected("Decision " + d.Id + " needs a " + d.Kind + " answer.");
            }
            if (d.Kind != DecisionKind.ChooseOrder && !HasOption(d, answer))
            {
                return CommandResult.Rejected("'" + answer + "' is not an option of decision " + d.Id + ".");
            }
            state.Stack[state.Stack.Count - 1].Set(d.ResultVar, answer);
            state.Pending = null;
            var events = new List<GameEvent> { new DecisionAnsweredEvent(d.Id, command.PlayerId, answer) };
            Resolver.Run(content, state, events);
            return CommandResult.Ok(events);
        }

        private static bool HasOption(PendingDecision d, string id)
        {
            foreach (var o in d.Options)
            {
                if (o.Id == id)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPermutation(IReadOnlyList<string> order, List<DecisionOption> options)
        {
            if (order.Count != options.Count)
            {
                return false;
            }
            var remaining = new HashSet<string>();
            foreach (var o in options)
            {
                remaining.Add(o.Id);
            }
            foreach (var id in order)
            {
                if (!remaining.Remove(id))
                {
                    return false;
                }
            }
            return true;
        }

        // ---- Planning step (R-4.6 to R-4.8) ------------------------------------

        private static CommandResult CheckPlanningOption(GameState state, PlayerState player, string option)
        {
            if (state.Step != TurnStep.Planning)
            {
                return CommandResult.Rejected("R-4.6: " + option + " is a planning step option, chosen once per turn.");
            }
            return CommandResult.Ok(new GameEvent[0]);
        }

        /// <summary>R-4.6 planning option "gain 2,000 credits"; not allowed while defeated (R-4.8).</summary>
        private static CommandResult GainCredits(GameState state, PlayerState player)
        {
            var check = CheckPlanningOption(state, player, "gaining credits");
            if (!check.Accepted)
            {
                return check;
            }
            if (player.Defeated)
            {
                return CommandResult.Rejected("R-4.8: a defeated player must recover damage.");
            }
            player.Credits += PlanningCredits;
            var events = new List<GameEvent> { new CreditsGainedEvent(player.Id, PlanningCredits) };
            StartStep(state, TurnStep.Action, events);
            return CommandResult.Ok(events);
        }

        /// <summary>R-4.6 planning option "recover damage": remove all damage from character and ship.</summary>
        private static CommandResult RecoverDamage(GameState state, PlayerState player)
        {
            var check = CheckPlanningOption(state, player, "recovering damage");
            if (!check.Accepted)
            {
                return check;
            }
            var events = new List<GameEvent>
            {
                new DamageRecoveredEvent(player.Id, player.CharacterDamage, player.ShipDamage),
            };
            player.CharacterDamage = 0;
            player.ShipDamage = 0;
            player.Defeated = false;
            StartStep(state, TurnStep.Action, events);
            return CommandResult.Ok(events);
        }

        /// <summary>
        /// R-4.6 planning option "move": a path of adjacent spaces, up to the ship's hyperdrive (R-5.17,
        /// R-5.18). Entering the Maelstrom (R-5.11) or a patrol's space without positive reputation with
        /// it (R-5.20) ends movement, so such a space must be the last of the path. Other players and
        /// friendly patrols do not stop movement (R-5.21); only entering counts (R-5.22).
        /// </summary>
        private static CommandResult Move(GameContent content, GameState state, PlayerState player, IReadOnlyList<string> path)
        {
            var check = CheckPlanningOption(state, player, "moving");
            if (!check.Accepted)
            {
                return check;
            }
            if (player.Defeated)
            {
                return CommandResult.Rejected("R-4.8: a defeated player must recover damage.");
            }
            int hyperdrive = Hyperdrive(content, player);
            if (path.Count > hyperdrive)
            {
                return CommandResult.Rejected("R-5.17: the path has " + path.Count + " spaces; the ship's hyperdrive is " + hyperdrive + ".");
            }
            string at = player.SpaceId;
            for (int i = 0; i < path.Count; i++)
            {
                string next = path[i];
                if (!content.Map.Contains(next) || !content.Map.AreAdjacent(at, next))
                {
                    return CommandResult.Rejected("R-5.18: " + next + " is not adjacent to " + at + ".");
                }
                bool last = i == path.Count - 1;
                if (!last && content.Map[next].Type == Map.SpaceType.Maelstrom)
                {
                    return CommandResult.Rejected("R-5.11: entering the Maelstrom ends movement.");
                }
                if (!last && StopsMovement(state, player, next))
                {
                    return CommandResult.Rejected("R-5.20: entering a patrol's space without positive reputation with it ends movement.");
                }
                at = next;
            }
            var from = player.SpaceId;
            player.SpaceId = at;
            var events = new List<GameEvent> { new PlayerMovedEvent(player.Id, from, new List<string>(path), false) };
            StartStep(state, TurnStep.Action, events);
            return CommandResult.Ok(events);
        }

        /// <summary>R-5.20: a patrol whose faction the player is not positive with stops movement.</summary>
        public static bool StopsMovement(GameState state, PlayerState player, string spaceId)
        {
            foreach (var patrol in state.PatrolsIn(spaceId))
            {
                if (player.Reputation.Get(patrol.Faction) != Reputation.Positive)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>The ship's hyperdrive (R-5.17). Card modifiers ("Gain +X", R-5.19) are added with the effect engine.</summary>
        public static int Hyperdrive(GameContent content, PlayerState player)
        {
            return content.ShipSide(player.ShipSideId).Hyperdrive;
        }

        /// <summary>
        /// R-5.23: moving "directly" places the standee on the space without passing through others, so
        /// no patrol or Maelstrom stops it. Used by card effects.
        /// </summary>
        public static void MoveDirect(GameContent content, GameState state, PlayerState player, string spaceId, List<GameEvent> events)
        {
            if (!content.Map.Contains(spaceId))
            {
                throw new KeyNotFoundException("Unknown space '" + spaceId + "'.");
            }
            var from = player.SpaceId;
            player.SpaceId = spaceId;
            events.Add(new PlayerMovedEvent(player.Id, from, new List<string> { spaceId }, true));
        }

        // ---- Action and encounter steps ------------------------------------------

        /// <summary>R-4.9: actions are optional; the player ends the action step when done.</summary>
        private static CommandResult EndActionStep(GameState state, PlayerState player)
        {
            if (state.Step != TurnStep.Action)
            {
                return CommandResult.Rejected("R-4.2: the action step comes after the planning step.");
            }
            var events = new List<GameEvent>();
            StartStep(state, TurnStep.Encounter, events);
            return CommandResult.Ok(events);
        }

        /// <summary>
        /// R-4.12 to R-4.15, R-11.26 to R-11.28: exactly one encounter: the space, a patrol in it, or a
        /// contact in it; a patrol of a faction the player is negative with must be encountered.
        /// The turn ends when the encounter has resolved (R-4.4).
        /// </summary>
        private static CommandResult Encounter(GameContent content, GameState state, PlayerState player, EncounterCommand command)
        {
            if (state.Step != TurnStep.Encounter)
            {
                return CommandResult.Rejected("R-4.2: the encounter step comes after the action step.");
            }
            var here = state.PatrolsIn(player.SpaceId);
            var hostile = new List<Faction>();
            foreach (var p in here)
            {
                if (player.Reputation.Get(p.Faction) == Reputation.Negative)
                {
                    hostile.Add(p.Faction);
                }
            }
            string handler;
            switch (command.Kind)
            {
                case EncounterKind.Patrol:
                    if (!TryFaction(command.TargetId, out var faction) || !here.Exists(p => p.Faction == faction))
                    {
                        return CommandResult.Rejected("R-4.13: there is no " + command.TargetId + " patrol in your space.");
                    }
                    if (hostile.Count > 0 && !hostile.Contains(faction))
                    {
                        return CommandResult.Rejected("R-11.28: you must encounter a patrol you have negative reputation with.");
                    }
                    handler = TurnHandlers.EncounterPatrol;
                    break;
                case EncounterKind.Space:
                    if (hostile.Count > 0)
                    {
                        return CommandResult.Rejected("R-4.14: you must encounter the patrol you have negative reputation with.");
                    }
                    handler = TurnHandlers.EncounterSpace;
                    break;
                default:
                    if (hostile.Count > 0)
                    {
                        return CommandResult.Rejected("R-4.14: you must encounter the patrol you have negative reputation with.");
                    }
                    return CommandResult.Rejected("Contacts are not on the map yet (PLAN T2.6).");
            }
            var frame = new Frame(TurnHandlers.EncounterStep);
            frame.Set("handler", handler);
            frame.Set("target", command.TargetId ?? "");
            return StartResolution(content, state, frame);
        }

        private static bool TryFaction(string? id, out Faction faction)
        {
            switch (id)
            {
                case "rebel": faction = Faction.Rebel; return true;
                case "imperial": faction = Faction.Imperial; return true;
                case "hutt": faction = Faction.Hutt; return true;
                case "syndicate": faction = Faction.Syndicate; return true;
                default: faction = Faction.Rebel; return false;
            }
        }

        internal static void StartStep(GameState state, TurnStep step, List<GameEvent> events)
        {
            state.Step = step;
            events.Add(new StepStartedEvent(state.CurrentPlayerId, step));
        }

        /// <summary>R-4.4: the turn ends and the player to the left begins theirs; a new round starts with the first player.</summary>
        internal static void EndTurn(GameState state, List<GameEvent> events)
        {
            events.Add(new TurnEndedEvent(state.CurrentPlayerId));
            int next = (state.SeatOf(state.CurrentPlayerId) + 1) % state.Players.Count;
            state.CurrentPlayerId = state.Players[next].Id;
            if (state.CurrentPlayerId == state.FirstPlayerId)
            {
                state.Round++;
            }
            events.Add(new TurnStartedEvent(state.CurrentPlayerId, state.Round));
            StartStep(state, TurnStep.Planning, events);
        }
    }
}
