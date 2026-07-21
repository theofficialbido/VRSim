using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using VRSIM.Net;

namespace VRSIM.Interaction
{
    /// <summary>
    /// A physical button that sends a command to the engine and reports, in
    /// colour, how far round the loop that command actually got.
    ///
    /// It exists to test the VR -> engine direction end to end. The crucial
    /// part is that it does NOT treat an ack as success: an ack means the
    /// engine took the command, not that the world changed. The button only
    /// turns green once <see cref="RigConnection.State"/> actually reports the
    /// value it asked for, which is the full round trip
    /// (press -> command -> engine -> state -> display).
    ///
    /// Activation is by proximity rather than XRI select input. That is a
    /// deliberate simplification for a diagnostic scene: it needs no interactor
    /// or input-action configuration, so a failure here is a networking failure
    /// and not a misconfigured binding. The real control library will use XRI
    /// interactables properly.
    /// </summary>
    public class RigCommandButton : MonoBehaviour
    {
        public enum ButtonKind
        {
            /// <summary>Alternates between on and off each press.</summary>
            Toggle,
            /// <summary>Sends the same value every press.</summary>
            Momentary,
        }

        [Header("Command")]
        [Tooltip("Control id as declared by the engine in hello.ack, e.g. console.pump_1")]
        public string controlId = "console.pump_1";
        public ButtonKind kind = ButtonKind.Toggle;
        public string onCommandValue = "on";
        public string offCommandValue = "off";

        [Header("Round-trip verification")]
        [Tooltip("Dotted path into rig state that this command should change, " +
                 "e.g. pumps.pump_1.active or bop.components.annular_1")]
        public string statePath = "pumps.pump_1.active";
        [Tooltip("Value at statePath meaning the command took effect.")]
        public string onStateValue = "True";
        public string offStateValue = "False";

        [Header("Activation")]
        [Tooltip("Transforms that can press this button -- normally the controllers.")]
        public List<Transform> activators = new List<Transform>();
        public float activationRadius = 0.07f;
        [Tooltip("Seconds before the same activator can press again.")]
        public float rearmDelay = 0.8f;

        [Header("Wiring")]
        public RigConnection connection;
        public TextMeshPro label;

        private enum Visual { Idle, Pending, Confirmed, Rejected }

        private static readonly Color IdleColour = new Color(0.30f, 0.34f, 0.40f);
        private static readonly Color PendingColour = new Color(0.95f, 0.78f, 0.25f);
        private static readonly Color ConfirmedColour = new Color(0.35f, 0.85f, 0.45f);
        private static readonly Color RejectedColour = new Color(0.90f, 0.35f, 0.30f);

        private Renderer _renderer;
        private MaterialPropertyBlock _block;
        private Visual _visual = Visual.Idle;
        private Vector3 _restScale;

        private bool _wantOn;
        private string _pendingCommandId;
        private string _expectedStateValue;
        private float _rearmAt;
        private float _requestedAt;
        private string _status = "ready";

        private void Awake()
        {
            _renderer = GetComponentInChildren<Renderer>();
            _block = new MaterialPropertyBlock();
            _restScale = transform.localScale;
            if (connection == null) connection = FindObjectOfType<RigConnection>();
            SetVisual(Visual.Idle);
        }

        private void Update()
        {
            PollActivators();
            VerifyRoundTrip();
            UpdateLabel();
        }

        private void PollActivators()
        {
            if (Time.time < _rearmAt) return;

            foreach (var activator in activators)
            {
                if (activator == null) continue;
                if (Vector3.Distance(activator.position, transform.position) > activationRadius) continue;
                Press();
                _rearmAt = Time.time + rearmDelay;
                return;
            }
        }

