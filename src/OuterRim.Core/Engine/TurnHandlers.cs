#nullable enable
namespace OuterRim.Core.Engine
{
    /// <summary>Resolution handlers that run the encounter step and end the turn (R-4.12, R-4.4).</summary>
    internal static class TurnHandlers
    {
        public const string EncounterStep = "turn.encounter_step";
        public const string AbilityStep = "turn.ability_step";
        public const string ChooseStartingReputation = "setup.starting_reputation";
        public const string EncounterSpace = "encounter.space";
        public const string EncounterPatrol = Rules.Patrols.EncounterHandler;

        public static void Register()
        {
            ResolutionHandlers.Register(new EncounterStepHandler());
            ResolutionHandlers.Register(new AbilityStepHandler());
            ResolutionHandlers.Register(new StartingReputationHandler());
            EffectHandlers.Register();
            ResolutionHandlers.Register(new EncounterSpaceStub());
            ResolutionHandlers.Register(new PatrolEncounterHandler());
            ResolutionHandlers.Register(new PatrolOutcomeHandler());
            ResolutionHandlers.Register(new PatrolMoveHandler());
            ResolutionHandlers.Register(new GainAssetHandler());
            ResolutionHandlers.Register(new EnforceSlotsHandler());
            ResolutionHandlers.Register(new BuyShipHandler());
            ResolutionHandlers.Register(new SkillTestHandler());
            ResolutionHandlers.Register(new CombatHandler());
        }

        /// <summary>Lando's setup: positive reputation "with the faction of your choice" (R-3.15.c).</summary>
        private sealed class StartingReputationHandler : IResolutionHandler
        {
            public string Id => ChooseStartingReputation;

            public StepResult Step(ResolutionContext context, Frame frame)
            {
                var player = context.State.FindPlayer(frame.Get("player"))!;
                if (frame.Step == 0)
                {
                    frame.Step = 1;
                    var options = new System.Collections.Generic.List<DecisionOption>();
                    foreach (var f in new[] { "rebel", "imperial", "hutt", "syndicate" })
                    {
                        options.Add(new DecisionOption(f, "faction." + f));
                    }
                    return StepResult.Await(context.Ask(DecisionKind.ChooseOption, player.Id, "setup.starting_reputation", options, "faction"));
                }
                Rules.PlayerRules.SetReputation(player, Rules.Patrols.Parse(frame.Get("faction")), Model.Reputation.Positive, context.Events);
                return StepResult.Done();
            }
        }

        /// <summary>Runs a step ability, then moves on: a planning ability leads to the action step (R-4.7), an encounter ability ends the turn (R-4.16).</summary>
        private sealed class AbilityStepHandler : IResolutionHandler
        {
            public string Id => AbilityStep;

            public StepResult Step(ResolutionContext context, Frame frame)
            {
                if (frame.Step == 0)
                {
                    frame.Step = 1;
                    var use = new Frame(EffectHandlers.Ability);
                    use.Set("owner", frame.Get("owner"));
                    use.Set("card", frame.Get("card"));
                    use.Set("path", frame.Get("path"));
                    return StepResult.Call(use);
                }
                switch (frame.Get("step"))
                {
                    case "planning":
                        GameEngine.StartStep(context.State, Model.TurnStep.Action, context.Events);
                        break;
                    case "encounter":
                        context.Events.Add(new EncounterResolvedEvent(context.State.CurrentPlayerId, "ability", frame.Get("card")));
                        GameEngine.EndTurn(context.State, context.Events);
                        break;
                }
                return StepResult.Done();
            }
        }

        /// <summary>Runs the chosen encounter, then ends the turn once it has fully resolved.</summary>
        private sealed class EncounterStepHandler : IResolutionHandler
        {
            public string Id => EncounterStep;

            public StepResult Step(ResolutionContext context, Frame frame)
            {
                switch (frame.Step)
                {
                    case 0:
                        frame.Step = 1;
                        var encounter = new Frame(frame.Get("handler"));
                        encounter.Set("target", frame.Get("target"));
                        return StepResult.Call(encounter);
                    default:
                        GameEngine.EndTurn(context.State, context.Events);
                        return StepResult.Done();
                }
            }
        }

        /// <summary>
        /// STUB until encounter decks are built (PLAN T2.6): drawing and resolving an encounter card.
        /// It only records that the encounter happened.
        /// </summary>
        private sealed class EncounterSpaceStub : IResolutionHandler
        {
            public string Id => EncounterSpace;

            public StepResult Step(ResolutionContext context, Frame frame)
            {
                context.Events.Add(new EncounterResolvedEvent(context.State.CurrentPlayerId, "space", ""));
                return StepResult.Done();
            }
        }

    }
}
