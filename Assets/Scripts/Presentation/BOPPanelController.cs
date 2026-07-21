using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// BOP Panel Controller - Handles BOP status lights and pressure displays
/// Reads data from SimulationDataReceiver and controls the visual elements
/// </summary>
public class BOPPanelController : MonoBehaviour
{
    [Header("Data Source")]
    [Tooltip("Reference to SimulationDataReceiver to get BOP data")]
    public SimulationDataReceiver dataReceiver;
    
    [Header("Needle Simulation")]
    [Tooltip("Enable or disable needle simulation for pressure gauges")]
    public bool enableNeedleSimulation = true;
    
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
    
    [Header("BOP Pressure Displays")]
    [Tooltip("TextMeshPro for displaying pressures in UI Canvas")]
    public TextMeshProUGUI annularPressureText;
    public TextMeshProUGUI manifoldPressureText;
    public TextMeshProUGUI accumulatorPressureText;
    public TextMeshProUGUI airPressureText;
    
    [Tooltip("TextMeshPro 3D objects for world space display")]
    public TextMeshPro annularPressure3D;
    public TextMeshPro manifoldPressure3D;
    public TextMeshPro accumulatorPressure3D;
    public TextMeshPro airPressure3D;
    
    [Header("BOP Status Text Displays")]
    [Tooltip("Text displays for BOP component states")]
    public TextMeshProUGUI annularStatusText;
    public TextMeshProUGUI pipeRamStatusText;
    public TextMeshProUGUI blindRamStatusText;
    public TextMeshProUGUI shearRamStatusText;
    public TextMeshProUGUI killLineStatusText;
    public TextMeshProUGUI chokeLineStatusText;
    
    [Header("Master Valve")]
    [Tooltip("Master valve status indicators")]
    public GameObject masterValveGreenLight;
    public GameObject masterValveRedLight;
    public TextMeshProUGUI masterValveStatusText;
    
    [Header("Master Valve Rotation")]
    [Tooltip("The master valve GameObject to rotate")]
    public GameObject masterValveObject;
    [Tooltip("Rotation speed in degrees per second")]
    public float valveRotationSpeed = 90f;
    [Tooltip("Axis to rotate around (default is Z axis for wheel)")]
    public Vector3 rotationAxis = new Vector3(0, 0, 1);
    private bool masterValveTargetOpen = false;
    private float currentValveRotation = 0f;
    private float targetValveRotation = 0f;
    
    [Header("Display Settings")]
    [Tooltip("Format for pressure values (e.g., '{0:F0} PSI')")]
    public string pressureFormat = "{0:F0} PSI";
    [Tooltip("Include label in pressure display")]
    public bool includePressureLabel = true;
    [Tooltip("Enable color coding for pressure warnings")]
    public bool enablePressureColorCoding = true;
    
    [Header("Update Settings")]
    [Tooltip("How often to update displays (in seconds)")]
    public float updateInterval = 0.1f;
    
    
    [Header("Debug Settings")]
    [Tooltip("Enable debug logging for troubleshooting")]
    public bool enableDebugLogging = true;  // Enabled to help debug needle issues
    
    [Header("Pressure Gauge Ranges - Matching Python System")]
    [Tooltip("Maximum pressure for annular gauge (PSI) - Python uses 5000")]
    public float annularMaxPressure = 5000f;
    [Tooltip("Maximum pressure for manifold gauge (PSI) - Python uses 6000")]
    public float manifoldMaxPressure = 6000f;
    [Tooltip("Maximum pressure for accumulator gauge (PSI) - Python uses 3000")]
    public float accumulatorMaxPressure = 3000f;
    [Tooltip("Maximum pressure for air gauge (PSI) - Python uses 200")]
    public float airMaxPressure = 200f;
    
    // Pressure thresholds for color coding
    private float annularLowThreshold = 500f;
    private float manifoldLowThreshold = 1500f;
    private float accumulatorLowThreshold = 1200f;
    private float airLowThreshold = 100f;
    
    
    // Update timer
    private float updateTimer = 0f;
    
    // Monitoring coroutine reference
    private Coroutine monitoringCoroutine = null;
    
    // Cache for BOP states to prevent flickering
    private Dictionary<string, string> cachedBopStates = new Dictionary<string, string>();
    private bool masterValveCached = false;
    private float lastUpdateTime = 0f;
    
    void Start()
    {
        Debug.Log("[BOPPanelController] Starting initialization...");
        
        // Auto-find SimulationDataReceiver if not assigned
        if (dataReceiver == null)
        {
            dataReceiver = FindObjectOfType<SimulationDataReceiver>();
            if (dataReceiver == null)
            {
                Debug.LogError("[BOPPanelController] SimulationDataReceiver not found! Please assign it in the Inspector.");
            }
            else
            {
                Debug.Log("[BOPPanelController] Found SimulationDataReceiver automatically");
            }
        }
        
        // Commented out - using Update() instead for better real-time response
        // if (monitoringCoroutine == null)
        // {
        //     monitoringCoroutine = StartCoroutine(MonitorDataUpdates());
        // }
        
        
        // Validate all light assignments
        ValidateLightAssignments();
        
        // DON'T initialize lights to OFF - let them maintain their current state
        // InitializeLights(); // REMOVED - this was causing flickering
    }
    
