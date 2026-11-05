#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Effects
{
    /// <summary>
    /// Evaluates condition nodes of card data (registry "conditions", EFFECTS.md §6).
    /// "You" is the owner of the ability. Inside a player selector's "where", the candidate player is
    /// the subject (registry note).
    /// </summary>
    public static class Conditions
    {
        /// <summary>Conditions this engine evaluates. The coverage test checks card data against it.</summary>
        public static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.Ordinal)
        {
            "all", "any", "not", "has_skill", "has_asset", "on_space", "is_droid", "is_character", "rep", "rep_count",
            "credits_at_least", "most_famous", "more_fame_than", "same_space", "other_player_in_space", "in_patrol_space",
            "ship_cost_at_least", "ship_trait", "planet_markers", "not_defeated", "no_damage", "player_count",
            "combat", "delivering", "moving_into",
        };

        /// <param name="context">What is happening, for conditions about it: "combat_type" (ground, ship),
        /// "combat_result" (won, lost), "opponent" (patrol, player, npc, contact), "patrol_faction",
        /// "delivering_traits" (comma list), "moving_into" (patrol, maelstrom).</param>
        public static bool Eval(GameContent content, GameState state, PlayerState you, JToken? node, PlayerState? candidate = null,
            IReadOnlyDictionary<string, string>? context = null)
        {
            if (node == null || node.Type == JTokenType.Null)
            {
                return true;
            }
            var c = (JObject)node;
            var subject = candidate ?? you;
            string kind = (string)c["cond"]!;
            switch (kind)
            {
                case "all":
                    foreach (var x in (JArray)c["of"]!)
                    {
                        if (!Eval(content, state, you, x, candidate, context)) return false;
                    }
                    return true;
                case "any":
                    foreach (var x in (JArray)c["of"]!)
                    {
                        if (Eval(content, state, you, x, candidate, context)) return true;
                    }
                    return false;
                case "not":
                    return !Eval(content, state, you, c["of"], candidate, context);
                case "has_skill":
                    foreach (var s in (JArray)c["skill"]!)
                    {
                        if (SkillTests.Instances(content, subject, SkillTests.Parse((string)s!)) > 0) return true;
                    }
                    return false;
                case "has_asset":
                    return HasAsset(content, subject, c);
                case "on_space":
                {
                    var space = content.Map[subject.SpaceId];
                    if (c["type"] != null && !MatchesType(space, (string)c["type"]!)) return false;
                    if (c["id"] != null && space.Id != (string)c["id"]!) return false;
                    if (c["faction"] != null && (space.Faction == null || Patrols.Id(space.Faction.Value) != (string)c["faction"]!)) return false;
                    return true;
                }
                case "is_droid":
                    return subject.CharacterId.Length > 0 && content.Character(subject.CharacterId).Droid;
                case "is_character":
                    return subject.CharacterId.Length > 0 && content.Name(subject.CharacterId).StartsWith((string)c["name"]!, StringComparison.Ordinal);
                case "rep":
                {
                    var target = subject;
                    var faction = Selectors.Faction(content, state, target, c["faction"]!);
                    if (faction == null) return false;
                    var states = new List<string>();
                    foreach (var s in (JArray)c["in"]!) states.Add((string)s!);
                    return states.Contains(RepId(target.Reputation.Get(faction.Value)));
                }
                case "rep_count":
                {
                    var wanted = (string)c["state"]!;
                    int n = 0;
                    foreach (Faction f in new[] { Faction.Rebel, Faction.Imperial, Faction.Hutt, Faction.Syndicate })
                    {
                        if (RepId(subject.Reputation.Get(f)) == wanted) n++;
                    }
                    return n >= ((int?)c["min"] ?? 0) && n <= ((int?)c["max"] ?? 4);
                }
                case "credits_at_least":
                    return subject.Credits >= (int)c["amount"]!;
                case "most_famous":
                    return PlayerRules.IsMostFamous(state, subject);
                case "more_fame_than":
                    // In a selector: the candidate has more fame than you (Bossk-3, Firespray-31).
                    return candidate != null
                        ? PlayerRules.Fame(candidate) > PlayerRules.Fame(you)
                        : false;
                case "same_space":
                    return candidate != null && candidate.SpaceId == you.SpaceId;
                case "other_player_in_space":
                    foreach (var p in state.Players)
                    {
                        if (p.Id != subject.Id && p.SpaceId == subject.SpaceId
                            && Eval(content, state, you, c["where"], p)) return true;
                    }
                    return false;
                case "in_patrol_space":
                    foreach (var patrol in state.PatrolsIn(subject.SpaceId))
                    {
                        if (c["faction"] != null && Patrols.Id(patrol.Faction) != (string)c["faction"]!) continue;
                        if (c["rep"] != null && !((JArray)c["rep"]!).ToObject<List<string>>()!.Contains(RepId(subject.Reputation.Get(patrol.Faction)))) continue;
                        return true;
                    }
                    return false;
                case "ship_cost_at_least":
                    return (content.ShipSide(subject.ShipSideId).Cost ?? 0) >= (int)c["amount"]!;
                case "ship_trait":
                    return (string)c["trait"]! == "starter ship" && content.ShipSide(subject.ShipSideId).Role == "starter";
                case "planet_markers":
                {
                    int n = 0;
                    foreach (var m in state.PlanetMarkers)
                    {
                        if (m.PlayerId != subject.Id) continue;
                        var f = content.Map[m.SpaceId].Faction;
                        if (c["faction"] == null || (f != null && Patrols.Id(f.Value) == (string)c["faction"]!)) n++;
                    }
                    return n >= (int)c["min"]!;
                }
                case "not_defeated":
                    return !subject.Defeated;
                case "no_damage":
                    return subject.CharacterDamage == 0 && subject.ShipDamage == 0;
                case "combat":
                {
                    if (context == null || !context.ContainsKey("combat_type")) return false;
                    if (c["type"] != null && (string)c["type"]! != "by_space" && (string)c["type"]! != context["combat_type"]) return false;
                    if (c["result"] != null && (!context.TryGetValue("combat_result", out var res) || res != (string)c["result"]!)) return false;
                    if (c["opponent"] != null && (!context.TryGetValue("opponent", out var opp) || opp != (string)c["opponent"]!)) return false;
                    if (c["patrol_faction"] != null && (!context.TryGetValue("patrol_faction", out var pf) || pf != (string)c["patrol_faction"]!)) return false;
                    return true;
                }
                case "delivering":
                {
                    if (context == null || !context.TryGetValue("delivering_traits", out var traits)) return false;
                    return c["trait"] == null || Array.IndexOf(traits.Split(','), (string)c["trait"]!) >= 0;
                }
                case "moving_into":
                    return context != null && context.TryGetValue("moving_into", out var into) && into == (string)c["what"]!;
                case "player_count":
                    return state.Players.Count >= (int)c["min"]! && state.Players.Count <= (int)c["max"]!;
                default:
                    throw new NotSupportedException("Condition '" + kind + "' is not implemented yet (PLAN T2.3b, T2.3c).");
            }
        }

        public static string RepId(Reputation r)
        {
            return r == Reputation.Positive ? "pos" : r == Reputation.Negative ? "neg" : "neu";
        }

        private static bool MatchesType(Space space, string type)
        {
            switch (type)
            {
                case "planet": return space.IsPlanet;
                case "navpoint": return space.Type == SpaceType.Navpoint;
                case "maelstrom": return space.Type == SpaceType.Maelstrom;
                case "core_worlds": return space.Type == SpaceType.CoreWorlds;
                default: return false;
            }
        }

        private static bool HasAsset(GameContent content, PlayerState player, JObject c)
        {
            var filter = (JObject)c["filter"]!["asset"]!;
            int n = 0, cost = 0;
            foreach (var a in player.Assets)
            {
                if (!Selectors.AssetMatches(content, a, filter)) continue;
                n++;
                cost += a.Cost;
            }
            int min = (int?)c["min"] ?? (c["min_total_cost"] != null ? 0 : 1);
            if (n < min) return false;
            if (c["max"] != null && n > (int)c["max"]!) return false;
            if (c["min_total_cost"] != null && cost < (int)c["min_total_cost"]!) return false;
            return true;
        }
    }

    /// <summary>Selectors and amounts of card data (EFFECTS.md §6, §7).</summary>
    public static class Selectors
    {
        /// <summary>A faction selector that needs no decision: a named faction or "planet" (null if the space has none).</summary>
        public static Faction? Faction(GameContent content, GameState state, PlayerState you, JToken selector)
        {
            if (selector.Type != JTokenType.String)
            {
                throw new NotSupportedException("Faction selector " + selector.ToString(Newtonsoft.Json.Formatting.None) + " is not implemented yet (PLAN T2.3b).");
            }
            var s = (string)selector!;
            switch (s)
            {
                case "rebel": return Model.Faction.Rebel;
                case "imperial": return Model.Faction.Imperial;
                case "hutt": return Model.Faction.Hutt;
                case "syndicate": return Model.Faction.Syndicate;
                case "planet": return content.Map[you.SpaceId].Faction;
                default: throw new NotSupportedException("Faction selector '" + s + "' is not implemented yet (PLAN T2.3b).");
            }
        }

        public static bool AssetMatches(GameContent content, AssetState a, JObject filter)
        {
            if (filter["type"] is JArray types)
            {
                bool ok = false;
                foreach (var t in types) ok |= (string)t! == a.Kind;
                if (!ok) return false;
            }
            if (filter["trait"] != null && !a.Traits.Contains((string)filter["trait"]!)) return false;
            if (filter["droid"] != null)
            {
                bool droid = content.HasCard(a.CardId) && ((bool?)content.Card(a.CardId)["droid"] ?? false);
                if (droid != (bool)filter["droid"]!) return false;
            }
            if (filter["name"] != null && content.Name(a.CardId) != (string)filter["name"]!) return false;
            return true;
        }

        /// <summary>A whole-number amount: an integer or {"per": {"each", "counter"}}. Other shapes need a decision (T2.3b).</summary>
        public static int Amount(GameContent content, PlayerState you, JToken amount)
        {
            if (amount.Type == JTokenType.Integer)
            {
                return (int)amount;
            }
            var o = (JObject)amount;
            if (o["per"] is JObject per)
            {
                return (int)per["each"]! * Counter(you, (string)per["counter"]!);
            }
            throw new NotSupportedException("Amount " + amount.ToString(Newtonsoft.Json.Formatting.None) + " is not implemented yet (PLAN T2.3b).");
        }

        public static int Counter(PlayerState you, string counter)
        {
            string? kind;
            switch (counter)
            {
                case "jobs": kind = "job"; break;
                case "crew": kind = "crew"; break;
                case "bounties": kind = "bounty"; break;
                case "cargo": kind = "cargo"; break;
                case "mods": kind = "mod"; break;
                case "gear": kind = "gear"; break;
                default: throw new NotSupportedException("Counter '" + counter + "' is not implemented yet.");
            }
            int n = 0;
            foreach (var a in you.Assets)
            {
                if (a.Kind == kind) n++;
            }
            return n;
        }
    }
}
