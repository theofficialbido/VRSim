using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// SWACO Panel Controller - Handles SWACO panel lights, pressure gauges, and displays
/// Reads data from SimulationDataReceiver and controls the 3D model elements
/// </summary>
public class SWACOPanelController : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Reference to SimulationDataReceiver to get SWACO data")]
    public SimulationDataReceiver dataReceiver;
    
    [Header("Needle Simulation Control")]
    [Tooltip("Enable or disable needle simulation for pressure gauges")]
    public bool enableNeedleSimulation = true;
    
    [Header("Light Controls")]
    [Tooltip("Hold and Reset indicator lights")]
    public GameObject holdLight;
    public GameObject resetLight;
    
    [Header("Pressure Gauge Needles")]
    [Tooltip("Needle GameObjects that rotate to show pressure values")]
    public Transform standpipeNeedle;
    public Transform casingNeedle;
    public Transform pumpPressure1Needle;
    public Transform pumpPressure2Needle;
    public Transform chokeManifoldNeedle;
    
    [Header("Pressure Gauge Settings")]
    [Tooltip("Maximum pressure values for each gauge (used for needle rotation calculation)")]
    public float standpipeMaxPressure = 5000f;
    public float casingMaxPressure = 3000f;
    public float pumpPressure1MaxPressure = 4000f;
    public float pumpPressure2MaxPressure = 4000f;
    public float chokeManifoldMaxPressure = 5000f;
    
    [Header("Needle Rotation Settings")]
    [Tooltip("Rotation ranges for pressure needles (in degrees)")]
    public float needleMinRotation = -135f;  // Starting angle (typically bottom-left)
    public float needleMaxRotation = 135f;   // Ending angle (typically bottom-right)
    public float needleRotationSpeed = 2f;   // Speed of needle movement
    
    [Header("Text Displays - Totalizer")]
    [Tooltip("TextMeshPro components for totalizer values")]
    public TextMeshProUGUI totalStrokesText;
    public TextMeshPro totalStrokes3DText;
    public TextMeshProUGUI totalVolumeText;
    public TextMeshPro totalVolume3DText;
    
    [Header("Text Displays - Pump Data")]
    [Tooltip("TextMeshPro components for pump information")]
    public TextMeshProUGUI mp1SpeedText;
    public TextMeshPro mp1Speed3DText;
    public TextMeshProUGUI mp1PressureText;
    public TextMeshPro mp1Pressure3DText;
    
    public TextMeshProUGUI mp2SpeedText;
    public TextMeshPro mp2Speed3DText;
    public TextMeshProUGUI mp2PressureText;
    public TextMeshPro mp2Pressure3DText;
    
    [Header("Text Displays - Pressures")]
    [Tooltip("TextMeshPro components for pressure readings")]
    public TextMeshProUGUI standpipePressureText;
    public TextMeshPro standpipePressure3DText;
    public TextMeshProUGUI casingPressureText;
    public TextMeshPro casingPressure3DText;
    public TextMeshProUGUI chokeManifoldPressureText;
    public TextMeshPro chokeManifoldPressure3DText;
    
    [Header("Display Settings")]
    [Tooltip("Formatting and update settings")]
    public string pressureFormat = "{0:F0} PSI";
    public string volumeFormat = "{0:F1} BBL";
    public string speedFormat = "{0:F0} SPM";
    public string strokesFormat = "{0:F0}";
    public bool includePressureLabels = true;
    public float updateInterval = 0.1f; // Update every 100ms
    
    // Private variables
    private float updateTimer = 0f;
    private Dictionary<string, float> cachedPressures = new Dictionary<string, float>();
    private Dictionary<string, bool> cachedLightStates = new Dictionary<string, bool>();
    private Dictionary<string, float> cachedValues = new Dictionary<string, float>();
    
    // Target needle rotations for smooth movement
    private float targetStandpipeRotation = 0f;
    private float targetCasingRotation = 0f;
    private float targetPump1Rotation = 0f;
    private float targetPump2Rotation = 0f;
    private float targetChokeRotation = 0f;
    
    void Start()
    {
        Debug.Log("[SWACOPanelController] Starting initialization...");
        
        // Auto-find SimulationDataReceiver if not assigned
        if (dataReceiver == null)
        {
            dataReceiver = FindObjectOfType<SimulationDataReceiver>();
            if (dataReceiver == null)
            {
                Debug.LogError("[SWACOPanelController] SimulationDataReceiver not found! Please assign it in the Inspector.");
            }
            else
            {
                Debug.Log("[SWACOPanelController] Found SimulationDataReceiver automatically");
            }
        }
        
        // Validate assignments
        ValidateAssignments();
        
        // Initialize all displays to default values
        InitializeDisplays();
        
        // Start monitoring for data updates
        StartCoroutine(MonitorSWACOData());
    }
    
    void ValidateAssignments()
    {
        Debug.Log("[SWACOPanelController] Validating component assignments...");
        
        // Check light assignments
        if (holdLight == null) Debug.LogWarning("[SWACOPanelController] Hold Light not assigned!");
        if (resetLight == null) Debug.LogWarning("[SWACOPanelController] Reset Light not assigned!");
        
        // Check needle assignments
        if (standpipeNeedle == null) Debug.LogWarning("[SWACOPanelController] Standpipe Needle not assigned!");
        if (casingNeedle == null) Debug.LogWarning("[SWACOPanelController] Casing Needle not assigned!");
        if (pumpPressure1Needle == null) Debug.LogWarning("[SWACOPanelController] Pump Pressure 1 Needle not assigned!");
        if (pumpPressure2Needle == null) Debug.LogWarning("[SWACOPanelController] Pump Pressure 2 Needle not assigned!");
        if (chokeManifoldNeedle == null) Debug.LogWarning("[SWACOPanelController] Choke Manifold Needle not assigned!");
        
        // Log text display assignments (optional components)
        int textAssignments = 0;
        if (totalStrokesText != null || totalStrokes3DText != null) textAssignments++;
        if (totalVolumeText != null || totalVolume3DText != null) textAssignments++;
        if (mp1SpeedText != null || mp1Speed3DText != null) textAssignments++;
        if (mp2SpeedText != null || mp2Speed3DText != null) textAssignments++;
        
        Debug.Log($"[SWACOPanelController] Text displays assigned: {textAssignments} categories");
    }
    
    void InitializeDisplays()
    {
        Debug.Log("[SWACOPanelController] Initializing displays...");
        
        // Initialize lights to OFF
        SetLightState(holdLight, false);
        SetLightState(resetLight, false);
        
        // Initialize needles to zero position if simulation is enabled
        if (enableNeedleSimulation)
        {
            SetNeedleRotation(standpipeNeedle, 0f);
            SetNeedleRotation(casingNeedle, 0f);
            SetNeedleRotation(pumpPressure1Needle, 0f);
            SetNeedleRotation(pumpPressure2Needle, 0f);
            SetNeedleRotation(chokeManifoldNeedle, 0f);
        }
        
        // Initialize text displays
        UpdateTextDisplay(totalStrokesText, totalStrokes3DText, "Total Strokes", 0f, strokesFormat);
        UpdateTextDisplay(totalVolumeText, totalVolume3DText, "Total Volume", 0f, volumeFormat);
        UpdateTextDisplay(mp1SpeedText, mp1Speed3DText, "MP1", 0f, speedFormat);
        UpdateTextDisplay(mp2SpeedText, mp2Speed3DText, "MP2", 0f, speedFormat);
        UpdateTextDisplay(standpipePressureText, standpipePressure3DText, "Standpipe", 0f, pressureFormat);
        UpdateTextDisplay(casingPressureText, casingPressure3DText, "Casing", 0f, pressureFormat);
        UpdateTextDisplay(chokeManifoldPressureText, chokeManifoldPressure3DText, "Choke", 0f, pressureFormat);
    }
    
    void Update()
    {
        // Smooth needle movement
        SmoothNeedleMovement();
    }
    
    void SmoothNeedleMovement()
    {
        // Check if needle simulation is enabled
        if (!enableNeedleSimulation)
        {
            return; // Skip needle movement if disabled
        }
        
        // Smoothly rotate needles to their target positions
        if (standpipeNeedle != null)
        {
            float currentZ = standpipeNeedle.localEulerAngles.z;
            if (currentZ > 180f) currentZ -= 360f; // Convert to -180 to 180 range
            float newZ = Mathf.LerpAngle(currentZ, targetStandpipeRotation, Time.deltaTime * needleRotationSpeed);
            standpipeNeedle.localEulerAngles = new Vector3(0, 0, newZ);
        }
        
        if (casingNeedle != null)
        {
            float currentZ = casingNeedle.localEulerAngles.z;
            if (currentZ > 180f) currentZ -= 360f;
            float newZ = Mathf.LerpAngle(currentZ, targetCasingRotation, Time.deltaTime * needleRotationSpeed);
            casingNeedle.localEulerAngles = new Vector3(0, 0, newZ);
        }
        
        if (pumpPressure1Needle != null)
        {
            float currentZ = pumpPressure1Needle.localEulerAngles.z;
            if (currentZ > 180f) currentZ -= 360f;
            float newZ = Mathf.LerpAngle(currentZ, targetPump1Rotation, Time.deltaTime * needleRotationSpeed);
            pumpPressure1Needle.localEulerAngles = new Vector3(0, 0, newZ);
        }
        
        if (pumpPressure2Needle != null)
        {
            float currentZ = pumpPressure2Needle.localEulerAngles.z;
            if (currentZ > 180f) currentZ -= 360f;
            float newZ = Mathf.LerpAngle(currentZ, targetPump2Rotation, Time.deltaTime * needleRotationSpeed);
            pumpPressure2Needle.localEulerAngles = new Vector3(0, 0, newZ);
        }
        
        if (chokeManifoldNeedle != null)
        {
            float currentZ = chokeManifoldNeedle.localEulerAngles.z;
            if (currentZ > 180f) currentZ -= 360f;
            float newZ = Mathf.LerpAngle(currentZ, targetChokeRotation, Time.deltaTime * needleRotationSpeed);
            chokeManifoldNeedle.localEulerAngles = new Vector3(0, 0, newZ);
        }
    }
    
    // Coroutine to monitor SWACO data updates
    IEnumerator MonitorSWACOData()
    {
        Debug.Log("[SWACOPanelController] Starting SWACO data monitoring...");
        yield return new WaitForSeconds(1f); // Wait for initialization
        
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);
            
            // Check if we have valid data
            if (dataReceiver != null && 
                dataReceiver.currentData != null && 
                dataReceiver.currentData.simulation_data != null)
            {
                UpdateSWACOPanel();
            }
        }
    }
    
    void UpdateSWACOPanel()
    {
        var simData = dataReceiver.currentData.simulation_data;
        
        // Validate SWACO data before proceeding
        if (!IsSWACODataValid(simData))
        {
            return; // Skip update if SWACO data is not available
        }
        
        // Update lights
        UpdateLights(simData);
        
        // Update pressure gauges
        UpdatePressureGauges(simData);
        
        // Update text displays
        UpdateTextDisplays(simData);
    }
    
    void UpdateLights(SimulationData simData)
    {
        // Get light states from SWACO panel data
        bool holdState = simData.swaco_panel.hold_active;
        bool resetState = simData.swaco_panel.reset_active;
        
        // Update lights only if states changed
        if (!cachedLightStates.ContainsKey("hold") || cachedLightStates["hold"] != holdState)
        {
            SetLightState(holdLight, holdState);
            cachedLightStates["hold"] = holdState;
            Debug.Log($"[SWACOPanelController] Hold Light: {holdState}");
        }
        
        if (!cachedLightStates.ContainsKey("reset") || cachedLightStates["reset"] != resetState)
        {
            SetLightState(resetLight, resetState);
            cachedLightStates["reset"] = resetState;
            Debug.Log($"[SWACOPanelController] Reset Light: {resetState}");
        }
    }
    
    void UpdatePressureGauges(SimulationData simData)
    {
        // Check if needle simulation is enabled
        if (!enableNeedleSimulation)
        {
            return; // Skip gauge updates if needle simulation is disabled
        }
        
        // Get pressure values from SWACO panel data
        float standpipePressure = simData.swaco_panel.standpipe_pressure;
        float casingPressure = simData.swaco_panel.casing_pressure;
        float pump1Pressure = simData.swaco_panel.pump_1_pressure;
        float pump2Pressure = simData.swaco_panel.pump_2_pressure;
        float chokePressure = simData.swaco_panel.choke_manifold_pressure;
        
        // Update needle rotations only if values changed significantly
        UpdateNeedleIfChanged("standpipe", standpipePressure, standpipeMaxPressure, ref targetStandpipeRotation);
        UpdateNeedleIfChanged("casing", casingPressure, casingMaxPressure, ref targetCasingRotation);
        UpdateNeedleIfChanged("pump1", pump1Pressure, pumpPressure1MaxPressure, ref targetPump1Rotation);
        UpdateNeedleIfChanged("pump2", pump2Pressure, pumpPressure2MaxPressure, ref targetPump2Rotation);
        UpdateNeedleIfChanged("choke", chokePressure, chokeManifoldMaxPressure, ref targetChokeRotation);
    }
    
    void UpdateNeedleIfChanged(string pressureType, float currentPressure, float maxPressure, ref float targetRotation)
    {
        // Check if pressure changed significantly (> 1 PSI difference)
        if (!cachedPressures.ContainsKey(pressureType) || 
            Mathf.Abs(cachedPressures[pressureType] - currentPressure) > 1f)
        {
            cachedPressures[pressureType] = currentPressure;
            targetRotation = PressureToRotation(currentPressure, maxPressure);
            
            Debug.Log($"[SWACOPanelController] {pressureType} pressure: {currentPressure:F0} PSI -> {targetRotation:F1}°");
        }
    }
    
    float PressureToRotation(float pressure, float maxPressure)
    {
        // Clamp pressure to valid range
        pressure = Mathf.Clamp(pressure, 0f, maxPressure);
        
        // Convert pressure to rotation angle
        float normalizedPressure = pressure / maxPressure;
        return Mathf.Lerp(needleMinRotation, needleMaxRotation, normalizedPressure);
    }
    
    void UpdateTextDisplays(SimulationData simData)
    {
        // Update totalizer values from SWACO panel data
        float totalStrokes = simData.swaco_panel.total_strokes;
        float totalVolume = simData.swaco_panel.total_volume;
        
        UpdateTextIfChanged("totalStrokes", totalStrokes, totalStrokesText, totalStrokes3DText, "Total Strokes", strokesFormat);
        UpdateTextIfChanged("totalVolume", totalVolume, totalVolumeText, totalVolume3DText, "Total Volume", volumeFormat);
        
        // Update pump data (use both SWACO panel data and main simulation data)
        float mp1Speed = simData.swaco_panel.mp1_speed > 0 ? simData.swaco_panel.mp1_speed : simData.pump_1_spm;
        float mp2Speed = simData.swaco_panel.mp2_speed > 0 ? simData.swaco_panel.mp2_speed : simData.pump_2_spm;
        float mp1Pressure = simData.swaco_panel.mp1_pressure;
        float mp2Pressure = simData.swaco_panel.mp2_pressure;
        
        UpdateTextIfChanged("mp1Speed", mp1Speed, mp1SpeedText, mp1Speed3DText, "MP1", speedFormat);
        UpdateTextIfChanged("mp2Speed", mp2Speed, mp2SpeedText, mp2Speed3DText, "MP2", speedFormat);
        UpdateTextIfChanged("mp1Pressure", mp1Pressure, mp1PressureText, mp1Pressure3DText, "MP1", pressureFormat);
        UpdateTextIfChanged("mp2Pressure", mp2Pressure, mp2PressureText, mp2Pressure3DText, "MP2", pressureFormat);
        
        // Update pressure displays from SWACO panel data
        float standpipePressure = simData.swaco_panel.standpipe_pressure;
        float casingPressure = simData.swaco_panel.casing_pressure;
        float chokePressure = simData.swaco_panel.choke_manifold_pressure;
        
        UpdateTextIfChanged("standpipePressure", standpipePressure, standpipePressureText, standpipePressure3DText, "Standpipe", pressureFormat);
        UpdateTextIfChanged("casingPressure", casingPressure, casingPressureText, casingPressure3DText, "Casing", pressureFormat);
        UpdateTextIfChanged("chokePressure", chokePressure, chokeManifoldPressureText, chokeManifoldPressure3DText, "Choke", pressureFormat);
    }
    
    void UpdateTextIfChanged(string valueType, float newValue, TextMeshProUGUI uiText, TextMeshPro text3D, string label, string format)
    {
        // Only update if value changed significantly
        if (!cachedValues.ContainsKey(valueType) || 
            Mathf.Abs(cachedValues[valueType] - newValue) > 0.1f)
        {
            cachedValues[valueType] = newValue;
            UpdateTextDisplay(uiText, text3D, label, newValue, format);
        }
    }
    
    void UpdateTextDisplay(TextMeshProUGUI uiText, TextMeshPro text3D, string label, float value, string format)
    {
        string displayText = string.Format(format, value);
        
        if (includePressureLabels && !string.IsNullOrEmpty(label))
        {
            displayText = $"{label}: {displayText}";
        }
        
        if (uiText != null)
        {
            uiText.text = displayText;
        }
        
        if (text3D != null)
        {
            text3D.text = displayText;
        }
    }
    
    void SetLightState(GameObject light, bool isOn)
    {
        if (light != null)
        {
            light.SetActive(isOn);
        }
    }
    
    void SetNeedleRotation(Transform needle, float rotationDegrees)
    {
        if (needle != null)
        {
            needle.localEulerAngles = new Vector3(0, 0, rotationDegrees);
        }
    }
    
    // Helper method to safely access SWACO data
    bool IsSWACODataValid(SimulationData simData)
    {
        return simData != null && simData.swaco_panel != null;
    }
    
    // Public methods for testing
    public void TestAllNeedles()
    {
        Debug.Log("[SWACOPanelController] Testing all needles...");
        StartCoroutine(TestNeedleSequence());
    }
    
    public void TestLights()
    {
        Debug.Log("[SWACOPanelController] Testing lights...");
        StartCoroutine(TestLightSequence());
    }
    
    IEnumerator TestNeedleSequence()
    {
        Debug.Log("[SWACOPanelController] Testing needle movements...");
        
        // Check if needle simulation is enabled
        if (!enableNeedleSimulation)
        {
            Debug.LogWarning("[SWACOPanelController] Needle simulation is disabled. Enable it in the Inspector to test needles.");
            yield break;
        }
        
        // Test all needles at different pressures
        float[] testPressures = { 0f, 25f, 50f, 75f, 100f };
        
        foreach (float percentage in testPressures)
        {
            targetStandpipeRotation = Mathf.Lerp(needleMinRotation, needleMaxRotation, percentage / 100f);
            targetCasingRotation = Mathf.Lerp(needleMinRotation, needleMaxRotation, percentage / 100f);
            targetPump1Rotation = Mathf.Lerp(needleMinRotation, needleMaxRotation, percentage / 100f);
            targetPump2Rotation = Mathf.Lerp(needleMinRotation, needleMaxRotation, percentage / 100f);
            targetChokeRotation = Mathf.Lerp(needleMinRotation, needleMaxRotation, percentage / 100f);
            
            Debug.Log($"[SWACOPanelController] Setting needles to {percentage}% ({targetStandpipeRotation:F1}°)");
            yield return new WaitForSeconds(2f);
        }
        
        // Return to zero
        targetStandpipeRotation = needleMinRotation;
        targetCasingRotation = needleMinRotation;
        targetPump1Rotation = needleMinRotation;
        targetPump2Rotation = needleMinRotation;
        targetChokeRotation = needleMinRotation;
        
        Debug.Log("[SWACOPanelController] Needle test complete");
    }
    
    IEnumerator TestLightSequence()
    {
        Debug.Log("[SWACOPanelController] Testing light sequence...");
        
        // Test lights on/off
        SetLightState(holdLight, true);
        SetLightState(resetLight, false);
        yield return new WaitForSeconds(1f);
        
        SetLightState(holdLight, false);
        SetLightState(resetLight, true);
        yield return new WaitForSeconds(1f);
        
        SetLightState(holdLight, true);
        SetLightState(resetLight, true);
        yield return new WaitForSeconds(1f);
        
        SetLightState(holdLight, false);
        SetLightState(resetLight, false);
        yield return new WaitForSeconds(1f);
        
        Debug.Log("[SWACOPanelController] Light test complete");
    }
    
    public void DiagnoseSWACOPanel()
    {
        Debug.Log("[SWACOPanelController] === SWACO Panel Diagnostics ===");
        Debug.Log($"Data Receiver: {(dataReceiver != null ? "Connected" : "Missing")}");
        Debug.Log($"Hold Light: {(holdLight != null ? holdLight.name : "Not assigned")}");
        Debug.Log($"Reset Light: {(resetLight != null ? resetLight.name : "Not assigned")}");
        Debug.Log($"Standpipe Needle: {(standpipeNeedle != null ? standpipeNeedle.name : "Not assigned")}");
        Debug.Log($"Casing Needle: {(casingNeedle != null ? casingNeedle.name : "Not assigned")}");
        Debug.Log($"Pump 1 Needle: {(pumpPressure1Needle != null ? pumpPressure1Needle.name : "Not assigned")}");
        Debug.Log($"Pump 2 Needle: {(pumpPressure2Needle != null ? pumpPressure2Needle.name : "Not assigned")}");
        Debug.Log($"Choke Needle: {(chokeManifoldNeedle != null ? chokeManifoldNeedle.name : "Not assigned")}");
        
        if (dataReceiver != null && dataReceiver.currentData != null)
        {
            var simData = dataReceiver.currentData.simulation_data;
            Debug.Log($"Pump 1 SPM: {simData.pump_1_spm}");
            Debug.Log($"Pump 2 SPM: {simData.pump_2_spm}");
            Debug.Log($"Total Flow: {simData.total_flow}");
            
            if (IsSWACODataValid(simData))
            {
                Debug.Log("=== SWACO Panel Data ===");
                Debug.Log($"Hold Active: {simData.swaco_panel.hold_active}");
                Debug.Log($"Reset Active: {simData.swaco_panel.reset_active}");
                Debug.Log($"Standpipe Pressure: {simData.swaco_panel.standpipe_pressure} PSI");
                Debug.Log($"Casing Pressure: {simData.swaco_panel.casing_pressure} PSI");
                Debug.Log($"Total Strokes: {simData.swaco_panel.total_strokes}");
                Debug.Log($"Total Volume: {simData.swaco_panel.total_volume} BBL");
            }
            else
            {
                Debug.LogWarning("SWACO Panel data not available!");
            }
        }
        
        Debug.Log("[SWACOPanelController] === End Diagnostics ===");
    }
}