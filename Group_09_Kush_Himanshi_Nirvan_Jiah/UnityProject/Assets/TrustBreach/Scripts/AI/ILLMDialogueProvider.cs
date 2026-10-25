using System;
using System.Threading;
using System.Threading.Tasks;

namespace TrustBreach.AI
{
    /// <summary>Where a delivered NPC line came from (recorded in telemetry).</summary>
    public enum DialogueContentSource
    {
        Scripted,
        Model
    }

    /// <summary>
    /// What a model may be asked to do: rephrase one NPC line for the current dialogue node, staying inside the
    /// scenario. The request carries scenario identifiers and the scripted baseline only, never participant PII.
    /// </summary>
    [Serializable]
    public struct LLMDialogueRequest
    {
        public string EncounterId;
        public string NodeId;
        /// <summary>The deterministic scripted line; also the fallback.</summary>
        public string ScriptedLine;
        public string PersuasionStrategy;
        public int EscalationLevel;
        /// <summary>Scenario actions the line is allowed to imply (e.g. "DirectToPasswordKiosk"); "None" is always allowed.</summary>
        public string[] PermittedActions;
        /// <summary>Previous choice classes only, e.g. "AskIdentification,Challenge".</summary>
        public string ConversationSummary;
        public int MaxCharacters;
    }

    [Serializable]
    public struct LLMDialogueResult
    {
        public bool Success;
        public string Text;
        /// <summary>The scenario action the model claims its line implies; must be in the request's permitted list.</summary>
        public string Action;
        public string Error;

        public static LLMDialogueResult Failed(string error) => new LLMDialogueResult { Success = false, Error = error };
    }

    /// <summary>
    /// Optional, separately configured source of model-generated NPC lines. The offline scripted engine is always the
    /// baseline: every result is validated by <see cref="DialogueResponseValidator"/> and discarded on failure, timeout
    /// or unavailability. Implementations must read credentials at runtime (e.g. from an environment variable) and must
    /// never store keys in Unity assets.
    /// </summary>
    public interface ILLMDialogueProvider
    {
        string ProviderName { get; }

        /// <summary>True only when the provider is configured and believes it can be reached.</summary>
        bool IsAvailable { get; }

        Task<LLMDialogueResult> GenerateLineAsync(LLMDialogueRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Default provider: never available, so the scripted engine is used. Requires no network.</summary>
    public sealed class OfflineDialogueProvider : ILLMDialogueProvider
    {
        public string ProviderName => "offline";
        public bool IsAvailable => false;

        public Task<LLMDialogueResult> GenerateLineAsync(LLMDialogueRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(LLMDialogueResult.Failed("offline provider: model generation disabled"));
    }

    /// <summary>
    /// Process-wide provider slot. Defaults to <see cref="OfflineDialogueProvider"/>. A real provider can be registered
    /// from code at startup once it is configured securely; nothing in this project registers one.
    /// </summary>
    public static class LLMDialogueProviderRegistry
    {
        public static ILLMDialogueProvider Current { get; private set; } = new OfflineDialogueProvider();

        public static void Register(ILLMDialogueProvider provider)
        {
            Current = provider ?? new OfflineDialogueProvider();
        }

        public static void ResetToOffline() => Current = new OfflineDialogueProvider();
    }
}