    void ValidateLightAssignments()
    {
        Debug.Log("[BOPPanelController] Validating light assignments...");
        
        // Check each light and log its status
        int assignedCount = 0;
        int nullCount = 0;
        
        // Check green lights
        if (annularGreenLight != null) { assignedCount++; Debug.Log($"✓ Annular Green Light: {annularGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Annular Green Light is NULL"); }
        
        if (pipeRamGreenLight != null) { assignedCount++; Debug.Log($"✓ Pipe Ram Green Light: {pipeRamGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Pipe Ram Green Light is NULL"); }
        
        if (blindRamGreenLight != null) { assignedCount++; Debug.Log($"✓ Blind Ram Green Light: {blindRamGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Blind Ram Green Light is NULL"); }
        
        if (shearRamGreenLight != null) { assignedCount++; Debug.Log($"✓ Shear Ram Green Light: {shearRamGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Shear Ram Green Light is NULL"); }
        
        if (killLineGreenLight != null) { assignedCount++; Debug.Log($"✓ Kill Line Green Light: {killLineGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Kill Line Green Light is NULL"); }
        
        if (chokeLineGreenLight != null) { assignedCount++; Debug.Log($"✓ Choke Line Green Light: {chokeLineGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Choke Line Green Light is NULL"); }
        
        // Check red lights
        if (annularRedLight != null) { assignedCount++; Debug.Log($"✓ Annular Red Light: {annularRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Annular Red Light is NULL"); }
        
        if (pipeRamRedLight != null) { assignedCount++; Debug.Log($"✓ Pipe Ram Red Light: {pipeRamRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Pipe Ram Red Light is NULL"); }
        
        if (blindRamRedLight != null) { assignedCount++; Debug.Log($"✓ Blind Ram Red Light: {blindRamRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Blind Ram Red Light is NULL"); }
        
        if (shearRamRedLight != null) { assignedCount++; Debug.Log($"✓ Shear Ram Red Light: {shearRamRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Shear Ram Red Light is NULL"); }
        
        if (killLineRedLight != null) { assignedCount++; Debug.Log($"✓ Kill Line Red Light: {killLineRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Kill Line Red Light is NULL"); }
        
        if (chokeLineRedLight != null) { assignedCount++; Debug.Log($"✓ Choke Line Red Light: {chokeLineRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Choke Line Red Light is NULL"); }
        
        // Check master valve lights
        if (masterValveGreenLight != null) { assignedCount++; Debug.Log($"✓ Master Valve Green Light: {masterValveGreenLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Master Valve Green Light is NULL"); }
        
        if (masterValveRedLight != null) { assignedCount++; Debug.Log($"✓ Master Valve Red Light: {masterValveRedLight.name}"); }
        else { nullCount++; Debug.LogWarning("✗ Master Valve Red Light is NULL"); }
        
        Debug.Log($"[BOPPanelController] Light Assignment Summary: {assignedCount} assigned, {nullCount} null");
        
        if (nullCount > 0)
        {
            Debug.LogWarning($"[BOPPanelController] {nullCount} lights are not assigned! They will be skipped.");
        }
    }
    
    void InitializeLights()
    {
        // Turn off all lights initially
        SetLightState(annularGreenLight, false);
        SetLightState(annularRedLight, false);
        SetLightState(pipeRamGreenLight, false);
        SetLightState(pipeRamRedLight, false);
        SetLightState(blindRamGreenLight, false);
        SetLightState(blindRamRedLight, false);
        SetLightState(shearRamGreenLight, false);
        SetLightState(shearRamRedLight, false);
        SetLightState(killLineGreenLight, false);
        SetLightState(killLineRedLight, false);
        SetLightState(chokeLineGreenLight, false);
        SetLightState(chokeLineRedLight, false);
        SetLightState(masterValveGreenLight, false);
        SetLightState(masterValveRedLight, false);
    }
    
    void Update()
    {
        // Update every frame for smooth needle movement
        if (dataReceiver != null && dataReceiver.currentData != null && 
            dataReceiver.currentData.simulation_data != null)
        {
            // Update pressure displays and needles every frame for smooth movement
            UpdateBOPPressureDisplays();
            UpdateMasterValveStatus();
            
            // Update BOP lights less frequently to prevent flickering
            if (Time.frameCount % 30 == 0) // Every 30 frames (~0.5 seconds at 60fps)
            {
                UpdateBOPStatusLights();
            }
        }
    }
    
    // Public method to force immediate update for testing
    public void ForceUpdate()
    {
        Debug.Log("[BOPPanelController] Force update called!");
        
        if (dataReceiver == null)
        {
            Debug.LogError("[BOPPanelController] dataReceiver is null!");
            return;
        }
        
        if (dataReceiver.currentData == null)
        {
            Debug.LogError("[BOPPanelController] currentData is null!");
            return;
        }
        
        if (dataReceiver.currentData.simulation_data == null)
        {
            Debug.LogError("[BOPPanelController] simulation_data is null!");
            return;
        }
        
        Debug.Log("[BOPPanelController] Data is valid, forcing update...");
        UpdateBOPStatusLights();
        UpdateBOPPressureDisplays();
        UpdateMasterValveStatus();
    }
    
    // Needle rotation test methods removed - rotation handled elsewhere
    
