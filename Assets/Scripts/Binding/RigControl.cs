using System;
using UnityEngine;
using UnityEngine.Events;
using VRSIM.Net;
using VRSIM.State;

namespace VRSIM.Binding
{
    /// <summary>
    /// Binds one physical control in the scene to one control id in the engine.
    ///
    /// This is the whole of the engine-facing side of a button, lever or valve.
    /// It knows nothing about how it gets activated -- proximity, a ray, an XRI
    /// interactable or a UI button all just call <see cref="Press"/> -- and
    /// nothing about how it looks, which is left to the UnityEvents below.
    ///
    /// That separation is the point. Re-modelling the console means dropping
    /// this on a different mesh; renaming an engine control means editing a
    /// string here. Neither is a code change, and neither end needs to know the
    /// other exists.
    ///
    /// A command is a request, not an assertion: pressing this does not change
    /// rig state. It sends intent, the engine decides, and the result arrives
    /// as state. <see cref="onConfirmed"/> fires only when state actually shows
    /// the requested value -- not when the ack arrives, because an ack means the
    /// engine took the command, not that the world changed.
    /// </summary>
    public class RigControl : MonoBehaviour, IRigPressable
    {
        public enum ControlKind
        {
            /// <summary>Alternates between on and off values.</summary>
            Toggle,
            /// <summary>Sends the same value every press.</summary>
            Momentary,
        }

        public enum VerifyMode
        {
            /// <summary>State must equal the expected value. Toggles, valves.</summary>
            EqualsValue,
            /// <summary>State must increase. Press counters.</summary>
            ValueIncreases,
            /// <summary>Trust the ack. Use only where nothing observable changes.</summary>
            AckOnly,
        }

        [Header("Engine binding")]
        [Tooltip("Control id the engine declares in hello.ack, e.g. bop.annular_1")]
        public string controlId = "bop.annular_1";
        public ControlKind kind = ControlKind.Toggle;
        public string onValue = "closed";
        public string offValue = "open";

        [Header("Round-trip verification")]
        [Tooltip("Dotted state path this command should change, e.g. " +
                 "bop.components.annular_1. Leave blank to use AckOnly.")]
        public string statePath = "bop.components.annular_1";
        public VerifyMode verifyMode = VerifyMode.EqualsValue;
        [Tooltip("Value at statePath meaning the ON command took effect.")]
        public string onStateValue = "closed";
        public string offStateValue = "open";
        [Tooltip("Seconds to wait for state to catch up. Valves travel for " +
                 "seconds, so this must exceed the slowest real movement.")]
        public float confirmTimeout = 8f;

        [Header("Startup validation")]
        [Tooltip("Warn on connect if the engine does not declare this control.")]
        public bool validateOnConnect = true;

        [Header("Events (wire visuals here, no code required)")]
        public UnityEvent onPressed;
        public UnityEvent onConfirmed;
        public UnityEventString onRejected;

        [Serializable] public class UnityEventString : UnityEvent<string> { }

        /// <summary>Fires on every state change: (isPending, isConfirmed, statusText).</summary>
        public event Action<bool, bool, string> StateChanged;

        public bool IsPending { get; private set; }
        public string Status { get; private set; } = "ready";
        public float LastRoundTripMs { get; private set; } = -1f;

        [Header("Diagnostics")]
        public bool verbose;

        private RigConnection _connection;
        private bool _wantOn;
        private string _expected;
        private double _baseline;
        private float _requestedAt;

        /// <summary>What a pointer shows when aimed at this control.</summary>
        public string PressableLabel => controlId + "\n" + Status;

        private void Awake() => _connection = FindAnyObjectByType<RigConnection>();

        private void OnEnable()
        {
            if (_connection == null) return;
            _connection.State.Changed += OnStateChanged;
            _connection.StatusChanged += OnStatusChanged;
        }

        private void OnDisable()
        {
            if (_connection == null) return;
            _connection.State.Changed -= OnStateChanged;
            _connection.StatusChanged -= OnStatusChanged;
        }

