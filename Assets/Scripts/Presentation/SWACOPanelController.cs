using TMPro;
using UnityEngine;
using VRSIM.Net;
using VRSIM.State;

/// <summary>
/// Drives the SWACO panel's gauge needles, readouts and indicator lights from
/// engine state.
///
/// Display only. Field names and the global namespace are unchanged, because
/// three needles and five readouts are wired by hand in the scene.
/// </summary>
public class SWACOPanelController : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Left empty, the first RigConnection in the scene is used.")]
    public RigConnection connection;

    [Header("Light Controls")]
    [Tooltip("Hold and Reset indicators. Protocol v1 has no field for these -- "
           + "see the note in OnStateChanged.")]
    public GameObject holdLight;
    public GameObject resetLight;

    [Header("Pressure Gauge Needles")]
    public Transform standpipeNeedle;
    public Transform casingNeedle;
    public Transform pumpPressure1Needle;
    public Transform pumpPressure2Needle;
    public Transform chokeManifoldNeedle;

    [Header("Pressure Gauge Settings")]
    public float standpipeMaxPressure = 5000f;
    public float casingMaxPressure = 3000f;
    public float pumpPressure1MaxPressure = 4000f;
    public float pumpPressure2MaxPressure = 4000f;
    public float chokeManifoldMaxPressure = 5000f;

    [Header("Needle Rotation Settings")]
    [Tooltip("Angle at zero pressure, typically bottom-left")]
    public float needleMinRotation = -135f;
    [Tooltip("Angle at full scale, typically bottom-right")]
    public float needleMaxRotation = 135f;
    public float needleRotationSpeed = 2f;

    [Header("Text Displays - Totalizer")]
    public TextMeshProUGUI totalStrokesText;
    public TextMeshPro totalStrokes3DText;
    public TextMeshProUGUI totalVolumeText;
    public TextMeshPro totalVolume3DText;

    [Header("Text Displays - Pump Data")]
    public TextMeshProUGUI mp1SpeedText;
    public TextMeshPro mp1Speed3DText;
    public TextMeshProUGUI mp1PressureText;
    public TextMeshPro mp1Pressure3DText;

    public TextMeshProUGUI mp2SpeedText;
    public TextMeshPro mp2Speed3DText;
    public TextMeshProUGUI mp2PressureText;
    public TextMeshPro mp2Pressure3DText;

    [Header("Text Displays - Pressures")]
    public TextMeshProUGUI standpipePressureText;
    public TextMeshPro standpipePressure3DText;
    public TextMeshProUGUI casingPressureText;
    public TextMeshPro casingPressure3DText;
    public TextMeshProUGUI chokeManifoldPressureText;
    public TextMeshPro chokeManifoldPressure3DText;

    [Header("Display Settings")]
    public string pressureFormat = "{0:F0} PSI";
    public string volumeFormat = "{0:F1} BBL";
    public string speedFormat = "{0:F0} SPM";
    public string strokesFormat = "{0:F0}";

    private float _standpipeTarget, _casingTarget, _pump1Target, _pump2Target, _chokeTarget;

    private void Awake()
    {
        if (connection == null) connection = FindObjectOfType<RigConnection>();
    }

    private void OnEnable()
    {
        if (connection == null) return;
        connection.State.Changed += OnStateChanged;
        if (connection.State.HasData) OnStateChanged(connection.State.Snapshot);
    }

    private void OnDisable()
    {
        if (connection == null) return;
        connection.State.Changed -= OnStateChanged;
    }

    private void OnStateChanged(RigSnapshot snapshot)
    {
        var swaco = snapshot.Swaco;
        var pumps = snapshot.Pumps;

        _standpipeTarget = Angle(swaco.StandpipePsi, standpipeMaxPressure);
        _casingTarget = Angle(swaco.CasingPsi, casingMaxPressure);
        _pump1Target = Angle(pumps.Pump1.PressurePsi, pumpPressure1MaxPressure);
        _pump2Target = Angle(pumps.Pump2.PressurePsi, pumpPressure2MaxPressure);
        // The choke gauge shows manifold pressure; choke_position is the valve
        // opening (0-1), which is a different quantity and not what this reads.
        _chokeTarget = Angle(swaco.StandpipePsi * swaco.ChokePosition, chokeManifoldMaxPressure);

        Set(totalStrokesText, totalStrokes3DText, string.Format(strokesFormat, swaco.TotalStrokes));
        Set(totalVolumeText, totalVolume3DText, string.Format(volumeFormat, swaco.TotalVolumeBbl));

        Set(mp1SpeedText, mp1Speed3DText, string.Format(speedFormat, pumps.Pump1.Spm));
        Set(mp1PressureText, mp1Pressure3DText, string.Format(pressureFormat, pumps.Pump1.PressurePsi));
        Set(mp2SpeedText, mp2Speed3DText, string.Format(speedFormat, pumps.Pump2.Spm));
        Set(mp2PressureText, mp2Pressure3DText, string.Format(pressureFormat, pumps.Pump2.PressurePsi));

        Set(standpipePressureText, standpipePressure3DText, string.Format(pressureFormat, swaco.StandpipePsi));
        Set(casingPressureText, casingPressure3DText, string.Format(pressureFormat, swaco.CasingPsi));
        Set(chokeManifoldPressureText, chokeManifoldPressure3DText,
            string.Format(pressureFormat, swaco.StandpipePsi * swaco.ChokePosition));

        // Hold and Reset are physical indicators on the real panel, but
        // protocol v1 defines no field for them, so they are left alone rather
        // than driven from an invented meaning. They need adding to the
        // protocol (docs/protocol.md section 5.2) once the engine models them.
    }

    /// <summary>Maps a pressure onto the gauge's sweep, clamped to the dial.</summary>
    private float Angle(float value, float fullScale)
    {
        if (fullScale <= 0f) return needleMinRotation;
        return Mathf.Lerp(needleMinRotation, needleMaxRotation, Mathf.Clamp01(value / fullScale));
    }

    private static void Set(TextMeshProUGUI canvasText, TextMeshPro worldText, string value)
    {
        if (canvasText != null) canvasText.text = value;
        if (worldText != null) worldText.text = value;
    }

    private void Update()
    {
        Sweep(standpipeNeedle, _standpipeTarget);
        Sweep(casingNeedle, _casingTarget);
        Sweep(pumpPressure1Needle, _pump1Target);
        Sweep(pumpPressure2Needle, _pump2Target);
        Sweep(chokeManifoldNeedle, _chokeTarget);
    }

    /// <summary>
    /// Eases a needle toward its target on Z only.
    ///
    /// Note this overwrites X and Y local rotation, so a needle prefab that
    /// relies on a tilt to sit flat against the dial face will be snapped
    /// upright. Needle pivots must be authored for Z-only rotation.
    /// </summary>
    private void Sweep(Transform needle, float targetZ)
    {
        if (needle == null) return;
        var current = needle.localEulerAngles.z;
        if (current > 180f) current -= 360f;
        var next = Mathf.LerpAngle(current, targetZ, Time.deltaTime * needleRotationSpeed);
        needle.localEulerAngles = new Vector3(0f, 0f, next);
    }
}
