using Oculus.Interaction;
using UnityEngine;

namespace TrustBreach.Dialogue.NPC
{
    /// <summary>
    /// Procedural presence for a jointed NPC (no animation clips): breathing and weight shift, natural gaze with
    /// clamped head turns and periodic glance-aways (no constant stare), blinking, body turn toward the participant
    /// only while engaged, arm gestures and a mouth driven by <see cref="MouthOpen"/>.
    /// All motion is smoothed and deterministic for a given seed.
    /// </summary>
    public class NPCPresence : MonoBehaviour
    {
        [Header("Skeleton")]
        [SerializeField] private Transform _hips;
        [SerializeField] private Transform _spine;
        [SerializeField] private Transform _neck;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _upperArmL;
        [SerializeField] private Transform _forearmL;
        [SerializeField] private Transform _upperArmR;
        [SerializeField] private Transform _forearmR;

        [Header("Face")]
        [SerializeField] private Transform _pupilL;
        [SerializeField] private Transform _pupilR;
        [SerializeField] private Transform _lidL;
        [SerializeField] private Transform _lidR;
        [SerializeField] private Transform _mouth;

        [Header("Badge")]
        [SerializeField] private Transform _badge;
        [SerializeField, Tooltip("Where the badge is held when shown to the participant.")]
        private Transform _badgePresentPose;

        [Header("Behaviour")]
        [SerializeField, Optional, Tooltip("Defaults to the main camera (XR head).")]
        private Transform _lookTarget;
        [SerializeField] private float _noticeDistance = 7f;
        [SerializeField, Range(10f, 80f)] private float _turnBodyAboveDegrees = 35f;
        [SerializeField] private int _seed = 1729;

        public NpcGesture Gesture { get; private set; } = NpcGesture.Idle;
        /// <summary>In conversation: the body turns to face the participant.</summary>
        public bool Engaged { get; set; }
        /// <summary>0..1, set by <see cref="NPCVoice"/>.</summary>
        public float MouthOpen { get; set; }
        public bool IsLookingAtTarget { get; private set; }
        public Transform LookTarget => _lookTarget;

        private Quaternion _neckRest, _headRest, _spineRest, _upperLRest, _foreLRest, _upperRRest, _foreRRest;
        private Vector3 _hipsRest, _pupilLRest, _pupilRRest, _lidScale, _mouthScale, _badgeLocalPos;
        private Quaternion _badgeLocalRot;
        private Vector3 _badgeLocalScale;
        private Vector3 _pointTarget;
        private float _yaw, _pitch, _yawVel, _pitchVel;
        private float _nextGlance, _glanceUntil, _glanceYaw, _nextBlink, _blinkStart = -1f;
        private bool _turning;
        private System.Random _random;

        protected virtual void Awake()
        {
            _random = new System.Random(_seed);
            _neckRest = _neck.localRotation;
            _headRest = _head.localRotation;
            _spineRest = _spine.localRotation;
            _upperLRest = _upperArmL.localRotation;
            _foreLRest = _forearmL.localRotation;
            _upperRRest = _upperArmR.localRotation;
            _foreRRest = _forearmR.localRotation;
            _hipsRest = _hips.localPosition;
            _pupilLRest = _pupilL.localPosition;
            _pupilRRest = _pupilR.localPosition;
            _lidScale = _lidL.localScale;
            _mouthScale = _mouth.localScale;
            _badgeLocalPos = _badge.localPosition;
            _badgeLocalRot = _badge.localRotation;
            _badgeLocalScale = _badge.localScale;
            _nextGlance = Time.time + Next(3f, 6f);
            _nextBlink = Time.time + Next(1.5f, 4f);
        }

        protected virtual void Start()
        {
            if (_lookTarget == null && Camera.main != null)
            {
                _lookTarget = Camera.main.transform;
            }
        }

        public void SetGesture(NpcGesture gesture, Vector3? pointTarget = null)
        {
            Gesture = gesture;
            if (pointTarget.HasValue)
            {
                _pointTarget = pointTarget.Value;
            }
        }

        public void ResetPresence()
        {
            Gesture = NpcGesture.Idle;
            Engaged = false;
            MouthOpen = 0f;
        }

        protected virtual void LateUpdate()
        {
            float t = Time.time;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (_lookTarget == null && Camera.main != null)
            {
                _lookTarget = Camera.main.transform;
            }

            // Breathing and weight shift.
            _spine.localRotation = _spineRest * Quaternion.Euler(Mathf.Sin(t * 1.5f) * 1.2f, 0f, Mathf.Sin(t * 0.45f) * 1.5f);
            _hips.localPosition = _hipsRest + new Vector3(Mathf.Sin(t * 0.45f) * 0.012f, Mathf.Sin(t * 3f) * 0.002f, 0f);

            UpdateGaze(t, dt);
            UpdateArms(t, dt);
            UpdateFace(t);
            UpdateBadge(dt);
        }