        // A named handler rather than a lambda, so it can be unsubscribed. The
        // lambda it replaces was added on every enable and removed on none, so
        // a control toggled off and on verified twice per state message, and a
        // destroyed one kept the connection holding a dead object.
        private void OnStateChanged(RigSnapshot snapshot) => Verify();

        private void OnStatusChanged(ConnectionStatus status, string detail)
        {
            if (status != ConnectionStatus.Connected || !validateOnConnect) return;
            if (!_connection.HasControl(controlId))
            {
                Debug.LogWarning(
                    $"[RigControl] {name}: engine does not declare '{controlId}'. " +
                    "Pressing it will do nothing. Check against docs/protocol.md.", this);
            }
        }

        /// <summary>Activate the control. Safe to call from any input mechanism.</summary>
        public void Press()
        {
            if (_connection == null || _connection.Status != ConnectionStatus.Connected)
            {
                Reject("not connected to the engine");
                return;
            }
            if (!_connection.HasControl(controlId))
            {
                Reject($"engine has no '{controlId}'");
                return;
            }

            _wantOn = kind == ControlKind.Momentary || !_wantOn;
            var value = _wantOn ? onValue : offValue;

            if (verifyMode == VerifyMode.ValueIncreases)
            {
                _baseline = RigStatePath.ReadNumber(_connection.State, statePath, double.MinValue);
                _expected = "<increase>";
            }
            else if (verifyMode == VerifyMode.AckOnly || string.IsNullOrWhiteSpace(statePath))
            {
                _expected = null;
            }
            else
            {
                _expected = _wantOn ? onStateValue : offStateValue;
            }

            IsPending = true;
            _requestedAt = Time.realtimeSinceStartup;
            SetStatus($"sent {value}");
            onPressed?.Invoke();

            _connection.SendCommand(controlId, value, "set", ack =>
            {
                if (ack.Accepted)
                {
                    if (verifyMode == VerifyMode.AckOnly || _expected == null) Confirm();
                    else SetStatus("accepted, awaiting state");
                }
                else
                {
                    _wantOn = !_wantOn; // the engine refused, so undo the intent
                    Reject(ack.Reason ?? ack.Status);
                }
            });
        }

        /// <summary>Watches state for the requested value. This proves the loop.</summary>
        private void Verify()
        {
            if (!IsPending || _expected == null || _connection == null) return;
            if (!_connection.State.HasData) return;

            var actual = RigStatePath.ReadString(_connection.State, statePath);
            if (actual == null) return;

            var satisfied = verifyMode == VerifyMode.ValueIncreases
                ? RigStatePath.ReadNumber(_connection.State, statePath, double.MinValue) > _baseline
                : string.Equals(actual, _expected, StringComparison.OrdinalIgnoreCase);

            if (satisfied) { Confirm(); return; }

            // Not there yet is normal: a ram reports "moving" while it travels.
            if (Time.realtimeSinceStartup - _requestedAt > confirmTimeout)
            {
                _wantOn = !_wantOn;
                Reject($"state stayed at '{actual}'");
            }
            else
            {
                SetStatus($"engine says {actual}…");
            }
        }

        private void Confirm()
        {
            LastRoundTripMs = (Time.realtimeSinceStartup - _requestedAt) * 1000f;
            IsPending = false;
            _expected = null;
            SetStatus($"confirmed in {LastRoundTripMs:0} ms");
            onConfirmed?.Invoke();
            StateChanged?.Invoke(false, true, Status);
        }

        private void Reject(string reason)
        {
            IsPending = false;
            _expected = null;
            SetStatus(reason);
            if (verbose) Debug.LogWarning($"[RigControl] {name}: {reason}", this);
            onRejected?.Invoke(reason);
            StateChanged?.Invoke(false, false, reason);
        }

        private void SetStatus(string status)
        {
            Status = status;
            StateChanged?.Invoke(IsPending, false, status);
        }
    }
}
