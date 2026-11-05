using System.Collections.Generic;

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

        /// <summary>Character card id (data/packs/*/cards/characters.json); empty until characters are chosen.</summary>
        public string CharacterId { get; set; } = "";

        /// <summary>Faceup side of the character card: 0 front (personal goal), 1 back (after flipping, R-18.3).</summary>
        public int CharacterSide { get; set; }

        /// <summary>Secret encounter cards the player holds facedown (R-15.82); they take no slot (R-15.90).</summary>
        public List<string> Secrets { get; set; } = new List<string>();

        /// <summary>Assets in the player's slots (R-14.1): bounties, cargo, crew, gear, mods and jobs. Placement is free (R-14.17).</summary>
        public List<AssetState> Assets { get; set; } = new List<AssetState>();

        /// <summary>The map space where the player's standee is (R-5).</summary>
        public string SpaceId { get; set; } = "";

        /// <summary>Ship sheet id (data/packs/*/cards/ship-sheets.json).</summary>
        public string ShipSheetId { get; set; } = "";

        /// <summary>The faceup side of the ship sheet (its image id); the rules read its values.</summary>
        public string ShipSideId { get; set; } = "";
    }

    /// <summary>An asset held by a player. Kind and cost are copied from the card when it is gained.</summary>
    public sealed class AssetState
    {
        public string CardId { get; set; } = "";

        /// <summary>bounty, cargo, crew, gear, mod or job (R-14.1).</summary>
        public string Kind { get; set; } = "";

        /// <summary>Printed cost, used for bartering (R-16.31); 0 for assets from encounter cards (R-15.78).</summary>
        public int Cost { get; set; }

        public List<string> Traits { get; set; } = new List<string>();

        /// <summary>Rotating assets [UB]: true once rotated to the other end (R-14.10).</summary>
        public bool Rotated { get; set; }
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
