using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions.Scenarios
{
    /// <summary>
    /// Base for per-artifact scenario state machines: records decisions against the artifact id and toggles the
    /// world-space panels that make each state observable. Outcomes are persistent for the rest of the session.
    /// </summary>
    public abstract class ScenarioController : MonoBehaviour
    {
        [SerializeField, Tooltip("Scenario artifact id this controller reports decisions for.")]
        private string _objectId;

        public string ObjectId => _objectId;

        /// <summary>True once the participant has made the scenario's final decision.</summary>
        public abstract bool IsResolved { get; }

        /// <summary>Returns the scenario to its initial state (demo/facilitator reset). Records no decision.</summary>
        public abstract void ResetScenario();

        protected void Record(string eventName, string choice, DecisionOutcome outcome)
        {
            DecisionLog.Record(eventName, _objectId, choice, outcome);
        }

        protected static void Show(GameObject target, bool visible)
        {
            if (target != null && target.activeSelf != visible)
            {
                target.SetActive(visible);
            }
        }

        protected static void Bind(XRButton button, System.Action<XRButton> handler, bool subscribe)
        {
            if (button == null)
            {
                return;
            }
            button.WhenActivated -= handler;
            if (subscribe)
            {
                button.WhenActivated += handler;
            }
        }

        public void InjectObjectId(string objectId)
        {
            _objectId = objectId;
        }
    }
}
