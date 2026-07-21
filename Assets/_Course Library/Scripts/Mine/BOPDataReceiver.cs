using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Receives and visualizes BOP (Blowout Preventer) data from Python simulator
/// </summary>
public class BOPDataReceiver : MonoBehaviour
{
    [Header("BOP Visual Components")]
    [Tooltip("Annular BOP GameObject")]
    public GameObject annularBOP;
    
    [Tooltip("Pipe Ram BOP GameObject")]
    public GameObject pipeRamBOP;
    
    [Tooltip("Blind Ram BOP GameObject")]
    public GameObject blindRamBOP;
    
    [Tooltip("Shear Ram BOP GameObject")]
    public GameObject shearRamBOP;
    
    [Header("BOP Indicators")]
    [Tooltip("Material for open state (green)")]
    public Material openMaterial;
    
    [Tooltip("Material for closed state (red)")]
    public Material closedMaterial;
    
    [Tooltip("Material for moving state (yellow)")]
    public Material movingMaterial;
    
    [Header("Pressure Displays")]
    public TextMeshProUGUI annularPressureText;
    public TextMeshProUGUI manifoldPressureText;
    public TextMeshProUGUI accumulatorPressureText;
    public TextMeshProUGUI airPressureText;
    
    [Header("Control Indicators")]
    public GameObject masterValveIndicator;
    public GameObject accumulatorIndicator;
    public GameObject pumpIndicator;
    
    [Header("Debug")]
    public bool showDebugGUI = true;
    
    // Reference to the main data receiver
    private SimulationDataReceiver dataReceiver;
    
    // BOP states dictionary
    private Dictionary<string, string> bopStates = new Dictionary<string, string>();
    
    // Pressure values
    private Dictionary<string, float> bopPressures = new Dictionary<string, float>();
    
    void Start()
    {
        // Ensure Unity runs in background
        Application.runInBackground = true;
        
        // Find the main simulation data receiver
        dataReceiver = FindObjectOfType<SimulationDataReceiver>();
        
        if (dataReceiver == null)
        {
            Debug.LogWarning("[BOPDataReceiver] SimulationDataReceiver not found! BOP data will not be received.");
        }
        
        // Initialize default materials if not assigned
        if (openMaterial == null)
        {
            openMaterial = CreateColorMaterial(Color.green);
        }
        if (closedMaterial == null)
        {
            closedMaterial = CreateColorMaterial(Color.red);
        }
        if (movingMaterial == null)
        {
            movingMaterial = CreateColorMaterial(Color.yellow);
        }
    }
    
    void Update()
    {
        if (dataReceiver == null || dataReceiver.currentData == null || dataReceiver.currentData.simulation_data == null)
            return;
        
        var simData = dataReceiver.currentData.simulation_data;
        
        // Update BOP states
        UpdateBOPStates(simData);
        
        // Update pressure displays
        UpdatePressureDisplays(simData);
        
        // Update control indicators
        UpdateControlIndicators(simData);
    }
    
    void UpdateBOPStates(SimulationData simData)
    {
        // Check if BOP states are available in the simulation data
        // Note: You'll need to add these fields to the SimulationData class
        // For now, we'll use a simplified approach
        
        // Example: Update Annular BOP
        if (annularBOP != null)
        {
            string state = GetBOPState("Annular BOP");
            ApplyBOPVisualState(annularBOP, state);
        }
        
        // Update Pipe Ram
        if (pipeRamBOP != null)
        {
            string state = GetBOPState("Pipe Ram");
            ApplyBOPVisualState(pipeRamBOP, state);
        }
        
        // Update Blind Ram
        if (blindRamBOP != null)
        {
            string state = GetBOPState("Blind Ram");
            ApplyBOPVisualState(blindRamBOP, state);
        }
        
        // Update Shear Ram
        if (shearRamBOP != null)
        {
            string state = GetBOPState("Shear Ram");
            ApplyBOPVisualState(shearRamBOP, state);
        }
    }
    
    string GetBOPState(string bopName)
    {
        // This would normally come from the simulation data
        // For now, return a default state
        if (bopStates.ContainsKey(bopName))
            return bopStates[bopName];
        return "open";
    }
    
    void ApplyBOPVisualState(GameObject bopObject, string state)
    {
        Renderer renderer = bopObject.GetComponent<Renderer>();
        if (renderer == null)
            renderer = bopObject.GetComponentInChildren<Renderer>();
        
        if (renderer != null)
        {
            switch (state.ToLower())
            {
                case "open":
                    renderer.material = openMaterial;
                    break;
                case "closed":
                    renderer.material = closedMaterial;
                    break;
                case "moving":
                    renderer.material = movingMaterial;
                    break;
            }
        }
        
        // Also update any animation or position if needed
        UpdateBOPAnimation(bopObject, state);
    }
    
