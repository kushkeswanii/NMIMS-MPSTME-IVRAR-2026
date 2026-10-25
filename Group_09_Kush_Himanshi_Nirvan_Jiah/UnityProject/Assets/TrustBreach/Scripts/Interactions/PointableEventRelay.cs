using System.Collections.Generic;
using Oculus.Interaction;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Interactions
{
    /// <summary>
    /// Forwards Interaction SDK pointer events (ray, grab, poke) from an <see cref="IPointable"/> to the
    /// <see cref="TrustBreachEventBus"/>, and tints a renderer so hover/select states are visible
    /// during Simulator and headset testing.
    /// </summary>
    public class PointableEventRelay : MonoBehaviour
    {
        [SerializeField, Interface(typeof(IPointable))]
        private Object _pointable;

        [SerializeField, Tooltip("Stable id written to telemetry. Defaults to the GameObject name.")]
        private string _targetId;

        [SerializeField, Optional]
        private Renderer _renderer;

        [SerializeField]
        private Color _hoverColor = new Color(1f, 0.82f, 0.2f);

        [SerializeField]
        private Color _selectColor = new Color(0.25f, 0.95f, 0.45f);

        [SerializeField]
        private bool _logToConsole = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly HashSet<int> _hovering = new HashSet<int>();
        private readonly HashSet<int> _selecting = new HashSet<int>();
        private IPointable Pointable { get; set; }
        private MaterialPropertyBlock _propertyBlock;
        private Color _normalColor = Color.white;

        public string TargetId => string.IsNullOrEmpty(_targetId) ? name : _targetId;

        protected virtual void Awake()
        {
            Pointable = _pointable as IPointable;
            _propertyBlock = new MaterialPropertyBlock();
            if (_renderer != null && _renderer.sharedMaterial != null && _renderer.sharedMaterial.HasProperty(BaseColorId))
            {
                _normalColor = _renderer.sharedMaterial.GetColor(BaseColorId);
            }
        }

        protected virtual void OnEnable()
        {
            if (Pointable == null)
            {
                Debug.LogError($"[{nameof(PointableEventRelay)}] '{name}' has no IPointable assigned.", this);
                enabled = false;
                return;
            }
            Pointable.WhenPointerEventRaised += HandlePointerEvent;
        }

        protected virtual void OnDisable()
        {
            if (Pointable != null)
            {
                Pointable.WhenPointerEventRaised -= HandlePointerEvent;
            }
            _hovering.Clear();
            _selecting.Clear();
            ApplyTint();
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            InteractionPhase phase;
            switch (evt.Type)
            {
                case PointerEventType.Hover:
                    _hovering.Add(evt.Identifier);
                    phase = InteractionPhase.HoverStart;
                    break;
                case PointerEventType.Unhover:
                    _hovering.Remove(evt.Identifier);
                    phase = InteractionPhase.HoverEnd;
                    break;
                case PointerEventType.Select:
                    _selecting.Add(evt.Identifier);
                    phase = InteractionPhase.SelectStart;
                    break;
                case PointerEventType.Unselect:
                    _selecting.Remove(evt.Identifier);
                    phase = InteractionPhase.SelectEnd;
                    break;
                case PointerEventType.Cancel:
                    _hovering.Remove(evt.Identifier);
                    _selecting.Remove(evt.Identifier);
                    phase = InteractionPhase.Cancel;
                    break;
                default:
                    // Move events fire every frame; telemetry samples poses separately.
                    return;
            }

            var interactionEvent = new InteractionEvent(TargetId, phase, evt.Identifier, evt.Pose, Time.realtimeSinceStartupAsDouble);
            TrustBreachEventBus.Raise(interactionEvent);
            ApplyTint();

            if (_logToConsole)
            {
                Debug.Log($"[TrustBreach.Interaction] {interactionEvent}", this);
            }
        }

        private void ApplyTint()
        {
            if (_renderer == null || _propertyBlock == null)
            {
                return;
            }

            Color color = _selecting.Count > 0 ? _selectColor : _hovering.Count > 0 ? _hoverColor : _normalColor;
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        #region Inject

        public void InjectAllPointableEventRelay(IPointable pointable, string targetId)
        {
            InjectPointable(pointable);
            _targetId = targetId;
        }

        public void InjectPointable(IPointable pointable)
        {
            _pointable = pointable as Object;
            Pointable = pointable;
        }

        public void InjectOptionalRenderer(Renderer renderer)
        {
            _renderer = renderer;
        }

        #endregion
    }
}
