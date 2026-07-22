using UnityEngine;
using VRSIM.Net;
using VRSIM.State;

/// <summary>
/// Rotates the driller's console buttons to mirror what the engine reports.
///
/// Output only: this shows the state of the pumps and top drive, it does not
/// operate them. Pressing a console button in VR sends a command, and the
/// button moves here when the resulting state comes back -- so the console can
/// never show a pump running that the engine does not agree is running.
///
/// Public field names and the global namespace are unchanged from the previous
/// implementation, because the four button transforms are wired by hand in the
/// scene and renaming a serialized field silently drops the assignment.
/// </summary>
public class CyberChairButtonController : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Left empty, the first RigConnection in the scene is used.")]
    public RigConnection connection;

    [Header("Pump Button Transforms")]
    public Transform pump1Button;
    public Transform pump2Button;
    public Transform pump3Button;

    [Header("TopDrive Button")]
    public Transform topDriveButton;

    [Header("Rotation Settings")]
    [Tooltip("Rotation angle when the control is ON (degrees)")]
    public float onRotationAngle = 45f;
    [Tooltip("Rotation angle when the control is OFF (degrees)")]
    public float offRotationAngle = 0f;
    [Tooltip("Rotation axis (default Y, for left/right throw)")]
    public Vector3 rotationAxis = Vector3.up;
    public float rotationSpeed = 5f;
    public bool smoothRotation = true;

    [Header("Visual Feedback")]
    [Tooltip("Optional emissive tint when ON")]
    public Color onEmissionColor = Color.green;
    [Tooltip("Optional emissive tint when OFF")]
    public Color offEmissionColor = Color.red;
    [Tooltip("Tint the button emissively as well as rotating it")]
    public bool useEmission = true;

    /// <summary>One console button: its transform, rest pose and target.</summary>
    private struct Button
    {
        public Transform Transform;
        public Renderer Renderer;
        public Quaternion Rest;
        public Quaternion Target;
        public bool On;
    }

    private Button _pump1, _pump2, _pump3, _topDrive;
    private MaterialPropertyBlock _block;

    private void Awake()
    {
        if (connection == null) connection = FindAnyObjectByType<RigConnection>();
        _block = new MaterialPropertyBlock();
        _pump1 = Init(pump1Button);
        _pump2 = Init(pump2Button);
        _pump3 = Init(pump3Button);
        _topDrive = Init(topDriveButton);
    }

    private static Button Init(Transform t)
    {
        return new Button
        {
            Transform = t,
            Renderer = t != null ? t.GetComponentInChildren<Renderer>() : null,
            Rest = t != null ? t.localRotation : Quaternion.identity,
            Target = t != null ? t.localRotation : Quaternion.identity,
        };
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
        Apply(ref _pump1, snapshot.Pumps.Pump1.Active);
        Apply(ref _pump2, snapshot.Pumps.Pump2.Active);
        Apply(ref _pump3, snapshot.Pumps.Pump3.Active);
        // The top drive has no on/off field; it is running when it is turning.
        Apply(ref _topDrive, snapshot.Drilling.Rpm > 0.5f);
    }

    private void Apply(ref Button button, bool on)
    {
        if (button.Transform == null) return;

        var angle = on ? onRotationAngle : offRotationAngle;
        button.Target = button.Rest * Quaternion.AngleAxis(angle, rotationAxis);

        if (button.On == on) return;
        button.On = on;

        if (!smoothRotation) button.Transform.localRotation = button.Target;
        if (!useEmission || button.Renderer == null) return;

        // A MaterialPropertyBlock, so this does not instantiate a material
        // clone per button. The previous version also enabled the _EMISSION
        // keyword when switching on but never disabled it, leaving the "off"
        // red emission lit permanently once a button had been used.
        button.Renderer.GetPropertyBlock(_block);
        _block.SetColor("_EmissionColor", on ? onEmissionColor : offEmissionColor);
        button.Renderer.SetPropertyBlock(_block);
    }

    private void Update()
    {
        if (!smoothRotation) return;
        Step(ref _pump1);
        Step(ref _pump2);
        Step(ref _pump3);
        Step(ref _topDrive);
    }

    private void Step(ref Button button)
    {
        if (button.Transform == null) return;
        button.Transform.localRotation = Quaternion.Lerp(
            button.Transform.localRotation, button.Target, Time.deltaTime * rotationSpeed);
    }
}
