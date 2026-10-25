using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TrustBreach.Analytics
{
    /// <summary>
    /// Durable, dataset-isolated session storage:
    ///   &lt;root&gt;/&lt;dataset&gt;/sessions/&lt;sessionId&gt;.json   one file per session (atomic temp + replace writes)
    ///   &lt;root&gt;/&lt;dataset&gt;/quarantine/                 unreadable / malformed files moved aside on load
    ///   &lt;root&gt;/&lt;dataset&gt;/exports/&lt;timestamp&gt;/       exports
    /// Default root: &lt;persistentDataPath&gt;/TrustBreach/research. Datasets (real / demo / test) never share folders.
    /// </summary>
    public sealed class ResearchStore
    {
        public sealed class LoadResult
        {
            public readonly List<SessionRecord> Sessions = new List<SessionRecord>();
            /// <summary>Files moved to quarantine during this load (file name + reason).</summary>
            public readonly List<string> Quarantined = new List<string>();
            /// <summary>Readable files with a schema this build does not understand (left in place).</summary>
            public readonly List<string> Unsupported = new List<string>();
        }

        private static readonly Regex SafeId = new Regex("^[A-Za-z0-9_-]{1,64}$");
        private static readonly string[] Datasets = { ResearchSchema.Real, ResearchSchema.Demo, ResearchSchema.Test };

        public string Root { get; }

        public ResearchStore(string root = null)
        {
            Root = string.IsNullOrEmpty(root) ? DefaultRoot : root;
        }

        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "TrustBreach", "research");

        public static bool IsValidDataset(string dataset) => Datasets.Contains(dataset);
        public static bool IsValidSessionId(string id) => !string.IsNullOrEmpty(id) && SafeId.IsMatch(id);

        public string DatasetDir(string dataset) => Path.Combine(Root, CheckDataset(dataset));
        public string SessionsDir(string dataset) => Path.Combine(DatasetDir(dataset), "sessions");
        public string QuarantineDir(string dataset) => Path.Combine(DatasetDir(dataset), "quarantine");
        public string ExportsDir(string dataset) => Path.Combine(DatasetDir(dataset), "exports");

        public string SessionPath(string dataset, string sessionId)
        {
            if (!IsValidSessionId(sessionId)) throw new ArgumentException($"Invalid session id '{sessionId}'.");
            return Path.Combine(SessionsDir(dataset), sessionId + ".json");
        }

        public void Save(SessionRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (record.dataset == null || !IsValidDataset(record.dataset)) throw new ArgumentException($"Invalid dataset '{record.dataset}'.");
            AtomicWrite(SessionPath(record.dataset, record.sessionId), JsonUtility.ToJson(record, true));
        }

        public bool Exists(string dataset, string sessionId) => IsValidSessionId(sessionId) && File.Exists(SessionPath(dataset, sessionId));

        public bool Delete(string dataset, string sessionId)
        {
            if (!IsValidSessionId(sessionId)) return false;
            string path = SessionPath(dataset, sessionId);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        /// <summary>Deletes every session file of a dataset (exports and quarantine are kept). Returns the number deleted.</summary>
        public int ClearSessions(string dataset)
        {
            string dir = SessionsDir(dataset);
            if (!Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                File.Delete(file);
                n++;
            }
            foreach (string tmp in Directory.GetFiles(dir, "*.tmp")) File.Delete(tmp);
            return n;
        }

        /// <summary>
        /// Loads every session of a dataset, oldest first. Malformed, empty or mismatched files are moved to quarantine
        /// (never silently dropped, never mixed into another dataset); files with an unknown schema version are skipped.
        /// </summary>
        public LoadResult LoadAll(string dataset)
        {
            var result = new LoadResult();
            string dir = SessionsDir(dataset);
            if (!Directory.Exists(dir)) return result;
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                string reason;
                SessionRecord record = null;
                try
                {
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    record = Parse(json, dataset, Path.GetFileNameWithoutExtension(file), out reason);
                }
                catch (Exception e)
                {
                    reason = $"unreadable: {e.GetType().Name}";
                }
                if (record != null)
                {
                    result.Sessions.Add(record);
                }
                else if (reason.StartsWith("unsupported"))
                {
                    result.Unsupported.Add($"{Path.GetFileName(file)}: {reason}");
                }
                else
                {
                    Quarantine(dataset, file, reason);
                    result.Quarantined.Add($"{Path.GetFileName(file)}: {reason}");
                }
            }
            result.Sessions.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(a.startedUtc, b.startedUtc);
                return c != 0 ? c : string.CompareOrdinal(a.sessionId, b.sessionId);
            });
            return result;
        }

        /// <summary>Validates a session file. Returns null (with a reason) when it must not be used.</summary>
        public static SessionRecord Parse(string json, string expectedDataset, string expectedId, out string reason)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "empty file";
                return null;
            }
            SessionRecord record;
            try
            {
                record = JsonUtility.FromJson<SessionRecord>(json);
            }
            catch (Exception e)
            {
                reason = $"malformed JSON ({e.GetType().Name})";
                return null;
            }
            if (record == null || string.IsNullOrEmpty(record.schema))
            {
                reason = "not a TRUST//BREACH session file";
                return null;
            }
            if (!record.schema.StartsWith(ResearchSchema.SchemaPrefix, StringComparison.Ordinal))
            {
                reason = $"unsupported schema '{record.schema}'";
                return null;
            }
            if (record.sessionId != expectedId || !IsValidSessionId(record.sessionId))
            {
                reason = "session id does not match file name";
                return null;
            }
            if (record.dataset != expectedDataset)
            {
                reason = $"dataset '{record.dataset}' stored in '{expectedDataset}' folder";
                return null;
            }
            if (record.events == null)
            {
                reason = "missing events";
                return null;
            }
            if (record.events.Any(e => e == null || string.IsNullOrEmpty(e.kind) || string.IsNullOrEmpty(e.name)))
            {
                reason = "event without kind/name";
                return null;
            }
            record.scenes ??= new List<string>();
            record.consent ??= new ConsentInfo();
            record.summary ??= new SessionSummary();
            reason = null;
            return record;
        }

        private void Quarantine(string dataset, string file, string reason)
        {
            try
            {
                string dir = QuarantineDir(dataset);
                Directory.CreateDirectory(dir);
                string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture);
                string target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(file)}.{stamp}.json");
                File.Move(file, target);
                File.WriteAllText(target + ".reason.txt", reason, Encoding.UTF8);
                Debug.LogWarning($"[TrustBreach.Research] quarantined {Path.GetFileName(file)} ({reason})");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrustBreach.Research] could not quarantine {file}: {e.Message}");
            }
        }

        /// <summary>Writes to a temp file, flushes it to disk, then replaces the target, so readers never see a partial file.</summary>
        public static void AtomicWrite(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, null);
                    return;
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(path);
                }
            }
            File.Move(tmp, path);
        }

        private static string CheckDataset(string dataset) =>
            IsValidDataset(dataset) ? dataset : throw new ArgumentException($"Unknown dataset '{dataset}'.");
    }
}
