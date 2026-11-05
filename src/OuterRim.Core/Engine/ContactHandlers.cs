#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Map;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>Contact tokens on the map (R-12), their databank cards (R-13) and bounties against them (R-15.42 to R-15.55).</summary>
    public static class Contacts
    {
        public const string EncounterHandler = "encounter.contact";
        public const string PlaceHandler = "rules.place_contact";

        /// <summary>R-3.8: tokens shuffled facedown, one on each contact space of the same class.</summary>
        public static void Setup(GameContent content, GameState state)
        {
            state.Contacts.Clear();
            var byClass = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var id in content.ContactTokenIds())
            {
                var cls = (string)content.Card(id)["class"]!;
                if (!byClass.TryGetValue(cls, out var list)) byClass[cls] = list = new List<string>();
                list.Add(id);
            }
            foreach (var list in byClass.Values) state.Random.Shuffle(list);
            foreach (var space in content.Map.ContactSpaces)
            {
                if (!byClass.TryGetValue(space.Class, out var list) || list.Count == 0) continue;
                state.Contacts.Add(new ContactToken { Id = list[0], SpaceId = space.Planet, Slot = space.Id });
                list.RemoveAt(0);
            }
        }

        public static ContactToken Token(GameState state, string id)
        {
            foreach (var t in state.Contacts)
            {
                if (t.Id == id) return t;
            }
            throw new KeyNotFoundException("Unknown contact token '" + id + "'.");
        }

        /// <summary>R-12.10: tokens on a planet (in its contact spaces or on the planet space itself).</summary>
        public static List<ContactToken> OnPlanet(GameState state, string planet)
        {
            var list = new List<ContactToken>();
            foreach (var t in state.Contacts)
            {
                if (t.OnMap && t.SpaceId == planet) list.Add(t);
            }
            return list;
        }

        /// <summary>R-12.6, R-12.7, R-15.13: a planet's slots are its contact spaces and the planet space itself.</summary>
        public static List<string> Slots(MapGraph map, string planet)
        {
            var slots = new List<string>();
            foreach (var cs in map.ContactSpacesOf(planet)) slots.Add(cs.Id);
            slots.Add(planet);
            return slots;
        }

        public static List<string> EmptySlots(MapGraph map, GameState state, string planet)
        {
            var slots = Slots(map, planet);
            foreach (var t in OnPlanet(state, planet)) slots.Remove(t.Slot);
            return slots;
        }

        public static List<string> Planets(MapGraph map)
        {
            var list = new List<string>();
            foreach (var s in map.Spaces)
            {
                if (s.IsPlanet) list.Add(s.Id);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>The bounties a player holds for this contact (R-15.42).</summary>
        public static List<AssetState> BountiesFor(GameContent content, PlayerState player, string token)
        {
            var list = new List<AssetState>();
            foreach (var a in player.Assets)
            {
                if (a.Kind == "bounty" && content.HasCard(a.CardId) && (string?)content.Card(a.CardId)["target"] == token) list.Add(a);
            }
            return list;
        }

        /// <summary>The token captured on a bounty card, if any.</summary>
        public static ContactToken? CapturedOn(GameState state, string bounty)
        {
            foreach (var t in state.Contacts)
            {
                if (t.Bounty == bounty) return t;
            }
            return null;
        }

        /// <summary>
        /// When an asset is discarded: a captured token on a bounty (R-15.45), or the token of a crew (R-15.11),
        /// goes faceup to the nearest planet with an empty slot; queued so the player can break ties (R-15.12).
        /// </summary>
        public static void OnDiscarded(GameContent content, GameState state, PlayerState player, AssetState asset)
        {
            ContactToken? token = null;
            if (asset.Kind == "bounty")
            {
                token = CapturedOn(state, asset.CardId);
            }
            else if (asset.Kind == "crew" && content.HasCard(asset.CardId) && content.Card(asset.CardId)["embedded_in"] is JToken host
                && content.HasCard((string)host!) && (string?)content.Card((string)host!)["kind"] == "contact_card")
            {
                int number = (int)content.Card((string)host!)["number"]!;
                foreach (var t in state.Contacts)
                {
                    if (t.Pool && (int?)content.Card(t.Id)["databank"] == number) token = t;
                }
            }
            if (token == null) return;
            token.Bounty = "";
            token.Pool = false;
            state.TokensToPlace.Add(new TokenPlacement { TokenId = token.Id, PlayerId = player.Id });
        }

        internal static void Place(GameState state, ContactToken token, string planet, string slot, bool faceUp, List<GameEvent> events)
        {
            token.SpaceId = planet;
            token.Slot = slot;
            token.FaceUp = faceUp;
            token.Bounty = "";
            token.Pool = false;
            events.Add(new ContactPlacedEvent(token.Id, planet, faceUp ? token.Id : ""));
        }

        /// <summary>R-12.12: two tokens swap places, each keeping its side.</summary>
        internal static void Exchange(ContactToken a, ContactToken b, List<GameEvent> events)
        {
            var space = a.SpaceId;
            var slot = a.Slot;
            a.SpaceId = b.SpaceId;
            a.Slot = b.Slot;
            b.SpaceId = space;
            b.Slot = slot;
            events.Add(new ContactsExchangedEvent(a.Id, b.Id));
        }

        /// <summary>The token being resolved by a contact card ("discard Dengar's token").</summary>
        internal static string? Current(IReadOnlyDictionary<string, string> context)
        {
            return context.TryGetValue("contact", out var id) ? id : null;
        }
    }

    /// <summary>
    /// Encountering a contact (R-12.15 to R-12.19): the player chooses a token on their planet before turning
    /// it faceup; with a matching bounty they may fight it instead of resolving its databank card (R-12.18).
    /// Vars: "player" (default current), "target" (token id, or "" to choose).
    /// </summary>
    internal sealed class ContactEncounterHandler : IResolutionHandler
    {
        public string Id => Contacts.EncounterHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var player = state.FindPlayer(frame.Vars.ContainsKey("player") ? frame.Get("player") : state.CurrentPlayerId)!;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    if (frame.Vars.TryGetValue("target", out var t) && t.Length > 0) return StepResult.Continue();
                    var options = new List<string>();
                    foreach (var token in Contacts.OnPlanet(state, player.SpaceId)) options.Add(token.Id);
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return SpaceSelect.AskOrSet(ctx, frame, player.Id, "rules.choose_contact", options, "target");
                }
                case 1:
                {
                    frame.Step = 2;
                    var token = Contacts.Token(state, frame.Get("target"));
                    if (!token.FaceUp)
                    {
                        token.FaceUp = true; // R-12.16
                        ctx.Events.Add(new ContactRevealedEvent(player.Id, token.Id));
                    }
                    var bounties = Contacts.BountiesFor(ctx.Content, player, token.Id);
                    if (bounties.Count == 0)
                    {
                        frame.Set("choice", "databank");
                        return StepResult.Continue();
                    }
                    var options = new List<DecisionOption> { new DecisionOption("databank", "rules.resolve_databank") };
                    foreach (var b in bounties) options.Add(new DecisionOption(b.CardId, "rules.fight_bounty"));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.contact_or_bounty", options, "choice"));
                }
                case 2:
                {
                    frame.Step = 3;
                    var token = Contacts.Token(state, frame.Get("target"));
                    if (frame.Get("choice") == "databank")
                    {
                        // R-13.3: a random card of the token's number.
                        var versions = ctx.Content.DatabankCards((int)ctx.Content.Card(token.Id)["databank"]!);
                        var card = versions[state.Random.NextInt(versions.Count)];
                        ctx.Events.Add(new DatabankResolvedEvent(player.Id, card));
                        var list = EffectHandlers.ListFrame(player.Id, card, "effects");
                        list.Set("ctx.contact", token.Id);
                        return StepResult.Call(list);
                    }
                    // R-15.49 to R-15.51: a combat against the contact, of the bounty's type and value.
                    var bounty = ctx.Content.Card(frame.Get("choice"));
                    var combat = new Frame(Combat.Handler);
                    combat.Set("type", (string)bounty["combat"]!["type"]!);
                    combat.Set("attacker", player.Id);
                    combat.Set("defender_kind", "contact");
                    combat.Set("defender", token.Id);
                    combat.Set("defender_value", ((int)bounty["combat"]!["value"]!).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    combat.Set("outcome", BountyOutcomeHandler.HandlerId);
                    combat.Set("ctx.bounty", frame.Get("choice"));
                    combat.Set("ctx.contact", token.Id);
                    return StepResult.Call(combat);
                }
                default:
                    ctx.Events.Add(new EncounterResolvedEvent(player.Id, "contact", frame.Get("target")));
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// Winning a bounty combat (R-15.52 to R-15.55), before damage (R-15.54): eliminate (reward, then the card and
    /// token leave the game) or capture (the token goes on the card). Losing changes nothing (R-15.53).
    /// </summary>
    internal sealed class BountyOutcomeHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.bounty_outcome";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("attacker"))!;
            var bounty = frame.Get("ctx.bounty");
            var token = Contacts.Token(ctx.State, frame.Get("ctx.contact"));
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    if (frame.Get("winner") != "attacker") return StepResult.Done();
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.eliminate_or_capture",
                        new[] { new DecisionOption("eliminate", "rules.eliminate"), new DecisionOption("capture", "rules.capture") }, "how"));
                case 1:
                {
                    frame.Step = 2;
                    if (frame.Get("how") == "capture")
                    {
                        token.SpaceId = "";
                        token.Slot = "";
                        token.Bounty = bounty;
                        token.CapturedThisTurn = ctx.State.Step == TurnStep.Encounter; // R-16.7
                        ctx.Events.Add(new ContactCapturedEvent(player.Id, token.Id, bounty));
                        return StepResult.Done();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(player.Id, bounty, "elimination_reward"));
                }
                case 2:
                    frame.Step = 3;
                    Bounties.RemoveFromGame(ctx, player, bounty, token);
                    ctx.Events.Add(new ContactEliminatedEvent(player.Id, token.Id, bounty));
                    return StepResult.Call(TriggersHandler.For("contact_eliminated", player.Id));
                case 3:
                    frame.Step = 4;
                    return StepResult.Call(TriggersHandler.For("bounty_reward", player.Id));
                default:
                    return StepResult.Done();
            }
        }
    }

    internal static class Bounties
    {
        /// <summary>R-15.55, R-16.4: after a reward, the bounty card and its token are removed from the game.</summary>
        public static void RemoveFromGame(ResolutionContext ctx, PlayerState player, string bounty, ContactToken token)
        {
            var asset = Assets.Find(player, bounty);
            if (asset != null)
            {
                player.Assets.Remove(asset);
                ctx.Events.Add(new AssetDiscardedEvent(player.Id, bounty, "bounty", "removed"));
            }
            token.SpaceId = "";
            token.Slot = "";
            token.Bounty = "";
            token.Removed = true;
        }
    }

    /// <summary>
    /// Delivering a captured contact (R-16.4): the capture reward, then the bounty and token leave the game.
    /// Vars: "player", "bounty".
    /// </summary>
    internal sealed class DeliverBountyHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.deliver_bounty";

        public string Id => HandlerId;

        public static Frame For(string player, string bounty)
        {
            var f = new Frame(HandlerId);
            f.Set("player", player);
            f.Set("bounty", bounty);
            return f;
        }

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            var bounty = frame.Get("bounty");
            switch (frame.Step)
            {
                case 0:
                    frame.Step = 1;
                    if (Contacts.CapturedOn(ctx.State, bounty) == null || Assets.Find(player, bounty) == null)
                    {
                        ctx.Return("failed", "true");
                        frame.Step = 9;
                        return StepResult.Done();
                    }
                    return StepResult.Call(EffectHandlers.ListFrame(player.Id, bounty, "capture_reward"));
                case 1:
                {
                    frame.Step = 2;
                    var token = Contacts.CapturedOn(ctx.State, bounty)!;
                    Bounties.RemoveFromGame(ctx, player, bounty, token);
                    ctx.Events.Add(new BountyDeliveredEvent(player.Id, bounty, token.Id));
                    return StepResult.Call(TriggersHandler.For("contact_delivered", player.Id));
                }
                case 2:
                    frame.Step = 3;
                    return StepResult.Call(TriggersHandler.For("bounty_reward", player.Id));
                default:
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// R-15.11, R-15.12, R-15.45: a token that returns to the map goes faceup to the planet with an empty slot
    /// nearest to its player (who breaks ties), ignoring class. Run by the resolver as soon as one is queued.
    /// </summary>
    internal sealed class PlaceContactHandler : IResolutionHandler
    {
        public string Id => Contacts.PlaceHandler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            if (frame.Step == 0)
            {
                if (state.TokensToPlace.Count == 0) return StepResult.Done();
                var next = state.TokensToPlace[0];
                frame.Set("token", next.TokenId);
                frame.Set("player", next.PlayerId);
                state.TokensToPlace.RemoveAt(0);
                var player = state.FindPlayer(next.PlayerId)!;
                var candidates = new List<string>();
                foreach (var planet in Contacts.Planets(ctx.Content.Map))
                {
                    if (Contacts.EmptySlots(ctx.Content.Map, state, planet).Count > 0) candidates.Add(planet);
                }
                frame.Step = 1;
                var nearest = ctx.Content.Map.Nearest(player.SpaceId, candidates);
                if (nearest.Count == 0)
                {
                    frame.Step = 0;
                    return StepResult.Continue();
                }
                return SpaceSelect.AskOrSet(ctx, frame, player.Id, "rules.place_contact", nearest, "planet");
            }
            var token = Contacts.Token(state, frame.Get("token"));
            var planetId = frame.Get("planet");
            Contacts.Place(state, token, planetId, Contacts.EmptySlots(ctx.Content.Map, state, planetId)[0], true, ctx.Events);
            frame.Step = 0;
            frame.Vars.Remove("planet");
            return StepResult.Continue();
        }
    }

    /// <summary>
    /// Contact effects (EFFECTS.md §4): look at, move, exchange, place, discard and encounter contact tokens.
    /// "This token" is the one whose databank card is resolving (context "contact").
    /// </summary>
    internal sealed class ContactEffectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.contact";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var context = FrameContext.Of(frame);
            switch ((string)node["op"]!)
            {
                case "contact.discard_token": return DiscardToken(ctx, context);
                case "contact.relocate_self": return Relocate(ctx, frame, owner, context, (string)node["planet"]!);
                case "contact.place_facedown": return PlaceFacedown(ctx, frame, owner, context);
                case "contact.look": return Look(ctx, frame, owner, node);
                case "contact.move": return Move(ctx, frame, owner, (bool?)node["facedown_only"] == true);
                case "encounter.contact":
                {
                    if (frame.Step == 0)
                    {
                        frame.Step = 1;
                        var encounter = new Frame(Contacts.EncounterHandler);
                        encounter.Set("player", owner.Id);
                        encounter.Set("target", "");
                        return StepResult.Call(encounter);
                    }
                    EffectHandlers.MarkFailedFromChild(frame);
                    EffectHandlers.ReturnFailed(ctx, frame);
                    return StepResult.Done();
                }
                default: throw new NotSupportedException("Effect '" + (string)node["op"]! + "' is not implemented yet.");
            }
        }

        private static StepResult Fail(ResolutionContext ctx)
        {
            ctx.Return("failed", "true");
            return StepResult.Done();
        }

        /// <summary>"Discard X's token": it leaves the map, beside it, until its crew is discarded (R-15.11, R-15.57).</summary>
        private static StepResult DiscardToken(ResolutionContext ctx, IReadOnlyDictionary<string, string> context)
        {
            var id = Contacts.Current(context);
            if (id == null) return Fail(ctx);
            var token = Contacts.Token(ctx.State, id);
            token.SpaceId = "";
            token.Slot = "";
            token.Pool = true;
            ctx.Events.Add(new ContactDiscardedEvent(token.Id));
            return StepResult.Done();
        }

        /// <summary>"Move Maz Kanata's token to an empty contact space on Takodana. If there is not one, exchange it with a contact there."</summary>
        private static StepResult Relocate(ResolutionContext ctx, Frame frame, PlayerState owner, IReadOnlyDictionary<string, string> context, string planet)
        {
            var id = Contacts.Current(context);
            if (id == null) return Fail(ctx);
            var token = Contacts.Token(ctx.State, id);
            if (!token.OnMap || token.SpaceId == planet) return StepResult.Done();
            if (frame.Step == 0)
            {
                var empty = Contacts.EmptySlots(ctx.Content.Map, ctx.State, planet);
                if (empty.Count > 0)
                {
                    Contacts.Place(ctx.State, token, planet, empty[0], token.FaceUp, ctx.Events);
                    return StepResult.Done();
                }
                frame.Step = 1;
                var options = new List<string>();
                foreach (var t in Contacts.OnPlanet(ctx.State, planet)) options.Add(t.Id);
                return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_contact", options, "other");
            }
            Contacts.Exchange(token, Contacts.Token(ctx.State, frame.Get("other")), ctx.Events);
            return StepResult.Done();
        }

        /// <summary>Dengar: "place Dengar's token facedown on his contact space or an empty contact space."</summary>
        private static StepResult PlaceFacedown(ResolutionContext ctx, Frame frame, PlayerState owner, IReadOnlyDictionary<string, string> context)
        {
            var id = Contacts.Current(context);
            if (id == null) return Fail(ctx);
            var token = Contacts.Token(ctx.State, id);
            if (frame.Step == 0)
            {
                frame.Step = 1;
                var options = new List<string> { token.Slot };
                foreach (var planet in Contacts.Planets(ctx.Content.Map))
                {
                    foreach (var slot in Contacts.EmptySlots(ctx.Content.Map, ctx.State, planet))
                    {
                        if (!options.Contains(slot)) options.Add(slot);
                    }
                }
                return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_contact_space", options, "slot");
            }
            var slotId = frame.Get("slot");
            var planetId = slotId;
            foreach (var cs in ctx.Content.Map.ContactSpaces)
            {
                if (cs.Id == slotId) planetId = cs.Planet;
            }
            Contacts.Place(ctx.State, token, planetId, slotId, false, ctx.Events);
            return StepResult.Done();
        }

        /// <summary>
        /// "Secretly look at" contacts (R-12.9): the owner sees the token's face, nobody else, and it stays facedown.
        /// count "all": every facedown token on one chosen planet; a number: that many tokens, one at a time.
        /// </summary>
        private static StepResult Look(ResolutionContext ctx, Frame frame, PlayerState owner, JObject node)
        {
            var planets = PlanetsFor(ctx, owner, node["where"]!);
            bool all = node["count"] is JObject o && o["all"] != null;
            if (all)
            {
                if (frame.Step == 0)
                {
                    frame.Step = 1;
                    var withTokens = new List<string>();
                    foreach (var p in planets)
                    {
                        if (Facedown(ctx.State, p, frame).Count > 0) withTokens.Add(p);
                    }
                    if (withTokens.Count == 0) return Fail(ctx);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_planet", withTokens, "planet");
                }
                foreach (var t in Facedown(ctx.State, frame.Get("planet"), frame))
                {
                    ctx.Events.Add(new ContactLookedEvent(owner.Id, t.Id));
                }
                return StepResult.Done();
            }
            if (frame.Step == 0)
            {
                frame.Step = 1;
                frame.Set("left", node["count"] != null ? Selectors.Amount(ctx.Content, owner, node["count"]!) : 1);
            }
            if (frame.Vars.TryGetValue("pick", out var pick))
            {
                frame.Vars.Remove("pick");
                ctx.Events.Add(new ContactLookedEvent(owner.Id, pick));
                frame.Set("seen", frame.Vars.TryGetValue("seen", out var seen) && seen.Length > 0 ? seen + "," + pick : pick);
                frame.Set("left", frame.GetInt("left") - 1);
            }
            if (frame.GetInt("left") <= 0) return StepResult.Done();
            var options = new List<string>();
            foreach (var p in planets)
            {
                foreach (var t in Facedown(ctx.State, p, frame)) options.Add(t.Id);
            }
            if (options.Count == 0) return StepResult.Done();
            return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.look_at_contact", options, "pick");
        }

        private static List<ContactToken> Facedown(GameState state, string planet, Frame frame)
        {
            var seen = frame.Vars.TryGetValue("seen", out var s) ? new List<string>(s.Split(',')) : new List<string>();
            var list = new List<ContactToken>();
            foreach (var t in Contacts.OnPlanet(state, planet))
            {
                if (!t.FaceUp && !seen.Contains(t.Id)) list.Add(t);
            }
            return list;
        }

        /// <summary>Planets a "where" selector names for contact effects.</summary>
        internal static List<string> PlanetsFor(ResolutionContext ctx, PlayerState owner, JToken where)
        {
            var map = ctx.Content.Map;
            var result = new List<string>();
            if (where is JObject o && o["planet_where"] is JObject pw)
            {
                var cond = (JObject)pw["cond"]!;
                foreach (var planet in Contacts.Planets(map))
                {
                    if (PlanetHolds(ctx, owner, planet, cond)) result.Add(planet);
                }
                return result;
            }
            if (where.Type == JTokenType.String && (string)where! == "any_space") return Contacts.Planets(map);
            foreach (var s in SpaceSelect.Options(ctx.Content, ctx.State, owner, where))
            {
                if (map[s].IsPlanet) result.Add(s);
            }
            return result;
        }

        /// <summary>Conditions about a planet: "within 3 spaces of the Hutt patrol", "a planet of a faction you are {REP_POS} with".</summary>
        private static bool PlanetHolds(ResolutionContext ctx, PlayerState owner, string planet, JObject cond)
        {
            switch ((string)cond["cond"]!)
            {
                case "within_patrol":
                {
                    var patrol = Patrols.Of(ctx.State, Patrols.Parse((string)cond["faction"]!));
                    int d = ctx.Content.Map.Distance(patrol.SpaceId, planet);
                    return d >= 0 && d <= (int)cond["distance"]!;
                }
                case "rep" when (string?)cond["faction"] == "planet":
                {
                    var f = ctx.Content.Map[planet].Faction;
                    if (f == null) return false;
                    var states = ((JArray)cond["in"]!).ToObject<List<string>>()!;
                    return states.Contains(Conditions.RepId(owner.Reputation.Get(f.Value)));
                }
                default:
                    throw new NotSupportedException("Planet condition '" + (string)cond["cond"]! + "' is not implemented yet.");
            }
        }

        /// <summary>
        /// Takodana-3, Takodana-4: "Move a (facedown) contact from any planet to an empty contact space on your
        /// planet. If there are no empty contact spaces, exchange that contact with a contact on your planet."
        /// </summary>
        private static StepResult Move(ResolutionContext ctx, Frame frame, PlayerState owner, bool facedownOnly)
        {
            var state = ctx.State;
            var here = owner.SpaceId;
            switch (frame.Step)
            {
                case 0:
                {
                    frame.Step = 1;
                    var options = new List<string>();
                    foreach (var planet in Contacts.Planets(ctx.Content.Map))
                    {
                        if (planet == here) continue;
                        foreach (var t in Contacts.OnPlanet(state, planet))
                        {
                            if (!facedownOnly || !t.FaceUp) options.Add(t.Id);
                        }
                    }
                    if (options.Count == 0 || !ctx.Content.Map[here].IsPlanet) return Fail(ctx);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_contact", options, "token");
                }
                case 1:
                {
                    var token = Contacts.Token(state, frame.Get("token"));
                    var empty = Contacts.EmptySlots(ctx.Content.Map, state, here);
                    if (empty.Count > 0)
                    {
                        Contacts.Place(state, token, here, empty[0], token.FaceUp, ctx.Events);
                        return StepResult.Done();
                    }
                    frame.Step = 2;
                    var options = new List<string>();
                    foreach (var t in Contacts.OnPlanet(state, here)) options.Add(t.Id);
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_contact", options, "other");
                }
                default:
                    Contacts.Exchange(Contacts.Token(state, frame.Get("token")), Contacts.Token(state, frame.Get("other")), ctx.Events);
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// Takodana-9 (secret, R-24.4): "Choose 2 facedown contact tokens on different planets. Secretly look at those
    /// tokens, and then you may exchange them."
    /// </summary>
    internal sealed class LookAndExchangeHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.look_and_exchange";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            var state = ctx.State;
            switch (frame.Step)
            {
                case 0:
                case 1:
                {
                    var firstPlanet = frame.Vars.TryGetValue("first", out var f) ? Contacts.Token(state, f).SpaceId : "";
                    var options = new List<string>();
                    foreach (var t in state.Contacts)
                    {
                        if (t.OnMap && !t.FaceUp && (firstPlanet.Length == 0 || t.SpaceId != firstPlanet)) options.Add(t.Id);
                    }
                    if (options.Count == 0)
                    {
                        ctx.Return("failed", "true");
                        return StepResult.Done();
                    }
                    var var = frame.Step == 0 ? "first" : "second";
                    frame.Step++;
                    return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.look_at_contact", options, var);
                }
                case 2:
                    frame.Step = 3;
                    ctx.Events.Add(new ContactLookedEvent(owner.Id, frame.Get("first")));
                    ctx.Events.Add(new ContactLookedEvent(owner.Id, frame.Get("second")));
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, owner.Id, "effects.may",
                        new[] { new DecisionOption("yes", "yes"), new DecisionOption("no", "no") }, "answer"));
                default:
                    if (frame.Get("answer") == "yes")
                    {
                        Contacts.Exchange(Contacts.Token(state, frame.Get("first")), Contacts.Token(state, frame.Get("second")), ctx.Events);
                    }
                    return StepResult.Done();
            }
        }
    }

    /// <summary>
    /// Tatooine-2 (secret): "deliver a contact token on 1 of your bounty cards for its capture reward. You may do
    /// this regardless of the reward's specified planet." (bounty.deliver, anywhere).
    /// </summary>
    internal sealed class BountyDeliverEffectHandler : IResolutionHandler
    {
        public const string HandlerId = "effects.bounty_deliver";

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var node = (JObject)EffectHandlers.Token(ctx, frame);
            var owner = ctx.State.FindPlayer(frame.Get("owner"))!;
            if (frame.Step == 0)
            {
                frame.Step = 1;
                bool anywhere = (bool?)node["anywhere"] == true;
                var options = new List<string>();
                foreach (var a in owner.Assets)
                {
                    var token = a.Kind == "bounty" ? Contacts.CapturedOn(ctx.State, a.CardId) : null;
                    if (token == null || token.CapturedThisTurn) continue;
                    if (!anywhere && (string?)ctx.Content.Card(a.CardId)["capture_destination"] != owner.SpaceId) continue;
                    options.Add(a.CardId);
                }
                if (options.Count == 0)
                {
                    ctx.Return("failed", "true");
                    frame.Step = 9;
                    return StepResult.Done();
                }
                return SpaceSelect.AskOrSet(ctx, frame, owner.Id, "effects.choose_bounty", options, "bounty");
            }
            if (frame.Step == 1)
            {
                frame.Step = 2;
                return StepResult.Call(DeliverBountyHandler.For(owner.Id, frame.Get("bounty")));
            }
            return StepResult.Done();
        }
    }
}
