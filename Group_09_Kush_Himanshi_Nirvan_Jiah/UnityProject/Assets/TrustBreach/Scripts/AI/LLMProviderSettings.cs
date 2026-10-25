using UnityEngine;

namespace TrustBreach.AI
{
    /// <summary>
    /// Non-secret configuration for an optional model provider. There is deliberately no API-key field: a provider
    /// implementation must read its key at runtime from the environment variable named here. Disabled by default,
    /// so the encounter runs entirely on the offline scripted engine.
    /// </summary>
    [CreateAssetMenu(menuName = "TrustBreach/LLM Provider Settings", fileName = "LLMProviderSettings")]
    public class LLMProviderSettings : ScriptableObject
    {
        [SerializeField, Tooltip("Off by default. When off, or when the registered provider is unavailable, scripted lines are used.")]
        private bool _enabled;

        [SerializeField, Tooltip("Name of the environment variable holding the API key. The key itself is never stored in the project.")]
        private string _apiKeyEnvironmentVariable = "TRUSTBREACH_LLM_API_KEY";

        [SerializeField, Tooltip("Model identifier passed to the provider implementation.")]
        private string _model = string.Empty;

        [SerializeField, Min(0.25f), Tooltip("Maximum time to wait for a generated line before falling back to the script.")]
        private float _timeoutSeconds = 2.5f;

        [SerializeField, Range(80, 400)]
        private int _maxCharacters = 280;

        public bool Enabled => _enabled;
        public string ApiKeyEnvironmentVariable => _apiKeyEnvironmentVariable;
        public string Model => _model;
        public float TimeoutSeconds => _timeoutSeconds;
        public int MaxCharacters => _maxCharacters;

        /// <summary>True when the key variable is set in this process (the value is never read or logged here).</summary>
        public bool HasApiKeyInEnvironment =>
            !string.IsNullOrEmpty(_apiKeyEnvironmentVariable) &&
            !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(_apiKeyEnvironmentVariable));
    }
}
