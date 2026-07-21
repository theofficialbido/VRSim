using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using VRSIM.Net;
using VRSIM.State;

/// <summary>
/// Drives the BOP panel's lights from the per-component status the engine
/// sends.
///
/// Every part of the BOP is one entry in <see cref="components"/>, and every
/// state that part can be in gets its own light assignment. That covers both
/// shapes the panel actually uses: a part with a green lamp and a red lamp, and
/// a part with a single lamp that is simply on or off. Adding a ram, or a part
/// with three lamps, is Inspector work rather than a code change.
///
/// Display only. Operating a BOP control sends a command; the lights move when
/// the resulting state comes back, so the panel can never claim a ram is shut
/// that the engine does not agree is shut.
/// </summary>
public class BOPPanelController : MonoBehaviour
{
    /// <summary>One light, and the state that turns it on.</summary>
    [Serializable]
    public class StateLight
    {
        [Tooltip("Engine state this light represents: open, closed, moving, fault")]
        public string state = "open";

        [Tooltip("Object to switch on while the part is in this state")]
        public GameObject light;

        [Tooltip("Colour applied while lit. Ignored if the object has no renderer or Light.")]
        public Color colour = Color.green;
    }

    /// <summary>One part of the BOP, and all of its lights.</summary>
    [Serializable]
    public class ComponentLights
    {
        [Tooltip("Engine component id, e.g. annular_1, pipe_ram_1, blind_shear_ram, "
               + "kill_line, choke_line. Must match docs/protocol.md exactly.")]
        public string componentId = "annular_1";

        [Tooltip("Name used in warnings. Cosmetic.")]
        public string label = "Annular";

        [Tooltip("One entry per case. A part with a green and a red lamp gets two "
               + "entries; a part with a single on/off lamp gets one.")]
        public List<StateLight> lights = new List<StateLight>();

        [Tooltip("If several ids are listed, the worst state wins: closed beats "
               + "moving beats open. Use for the three pipe rams sharing one lamp.")]
        public List<string> alsoCombine = new List<string>();

        [NonSerialized] public string LastState;
    }

    [Header("Data Source")]
    [Tooltip("Left empty, the first RigConnection in the scene is used.")]
    public RigConnection connection;

    [Header("BOP Components And Their Lights")]
    [Tooltip("One entry per part of the BOP. Assign a light for each state the "
           + "part can be in.")]
    public List<ComponentLights> components = new List<ComponentLights>();

    [Header("Master Valve")]
    [Tooltip("Master valve uses the same per-state assignment. Its states are "
           + "open and closed.")]
    public ComponentLights masterValve = new ComponentLights
    {
        componentId = "master_valve",
        label = "Master Valve",
    };
    [Tooltip("Optional wheel to turn as the valve opens")]
    public GameObject masterValveObject;
    public float valveOpenAngle = 90f;
    public Vector3 rotationAxis = new Vector3(0, 0, 1);
    public float valveRotationSpeed = 2f;

    [Header("Pressure Readouts (world space)")]
    public TextMeshPro annularPressure3D;
    public TextMeshPro manifoldPressure3D;
    public TextMeshPro accumulatorPressure3D;
    public TextMeshPro airPressure3D;

    [Header("Display Settings")]
    public string pressureFormat = "{0:F0} PSI";
    [Tooltip("Below these values a readout turns amber")]
    public float annularLowPsi = 500f;
    public float manifoldLowPsi = 1500f;
    public float accumulatorLowPsi = 1200f;
    public float airLowPsi = 100f;

    [Header("Stale Display")]
    [Tooltip("Dim readouts when the engine stops sending, so a frozen number is "
           + "never mistaken for a live one.")]
    public bool dimWhenStale = true;

    [Header("Diagnostics")]
    [Tooltip("Warn once on connect for any component id the engine never sends.")]
    public bool validateOnConnect = true;

    private static readonly Color NormalColour = Color.white;
    private static readonly Color LowColour = new Color(1f, 0.82f, 0.25f);
    private static readonly Color StaleColour = new Color(0.45f, 0.45f, 0.45f);

    private MaterialPropertyBlock _block;
    private Quaternion _valveClosedRotation;
    private float _valveAngle, _valveTargetAngle;

    private void Awake()
    {
        if (connection == null) connection = FindObjectOfType<RigConnection>();
        _block = new MaterialPropertyBlock();
        if (masterValveObject != null)
            _valveClosedRotation = masterValveObject.transform.localRotation;
    }

    private void OnEnable()
    {
        if (connection == null) return;
        connection.State.Changed += OnStateChanged;
        connection.State.StaleChanged += OnStaleChanged;
        connection.StatusChanged += OnStatusChanged;
        if (connection.State.HasData) OnStateChanged(connection.State.Snapshot);
    }

