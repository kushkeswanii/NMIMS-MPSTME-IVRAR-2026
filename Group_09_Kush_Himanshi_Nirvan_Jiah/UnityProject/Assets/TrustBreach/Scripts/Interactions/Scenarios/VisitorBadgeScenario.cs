using Oculus.Interaction;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions.Scenarios
{
    /// <summary>
    /// Picking up the spoofed visitor badge (grab or ray-grab) opens an enlarged inspection panel at reception with
    /// the badge details; the optional Verify Identity action checks it against the (fictional) visitor log.
    /// Grab/release behaviour of the badge itself is untouched: this only listens to its pointer events.
    /// </summary>
    public class VisitorBadgeScenario : ScenarioController
    {
        [SerializeField, Interface(typeof(IPointable)), Tooltip("The badge's Grabbable.")]
        private Object _badge;

        [SerializeField] private GameObject _pickUpHint;
        [SerializeField] private GameObject _inspectionPanel;
        [SerializeField] private XRButton _verifyIdentityButton;
        [SerializeField] private GameObject _verificationResult;

        private IPointable Badge { get; set; }
        private Pose _badgeHome;

        public bool Inspected { get; private set; }
        public bool Verified { get; private set; }
        public override bool IsResolved => Verified;

        protected virtual void Awake()
        {
            Badge = _badge as IPointable;
            Transform badge = ((Component)_badge).transform;
            _badgeHome = new Pose(badge.position, badge.rotation);
        }

        protected virtual void OnEnable()
        {
            if (Badge != null)
            {
                Badge.WhenPointerEventRaised += HandleBadgeEvent;
            }
            Bind(_verifyIdentityButton, HandleVerify, true);
        }

        protected virtual void OnDisable()
        {
            if (Badge != null)
            {
                Badge.WhenPointerEventRaised -= HandleBadgeEvent;
            }
            Bind(_verifyIdentityButton, HandleVerify, false);
        }

        protected virtual void Start()
        {
            Show(_pickUpHint, true);
            Show(_inspectionPanel, false);
            Show(_verificationResult, false);
        }

        public override void ResetScenario()
        {
            Inspected = false;
            Verified = false;
            _verifyIdentityButton.SetSelected(false);
            _verifyIdentityButton.SetInteractable(true);
            Show(_pickUpHint, true);
            Show(_inspectionPanel, false);
            Show(_verificationResult, false);
            Transform badge = ((Component)_badge).transform;
            badge.SetPositionAndRotation(_badgeHome.position, _badgeHome.rotation);
        }

        private void HandleBadgeEvent(PointerEvent evt)
        {
            if (evt.Type != PointerEventType.Select || Inspected)
            {
                return;
            }
            Inspected = true;
            Record("visitor_badge.inspected", "picked_up_badge", DecisionOutcome.Neutral);
            Show(_pickUpHint, false);
            Show(_inspectionPanel, true);
            _verifyIdentityButton.SetInteractable(true);
        }

        private void HandleVerify(XRButton _)
        {
            if (Verified) return;
            Verified = true;
            Record("visitor_badge.identity_verified", "verify_identity", DecisionOutcome.Safe);
            _verifyIdentityButton.SetSelected(true);
            _verifyIdentityButton.SetInteractable(false);
            Show(_verificationResult, true);
        }
    }
}
