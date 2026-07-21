using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls cyber chair button rotations based on pump states from simulation
/// Rotates buttons 45 degrees when pumps are ON/OFF
/// </summary>
public class CyberChairButtonController : MonoBehaviour
{
    [Header("Pump Button Transforms")]
    [Tooltip("Transform of the physical button for Pump 1")]
    public Transform pump1Button;
    
    [Tooltip("Transform of the physical button for Pump 2")]
    public Transform pump2Button;
    
    [Tooltip("Transform of the physical button for Pump 3")]
    public Transform pump3Button;
    
    [Header("TopDrive Button")]
    [Tooltip("Transform of the physical button for TopDrive/Rotation")]
    public Transform topDriveButton;
    
    [Header("Rotation Settings")]
    [Tooltip("Rotation angle when pump is ON (degrees)")]
    public float onRotationAngle = 45f;
    
    [Tooltip("Rotation angle when pump is OFF (degrees)")]
    public float offRotationAngle = 0f;
    
    [Tooltip("Rotation axis (default is Y axis for left/right rotation)")]
    public Vector3 rotationAxis = Vector3.up;
    
    [Tooltip("Speed of rotation animation")]
    public float rotationSpeed = 5f;
    
    [Tooltip("Use smooth rotation animation")]
    public bool smoothRotation = true;
    
    [Header("Visual Feedback")]
    [Tooltip("Optional: Material to use when pump is ON")]
    public Material onMaterial;
    
    [Tooltip("Optional: Material to use when pump is OFF")]
    public Material offMaterial;
    
    [Tooltip("Optional: Emission color when ON")]
    public Color onEmissionColor = Color.green;
    
    [Tooltip("Optional: Emission color when OFF")]
    public Color offEmissionColor = Color.red;
    
    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool forceTestMode = false;  // Force test mode to bypass SimulationDataReceiver
    
    // Current pump states
    private bool pump1State = false;
    private bool pump2State = false;
    private bool pump3State = false;
    private bool topDriveState = false;
    
    // Target rotations
    private Quaternion pump1TargetRotation;
    private Quaternion pump2TargetRotation;
    private Quaternion pump3TargetRotation;
    private Quaternion topDriveTargetRotation;
    
    // Initial rotations (to restore if needed)
    private Quaternion pump1InitialRotation;
    private Quaternion pump2InitialRotation;
    private Quaternion pump3InitialRotation;
    private Quaternion topDriveInitialRotation;
    
    // Renderers for material changes
    private Renderer pump1Renderer;
    private Renderer pump2Renderer;
    private Renderer pump3Renderer;
    private Renderer topDriveRenderer;
    
    // Reference to SimulationDataReceiver
    private SimulationDataReceiver dataReceiver;
    
    void Start()
    {
        // Find SimulationDataReceiver in the scene
        dataReceiver = FindObjectOfType<SimulationDataReceiver>();
        if (dataReceiver == null)
        {
            Debug.LogWarning("[CyberChairButtonController] SimulationDataReceiver not found! Button states won't update.");
        }
        
        // Store initial rotations
        if (pump1Button != null)
        {
            pump1InitialRotation = pump1Button.rotation;
            pump1TargetRotation = pump1InitialRotation;
            pump1Renderer = pump1Button.GetComponent<Renderer>();
        }
        
        if (pump2Button != null)
        {
            pump2InitialRotation = pump2Button.rotation;
            pump2TargetRotation = pump2InitialRotation;
            pump2Renderer = pump2Button.GetComponent<Renderer>();
        }
        
        if (pump3Button != null)
        {
            pump3InitialRotation = pump3Button.rotation;
            pump3TargetRotation = pump3InitialRotation;
            pump3Renderer = pump3Button.GetComponent<Renderer>();
        }
        
        if (topDriveButton != null)
        {
            topDriveInitialRotation = topDriveButton.rotation;
            topDriveTargetRotation = topDriveInitialRotation;
            topDriveRenderer = topDriveButton.GetComponent<Renderer>();
        }
        
        // Normalize rotation axis
        rotationAxis.Normalize();
        
        if (showDebugLogs)
        {
            Debug.Log("[CyberChairButtonController] Initialized with buttons:");
            Debug.Log($"  Pump 1: {(pump1Button != null ? pump1Button.name : "Not assigned")}");
            Debug.Log($"  Pump 2: {(pump2Button != null ? pump2Button.name : "Not assigned")}");
            Debug.Log($"  Pump 3: {(pump3Button != null ? pump3Button.name : "Not assigned")}");
            Debug.Log($"  TopDrive: {(topDriveButton != null ? topDriveButton.name : "Not assigned")}");
        }
    }
    
