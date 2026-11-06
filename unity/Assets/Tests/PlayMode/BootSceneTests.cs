using System.Collections;
using NUnit.Framework;
using OuterRim.Core.Model;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace OuterRim.Client.Tests
{
    /// <summary>The boot scene starts a seeded game through the session shell and shows its state (PLAN T0.4).</summary>
    public class BootSceneTests
    {
        [UnityTest]
        public IEnumerator TheBootScene_StartsASeededGame_AndShowsTheActingPlayerAndStep()
        {
            SceneManager.LoadScene("Boot");
            BootView view = null;
            for (int frame = 0; frame < 300 && (view == null || view.Session == null); frame++)
            {
                yield return null;
                view = Object.FindAnyObjectByType<BootView>();
            }
            Assert.IsNotNull(view, "No BootView in the Boot scene.");
            Assert.IsNotNull(view.Session, "The session did not start.");

            var state = view.Session.State;
            Assert.AreEqual(GamePhase.Playing, state.Phase);
            Assert.AreEqual(1, state.Round);
            Assert.IsNull(state.Pending);

            var label = view.GetComponent<UIDocument>().rootVisualElement.Q<Label>(BootView.StatusLabel);
            Assert.IsNotNull(label);
            StringAssert.Contains("Round 1", label.text);
            StringAssert.Contains(state.FindPlayer(state.CurrentPlayerId).Name, label.text);
            StringAssert.Contains("Planning", label.text);
        }
    }
}
