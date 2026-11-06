#nullable enable
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Rules that hold "at all times", applied once an operation has finished (after every command), for
    /// each player from the current one in turn order:
    /// R-8.4: a character or ship never holds more damage than its health or hull, so tokens above a value
    /// that went down (a health bonus lost with its card) are removed; R-8.2, R-8.3: damage equal to the
    /// value defeats the player (R-25.24, owner decision); R-14.18: a player who lacks slots for all their
    /// assets discards assets of their choice. It repeats until nothing changes, since each step can undo
    /// another (discarding an ARMOR lowers health).
    /// </summary>
    internal sealed class SettleHandler : IResolutionHandler
    {
        public const string HandlerId = "rules.settle";

        public string Id => HandlerId;

        /// <summary>Whether any player breaks one of these rules now.</summary>
        public static bool Needed(Content.GameContent content, GameState state)
        {
            foreach (var p in state.Players)
            {
                if (Excess(content, state, p) || (!p.Defeated && AtLimit(content, state, p)) || !Assets.AllFit(content, p))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Excess(Content.GameContent content, GameState state, PlayerState p)
        {
            return p.CharacterDamage > Health(content, state, p) || p.ShipDamage > Combat.Hull(content, state, p);
        }

        private static bool AtLimit(Content.GameContent content, GameState state, PlayerState p)
        {
            return p.CharacterDamage >= Health(content, state, p) || p.ShipDamage >= Combat.Hull(content, state, p);
        }

        /// <summary>Health, or no limit for a player placed without a character (test scenarios).</summary>
        private static int Health(Content.GameContent content, GameState state, PlayerState p)
        {
            return content.HasCharacter(p.CharacterId) ? Combat.Health(content, state, p) : int.MaxValue;
        }

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            int start = state.SeatOf(state.CurrentPlayerId);
            for (int i = 0; i < state.Players.Count; i++)
            {
                var p = state.Players[(start + i) % state.Players.Count];
                if (Excess(ctx.Content, state, p))
                {
                    int health = Health(ctx.Content, state, p), hull = Combat.Hull(ctx.Content, state, p);
                    int character = System.Math.Max(0, p.CharacterDamage - health), ship = System.Math.Max(0, p.ShipDamage - hull);
                    p.CharacterDamage -= character;
                    p.ShipDamage -= ship;
                    ctx.Events.Add(new ExcessDamageRemovedEvent(p.Id, character, ship));
                }
                if (!p.Defeated && AtLimit(ctx.Content, state, p))
                {
                    PlayerRules.BecomeDefeated(ctx.Content, state, p, ctx.Events);
                    return StepResult.Call(TriggersHandler.For("defeated", p.Id)); // then check again
                }
                if (!Assets.AllFit(ctx.Content, p))
                {
                    var fit = new Frame(Assets.FitHandler);
                    fit.Set("player", p.Id);
                    return StepResult.Call(fit); // then check again
                }
            }
            return StepResult.Done();
        }
    }
}
