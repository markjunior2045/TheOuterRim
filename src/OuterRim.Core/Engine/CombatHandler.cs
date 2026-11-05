#nullable enable
using System.Collections.Generic;
using System.Globalization;
using OuterRim.Core.Dice;
using OuterRim.Core.Effects;
using OuterRim.Core.Model;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// The five combat steps (R-7.10 to R-7.14) as a resolution frame. Variables are documented on
    /// <see cref="Combat.Handler"/>. Steps marked "window" are where combat abilities plug in when the
    /// effect engine is built (PLAN T2.3): start of combat (R-7.18), value changes and rerolls
    /// (R-7.10, R-7.12), result changes (R-7.11).
    /// </summary>
    internal sealed class CombatHandler : IResolutionHandler
    {
        public string Id => Combat.Handler;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var state = ctx.State;
            var content = ctx.Content;
            var type = Combat.ParseType(frame.Get("type"));
            var attacker = state.FindPlayer(frame.Get("attacker"))!;
            string defenderKind = frame.Get("defender_kind");
            PlayerState? defenderPlayer = defenderKind == "player" ? state.FindPlayer(frame.Get("defender")) : null;

            switch (frame.Step)
            {
                case 0:
                {
                    if (defenderPlayer != null && defenderPlayer.Defeated)
                    {
                        // R-8.12: a defeated player cannot be attacked.
                        ctx.Events.Add(new CombatResolvedEvent("none", 0, 0));
                        ctx.Return("winner", "none");
                        return StepResult.Done();
                    }
                    int attackerValue = Combat.Value(content, state, attacker, type);
                    string defenderValue = defenderPlayer != null
                        ? Combat.Value(content, state, defenderPlayer, type).ToString(CultureInfo.InvariantCulture)
                        : frame.Get("defender_value");
                    frame.Set("attacker_value", attackerValue);
                    frame.Set("defender_value", defenderValue);
                    ctx.Events.Add(new CombatStartedEvent(frame.Get("type"), attacker.Id, defenderKind, frame.Get("defender"),
                        attackerValue, defenderValue));
                    frame.Step = 1;
                    return StepResult.Continue(); // window: "at the start of combat" (R-7.18)
                }
                case 1:
                {
                    // Step 1: the attacker rolls dice equal to their combat value (R-7.10), less any
                    // "your opponent rolls fewer dice" of the defender.
                    int fewer = defenderPlayer != null ? Modifiers.OpponentFewer(content, state, defenderPlayer, frame.Get("type")) : 0;
                    var faces = DiceRoller.Roll(state.Random, System.Math.Max(0, frame.GetInt("attacker_value") - fewer));
                    frame.Set("attacker_faces", DicePool.Encode(faces));
                    frame.Step = 2;
                    // Rerolls and result changes: the attacker (who rolled) first, then the defender (R-7.10, R-7.11).
                    var players = new List<string> { attacker.Id };
                    if (defenderPlayer != null) players.Add(defenderPlayer.Id);
                    return StepResult.Call(DiceWindows.For(state, "attacker_faces", Scope(type), attacker.Id, players));
                }
                case 2:
                {
                    // Step 2: attacker damage (R-7.11).
                    var faces = DicePool.Effective(DicePool.Decode(frame.Get("attacker_faces")));
                    int damage = Combat.DamageOf(faces);
                    frame.Set("attacker_damage", damage);
                    ctx.Events.Add(new CombatRolledEvent("attacker", attacker.Id, Combat.PlayerLabel(content, attacker), faces, damage));
                    frame.Step = 3;
                    return StepResult.Continue(); // window: result changes, roller first
                }
                case 3:
                {
                    // Step 3: defender rolls (R-7.12); a level-4 patrol rolls nothing (R-11.6).
                    if (frame.Get("defender_value") == Combat.Infinite)
                    {
                        frame.Set("defender_faces", "");
                        frame.Set("defender_damage", 0);
                        frame.Step = 5;
                        return StepResult.Continue();
                    }
                    int fewer = Modifiers.OpponentFewer(content, state, attacker, frame.Get("type"));
                    int count = System.Math.Max(0, int.Parse(frame.Get("defender_value"), CultureInfo.InvariantCulture) - fewer);
                    var faces = DiceRoller.Roll(state.Random, count);
                    frame.Set("defender_faces", DicePool.Encode(faces));
                    frame.Step = 4;
                    // The defender (if a player) first, then the attacker (R-7.12).
                    var players = new List<string>();
                    if (defenderPlayer != null) players.Add(defenderPlayer.Id);
                    players.Add(attacker.Id);
                    return StepResult.Call(DiceWindows.For(state, "defender_faces", Scope(type), defenderPlayer?.Id ?? "", players));
                }
                case 4:
                {
                    var faces = DicePool.Effective(DicePool.Decode(frame.Get("defender_faces")));
                    int damage = Combat.DamageOf(faces);
                    frame.Set("defender_damage", damage);
                    // A player rolls their own dice; for a patrol, contact or card enemy the game rolls
                    // (digital adaptation of R-7.9 and R-7.12, DECISIONS §4.2).
                    ctx.Events.Add(new CombatRolledEvent("defender", defenderPlayer?.Id, DefenderLabel(ctx, frame, defenderPlayer),
                        faces, damage));
                    frame.Step = 5;
                    return StepResult.Continue(); // window: result changes, roller first
                }
                case 5:
                {
                    // Step 4: the winner; a tie goes to the attacker (R-7.13); nobody beats a level-4 patrol (R-11.6).
                    int a = frame.GetInt("attacker_damage");
                    int d = frame.GetInt("defender_damage");
                    bool attackerWins = frame.Get("defender_value") != Combat.Infinite && Combat.AttackerWins(a, d);
                    string winner = attackerWins ? "attacker" : "defender";
                    frame.Set("winner", winner);
                    ctx.Events.Add(new CombatResolvedEvent(winner, a, d));
                    frame.Step = 6;
                    if (frame.Vars.TryGetValue("outcome", out var outcome) && outcome.Length > 0)
                    {
                        // R-7.13, R-8.15: the effect of winning resolves before damage is suffered.
                        var effect = new Frame(outcome);
                        foreach (var kv in frame.Vars)
                        {
                            if (kv.Key.StartsWith("ctx.", System.StringComparison.Ordinal))
                            {
                                effect.Set(kv.Key, kv.Value);
                            }
                        }
                        effect.Set("winner", winner);
                        effect.Set("attacker", attacker.Id);
                        effect.Set("defender", frame.Get("defender"));
                        return StepResult.Call(effect);
                    }
                    return StepResult.Continue();
                }
                default:
                {
                    // Step 5: each side suffers the opponent's damage; only players take damage (R-7.14).
                    int a = frame.GetInt("attacker_damage");
                    int d = frame.GetInt("defender_damage");
                    if (frame.Get("defender_value") == Combat.Infinite)
                    {
                        // R-11.6: enough ship damage to be defeated.
                        d = Combat.Hull(content, state, attacker);
                    }
                    Hurt(ctx, attacker, type, d);
                    if (defenderPlayer != null)
                    {
                        Hurt(ctx, defenderPlayer, type, a);
                    }
                    ctx.Return("winner", frame.Get("winner"));
                    ctx.Return("attacker_damage", a.ToString(CultureInfo.InvariantCulture));
                    ctx.Return("defender_damage", d.ToString(CultureInfo.InvariantCulture));
                    return StepResult.Done();
                }
            }
        }

        private static string DefenderLabel(ResolutionContext ctx, Frame frame, PlayerState? defenderPlayer)
        {
            if (defenderPlayer != null)
            {
                return Combat.PlayerLabel(ctx.Content, defenderPlayer);
            }
            var defender = frame.Get("defender");
            if (frame.Get("defender_kind") == "patrol")
            {
                foreach (var patrol in ctx.State.Patrols)
                {
                    if (patrol.Faction.ToString().ToLowerInvariant() == defender)
                    {
                        return Combat.GameLabel(Combat.PatrolName(patrol.Faction, patrol.Level));
                    }
                }
            }
            return Combat.GameLabel(defender);
        }

        private static void Hurt(ResolutionContext ctx, PlayerState player, CombatType type, int amount)
        {
            PlayerRules.SufferDamage(ctx.Content, ctx.State, player, type, amount, ctx.Events);
        }

        private static string Scope(CombatType type)
        {
            return type == CombatType.Ground ? "ground_combat" : "ship_combat";
        }
    }
}
