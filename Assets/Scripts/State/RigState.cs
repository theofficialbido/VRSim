using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VRSIM.State
{
    /// <summary>
    /// The client's local mirror of engine state.
    ///
    /// This is a mirror, not a source of truth. Nothing in VR writes to it
    /// except the network layer: the client sends intent, the engine computes,
    /// and the result arrives here. That is what keeps the two halves from
    /// silently disagreeing about whether a ram is closed.
    ///
    /// Raw JSON is kept alongside the typed view because a delta is a merge
    /// patch that must be applied to the raw tree (see <see cref="MergePatch"/>);
    /// the typed view is re-materialised after each change for convenience.
    /// </summary>
    public class RigState
    {
        /// <summary>Fired after any snapshot or delta is applied.</summary>
        public event Action<RigSnapshot> Changed;

        /// <summary>
        /// Fired when staleness changes. Stale means "we are still connected
        /// but the data has stopped arriving", which is a different and more
        /// dangerous condition than being disconnected -- a frozen gauge
        /// showing a plausible number reads as live.
        /// </summary>
        public event Action<bool> StaleChanged;

        public RigSnapshot Snapshot { get; private set; } = new RigSnapshot();

        /// <summary>True once a full snapshot has been received.</summary>
        public bool HasData { get; private set; }

        public bool IsStale
        {
            get => _isStale;
            set
            {
                if (_isStale == value) return;
                _isStale = value;
                StaleChanged?.Invoke(value);
            }
        }

        /// <summary>Seconds since the last state message, for display.</summary>
        public float SecondsSinceUpdate => HasData ? Time.realtimeSinceStartup - _lastUpdateTime : 0f;

        private JToken _raw = new JObject();
        private bool _isStale;
        private float _lastUpdateTime;

        public void ApplySnapshot(JToken payload)
        {
            _raw = payload?.DeepClone() ?? new JObject();
            Materialise();
            HasData = true;
        }

        public void ApplyDelta(JToken payload)
        {
            if (!HasData)
            {
                // A delta before any snapshot cannot be applied to anything.
                // Dropping it is correct: the connection layer requests a
                // resync, and applying it to an empty tree would fabricate a
                // partial rig state that looks real.
                Debug.LogWarning("[RigState] delta received before snapshot; ignored");
                return;
            }

            _raw = MergePatch.Apply(_raw, payload);
            Materialise();
        }

        /// <summary>
        /// Discards everything. Called on disconnect: state is never merged
        /// across a reconnect, because anything that changed while we were away
        /// would otherwise persist as though it were current.
        /// </summary>
        public void Clear()
        {
            _raw = new JObject();
            Snapshot = new RigSnapshot();
            HasData = false;
            IsStale = false;
        }

        private void Materialise()
        {
            try
            {
                Snapshot = _raw.ToObject<RigSnapshot>() ?? new RigSnapshot();
            }
            catch (Exception ex)
            {
                // Hold the previous typed view rather than throwing. A field
                // the engine added that this build does not understand must not
                // take the whole display down mid-session.
                Debug.LogWarning($"[RigState] could not materialise state: {ex.Message}");
                return;
            }

            _lastUpdateTime = Time.realtimeSinceStartup;
            IsStale = false;
            Changed?.Invoke(Snapshot);
        }

        /// <summary>Raw access, for diagnostics and for fields not yet typed.</summary>
        public JToken Raw => _raw;
    }
}
