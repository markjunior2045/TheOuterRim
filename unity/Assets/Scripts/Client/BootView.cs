using OuterRim.Core.Engine;
using OuterRim.Core.Model;
using OuterRim.Core.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace OuterRim.Client
{
    /// <summary>
    /// First screen of the prototype (PLAN T0.4): starts a seeded game through <see cref="GameSession"/> and shows
    /// the round, the acting player and the step as text. Replaced by the game screen from T3.6 on.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BootView : MonoBehaviour
    {
        public const string StatusLabel = "status";

        [SerializeField] private ulong seed = 1;
        [SerializeField] private int players = 2;

        private Label _status;

        public GameSession Session { get; private set; }

        public string StatusText => _status != null ? _status.text : "";

        private void Start()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _status = new Label { name = StatusLabel };
            _status.style.fontSize = 28;
            _status.style.color = Color.white;
            _status.style.marginLeft = 24;
            _status.style.marginTop = 24;
            root.Add(_status);

            var content = ContentLocator.Base;
            var options = new SimulationOptions { Seed = seed, Players = players };
            var setup = GameRunner.Setup(content, options, new RandomBot(seed).Random);
            Session = GameSession.NewGame(content, setup, out _);
            Session.EventsReceived += _ => Refresh();
            // Setup choices before the first turn (Lando's starting reputation, R-3.15.c) are taken by a bot here;
            // the new-game screen asks the players (PLAN T3.9).
            var bot = new RandomBot(seed);
            while (Session.State.Pending != null)
            {
                foreach (var command in bot.Candidates(content, Session.State))
                {
                    if (Session.Send(command).Accepted) break;
                }
            }
            Refresh();
        }

        private void Refresh()
        {
            var state = Session.State;
            var player = state.FindPlayer(Session.Actor);
            string who = player.Name + " — " + Session.Content.Name(player.CharacterId);
            _status.text = "Round " + state.Round + " · " + who + " · " + state.Step
                + (state.Phase == GamePhase.Ended ? " · game over" : "");
        }
    }
}
