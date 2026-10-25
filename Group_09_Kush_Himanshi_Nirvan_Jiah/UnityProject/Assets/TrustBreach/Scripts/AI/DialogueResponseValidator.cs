using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrustBreach.AI
{
    public readonly struct ValidationResult
    {
        public readonly bool Valid;
        public readonly string Reason;

        public ValidationResult(bool valid, string reason)
        {
            Valid = valid;
            Reason = reason;
        }

        public static ValidationResult Ok => new ValidationResult(true, "ok");
        public static ValidationResult Reject(string reason) => new ValidationResult(false, reason);
    }

    /// <summary>
    /// Scenario boundary check for model-generated NPC lines. A line is rejected (and the scripted line used instead) if
    /// it is empty or too long, implies an action outside the node's permitted set, contains links, breaks the
    /// in-character persona or reveals the exercise before the debrief, or asks for data outside the training scenario.
    /// </summary>
    public static class DialogueResponseValidator
    {
        public const int DefaultMaxCharacters = 300;

        // Revealing the exercise before the debrief, or breaking character.
        private static readonly string[] RevealTerms =
        {
            "phish", "scam", "social engineer", "impostor", "imposter", "malicious", "hacker", "i'm lying", "i am lying",
            "pretend", "this is a test", "simulation", "training exercise", "as an ai", "language model"
        };

        // Personal / financial data the scenario never asks for.
        private static readonly string[] OutOfScopeData =
        {
            "credit card", "card number", "bank", "social security", "national insurance", "home address", "date of birth",
            "mother's maiden", "pin code", "one-time code", "2fa code", "mfa code"
        };

        private static readonly string[] Profanity = { "fuck", "shit", "bitch", "bastard", "damn you" };

        private static readonly Regex Link = new Regex(@"(https?://|www\.|\b[a-z0-9-]+\.(com|net|org|co|io|xyz)\b)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ValidationResult Validate(LLMDialogueResult result, LLMDialogueRequest request)
        {
            if (!result.Success)
            {
                return ValidationResult.Reject($"provider failure: {result.Error}");
            }
            string text = result.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return ValidationResult.Reject("empty line");
            }
            int max = request.MaxCharacters > 0 ? request.MaxCharacters : DefaultMaxCharacters;
            if (text.Length > max)
            {
                return ValidationResult.Reject($"too long ({text.Length} > {max})");
            }

            string action = string.IsNullOrEmpty(result.Action) ? "None" : result.Action;
            if (action != "None" && (request.PermittedActions == null || !request.PermittedActions.Contains(action)))
            {
                return ValidationResult.Reject($"action '{action}' not permitted at node '{request.NodeId}'");
            }

            string lower = text.ToLowerInvariant();
            string reveal = RevealTerms.FirstOrDefault(lower.Contains);
            if (reveal != null)
            {
                return ValidationResult.Reject($"breaks persona / reveals exercise ('{reveal}')");
            }
            string data = OutOfScopeData.FirstOrDefault(lower.Contains);
            if (data != null)
            {
                return ValidationResult.Reject($"requests out-of-scope data ('{data}')");
            }
            if (Profanity.Any(lower.Contains))
            {
                return ValidationResult.Reject("profanity");
            }
            if (Link.IsMatch(text))
            {
                return ValidationResult.Reject("contains a link or domain");
            }
            if (text.IndexOf('<') >= 0 || text.IndexOf('>') >= 0)
            {
                return ValidationResult.Reject("contains markup");
            }
            return ValidationResult.Ok;
        }
    }
}
