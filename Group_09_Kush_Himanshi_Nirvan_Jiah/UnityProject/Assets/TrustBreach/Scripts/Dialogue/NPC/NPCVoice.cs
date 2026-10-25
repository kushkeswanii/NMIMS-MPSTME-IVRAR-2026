using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace TrustBreach.Dialogue.NPC
{
    /// <summary>
    /// Spatial NPC voice + subtitles. Plays a pre-generated clip when available (3D AudioSource with logarithmic
    /// attenuation), otherwise times the line from its length. Subtitles above the NPC show one sentence at a time,
    /// paced to the audio. Drives <see cref="NPCPresence.MouthOpen"/> from output amplitude (or a gentle synthetic
    /// rhythm when muted / clip-less). <see cref="Muted"/> is a session-wide setting.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class NPCVoice : MonoBehaviour
    {
        [SerializeField] private AudioSource _source;
        [SerializeField] private NPCPresence _presence;
        [SerializeField] private GameObject _subtitleRoot;
        [SerializeField] private TMP_Text _subtitleText;
        [SerializeField, Tooltip("Reading pace used when there is no clip or the voice is muted.")]
        private float _charactersPerSecond = 16f;
        [SerializeField] private float _minimumLineSeconds = 1.6f;

        private const string MutedPrefKey = "TrustBreach.VoiceMuted";
        private static bool? _muted;

        public static event Action<bool> MutedChanged;

        public static bool Muted
        {
            get
            {
                if (!_muted.HasValue)
                {
                    try { _muted = PlayerPrefs.GetInt(MutedPrefKey, 0) == 1; }
                    catch (Exception) { _muted = false; }
                }
                return _muted.Value;
            }
            set
            {
                _muted = value;
                try { PlayerPrefs.SetInt(MutedPrefKey, value ? 1 : 0); } catch (Exception) { /* preference is optional */ }
                MutedChanged?.Invoke(value);
            }
        }

        public bool IsSpeaking { get; private set; }
        public bool PlayedAudio { get; private set; }
        public float LineDuration { get; private set; }
        public float LineProgress => IsSpeaking ? Mathf.Clamp01((Time.time - _startTime) / Mathf.Max(LineDuration, 0.01f)) : 1f;
        public AudioSource Source => _source;
        public string CurrentSubtitle => _subtitleText != null ? _subtitleText.text : string.Empty;

        public event Action LineFinished;

        private readonly List<(string text, float end)> _segments = new List<(string, float)>();
        private readonly float[] _samples = new float[256];
        private float _startTime;
        private float _envelope;

        private static readonly Regex SentenceSplit = new Regex(@"(?<=[.!?])\s+", RegexOptions.Compiled);

        protected virtual void Awake()
        {
            if (_source == null) _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            Show(false);
        }

        protected virtual void OnEnable() => MutedChanged += HandleMuteChanged;
        protected virtual void OnDisable() => MutedChanged -= HandleMuteChanged;

        private void HandleMuteChanged(bool muted)
        {
            if (muted && _source.isPlaying)
            {
                _source.Stop(); // subtitles keep their timing
            }
        }

        /// <summary>Speaks a line. Returns the planned duration in seconds.</summary>
        public float Speak(string text, AudioClip clip)
        {
            Stop(notify: false);
            text = text ?? string.Empty;
            bool useClip = clip != null && !Muted;
            float textSeconds = Mathf.Max(_minimumLineSeconds, text.Length / Mathf.Max(_charactersPerSecond, 1f));
            LineDuration = useClip ? clip.length + 0.15f : textSeconds;
            PlayedAudio = false;
            if (useClip)
            {
                _source.clip = clip;
                _source.Play();
                PlayedAudio = true;
            }

            // Sentence-level subtitles, timed proportionally to character count.
            _segments.Clear();
            string[] sentences = SentenceSplit.Split(text.Trim());
            int total = Mathf.Max(1, text.Length);
            float elapsed = 0f;
            foreach (string sentence in sentences)
            {
                elapsed += LineDuration * sentence.Length / total;
                _segments.Add((sentence, elapsed));
            }
            _startTime = Time.time;
            IsSpeaking = true;
            Show(true);
            UpdateSubtitle(0f);
            return LineDuration;
        }

        /// <summary>Ends the current line immediately (e.g. "skip").</summary>
        public void Stop(bool notify = true)
        {
            bool wasSpeaking = IsSpeaking;
            IsSpeaking = false;
            if (_source != null && _source.isPlaying)
            {
                _source.Stop();
            }
            if (_presence != null) _presence.MouthOpen = 0f;
            Show(false);
            if (notify && wasSpeaking)
            {
                LineFinished?.Invoke();
            }
        }

        protected virtual void Update()
        {
            if (!IsSpeaking)
            {
                return;
            }
            float elapsed = Time.time - _startTime;
            UpdateSubtitle(elapsed);
            UpdateMouth(elapsed);
            if (elapsed >= LineDuration)
            {
                Stop(notify: true);
            }
        }

        private void LateUpdate()
        {
            // Subtitles face the participant (yaw only) so they stay readable while walking around.
            if (_subtitleRoot != null && _subtitleRoot.activeSelf && Camera.main != null)
            {
                Vector3 toCamera = _subtitleRoot.transform.position - Camera.main.transform.position;
                toCamera.y = 0f;
                if (toCamera.sqrMagnitude > 1e-4f)
                {
                    _subtitleRoot.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }
        }

        private void UpdateSubtitle(float elapsed)
        {
            if (_subtitleText == null || _segments.Count == 0)
            {
                return;
            }
            string current = _segments[_segments.Count - 1].text;
            foreach ((string text, float end) in _segments)
            {
                if (elapsed <= end)
                {
                    current = text;
                    break;
                }
            }
            if (_subtitleText.text != current)
            {
                _subtitleText.text = current;
            }
        }

        private void UpdateMouth(float elapsed)
        {
            if (_presence == null)
            {
                return;
            }
            float target;
            if (_source.isPlaying)
            {
                _source.GetOutputData(_samples, 0);
                float sum = 0f;
                for (int i = 0; i < _samples.Length; i++) sum += _samples[i] * _samples[i];
                target = Mathf.Clamp01(Mathf.Sqrt(sum / _samples.Length) * 9f);
            }
            else
            {
                // Muted or no clip: soft syllable rhythm so the character still visibly speaks.
                target = 0.25f + 0.25f * Mathf.Sin(elapsed * 13f) * Mathf.Sin(elapsed * 3.1f + 0.7f);
            }
            _envelope = Mathf.Lerp(_envelope, target, 0.45f);
            _presence.MouthOpen = Mathf.Clamp01(_envelope);
        }

        private void Show(bool visible)
        {
            if (_subtitleRoot != null && _subtitleRoot.activeSelf != visible)
            {
                _subtitleRoot.SetActive(visible);
            }
        }
    }
}
