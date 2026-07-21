using UnityEngine;

/// <summary>
/// Visualizes joystick input by rotating/moving a 3D joystick model
/// Maps the input values to visual stick movement
/// </summary>
public class JoystickVisualizer : MonoBehaviour
{
    [Header("Joystick Model")]
    [Tooltip("The joystick stick/handle to move")]
    public Transform joystickStick;
    
    [Tooltip("Which joystick to visualize")]
    public JoystickType joystickType = JoystickType.Left;
    
    [Header("Movement Settings")]
    [Tooltip("Maximum rotation angle in degrees")]
    public float maxRotationAngle = 30f;  // 30 degrees forward or backward
    
    [Tooltip("Use rotation instead of position")]
    public bool useRotation = true;
    
    [Tooltip("Maximum position offset if not using rotation")]
    public float maxPositionOffset = 0.1f;
    
    [Header("Visual Settings")]
    [Tooltip("Smooth the movement")]
    public bool smoothMovement = true;
    
    [Tooltip("Smoothing speed")]
    public float smoothSpeed = 10f;
    
    [Tooltip("Spring return speed when released")]
    public float springReturnSpeed = 15f;
    
    [Tooltip("Invert X axis")]
    public bool invertX = false;
    
    [Tooltip("Invert Y axis")]
    public bool invertY = false;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    public enum JoystickType
    {
        Left,
        Right
    }
    
    private SimulationDataReceiver dataReceiver;
    private Vector2 currentInput;
    private Vector2 targetInput;
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    
    void Start()
    {
        // Ensure Unity runs in background
        Application.runInBackground = true;
        
        // Find the SimulationDataReceiver
        dataReceiver = FindObjectOfType<SimulationDataReceiver>();
        
        if (dataReceiver == null)
        {
            Debug.LogWarning("[JoystickVisualizer] SimulationDataReceiver not found! Joystick visualization will not work.");
        }
        
        // If no stick is assigned, try to find it as a child
        if (joystickStick == null)
        {
            // Try common names for joystick parts
            Transform stick = transform.Find("Stick") ?? transform.Find("Handle") ?? transform.Find("Joystick");
            if (stick != null)
            {
                joystickStick = stick;
                Debug.Log($"[JoystickVisualizer] Found joystick stick: {stick.name}");
            }
            else if (transform.childCount > 0)
            {
                joystickStick = transform.GetChild(0);
                Debug.Log($"[JoystickVisualizer] Using first child as stick: {joystickStick.name}");
            }
        }
        
        if (joystickStick != null)
        {
            initialPosition = joystickStick.localPosition;
            initialRotation = joystickStick.localRotation;
            Debug.Log($"[JoystickVisualizer] Joystick stick initialized: {joystickStick.name}");
            Debug.Log($"[JoystickVisualizer] Initial rotation: {initialRotation.eulerAngles}");
            Debug.Log($"[JoystickVisualizer] Max rotation angle: {maxRotationAngle} degrees");
        }
        else
        {
            Debug.LogError("[JoystickVisualizer] No joystick stick/handle assigned! Please assign the joystick stick Transform.");
            enabled = false;
        }
    }
    
    void Update()
    {
        if (joystickStick == null || dataReceiver == null)
            return;
        
        // Get input based on joystick type
        UpdateInput();
        
        // Apply smoothing if enabled
        if (smoothMovement)
        {
            // Use spring return speed when returning to center (stop command)
            // Use normal smooth speed when moving to a position
            float speed = (targetInput.magnitude < 0.1f) ? springReturnSpeed : smoothSpeed;
            currentInput = Vector2.Lerp(currentInput, targetInput, Time.deltaTime * speed);
            
            // Force to zero if very close to center (deadzone)
            if (targetInput.magnitude < 0.01f && currentInput.magnitude < 0.05f)
            {
                currentInput = Vector2.zero;
            }
        }
        else
        {
            currentInput = targetInput;
        }
        
        // Apply the movement/rotation
        if (useRotation)
        {
            ApplyRotation();
        }
        else
        {
            ApplyPosition();
        }
    }
    
    void UpdateInput()
    {
        if (dataReceiver.currentData == null)
        {
            targetInput = Vector2.zero;
            return;
        }
        
        float x = 0f;
        float y = 0f;
        
        if (joystickType == JoystickType.Left)
        {
            // Get the joystick data that represents TDS movement
            x = dataReceiver.currentData.left_x;  // RPM/rotation control
            y = dataReceiver.currentData.left_y;  // TDS up/down control
            
            // The Python bridge sends:
            // y = -1.0 when TDS is moving UP (joystick pulled back)
            // y = 1.0 when TDS is moving DOWN (joystick pushed forward)
            // y = 0.0 when TDS is STOPPED (joystick centered)
            
            // This is already correct for joystick visualization:
            // -1 = pulled back, 1 = pushed forward
        }
        else
        {
            x = dataReceiver.currentData.right_x;
            y = dataReceiver.currentData.right_y;
        }
        
        // Apply inversion if needed
        if (invertX) x = -x;
        if (invertY) y = -y;
        
        targetInput = new Vector2(x, y);
    }
    
