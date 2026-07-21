using UnityEngine;

/// <summary>
/// Simple test script to diagnose BOP light issues
/// Attach this to a GameObject to test in Unity
/// </summary>
public class BOPLightTester : MonoBehaviour
{
    [Header("Test Controls")]
    public BOPPanelController bopController;
    
    [Header("Manual Light Test")]
    public GameObject testLight;
    public bool turnOnLight = false;
    public bool turnOffLight = false;
    
    void Start()
    {
        // Find BOP controller if not assigned
        if (bopController == null)
        {
            bopController = FindObjectOfType<BOPPanelController>();
        }
    }
    
    void Update()
    {
        // Manual test controls
        if (turnOnLight && testLight != null)
        {
            testLight.SetActive(true);
            Debug.Log($"Manually turned ON: {testLight.name}");
            turnOnLight = false;
        }
        
        if (turnOffLight && testLight != null)
        {
            testLight.SetActive(false);
            Debug.Log($"Manually turned OFF: {testLight.name}");
            turnOffLight = false;
        }
        
        // Keyboard shortcuts for testing
        if (Input.GetKeyDown(KeyCode.D))
        {
            if (bopController != null)
            {
                bopController.DiagnoseLights();
            }
            else
            {
                Debug.LogError("BOP Controller not found!");
            }
        }
        
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (bopController != null)
            {
                bopController.TestAllLights();
            }
        }
    }
    
    void OnGUI()
    {
        // Create debug buttons
        if (GUI.Button(new Rect(10, 10, 150, 30), "Diagnose Lights"))
        {
            if (bopController != null)
                bopController.DiagnoseLights();
        }
        
        if (GUI.Button(new Rect(10, 50, 150, 30), "Test All Lights"))
        {
            if (bopController != null)
                bopController.TestAllLights();
        }
        
        if (GUI.Button(new Rect(10, 90, 150, 30), "Force Update"))
        {
            if (bopController != null)
                bopController.ForceUpdate();
        }
        
        if (GUI.Button(new Rect(10, 130, 150, 30), "Test Annular"))
        {
            if (bopController != null)
                bopController.TestComponentStates("Annular");
        }
        
        if (GUI.Button(new Rect(10, 170, 150, 30), "Force Green ON"))
        {
            ForceAllLights(true, false);
        }
        
        if (GUI.Button(new Rect(10, 210, 150, 30), "Force Red ON"))
        {
            ForceAllLights(false, true);
        }
        
        if (GUI.Button(new Rect(10, 250, 150, 30), "Force All OFF"))
        {
            ForceAllLights(false, false);
        }
        
        // Manual state test buttons
        if (GUI.Button(new Rect(170, 10, 150, 30), "Annular OPEN"))
        {
            if (bopController != null)
                bopController.SetTestState("Annular", "open");
        }
        
        if (GUI.Button(new Rect(170, 50, 150, 30), "Annular CLOSED"))
        {
            if (bopController != null)
                bopController.SetTestState("Annular", "closed");
        }
        
        if (GUI.Button(new Rect(170, 90, 150, 30), "Annular MOVING"))
        {
            if (bopController != null)
                bopController.SetTestState("Annular", "moving");
        }
        
        if (GUI.Button(new Rect(170, 130, 150, 30), "Master OPEN"))
        {
            if (bopController != null)
                bopController.SetTestState("Master", "open");
        }
        
        if (GUI.Button(new Rect(170, 170, 150, 30), "Master CLOSED"))
        {
            if (bopController != null)
                bopController.SetTestState("Master", "closed");
        }
    }
    
    void ForceAllLights(bool green, bool red)
    {
        if (bopController == null) return;
        
        // Force all lights to specific state
        SetLight(bopController.annularGreenLight, green);
        SetLight(bopController.annularRedLight, red);
        SetLight(bopController.pipeRamGreenLight, green);
        SetLight(bopController.pipeRamRedLight, red);
        SetLight(bopController.blindRamGreenLight, green);
        SetLight(bopController.blindRamRedLight, red);
        
        Debug.Log($"Forced all lights: Green={green}, Red={red}");
    }
    
    void SetLight(GameObject light, bool state)
    {
        if (light != null)
        {
            light.SetActive(state);
        }
    }
}