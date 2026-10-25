using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TrustBreach.Dialogue;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace TrustBreach.Editor
{
    /// <summary>
    /// Deterministic offline voice clips for dialogue lines using the macOS system speech synthesiser (`say`), which
    /// runs locally with no network or cloud TTS. One WAV per line id; a manifest of text hashes means only changed
    /// lines are regenerated and stale clips are removed. On other platforms (or if `say` fails) lines keep no clip
    /// and the encounter falls back to subtitles-only pacing.
    /// </summary>
    internal static class VoiceClipGenerator
    {
        internal const string Voice = "Daniel";
        internal const int WordsPerMinute = 178;

        [Serializable]
        private class Manifest
        {
            public List<string> ids = new List<string>();
            public List<string> hashes = new List<string>();
        }

        internal readonly struct Result
        {
            public readonly int Generated, Reused, Failed;
            public readonly string Message;

            public Result(int generated, int reused, int failed, string message)
            {
                Generated = generated;
                Reused = reused;
                Failed = failed;
                Message = message;
            }
        }

        internal static Result GenerateFor(DialogueGraph graph, string folder)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absoluteFolder = Path.Combine(projectRoot, folder);
            Directory.CreateDirectory(absoluteFolder);
            string manifestPath = Path.Combine(absoluteFolder, "voice_manifest.json");
            Manifest manifest = File.Exists(manifestPath) ? JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath)) : new Manifest();
            var known = new Dictionary<string, string>();
            for (int i = 0; i < manifest.ids.Count && i < manifest.hashes.Count; i++) known[manifest.ids[i]] = manifest.hashes[i];

            bool canSynthesize = Application.platform == RuntimePlatform.OSXEditor && File.Exists("/usr/bin/say");
            int generated = 0, reused = 0, failed = 0;
            var next = new Manifest();
            var wanted = new HashSet<string>();
            var changedPaths = new List<string>();

            foreach (LineVariant line in graph.AllLines().Where(l => !string.IsNullOrEmpty(l.Id)))
            {
                string fileName = $"{line.Id}.wav";
                string assetPath = $"{folder}/{fileName}";
                string absolute = Path.Combine(absoluteFolder, fileName);
                string hash = Hash($"{Voice}|{WordsPerMinute}|{line.Text}");
                wanted.Add(fileName);

                bool upToDate = File.Exists(absolute) && known.TryGetValue(line.Id, out string oldHash) && oldHash == hash;
                if (!upToDate)
                {
                    if (canSynthesize && Synthesize(line.Text, absolute))
                    {
                        generated++;
                        changedPaths.Add(assetPath);
                    }
                    else
                    {
                        failed++;
                        continue;
                    }
                }
                else
                {
                    reused++;
                }
                next.ids.Add(line.Id);
                next.hashes.Add(hash);
            }

            foreach (string stale in Directory.GetFiles(absoluteFolder, "*.wav").Select(Path.GetFileName).Where(f => !wanted.Contains(f)))
            {
                AssetDatabase.DeleteAsset($"{folder}/{stale}");
            }
            File.WriteAllText(manifestPath, JsonUtility.ToJson(next, true));

            AssetDatabase.Refresh();
            foreach (string path in changedPaths)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            foreach (LineVariant line in graph.AllLines().Where(l => !string.IsNullOrEmpty(l.Id)))
            {
                string assetPath = $"{folder}/{line.Id}.wav";
                if (AssetImporter.GetAtPath(assetPath) is AudioImporter importer)
                {
                    ConfigureImporter(importer);
                }
                line.Voice = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            }
            EditorUtility.SetDirty(graph);

            string message = canSynthesize
                ? $"voice '{Voice}': {generated} generated, {reused} reused, {failed} failed"
                : "macOS `say` unavailable: lines will use subtitles-only pacing";
            return new Result(generated, reused, failed, message);
        }

        private static void ConfigureImporter(AudioImporter importer)
        {
            bool changed = !importer.forceToMono;
            importer.forceToMono = true;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType != AudioClipLoadType.DecompressOnLoad || !Mathf.Approximately(settings.quality, 0.7f))
            {
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }

        private static bool Synthesize(string text, string outputPath)
        {
            string textFile = Path.Combine(Path.GetTempPath(), $"trustbreach_line_{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(textFile, text);
                var info = new ProcessStartInfo("/usr/bin/say",
                    $"-v \"{Voice}\" -r {WordsPerMinute} --file-format=WAVE --data-format=LEI16@22050 -o \"{outputPath}\" -f \"{textFile}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };
                using (Process process = Process.Start(info))
                {
                    if (process == null || !process.WaitForExit(30000))
                    {
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        Debug.LogWarning($"[TrustBreach.Voice] say failed: {process.StandardError.ReadToEnd()}");
                        return false;
                    }
                }
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 1000;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TrustBreach.Voice] synthesis unavailable: {e.Message}");
                return false;
            }
            finally
            {
                if (File.Exists(textFile)) File.Delete(textFile);
            }
        }

        private static string Hash(string text)
        {
            using (SHA1 sha = SHA1.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").Substring(0, 16);
            }
        }
    }
}