    void ApplyRotation()
    {
        // JOYSTICK ROTATION MAPPING:
        // The joystick rotates ONLY on the X-axis (forward/backward tilt)
        // Y input from Python controls the X-axis rotation:
        //   - currentInput.y = -1.0 (UP command) = Joystick pulled BACK = NEGATIVE X rotation (-30 degrees)
        //   - currentInput.y = +1.0 (DOWN command) = Joystick pushed FORWARD = POSITIVE X rotation (+30 degrees)
        //   - currentInput.y = 0.0 (STOP) = Joystick CENTERED = 0 rotation
        
        // Calculate X-axis rotation (forward/backward tilt)
        // Positive Y input = Forward tilt (positive X rotation)
        // Negative Y input = Backward tilt (negative X rotation)
        float xRotation = currentInput.y * maxRotationAngle; // Direct mapping: +1 = +30°, -1 = -30°, 0 = 0°
        
        // Optional: Add left/right rotation if needed (usually not used for TDS control)
        float zRotation = 0f; // Disable Z rotation for TDS joystick
        
        // Apply rotation relative to initial rotation (X-axis only)
        Quaternion targetRotation = initialRotation * Quaternion.Euler(xRotation, 0, zRotation);
        joystickStick.localRotation = targetRotation;
        
        // Debug output
        if (showDebugInfo && Mathf.Abs(currentInput.y) > 0.01f)
        {
            Debug.Log($"[JoystickVisualizer] Y Input: {currentInput.y:F2} => X Rotation: {xRotation:F1}°");
        }
    }
    
    void ApplyPosition()
    {
        // Calculate position offset
        Vector3 offset = new Vector3(
            currentInput.x * maxPositionOffset,
            0,
            currentInput.y * maxPositionOffset
        );
        
        joystickStick.localPosition = initialPosition + offset;
    }
    
    public void ResetJoystick()
    {
        if (joystickStick != null)
        {
            joystickStick.localPosition = initialPosition;
            joystickStick.localRotation = initialRotation;
            currentInput = Vector2.zero;
            targetInput = Vector2.zero;
        }
    }
    
    void OnGUI()
    {
        if (!showDebugInfo || !enabled)
            return;
        
        int yOffset = joystickType == JoystickType.Left ? 10 : 180;
        
        GUI.Box(new Rect(Screen.width - 280, yOffset, 270, 170), $"{joystickType} Joystick Visualizer");
        
        GUI.Label(new Rect(Screen.width - 270, yOffset + 25, 250, 20), 
            $"Raw Input: X={targetInput.x:F2}, Y={targetInput.y:F2}");
        
        GUI.Label(new Rect(Screen.width - 270, yOffset + 45, 250, 20), 
            $"Smoothed: X={currentInput.x:F2}, Y={currentInput.y:F2}");
        
        // Show joystick interpretation and rotation
        string joystickState = "Centered";
        float currentRotation = currentInput.y * maxRotationAngle;
        
        if (currentInput.y < -0.5f) joystickState = "Pulled Back (UP)";
        else if (currentInput.y > 0.5f) joystickState = "Pushed Forward (DOWN)";
        else if (Mathf.Abs(currentInput.y) < 0.1f) joystickState = "CENTERED";
        
        GUI.Label(new Rect(Screen.width - 270, yOffset + 65, 250, 20), 
            $"Joystick: {joystickState}");
        
        GUI.Label(new Rect(Screen.width - 270, yOffset + 85, 250, 20), 
            $"X-Axis Rotation: {currentRotation:F1}° (Max: ±{maxRotationAngle}°)");
        
        if (dataReceiver != null && dataReceiver.currentData != null && dataReceiver.currentData.simulation_data != null)
        {
            var sim = dataReceiver.currentData.simulation_data;
            
            if (joystickType == JoystickType.Left)
            {
                GUI.Label(new Rect(Screen.width - 270, yOffset + 105, 250, 20), 
                    $"TDS Movement: {sim.tds_movement}");
                GUI.Label(new Rect(Screen.width - 270, yOffset + 125, 250, 20), 
                    $"TDS Position: {sim.tds_position:F1} ft");
                GUI.Label(new Rect(Screen.width - 270, yOffset + 145, 250, 20), 
                    $"RPM: {sim.rpm:F0}");
            }
        }
        else
        {
            GUI.Label(new Rect(Screen.width - 270, yOffset + 85, 250, 40), 
                "Waiting for simulation data...");
        }
    }
    
    void OnDrawGizmosSelected()
    {
        if (joystickStick == null)
            return;
        
        // Draw movement range
        Gizmos.color = Color.yellow;
        
        if (useRotation)
        {
            // Draw rotation arc
            Gizmos.DrawWireSphere(joystickStick.position, 0.1f);
            
            // Draw max rotation positions
            Gizmos.color = Color.green;
            Vector3 forward = joystickStick.position + joystickStick.forward * 0.2f;
            Vector3 back = joystickStick.position - joystickStick.forward * 0.2f;
            Vector3 left = joystickStick.position - joystickStick.right * 0.2f;
            Vector3 right = joystickStick.position + joystickStick.right * 0.2f;
            
            Gizmos.DrawLine(joystickStick.position, forward);
            Gizmos.DrawLine(joystickStick.position, back);
            Gizmos.DrawLine(joystickStick.position, left);
            Gizmos.DrawLine(joystickStick.position, right);
        }
        else
        {
            // Draw position bounds
            Gizmos.DrawWireCube(transform.position, Vector3.one * maxPositionOffset * 2);
        }
    }
}