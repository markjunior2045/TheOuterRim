#nullable enable
using System.Collections.Generic;
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

        /// <summary>Fame needed to win (R-3.1, R-18.9).</summary>
        public int FameRequirement { get; set; } = 10;

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
