using UnityEngine;

namespace TrustBreach.Core
{
    public enum ArtifactCategory
    {
        SpoofedCredential,
        MaliciousMedia,
        PhishingMessage,
        FraudulentLogin,
        PoorSecurityHygiene,
        SecurityControl
    }

    public enum OfficeZone
    {
        Reception,
        Workstations,
        ITSecurityDesk,
        MeetingArea,
        Hallway
    }

    /// <summary>
    /// Scenario metadata for an object the participant can inspect. Telemetry (gaze dwell, lure detection)
    /// and the future dialogue/AI layers look artifacts up by <see cref="ArtifactId"/> through
    /// <see cref="ScenarioArtifactRegistry"/> rather than by scene path.
    /// </summary>
    public class ScenarioArtifact : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable id used in telemetry CSVs and dialogue conditions.")]
        private string _artifactId;

        [SerializeField]
        private ArtifactCategory _category;

        [SerializeField]
        private OfficeZone _zone;

        [SerializeField, Tooltip("True for lures the participant should recognise as a threat.")]
        private bool _isSuspicious = true;

        [SerializeField, TextArea, Tooltip("Red flags a trained employee should notice (facilitator / debrief use).")]
        private string _redFlags;

        [SerializeField, Tooltip("Collider used for gaze/lure hit tests. Defaults to the first collider in children.")]
        private Collider _gazeCollider;

        public string ArtifactId => string.IsNullOrEmpty(_artifactId) ? name : _artifactId;
        public ArtifactCategory Category => _category;
        public OfficeZone Zone => _zone;
        public bool IsSuspicious => _isSuspicious;
        public string RedFlags => _redFlags;
        public Collider GazeCollider => _gazeCollider != null ? _gazeCollider : GetComponentInChildren<Collider>();

        protected virtual void OnEnable()
        {
            ScenarioArtifactRegistry.Register(this);
        }

        protected virtual void OnDisable()
        {
            ScenarioArtifactRegistry.Unregister(this);
        }

        #region Inject

        public void InjectAllScenarioArtifact(string artifactId, ArtifactCategory category, OfficeZone zone,
            bool isSuspicious, string redFlags)
        {
            _artifactId = artifactId;
            _category = category;
            _zone = zone;
            _isSuspicious = isSuspicious;
            _redFlags = redFlags;
        }

        public void InjectOptionalGazeCollider(Collider gazeCollider)
        {
            _gazeCollider = gazeCollider;
        }

        #endregion
    }
}
