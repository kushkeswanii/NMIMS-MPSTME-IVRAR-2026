using System.Runtime.CompilerServices;

// Phase 4 analytics tooling reuses the office build kit (screens, buttons, text) instead of duplicating it.
[assembly: InternalsVisibleTo("TrustBreach.Analytics.Editor")]
[assembly: InternalsVisibleTo("TrustBreach.Analytics.Tests")]
[assembly: InternalsVisibleTo("TrustBreach.Experience.Editor")] // Phase 5 participant-flow builder
