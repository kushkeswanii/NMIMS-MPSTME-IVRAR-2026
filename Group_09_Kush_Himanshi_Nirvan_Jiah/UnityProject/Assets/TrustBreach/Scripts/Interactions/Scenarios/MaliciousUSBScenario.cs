using Oculus.Interaction;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions.Scenarios
{
    /// <summary>
    /// The found USB drive can be carried to a workstation dock ("Connect USB", unsafe) or to the IT Security
    /// drop-off ("Submit to IT", safe). A station's button unlocks only while the drive is placed in that station's
    /// zone. Entirely simulated: no real USB device or host file system is ever accessed.
    /// </summary>
    public class MaliciousUSBScenario : ScenarioController
    {
        public enum Stage
        {
            Unresolved,
            Connected,
            SubmittedToIT
        }

        [SerializeField, Interface(typeof(IPointable)), Tooltip("The drive's Grabbable.")]
        private Object _usbPointable;
        [SerializeField] private Collider _usbCollider;

        [Header("Workstation dock (unsafe)")]
        [SerializeField, Tooltip("Trigger volume above the dock.")] private Collider _connectZone;
        [SerializeField] private Transform _connectSnapPoint;
        [SerializeField] private XRButton _connectButton;
        [SerializeField] private GameObject _connectWaitingHint;
        [SerializeField] private GameObject _connectOutcome;
        [SerializeField] private GameObject _infectedScreen;

        [Header("IT Security drop-off (safe)")]
        [SerializeField, Tooltip("Trigger volume above the drop-off tray.")] private Collider _submitZone;
        [SerializeField] private Transform _submitSnapPoint;
        [SerializeField] private XRButton _submitButton;
        [SerializeField] private GameObject _submitWaitingHint;
        [SerializeField] private GameObject _submitOutcome;

        private IPointable UsbPointable { get; set; }
        private bool _pickedUp;
        private bool _inConnectZone;
        private bool _inSubmitZone;
        private Pose _usbHome;

        public Stage CurrentStage { get; private set; } = Stage.Unresolved;
        public override bool IsResolved => CurrentStage != Stage.Unresolved;
        public bool InConnectZone => _inConnectZone;
        public bool InSubmitZone => _inSubmitZone;

        protected virtual void Awake()
        {
            UsbPointable = _usbPointable as IPointable;
            _usbHome = new Pose(_usbCollider.transform.position, _usbCollider.transform.rotation);
        }

        protected virtual void OnEnable()
        {
            if (UsbPointable != null)
            {
                UsbPointable.WhenPointerEventRaised += HandleUsbEvent;
            }
            Bind(_connectButton, HandleConnect, true);
            Bind(_submitButton, HandleSubmit, true);
        }

        protected virtual void OnDisable()
        {
            if (UsbPointable != null)
            {
                UsbPointable.WhenPointerEventRaised -= HandleUsbEvent;
            }
            Bind(_connectButton, HandleConnect, false);
            Bind(_submitButton, HandleSubmit, false);
        }

        protected virtual void Start()
        {
            Show(_connectOutcome, false);
            Show(_infectedScreen, false);
            Show(_submitOutcome, false);
            UpdateZones(force: true);
        }

        protected virtual void Update()
        {
            if (CurrentStage == Stage.Unresolved)
            {
                UpdateZones(force: false);
            }
        }

        public override void ResetScenario()
        {
            CurrentStage = Stage.Unresolved;
            _pickedUp = false;
            Transform usb = _usbCollider.transform;
            foreach (MonoBehaviour behaviour in usb.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IInteractableView)
                {
                    behaviour.enabled = true;
                }
            }
            usb.SetPositionAndRotation(_usbHome.position, _usbHome.rotation);
            if (usb.TryGetComponent(out Rigidbody body))
            {
                body.position = _usbHome.position;
                body.rotation = _usbHome.rotation;
            }
            _connectButton.SetSelected(false);
            _submitButton.SetSelected(false);
            Show(_connectOutcome, false);
            Show(_infectedScreen, false);
            Show(_submitOutcome, false);
            UpdateZones(force: true);
        }

        private void UpdateZones(bool force)
        {
            Vector3 usb = _usbCollider.bounds.center;
            bool inConnect = _connectZone.bounds.Contains(usb);
            bool inSubmit = _submitZone.bounds.Contains(usb);
            if (!force && inConnect == _inConnectZone && inSubmit == _inSubmitZone)
            {
                return;
            }
            _inConnectZone = inConnect;
            _inSubmitZone = inSubmit;
            _connectButton.SetInteractable(inConnect);
            _submitButton.SetInteractable(inSubmit);
            Show(_connectWaitingHint, !inConnect);
            Show(_submitWaitingHint, !inSubmit);
        }

        private void HandleUsbEvent(PointerEvent evt)
        {
            if (evt.Type == PointerEventType.Select && !_pickedUp)
            {
                _pickedUp = true;
                Record("usb_drive.picked_up", "picked_up_usb", DecisionOutcome.Neutral);
            }
        }

        private void HandleConnect(XRButton _)
        {
            if (CurrentStage != Stage.Unresolved || !_inConnectZone) return;
            Record("usb_drive.connected", "connect_usb", DecisionOutcome.Unsafe);
            _connectButton.SetSelected(true);
            Resolve(Stage.Connected, _connectSnapPoint);
            Show(_connectOutcome, true);
            Show(_infectedScreen, true);
        }

        private void HandleSubmit(XRButton _)
        {
            if (CurrentStage != Stage.Unresolved || !_inSubmitZone) return;
            Record("usb_drive.submitted_to_it", "submit_to_it", DecisionOutcome.Safe);
            _submitButton.SetSelected(true);
            Resolve(Stage.SubmittedToIT, _submitSnapPoint);
            Show(_submitOutcome, true);
        }

        /// <summary>Locks the drive into the chosen station and disables both decisions.</summary>
        private void Resolve(Stage stage, Transform snapPoint)
        {
            CurrentStage = stage;
            _connectButton.SetInteractable(false);
            _submitButton.SetInteractable(false);
            Show(_connectWaitingHint, false);
            Show(_submitWaitingHint, false);

            Transform usb = _usbCollider.transform;
            foreach (MonoBehaviour behaviour in usb.GetComponentsInChildren<MonoBehaviour>())
            {
                if (behaviour is IInteractableView)
                {
                    behaviour.enabled = false; // releases any active grab and prevents re-grabbing
                }
            }
            usb.SetPositionAndRotation(snapPoint.position, snapPoint.rotation);
            if (usb.TryGetComponent(out Rigidbody body))
            {
                body.isKinematic = true;
                body.position = snapPoint.position;
                body.rotation = snapPoint.rotation;
            }
        }
    }
}
