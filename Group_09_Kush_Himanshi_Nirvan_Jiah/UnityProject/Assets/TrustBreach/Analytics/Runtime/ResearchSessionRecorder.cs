using System;
using System.Collections.Generic;
using TrustBreach.Core;
using UnityEngine;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Turns the DecisionLog stream into one durable <see cref="SessionRecord"/> per consented session.
    ///  - Consent gate: nothing is kept or written unless <see cref="ResearchConsent.CanRecord"/>.
    ///  - Dedupe: decisions by (session, DecisionLog sequence); lifecycle events by (name, time, detail).
    ///  - Ordering: events get a contiguous 1-based seq in arrival order; elapsed is time since the previous event.
    ///  - Persistence: the file is rewritten atomically after every accepted event, so a crash loses at most nothing.
    ///  - Withdrawal deletes the file. Pure C# (no scene objects) so it can be unit-tested.
    /// </summary>
    public sealed class ResearchSessionRecorder
    {
        private readonly ResearchStore _store;
        private readonly Func<string> _scene;
        private readonly Func<DateTime> _clock;
        private readonly HashSet<int> _decisionKeys = new HashSet<int>();
        private readonly HashSet<string> _lifecycleKeys = new HashSet<string>();

        public string Dataset { get; }
        public SessionRecord Current { get; private set; }
        public bool IsRecording => Current != null;
        public int DuplicatesDropped { get; private set; }
        public int StaleDropped { get; private set; }
        public int SaveFailures { get; private set; }
        /// <summary>Raised after every successful save (dashboard / tests).</summary>
        public event Action<SessionRecord> Saved;

        public ResearchSessionRecorder(ResearchStore store, string dataset, Func<string> sceneProvider = null, Func<DateTime> utcClock = null)
        {
            if (!ResearchStore.IsValidDataset(dataset)) throw new ArgumentException($"Unknown dataset '{dataset}'.");
            _store = store;
            Dataset = dataset;
            _scene = sceneProvider ?? (() => string.Empty);
            _clock = utcClock ?? (() => DateTime.UtcNow);
        }

        public void HandleSessionStarted(string sessionId)
        {
            Finish("session_restart");
            if (ResearchConsent.CanRecord) Begin(sessionId);
        }

        public void HandleConsentChanged(ConsentStatus status)
        {
            // Withdrawal is handled by HandleWithdrawn (raised first); a reset means a new participant.
            if (status == ConsentStatus.Undecided || status == ConsentStatus.Declined) Finish("session_restart");
        }

        public void HandleWithdrawn(string sessionId)
        {
            if (Current == null || Current.sessionId != sessionId)
            {
                // Data of that session may still be on disk (e.g. recorder restarted); delete it anyway.
                TryDelete(sessionId);
                return;
            }
            TryDelete(Current.sessionId);
            Debug.Log($"[TrustBreach.Research] participant withdrew: deleted research session {Current.sessionId}");
            Current = null;
        }

        public void HandleDecision(DecisionEvent d)
        {
            if (!EnsureSession(d.SessionId)) return;
            if (!_decisionKeys.Add(d.Sequence))
            {
                DuplicatesDropped++;
                return;
            }
            Append(ResearchEvent.FromDecision(d, _scene()));
        }

        public void HandleLifecycle(LifecycleEvent e)
        {
            if (!EnsureSession(e.SessionId)) return;
            string key = $"{e.EventName}|{Math.Round(e.SessionTime, 3)}|{e.Detail}";
            if (!_lifecycleKeys.Add(key))
            {
                DuplicatesDropped++;
                return;
            }
            if (e.EventName == "research.consent_granted") Current.consent.grantedAt = Math.Round(e.SessionTime, 3);
            Append(ResearchEvent.FromLifecycle(e, _scene()));
        }

        public void HandleSceneLoaded(string scene)
        {
            if (Current == null || string.IsNullOrEmpty(scene)) return;
            if (Current.scenes.Count == 0 || Current.scenes[Current.scenes.Count - 1] != scene)
            {
                Current.scenes.Add(scene);
                Save();
            }
        }

        /// <summary>Closes the current session (endReason, end time, final summary) and stops recording it.</summary>
        public void Finish(string reason)
        {
            if (Current == null) return;
            Current.endReason = reason;
            Current.endedUtc = SessionRecord.UtcMinute(_clock());
            Save();
            Current = null;
        }

        /// <summary>Re-saves the current session (e.g. application pause) with an up-to-date duration.</summary>
        public void Flush(double sessionTime)
        {
            if (Current == null) return;
            Current.durationSeconds = Math.Max(Current.durationSeconds, Math.Round(sessionTime, 3));
            Save();
        }

        private bool EnsureSession(string sessionId)
        {
            if (!ResearchConsent.CanRecord) return false;
            if (Current == null)
            {
                // Recorder (re)created after consent, e.g. after a scene change: continue the consented session.
                Begin(sessionId);
                if (_store.Exists(Dataset, sessionId)) Resume(sessionId);
                return true;
            }
            if (Current.sessionId != sessionId)
            {
                StaleDropped++;
                return false;
            }
            return true;
        }

        private void Begin(string sessionId)
        {
            _decisionKeys.Clear();
            _lifecycleKeys.Clear();
            Current = new SessionRecord
            {
                dataset = Dataset,
                sessionId = sessionId,
                startedUtc = SessionRecord.UtcMinute(_clock()),
                appVersion = Application.version,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                consent = new ConsentInfo { status = ConsentStatus.Granted.ToString() }
            };
            string scene = _scene();
            if (!string.IsNullOrEmpty(scene)) Current.scenes.Add(scene);
        }

        /// <summary>Continues an existing file for the same session: keeps its events and dedupe keys.</summary>
        private void Resume(string sessionId)
        {
            ResearchStore.LoadResult loaded = _store.LoadAll(Dataset);
            SessionRecord existing = loaded.Sessions.Find(s => s.sessionId == sessionId);
            if (existing == null) return;
            existing.endReason = "in_progress";
            Current = existing;
            foreach (ResearchEvent e in existing.events)
            {
                if (e.IsDecision) _decisionKeys.Add(e.sourceSeq);
                else _lifecycleKeys.Add($"{e.name}|{Math.Round(e.t, 3)}|{e.detail}");
            }
            HandleSceneLoaded(_scene());
        }

        private void Append(ResearchEvent e)
        {
            List<ResearchEvent> events = Current.events;
            double previous = events.Count > 0 ? events[events.Count - 1].t : 0d;
            e.seq = events.Count + 1;
            e.t = Math.Round(e.t, 3);
            e.elapsed = Math.Round(Math.Max(0d, e.t - previous), 3);
            if (e.latency >= 0) e.latency = Math.Round(e.latency, 3);
            events.Add(e);
            Current.durationSeconds = Math.Max(Current.durationSeconds, e.t);
            Save();
        }

        private void Save()
        {
            if (Current == null) return;
            try
            {
                SessionScorer.UpdateSummary(Current);
                _store.Save(Current);
                Saved?.Invoke(Current);
            }
            catch (Exception ex)
            {
                SaveFailures++;
                Debug.LogWarning($"[TrustBreach.Research] could not save session {Current.sessionId}: {ex.Message}");
            }
        }

        private void TryDelete(string sessionId)
        {
            try
            {
                _store.Delete(Dataset, sessionId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TrustBreach.Research] could not delete session {sessionId}: {ex.Message}");
            }
        }
    }
}
