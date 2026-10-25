using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static TrustBreach.Analytics.Dashboard.DashboardUI;

namespace TrustBreach.Analytics.Dashboard
{
    /// <summary>
    /// TRUST//BREACH | BEHAVIORAL INTELLIGENCE: instructor desktop dashboard (Screen Space canvas, mouse/keyboard).
    /// Reads one dataset at a time (REAL, DEMO DATA, TEST / PRACTICE) from <see cref="ResearchStore"/>; datasets are never
    /// combined. Every rate shows its numerator and denominator; hover any element for its definition.
    /// The UI is generated at runtime from code (no hand-assembled scene objects).
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class ResearchDashboardController : MonoBehaviour
    {
        private const float W = 1920f;
        private const int TableRows = 11;
        private const int TimelineRows = 13;
        private const string DefaultFooter =
            "Hover any number for its definition. Metrics describe behaviour inside this simulation; the index is descriptive, not a validated measure.";

        [SerializeField, Tooltip("Dataset shown when the dashboard opens.")]
        private string _initialDataset = ResearchSchema.Real;
        [SerializeField, Tooltip("Optional storage root override (tests). Empty = persistentDataPath/TrustBreach/research.")]
        private string _storeRoot;

        private ResearchStore _store;
        private ResearchStore.LoadResult _load = new ResearchStore.LoadResult();
        private List<SessionRecord> _visible = new List<SessionRecord>();
        private AggregateMetrics _metrics;
        private string _status;
        private int _tablePage;
        private int _timelinePage;
        private Action _pendingConfirm;
        private bool _built;

        // Filters ("" = all).
        public string Dataset { get; private set; }
        public string SessionFilter { get; private set; } = string.Empty;
        public string ScenarioFilter { get; private set; } = string.Empty;
        public string OutcomeFilter { get; private set; } = string.Empty;
        public string SelectedSessionId { get; private set; }

        // Exposed for automated checks.
        public IReadOnlyList<SessionRecord> VisibleSessions => _visible;
        public AggregateMetrics Metrics => _metrics;
        public ResearchStore.LoadResult LastLoad => _load;
        public bool EmptyStateVisible => _empty != null && _empty.activeSelf;
        public bool ConfirmVisible => _confirm != null && _confirm.activeSelf;
        public string StatusMessage => _status;
        public string LastExportFolder { get; private set; }
        public readonly Dictionary<string, TextMeshProUGUI> KpiValues = new Dictionary<string, TextMeshProUGUI>();

        // UI references.
        private RectTransform _root;
        private readonly Dictionary<string, Button> _datasetButtons = new Dictionary<string, Button>();
        private Button _deleteButton, _generateButton, _clearButton;
        private TextMeshProUGUI _banner, _footer, _sessionFilterText, _scenarioFilterText, _outcomeFilterText;
        private readonly Dictionary<string, TextMeshProUGUI> _kpiDetails = new Dictionary<string, TextMeshProUGUI>();
        private RectTransform _outcomePanel, _scenarioPanel, _techniquePanel, _tablePanel, _timelinePanel;
        private GameObject _empty, _confirm;
        private TextMeshProUGUI _emptyText, _confirmText;

        protected virtual void Awake()
        {
            _store = new ResearchStore(string.IsNullOrEmpty(_storeRoot) ? null : _storeRoot);
            Dataset = ResearchStore.IsValidDataset(_initialDataset) ? _initialDataset : ResearchSchema.Real;
        }

        protected virtual void Start()
        {
            Build();
            Reload();
        }

        // =============================================================================================================
        // Public actions (buttons and automated checks)
        // =============================================================================================================
        public void UseStoreRoot(string root)
        {
            _store = new ResearchStore(root);
            if (_built) Reload();
        }

        public void SetDataset(string dataset)
        {
            if (!ResearchStore.IsValidDataset(dataset)) return;
            Dataset = dataset;
            SessionFilter = ScenarioFilter = OutcomeFilter = string.Empty;
            SelectedSessionId = null;
            _status = null;
            Reload();
        }

        public void Reload()
        {
            _load = _store.LoadAll(Dataset);
            Apply();
        }

        public void SetFilters(string session, string scenario, string outcome)
        {
            SessionFilter = session ?? string.Empty;
            ScenarioFilter = scenario ?? string.Empty;
            OutcomeFilter = outcome ?? string.Empty;
            _tablePage = 0;
            Apply();
        }

        public void SelectSession(string sessionId)
        {
            SelectedSessionId = sessionId;
            _timelinePage = 0;
            Apply();
        }

        public ResearchExporter.Result ExportCurrent()
        {
            if (_load.Sessions.Count == 0)
            {
                _status = "Nothing to export: this dataset has no sessions.";
                Apply();
                return null;
            }
            ResearchExporter.Result result = ResearchExporter.Export(_store, Dataset, _load.Sessions);
            LastExportFolder = result.Folder;
            _status = $"Exported {result.Sessions} sessions / {result.Events} events (all sessions of this dataset, unfiltered) to {result.Folder}";
            Apply();
            return result;
        }

        public void OpenDataFolder()
        {
            string folder = LastExportFolder ?? _store.DatasetDir(Dataset);
            Directory.CreateDirectory(folder);
            Application.OpenURL(new Uri(folder).AbsoluteUri);
        }

        public void RequestDeleteSelected()
        {
            if (string.IsNullOrEmpty(SelectedSessionId)) return;
            string id = SelectedSessionId;
            Confirm($"Permanently delete session <b>{id}</b> from the {DatasetLabel(Dataset)} dataset?\nExports already written are not changed.",
                () =>
                {
                    _store.Delete(Dataset, id);
                    _status = $"Deleted session {id}.";
                    SelectedSessionId = null;
                    if (SessionFilter == id) SessionFilter = string.Empty;
                    Reload();
                });
        }

        public void GenerateDemo()
        {
            if (Dataset != ResearchSchema.Demo) return;
            int n = DemoDataGenerator.WriteTo(_store);
            _status = $"Generated {n} synthetic DEMO sessions (seed {DemoDataGenerator.DefaultSeed}).";
            Reload();
        }

        public void RequestClearDataset()
        {
            if (Dataset == ResearchSchema.Real) return; // real sessions are deleted one at a time, deliberately
            string dataset = Dataset;
            Confirm($"Delete <b>all</b> {DatasetLabel(dataset)} sessions? REAL participant data is not affected.", () =>
            {
                int n = _store.ClearSessions(dataset);
                _status = $"Cleared {n} {DatasetLabel(dataset)} sessions.";
                SelectedSessionId = null;
                Reload();
            });
        }

        public void ConfirmPending()
        {
            Action action = _pendingConfirm;
            _pendingConfirm = null;
            _confirm.SetActive(false);
            action?.Invoke();
        }

        public void CancelPending()
        {
            _pendingConfirm = null;
            _confirm.SetActive(false);
        }

        // =============================================================================================================
        // Data -> view
        // =============================================================================================================
        private void Apply()
        {
            if (!_built) return;
            IEnumerable<SessionRecord> sessions = _load.Sessions;
            if (!string.IsNullOrEmpty(SessionFilter)) sessions = sessions.Where(s => s.sessionId == SessionFilter);
            _metrics = AggregateMetrics.Compute(sessions.ToList(), NullIfEmpty(ScenarioFilter), NullIfEmpty(OutcomeFilter));
            HashSet<string> kept = new HashSet<string>(_metrics.PerSession.Select(m => m.SessionId));
            _visible = sessions.Where(s => kept.Contains(s.sessionId)).ToList();
            if (SelectedSessionId == null || _visible.All(s => s.sessionId != SelectedSessionId))
            {
                SelectedSessionId = _visible.LastOrDefault()?.sessionId;
            }
            RenderHeader();
            RenderKpis();
            RenderOutcomes();
            RenderScenarios();
            RenderTechniques();
            RenderTable();
            RenderTimeline();
            RenderEmptyState();
        }

        private void RenderHeader()
        {
            foreach (KeyValuePair<string, Button> b in _datasetButtons)
            {
                b.Value.image.color = b.Key == Dataset ? (b.Key == ResearchSchema.Demo ? Amber * 0.75f : Cyan * 0.55f) : Card;
            }
            _generateButton.gameObject.SetActive(Dataset == ResearchSchema.Demo);
            _clearButton.gameObject.SetActive(Dataset != ResearchSchema.Real);
            SetLabel(_clearButton, Dataset == ResearchSchema.Demo ? "CLEAR DEMO" : "CLEAR TEST");
            _deleteButton.interactable = !string.IsNullOrEmpty(SelectedSessionId);

            string notice;
            switch (Dataset)
            {
                case ResearchSchema.Demo:
                    notice = "<color=#FFB547><b>DEMO DATA</b></color>  -  synthetic sessions generated for demonstration (deterministic seed). Not research findings.";
                    break;
                case ResearchSchema.Test:
                    notice = "<color=#A9B3C1><b>TEST / PRACTICE</b></color>  -  DEMO-scene play-throughs and automated validation runs. Not participant data.";
                    break;
                default:
                    notice = "<color=#38E1FF><b>REAL</b></color>  -  anonymous sessions recorded with consent in the participant scene.";
                    break;
            }
            notice += $"   <color=#717C8C>{_load.Sessions.Count} sessions on disk · {_store.SessionsDir(Dataset)}</color>";
            if (_load.Quarantined.Count > 0) notice += $"   <color=#FF5A4E>{_load.Quarantined.Count} unreadable file(s) moved to quarantine</color>";
            if (_load.Unsupported.Count > 0) notice += $"   <color=#FFB547>{_load.Unsupported.Count} file(s) with an unsupported schema skipped</color>";
            _banner.text = notice;
            _footer.text = _status ?? DefaultFooter;

            _sessionFilterText.text = string.IsNullOrEmpty(SessionFilter) ? "All sessions" : SessionFilter;
            _scenarioFilterText.text = string.IsNullOrEmpty(ScenarioFilter) ? "All scenarios" : ResearchSchema.ScenarioLabel(ScenarioFilter);
            _outcomeFilterText.text = string.IsNullOrEmpty(OutcomeFilter) ? "All outcomes" : OutcomeFilter;
        }

        private void RenderKpis()
        {
            AggregateMetrics a = _metrics;
            string scope = string.IsNullOrEmpty(ScenarioFilter) ? "4 scenarios" : ResearchSchema.ScenarioLabel(ScenarioFilter);
            Kpi("sessions", a.Sessions.ToString(), a.Sessions == 1 ? "session" : "sessions in view");
            Kpi("risky", Pct(a.RiskyActionRate), $"{a.RiskyActions} of {a.ClassifiedActions} classified actions");
            Kpi("recovery", Pct(a.RecoveryRate), $"{a.RecoveredSessions} of {a.SessionsWithRiskyAction} sessions with a risky action");
            Kpi("completion", Pct(a.CompletionRate), $"{a.ScenarioCompletions} of {a.ScenarioSlots} ({scope})");
            Kpi("latency", double.IsNaN(a.MedianLatency) ? "—" : $"{a.MedianLatency:0.0} s",
                a.Latencies.Count == 0 ? "no timed replies" : $"n = {a.Latencies.Count} replies · IQR {a.LatencyP25:0.0}–{a.LatencyP75:0.0} s");
            Kpi("index", double.IsNaN(a.MeanIndex) ? "—" : $"{a.MeanIndex:0}", $"{a.Indices.Count} of {a.Sessions} sessions scored · not validated");
        }

        private void Kpi(string key, string value, string detail)
        {
            KpiValues[key].text = value;
            _kpiDetails[key].text = detail;
        }

        private void RenderOutcomes()
        {
            RectTransform body = ClearBody(_outcomePanel);
            AggregateMetrics a = _metrics;
            float y = 4f;
            foreach (string outcome in ResearchSchema.Outcomes)
            {
                int final = a.OutcomeCounts[outcome];
                int initial = a.InitialOutcomeCounts[outcome];
                Bar(body, outcome, 0f, y, OutcomeLabel(outcome), Ratio(final, a.Sessions), OutcomeColor(outcome),
                    $"{final}  ({Pct(Ratio(final, a.Sessions))})", () => $"{OutcomeLabel(outcome)}: {final} of {a.Sessions} sessions by final outcome. " +
                    $"Before any corrective step: {initial}. {OutcomeDefinition(outcome)}");
                y += 40f;
            }
            int before = a.InitialOutcomeCounts[ResearchSchema.Compromised];
            Label(body, "Initial", $"Before recovery: <b>{before}</b> of {a.Sessions} sessions had at least one risky action " +
                $"({Pct(Ratio(before, a.Sessions))}); {a.RecoveredSessions} recovered.", 0f, y + 4f, 580f, 46f, 15f, InkSecondary);
        }

        private void RenderScenarios()
        {
            RectTransform body = ClearBody(_scenarioPanel);
            AggregateMetrics a = _metrics;
            float y = 4f;
            IEnumerable<string> channels = ResearchSchema.ScenarioChannels.Concat(a.Scenarios.Keys.Where(k => k == "verbal"));
            foreach (string channel in channels.Where(c => string.IsNullOrEmpty(ScenarioFilter) || c == ScenarioFilter))
            {
                a.Scenarios.TryGetValue(channel, out ScenarioStats s);
                s ??= new ScenarioStats();
                string done = channel == "verbal" ? "" : $" · done {s.SessionsCompleted}/{a.Sessions}";
                Bar(body, channel, 0f, y, ResearchSchema.ScenarioLabel(channel), s.UnsafeRate, RiskyMark,
                    s.Classified == 0 ? $"no data{done}" : $"{s.RiskyActions}/{s.Classified} risky{done}",
                    () => $"{ResearchSchema.ScenarioLabel(channel)}: unsafe-action rate = risky completed actions ({s.RiskyActions}) / " +
                          $"Safe+Unsafe completed actions ({s.Classified}); {s.NeutralActions} neutral actions excluded. " +
                          (channel == "verbal" ? "Telling Alex the password." : $"Completed (terminal action reached) in {s.SessionsCompleted} of {a.Sessions} sessions."));
                y += 40f;
            }
            Label(body, "Note", "Bar = share of classified actions that were risky (lower is safer).", 0f, y + 4f, 580f, 40f, 15f, InkMuted);
        }

        private void RenderTechniques()
        {
            RectTransform body = ClearBody(_techniquePanel);
            AggregateMetrics a = _metrics;
            float y = 4f;
            string[] order = { "Authority", "Urgency", "TrustBuilding" };
            foreach (string technique in order.Concat(a.Techniques.Keys.Except(order).OrderBy(k => k, StringComparer.Ordinal)))
            {
                a.Techniques.TryGetValue(technique, out TechniqueStats t);
                t ??= new TechniqueStats();
                Bar(body, technique, 0f, y, technique == "TrustBuilding" ? "Trust-building" : technique, t.RiskyRate, RiskyMark,
                    t.Responses == 0 ? "no replies" : $"{t.RiskyResponses}/{t.Responses} risky replies",
                    () => $"{technique}: risky replies to lines using this technique ({t.RiskyResponses}) / all replies to it ({t.Responses}). " +
                          "A risky reply is a promise to comply or a disclosure; it is not the same as a completed risky action.");
                y += 40f;
            }
            y += 6f;
            Label(body, "Agreement", $"Intention vs action: <b>{a.PromisesActedOn}</b> of <b>{a.Promises}</b> promises to comply were followed by " +
                $"the promised risky action ({Pct(Ratio(a.PromisesActedOn, a.Promises))}).", 0f, y, 580f, 46f, 15f, InkSecondary);
            Hint(body.Find("Agreement").gameObject, () =>
                "Intention vs action agreement = promises (\"OK, I'll use the kiosk\") followed by the matching risky action on the same channel / all promises.", ShowHint);
            y += 50f;
            Label(body, "Verification", $"Verification behaviour: <b>{a.VerificationEvents}</b> checks across {a.DialogueChoices} replies " +
                $"({Pct(Ratio(a.VerificationEvents, a.DialogueChoices))}); reported in {a.ReportedSessions} of {a.Sessions} sessions.",
                0f, y, 580f, 46f, 15f, InkSecondary);
            Hint(body.Find("Verification").gameObject, () =>
                "Verification checks = 'ask for ID' and 'verify independently' replies plus visitor-badge verifications. Reported = reported Alex, the email or the kiosk, or handed the USB to IT.", ShowHint);
        }

        private void RenderTable()
        {
            RectTransform body = ClearBody(_tablePanel);
            string[] headers = { "Session", "Started (UTC)", "Outcome", "Before recovery", "Risky / classified", "Done", "Median", "Index" };
            float[] widths = { 190f, 160f, 150f, 150f, 140f, 60f, 70f, 60f };
            float x = 8f;
            for (int i = 0; i < headers.Length; i++)
            {
                Label(body, $"H{i}", headers[i], x, 0f, widths[i], 28f, 14f, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                x += widths[i];
            }
            List<SessionMetrics> rows = _metrics.PerSession.AsEnumerable().Reverse().ToList(); // newest first
            int pages = Math.Max(1, (rows.Count + TableRows - 1) / TableRows);
            _tablePage = Mathf.Clamp(_tablePage, 0, pages - 1);
            float y = 30f;
            foreach (SessionMetrics m in rows.Skip(_tablePage * TableRows).Take(TableRows))
            {
                bool selected = m.SessionId == SelectedSessionId;
                Image row = Box(body, $"Row_{m.SessionId}", 0f, y, 996f, 28f, selected ? CardEdge : new Color(0, 0, 0, 0), true);
                var button = row.gameObject.AddComponent<Button>();
                string id = m.SessionId;
                button.onClick.AddListener(() => SelectSession(id));
                string[] cells =
                {
                    m.SessionId, (m.StartedUtc ?? "").Replace("T", " ").TrimEnd('Z'), $"<color=#{Html(OutcomeColor(m.FinalOutcome))}>■</color> {m.FinalOutcome}",
                    m.InitialOutcome, m.ClassifiedActions == 0 ? "—" : $"{m.RiskyActions} / {m.ClassifiedActions}",
                    $"{m.ScenariosCompleted.Count}/4", double.IsNaN(m.MedianLatency) ? "—" : $"{m.MedianLatency:0.0} s",
                    double.IsNaN(m.Index) ? "—" : $"{m.Index:0}"
                };
                x = 8f;
                for (int i = 0; i < cells.Length; i++)
                {
                    TextMeshProUGUI cell = Label(row.transform, $"C{i}", cells[i], x, 0f, widths[i] - 6f, 28f, 15f, selected ? Ink : InkSecondary);
                    cell.textWrappingMode = TextWrappingModes.NoWrap;
                    x += widths[i];
                }
                Hint(row.gameObject, () => $"Session {id}: click to show its timeline. Done = scenarios that reached a terminal action. " +
                    "Index = descriptive 0-100 summary (weights 0.40 action safety, 0.25 persuasion resistance, 0.15 verification, 0.20 reporting/recovery; missing parts renormalised).", ShowHint);
                y += 30f;
            }
            if (rows.Count > TableRows)
            {
                Label(body, "Page", $"Page {_tablePage + 1} / {pages}  ({rows.Count} sessions, newest first)", 8f, 368f, 400f, 26f, 14f, InkMuted);
                MakeButton(body, "Prev", "<", 860f, 366f, 60f, 28f, Card, () => { _tablePage--; Apply(); });
                MakeButton(body, "Next", ">", 928f, 366f, 60f, 28f, Card, () => { _tablePage++; Apply(); });
            }
        }

        private void RenderTimeline()
        {
            RectTransform body = ClearBody(_timelinePanel);
            SessionRecord s = _visible.FirstOrDefault(v => v.sessionId == SelectedSessionId);
            if (s == null)
            {
                Label(body, "None", "Select a session in the table.", 0f, 0f, 820f, 30f, 16f, InkMuted);
                return;
            }
            SessionMetrics m = SessionScorer.Score(s, NullIfEmpty(ScenarioFilter));
            Label(body, "Head", $"<b>{s.sessionId}</b>  ·  <color=#{Html(OutcomeColor(m.FinalOutcome))}>{m.FinalOutcome}</color>" +
                $"  ·  {s.durationSeconds:0}s  ·  ended: {s.endReason}  ·  scenes: {string.Join(" → ", s.scenes)}", 0f, 0f, 830f, 26f, 15f, Ink);

            // Safe / risky / neutral history strip (letters keep it readable without colour).
            List<ResearchEvent> decisions = s.events.Where(e => e.IsDecision).OrderBy(e => e.seq).ToList();
            float cell = Mathf.Min(26f, 820f / Mathf.Max(1, decisions.Count));
            for (int i = 0; i < decisions.Count; i++)
            {
                (Color color, string glyph) = Mark(decisions[i]);
                Image square = Box(body, $"H{i}", i * cell, 32f, cell - 2f, 22f, color, true);
                if (cell >= 14f) Label(square.transform, "G", glyph, 0f, 0f, cell - 2f, 22f, 12f, Charcoal, TextAlignmentOptions.Center, FontStyles.Bold);
                ResearchEvent d = decisions[i];
                Hint(square.gameObject, () => $"#{d.seq} t={d.t:0.0}s {Describe(d)}", ShowHint);
            }
            Label(body, "Legend", "S safe   R risky action   P promise (risky reply)   N neutral   + corrective", 0f, 58f, 820f, 22f, 13f, InkMuted);

            // Comparison with the dataset in view.
            AggregateMetrics a = _metrics;
            Label(body, "Compare",
                $"vs dataset: risky rate {Pct(m.UnsafeActionRate)} (all {Pct(a.RiskyActionRate)}) · median reply {Sec(m.MedianLatency)} (all {Sec(a.MedianLatency)}) · " +
                $"index {(double.IsNaN(m.Index) ? "—" : m.Index.ToString("0"))} (mean {(double.IsNaN(a.MeanIndex) ? "—" : a.MeanIndex.ToString("0"))})",
                0f, 80f, 830f, 24f, 14f, InkSecondary);

            // Chronological event list.
            List<ResearchEvent> events = s.events.Where(e => e.IsDecision || IsKeyLifecycle(e)).OrderBy(e => e.seq).ToList();
            int pages = Math.Max(1, (events.Count + TimelineRows - 1) / TimelineRows);
            _timelinePage = Mathf.Clamp(_timelinePage, 0, pages - 1);
            float y = 110f;
            foreach (ResearchEvent e in events.Skip(_timelinePage * TimelineRows).Take(TimelineRows))
            {
                (Color color, string glyph) = e.IsDecision ? Mark(e) : (CardEdge, "·");
                Image dot = Box(body, $"E{e.seq}", 0f, y + 3f, 18f, 18f, color);
                Label(dot.transform, "G", glyph, 0f, 0f, 18f, 18f, 11f, Charcoal, TextAlignmentOptions.Center, FontStyles.Bold);
                Label(body, $"T{e.seq}", $"{e.t,7:0.0}s", 24f, y, 80f, 24f, 14f, InkMuted);
                Label(body, $"D{e.seq}", Describe(e), 104f, y, 726f, 24f, 14f, e.IsDecision ? Ink : InkMuted).textWrappingMode = TextWrappingModes.NoWrap;
                y += 20f;
            }
            if (pages > 1)
            {
                Label(body, "Page", $"Events page {_timelinePage + 1} / {pages}", 0f, 368f, 300f, 26f, 14f, InkMuted);
                MakeButton(body, "Prev", "<", 700f, 366f, 60f, 28f, Card, () => { _timelinePage--; Apply(); });
                MakeButton(body, "Next", ">", 768f, 366f, 60f, 28f, Card, () => { _timelinePage++; Apply(); });
            }
        }

        private void RenderEmptyState()
        {
            bool empty = _visible.Count == 0;
            _empty.SetActive(empty);
            if (!empty) return;
            if (_load.Sessions.Count > 0)
            {
                _emptyText.text = "<b>No sessions match the current filters.</b>\nClear the filters to see all sessions in this dataset.";
                return;
            }
            switch (Dataset)
            {
                case ResearchSchema.Demo:
                    _emptyText.text = "<b>No DEMO DATA yet.</b>\nPress <b>GENERATE DEMO</b> to create synthetic sessions for a walkthrough.\nDemo data is kept apart from REAL and TEST data.";
                    break;
                case ResearchSchema.Test:
                    _emptyText.text = "<b>No TEST / PRACTICE sessions.</b>\nPlay the DEMO scene and agree to recording on the research panel, or run the automated validation.";
                    break;
                default:
                    _emptyText.text = "<b>No participant sessions recorded yet.</b>\nSessions appear here after a participant agrees to research recording in " +
                                      "<b>TrustBreach_Phase3_Adversary</b> and makes at least one decision.\nNothing is estimated or filled in: the dashboard only shows recorded data.";
                    break;
            }
        }

        // =============================================================================================================
        // Layout
        // =============================================================================================================
        private void Build()
        {
            _root = (RectTransform)transform;
            Box(_root, "Background", 0f, 0f, W, 1080f, Charcoal);

            // Header.
            Box(_root, "Header", 0f, 0f, W, 64f, Navy);
            Label(_root, "Title", "<color=#38E1FF>TRUST//BREACH</color>  <color=#717C8C>|</color>  BEHAVIORAL INTELLIGENCE", 24f, 0f, 600f, 64f, 21f, Ink,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold).textWrappingMode = TextWrappingModes.NoWrap;
            float x = 640f;
            foreach ((string dataset, string label) in new[] { (ResearchSchema.Real, "REAL"), (ResearchSchema.Demo, "DEMO DATA"), (ResearchSchema.Test, "TEST / PRACTICE") })
            {
                float w = label.Length * 10f + 32f;
                Button b = MakeButton(_root, $"Dataset_{dataset}", label, x, 14f, w, 36f, Card, () => SetDataset(dataset));
                Hint(b.gameObject, () => $"Show the {DatasetLabel(dataset)} dataset. Datasets are stored in separate folders and are never combined.", ShowHint);
                _datasetButtons[dataset] = b;
                x += w + 8f;
            }
            float rx = W - 24f;
            Button Right(string name, string label, float w, Color color, Action action, string hint)
            {
                rx -= w;
                Button b = MakeButton(_root, name, label, rx, 14f, w, 36f, color, action, 15f);
                Hint(b.gameObject, () => hint, ShowHint);
                rx -= 8f;
                return b;
            }
            _clearButton = Right("ClearDataset", "CLEAR DEMO", 120f, Hex("#5A2320"), RequestClearDataset, "Delete every session of this non-REAL dataset (asks for confirmation).");
            _generateButton = Right("GenerateDemo", "GENERATE DEMO", 150f, Hex("#5C4313"), GenerateDemo, "Create deterministic synthetic sessions in the DEMO dataset only.");
            _deleteButton = Right("DeleteSession", "DELETE SESSION", 150f, Hex("#5A2320"), RequestDeleteSelected, "Delete the selected session file (asks for confirmation).");
            Right("OpenFolder", "OPEN FOLDER", 130f, Card, OpenDataFolder, "Open the last export folder, or this dataset's data folder.");
            Right("Export", "EXPORT", 100f, Hex("#0F4A5C"), () => ExportCurrent(), "Export all sessions of this dataset: per-session JSON, events.csv, session_summary.csv, aggregate_metrics.csv, manifest.");
            Right("Refresh", "REFRESH", 100f, Card, Reload, "Reload session files from disk.");

            // Dataset banner.
            Box(_root, "BannerBar", 0f, 64f, W, 34f, Hex("#111722"));
            _banner = Label(_root, "Banner", "", 24f, 64f, W - 48f, 34f, 15f, InkSecondary);

            // Filters.
            float fy = 106f;
            _sessionFilterText = Filter("Session", 24f, fy, 330f, () => Cycle(SessionFilter, _load.Sessions.Select(s => s.sessionId).ToList(), -1),
                () => Cycle(SessionFilter, _load.Sessions.Select(s => s.sessionId).ToList(), 1), v => SetFilters(v, ScenarioFilter, OutcomeFilter),
                "Show one session or all sessions of the dataset.");
            _scenarioFilterText = Filter("Scenario", 400f, fy, 320f, () => Cycle(ScenarioFilter, ResearchSchema.ScenarioChannels.ToList(), -1),
                () => Cycle(ScenarioFilter, ResearchSchema.ScenarioChannels.ToList(), 1), v => SetFilters(SessionFilter, v, OutcomeFilter),
                "Limit security-action metrics and completion to one scenario. Dialogue metrics (replies, techniques) are not scenario-specific.");
            _outcomeFilterText = Filter("Outcome", 760f, fy, 300f, () => Cycle(OutcomeFilter, ResearchSchema.Outcomes.ToList(), -1),
                () => Cycle(OutcomeFilter, ResearchSchema.Outcomes.ToList(), 1), v => SetFilters(SessionFilter, ScenarioFilter, v),
                "Show only sessions with this final outcome.");
            Button clear = MakeButton(_root, "ClearFilters", "CLEAR FILTERS", 1100f, fy, 150f, 36f, Card, () => SetFilters("", "", ""), 15f);
            Hint(clear.gameObject, () => "Reset all filters.", ShowHint);

            // KPI cards.
            (string key, string title, string hint)[] kpis =
            {
                ("sessions", "SESSIONS", "Number of sessions after filters. Each session is one anonymous consented run."),
                ("risky", "RISKY-ACTION RATE", "Completed risky security actions / completed security actions classified Safe or Unsafe. Neutral steps (e.g. picking up the USB) and promises are excluded."),
                ("recovery", "RECOVERY RATE", "Sessions whose risky actions were all followed by a covering report or verification (RECOVERED) / sessions with at least one risky action."),
                ("completion", "SCENARIO COMPLETION", "Scenario instances that reached a terminal action (e.g. credentials entered, reported, cancelled, verified, connected, handed in) / sessions x scenarios in scope."),
                ("latency", "MEDIAN DECISION TIME", "Median time from Alex's line to the participant's reply, over all timed replies (n shown). IQR = 25th–75th percentile."),
                ("index", "DESCRIPTIVE INDEX", "Mean 0-100 descriptive index. Weights: action safety 0.40, persuasion resistance 0.25, verification 0.15, reporting/recovery 0.20; missing parts are dropped and the rest renormalised; fewer than 2 parts = no index. NOT a validated measure."),
            };
            float cw = (W - 48f - 5 * 12f) / 6f;
            for (int i = 0; i < kpis.Length; i++)
            {
                (string key, string title, string hint) = kpis[i];
                Image card = Box(_root, $"Kpi_{key}", 24f + i * (cw + 12f), 156f, cw, 128f, Card, true);
                Box(card.transform, "Accent", 0f, 0f, 4f, 128f, key == "risky" ? Red : key == "recovery" ? Amber : Cyan);
                Label(card.transform, "Title", title, 18f, 10f, cw - 28f, 24f, 14f, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                KpiValues[key] = Label(card.transform, "Value", "—", 18f, 34f, cw - 28f, 52f, 42f, Ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
                _kpiDetails[key] = Label(card.transform, "Detail", "", 18f, 88f, cw - 28f, 34f, 14f, InkSecondary);
                Hint(card.gameObject, () => hint, ShowHint);
            }

            // Charts.
            float pw = (W - 48f - 2 * 12f) / 3f;
            _outcomePanel = Panel("Outcomes", "OUTCOME DISTRIBUTION  (final, after recovery)", 24f, 296f, pw, 318f);
            _scenarioPanel = Panel("Scenarios", "SCENARIO COMPARISON  (unsafe-action rate)", 24f + pw + 12f, 296f, pw, 318f);
            _techniquePanel = Panel("Techniques", "PERSUASION TECHNIQUE  (risky-reply rate)", 24f + 2 * (pw + 12f), 296f, pw, 318f);

            // Table + timeline.
            _tablePanel = Panel("Table", "SESSIONS  (click a row)", 24f, 626f, 1012f, 404f);
            _timelinePanel = Panel("Timeline", "SESSION TIMELINE", 1048f, 626f, W - 1072f, 404f);

            // Footer / hint bar.
            Box(_root, "FooterBar", 0f, 1040f, W, 40f, Navy);
            _footer = Label(_root, "Footer", DefaultFooter, 24f, 1040f, W - 48f, 40f, 15f, InkSecondary);

            // Empty state (covers the data area).
            Image empty = Box(_root, "EmptyState", 24f, 156f, W - 48f, 874f, Charcoal, true);
            _empty = empty.gameObject;
            _emptyText = Label(empty.transform, "Text", "", 0f, 300f, W - 48f, 220f, 24f, InkSecondary, TextAlignmentOptions.Center);

            // Confirmation dialog.
            Image dim = Box(_root, "Confirm", 0f, 0f, W, 1080f, new Color(0f, 0f, 0f, 0.7f), true);
            _confirm = dim.gameObject;
            Image dialog = Box(dim.transform, "Dialog", 610f, 390f, 700f, 260f, Card, true);
            Box(dialog.transform, "Accent", 0f, 0f, 700f, 4f, Red);
            _confirmText = Label(dialog.transform, "Text", "", 30f, 30f, 640f, 130f, 20f, Ink, TextAlignmentOptions.Center);
            MakeButton(dialog.transform, "Yes", "DELETE", 150f, 180f, 180f, 48f, Hex("#8E2A22"), ConfirmPending);
            MakeButton(dialog.transform, "No", "CANCEL", 370f, 180f, 180f, 48f, CardEdge, CancelPending);
            _confirm.SetActive(false);
            _built = true;
        }

        private RectTransform Panel(string name, string title, float x, float y, float w, float h)
        {
            Image panel = Box(_root, $"Panel_{name}", x, y, w, h, Card);
            Label(panel.transform, "Title", title, 16f, 6f, w - 32f, 30f, 15f, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            RectTransform body = Area(panel.transform, "Body", 16f, 40f, w - 32f, h - 48f);
            return body;
        }

        private static RectTransform ClearBody(RectTransform body)
        {
            for (int i = body.childCount - 1; i >= 0; i--)
            {
                GameObject child = body.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            return body;
        }

        private TextMeshProUGUI Filter(string name, float x, float y, float w, Func<string> prev, Func<string> next, Action<string> set, string hint)
        {
            Label(_root, $"{name}Label", name.ToUpperInvariant(), x, y, 100f, 36f, 14f, InkMuted, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            MakeButton(_root, $"{name}Prev", "<", x + 96f, y, 36f, 36f, Card, () => set(prev()));
            Image box = Box(_root, $"{name}Value", x + 136f, y, w - 180f, 36f, Track, true);
            TextMeshProUGUI text = Label(box.transform, "Text", "", 10f, 0f, w - 200f, 36f, 15f, Ink);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            MakeButton(_root, $"{name}Next", ">", x + w - 40f, y, 36f, 36f, Card, () => set(next()));
            Hint(box.gameObject, () => hint, ShowHint);
            return text;
        }

        private void Bar(Transform body, string name, float x, float y, string label, double ratio, Color color, string valueText, Func<string> hint)
        {
            float labelW = 150f, trackW = 250f;
            Image row = Box(body, $"Bar_{name}", x, y, 580f, 34f, new Color(0, 0, 0, 0), true);
            Label(row.transform, "Label", label, 0f, 0f, labelW, 34f, 16f, Ink);
            Box(row.transform, "Track", labelW, 7f, trackW, 20f, Track);
            if (!double.IsNaN(ratio) && ratio > 0)
            {
                Box(row.transform, "Fill", labelW, 7f, Mathf.Max(4f, trackW * (float)Math.Min(1.0, ratio)), 20f, color);
            }
            Label(row.transform, "Value", double.IsNaN(ratio) ? valueText : $"{valueText}", labelW + trackW + 12f, 0f, 580f - labelW - trackW - 12f, 34f, 15f,
                InkSecondary).textWrappingMode = TextWrappingModes.NoWrap;
            Hint(row.gameObject, hint, ShowHint);
        }

        private void Confirm(string message, Action action)
        {
            _pendingConfirm = action;
            _confirmText.text = message;
            _confirm.SetActive(true);
            _confirm.transform.SetAsLastSibling();
        }

        private void ShowHint(string text) => _footer.text = string.IsNullOrEmpty(text) ? _status ?? DefaultFooter : text;

        // =============================================================================================================
        // Formatting helpers
        // =============================================================================================================
        private static string Cycle(string current, List<string> values, int step)
        {
            var all = new List<string> { string.Empty };
            all.AddRange(values);
            int index = Math.Max(0, all.IndexOf(current ?? string.Empty));
            return all[(index + step + all.Count) % all.Count];
        }

        private static (Color, string) Mark(ResearchEvent e)
        {
            if (e.riskyAction) return (RiskyMark, "R");
            if (e.completion == "promised") return (PromiseMark, "P");
            if (e.corrective) return (SafeMark, "+");
            if (e.classification == "Safe") return (SafeMark, "S");
            if (e.classification == "Unsafe") return (PromiseMark, "P");
            return (NeutralMark, "N");
        }

        private static bool IsKeyLifecycle(ResearchEvent e) =>
            e.name == "research.consent_granted" || e.name == "alex_encounter.approached" || e.name == "alex_encounter.completed" ||
            e.name == "alex_encounter.debrief_shown" || e.name.StartsWith("demo.");

        private static string Describe(ResearchEvent e)
        {
            if (!e.IsDecision)
            {
                string outcome = SessionScorer.ParseOutcome(e.detail);
                return e.name + (outcome != null ? $"  (outcome {outcome})" : string.Empty);
            }
            string what = e.kind == "security_action"
                ? $"{ResearchSchema.ScenarioLabel(e.channel)}: {e.name.Substring(e.name.IndexOf('.') + 1).Replace('_', ' ')}"
                : $"Reply to Alex: {Spaced(e.choiceClass)}" + (string.IsNullOrEmpty(e.persuasion) || e.persuasion == "None" ? "" : $" (to {e.persuasion})") +
                  (e.completion == "promised" ? $" · promised {ResearchSchema.ScenarioLabel(e.channel)}" : "");
            string tags = (e.riskyAction ? " · RISKY ACTION" : "") + (e.corrective ? " · corrective" : "") + (e.latency >= 0 ? $" · {e.latency:0.0}s to decide" : "");
            return what + tags;
        }

        private static string Spaced(string pascal) =>
            string.IsNullOrEmpty(pascal) ? "reply" : string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));

        private static string OutcomeLabel(string outcome) => outcome;

        private static string OutcomeDefinition(string outcome)
        {
            switch (outcome)
            {
                case ResearchSchema.Compromised: return "At least one risky action was never covered by a report or verification.";
                case ResearchSchema.Recovered: return "Every risky action was followed by a covering report or verification.";
                case ResearchSchema.Resisted: return "No risky action, and the participant reported or verified.";
                case ResearchSchema.Declined: return "No risky action; refused until Alex gave up, without reporting or verifying.";
                default: return "Ended before any security outcome.";
            }
        }

        private static string DatasetLabel(string dataset) =>
            dataset == ResearchSchema.Demo ? "DEMO DATA" : dataset == ResearchSchema.Test ? "TEST / PRACTICE" : "REAL";

        private static double Ratio(double num, double den) => den > 0 ? num / den : double.NaN;
        private static string Pct(double ratio) => double.IsNaN(ratio) ? "—" : $"{ratio * 100:0}%";
        private static string Sec(double s) => double.IsNaN(s) ? "—" : $"{s:0.0}s";
        private static string Html(Color c) => ColorUtility.ToHtmlStringRGB(c);
        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
    }
}
