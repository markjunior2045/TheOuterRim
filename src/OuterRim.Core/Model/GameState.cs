#nullable enable
using System.Collections.Generic;
using OuterRim.Core.Engine;
using OuterRim.Core.Random;

namespace OuterRim.Core.Model
{
    /// <summary>
    /// The whole serializable state of a game. Everything the rules need lives here, so a save
    /// is this object and nothing else (DECISIONS §7). It grows task by task (PLAN Phase 1).
    /// </summary>
    public sealed class GameState
    {
        /// <summary>The seed the game was created with; kept for reference and bug reports.</summary>
        public ulong Seed { get; set; }

        /// <summary>The live random generator. Its position is part of the state.</summary>
        public GameRandom Random { get; set; } = new GameRandom(0UL);

        /// <summary>Content packs and versions this game uses (DECISIONS §7).</summary>
        public List<PackRef> Packs { get; set; } = new List<PackRef>();

        public GamePhase Phase { get; set; } = GamePhase.Setup;

        /// <summary>Players in seating order (clockwise, R-4.1). Turn order starts at <see cref="FirstPlayerId"/>.</summary>
        public List<PlayerState> Players { get; set; } = new List<PlayerState>();

        public string FirstPlayerId { get; set; } = "";

        /// <summary>Round number, starting at 1 when the first player takes their first turn.</summary>
        public int Round { get; set; }

        public string CurrentPlayerId { get; set; } = "";

        public TurnStep Step { get; set; } = TurnStep.Planning;

        /// <summary>Map pieces left to right (R-3.2 standard, or a random order with R-3.3). The map is rebuilt from it.</summary>
        public List<string> MapPieces { get; set; } = new List<string>();

        /// <summary>Patrols on the map: always one per faction (R-11.9).</summary>
        public List<PatrolState> Patrols { get; set; } = new List<PatrolState>();

        /// <summary>Ship sheets not held by any player (R-3.16.d, R-16.29); a sheet id per copy.</summary>
        public List<string> ShipSupply { get; set; } = new List<string>();

        /// <summary>Facedown patrol tokens per faction, top first (R-3.7).</summary>
        public List<PatrolStack> PatrolStacks { get; set; } = new List<PatrolStack>();

        /// <summary>Fame needed to win (R-3.1, R-18.9).</summary>
        public int FameRequirement { get; set; } = 10;

        /// <summary>Damage tokens placed on planets by Jyn Erso's ability; markers only (R-25.14).</summary>
        public List<PlanetMarker> PlanetMarkers { get; set; } = new List<PlanetMarker>();

        /// <summary>Abilities used this turn, as "step:card#path" (R-4.11: each "Action" ability once per action step).</summary>
        public List<string> UsedAbilities { get; set; } = new List<string>();

        /// <summary>The winner once the game has ended (R-18.8).</summary>
        public string WinnerId { get; set; } = "";

        /// <summary>R-8.8.4: the current player was defeated; their turn ends when the current action or step finishes.</summary>
        public bool TurnEndsAfterStep { get; set; }

        /// <summary>Rules logic in progress, innermost last. Empty between commands unless a decision is pending.</summary>
        public List<Frame> Stack { get; set; } = new List<Frame>();

        /// <summary>The question the engine is waiting on, if any (RULES §26.2).</summary>
        public PendingDecision? Pending { get; set; }

        /// <summary>Id for the next decision; ids never repeat within a game.</summary>
        public int NextDecisionId { get; set; } = 1;

        public PlayerState? FindPlayer(string id)
        {
            foreach (var p in Players)
            {
                if (p.Id == id)
                {
                    return p;
                }
            }
            return null;
        }

        public List<PatrolState> PatrolsIn(string spaceId)
        {
            var result = new List<PatrolState>();
            foreach (var p in Patrols)
            {
                if (p.SpaceId == spaceId)
                {
                    result.Add(p);
                }
            }
            return result;
        }

        /// <summary>Index of a player in seating order, or -1.</summary>
        public int SeatOf(string id)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
        }
    }

    /// <summary>A patrol token on the map (R-11).</summary>
    public sealed class PatrolState
    {
        public Faction Faction { get; set; }

        /// <summary>1 to 4 (R-11.3).</summary>
        public int Level { get; set; } = 1;

        public string SpaceId { get; set; } = "";
    }

    /// <summary>A damage token placed on a planet as a marker (R-25.14).</summary>
    public sealed class PlanetMarker
    {
        public string PlayerId { get; set; } = "";

        public string SpaceId { get; set; } = "";
    }

    /// <summary>A faction's stack of facedown patrol tokens, by level, top first (R-3.7).</summary>
    public sealed class PatrolStack
    {
        public Faction Faction { get; set; }

        public List<int> Levels { get; set; } = new List<int>();
    }

    /// <summary>A content pack used by a game: id and version (DECISIONS §7).</summary>
    public sealed class PackRef
    {
        public PackRef()
        {
        }

        public PackRef(string id, string version)
        {
            Id = id;
            Version = version;
        }

        public string Id { get; set; } = "";

        public string Version { get; set; } = "";
    }
}
