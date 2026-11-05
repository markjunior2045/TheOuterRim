#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Dice;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Random;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Who sits at the table, before the game starts.</summary>
    public sealed class PlayerSetup
    {
        public PlayerSetup(string id, string name, string color, string startSpaceId, string starterShipSideId,
            string characterId = "")
        {
            Id = id;
            Name = name;
            Color = color;
            StartSpaceId = startSpaceId;
            StarterShipSideId = starterShipSideId;
            CharacterId = characterId;
        }

        /// <summary>The chosen character (R-3.11); its setup line is applied once character setup is built.</summary>
        public string CharacterId { get; }

        public string Id { get; }

        public string Name { get; }

        public string Color { get; }

        /// <summary>
        /// Leave empty for the normal setup: the starting planet comes from the character's starting
        /// databank card (R-3.15). Set it only to place a player directly (tests, scenarios); that skips
        /// the character's setup line (starting card and reputation).
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

        /// <summary>R-2.1: the base game has four copies of the starter sheet.</summary>
        public const int StarterSheets = 4;

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
                if (p.StartSpaceId.Length == 0 && p.CharacterId.Length == 0)
                {
                    throw new ArgumentException("R-3.11: " + p.Id + " needs a character (or a start space for a scenario).", nameof(setup));
                }
                if (p.StartSpaceId.Length > 0 && (!content.Map.Contains(p.StartSpaceId) || !content.Map[p.StartSpaceId].IsPlanet))
                {
                    throw new ArgumentException("R-3.15.b: " + p.Id + " must start on a planet, not '" + p.StartSpaceId + "'.", nameof(setup));
                }
                if (p.CharacterId.Length > 0 && !content.HasCharacter(p.CharacterId))
                {
                    throw new ArgumentException("Unknown character '" + p.CharacterId + "'.", nameof(setup));
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
                    CharacterId = p.CharacterId,
                    SpaceId = p.StartSpaceId,
                    ShipSheetId = side.SheetId,
                    ShipSideId = side.Id,
                });
            }
            Patrols.Setup(content, state);
            // R-3.16.d: unused ship sheets form the supply. There are four starter sheets (R-2.1).
            foreach (var sheet in content.ShipSheetIds())
            {
                bool starter = content.ShipSide(sheet).Role == "starter";
                int copies = starter ? StarterSheets - setup.Players.Count : 1;
                for (int i = 0; i < copies; i++)
                {
                    state.ShipSupply.Add(sheet);
                }
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

            // R-3.15: character setup, for players placed by their starting card.
            var choices = new List<string>();
            for (int i = 0; i < state.Players.Count; i++)
            {
                var player = state.Players[(firstSeat + i) % state.Players.Count];
                var ps = Find(setup, player.Id);
                if (ps.StartSpaceId.Length == 0 && CharacterSetup(content, state, player, log))
                {
                    choices.Add(player.Id);
                }
            }

            state.Phase = GamePhase.Playing;
            state.Round = 1;
            state.CurrentPlayerId = first;
            state.Step = TurnStep.Planning;
            log.Add(new TurnStartedEvent(first, 1));
            log.Add(new StepStartedEvent(first, TurnStep.Planning));
            // Starting reputation "with the faction of your choice" (Lando) is asked before the first turn.
            for (int i = choices.Count - 1; i >= 0; i--)
            {
                var frame = new Frame(TurnHandlers.ChooseStartingReputation);
                frame.Set("player", choices[i]);
                state.Stack.Add(frame);
            }
            if (state.Stack.Count > 0)
            {
                Resolver.Run(content, state, log);
            }
            events = log;
            return state;
        }

        private static PlayerSetup Find(GameSetup setup, string id)
        {
            foreach (var p in setup.Players)
            {
                if (p.Id == id) return p;
            }
            throw new KeyNotFoundException(id);
        }

        /// <summary>
        /// R-3.15: take a random card of the character's starting databank number (R-13.3; each card goes to
        /// one player), put its asset in a slot (cargo, or job or bounty), start on its planet, and apply the
        /// starting reputation (R-10.7). Returns true when the reputation faction is the player's choice.
        /// </summary>
        private static bool CharacterSetup(GameContent content, GameState state, PlayerState player, List<GameEvent> log)
        {
            var character = content.Card(player.CharacterId);
            var setupLine = (Newtonsoft.Json.Linq.JObject)character["setup"]!;
            int number = (int)setupLine["databank"]!;
            var candidates = content.DatabankCards(number);
            foreach (var p in state.Players)
            {
                foreach (var a in p.Assets) candidates.Remove(a.CardId);
            }
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException("No card #" + number + " left in the databank.");
            }
            var cardId = candidates[state.Random.NextInt(candidates.Count)];
            var card = content.Card(cardId);
            player.Assets.Add(new AssetState { CardId = cardId, Kind = (string)card["asset_kind"]!, Cost = 0 });
            player.SpaceId = (string)card["starting_planet"]!;
            log.Add(new StartingCardDrawnEvent(player.Id, cardId, player.SpaceId));
            bool choice = false;
            foreach (Newtonsoft.Json.Linq.JObject rep in (Newtonsoft.Json.Linq.JArray)setupLine["reputation"]!)
            {
                var faction = (string)rep["faction"]!;
                var value = (string)rep["state"]! == "pos" ? Reputation.Positive : (string)rep["state"]! == "neg" ? Reputation.Negative : Reputation.Neutral;
                if (faction == "choice")
                {
                    choice = true;
                    continue;
                }
                PlayerRules.SetReputation(player, Patrols.Parse(faction), value, log);
            }
            return choice;
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
            var result = ExecuteCore(content, state, command);
            if (result.Accepted)
            {
                var events = new List<GameEvent>(result.Events);
                AfterCommand(content, state, events);
                return CommandResult.Ok(events);
            }
            return result;
        }

        /// <summary>
        /// Checks that run once a command has fully resolved: victory (R-18.8), and the end of the turn of a
        /// current player defeated during it, once the action or step in progress is over (R-8.8.4).
        /// </summary>
        private static void AfterCommand(GameContent content, GameState state, List<GameEvent> events)
        {
            if (Victory.Check(state, events))
            {
                return;
            }
            // "Have X" goals (Han Solo, Bossk, IG-88, Jyn Erso) complete as soon as their state holds.
            int guard = 0;
            while (state.Stack.Count == 0 && state.Pending == null && guard++ < 16
                && Goals.FindStateGoal(content, state, out var owner, out var goal))
            {
                var frame = new Frame(EffectHandlers.Ability);
                frame.Set("owner", owner!.Id);
                frame.Set("card", goal!.CardId);
                frame.Set("path", goal.Path);
                state.Stack.Add(frame);
                Resolver.Run(content, state, events);
                if (Victory.Check(state, events))
                {
                    return;
                }
            }
            if (state.TurnEndsAfterStep && state.Stack.Count == 0 && state.Pending == null)
            {
                EndTurn(state, events);
            }
        }

        private static CommandResult ExecuteCore(GameContent content, GameState state, Command command)
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
                    return RecoverDamage(content, state, player);
                case MoveCommand move:
                    return Move(content, state, player, move.Path);
                case EndActionStepCommand _:
                    return EndActionStep(state, player);
                case EncounterCommand encounter:
                    return Encounter(content, state, player, encounter);
                case UseAbilityCommand use:
                    return UseAbility(content, state, player, use);
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
            if (state.Phase != GamePhase.Playing)
            {
                return CommandResult.Rejected("The game is not in progress.");
            }
            if (state.Pending != null || state.Stack.Count > 0)
            {
                return CommandResult.Rejected("R-21.12: another resolution is in progress.");
            }
            ResolutionHandlers.Get(frame.Handler);
            var events = new List<GameEvent>();
            state.Stack.Add(frame);
            Resolver.Run(content, state, events);
            AfterCommand(content, state, events);
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
            var events = new List<GameEvent>();
            PlayerRules.GainCredits(player, PlanningCredits, events);
            StartStep(state, TurnStep.Action, events);
            return CommandResult.Ok(events);
        }

        /// <summary>R-4.6 planning option "recover damage": remove all damage from character and ship.</summary>
        private static CommandResult RecoverDamage(GameContent content, GameState state, PlayerState player)
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
            string? recovered = EffectHandlers.Recovered(player.CharacterDamage, player.ShipDamage);
            player.CharacterDamage = 0;
            player.ShipDamage = 0;
            player.Defeated = false;
            StartStep(state, TurnStep.Action, events);
            if (recovered != null)
            {
                // "When you recover at least 1 damage from this ship" (YV-666 ship goal).
                state.Stack.Add(TriggersHandler.For("damage_recovered", player.Id, new Dictionary<string, string> { ["recovered"] = recovered }));
                Resolver.Run(content, state, events);
            }
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
            int hyperdrive = Hyperdrive(content, state, player);
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
                if (!last && StopsMovement(state, player, next) && !MoveHandler.MayPassPatrols(null, content, player))
                {
                    return CommandResult.Rejected("R-5.20: entering a patrol's space without positive reputation with it ends movement.");
                }
                at = next;
            }
            // Walk the path one space at a time: abilities may act on entering a space (Han Solo, Tractor Beam).
            var frame = new Frame(MoveHandler.HandlerId);
            frame.Set("player", player.Id);
            frame.Set("path", string.Join(",", path));
            frame.Set("from", player.SpaceId);
            return StartResolution(content, state, frame);
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

        /// <summary>The ship's hyperdrive (R-5.17) plus passive "Gain +X {HYPERDRIVE}" bonuses, which stack (R-5.19).</summary>
        public static int Hyperdrive(GameContent content, GameState state, PlayerState player)
        {
            return content.ShipSide(player.ShipSideId).Hyperdrive + Modifiers.StatBonus(content, state, player, "hyperdrive");
        }

        /// <summary>
        /// Starts a ship purchase after checking it (R-16.27 to R-16.30, R-16.38): the sheet must be in the
        /// supply, the barter allowed and the price affordable. Called by the market (PLAN T2.5) and effects.
        /// </summary>
        public static CommandResult BuyShip(GameContent content, GameState state, string playerId, string sheet,
            IReadOnlyList<string> barterIds)
        {
            var player = state.FindPlayer(playerId);
            if (player == null)
            {
                return CommandResult.Rejected("Unknown player " + playerId + ".");
            }
            var error = BuyShipHandler.Validate(content, state, player, sheet, barterIds);
            if (error != null)
            {
                return CommandResult.Rejected(error);
            }
            var frame = new Frame(Assets.BuyShipHandler);
            frame.Set("player", playerId);
            frame.Set("sheet", sheet);
            frame.Set("barter", string.Join(",", barterIds));
            return StartResolution(content, state, frame);
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
                    if (!Modifiers.HasRule(content, state, player, "not_forced_to_encounter_negative_patrols"))
                    {
                        hostile.Add(p.Faction); // R-4.14, unless Valorous
                    }
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

        /// <summary>
        /// Uses a "Planning:", "Action:" or "Encounter:" ability of the player's character, ship or assets
        /// (EFFECTS.md §3.2). Planning: instead of the planning options, then the action step (R-4.7, R-4.8).
        /// Action: each ability once per action step (R-4.11). Encounter: instead of encountering, not when
        /// a patrol must be encountered, and the turn ends after it (R-4.16, R-4.17). The ability's
        /// condition must hold and its credit costs must be affordable (R-9.8, R-21.4).
        /// </summary>
        private static CommandResult UseAbility(GameContent content, GameState state, PlayerState player, UseAbilityCommand command)
        {
            var ability = AbilitySources.Find(content, player, command.CardId, command.Path);
            string? step = ability == null ? null
                : ability.TimingType == "step" ? (string)ability.Ability["timing"]!["step"]!
                : Goals.StepOf(ability); // "Ship Goal – Action:" and "Ship Goal – Encounter:" (R-18.3)
            if (ability == null || step == null)
            {
                return CommandResult.Rejected("You have no step ability at " + command.CardId + " " + command.Path + ".");
            }
            if (step != StepId(state.Step))
            {
                return CommandResult.Rejected("R-4.2: this is a " + step + " ability.");
            }
            string key = step + ":" + ability.Key;
            if (step == "planning" && player.Defeated)
            {
                return CommandResult.Rejected("R-4.8: a defeated player must recover damage.");
            }
            if (step == "action" && state.UsedAbilities.Contains(key))
            {
                return CommandResult.Rejected("R-4.11: each Action ability once per action step.");
            }
            if (step == "encounter" && MustEncounterPatrol(state, player)
                && !Modifiers.HasRule(content, state, player, "not_forced_to_encounter_negative_patrols"))
            {
                return CommandResult.Rejected("R-4.17: a player who must encounter a patrol cannot use an Encounter ability.");
            }
            if (!Conditions.Eval(content, state, player, ability.Ability["condition"]))
            {
                return CommandResult.Rejected("The ability's condition does not hold.");
            }
            int spend = 0;
            if (ability.Ability["cost"] is Newtonsoft.Json.Linq.JArray cost)
            {
                foreach (Newtonsoft.Json.Linq.JObject c in cost)
                {
                    if ((string?)c["op"] == "credits.spend")
                    {
                        spend += Selectors.Amount(content, player, c["amount"]!);
                    }
                }
            }
            if (player.Credits < spend)
            {
                return CommandResult.Rejected("R-9.8: the ability costs " + spend + " credits.");
            }
            state.UsedAbilities.Add(key);
            var frame = new Frame(TurnHandlers.AbilityStep);
            frame.Set("step", step);
            frame.Set("owner", player.Id);
            frame.Set("card", ability.CardId);
            frame.Set("path", ability.Path);
            var result = StartResolution(content, state, frame);
            if (result.Accepted)
            {
                var events = new List<GameEvent> { new AbilityUsedEvent(player.Id, ability.CardId, ability.Path, step) };
                events.AddRange(result.Events);
                return CommandResult.Ok(events);
            }
            return result;
        }

        private static string StepId(TurnStep step)
        {
            return step == TurnStep.Planning ? "planning" : step == TurnStep.Action ? "action" : "encounter";
        }

        private static bool MustEncounterPatrol(GameState state, PlayerState player)
        {
            foreach (var p in state.PatrolsIn(player.SpaceId))
            {
                if (player.Reputation.Get(p.Faction) == Reputation.Negative)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>R-4.4: the turn ends and the player to the left begins theirs; a new round starts with the first player.</summary>
        internal static void EndTurn(GameState state, List<GameEvent> events)
        {
            state.TurnEndsAfterStep = false;
            state.UsedAbilities.Clear();
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
