using TMPro;
using UnityEngine;
using VRSIM.Net;
using VRSIM.Presentation;
using VRSIM.State;

/// <summary>
/// Drives the BOP panel's status lights, pressure readouts and master valve
/// from engine state.
///
/// Display only. Nothing here writes to rig state -- operating a BOP control
/// sends a command, and the change comes back through <see cref="RigState"/>.
/// That keeps a single authority for whether a ram is open.
///
/// Two things are deliberately unchanged from the previous implementation:
/// the public field names, because the scene has twelve lights and four
/// readouts wired to them by hand and renaming a serialized field silently
/// drops the assignment; and the global namespace, for the same reason.
/// </summary>
public class BOPPanelController : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Left empty, the first RigConnection in the scene is used.")]
    public RigConnection connection;

    [Header("BOP Status Lights - Separate Green/Red")]
    [Tooltip("Green lights for OPEN state")]
    public GameObject annularGreenLight;
    public GameObject pipeRamGreenLight;
    public GameObject blindRamGreenLight;
    public GameObject shearRamGreenLight;
    public GameObject killLineGreenLight;
    public GameObject chokeLineGreenLight;

    [Tooltip("Red lights for CLOSED state")]
    public GameObject annularRedLight;
    public GameObject pipeRamRedLight;
    public GameObject blindRamRedLight;
    public GameObject shearRamRedLight;
    public GameObject killLineRedLight;
    public GameObject chokeLineRedLight;

    [Header("Master Valve")]
    public GameObject masterValveGreenLight;
    public GameObject masterValveRedLight;
    [Tooltip("The master valve wheel to rotate")]
    public GameObject masterValveObject;
    [Tooltip("Degrees the wheel turns between closed and open")]
    public float valveOpenAngle = 90f;
    [Tooltip("Axis to rotate around (default Z for a wheel)")]
    public Vector3 rotationAxis = new Vector3(0, 0, 1);
    public float valveRotationSpeed = 2f;

    [Header("BOP Pressure Displays")]
    [Tooltip("World-space readouts. These are the ones the scene actually uses.")]
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
    [Tooltip("Dim readouts when the engine stops sending, so a frozen number "
           + "is never mistaken for a live one.")]
    public bool dimWhenStale = true;

    private static readonly Color NormalColour = Color.white;
    private static readonly Color LowColour = new Color(1f, 0.82f, 0.25f);
    private static readonly Color StaleColour = new Color(0.45f, 0.45f, 0.45f);

    private readonly StatusIndicator _annular = new StatusIndicator();
    private readonly StatusIndicator _pipeRam = new StatusIndicator();
    private readonly StatusIndicator _blindRam = new StatusIndicator();
    private readonly StatusIndicator _shearRam = new StatusIndicator();
    private readonly StatusIndicator _killLine = new StatusIndicator();
    private readonly StatusIndicator _chokeLine = new StatusIndicator();
    private readonly StatusIndicator _masterValve = new StatusIndicator();

    private Quaternion _valveClosedRotation;
    private float _valveAngle;
    private float _valveTargetAngle;

    private void Awake()
    {
        if (connection == null) connection = FindObjectOfType<RigConnection>();

        _annular.greenLight = annularGreenLight;       _annular.redLight = annularRedLight;
        _pipeRam.greenLight = pipeRamGreenLight;       _pipeRam.redLight = pipeRamRedLight;
        _blindRam.greenLight = blindRamGreenLight;     _blindRam.redLight = blindRamRedLight;
        _shearRam.greenLight = shearRamGreenLight;     _shearRam.redLight = shearRamRedLight;
        _killLine.greenLight = killLineGreenLight;     _killLine.redLight = killLineRedLight;
        _chokeLine.greenLight = chokeLineGreenLight;   _chokeLine.redLight = chokeLineRedLight;
        _masterValve.greenLight = masterValveGreenLight; _masterValve.redLight = masterValveRedLight;

        if (masterValveObject != null)
            _valveClosedRotation = masterValveObject.transform.localRotation;
    }

    private void OnEnable()
    {
        if (connection == null) return;
        // Event-driven rather than polled. The previous version updated on every
        // 30th frame to hide flicker caused by the receiver clearing its state
        // dictionary on each message; that cause is gone, so updates can be
        // immediate and exact.
        connection.State.Changed += OnStateChanged;
        connection.State.StaleChanged += OnStaleChanged;
        if (connection.State.HasData) OnStateChanged(connection.State.Snapshot);
    }

    private void OnDisable()
    {
        if (connection == null) return;
        connection.State.Changed -= OnStateChanged;
        connection.State.StaleChanged -= OnStaleChanged;
    }

    private void OnStateChanged(RigSnapshot snapshot)
    {
        var bop = snapshot.Bop;

        _annular.Apply(bop.ComponentState("annular_1"));
        _pipeRam.Apply(CombinedPipeRamState(bop));

        // The data has one blind/shear ram and the panel has two indicator
        // pairs, so both show the same value. They cannot disagree.
        var blindShear = bop.ComponentState("blind_shear_ram");
        _blindRam.Apply(blindShear);
        _shearRam.Apply(blindShear);

        _killLine.Apply(bop.ComponentState("kill_line"));
        _chokeLine.Apply(bop.ComponentState("choke_line"));

        var open = bop.MasterValve == "open";
        _masterValve.SetBoolean(open);
        _valveTargetAngle = open ? valveOpenAngle : 0f;

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

    /// <summary>
    /// Three pipe rams share one indicator. Closed beats moving, which beats
    /// open: the panel must never read "open" while any ram is shut or on its
    /// way there.
    /// </summary>
    private static string CombinedPipeRamState(BopState bop)
    {
        var moving = false;
        for (var i = 1; i <= 3; i++)
        {
            switch (bop.ComponentState($"pipe_ram_{i}"))
            {
                case "closed": return "closed";
                case "moving": moving = true; break;
            }
        }
        return moving ? "moving" : "open";
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
        // Turn the wheel toward its target rather than snapping, so the motion
        // reads as mechanical.
        _valveAngle = Mathf.Lerp(_valveAngle, _valveTargetAngle, Time.deltaTime * valveRotationSpeed);
        masterValveObject.transform.localRotation =
            _valveClosedRotation * Quaternion.AngleAxis(_valveAngle, rotationAxis);
    }
}
