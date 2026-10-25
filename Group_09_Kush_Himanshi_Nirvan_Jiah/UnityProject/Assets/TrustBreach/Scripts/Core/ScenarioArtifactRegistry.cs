using System.Collections.Generic;
using UnityEngine;

namespace TrustBreach.Core
{
    /// <summary>Lookup of the scenario artifacts currently enabled in the loaded scene(s).</summary>
    public static class ScenarioArtifactRegistry
    {
        private static readonly Dictionary<string, ScenarioArtifact> Artifacts = new Dictionary<string, ScenarioArtifact>();

        public static IReadOnlyCollection<ScenarioArtifact> All => Artifacts.Values;

        public static bool TryGet(string artifactId, out ScenarioArtifact artifact) =>
            Artifacts.TryGetValue(artifactId, out artifact);

        internal static void Register(ScenarioArtifact artifact)
        {
            if (Artifacts.TryGetValue(artifact.ArtifactId, out ScenarioArtifact existing) && existing != artifact)
            {
                Debug.LogError($"[TrustBreach] Duplicate scenario artifact id '{artifact.ArtifactId}'.", artifact);
                return;
            }
            Artifacts[artifact.ArtifactId] = artifact;
        }

        internal static void Unregister(ScenarioArtifact artifact)
        {
            if (Artifacts.TryGetValue(artifact.ArtifactId, out ScenarioArtifact existing) && existing == artifact)
            {
                Artifacts.Remove(artifact.ArtifactId);
            }
        }

        // Clears stale entries when Enter Play Mode Options skip the domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Artifacts.Clear();
        }
    }
}
