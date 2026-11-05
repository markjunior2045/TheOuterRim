#nullable enable
using System;
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;

namespace OuterRim.Core.Rules
{
    /// <summary>One slot: the asset types it accepts (two for "Cargo or Mod", R-14.16) and how many cards ("x2", R-14.14).</summary>
    public sealed class SlotSpec
    {
        public SlotSpec(IReadOnlyList<string> types, int count)
        {
            Types = types;
            Count = count;
        }

        public IReadOnlyList<string> Types { get; }

        public int Count { get; }
    }

    /// <summary>Assets and slots (R-14, R-16.27 to R-16.40, R-17).</summary>
    public static class Assets
    {
        /// <summary>Resolution handler: gain an asset (vars "player", "card", "kind", "cost", optional "traits", "forced").</summary>
        public const string GainHandler = "rules.gain_asset";

        /// <summary>Resolution handler: discard until every asset fits (var "player"), R-14.18.</summary>
        public const string FitHandler = "rules.enforce_slots";

        /// <summary>Resolution handler: buy a ship (vars "player", "sheet", optional "barter": asset card ids joined by ',').</summary>
        public const string BuyShipHandler = "rules.buy_ship";

        /// <summary>Asset types (R-14.1).</summary>
        public static readonly string[] Kinds = { "bounty", "cargo", "crew", "gear", "mod", "job" };

        /// <summary>All slots of a player: player board plus the faceup side of the ship sheet (R-14.2).</summary>
        public static List<SlotSpec> SlotsOf(GameContent content, PlayerState player)
        {
            var slots = new List<SlotSpec>(content.BoardSlots);
            slots.AddRange(content.ShipSide(player.ShipSideId).Slots);
            return slots;
        }

        /// <summary>
        /// R-14.13 to R-14.17: true if the assets can be arranged so each sits in a slot that accepts its
        /// type, with slot capacities respected. Placement is free (R-14.17), so only this matters.
        /// </summary>
        public static bool Fits(IReadOnlyList<SlotSpec> slots, IReadOnlyList<string> assetKinds)
        {
            var units = new List<IReadOnlyList<string>>();
            foreach (var s in slots)
            {
                for (int i = 0; i < s.Count; i++)
                {
                    units.Add(s.Types);
                }
            }
            if (assetKinds.Count > units.Count)
            {
                return false;
            }
            var unitOwner = new int[units.Count];
            for (int i = 0; i < unitOwner.Length; i++)
            {
                unitOwner[i] = -1;
            }
            for (int a = 0; a < assetKinds.Count; a++)
            {
                if (!Augment(a, assetKinds, units, unitOwner, new bool[units.Count]))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Augment(int asset, IReadOnlyList<string> kinds, List<IReadOnlyList<string>> units, int[] owner, bool[] seen)
        {
            for (int u = 0; u < units.Count; u++)
            {
                if (seen[u] || !Contains(units[u], kinds[asset]))
                {
                    continue;
                }
                seen[u] = true;
                if (owner[u] < 0 || Augment(owner[u], kinds, units, owner, seen))
                {
                    owner[u] = asset;
                    return true;
                }
            }
            return false;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (var x in list)
            {
                if (x == value)
                {
                    return true;
                }
            }
            return false;
        }

        public static List<string> KindsOf(IEnumerable<AssetState> assets, AssetState? extra = null, AssetState? without = null)
        {
            var kinds = new List<string>();
            foreach (var a in assets)
            {
                if (!ReferenceEquals(a, without))
                {
                    kinds.Add(a.Kind);
                }
            }
            if (extra != null)
            {
                kinds.Add(extra.Kind);
            }
            return kinds;
        }

        public static bool AllFit(GameContent content, PlayerState player)
        {
            return Fits(SlotsOf(content, player), KindsOf(player.Assets));
        }

        /// <summary>Discards an asset (R-14.6 allows it only when a rule or ability does, or to open a slot).</summary>
        public static void Discard(PlayerState player, AssetState asset, string reason, List<GameEvent> events)
        {
            player.Assets.Remove(asset);
            events.Add(new AssetDiscardedEvent(player.Id, asset.CardId, asset.Kind, reason));
        }

        public static AssetState? Find(PlayerState player, string cardId)
        {
            foreach (var a in player.Assets)
            {
                if (a.CardId == cardId)
                {
                    return a;
                }
            }
            return null;
        }

        /// <summary>
        /// R-16.31 to R-16.39: the cost reduction from bartering these assets when buying. Bounties, crew and
        /// jobs cannot be bartered (R-16.33), nor assets without a cost (R-16.32); the ship only when buying
        /// another ship (R-16.38, R-16.39). Returns null with a reason when the barter is not allowed.
        /// </summary>
        public static int? BarterValue(IEnumerable<AssetState> assets, out string reason)
        {
            int total = 0;
            foreach (var a in assets)
            {
                if (a.Kind == "bounty" || a.Kind == "crew" || a.Kind == "job")
                {
                    reason = "R-16.33: bounties, crew and jobs cannot be bartered.";
                    return null;
                }
                if (a.Cost <= 0)
                {
                    reason = "R-16.32: assets without a cost cannot be bartered.";
                    return null;
                }
                total += a.Cost;
            }
            reason = "";
            return total;
        }

        /// <summary>R-16.36: bartering only reduces the cost, to a minimum of 0; no change is given.</summary>
        public static int PriceAfterBarter(int cost, int barter)
        {
            return Math.Max(0, cost - barter);
        }
    }
}
