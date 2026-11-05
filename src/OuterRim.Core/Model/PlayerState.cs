namespace OuterRim.Core.Model
{
    /// <summary>One player's state. Fields are added as the rules that use them are built.</summary>
    public sealed class PlayerState
    {
        public string Id { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>Player board colour (green, red, yellow, white).</summary>
        public string Color { get; set; } = "";

        public int Credits { get; set; }

        /// <summary>Fame, never below 0 (R-18.14).</summary>
        public int Fame { get; set; }

        public ReputationTrack Reputation { get; set; } = new ReputationTrack();

        /// <summary>Damage tokens on the character card (R-8).</summary>
        public int CharacterDamage { get; set; }

        /// <summary>Damage tokens on the ship sheet (R-8).</summary>
        public int ShipDamage { get; set; }

        /// <summary>True while the player is defeated (R-8, R-4.8).</summary>
        public bool Defeated { get; set; }
    }

    /// <summary>Reputation with each faction (R-10.4). Fixed fields keep saves stable and readable.</summary>
    public sealed class ReputationTrack
    {
        public Reputation Rebel { get; set; } = Reputation.Neutral;

        public Reputation Imperial { get; set; } = Reputation.Neutral;

        public Reputation Hutt { get; set; } = Reputation.Neutral;

        public Reputation Syndicate { get; set; } = Reputation.Neutral;

        public Reputation Get(Faction faction)
        {
            switch (faction)
            {
                case Faction.Rebel: return Rebel;
                case Faction.Imperial: return Imperial;
                case Faction.Hutt: return Hutt;
                default: return Syndicate;
            }
        }

        public void Set(Faction faction, Reputation value)
        {
            switch (faction)
            {
                case Faction.Rebel: Rebel = value; break;
                case Faction.Imperial: Imperial = value; break;
                case Faction.Hutt: Hutt = value; break;
                default: Syndicate = value; break;
            }
        }
    }
}