    void Update()
    {
        // Check for pump state changes from simulation data
        if (!forceTestMode)
        {
            if (dataReceiver != null && dataReceiver.currentData != null && 
                dataReceiver.currentData.simulation_data != null)
            {
                var simData = dataReceiver.currentData.simulation_data;
                
                if (showDebugLogs && Time.frameCount % 60 == 0) // Log every second
                {
                    Debug.Log($"[CyberChairButtonController] Simulation Data - Pump1: {simData.pump_1_active}, Pump2: {simData.pump_2_active}, Pump3: {simData.pump_3_active}, RPM: {simData.rpm}");
                }
                
                // Update pump states
                UpdatePumpState(1, simData.pump_1_active, ref pump1State, pump1Button, ref pump1TargetRotation, pump1InitialRotation);
                UpdatePumpState(2, simData.pump_2_active, ref pump2State, pump2Button, ref pump2TargetRotation, pump2InitialRotation);
                UpdatePumpState(3, simData.pump_3_active, ref pump3State, pump3Button, ref pump3TargetRotation, pump3InitialRotation);
                
                // Update TopDrive state (ON when RPM > 0)
                bool topDriveOn = simData.rpm > 0;
                UpdateTopDriveState(topDriveOn, ref topDriveState, topDriveButton, ref topDriveTargetRotation, topDriveInitialRotation);
            }
            else if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                Debug.LogWarning("[CyberChairButtonController] No simulation data available!");
            }
        }
        