        private void UpdateGaze(float t, float dt)
        {
            float targetYaw = 0f, targetPitch = 4f;
            IsLookingAtTarget = false;
            if (_lookTarget != null)
            {
                Vector3 toTarget = _lookTarget.position - _head.position;
                if (toTarget.magnitude < _noticeDistance || Engaged)
                {
                    Vector3 local = transform.InverseTransformDirection(toTarget);
                    float yaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    float pitch = -Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;

                    // Turn the body toward the participant while engaged (with hysteresis).
                    if (Engaged && (Mathf.Abs(yaw) > _turnBodyAboveDegrees || (_turning && Mathf.Abs(yaw) > 6f)))
                    {
                        _turning = true;
                        float step = Mathf.Clamp(yaw, -80f * dt, 80f * dt);
                        transform.Rotate(0f, step, 0f, Space.World);
                        yaw -= step;
                    }
                    else
                    {
                        _turning = false;
                    }

                    if (Mathf.Abs(yaw) < 110f)
                    {
                        targetYaw = Mathf.Clamp(yaw, -65f, 65f);
                        targetPitch = Mathf.Clamp(pitch, -25f, 30f);
                        IsLookingAtTarget = true;
                    }
                }
            }

            // Natural eye contact: glance away briefly every few seconds (less while speaking/engaged).
            if (t > _nextGlance && _glanceUntil < t)
            {
                _glanceUntil = t + Next(0.5f, 1.1f);
                _glanceYaw = (_random.NextDouble() < 0.5 ? -1f : 1f) * Next(18f, 32f);
                _nextGlance = _glanceUntil + (Engaged ? Next(4f, 7f) : Next(2.5f, 5f));
            }
            if (t < _glanceUntil)
            {
                targetYaw += _glanceYaw;
                targetPitch += 6f;
                IsLookingAtTarget = false;
            }
            if (Gesture == NpcGesture.Point)
            {
                // Glance toward what he is pointing at.
                Vector3 local = transform.InverseTransformDirection(_pointTarget - _head.position);
                targetYaw = Mathf.Lerp(targetYaw, Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -65f, 65f), 0.5f);
            }
            if (Gesture == NpcGesture.PhoneCall)
            {
                targetYaw += 25f;
                targetPitch += 8f;
            }

            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVel, 0.28f, 220f, dt);
            _pitch = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVel, 0.3f, 160f, dt);
            float tilt = Gesture == NpcGesture.Shrug ? 6f : 0f;
            _neck.localRotation = _neckRest * Quaternion.Euler(_pitch * 0.35f, _yaw * 0.4f, 0f);
            _head.localRotation = _headRest * Quaternion.Euler(_pitch * 0.65f, _yaw * 0.6f, tilt);

            // Eyes lead the head slightly toward the remaining offset.
            float eyeX = Mathf.Clamp((targetYaw - _yaw) * 0.0002f, -0.0035f, 0.0035f);
            float eyeY = Mathf.Clamp(-(targetPitch - _pitch) * 0.00015f, -0.0025f, 0.0025f);
            _pupilL.localPosition = _pupilLRest + new Vector3(eyeX, eyeY, 0f);
            _pupilR.localPosition = _pupilRRest + new Vector3(eyeX, eyeY, 0f);
        }

        private void UpdateArms(float t, float dt)
        {
            Vector3 upperL = new Vector3(0f, 0f, -4f), foreL = new Vector3(-10f, 0f, 0f);
            Vector3 upperR = new Vector3(0f, 0f, 4f), foreR = new Vector3(-10f, 0f, 0f);
            Quaternion? aimR = null;
            float talk = MouthOpen > 0.05f ? 1f : 0.4f;

            switch (Gesture)
            {
                case NpcGesture.Explain:
                    upperR = new Vector3(-30f + Mathf.Sin(t * 2.1f) * 5f * talk, Mathf.Sin(t * 1.3f) * 6f * talk, 10f);
                    foreR = new Vector3(-75f + Mathf.Sin(t * 2.6f) * 8f * talk, 0f, 0f);
                    upperL = new Vector3(-12f, 0f, -8f);
                    foreL = new Vector3(-35f + Mathf.Sin(t * 1.7f + 1f) * 5f * talk, 0f, 0f);
                    break;
                case NpcGesture.Point:
                    aimR = AimDown(_upperArmR, _pointTarget);
                    foreR = new Vector3(-6f, 0f, 0f);
                    break;
                case NpcGesture.ShowBadge:
                    upperL = new Vector3(-48f, 0f, -6f);
                    foreL = new Vector3(-62f, 20f, 0f);
                    upperR = new Vector3(-10f, 0f, 6f);
                    foreR = new Vector3(-30f, 0f, 0f);
                    break;
                case NpcGesture.Wave:
                    upperR = new Vector3(-15f, 0f, 125f);
                    foreR = new Vector3(0f, 0f, 25f + Mathf.Sin(t * 9f) * 22f);
                    break;
                case NpcGesture.Shrug:
                    upperL = new Vector3(-18f, 0f, -22f);
                    foreL = new Vector3(-80f, -25f, 0f);
                    upperR = new Vector3(-18f, 0f, 22f);
                    foreR = new Vector3(-80f, 25f, 0f);
                    break;
                case NpcGesture.PhoneCall:
                    upperR = new Vector3(-35f, 0f, 38f);
                    foreR = new Vector3(-145f, 0f, 0f);
                    break;
                default:
                    upperL += new Vector3(Mathf.Sin(t * 0.9f) * 2f, 0f, 0f);
                    upperR += new Vector3(Mathf.Sin(t * 0.9f + 1.5f) * 2f, 0f, 0f);
                    break;
            }

            float k = 1f - Mathf.Exp(-8f * dt);
            _upperArmL.localRotation = Quaternion.Slerp(_upperArmL.localRotation, _upperLRest * Quaternion.Euler(upperL), k);
            _forearmL.localRotation = Quaternion.Slerp(_forearmL.localRotation, _foreLRest * Quaternion.Euler(foreL), k);
            Quaternion upperRTarget = aimR ?? _upperRRest * Quaternion.Euler(upperR);
            _upperArmR.localRotation = Quaternion.Slerp(_upperArmR.localRotation, upperRTarget, k);
            _forearmR.localRotation = Quaternion.Slerp(_forearmR.localRotation, _foreRRest * Quaternion.Euler(foreR), k);
        }

        /// <summary>Local rotation that points the arm's -Y axis at a world target (clamped to a comfortable cone).</summary>
        private static Quaternion AimDown(Transform upperArm, Vector3 target)
        {
            Transform parent = upperArm.parent;
            Vector3 dir = parent.InverseTransformDirection((target - upperArm.position).normalized);
            dir.y = Mathf.Clamp(dir.y, -0.6f, 0.35f);
            return Quaternion.FromToRotation(Vector3.down, dir.normalized);
        }

        private void UpdateFace(float t)
        {
            if (t > _nextBlink)
            {
                _blinkStart = t;
                _nextBlink = t + Next(2.2f, 5.5f);
            }
            float closed = 0f;
            if (_blinkStart >= 0f)
            {
                float u = (t - _blinkStart) / 0.16f;
                closed = u < 0.4f ? u / 0.4f : u < 1f ? 1f - (u - 0.4f) / 0.6f : 0f;
            }
            Vector3 lid = new Vector3(_lidScale.x, Mathf.Lerp(_lidScale.y, _lidScale.y * 9f, closed), _lidScale.z);
            _lidL.localScale = lid;
            _lidR.localScale = lid;
            _mouth.localScale = new Vector3(_mouthScale.x * (1f - MouthOpen * 0.15f), _mouthScale.y * (1f + MouthOpen * 2.6f), _mouthScale.z);
        }

        private void UpdateBadge(float dt)
        {
            float k = 1f - Mathf.Exp(-6f * dt);
            if (Gesture == NpcGesture.ShowBadge && _badgePresentPose != null)
            {
                _badge.position = Vector3.Lerp(_badge.position, _badgePresentPose.position, k);
                _badge.rotation = Quaternion.Slerp(_badge.rotation, _badgePresentPose.rotation, k);
                _badge.localScale = Vector3.Lerp(_badge.localScale, _badgeLocalScale * 1.8f, k);
            }
            else
            {
                _badge.localPosition = Vector3.Lerp(_badge.localPosition, _badgeLocalPos, k);
                _badge.localRotation = Quaternion.Slerp(_badge.localRotation, _badgeLocalRot, k);
                _badge.localScale = Vector3.Lerp(_badge.localScale, _badgeLocalScale, k);
            }
        }

        private float Next(float min, float max) => min + (float)_random.NextDouble() * (max - min);
    }
}