    void UpdateBOPAnimation(GameObject bopObject, string state)
    {
        // Example: Animate BOP rams closing/opening
        // This would depend on your specific BOP model setup
        
        // Find rams or other moving parts
        Transform rams = bopObject.transform.Find("Rams");
        if (rams != null)
        {
            Vector3 targetPos = rams.localPosition;
            
            switch (state.ToLower())
            {
                case "open":
                    targetPos.x = 1.0f; // Rams apart
                    break;
                case "closed":
                    targetPos.x = 0.0f; // Rams together
                    break;
            }
            
            rams.localPosition = Vector3.Lerp(rams.localPosition, targetPos, Time.deltaTime * 2f);
        }
    }
    
    void UpdatePressureDisplays(SimulationData simData)
    {
        // Update pressure text displays
        if (annularPressureText != null)
            annularPressureText.text = $"Annular: {GetPressure("annular"):F0} PSI";
        
        if (manifoldPressureText != null)
            manifoldPressureText.text = $"Manifold: {GetPressure("manifold"):F0} PSI";
        
        if (accumulatorPressureText != null)
            accumulatorPressureText.text = $"Accumulator: {GetPressure("accumulator"):F0} PSI";
        
        if (airPressureText != null)
            airPressureText.text = $"Air: {GetPressure("air"):F0} PSI";
    }
    
    float GetPressure(string pressureType)
    {
        // This would normally come from the simulation data
        // Default values for now
        switch (pressureType)
        {
            case "annular": return 0f;
            case "manifold": return 1529f;
            case "accumulator": return 3000f;
            case "air": return 125f;
            default: return 0f;
        }
    }
    
    void UpdateControlIndicators(SimulationData simData)
    {
        // Update master valve indicator
        if (masterValveIndicator != null)
        {
            bool isOpen = false; // Would come from simData.master_valve_open
            masterValveIndicator.SetActive(isOpen);
            
            Renderer renderer = masterValveIndicator.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = isOpen ? openMaterial : closedMaterial;
        }
        
        // Update accumulator indicator
        if (accumulatorIndicator != null)
        {
            bool isOn = false; // Would come from simData.accumulator_on
            accumulatorIndicator.SetActive(isOn);
        }
        
        // Update pump indicator
        if (pumpIndicator != null)
        {
            bool isRunning = false; // Would come from simData.pump_running
            pumpIndicator.SetActive(isRunning);
            
            // Animate pump if running
            if (isRunning)
            {
                pumpIndicator.transform.Rotate(0, 100 * Time.deltaTime, 0);
            }
        }
    }
    
    Material CreateColorMaterial(Color color)
    {
        Material mat = new Material(Shader.Find("Standard"));
        mat.color = color;
        return mat;
    }
    
    void OnGUI()
    {
        if (!showDebugGUI) return;
        
        int w = 300;
        int h = 400;
        
        GUI.Box(new Rect(10, Screen.height - h - 10, w, h), "BOP Panel Status");
        
        int y = Screen.height - h + 20;
        
        GUI.Label(new Rect(20, y, w - 20, 20), "BOP Component States:");
        y += 25;
        
        // Show BOP states
        string[] bopNames = { "Annular BOP", "Pipe Ram", "Blind Ram", "Shear Ram" };
        foreach (string bopName in bopNames)
        {
            string state = GetBOPState(bopName);
            GUI.color = GetStateColor(state);
            GUI.Label(new Rect(30, y, w - 30, 20), $"{bopName}: {state.ToUpper()}");
            y += 20;
        }
        
        GUI.color = Color.white;
        y += 10;
        
        GUI.Label(new Rect(20, y, w - 20, 20), "Pressure Values:");
        y += 25;
        
        GUI.Label(new Rect(30, y, w - 30, 20), $"Annular: {GetPressure("annular"):F0} PSI");
        y += 20;
        GUI.Label(new Rect(30, y, w - 30, 20), $"Manifold: {GetPressure("manifold"):F0} PSI");
        y += 20;
        GUI.Label(new Rect(30, y, w - 30, 20), $"Accumulator: {GetPressure("accumulator"):F0} PSI");
        y += 20;
        GUI.Label(new Rect(30, y, w - 30, 20), $"Air: {GetPressure("air"):F0} PSI");
        y += 30;
        
        GUI.Label(new Rect(20, y, w - 20, 20), "Control Status:");
        y += 25;
        
        GUI.Label(new Rect(30, y, w - 30, 20), "Master Valve: CLOSED");
        y += 20;
        GUI.Label(new Rect(30, y, w - 30, 20), "Accumulator: OFF");
        y += 20;
        GUI.Label(new Rect(30, y, w - 30, 20), "Pump: STOPPED");
    }
    
    Color GetStateColor(string state)
    {
        switch (state.ToLower())
        {
            case "open": return Color.green;
            case "closed": return Color.red;
            case "moving": return Color.yellow;
            default: return Color.white;
        }
    }
}