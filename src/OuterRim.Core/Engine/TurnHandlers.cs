#nullable enable
namespace OuterRim.Core.Engine
{
    /// <summary>Resolution handlers that run the encounter step and end the turn (R-4.12, R-4.4).</summary>
    internal static class TurnHandlers
    {
        public const string EncounterStep = "turn.encounter_step";
        public const string EncounterSpace = "encounter.space";
        public const string EncounterPatrol = Rules.Patrols.EncounterHandler;

        public static void Register()
        {
            ResolutionHandlers.Register(new EncounterStepHandler());
            ResolutionHandlers.Register(new EncounterSpaceStub());
            ResolutionHandlers.Register(new PatrolEncounterHandler());
            ResolutionHandlers.Register(new PatrolOutcomeHandler());
            ResolutionHandlers.Register(new PatrolMoveHandler());
            ResolutionHandlers.Register(new SkillTestHandler());
            ResolutionHandlers.Register(new CombatHandler());
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
