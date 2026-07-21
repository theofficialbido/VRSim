using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Direct test for BOP lights - simulates receiving data from Python
/// </summary>
public class BOPDirectTest : MonoBehaviour
{
    private BOPPanelController bopController;
    private SimulationDataReceiver dataReceiver;
    private float testTimer = 0f;
    private int testPhase = 0;
    
    void Start()
    {
        bopController = FindObjectOfType<BOPPanelController>();
        dataReceiver = FindObjectOfType<SimulationDataReceiver>();
        
        if (bopController == null)
        {
            Debug.LogError("[BOPDirectTest] BOPPanelController not found!");
        }
        
        if (dataReceiver == null)
        {
            Debug.LogError("[BOPDirectTest] SimulationDataReceiver not found!");
        }
    }
    
    void Update()
    {
        // Run automatic tests every few seconds
        testTimer += Time.deltaTime;
        
        if (testTimer > 3f)
        {
            testTimer = 0f;
            RunNextTest();
        }
    }
    
    void RunNextTest()
    {
        if (dataReceiver == null || dataReceiver.currentData == null)
        {
            Debug.LogWarning("[BOPDirectTest] No data receiver or current data");
            return;
        }
        
        // Initialize simulation data if needed
        if (dataReceiver.currentData.simulation_data == null)
        {
            dataReceiver.currentData.simulation_data = new SimulationData();
        }
        
        if (dataReceiver.currentData.simulation_data.bop_states == null)
        {
            dataReceiver.currentData.simulation_data.bop_states = new Dictionary<string, string>();
        }
        
        // Cycle through test phases
        switch (testPhase)
        {
            case 0:
                Debug.Log("[BOPDirectTest] TEST PHASE 0: All components OPEN");
                SetAllComponentsState("open");
                break;
                
            case 1:
                Debug.Log("[BOPDirectTest] TEST PHASE 1: All components CLOSED");
                SetAllComponentsState("closed");
                break;
                
            case 2:
                Debug.Log("[BOPDirectTest] TEST PHASE 2: All components MOVING");
                SetAllComponentsState("moving");
                break;
                
            case 3:
                Debug.Log("[BOPDirectTest] TEST PHASE 3: Testing Kill and Choke Lines specifically");
                dataReceiver.currentData.simulation_data.bop_states["Kill Line"] = "open";
                dataReceiver.currentData.simulation_data.bop_states["Choke Line"] = "closed";
                Debug.Log("  Kill Line: OPEN, Choke Line: CLOSED");
                break;
                
            case 4:
                Debug.Log("[BOPDirectTest] TEST PHASE 4: Testing Kill and Choke Lines reversed");
                dataReceiver.currentData.simulation_data.bop_states["Kill Line"] = "closed";
                dataReceiver.currentData.simulation_data.bop_states["Choke Line"] = "open";
                Debug.Log("  Kill Line: CLOSED, Choke Line: OPEN");
                break;
        }
        
        // Force the BOP controller to update
        if (bopController != null)
        {
            bopController.ForceUpdate();
        }
        
        // Move to next phase
        testPhase = (testPhase + 1) % 5;
    }
    
    void SetAllComponentsState(string state)
    {
        var states = dataReceiver.currentData.simulation_data.bop_states;
        
        states["Annular-1"] = state;
        states["Pipe Ram-1"] = state;
        states["Pipe Ram-2"] = state;
        states["Pipe Ram-3"] = state;
        states["Blind/Shear Ram"] = state;
        states["Kill Line"] = state;
        states["Choke Line"] = state;
        
        Debug.Log($"[BOPDirectTest] Set all components to: {state}");
        Debug.Log($"  Kill Line in data: {states.ContainsKey("Kill Line")} = {(states.ContainsKey("Kill Line") ? states["Kill Line"] : "NOT FOUND")}");
        Debug.Log($"  Choke Line in data: {states.ContainsKey("Choke Line")} = {(states.ContainsKey("Choke Line") ? states["Choke Line"] : "NOT FOUND")}");
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, Screen.height - 200, 300, 190));
        GUILayout.Box("BOP Direct Test Controls");
        
        if (GUILayout.Button("Force All OPEN"))
        {
            SetAllComponentsState("open");
            if (bopController != null) bopController.ForceUpdate();
        }
        
        if (GUILayout.Button("Force All CLOSED"))
        {
            SetAllComponentsState("closed");
            if (bopController != null) bopController.ForceUpdate();
        }
        
        if (GUILayout.Button("Force All MOVING"))
        {
            SetAllComponentsState("moving");
            if (bopController != null) bopController.ForceUpdate();
        }
        
        if (GUILayout.Button("Test Kill/Choke Lines"))
        {
            if (dataReceiver != null && dataReceiver.currentData != null)
            {
                if (dataReceiver.currentData.simulation_data == null)
                    dataReceiver.currentData.simulation_data = new SimulationData();
                if (dataReceiver.currentData.simulation_data.bop_states == null)
                    dataReceiver.currentData.simulation_data.bop_states = new Dictionary<string, string>();
                    
                dataReceiver.currentData.simulation_data.bop_states["Kill Line"] = "moving";
                dataReceiver.currentData.simulation_data.bop_states["Choke Line"] = "moving";
                
                Debug.Log("[BOPDirectTest] Set Kill/Choke to MOVING");
                if (bopController != null) bopController.ForceUpdate();
            }
        }
        
        GUILayout.Label($"Auto Test Phase: {testPhase}/4");
        GUILayout.Label($"Next test in: {(3f - testTimer):F1}s");
        
        GUILayout.EndArea();
    }
}