using System;
using System.Collections.Generic;
using TMPro;
using TrustBreach.Interactions;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    /// <summary>
    /// World-space conversation panel (built from quads + TextMeshPro, ray-pressable via <see cref="XRButton"/>, so the
    /// controller index trigger / Simulator "T" selects options). Shows speaker, the current line, an objective strip,
    /// up to six context-sensitive options, and footer actions (report, end, voice on/off), plus a debrief page.
    /// </summary>
    public class DialoguePanel : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text _speakerName;
        [SerializeField] private TMP_Text _speakerRole;

        [Header("Body")]
        [SerializeField] private TMP_Text _lineText;
        [SerializeField] private TMP_Text _objectiveText;
        [SerializeField] private TMP_Text _statusText;

        [Header("Options")]
        [SerializeField] private XRButton[] _optionButtons = new XRButton[0];
        [SerializeField] private TMP_Text[] _optionLabels = new TMP_Text[0];
        [SerializeField] private XRButton _talkButton;
        [SerializeField, Tooltip("Talk button + its label.")] private GameObject _talkGroup;
        [SerializeField] private XRButton _continueButton;
        [SerializeField, Tooltip("Continue button + its label.")] private GameObject _continueGroup;

        [Header("Footer")]
        [SerializeField] private GameObject _footer;
        [SerializeField] private XRButton _reportButton;
        [SerializeField] private XRButton _exitButton;
        [SerializeField] private XRButton _muteButton;
        [SerializeField] private TMP_Text _muteLabel;

        [Header("Debrief")]
        [SerializeField] private GameObject _debriefPage;
        [SerializeField] private TMP_Text _debriefHeadline;
        [SerializeField, Tooltip("Headline bar, tinted by outcome.")] private Renderer _debriefTitleBar;
        [SerializeField] private TMP_Text _debriefPageTitle;
        [SerializeField] private TMP_Text _debriefBody;
        [SerializeField] private XRButton _debriefNextButton;
        [SerializeField] private XRButton _debriefBackButton;
        [SerializeField] private XRButton _debriefCloseButton;
        [SerializeField] private XRButton _debriefResetButton;
        [SerializeField, Tooltip("Reset button + its label (demo mode only).")] private GameObject _debriefResetGroup;

        public event Action TalkPressed;
        public event Action<int> OptionPressed;
        public event Action ContinuePressed;
        public event Action ReportPressed;
        public event Action ExitPressed;
        public event Action MutePressed;
        public event Action DebriefClosed;
        public event Action ResetPressed;

        public int OptionSlots => _optionButtons.Length;
        public XRButton TalkButton => _talkButton;
        public XRButton ContinueButton => _continueButton;
        public XRButton ReportButton => _reportButton;
        public XRButton ExitButton => _exitButton;
        public XRButton MuteButton => _muteButton;
        public XRButton DebriefCloseButton => _debriefCloseButton;
        public XRButton DebriefResetButton => _debriefResetButton;
        public XRButton OptionButton(int index) => _optionButtons[index];
        public string LineText => _lineText.text;
        public string ObjectiveText => _objectiveText.text;
        public bool DebriefVisible => _debriefPage.activeSelf;
        public string DebriefText => _debriefBody.text;
        public int DebriefPageIndex => _debriefIndex;
        public int DebriefPageCount => _debriefPages.Count;
        public XRButton DebriefNextButton => _debriefNextButton;
        public XRButton DebriefBackButton => _debriefBackButton;

        private readonly Dictionary<XRButton, Action<XRButton>> _handlers = new Dictionary<XRButton, Action<XRButton>>();
        private readonly List<string> _debriefPages = new List<string>();
        private readonly List<string> _debriefTitles = new List<string>();
        private int _debriefIndex;

        protected virtual void OnEnable()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                int index = i;
                Bind(_optionButtons[i], _ => OptionPressed?.Invoke(index));
            }
            Bind(_talkButton, _ => TalkPressed?.Invoke());
            Bind(_continueButton, _ => ContinuePressed?.Invoke());
            Bind(_reportButton, _ => ReportPressed?.Invoke());
            Bind(_exitButton, _ => ExitPressed?.Invoke());
            Bind(_muteButton, _ => MutePressed?.Invoke());
            Bind(_debriefCloseButton, _ => DebriefClosed?.Invoke());
            Bind(_debriefResetButton, _ => ResetPressed?.Invoke());
            Bind(_debriefNextButton, _ => ShowDebriefPage(_debriefIndex + 1));
            Bind(_debriefBackButton, _ => ShowDebriefPage(_debriefIndex - 1));
        }

        protected virtual void OnDisable()
        {
            foreach (KeyValuePair<XRButton, Action<XRButton>> pair in _handlers)
            {
                if (pair.Key != null) pair.Key.WhenActivated -= pair.Value;
            }
            _handlers.Clear();
        }

        private void Bind(XRButton button, Action<XRButton> handler)
        {
            if (button == null) return;
            button.WhenActivated += handler;
            _handlers[button] = handler;
        }

        public void SetSpeaker(string speakerName, string role)
        {
            _speakerName.text = speakerName;
            _speakerRole.text = role;
        }

        /// <summary>Before the conversation: invitation text and a single Talk button.</summary>
        public void ShowIdle(string text, string status, bool allowTalk)
        {
            HideOptions();
            Show(_debriefPage, false);
            Show(_footer, false);
            Show(_continueGroup, false);
            _lineText.text = text;
            _objectiveText.text = string.Empty;
            _statusText.text = status;
            Show(_talkGroup, allowTalk);
            if (allowTalk) _talkButton.SetInteractable(true);
        }

        /// <summary>An NPC line is being delivered: options hidden until it finishes.</summary>
        public void ShowLine(string text, string objective, string status)
        {
            Show(_talkGroup, false);
            Show(_continueGroup, false);
            Show(_debriefPage, false);
            HideOptions();
            Show(_footer, true);
            _lineText.text = text;
            _objectiveText.text = string.IsNullOrEmpty(objective) ? string.Empty : $"<b>Request:</b> {objective}";
            _statusText.text = status;
        }

        public void ShowOptions(IReadOnlyList<string> labels, string status)
        {
            int count = Mathf.Min(labels.Count, _optionButtons.Length);
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                bool used = i < count;
                Show(_optionButtons[i].gameObject, used);
                Show(_optionLabels[i].gameObject, used);
                if (used)
                {
                    _optionLabels[i].text = labels[i];
                    _optionButtons[i].SetSelected(false);
                    _optionButtons[i].SetInteractable(true);
                }
            }
            SetFooterInteractable(true);
            _statusText.text = status;
        }

        /// <summary>Selection feedback: the chosen option keeps its highlight, the rest lock until the next line.</summary>
        public void MarkChosen(int index)
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                if (!_optionButtons[i].gameObject.activeSelf) continue;
                _optionButtons[i].SetSelected(i == index);
                _optionButtons[i].SetInteractable(false);
            }
            SetFooterInteractable(false);
        }

        public void ShowContinue(string status)
        {
            HideOptions();
            Show(_footer, false);
            Show(_continueGroup, true);
            _continueButton.SetInteractable(true);
            _statusText.text = status;
        }

        /// <summary>Multi-page debrief with BACK / NEXT, CLOSE and (demo) RESTART EXPERIENCE.</summary>
        public void ShowDebrief(string headline, IReadOnlyList<string> titles, IReadOnlyList<string> pages, bool allowReset, Color headlineColor)
        {
            var block = new MaterialPropertyBlock();
            _debriefTitleBar.GetPropertyBlock(block);
            block.SetColor("_BaseColor", headlineColor);
            _debriefTitleBar.SetPropertyBlock(block);
            HideOptions();
            Show(_footer, false);
            Show(_continueGroup, false);
            Show(_talkGroup, false);
            _debriefHeadline.text = headline;
            _debriefPages.Clear();
            _debriefPages.AddRange(pages);
            _debriefTitles.Clear();
            _debriefTitles.AddRange(titles);
            Show(_debriefPage, true);
            Show(_debriefResetGroup, allowReset);
            _debriefCloseButton.SetInteractable(true);
            if (allowReset) _debriefResetButton.SetInteractable(true);
            ShowDebriefPage(0);
        }

        public void ShowDebriefPage(int index)
        {
            if (_debriefPages.Count == 0) return;
            _debriefIndex = Mathf.Clamp(index, 0, _debriefPages.Count - 1);
            _debriefBody.text = _debriefPages[_debriefIndex];
            string title = _debriefIndex < _debriefTitles.Count ? _debriefTitles[_debriefIndex] : string.Empty;
            _debriefPageTitle.text = $"{title}   ({_debriefIndex + 1}/{_debriefPages.Count})";
            _debriefBackButton.SetInteractable(_debriefIndex > 0);
            _debriefNextButton.SetInteractable(_debriefIndex < _debriefPages.Count - 1);
        }

        /// <summary>Alex's reaction is pending until the participant returns; nothing to choose meanwhile.</summary>
        public void ShowWaiting(string status)
        {
            HideOptions();
            Show(_footer, false);
            Show(_continueGroup, false);
            Show(_talkGroup, false);
            _statusText.text = status;
        }

        public void HideDebrief() => Show(_debriefPage, false);

        public void SetMuted(bool muted) => _muteLabel.text = muted ? "VOICE: OFF" : "VOICE: ON";

        public void SetStatus(string status) => _statusText.text = status;

        private void SetFooterInteractable(bool interactable)
        {
            _reportButton.SetInteractable(interactable);
            _exitButton.SetInteractable(interactable);
        }

        private void HideOptions()
        {
            for (int i = 0; i < _optionButtons.Length; i++)
            {
                Show(_optionButtons[i].gameObject, false);
                Show(_optionLabels[i].gameObject, false);
            }
        }

        private static void Show(GameObject target, bool visible)
        {
            if (target != null && target.activeSelf != visible) target.SetActive(visible);
        }
    }
}