    void UpdateBOPStatusLights()
    {
        // Try to get BOP states with better error handling
        Dictionary<string, string> bopStates = null;
        
        try
        {
            if (dataReceiver != null && 
                dataReceiver.currentData != null && 
                dataReceiver.currentData.simulation_data != null &&
                dataReceiver.currentData.simulation_data.bop_states != null)
            {
                bopStates = dataReceiver.currentData.simulation_data.bop_states;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[BOPPanelController] Error accessing bop_states: {e.Message}");
            return;
        }
        
        if (bopStates == null)
        {
            // Create empty dictionary to prevent null reference
            bopStates = new Dictionary<string, string>();
            Debug.LogWarning("[BOPPanelController] bop_states is null! Using empty dictionary.");
        }
        
        // Debug: Log all available BOP states (only when debugging is needed)
        if (Time.frameCount % 600 == 0) // Log every 10 seconds to reduce spam
        {
            Debug.Log($"[BOPPanelController] BOP States: {bopStates.Count} components");
        }
        
        // Update each BOP component's dual lights using EXACT database names
        // Database names: 'Annular-1', 'Pipe Ram-1', 'Pipe Ram-2', 'Pipe Ram-3', 'Blind/Shear Ram', 'Kill Line', 'Choke Line'
        
        // Log what we're looking for vs what we have
        if (Time.frameCount % 300 == 0) // Every 5 seconds
        {
            Debug.Log("[BOPPanelController] Looking for components in received states:");
            Debug.Log("  Need: Annular-1, checking if exists: " + bopStates.ContainsKey("Annular-1"));
            Debug.Log("  Need: Pipe Ram-1/2/3, checking: " + 
                $"1={bopStates.ContainsKey("Pipe Ram-1")}, " +
                $"2={bopStates.ContainsKey("Pipe Ram-2")}, " +
                $"3={bopStates.ContainsKey("Pipe Ram-3")}");
            Debug.Log("  Need: Blind/Shear Ram, checking: " + bopStates.ContainsKey("Blind/Shear Ram"));
            Debug.Log("  Need: Kill Line, checking: " + bopStates.ContainsKey("Kill Line"));
            Debug.Log("  Need: Choke Line, checking: " + bopStates.ContainsKey("Choke Line"));
        }
        
        // Use EXACT database names for direct lookup
        
        // Annular-1 (exact match)
        if (bopStates.ContainsKey("Annular-1"))
        {
            UpdateDirectBOPLight("Annular-1", bopStates["Annular-1"], 
                annularGreenLight, annularRedLight, annularStatusText);
        }
        
        // Pipe Ram - combine states from Pipe Ram-1, Pipe Ram-2, Pipe Ram-3
        List<string> pipeRamStates = new List<string>();
        if (bopStates.ContainsKey("Pipe Ram-1")) pipeRamStates.Add(bopStates["Pipe Ram-1"]);
        if (bopStates.ContainsKey("Pipe Ram-2")) pipeRamStates.Add(bopStates["Pipe Ram-2"]);
        if (bopStates.ContainsKey("Pipe Ram-3")) pipeRamStates.Add(bopStates["Pipe Ram-3"]);
        
        if (pipeRamStates.Count > 0)
        {
            // Combine states: closed if any closed, moving if any moving, open if all open
            string combinedState = "open";
            if (pipeRamStates.Any(s => s.ToLower().Contains("closed")))
                combinedState = "closed";
            else if (pipeRamStates.Any(s => s.ToLower().Contains("moving")))
                combinedState = "moving";
            
            UpdateDirectBOPLight("Pipe Ram (Combined)", combinedState, 
                pipeRamGreenLight, pipeRamRedLight, pipeRamStatusText);
        }
        
        // Blind/Shear Ram (exact match)
        if (bopStates.ContainsKey("Blind/Shear Ram"))
        {
            UpdateDirectBOPLight("Blind/Shear Ram", bopStates["Blind/Shear Ram"], 
                blindRamGreenLight, blindRamRedLight, blindRamStatusText);
        }
        
        // For separate Shear Ram light (use Blind/Shear Ram state)
        if (bopStates.ContainsKey("Blind/Shear Ram"))
        {
            UpdateDirectBOPLight("Shear Ram", bopStates["Blind/Shear Ram"], 
                shearRamGreenLight, shearRamRedLight, shearRamStatusText);
        }
        
        // Kill Line (exact match)
        if (bopStates.ContainsKey("Kill Line"))
        {
            UpdateDirectBOPLight("Kill Line", bopStates["Kill Line"], 
                killLineGreenLight, killLineRedLight, killLineStatusText);
        }
        
        // Choke Line (exact match)
        if (bopStates.ContainsKey("Choke Line"))
        {
            UpdateDirectBOPLight("Choke Line", bopStates["Choke Line"], 
                chokeLineGreenLight, chokeLineRedLight, chokeLineStatusText);
        }
    }
    
    // Direct update method for exact component names
    void UpdateDirectBOPLight(string componentName, string state, 
        GameObject greenLight, GameObject redLight, TextMeshProUGUI statusText)
    {
        // Determine light states based on BOP state
        bool greenOn = false;
        bool redOn = false;
        string displayState = "UNKNOWN";
        
        if (!string.IsNullOrEmpty(state))
        {
            string stateLower = state.ToLower();
            
            if (stateLower.Contains("open"))
            {
                greenOn = true;
                redOn = false;
                displayState = "OPEN";
            }
            else if (stateLower.Contains("closed"))
            {
                greenOn = false;
                redOn = true;
                displayState = "CLOSED";
            }
            else if (stateLower.Contains("moving") || stateLower.Contains("transitioning"))
            {
                greenOn = true;
                redOn = true;
                displayState = "MOVING";
            }
            
            // Only log errors for missing lights
            if (greenLight == null && Time.frameCount % 600 == 0)
                Debug.LogError($"[BOPPanelController] Green light is NULL for {componentName}!");
            if (redLight == null && Time.frameCount % 600 == 0)
                Debug.LogError($"[BOPPanelController] Red light is NULL for {componentName}!");
        }
        
        // Set light states - Force update regardless of current state
        // This ensures lights update even if Unity thinks they're already in correct state
        if (greenLight != null)
        {
            // Always update to ensure consistency
            SetLightState(greenLight, greenOn);
            
            // Double-check it worked
            if (greenOn && !greenLight.activeSelf)
            {
                Debug.LogWarning($"[BOPPanelController] {componentName} green light failed to activate!");
                greenLight.SetActive(true);
            }
        }
        else if (greenOn)
        {
            Debug.LogError($"[BOPPanelController] {componentName} green light is NULL but should be ON!");
        }
        
        if (redLight != null)
        {
            // Always update to ensure consistency
            SetLightState(redLight, redOn);
            
            // Double-check it worked
            if (redOn && !redLight.activeSelf)
            {
                Debug.LogWarning($"[BOPPanelController] {componentName} red light failed to activate!");
                redLight.SetActive(true);
            }
        }
        else if (redOn)
        {
            Debug.LogError($"[BOPPanelController] {componentName} red light is NULL but should be ON!");
        }
        
        // Update status text
        if (statusText != null)
        {
            statusText.text = $"{componentName}: {displayState}";
            if (greenOn && !redOn)
                statusText.color = Color.green;
            else if (!greenOn && redOn)
                statusText.color = Color.red;
            else if (greenOn && redOn)
                statusText.color = Color.yellow;
            else
                statusText.color = Color.gray;
        }
    }
    
    void UpdateDualBOPLightsWithAlternatives(string componentName, string[] nameVariations, 
        Dictionary<string, string> bopStates, GameObject greenLight, GameObject redLight, TextMeshProUGUI statusText)
    {
        // Try to find the component with any of the name variations
        string foundKey = null;
        foreach (string variation in nameVariations)
        {
            if (bopStates.ContainsKey(variation))
            {
                foundKey = variation;
                break;
            }
        }
        
        if (foundKey != null)
        {
            // Use the found key to get the state
            var modifiedStates = new Dictionary<string, string>(bopStates);
            if (!bopStates.ContainsKey(componentName))
            {
                modifiedStates[componentName] = bopStates[foundKey];
            }
            UpdateDualBOPLights(componentName, modifiedStates, greenLight, redLight, statusText);
        }
        else
        {
            // Still not found, use original method
            UpdateDualBOPLights(componentName, bopStates, greenLight, redLight, statusText);
        }
    }
    
    // Method to combine multiple component states (e.g., Pipe Ram-1, Pipe Ram-2, Pipe Ram-3)
    void UpdateCombinedBOPLights(string componentName, string[] nameVariations, 
        Dictionary<string, string> bopStates, GameObject greenLight, GameObject redLight, TextMeshProUGUI statusText)
    {
        // Collect all matching states
        List<string> foundStates = new List<string>();
        
        foreach (string variation in nameVariations)
        {
            if (bopStates.ContainsKey(variation))
            {
                foundStates.Add(bopStates[variation]);
            }
        }
        
        // Determine combined state
        string combinedState = "unknown";
        
        if (foundStates.Count > 0)
        {
            // If any are closed, show closed
            if (foundStates.Any(s => s.ToLower().Contains("closed")))
            {
                combinedState = "closed";
            }
            // If any are moving, show moving
            else if (foundStates.Any(s => s.ToLower().Contains("moving")))
            {
                combinedState = "moving";
            }
            // If all are open, show open
            else if (foundStates.All(s => s.ToLower().Contains("open")))
            {
                combinedState = "open";
            }
            
            // Create temporary state dictionary
            var tempStates = new Dictionary<string, string>();
            tempStates[componentName] = combinedState;
            
            UpdateDualBOPLights(componentName, tempStates, greenLight, redLight, statusText);
        }
        else
        {
            // No states found, use default
            UpdateDualBOPLights(componentName, bopStates, greenLight, redLight, statusText);
        }
    }
    
    void UpdateDualBOPLights(string componentName, Dictionary<string, string> bopStates, 
        GameObject greenLight, GameObject redLight, TextMeshProUGUI statusText)
    {
        // Debug: Check if lights are assigned
        if (greenLight == null && redLight == null)
        {
            // Only log once to avoid spam
            if (Time.frameCount % 300 == 0)
            {
                Debug.LogWarning($"[BOPPanelController] Both lights are null for {componentName}!");
            }
            return;
        }
        
        // Try to find the component state
        string state = "";
        if (bopStates.ContainsKey(componentName))
        {
            state = bopStates[componentName];
        }
        else
        {
            // Component not found in states - try alternative names with better matching
            // First try exact match with different cases
            foreach (var kvp in bopStates)
            {
                if (string.Equals(kvp.Key, componentName, System.StringComparison.OrdinalIgnoreCase))
                {
                    state = kvp.Value;
                    if (Time.frameCount % 300 == 0)
                        Debug.Log($"[BOPPanelController] Found case-insensitive match: '{kvp.Key}' for '{componentName}'");
                    break;
                }
            }
            
            // If still not found, try partial matches
            if (string.IsNullOrEmpty(state))
            {
                string componentLower = componentName.ToLower().Replace(" ", "").Replace("_", "");
                foreach (var kvp in bopStates)
                {
                    string keyLower = kvp.Key.ToLower().Replace(" ", "").Replace("_", "");
                    if (keyLower.Contains(componentLower) || componentLower.Contains(keyLower))
                    {
                        state = kvp.Value;
                        if (Time.frameCount % 300 == 0)
                            Debug.Log($"[BOPPanelController] Found partial match: '{kvp.Key}' for '{componentName}'");
                        break;
                    }
                }
            }
        }
        
        // Default: both lights off
        bool greenOn = false;
        bool redOn = false;
        string displayState = "UNKNOWN";
        
        if (!string.IsNullOrEmpty(state))
        {
            string stateLower = state.ToLower();
            
            if (stateLower.Contains("open"))
            {
                // OPEN: Green light ON, Red light OFF
                greenOn = true;
                redOn = false;
                displayState = "OPEN";
            }
            else if (stateLower.Contains("closed"))
            {
                // CLOSED: Green light OFF, Red light ON
                greenOn = false;
                redOn = true;
                displayState = "CLOSED";
            }
            else if (stateLower.Contains("moving") || stateLower.Contains("transitioning"))
            {
                // MOVING: Both lights ON
                greenOn = true;
                redOn = true;
                displayState = "MOVING";
            }
            
            // Debug log state changes (only log meaningful changes)
            if (Time.frameCount % 60 == 0 || displayState != "UNKNOWN")
            {
                Debug.Log($"[BOPPanelController] {componentName}: state='{state}' -> Green={greenOn}, Red={redOn}");
            }
        }
        else
        {
            if (Time.frameCount % 300 == 0)
            {
                Debug.LogWarning($"[BOPPanelController] No state found for {componentName}");
            }
        }
        
        // Control green light
        SetLightState(greenLight, greenOn);
        
        // Control red light
        SetLightState(redLight, redOn);
        
        // Update status text if provided
        if (statusText != null)
        {
            statusText.text = $"{componentName}: {displayState}";
            
            // Set text color based on state
            if (greenOn && !redOn)
                statusText.color = Color.green;
            else if (!greenOn && redOn)
                statusText.color = Color.red;
            else if (greenOn && redOn)
                statusText.color = Color.yellow;
            else
                statusText.color = Color.gray;
        }
    }
    
    void UpdateMasterValveStatus()
    {
        bool masterValveOpen = dataReceiver.currentData.simulation_data.master_valve_open;
        
        // Control master valve lights with null checks
        if (masterValveGreenLight != null)
        {
            SetLightState(masterValveGreenLight, masterValveOpen);
        }
        
        if (masterValveRedLight != null)
        {
            SetLightState(masterValveRedLight, !masterValveOpen);
        }
        
        // Update master valve text
        if (masterValveStatusText != null)
        {
            masterValveStatusText.text = masterValveOpen ? "MASTER VALVE: OPEN" : "MASTER VALVE: CLOSED";
            masterValveStatusText.color = masterValveOpen ? Color.green : Color.red;
        }
        
        // Rotate master valve object
        if (masterValveObject != null)
        {
            // Set target rotation based on state
            // Open = 360 degrees (full turn), Closed = 0 degrees
            masterValveTargetOpen = masterValveOpen;
            targetValveRotation = masterValveOpen ? 360f : 0f;
            
            // Smoothly rotate to target
            currentValveRotation = Mathf.Lerp(currentValveRotation, targetValveRotation, Time.deltaTime * 2f);
            
            // Apply rotation
            masterValveObject.transform.localRotation = Quaternion.Euler(rotationAxis * currentValveRotation);
        }
    }
    
    void UpdateBOPPressureDisplays()
    {
        if (dataReceiver.currentData.simulation_data.bop_pressures == null)
        {
            if (enableDebugLogging && Time.frameCount % 60 == 0)
                Debug.LogWarning("[BOPPanelController] bop_pressures is null!");
            return;
        }
        
        var pressures = dataReceiver.currentData.simulation_data.bop_pressures;
        
        // Log pressure values periodically
        if (enableDebugLogging && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[BOPPanelController] Pressures - Annular: {pressures.annular:F1}, Manifold: {pressures.manifold:F1}, Accumulator: {pressures.accumulator:F1}, Air: {pressures.air:F1}");
        }
        
        // Update Annular Pressure
        UpdatePressureDisplay(annularPressureText, annularPressure3D, "Annular", 
            pressures.annular, annularLowThreshold);
        
        // Update Manifold Pressure
        UpdatePressureDisplay(manifoldPressureText, manifoldPressure3D, "Manifold", 
            pressures.manifold, manifoldLowThreshold);
        
        // Update Accumulator Pressure
        UpdatePressureDisplay(accumulatorPressureText, accumulatorPressure3D, "Accumulator", 
            pressures.accumulator, accumulatorLowThreshold);
        
        // Update Air Pressure
        UpdatePressureDisplay(airPressureText, airPressure3D, "Air", 
            pressures.air, airLowThreshold);
        
    }
    
    // UpdatePressureNeedle overload removed - needle rotation handled elsewhere
    
    // All needle rotation methods removed - rotation handled elsewhere
    
    void UpdatePressureDisplay(TextMeshProUGUI uiText, TextMeshPro text3D, 
        string label, float value, float lowThreshold)
    {
        string text = FormatPressureText(label, value);
        Color color = GetPressureColor(value, lowThreshold);
        
        if (uiText != null)
        {
            uiText.text = text;
            if (enablePressureColorCoding)
            {
                uiText.color = color;
            }
        }
        
        if (text3D != null)
        {
            text3D.text = text;
            if (enablePressureColorCoding)
            {
                text3D.color = color;
            }
        }
    }
    
    string FormatPressureText(string label, float value)
    {
        string formattedValue = string.Format(pressureFormat, value);
        
        if (includePressureLabel)
        {
            return $"{label}: {formattedValue}";
        }
        else
        {
            return formattedValue;
        }
    }
    
    Color GetPressureColor(float value, float lowThreshold)
    {
        if (value < lowThreshold)
        {
            return Color.yellow; // Warning color for low pressure
        }
        else
        {
            return Color.white; // Normal color
        }
    }
    
    void SetLightState(GameObject lightObject, bool isOn)
    {
        if (lightObject == null) 
        {
            return; // Silently skip null objects
        }
        
        // Force the state change using multiple approaches
        try
        {
            // Method 1: Direct GameObject activation (MOST RELIABLE)
            lightObject.SetActive(isOn);
            
            // Method 2: If it's a UI Button, change its interactable state
            var button = lightObject.GetComponent<UnityEngine.UI.Button>();
            if (button != null)
            {
                // For buttons, we might want to change color instead
                var colors = button.colors;
                if (isOn)
                {
                    // Button is "lit up" - make it brighter or change color
                    if (lightObject.name.ToLower().Contains("green"))
                    {
                        colors.normalColor = Color.green;
                        colors.highlightedColor = Color.green * 1.2f;
                    }
                    else if (lightObject.name.ToLower().Contains("red"))
                    {
                        colors.normalColor = Color.red;
                        colors.highlightedColor = Color.red * 1.2f;
                    }
                }
                else
                {
                    // Button is "off" - make it darker
                    colors.normalColor = Color.gray * 0.5f;
                    colors.highlightedColor = Color.gray * 0.6f;
                }
                button.colors = colors;
                
                // Also try to change the button's image color directly
                var buttonImage = button.GetComponent<UnityEngine.UI.Image>();
                if (buttonImage != null)
                {
                    if (isOn)
                    {
                        if (lightObject.name.ToLower().Contains("green"))
                            buttonImage.color = Color.green;
                        else if (lightObject.name.ToLower().Contains("red"))
                            buttonImage.color = Color.red;
                    }
                    else
                    {
                        buttonImage.color = Color.gray * 0.3f;
                    }
                }
            }
            
            // Method 3: Control Light component - CRITICAL for actual light emission
            Light[] lights = lightObject.GetComponentsInChildren<Light>(true);
            foreach (var light in lights)
            {
                light.enabled = isOn;
                if (isOn)
                {
                    // Set light intensity to a softer level
                    light.intensity = 1.5f; // Reduced intensity for softer glow
                    light.range = 5.0f; // Reduced range for more localized effect
                    
                    // Set light color based on name
                    if (lightObject.name.ToLower().Contains("green"))
                    {
                        light.color = Color.green;
                        Debug.Log($"[BOPPanelController] Green light enabled with intensity {light.intensity}");
                    }
                    else if (lightObject.name.ToLower().Contains("red"))
                    {
                        light.color = Color.red;
                        Debug.Log($"[BOPPanelController] Red light enabled with intensity {light.intensity}");
                    }
                }
            }
            
            // Also check for a Light component directly on the object
            Light directLight = lightObject.GetComponent<Light>();
            if (directLight != null)
            {
                directLight.enabled = isOn;
                if (isOn)
                {
                    directLight.intensity = 1.5f;
                    directLight.range = 5.0f;
                    if (lightObject.name.ToLower().Contains("green"))
                        directLight.color = Color.green;
                    else if (lightObject.name.ToLower().Contains("red"))
                        directLight.color = Color.red;
                }
            }
            
            // Method 4: Control Renderer for 3D objects
            Renderer[] renderers = lightObject.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer.material != null)
                {
                    if (isOn)
                    {
                        // Make it glow
                        renderer.material.EnableKeyword("_EMISSION");
                        
                        if (lightObject.name.ToLower().Contains("green"))
                        {
                            renderer.material.color = Color.green;
                            renderer.material.SetColor("_EmissionColor", Color.green * 0.8f);
                        }
                        else if (lightObject.name.ToLower().Contains("red"))
                        {
                            renderer.material.color = Color.red;
                            renderer.material.SetColor("_EmissionColor", Color.red * 0.8f);
                        }
                    }
                    else
                    {
                        // Turn off glow
                        renderer.material.color = Color.gray * 0.3f;
                        renderer.material.SetColor("_EmissionColor", Color.black);
                    }
                }
            }
            
            // Method 5: Control UI Image
            UnityEngine.UI.Image[] images = lightObject.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var image in images)
            {
                if (isOn)
                {
                    image.enabled = true;
                    if (lightObject.name.ToLower().Contains("green"))
                        image.color = Color.green;
                    else if (lightObject.name.ToLower().Contains("red"))
                        image.color = Color.red;
                }
                else
                {
                    image.color = Color.gray * 0.3f;
                }
            }
            
