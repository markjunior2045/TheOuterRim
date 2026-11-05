#nullable enable
using System.Collections.Generic;
using System.Globalization;
using OuterRim.Core.Dice;
using OuterRim.Core.Rules;

namespace OuterRim.Core.Engine
{
    /// <summary>
    /// Resolves one skill test (R-6.9) as a resolution frame, so it can pause for decisions.
    ///
    /// Frame variables in: "player" (id), "skills" (one or more skill ids joined by ','; with several,
    /// the testing player chooses which to test), optional "granted" (extra instances, R-6.8).
    /// Returned to the caller: "ret.passed" ("true" or "false") and "ret.skill".
    ///
    /// Steps 1 and 3 are where abilities used "before a skill test" and dice-modification windows
    /// ("after you roll dice for a skill test") plug in when the effect engine is built (PLAN T2.3).
    /// </summary>
    internal sealed class SkillTestHandler : IResolutionHandler
    {
        public const string HandlerId = SkillTests.Handler;

        public string Id => HandlerId;

        public StepResult Step(ResolutionContext ctx, Frame frame)
        {
            var player = ctx.State.FindPlayer(frame.Get("player"))!;
            switch (frame.Step)
            {
                case 0:
                {
                    // "Test X or Y": the testing player picks the skill.
                    var skills = frame.Get("skills").Split(',');
                    frame.Step = 1;
                    if (skills.Length == 1)
                    {
                        frame.Set("skill", skills[0]);
                        return StepResult.Continue();
                    }
                    var options = new List<DecisionOption>();
                    foreach (var s in skills)
                    {
                        options.Add(new DecisionOption(s, "skill." + s));
                    }
                    return StepResult.Await(ctx.Ask(DecisionKind.ChooseOption, player.Id, "rules.choose_skill", options, "skill"));
                }
                case 1:
                    // Before the test: abilities that grant a skill for this test (T2.3).
                    frame.Step = 2;
                    return StepResult.Continue();
                case 2:
                {
                    var faces = DiceRoller.Roll(ctx.State.Random, SkillTests.Dice);
                    frame.Set("faces", Faces(faces));
                    frame.Step = 3;
                    return StepResult.Continue();
                }
                case 3:
                    // After rolling: rerolls and result changes (T2.3).
                    frame.Step = 4;
                    return StepResult.Continue();
                default:
                {
                    var skill = SkillTests.Parse(frame.Get("skill"));
                    int granted = frame.Vars.ContainsKey("granted") ? frame.GetInt("granted") : 0;
                    int instances = SkillTests.Instances(ctx.Content, player, skill, granted);
                    var faces = ParseFaces(frame.Get("faces"));
                    bool passed = SkillTests.Passes(SkillTests.Level(instances), faces);
                    ctx.Events.Add(new SkillTestRolledEvent(player.Id, Combat.PlayerLabel(ctx.Content, player),
                        SkillTests.Id(skill), instances, faces, passed));
                    ctx.Return("passed", passed ? "true" : "false");
                    ctx.Return("skill", SkillTests.Id(skill));
                    return StepResult.Done();
                }
            }
        }

        private static string Faces(IEnumerable<Model.DieFace> faces)
        {
            var parts = new List<string>();
            foreach (var f in faces)
            {
                parts.Add(((int)f).ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", parts);
        }

        private static List<Model.DieFace> ParseFaces(string text)
        {
            var result = new List<Model.DieFace>();
            foreach (var p in text.Split(','))
            {
                result.Add((Model.DieFace)int.Parse(p, CultureInfo.InvariantCulture));
            }
            return result;
        }
    }
}