    private void OnDisable()
    {
        if (connection == null) return;
        connection.State.Changed -= OnStateChanged;
        connection.State.StaleChanged -= OnStaleChanged;
        connection.StatusChanged -= OnStatusChanged;
    }

    /// <summary>
    /// Checks every configured id against what the engine actually sends, once,
    /// on connect. A component id with a typo is otherwise silent: the lamp
    /// simply never changes, which on a panel reads as a blown bulb.
    /// </summary>
    private void OnStatusChanged(ConnectionStatus status, string detail)
    {
        if (status != ConnectionStatus.Connected || !validateOnConnect) return;
        if (!connection.State.HasData) return;

        var known = connection.State.Snapshot.Bop.Components;
        foreach (var component in components)
        {
            foreach (var id in AllIds(component))
            {
                if (known != null && known.ContainsKey(id)) continue;
                Debug.LogWarning(
                    $"[BOPPanelController] '{component.label}' is bound to component id " +
                    $"'{id}', which the engine does not send. Its lights will never " +
                    "change. Check the id against docs/protocol.md.", this);
            }
        }
    }

    private void OnStateChanged(RigSnapshot snapshot)
    {
        var bop = snapshot.Bop;

        foreach (var component in components)
        {
            var state = ResolveState(bop, component);
            if (state == component.LastState) continue;
            component.LastState = state;
            ApplyLights(component, state);
        }

        var masterState = bop.MasterValve;
        if (masterState != masterValve.LastState)
        {
            masterValve.LastState = masterState;
            ApplyLights(masterValve, masterState);
        }
        _valveTargetAngle = masterState == "open" ? valveOpenAngle : 0f;

        var stale = dimWhenStale && connection.State.IsStale;
        SetPressure(annularPressure3D, bop.PressuresPsi.Annular, annularLowPsi, stale);
        SetPressure(manifoldPressure3D, bop.PressuresPsi.Manifold, manifoldLowPsi, stale);
        SetPressure(accumulatorPressure3D, bop.PressuresPsi.Accumulator, accumulatorLowPsi, stale);
        SetPressure(airPressure3D, bop.PressuresPsi.Air, airLowPsi, stale);
    }

    private void OnStaleChanged(bool stale)
    {
        if (connection.State.HasData) OnStateChanged(connection.State.Snapshot);
    }

    private static IEnumerable<string> AllIds(ComponentLights component)
    {
        yield return component.componentId;
        if (component.alsoCombine == null) yield break;
        foreach (var id in component.alsoCombine)
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
    }

    /// <summary>
    /// The state to display. Where one lamp covers several parts -- three pipe
    /// rams, typically -- the worst state wins: closed beats moving beats open,
    /// so the panel never reads "open" while any of them is shut or moving.
    /// </summary>
    private static string ResolveState(BopState bop, ComponentLights component)
    {
        string worst = null;
        foreach (var id in AllIds(component))
        {
            var state = bop.ComponentState(id);
            if (state == "closed") return "closed";
            if (state == "moving") { worst = "moving"; continue; }
            if (worst == null) worst = state;
        }
        return worst ?? "unknown";
    }

    /// <summary>
    /// Lights the object assigned to this state and switches the others off, so
    /// two lamps on the same part can never be lit at once unless the same
    /// object is assigned twice deliberately.
    /// </summary>
    private void ApplyLights(ComponentLights component, string state)
    {
        if (component.lights == null) return;
        foreach (var entry in component.lights)
        {
            if (entry?.light == null) continue;
            var on = string.Equals(entry.state, state, StringComparison.OrdinalIgnoreCase);
            SetLight(entry.light, on, entry.colour);
        }
    }

    private void SetLight(GameObject light, bool on, Color colour)
    {
        if (light.activeSelf != on) light.SetActive(on);
        if (!on) return;

        var renderer = light.GetComponent<Renderer>();
        if (renderer != null)
        {
            // Property block rather than renderer.material, which clones a
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

    private void SetPressure(TextMeshPro text, float psi, float lowThreshold, bool stale)
    {
        if (text == null) return;
        text.text = string.Format(pressureFormat, psi);
        text.color = stale ? StaleColour : (psi < lowThreshold ? LowColour : NormalColour);
    }

    private void Update()
    {
        if (masterValveObject == null) return;
        _valveAngle = Mathf.Lerp(_valveAngle, _valveTargetAngle, Time.deltaTime * valveRotationSpeed);
        masterValveObject.transform.localRotation =
            _valveClosedRotation * Quaternion.AngleAxis(_valveAngle, rotationAxis);
    }
}
