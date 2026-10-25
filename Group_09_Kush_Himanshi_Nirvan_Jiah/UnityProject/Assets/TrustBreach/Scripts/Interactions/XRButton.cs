using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEngine;
using UnityEngine.Events;

namespace TrustBreach.Interactions
{
    /// <summary>
    /// World-space button driven by an Interaction SDK <see cref="IPointable"/> (normally a RayInteractable, so the
    /// controller index trigger / Simulator "T" selects it). Interaction phases:
    ///  - Hover: a ray is aimed at the button (highlight + outline).
    ///  - Press: the trigger is held while aimed (pressed colour).
    ///  - Activate: the trigger is released while the press is still owned by this button (fires the action once).
    /// A cancelled press (interactor disabled, ray lost) never activates.
    /// </summary>
    public class XRButton : MonoBehaviour
    {
        public enum VisualState
        {
            Normal,
            Hover,
            Pressed,
            Disabled
        }

        [SerializeField, Interface(typeof(IPointable))]
        private UnityEngine.Object _pointable;

        [SerializeField, Tooltip("Renderer whose _BaseColor reflects the button state.")]
        private Renderer _background;

        [SerializeField, Optional, Tooltip("Frame shown on hover and when the button is the selected choice.")]
        private Renderer _outline;

        [SerializeField, Tooltip("Stable id used in logs, e.g. \"phishing_email.proceed\".")]
        private string _buttonId;

        [SerializeField]
        private Color _normalColor = new Color(0.05f, 0.35f, 0.55f);

        [SerializeField]
        private Color _disabledColor = new Color(0.55f, 0.57f, 0.6f);

        [SerializeField]
        private Color _hoverOutlineColor = Color.white;

        [SerializeField]
        private Color _selectedOutlineColor = new Color(0.0f, 0.85f, 0.85f);

        [SerializeField]
        private bool _interactable = true;

        [SerializeField]
        private UnityEvent _whenActivated = new UnityEvent();

        /// <summary>Raised once per completed press (trigger released on this button).</summary>
        public event Action<XRButton> WhenActivated;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly HashSet<int> _hovering = new HashSet<int>();
        private readonly HashSet<int> _pressing = new HashSet<int>();
        private IPointable Pointable { get; set; }
        private MaterialPropertyBlock _block;
        private bool _selected;
        private bool _started;

        public string ButtonId => string.IsNullOrEmpty(_buttonId) ? name : _buttonId;
        public bool Interactable => _interactable;
        public bool IsSelected => _selected;
        public int ActivationCount { get; private set; }
        public UnityEvent WhenActivatedEvent => _whenActivated;

        public VisualState State =>
            !_interactable ? VisualState.Disabled :
            _pressing.Count > 0 ? VisualState.Pressed :
            _hovering.Count > 0 ? VisualState.Hover : VisualState.Normal;

        protected virtual void Awake()
        {
            Pointable = _pointable as IPointable;
            _block = new MaterialPropertyBlock();
        }

        protected virtual void Start()
        {
            if (Pointable == null)
            {
                Debug.LogError($"[{nameof(XRButton)}] '{name}' has no IPointable assigned.", this);
                enabled = false;
                return;
            }
            _started = true;
            ApplyInteractable();
            OnEnable();
        }

        protected virtual void OnEnable()
        {
            if (!_started)
            {
                return;
            }
            Pointable.WhenPointerEventRaised -= HandlePointerEvent;
            Pointable.WhenPointerEventRaised += HandlePointerEvent;
            RefreshVisuals();
        }

        protected virtual void OnDisable()
        {
            if (_started && Pointable != null)
            {
                Pointable.WhenPointerEventRaised -= HandlePointerEvent;
            }
            _hovering.Clear();
            _pressing.Clear();
            if (_outline != null)
            {
                _outline.enabled = false; // the outline is a sibling, so hide it with the button
            }
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    _hovering.Add(evt.Identifier);
                    break;
                case PointerEventType.Unhover:
                    _hovering.Remove(evt.Identifier);
                    break;
                case PointerEventType.Select:
                    _pressing.Add(evt.Identifier);
                    break;
                case PointerEventType.Unselect:
                    if (_pressing.Remove(evt.Identifier))
                    {
                        Activate();
                    }
                    break;
                case PointerEventType.Cancel:
                    _hovering.Remove(evt.Identifier);
                    _pressing.Remove(evt.Identifier);
                    break;
                default:
                    return;
            }
            RefreshVisuals();
        }

        /// <summary>Performs the button action (also callable from code, e.g. accessibility or tests).</summary>
        public void Activate()
        {
            if (!_interactable || !isActiveAndEnabled)
            {
                return;
            }
            ActivationCount++;
            Debug.Log($"[TrustBreach.Button] activated '{ButtonId}'", this);
            _whenActivated.Invoke();
            WhenActivated?.Invoke(this);
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            if (!interactable)
            {
                _hovering.Clear();
                _pressing.Clear();
            }
            ApplyInteractable();
            RefreshVisuals();
        }

        /// <summary>Marks the button as the participant's chosen option (persistent outline).</summary>
        public void SetSelected(bool selected)
        {
            _selected = selected;
            RefreshVisuals();
        }

        private void ApplyInteractable()
        {
            // A disabled RayInteractable is ignored by ray interactors, so hidden/locked buttons cannot be hit.
            if (_pointable is Behaviour behaviour)
            {
                behaviour.enabled = _interactable;
            }
        }

        private void RefreshVisuals()
        {
            if (_block == null)
            {
                return;
            }
            Color color;
            switch (State)
            {
                case VisualState.Disabled:
                    color = _selected ? Color.Lerp(_normalColor, _disabledColor, 0.35f) : _disabledColor;
                    break;
                case VisualState.Pressed:
                    color = Color.Lerp(_normalColor, Color.black, 0.35f);
                    break;
                case VisualState.Hover:
                    color = Color.Lerp(_normalColor, Color.white, 0.3f);
                    break;
                default:
                    color = _normalColor;
                    break;
            }
            SetColor(_background, color);

            if (_outline != null)
            {
                bool showOutline = _selected || State == VisualState.Hover || State == VisualState.Pressed;
                _outline.enabled = showOutline;
                SetColor(_outline, _selected ? _selectedOutlineColor : _hoverOutlineColor);
            }
        }

        private void SetColor(Renderer target, Color color)
        {
            if (target == null)
            {
                return;
            }
            target.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            target.SetPropertyBlock(_block);
        }

        #region Inject

        public void InjectAllXRButton(IPointable pointable, Renderer background, string buttonId, Color normalColor)
        {
            InjectPointable(pointable);
            _background = background;
            _buttonId = buttonId;
            _normalColor = normalColor;
        }

        public void InjectPointable(IPointable pointable)
        {
            _pointable = pointable as UnityEngine.Object;
            Pointable = pointable;
        }

        public void InjectOptionalOutline(Renderer outline)
        {
            _outline = outline;
        }

        #endregion
    }
}
