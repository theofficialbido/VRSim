using UnityEngine;

/// <summary>
/// Test script for SWACO Panel Controller
/// Provides GUI buttons and keyboard shortcuts for testing SWACO panel functionality
/// </summary>
public class SWACOPanelTester : MonoBehaviour
{
    [Header("Test Controls")]
    public SWACOPanelController swacoController;
    
    [Header("Manual Test Values")]
    public float testPressureValue = 1000f;
    public bool testHoldLight = false;
    public bool testResetLight = false;
    
    void Start()
    {
        // Find SWACO controller if not assigned
        if (swacoController == null)
        {
            swacoController = FindObjectOfType<SWACOPanelController>();
        }
    }
    
    void Update()
    {
        // Keyboard shortcuts for testing
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (swacoController != null)
                swacoController.DiagnoseSWACOPanel();
        }
        
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (swacoController != null)
                swacoController.TestAllNeedles();
        }
        
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            if (swacoController != null)
                swacoController.TestLights();
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 400));
        
        GUILayout.Label("SWACO Panel Tester", GUI.skin.box);
        
        if (swacoController == null)
        {
            GUILayout.Label("SWACO Controller not found!", GUI.skin.box);
            return;
        }
        
        GUILayout.Space(10);
        
        // Diagnostic button
        if (GUILayout.Button("Diagnose SWACO Panel (1)"))
        {
            swacoController.DiagnoseSWACOPanel();
        }
        
        GUILayout.Space(5);
        
        // Test buttons
        if (GUILayout.Button("Test All Needles (2)"))
        {
            swacoController.TestAllNeedles();
        }
        
        if (GUILayout.Button("Test Lights (3)"))
        {
            swacoController.TestLights();
        }
        
        GUILayout.Space(10);
        
        // Manual controls
        GUILayout.Label("Manual Controls:", GUI.skin.box);
        
        GUILayout.Label($"Test Pressure: {testPressureValue:F0} PSI");
        testPressureValue = GUILayout.HorizontalSlider(testPressureValue, 0f, 5000f);
        
        GUILayout.Space(5);
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"Hold Light: {(testHoldLight ? "ON" : "OFF")}"))
        {
            testHoldLight = !testHoldLight;
            SetLightManually("hold", testHoldLight);
        }
        
        if (GUILayout.Button($"Reset Light: {(testResetLight ? "ON" : "OFF")}"))
        {
            testResetLight = !testResetLight;
            SetLightManually("reset", testResetLight);
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(5);
        
        // Pressure test buttons
        GUILayout.Label("Pressure Tests:");
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("0%"))
        {
            SetAllNeedles(0f);
        }
        if (GUILayout.Button("25%"))
        {
            SetAllNeedles(0.25f);
        }
        if (GUILayout.Button("50%"))
        {
            SetAllNeedles(0.5f);
        }
        GUILayout.EndHorizontal();
        
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("75%"))
        {
            SetAllNeedles(0.75f);
        }
        if (GUILayout.Button("100%"))
        {
            SetAllNeedles(1f);
        }
        if (GUILayout.Button("Custom"))
        {
            SetAllNeedles(testPressureValue / 5000f); // Assuming 5000 PSI max
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Space(10);
        
        // Instructions
        GUILayout.Label("Keyboard Shortcuts:", GUI.skin.box);
        GUILayout.Label("1 - Diagnose Panel");
        GUILayout.Label("2 - Test All Needles");
        GUILayout.Label("3 - Test Lights");
        
        GUILayout.EndArea();
    }
    
    void SetLightManually(string lightType, bool state)
    {
        if (swacoController == null) return;
        
        Debug.Log($"[SWACOPanelTester] Manual {lightType} light: {state}");
        
        // Since the controller's light methods are private, we'll need to access them through reflection
        // or add public methods to the controller for testing
        
        if (lightType == "hold" && swacoController.holdLight != null)
        {
            swacoController.holdLight.SetActive(state);
        }
        else if (lightType == "reset" && swacoController.resetLight != null)
        {
            swacoController.resetLight.SetActive(state);
        }
    }
    
    void SetAllNeedles(float percentage)
    {
        if (swacoController == null) return;
        
        Debug.Log($"[SWACOPanelTester] Setting all needles to {percentage * 100:F0}%");
        
        // Calculate rotation based on needle range
        float targetRotation = Mathf.Lerp(swacoController.needleMinRotation, swacoController.needleMaxRotation, percentage);
        
        // Set all needles directly (bypassing smooth movement for immediate testing)
        if (swacoController.standpipeNeedle != null)
            swacoController.standpipeNeedle.localEulerAngles = new Vector3(0, 0, targetRotation);
            
        if (swacoController.casingNeedle != null)
            swacoController.casingNeedle.localEulerAngles = new Vector3(0, 0, targetRotation);
            
        if (swacoController.pumpPressure1Needle != null)
            swacoController.pumpPressure1Needle.localEulerAngles = new Vector3(0, 0, targetRotation);
            
        if (swacoController.pumpPressure2Needle != null)
            swacoController.pumpPressure2Needle.localEulerAngles = new Vector3(0, 0, targetRotation);
            
        if (swacoController.chokeManifoldNeedle != null)
            swacoController.chokeManifoldNeedle.localEulerAngles = new Vector3(0, 0, targetRotation);
    }
}