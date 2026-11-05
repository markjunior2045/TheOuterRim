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
            return SlotsOf(content, player, player.Assets);
        }

        /// <summary>
        /// Board and ship slots, plus slots added by held assets ("You can also have 1 cargo in this slot":
        /// Smuggling Compartment; R-14.15).
        /// </summary>
        public static List<SlotSpec> SlotsOf(GameContent content, PlayerState player, IEnumerable<AssetState> held)
        {
            var slots = new List<SlotSpec>(content.BoardSlots);
            slots.AddRange(content.ShipSide(player.ShipSideId).Slots);
            foreach (var a in held)
            {
                foreach (var effect in PassiveEffects(content, a, "slot.modify"))
                {
                    slots.Add(new SlotSpec(new[] { (string)effect["slot"]! }, (int)effect["capacity"]!));
                }
            }
            return slots;
        }

        private static IEnumerable<Newtonsoft.Json.Linq.JObject> PassiveEffects(GameContent content, AssetState a, string op)
        {
            if (!content.HasCard(a.CardId) || !(content.Card(a.CardId)["abilities"] is Newtonsoft.Json.Linq.JArray abilities))
            {
                yield break;
            }
            foreach (Newtonsoft.Json.Linq.JObject ability in abilities)
            {
                if ((string?)ability["timing"]?["type"] != "passive") continue;
                foreach (Newtonsoft.Json.Linq.JObject e in (Newtonsoft.Json.Linq.JArray)ability["effects"]!)
                {
                    if ((string?)e["op"] == op) yield return e;
                }
            }
        }

        /// <summary>Slot types an asset may occupy: its kind, or the types its card names (Smuggling Compartment: cargo or mod).</summary>
        public static IReadOnlyList<string> TypesOf(GameContent content, AssetState a)
        {
            if (content.HasCard(a.CardId) && content.Card(a.CardId)["slot"] is Newtonsoft.Json.Linq.JArray slot)
            {
                var types = new List<string>();
                foreach (var t in slot) types.Add((string)t!);
                return types;
            }
            return new[] { a.Kind };
        }

        /// <summary>Traits of an asset: from the card data when loaded, else those recorded when it was gained.</summary>
        public static List<string> TraitsOf(GameContent content, AssetState a)
        {
            var traits = new List<string>(a.Traits);
            if (content.HasCard(a.CardId) && content.Card(a.CardId)["traits"] is Newtonsoft.Json.Linq.JArray arr)
            {
                foreach (var t in arr)
                {
                    if (!traits.Contains((string)t!)) traits.Add((string)t!);
                }
            }
            return traits;
        }

        /// <summary>
        /// Whether a set of assets is legal for the player: every asset fits a slot (R-14.13 to R-14.17)
        /// and no trait limit printed on a held card is exceeded ("Limit 1 ARMOR per character").
        /// </summary>
        public static bool Valid(GameContent content, PlayerState player, IReadOnlyList<AssetState> assets)
        {
            var types = new List<IReadOnlyList<string>>();
            foreach (var a in assets) types.Add(TypesOf(content, a));
            if (!Fits(SlotsOf(content, player, assets), types))
            {
                return false;
            }
            var limits = new Dictionary<string, int>();
            foreach (var a in assets)
            {
                foreach (var effect in PassiveEffects(content, a, "limit.trait"))
                {
                    var trait = (string)effect["trait"]!;
                    int max = (int)effect["max"]!;
                    limits[trait] = limits.TryGetValue(trait, out var m) ? Math.Min(m, max) : max;
                }
            }
            foreach (var kv in limits)
            {
                int n = 0;
                foreach (var a in assets)
                {
                    if (TraitsOf(content, a).Contains(kv.Key)) n++;
                }
                if (n > kv.Value) return false;
            }
            return true;
        }

        public static List<AssetState> With(IEnumerable<AssetState> assets, AssetState? extra = null, AssetState? without = null)
        {
            var list = new List<AssetState>();
            foreach (var a in assets)
            {
                if (!ReferenceEquals(a, without)) list.Add(a);
            }
            if (extra != null) list.Add(extra);
            return list;
        }

        /// <summary>
        /// R-14.13 to R-14.17: true if the assets can be arranged so each sits in a slot that accepts its
        /// type, with slot capacities respected. Placement is free (R-14.17), so only this matters.
        /// </summary>
        public static bool Fits(IReadOnlyList<SlotSpec> slots, IReadOnlyList<string> assetKinds)
        {
            var types = new List<IReadOnlyList<string>>();
            foreach (var k in assetKinds) types.Add(new[] { k });
            return Fits(slots, types);
        }

        /// <summary>As <see cref="Fits(IReadOnlyList{SlotSpec}, IReadOnlyList{string})"/>, each asset accepting several slot types.</summary>
        public static bool Fits(IReadOnlyList<SlotSpec> slots, IReadOnlyList<IReadOnlyList<string>> assetKinds)
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

        private static bool Augment(int asset, IReadOnlyList<IReadOnlyList<string>> kinds, List<IReadOnlyList<string>> units, int[] owner, bool[] seen)
        {
            for (int u = 0; u < units.Count; u++)
            {
                if (seen[u] || !Overlaps(units[u], kinds[asset]))
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

        private static bool Overlaps(IReadOnlyList<string> slot, IReadOnlyList<string> asset)
        {
            foreach (var t in asset)
            {
                if (Contains(slot, t)) return true;
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
            return Valid(content, player, player.Assets);
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
