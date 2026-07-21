using System;
using System.Collections.Generic;
using UnityEngine;
using VRSIM.Net;
using VRSIM.State;

namespace VRSIM.Binding
{
    /// <summary>
    /// Drives one indicator from one value in engine state.
    ///
    /// Drop it on a light, name the state path, list which values mean what.
    /// Nothing about the BOP or SWACO is compiled in: a new panel, a renamed
    /// engine field or an extra ram is Inspector work, not a code change.
    ///
    /// This replaces the pattern where a controller class held a named field
    /// per light, so that adding an indicator meant editing C#, and the mapping
    /// from engine value to lamp lived in a switch statement halfway down a
    /// 1200-line file.
    /// </summary>
    public class RigIndicator : MonoBehaviour
    {
        [Serializable]
        public class StateVisual
        {
            [Tooltip("Value at the state path this rule matches, e.g. open, closed, moving")]
            public string value = "open";
            public bool greenOn = true;
            public bool redOn;
            public Color colour = Color.green;
        }

        [Header("Engine binding")]
        [Tooltip("Dotted path into rig state, e.g. bop.components.annular_1 " +
                 "or pumps.pump_1.active")]
        public string statePath = "bop.components.annular_1";

        [Tooltip("Warn on connect if the engine never sends this path. Catches a " +
                 "typo or a renamed field at startup instead of mid-session.")]
        public bool validateOnConnect = true;

        [Header("Scene binding")]
        public GameObject greenLight;
        public GameObject redLight;

        [Header("Value mapping")]
        [Tooltip("First matching rule wins. Values not listed fall through to Unknown.")]
        public List<StateVisual> mapping = new List<StateVisual>
        {
            new StateVisual { value = "open",   greenOn = true,  redOn = false, colour = new Color(0.25f, 0.95f, 0.35f) },
            new StateVisual { value = "closed", greenOn = false, redOn = true,  colour = new Color(0.95f, 0.25f, 0.20f) },
            new StateVisual { value = "moving", greenOn = true,  redOn = true,  colour = new Color(1f, 0.78f, 0.2f) },
            new StateVisual { value = "fault",  greenOn = false, redOn = true,  colour = new Color(0.95f, 0.25f, 0.20f) },
            // Booleans, so the same component drives a pump lamp without a
            // separate class.
            new StateVisual { value = "True",   greenOn = true,  redOn = false, colour = new Color(0.25f, 0.95f, 0.35f) },
            new StateVisual { value = "False",  greenOn = false, redOn = true,  colour = new Color(0.35f, 0.35f, 0.35f) },
        };

        [Header("Unknown / no data")]
        [Tooltip("What to show before the first snapshot, or for an unrecognised value.")]
        public bool unknownGreenOn;
        public bool unknownRedOn;

        [Header("Diagnostics")]
        public bool verbose;

        private RigConnection _connection;
        private MaterialPropertyBlock _block;
        private string _lastValue = "\0";

        private void Awake()
        {
            _connection = FindObjectOfType<RigConnection>();
            _block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            if (_connection == null) return;
            _connection.State.Changed += OnStateChanged;
            _connection.StatusChanged += OnStatusChanged;
            if (_connection.State.HasData) OnStateChanged(_connection.State.Snapshot);
        }

        private void OnDisable()
        {
            if (_connection == null) return;
            _connection.State.Changed -= OnStateChanged;
            _connection.StatusChanged -= OnStatusChanged;
        }

        private void OnStatusChanged(ConnectionStatus status, string detail)
        {
            if (status != ConnectionStatus.Connected || !validateOnConnect) return;
            if (!RigStatePath.Exists(_connection.State, statePath))
            {
                // Loud, once, at connect. A binding pointing at a path the
                // engine does not send is silent otherwise -- the lamp just
                // never changes, which reads as a hardware fault.
                Debug.LogWarning(
                    $"[RigIndicator] {name}: engine sends no '{statePath}'. " +
                    "Check the path against docs/protocol.md.", this);
            }
        }

        private void OnStateChanged(RigSnapshot snapshot)
        {
            var value = RigStatePath.ReadString(_connection.State, statePath);
            if (value == _lastValue) return;
            _lastValue = value;
            Apply(value);
        }

        private void Apply(string value)
        {
            StateVisual rule = null;
            if (value != null)
            {
                foreach (var candidate in mapping)
                {
                    if (!string.Equals(candidate.value, value, StringComparison.OrdinalIgnoreCase)) continue;
                    rule = candidate;
                    break;
                }
            }

            if (rule == null)
            {
                if (verbose && value != null)
                    Debug.Log($"[RigIndicator] {name}: no rule for '{value}' at {statePath}", this);
                SetLight(greenLight, unknownGreenOn, Color.grey);
                SetLight(redLight, unknownRedOn, Color.grey);
                return;
            }

            SetLight(greenLight, rule.greenOn, rule.colour);
            SetLight(redLight, rule.redOn, rule.colour);
        }

        private void SetLight(GameObject light, bool on, Color colour)
        {
            if (light == null) return;
            if (light.activeSelf != on) light.SetActive(on);
            if (!on) return;

            var renderer = light.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Property block, not renderer.material -- the latter clones a
                // material per renderer per call.
                renderer.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", colour);
                _block.SetColor("_Color", colour);
                _block.SetColor("_EmissionColor", colour * 2f);
                renderer.SetPropertyBlock(_block);
            }

            var unityLight = light.GetComponent<Light>();
            if (unityLight != null)
            {
                unityLight.color = colour;
                unityLight.enabled = true;
            }
        }
    }
}
