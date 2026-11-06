#nullable enable
using System.Collections.Generic;
using OuterRim.Core.Content;
using OuterRim.Core.Effects;
using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Random;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Simulation
{
    /// <summary>
    /// A player with no strategy (PLAN T3.1). It lists plausible commands for whoever must act, in random
    /// order; the caller tries them until the engine accepts one. The engine is the only judge of what is
    /// legal, so the bot never needs to know the rules, and a rejected command changes nothing.
    /// It draws from its own generator, so the game's random numbers depend only on the game seed and
    /// the commands played.
    /// </summary>
    public sealed class RandomBot
    {
        /// <summary>Random walks offered as moves in each planning step.</summary>
        private const int MoveCandidates = 3;

        private readonly GameRandom _rng;

        public RandomBot(ulong seed)
        {
            _rng = new GameRandom(seed, 0xB07UL);
        }

        public GameRandom Random => _rng;

        /// <summary>
        /// The player who must act now: the one the oldest open credit request is for (the bot answers requests
        /// before anything else), else the one a decision waits on, else the current player.
        /// </summary>
        public static string Actor(GameState state)
        {
            if (state.CreditRequests.Count > 0) return state.CreditRequests[0].ToId;
            return state.Pending != null ? state.Pending.PlayerId : state.CurrentPlayerId;
        }

        /// <summary>Commands for <see cref="Actor"/>, shuffled. Some may be illegal; the engine rejects those.</summary>
        public List<Command> Candidates(GameContent content, GameState state)
        {
            var list = new List<Command>();
            var actor = Actor(state);
            var player = state.FindPlayer(actor)!;
            if (state.CreditRequests.Count > 0)
            {
                // R-9.6: the player asked accepts or refuses; now and then the requester withdraws it first, or the
                // 15 seconds run out (the interface's clock, played here as a command).
                var open = state.CreditRequests[0];
                list.Add(new AnswerCreditRequestCommand(open.ToId, open.Id, true));
                list.Add(new AnswerCreditRequestCommand(open.ToId, open.Id, false));
                if (_rng.NextInt(4) == 0) list.Add(new CancelCreditRequestCommand(open.FromId, open.Id));
                if (_rng.NextInt(4) == 0) list.Add(new TimeOutCreditRequestCommand(open.ToId, open.Id));
                _rng.Shuffle(list);
                return list;
            }
            if (state.Pending != null)
            {
                Answers(state.Pending, list);
            }
            else
            {
                switch (state.Step)
                {
                    case TurnStep.Planning:
                        Planning(content, state, player, list);
                        break;
                    case TurnStep.Action:
                        Action(player, list);
                        Trade(state, player, list);
                        break;
                    default:
                        Encounter(state, player, list);
                        break;
                }
                Abilities(content, player, list);
            }
            CreditRequest(state, player, list);
            _rng.Shuffle(list);
            return list;
        }

        private void Answers(PendingDecision d, List<Command> list)
        {
            switch (d.Kind)
            {
                case DecisionKind.ChooseOrder:
                    var order = new List<string>();
                    foreach (var o in d.Options) order.Add(o.Id);
                    _rng.Shuffle(order);
                    list.Add(new ChooseOrderCommand(d.PlayerId, d.Id, order));
                    break;
                case DecisionKind.ResolveTie:
                    foreach (var o in d.Options) list.Add(new ResolveTieCommand(d.PlayerId, d.Id, o.Id));
                    break;
                default:
                    foreach (var o in d.Options) list.Add(new ChooseOptionCommand(d.PlayerId, d.Id, o.Id));
                    break;
            }
        }

        /// <summary>R-4.6: gain credits, recover damage, or move along a random path within the hyperdrive.</summary>
        private void Planning(GameContent content, GameState state, PlayerState player, List<Command> list)
        {
            list.Add(new GainCreditsCommand(player.Id));
            list.Add(new RecoverDamageCommand(player.Id));
            int hyperdrive = GameEngine.Hyperdrive(content, state, player);
            for (int i = 0; i < MoveCandidates; i++)
            {
                list.Add(new MoveCommand(player.Id, RandomWalk(content, state, player, 1 + _rng.NextInt(System.Math.Max(1, hyperdrive)))));
            }
        }

        /// <summary>A path of adjacent spaces that stops where movement must stop (R-5.11, R-5.18, R-5.20).</summary>
        private List<string> RandomWalk(GameContent content, GameState state, PlayerState player, int length)
        {
            var path = new List<string>();
            var at = player.SpaceId;
            for (int i = 0; i < length; i++)
            {
                var next = new List<string>(content.Map.Neighbours(at));
                next.Sort(System.StringComparer.Ordinal); // same walk on every runtime
                if (next.Count == 0) break;
                at = next[_rng.NextInt(next.Count)];
                path.Add(at);
                if (GameEngine.StopsMovement(state, player, at)) break;
            }
            return path;
        }

        /// <summary>R-4.9: market discards and purchases on every deck, deliveries, and ending the step.</summary>
        private void Action(PlayerState player, List<Command> list)
        {
            list.Add(new EndActionStepCommand(player.Id));
            foreach (var deck in Market.Decks)
            {
                list.Add(new MarketDiscardCommand(player.Id, deck));
                list.Add(new MarketBuyCommand(player.Id, deck));
                var barter = RandomSubset(player.Assets);
                if (barter.Count > 0) list.Add(new MarketBuyCommand(player.Id, deck, barter));
            }
            // R-16.1, R-16.4: cargo and captured contacts (named by their bounty card).
            var deliverable = new List<string>();
            foreach (var a in player.Assets)
            {
                if (a.Kind == "cargo" || a.Kind == "bounty")
                {
                    deliverable.Add(a.CardId);
                    list.Add(new DeliverCommand(player.Id, new[] { a.CardId }));
                }
            }
            if (deliverable.Count > 1)
            {
                _rng.Shuffle(deliverable);
                list.Add(new DeliverCommand(player.Id, deliverable));
            }
        }

        /// <summary>R-16.8: an offer to each player in the space, of random credits and cards both ways.</summary>
        private void Trade(GameState state, PlayerState player, List<Command> list)
        {
            foreach (var partner in state.Players)
            {
                if (partner.Id == player.Id || partner.SpaceId != player.SpaceId) continue;
                list.Add(new OfferTradeCommand(player.Id, partner.Id, RandomCredits(player), RandomSubset(player.Assets),
                    RandomCredits(partner), RandomSubset(partner.Assets)));
            }
        }

        /// <summary>
        /// R-9.6, R-16.15: now and then (rarely, so games still move on), a credit request to or from the current
        /// player, asking for credits or sending them, even while a decision is pending (R-16.16).
        /// </summary>
        private void CreditRequest(GameState state, PlayerState actor, List<Command> list)
        {
            if (state.Players.Count < 2 || _rng.NextInt(20) != 0) return;
            string to = state.CurrentPlayerId;
            if (to == actor.Id)
            {
                var others = state.Players.FindAll(p => p.Id != actor.Id);
                to = others[_rng.NextInt(others.Count)].Id;
            }
            bool ask = _rng.NextInt(2) == 0;
            var payer = ask ? state.FindPlayer(to)! : actor;
            if (payer.Credits < 1000) return;
            list.Add(new RequestCreditsCommand(actor.Id, to, ask ? CreditRequestKind.Ask : CreditRequestKind.Send,
                1000 * (1 + _rng.NextInt(payer.Credits / 1000))));
        }

        private int RandomCredits(PlayerState player)
        {
            return _rng.NextInt(3) == 0 ? 1000 * _rng.NextInt(player.Credits / 1000 + 1) : 0;
        }

        /// <summary>R-4.13: the space, a patrol, a contact, a job, a bounty against crew, or another player.</summary>
        private static void Encounter(GameState state, PlayerState player, List<Command> list)
        {
            list.Add(new EncounterCommand(player.Id, EncounterKind.Space));
            foreach (var p in state.PatrolsIn(player.SpaceId))
            {
                list.Add(new EncounterCommand(player.Id, EncounterKind.Patrol, Patrols.Id(p.Faction)));
            }
            foreach (var t in state.Contacts)
            {
                if (t.OnMap && t.SpaceId == player.SpaceId)
                {
                    list.Add(new EncounterCommand(player.Id, EncounterKind.Contact, t.Id));
                }
            }
            foreach (var a in player.Assets)
            {
                if (a.Kind == "job") list.Add(new EncounterCommand(player.Id, EncounterKind.Job, a.CardId));
                if (a.Kind == "bounty") list.Add(new EncounterCommand(player.Id, EncounterKind.BountyCrew, a.CardId));
            }
            foreach (var other in state.Players)
            {
                if (other.Id != player.Id && other.SpaceId == player.SpaceId)
                {
                    list.Add(new EncounterCommand(player.Id, EncounterKind.Player, other.Id));
                }
            }
        }

        /// <summary>Every "Planning:", "Action:" and "Encounter:" ability the player has, including step goals (R-4.7, R-4.11, R-4.16).</summary>
        private static void Abilities(GameContent content, PlayerState player, List<Command> list)
        {
            foreach (var a in AbilitySources.Of(content, player))
            {
                if (a.TimingType == "step" || Goals.StepOf(a) != null)
                {
                    list.Add(new UseAbilityCommand(player.Id, a.CardId, a.Path));
                }
            }
        }

        private List<string> RandomSubset(List<AssetState> assets)
        {
            var result = new List<string>();
            foreach (var a in assets)
            {
                if (_rng.NextInt(2) == 0) result.Add(a.CardId);
            }
            return result;
        }
    }
}
