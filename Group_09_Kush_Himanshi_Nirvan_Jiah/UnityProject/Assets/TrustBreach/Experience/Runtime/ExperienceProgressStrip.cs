using TMPro;
using UnityEngine;

namespace TrustBreach.Experience
{
    /// <summary>
    /// Small, always-available progress indicator ("STEP 4 OF 6 - EXPLORE THE OFFICE") shown low in the field of view.
    /// Comfort: it is lazy-follow, not head-locked. It stays still in the world and only glides back when the participant
    /// has turned well away from it. It has no colliders (never blocks rays or movement) and hides itself near the guide
    /// panel and near Alex's dialogue panel, so floating panels never overlap. It also hides while something solid (a kiosk,
    /// a desk, a wall) is closer than the strip, so it never sinks into scenery or covers what the participant is using.
    /// </summary>
    public class ExperienceProgressStrip : MonoBehaviour
    {
        [SerializeField] private ExperienceGuidePanel _guide;
        [SerializeField] private Transform _content;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Renderer[] _segments = new Renderer[0];
        [SerializeField, Tooltip("Hidden within this distance of these panels (guide panel, Alex's dialogue panel).")]
        private Transform[] _hideNear = new Transform[0];
        [SerializeField] private float _hideRadius = 2.6f;
        [SerializeField] private float _distance = 1.25f;
        [SerializeField, Tooltip("Metres below eye level.")] private float _drop = 0.48f;
        [SerializeField, Tooltip("Re-centre when the strip is this many degrees away from where the participant faces.")]
        private float _recenterAngle = 32f;
        [SerializeField] private float _smoothTime = 0.35f;

        private Vector3 _velocity;
        private bool _recentering = true;
        private ExperienceStage _shownStage = (ExperienceStage)(-1);
        private MaterialPropertyBlock _block;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private float _visibilityChangeAt = -1f;

        public bool Visible => _content != null && _content.gameObject.activeSelf;

        protected virtual void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || _guide == null) return;
            Vector3 head = cam.transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(cam.transform.up, Vector3.up);
            forward.Normalize();
            bool visible = !NearAny(head) && !Obstructed(cam, head, forward);
            if (_content.gameObject.activeSelf != visible)
            {
                // Debounce (0.25 s) so the strip never flickers at the edge of an obstacle or a panel's radius.
                if (_visibilityChangeAt < 0f) _visibilityChangeAt = Time.unscaledTime + 0.25f;
                if (Time.unscaledTime >= _visibilityChangeAt)
                {
                    _content.gameObject.SetActive(visible);
                    _visibilityChangeAt = -1f;
                }
            }
            else
            {
                _visibilityChangeAt = -1f;
            }
            visible = _content.gameObject.activeSelf;
            if (!visible)
            {
                _recentering = true; // reappear in front of the participant, not where it was left
                return;
            }

            Vector3 target = head + forward * _distance + Vector3.down * _drop;
            Vector3 toStrip = Vector3.ProjectOnPlane(transform.position - head, Vector3.up);
            if (!_recentering && (Vector3.Angle(forward, toStrip) > _recenterAngle || Mathf.Abs(toStrip.magnitude - _distance) > 0.5f))
            {
                _recentering = true;
            }
            if (_recentering)
            {
                if (Vector3.Distance(transform.position, target) > 3f) transform.position = target; // first frame or after teleport
                transform.position = Vector3.SmoothDamp(transform.position, target, ref _velocity, _smoothTime);
                if (Vector3.Distance(transform.position, target) < 0.02f) _recentering = false;
            }
            Vector3 look = Vector3.ProjectOnPlane(transform.position - head, Vector3.up);
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);

            if (_guide.Stage != _shownStage) Show(_guide.Stage);
        }

        private void Show(ExperienceStage stage)
        {
            _shownStage = stage;
            _label.text = ExperienceFlow.StepLabel(stage);
            _block ??= new MaterialPropertyBlock();
            for (int i = 0; i < _segments.Length; i++)
            {
                if (_segments[i] == null) continue;
                Color c = i < (int)stage ? ExperienceGuidePanel.DoneColor : i == (int)stage ? ExperienceGuidePanel.CurrentColor : ExperienceGuidePanel.TodoColor;
                _segments[i].GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c);
                _segments[i].SetPropertyBlock(_block);
            }
        }

        /// <summary>True when scenery is closer than the strip's place in front of the participant (one non-allocating raycast).</summary>
        private bool Obstructed(Camera cam, Vector3 head, Vector3 forward)
        {
            Vector3 offset = forward * _distance + Vector3.down * _drop;
            float length = offset.magnitude + 0.1f;
            int count = Physics.RaycastNonAlloc(head, offset.normalized, _hits, length, ~0, QueryTriggerInteraction.Ignore);
            Transform rig = cam.transform.root;
            for (int i = 0; i < count; i++)
            {
                if (!_hits[i].transform.IsChildOf(rig)) return true;
            }
            return false;
        }

        private bool NearAny(Vector3 head)
        {
            foreach (Transform t in _hideNear)
            {
                if (t == null) continue;
                Vector3 d = t.position - head;
                d.y = 0f;
                if (d.sqrMagnitude < _hideRadius * _hideRadius) return true;
            }
            return false;
        }
    }
}
