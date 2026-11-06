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

        /// <summary>R-5.14: the current player takes another full turn after this one.</summary>
        public bool AdditionalTurn { get; set; }

        /// <summary>R-5.14: the current turn is an additional turn; further ones gained in it are ignored.</summary>
        public bool InAdditionalTurn { get; set; }

        /// <summary>The six market decks, top card first; the top card is faceup (R-16.42).</summary>
        public List<MarketDeck> MarketDecks { get; set; } = new List<MarketDeck>();

        /// <summary>Every contact token and where it is (R-12).</summary>
        public List<ContactToken> Contacts { get; set; } = new List<ContactToken>();

        /// <summary>Tokens waiting to return to the map, nearest to their player (R-15.11, R-15.45).</summary>
        public List<TokenPlacement> TokensToPlace { get; set; } = new List<TokenPlacement>();

        /// <summary>Encounter decks, facedown, top card first (R-15.72, R-15.73).</summary>
        public List<MarketDeck> EncounterDecks { get; set; } = new List<MarketDeck>();

        /// <summary>Facedown patrol tokens per faction, top first (R-3.7).</summary>
        public List<PatrolStack> PatrolStacks { get; set; } = new List<PatrolStack>();

        /// <summary>Fame needed to win (R-3.1, R-18.9).</summary>
        public int FameRequirement { get; set; } = 10;

        /// <summary>Damage tokens placed on planets by Jyn Erso's ability; markers only (R-25.14).</summary>
        public List<PlanetMarker> PlanetMarkers { get; set; } = new List<PlanetMarker>();

        /// <summary>"Gain +N until the end of the turn" bonuses (stat.modify with duration end_of_turn); cleared when the turn ends.</summary>
        public List<StatModifier> TurnModifiers { get; set; } = new List<StatModifier>();

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

        /// <summary>
        /// Open credit requests (R-9.6, R-16.15), oldest first. They sit beside the resolution stack, not on it:
        /// an unanswered request holds nothing up, and every request is withdrawn when the turn ends.
        /// Not saved (owner decision, DECISIONS §4.2): a game saved or left with a request open loads without it.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public List<CreditRequest> CreditRequests { get; set; } = new List<CreditRequest>();

        /// <summary>Id for the next credit request; saved, so ids never repeat within a game, across loads too.</summary>
        public int NextCreditRequestId { get; set; } = 1;

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

    /// <summary>Which way the credits of a <see cref="CreditRequest"/> go.</summary>
    public enum CreditRequestKind
    {
        /// <summary>"Ask Credits": the requester asks the other player to pay them the amount.</summary>
        Ask,

        /// <summary>"Send Credits": the requester offers to pay the other player the amount.</summary>
        Send,
    }

    /// <summary>
    /// A credit request between two players, one of them the current player (R-9.6, R-16.15). The other player
    /// accepts or refuses it; on acceptance the credits move at once, or nothing does if the payer is short (R-9.7).
    /// </summary>
    public sealed class CreditRequest
    {
        public int Id { get; set; }

        /// <summary>The player who made the request.</summary>
        public string FromId { get; set; } = "";

        /// <summary>The player who answers it.</summary>
        public string ToId { get; set; } = "";

        public CreditRequestKind Kind { get; set; }

        public int Amount { get; set; }

        /// <summary>The player who pays if it is accepted.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string PayerId => Kind == CreditRequestKind.Ask ? ToId : FromId;

        /// <summary>The player who is paid if it is accepted.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string PayeeId => Kind == CreditRequestKind.Ask ? FromId : ToId;
    }

    /// <summary>A patrol token on the map (R-11).</summary>
    public sealed class PatrolState
    {
        public Faction Faction { get; set; }

        /// <summary>1 to 4 (R-11.3).</summary>
        public int Level { get; set; } = 1;

        public string SpaceId { get; set; } = "";
    }

    /// <summary>
    /// A contact token (R-12): on a planet (<see cref="SpaceId"/>, in <see cref="Slot"/>, a contact space id or
    /// the planet id for the planet space itself), captured on a bounty card, discarded beside the map (its
    /// crew is in play), or removed from the game.
    /// </summary>
    public sealed class ContactToken
    {
        public string Id { get; set; } = "";

        public string SpaceId { get; set; } = "";

        public string Slot { get; set; } = "";

        public bool FaceUp { get; set; }

        /// <summary>The bounty card it is captured on (R-15.52), or "".</summary>
        public string Bounty { get; set; } = "";

        /// <summary>R-16.7: captured in this turn's encounter step, so not deliverable until a later turn.</summary>
        public bool CapturedThisTurn { get; set; }

        /// <summary>Discarded beside the map (R-15.57): its crew card is held by a player.</summary>
        public bool Pool { get; set; }

        public bool Removed { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public bool OnMap => SpaceId.Length > 0 && Bounty.Length == 0 && !Pool && !Removed;
    }

    public sealed class TokenPlacement
    {
        public string TokenId { get; set; } = "";

        public string PlayerId { get; set; } = "";
    }

    /// <summary>A market deck: card ids, top first (R-16.41, R-16.42).</summary>
    public sealed class MarketDeck
    {
        public string Deck { get; set; } = "";

        public List<string> Cards { get; set; } = new List<string>();
    }

    /// <summary>A temporary bonus to one of a player's values (hyperdrive, ground_combat, ship_combat, hull, health).</summary>
    public sealed class StatModifier
    {
        public string PlayerId { get; set; } = "";

        public string Stat { get; set; } = "";

        public int By { get; set; }
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
