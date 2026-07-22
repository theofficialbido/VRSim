using UnityEngine;
using VRSIM.Net;
using VRSIM.State;

/// <summary>
/// Tilts the driller's console joystick model to reflect what the top drive is
/// doing.
///
/// Its meaning has changed. It used to mirror the axes of a physical USB
/// joystick that the engine read on the PC; VR is now the only input, so there
/// are no hardware axes to mirror. Instead it shows the *result*: the stick
/// leans while the top drive is travelling and springs back when it stops, so
/// the console reads correctly to anyone looking at it.
///
/// Field names and the global namespace are unchanged -- joystickStick is wired
/// by hand in the scene.
/// </summary>
public class JoystickVisualizer : MonoBehaviour
{
    [Header("Joystick Model")]
    [Tooltip("The stick/handle to tilt")]
    public Transform joystickStick;

    [Header("Data Source")]
    [Tooltip("Left empty, the first RigConnection in the scene is used.")]
    public RigConnection connection;

    [Header("Movement Settings")]
    [Tooltip("Maximum tilt in degrees")]
    public float maxRotationAngle = 30f;
    [Tooltip("Tilt the stick, rather than sliding it")]
    public bool useRotation = true;
    [Tooltip("Maximum offset if not using rotation")]
    public float maxPositionOffset = 0.1f;

    [Header("Visual Settings")]
    public bool smoothMovement = true;
    public float smoothSpeed = 10f;
    [Tooltip("Faster return to centre when the drive stops, like a real spring")]
    public float springReturnSpeed = 15f;
    public bool invertY = false;

    private Quaternion _initialRotation;
    private Vector3 _initialPosition;
    private float _target;   // -1 up, +1 down, 0 centred
    private float _current;

    private void Awake()
    {
        if (connection == null) connection = FindAnyObjectByType<RigConnection>();
        if (joystickStick == null) joystickStick = transform;
        _initialRotation = joystickStick.localRotation;
        _initialPosition = joystickStick.localPosition;
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
        switch (snapshot.Drilling.TdsMovement)
        {
            case "up": _target = -1f; break;
            case "down": _target = 1f; break;
            default: _target = 0f; break;
        }
        if (invertY) _target = -_target;
    }

    private void Update()
    {
        // Returning to centre is quicker than leaving it, which is how a
        // sprung lever actually behaves.
        var speed = Mathf.Approximately(_target, 0f) ? springReturnSpeed : smoothSpeed;
        _current = smoothMovement
            ? Mathf.Lerp(_current, _target, Time.deltaTime * speed)
            : _target;
        if (Mathf.Abs(_current) < 0.01f) _current = 0f;

        if (useRotation)
            joystickStick.localRotation = _initialRotation * Quaternion.Euler(_current * maxRotationAngle, 0f, 0f);
        else
            joystickStick.localPosition = _initialPosition + new Vector3(0f, 0f, _current * maxPositionOffset);
    }
}