            // Log successful state change
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[BOPPanelController] {lightObject.name} is now {(isOn ? "ON" : "OFF")} (Active: {lightObject.activeSelf})");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BOPPanelController] Error setting light state for {lightObject.name}: {e.Message}");
        }
    }
    
    // Public methods for testing and diagnostics
    public void DiagnoseLights()
    {
        Debug.Log("[BOPPanelController] ===== LIGHT DIAGNOSTICS =====");
        
        // Check data receiver
        if (dataReceiver == null)
        {
            Debug.LogError("DataReceiver is NULL!");
            return;
        }
        else
        {
            Debug.Log("DataReceiver is connected");
        }
        
        // Check each light assignment
        Debug.Log("Checking light assignments:");
        CheckLightAssignment("Annular Green", annularGreenLight);
        CheckLightAssignment("Annular Red", annularRedLight);
        CheckLightAssignment("Pipe Ram Green", pipeRamGreenLight);
        CheckLightAssignment("Pipe Ram Red", pipeRamRedLight);
        CheckLightAssignment("Blind Ram Green", blindRamGreenLight);
        CheckLightAssignment("Blind Ram Red", blindRamRedLight);
        CheckLightAssignment("Shear Ram Green", shearRamGreenLight);
        CheckLightAssignment("Shear Ram Red", shearRamRedLight);
        CheckLightAssignment("Kill Line Green", killLineGreenLight);
        CheckLightAssignment("Kill Line Red", killLineRedLight);
        CheckLightAssignment("Choke Line Green", chokeLineGreenLight);
        CheckLightAssignment("Choke Line Red", chokeLineRedLight);
        
        // Check BOP states
        if (dataReceiver.currentData != null && 
            dataReceiver.currentData.simulation_data != null &&
            dataReceiver.currentData.simulation_data.bop_states != null)
        {
            var states = dataReceiver.currentData.simulation_data.bop_states;
            Debug.Log($"BOP States available: {states.Count}");
            foreach (var kvp in states)
            {
                Debug.Log($"  {kvp.Key} = {kvp.Value}");
            }
        }
        else
        {
            Debug.LogWarning("No BOP states available!");
        }
        
        Debug.Log("[BOPPanelController] ===== END DIAGNOSTICS =====");
    }
    
    void CheckLightAssignment(string name, GameObject light)
    {
        if (light == null)
        {
            Debug.LogWarning($"  ✗ {name} is NOT assigned!");
        }
        else
        {
            Debug.Log($"  ✓ {name} is assigned to: {light.name}");
            
            // Check what components it has
            if (light.GetComponent<Light>() != null)
                Debug.Log($"    - Has Light component");
            if (light.GetComponent<Renderer>() != null)
                Debug.Log($"    - Has Renderer component");
            if (light.GetComponent<UnityEngine.UI.Image>() != null)
                Debug.Log($"    - Has UI Image component");
            
            // Check if it's currently active
            Debug.Log($"    - GameObject active: {light.activeSelf}");
            Debug.Log($"    - GameObject activeInHierarchy: {light.activeInHierarchy}");
        }
    }
    
    public void TestAllLights()
    {
        StartCoroutine(TestLightSequence());
    }
    
    // Test method to manually set BOP states
    public void SetTestState(string componentName, string state)
    {
        Debug.Log($"[BOPPanelController] Setting test state: {componentName} = {state}");
        
        // Create a temporary dictionary with the test state
        var testStates = new Dictionary<string, string>();
        testStates[componentName] = state;
        
        // Update the specific component
        if (componentName.ToLower().Contains("annular"))
        {
            UpdateDualBOPLights("Annular", testStates, annularGreenLight, annularRedLight, annularStatusText);
        }
        else if (componentName.ToLower().Contains("pipe"))
        {
            UpdateDualBOPLights("Pipe Ram", testStates, pipeRamGreenLight, pipeRamRedLight, pipeRamStatusText);
        }
        else if (componentName.ToLower().Contains("blind"))
        {
            UpdateDualBOPLights("Blind Ram", testStates, blindRamGreenLight, blindRamRedLight, blindRamStatusText);
        }
        else if (componentName.ToLower().Contains("master"))
        {
            // For master valve, directly control the lights
            bool isOpen = state.ToLower().Contains("open");
            SetLightState(masterValveGreenLight, isOpen);
            SetLightState(masterValveRedLight, !isOpen);
            if (masterValveStatusText != null)
            {
                masterValveStatusText.text = isOpen ? "MASTER VALVE: OPEN" : "MASTER VALVE: CLOSED";
                masterValveStatusText.color = isOpen ? Color.green : Color.red;
            }
        }
    }
    
    // Test all states for a component
    public void TestComponentStates(string componentName)
    {
        StartCoroutine(TestComponentStatesSequence(componentName));
    }
    
    IEnumerator TestComponentStatesSequence(string componentName)
    {
        Debug.Log($"[BOPPanelController] Testing states for {componentName}");
        
        SetTestState(componentName, "open");
        yield return new WaitForSeconds(1f);
        
        SetTestState(componentName, "closed");
        yield return new WaitForSeconds(1f);
        
        SetTestState(componentName, "moving");
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[BOPPanelController] Test complete for {componentName}");
    }
    
    // Coroutine to continuously monitor for data updates
    IEnumerator MonitorDataUpdates()
    {
        Debug.Log("[BOPPanelController] Starting data monitoring coroutine");
        float lastLogTime = 0;
        int updateCount = 0;
        bool firstUpdate = true;
        
        // Wait longer for everything to initialize properly
        yield return new WaitForSeconds(2f);
        
        while (true)
        {
            yield return new WaitForSeconds(1f); // Check every 1 second to prevent flickering
            
            // Check if we have data
            bool hasData = false;
            bool hasBopStates = false;
            int stateCount = 0;
            bool statesChanged = false;
            
            try
            {
                hasData = dataReceiver != null && 
                         dataReceiver.currentData != null && 
                         dataReceiver.currentData.simulation_data != null;
                         
                if (hasData)
                {
                    hasBopStates = dataReceiver.currentData.simulation_data.bop_states != null;
                    if (hasBopStates)
                    {
                        var currentStates = dataReceiver.currentData.simulation_data.bop_states;
                        stateCount = currentStates.Count;
                        
                        // Skip if we get empty data (could be initialization)
                        if (stateCount == 0 && cachedBopStates.Count > 0)
                        {
                            Debug.LogWarning("[BOPPanelController] Received empty BOP states, keeping cached values");
                            continue;
                        }
                        
                        // Check if states have actually changed
                        statesChanged = HasStatesChanged(currentStates);
                        
                        // On first update, always apply the states
                        if (firstUpdate && stateCount > 0)
                        {
                            statesChanged = true;
                            firstUpdate = false;
                            Debug.Log($"[BOPPanelController] First update with {stateCount} states");
                        }
                        
                        // Only update if states changed
                        if (statesChanged)
                        {
                            // Update cached states WITHOUT clearing first
                            // This prevents flickering from temporary empty states
                            foreach (var kvp in currentStates)
                            {
                                cachedBopStates[kvp.Key] = kvp.Value;
                            }
                            
                            // We have BOP states, update the lights
                            UpdateBOPStatusLights();
                            UpdateBOPPressureDisplays();
                            UpdateMasterValveStatus();
                            updateCount++;
                            lastUpdateTime = Time.time;
                            
                            // Log update
                            if (updateCount % 10 == 0)
                            {
                                Debug.Log($"[BOPPanelController] Updated {updateCount} times, {stateCount} states active");
                            }
                        }
                        // REMOVED the 5-second forced update - this was causing flickering
                        // The state change detection is sufficient
                        
                        // Log successful updates periodically
                        if (updateCount % 10 == 0) // Every 5 seconds at 500ms interval
                        {
                            Debug.Log($"[BOPPanelController] Updated {updateCount} times, {stateCount} BOP states active");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (Time.time - lastLogTime > 5f) // Log errors every 5 seconds max
                {
                    Debug.LogError($"[BOPPanelController] Error in MonitorDataUpdates: {e.Message}");
                    lastLogTime = Time.time;
                }
            }
            
            // Log status periodically
            if (Time.time - lastLogTime > 10f)
            {
                if (!hasData)
                {
                    Debug.LogWarning($"[BOPPanelController] No data: receiver={dataReceiver != null}, " +
                        $"currentData={dataReceiver?.currentData != null}, " +
                        $"simData={dataReceiver?.currentData?.simulation_data != null}");
                }
                else if (!hasBopStates)
                {
                    Debug.LogWarning("[BOPPanelController] Have data but no BOP states!");
                }
                else
                {
                    Debug.Log($"[BOPPanelController] Monitoring active: {stateCount} BOP states, {updateCount} total updates");
                    // Log all current states
                    foreach (var kvp in cachedBopStates)
                    {
                        Debug.Log($"  State: {kvp.Key} = {kvp.Value}");
                    }
                }
                lastLogTime = Time.time;
            }
        }
    }
    
    // Check if BOP states have changed
    bool HasStatesChanged(Dictionary<string, string> newStates)
    {
        if (cachedBopStates.Count != newStates.Count)
            return true;
            
        foreach (var kvp in newStates)
        {
            if (!cachedBopStates.ContainsKey(kvp.Key) || cachedBopStates[kvp.Key] != kvp.Value)
                return true;
        }
        
        return false;
    }
    
    IEnumerator TestLightSequence()
    {
        Debug.Log("[BOPPanelController] Testing all lights...");
        
        // Test each component: Open -> Closed -> Moving
        yield return TestComponentLights("Annular", annularGreenLight, annularRedLight);
        yield return TestComponentLights("Pipe Ram", pipeRamGreenLight, pipeRamRedLight);
        yield return TestComponentLights("Blind Ram", blindRamGreenLight, blindRamRedLight);
        yield return TestComponentLights("Shear Ram", shearRamGreenLight, shearRamRedLight);
        yield return TestComponentLights("Kill Line", killLineGreenLight, killLineRedLight);
        yield return TestComponentLights("Choke Line", chokeLineGreenLight, chokeLineRedLight);
        
        Debug.Log("[BOPPanelController] Light test complete!");
    }
    
    IEnumerator TestComponentLights(string name, GameObject green, GameObject red)
    {
        Debug.Log($"Testing {name}...");
        
        // Open state
        SetLightState(green, true);
        SetLightState(red, false);
        yield return new WaitForSeconds(0.5f);
        
        // Closed state
        SetLightState(green, false);
        SetLightState(red, true);
        yield return new WaitForSeconds(0.5f);
        
        // Moving state
        SetLightState(green, true);
        SetLightState(red, true);
        yield return new WaitForSeconds(0.5f);
        
        // Off
        SetLightState(green, false);
        SetLightState(red, false);
        yield return new WaitForSeconds(0.2f);
    }
}