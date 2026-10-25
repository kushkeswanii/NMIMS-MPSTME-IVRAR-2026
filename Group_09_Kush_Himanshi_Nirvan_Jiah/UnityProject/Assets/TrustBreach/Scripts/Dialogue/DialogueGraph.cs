using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TrustBreach.Dialogue
{
    /// <summary>Branching conversation data for one NPC encounter (authored by the Phase 3 builder).</summary>
    [CreateAssetMenu(menuName = "TrustBreach/Dialogue Graph", fileName = "DialogueGraph")]
    public class DialogueGraph : ScriptableObject
    {
        public string EncounterId = "encounter";
        public string ObjectId = "NPC";
        public string SpeakerName = "Speaker";
        public string SpeakerRole = string.Empty;
        public string StartNodeId;
        public string GlobalReportNodeId;
        public string GlobalExitNodeId;
        public LineVariant Greeting = new LineVariant();
        public List<DialogueNode> Nodes = new List<DialogueNode>();
        [Tooltip("Applied from any active node without its own transition for the signal (e.g. a completed compromise).")]
        public List<SignalTransition> GlobalSignalTransitions = new List<SignalTransition>();

        [NonSerialized] private Dictionary<string, DialogueNode> _lookup;

        public DialogueNode GetNode(string id)
        {
            if (_lookup == null || _lookup.Count != Nodes.Count)
            {
                _lookup = Nodes.Where(n => !string.IsNullOrEmpty(n.Id)).ToDictionary(n => n.Id);
            }
            return id != null && _lookup.TryGetValue(id, out DialogueNode node) ? node : null;
        }

        public IEnumerable<LineVariant> AllLines() => new[] { Greeting }.Concat(Nodes.SelectMany(n => n.Lines));

        public void InvalidateLookup() => _lookup = null;
    }
}