        // Apply smooth rotation if enabled
        if (smoothRotation)
        {
            ApplySmoothRotation(pump1Button, pump1TargetRotation);
            ApplySmoothRotation(pump2Button, pump2TargetRotation);
            ApplySmoothRotation(pump3Button, pump3TargetRotation);
            ApplySmoothRotation(topDriveButton, topDriveTargetRotation);
        }
    }
    
    void UpdatePumpState(int pumpNumber, bool newState, ref bool currentState, Transform button, 
                         ref Quaternion targetRotation, Quaternion initialRotation)
    {
        if (button == null)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[CyberChairButtonController] Pump {pumpNumber} button is null!");
            }
            return;
        }
        
        // Check if state has changed
        if (newState != currentState)
        {
            currentState = newState;
            
            if (showDebugLogs)
            {
                Debug.Log($"[CyberChairButtonController] Pump {pumpNumber} state changed to: {(newState ? "ON" : "OFF")}");
                Debug.Log($"  Button: {button.name}");
                Debug.Log($"  Current rotation: {button.rotation.eulerAngles}");
            }
            
            // Calculate target rotation
            float targetAngle = newState ? onRotationAngle : offRotationAngle;
            targetRotation = initialRotation * Quaternion.AngleAxis(targetAngle, rotationAxis);
            
            if (showDebugLogs)
            {
                Debug.Log($"  Target angle: {targetAngle}");
                Debug.Log($"  Target rotation: {targetRotation.eulerAngles}");
                Debug.Log($"  Rotation axis: {rotationAxis}");
            }
            
            // Apply immediate rotation if smooth rotation is disabled
            if (!smoothRotation)
            {
                button.rotation = targetRotation;
                if (showDebugLogs)
                {
                    Debug.Log($"  Applied immediate rotation: {button.rotation.eulerAngles}");
                }
            }
            
            // Update visual feedback
            UpdateVisualFeedback(pumpNumber, newState);
        }
    }
    
    void UpdateTopDriveState(bool newState, ref bool currentState, Transform button, 
                             ref Quaternion targetRotation, Quaternion initialRotation)
    {
        if (button == null) return;
        
        // Check if state has changed
        if (newState != currentState)
        {
            currentState = newState;
            
            if (showDebugLogs)
            {
                Debug.Log($"[CyberChairButtonController] TopDrive state changed to: {(newState ? "ON (Rotating)" : "OFF (Stopped)")}");
            }
            
            // Calculate target rotation
            float targetAngle = newState ? onRotationAngle : offRotationAngle;
            targetRotation = initialRotation * Quaternion.AngleAxis(targetAngle, rotationAxis);
            
            // Apply immediate rotation if smooth rotation is disabled
            if (!smoothRotation)
            {
                button.rotation = targetRotation;
            }
            
            // Update visual feedback for TopDrive
            UpdateTopDriveVisualFeedback(newState);
        }
    }
    
    void ApplySmoothRotation(Transform button, Quaternion targetRotation)
    {
        if (button == null) return;
        
        // Check if we need to rotate
        float angleDifference = Quaternion.Angle(button.rotation, targetRotation);
        if (angleDifference > 0.01f) // Only rotate if difference is significant
        {
            // Smoothly rotate towards target
            button.rotation = Quaternion.Lerp(button.rotation, targetRotation, Time.deltaTime * rotationSpeed);
            
            if (showDebugLogs && Time.frameCount % 30 == 0) // Log every half second
            {
                Debug.Log($"[CyberChairButtonController] Rotating {button.name}: Current: {button.rotation.eulerAngles}, Target: {targetRotation.eulerAngles}, Difference: {angleDifference:F2}°");
            }
        }
    }
    
    void UpdateVisualFeedback(int pumpNumber, bool isOn)
    {
        Renderer renderer = null;
        
        switch (pumpNumber)
        {
            case 1:
                renderer = pump1Renderer;
                break;
            case 2:
                renderer = pump2Renderer;
                break;
            case 3:
                renderer = pump3Renderer;
                break;
        }
        
        if (renderer == null) return;
        
        // Change material if provided
        if (onMaterial != null && offMaterial != null)
        {
            renderer.material = isOn ? onMaterial : offMaterial;
        }
        
        // Change emission color if material supports it
        if (renderer.material.HasProperty("_EmissionColor"))
        {
            Color emissionColor = isOn ? onEmissionColor : offEmissionColor;
            renderer.material.SetColor("_EmissionColor", emissionColor);
            
            // Enable emission
            if (isOn)
            {
                renderer.material.EnableKeyword("_EMISSION");
            }
        }
    }
    
    void UpdateTopDriveVisualFeedback(bool isOn)
    {
        if (topDriveRenderer == null) return;
        
        // Change material if provided
        if (onMaterial != null && offMaterial != null)
        {
            topDriveRenderer.material = isOn ? onMaterial : offMaterial;
        }
        
        // Change emission color if material supports it
        if (topDriveRenderer.material.HasProperty("_EmissionColor"))
        {
            Color emissionColor = isOn ? onEmissionColor : offEmissionColor;
            topDriveRenderer.material.SetColor("_EmissionColor", emissionColor);
            
            // Enable emission
            if (isOn)
            {
                topDriveRenderer.material.EnableKeyword("_EMISSION");
            }
        }
    }
    
    // Public methods for manual control (useful for testing)
    
    /// <summary>
    /// Manually set pump 1 state
    /// </summary>
    public void SetPump1State(bool isOn)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[CyberChairButtonController] SetPump1State called with: {isOn}");
        }
        
        // Force state update by temporarily setting to opposite state
        if (pump1State == isOn)
        {
            pump1State = !isOn;
        }
        
        UpdatePumpState(1, isOn, ref pump1State, pump1Button, ref pump1TargetRotation, pump1InitialRotation);
    }
    
    /// <summary>
    /// Manually set pump 2 state
    /// </summary>
    public void SetPump2State(bool isOn)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[CyberChairButtonController] SetPump2State called with: {isOn}");
        }
        
        // Force state update by temporarily setting to opposite state
        if (pump2State == isOn)
        {
            pump2State = !isOn;
        }
        
        UpdatePumpState(2, isOn, ref pump2State, pump2Button, ref pump2TargetRotation, pump2InitialRotation);
    }
    
    /// <summary>
    /// Manually set pump 3 state
    /// </summary>
    public void SetPump3State(bool isOn)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[CyberChairButtonController] SetPump3State called with: {isOn}");
        }
        
        // Force state update by temporarily setting to opposite state
        if (pump3State == isOn)
        {
            pump3State = !isOn;
        }
        
        UpdatePumpState(3, isOn, ref pump3State, pump3Button, ref pump3TargetRotation, pump3InitialRotation);
    }
    
    /// <summary>
    /// Manually set TopDrive state
    /// </summary>
    public void SetTopDriveState(bool isOn)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[CyberChairButtonController] SetTopDriveState called with: {isOn}");
        }
        
        // Force state update by temporarily setting to opposite state
        if (topDriveState == isOn)
        {
            topDriveState = !isOn;
        }
        
        UpdateTopDriveState(isOn, ref topDriveState, topDriveButton, ref topDriveTargetRotation, topDriveInitialRotation);
    }
    
    /// <summary>
    /// Set all pump states at once
    /// </summary>
    public void SetAllPumpStates(bool pump1, bool pump2, bool pump3)
    {
        SetPump1State(pump1);
        SetPump2State(pump2);
        SetPump3State(pump3);
    }
    
    /// <summary>
    /// Set all states including TopDrive
    /// </summary>
    public void SetAllStates(bool pump1, bool pump2, bool pump3, bool topDrive)
    {
        SetPump1State(pump1);
        SetPump2State(pump2);
        SetPump3State(pump3);
        SetTopDriveState(topDrive);
    }
    
    /// <summary>
    /// Reset all buttons to initial rotation
    /// </summary>
    public void ResetAllButtons()
    {
        if (pump1Button != null)
        {
            pump1Button.rotation = pump1InitialRotation;
            pump1TargetRotation = pump1InitialRotation;
            pump1State = false;
        }
        
        if (pump2Button != null)
        {
            pump2Button.rotation = pump2InitialRotation;
            pump2TargetRotation = pump2InitialRotation;
            pump2State = false;
        }
        
        if (pump3Button != null)
        {
            pump3Button.rotation = pump3InitialRotation;
            pump3TargetRotation = pump3InitialRotation;
            pump3State = false;
        }
        
        if (topDriveButton != null)
        {
            topDriveButton.rotation = topDriveInitialRotation;
            topDriveTargetRotation = topDriveInitialRotation;
            topDriveState = false;
        }
        
        if (showDebugLogs)
        {
            Debug.Log("[CyberChairButtonController] All buttons reset to initial state");
        }
    }
    
    /// <summary>
    /// Get current pump states
    /// </summary>
    public void GetPumpStates(out bool pump1, out bool pump2, out bool pump3)
    {
        pump1 = pump1State;
        pump2 = pump2State;
        pump3 = pump3State;
    }
    
    /// <summary>
    /// Get all states including TopDrive
    /// </summary>
    public void GetAllStates(out bool pump1, out bool pump2, out bool pump3, out bool topDrive)
    {
        pump1 = pump1State;
        pump2 = pump2State;
        pump3 = pump3State;
        topDrive = topDriveState;
    }
    
    void OnDrawGizmosSelected()
    {
        // Draw rotation axis for each button
        if (pump1Button != null)
        {
            Gizmos.color = pump1State ? Color.green : Color.red;
            Gizmos.DrawRay(pump1Button.position, pump1Button.TransformDirection(rotationAxis) * 0.5f);
        }
        
        if (pump2Button != null)
        {
            Gizmos.color = pump2State ? Color.green : Color.red;
            Gizmos.DrawRay(pump2Button.position, pump2Button.TransformDirection(rotationAxis) * 0.5f);
        }
        
        if (pump3Button != null)
        {
            Gizmos.color = pump3State ? Color.green : Color.red;
            Gizmos.DrawRay(pump3Button.position, pump3Button.TransformDirection(rotationAxis) * 0.5f);
        }
    }
}