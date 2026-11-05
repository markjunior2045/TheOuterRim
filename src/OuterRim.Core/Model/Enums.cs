namespace OuterRim.Core.Model
{
    /// <summary>The four factions (R-10.1).</summary>
    public enum Faction
    {
        Rebel,
        Imperial,
        Hutt,
        Syndicate,
    }

    /// <summary>Reputation with one faction: positive, neutral or negative (R-10.4).</summary>
    public enum Reputation
    {
        Negative = -1,
        Neutral = 0,
        Positive = 1,
    }

    /// <summary>The three steps of a turn, in order (R-4.2).</summary>
    public enum TurnStep
    {
        Planning,
        Action,
        Encounter,
    }

    /// <summary>Whether the game is being set up, played or over.</summary>
    public enum GamePhase
    {
        Setup,
        Playing,
        Ended,
    }

    /// <summary>The faces of a die (R-6.1).</summary>
    public enum DieFace
    {
        Blank,
        Hit,
        Crit,
        Focus,
    }
}