        /// <summary>Sends the command. Public so it can also be driven from a UI or a test.</summary>
        public void Press()
        {
            if (connection == null || connection.Status != ConnectionStatus.Connected)
            {
                _status = "not connected";
                SetVisual(Visual.Rejected);
                return;
            }

            if (!connection.HasControl(controlId))
            {
                // The engine never declared this control. Saying so beats
                // sending commands into a void and looking merely unresponsive.
                _status = $"engine does not have {controlId}";
                SetVisual(Visual.Rejected);
                return;
            }

            _wantOn = kind == ButtonKind.Momentary || !_wantOn;
            var value = _wantOn ? onCommandValue : offCommandValue;
            _expectedStateValue = _wantOn ? onStateValue : offStateValue;

            // Squash immediately. Local visual feedback must not wait for the
            // network, or every control feels broken over Wi-Fi -- but it is
            // only a visual, and the button stays amber until state confirms.
            transform.localScale = new Vector3(_restScale.x, _restScale.y * 0.55f, _restScale.z);
            SetVisual(Visual.Pending);
            _status = $"sent {value}";
            _requestedAt = Time.realtimeSinceStartup;

            _pendingCommandId = connection.SendCommand(controlId, value, "set", ack =>
            {
                if (ack.Accepted)
                {
                    // Accepted means taken, not done. Stay amber and wait for
                    // state to actually show the change.
                    _status = "accepted, awaiting state";
                }
                else
                {
                    _pendingCommandId = null;
                    _expectedStateValue = null;
                    _wantOn = !_wantOn; // the engine refused, so undo the intent
                    _status = ack.Reason ?? ack.Status;
                    SetVisual(Visual.Rejected);
                    transform.localScale = _restScale;
                }
            });
        }

        /// <summary>
        /// Watches rig state for the value the command asked for. This is the
        /// step that proves the whole loop rather than just that a message left
        /// the headset.
        /// </summary>
        private void VerifyRoundTrip()
        {
            if (_expectedStateValue == null || connection == null || !connection.State.HasData) return;

            var actual = ReadState(statePath);
            if (actual == null) return;

            if (!string.Equals(actual, _expectedStateValue, System.StringComparison.OrdinalIgnoreCase))
            {
                // Not there yet. Valves take seconds to travel and report
                // "moving" on the way, which is a legitimate intermediate state.
                if (Time.realtimeSinceStartup - _requestedAt > 8f)
                {
                    _status = $"state never became {_expectedStateValue} (is {actual})";
                    SetVisual(Visual.Rejected);
                    _expectedStateValue = null;
                    transform.localScale = _restScale;
                }
                else
                {
                    _status = $"engine says {actual}…";
                }
                return;
            }

            var ms = (Time.realtimeSinceStartup - _requestedAt) * 1000f;
            _status = $"confirmed in {ms:0} ms";
            _expectedStateValue = null;
            _pendingCommandId = null;
            SetVisual(Visual.Confirmed);
            transform.localScale = _restScale;
        }

        private string ReadState(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            JToken node = connection.State.Raw;
            foreach (var part in path.Split('.'))
            {
                if (node == null) return null;
                node = node[part];
            }
            return node?.ToString();
        }

        private void UpdateLabel()
        {
            if (label == null) return;
            var colour = _visual == Visual.Confirmed ? "#59D96F"
                       : _visual == Visual.Rejected ? "#E65A4C"
                       : _visual == Visual.Pending ? "#F2C740" : "#B8C0CC";
            label.text = $"{controlId}\n<size=60%><color={colour}>{_status}</color></size>";
        }

        private void SetVisual(Visual visual)
        {
            _visual = visual;
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_block);
            var colour = visual == Visual.Confirmed ? ConfirmedColour
                       : visual == Visual.Rejected ? RejectedColour
                       : visual == Visual.Pending ? PendingColour : IdleColour;
            _block.SetColor("_BaseColor", colour); // URP lit
            _block.SetColor("_Color", colour);     // fallback for other shaders
            _renderer.SetPropertyBlock(_block);
        }
    }
}
