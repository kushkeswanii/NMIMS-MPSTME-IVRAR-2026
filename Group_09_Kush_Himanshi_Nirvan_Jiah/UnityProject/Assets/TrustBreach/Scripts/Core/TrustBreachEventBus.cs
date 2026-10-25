using System;
using UnityEngine;

namespace TrustBreach.Core
{
    /// <summary>
    /// SDK-agnostic interaction phases. The XR layer translates its own events into these so that
    /// Telemetry, Dialogue and AI systems never take a dependency on a specific XR SDK.
    /// </summary>
    public enum InteractionPhase
    {
        HoverStart,
        HoverEnd,
        SelectStart,
        SelectEnd,
        Cancel
    }

    public readonly struct InteractionEvent
    {
        /// <summary>Stable, human-readable id of the target (used as the telemetry key).</summary>
        public readonly string TargetId;
        public readonly InteractionPhase Phase;
        /// <summary>Id of the interactor (ray, grab, poke...) that caused the event.</summary>
        public readonly int InteractorId;
        /// <summary>World-space pose of the pointer at the time of the event.</summary>
        public readonly Pose PointerPose;
        /// <summary>Seconds since startup, unaffected by time scale.</summary>
        public readonly double Timestamp;

        public InteractionEvent(string targetId, InteractionPhase phase, int interactorId, Pose pointerPose, double timestamp)
        {
            TargetId = targetId;
            Phase = phase;
            InteractorId = interactorId;
            PointerPose = pointerPose;
            Timestamp = timestamp;
        }

        public override string ToString() => $"{Timestamp:F3}s {TargetId} {Phase} (interactor {InteractorId})";
    }

    /// <summary>
    /// Process-wide event bus that decouples XR interaction sources from research systems
    /// (telemetry logging, dialogue branching, adaptive AI).
    /// </summary>
    public static class TrustBreachEventBus
    {
        public static event Action<InteractionEvent> InteractionRaised;

        public static void Raise(in InteractionEvent interactionEvent)
        {
            InteractionRaised?.Invoke(interactionEvent);
        }

        // Clears stale subscribers when Enter Play Mode Options skip the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            InteractionRaised = null;
        }
    }
}
