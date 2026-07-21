using UnityEngine;

/// <summary>
/// Test script for CyberChairButtonController
/// Allows manual testing of button rotations in Unity Editor
/// </summary>
public class CyberChairButtonTest : MonoBehaviour
{
    [Header("Button Controller Reference")]
    public CyberChairButtonController buttonController;
    
    [Header("Manual Test Controls")]
    [Space(10)]
    [Header("Individual Pump Controls")]
    public bool pump1On = false;
    public bool pump2On = false;
    public bool pump3On = false;
    
    [Space(5)]
    [Header("TopDrive Control")]
    public bool topDriveOn = false;
    
    [Space(10)]
    [Header("Quick Actions")]
    public bool allPumpsOn = false;
    public bool allPumpsOff = false;
    public bool resetButtons = false;
    
    [Space(10)]
    [Header("Test Patterns")]
    public bool runSequenceTest = false;
    public float sequenceDelay = 1f;
    
    private bool lastPump1State = false;
    private bool lastPump2State = false;
    private bool lastPump3State = false;
    private bool lastTopDriveState = false;
    private bool lastAllOn = false;
    private bool lastAllOff = false;
    
    private float sequenceTimer = 0f;
    private int sequenceStep = 0;
    
    void Start()
    {
        // Auto-find button controller if not assigned
        if (buttonController == null)
        {
            buttonController = GetComponent<CyberChairButtonController>();
            if (buttonController == null)
            {
                buttonController = FindObjectOfType<CyberChairButtonController>();
            }
        }
        
        if (buttonController == null)
        {
            Debug.LogError("[CyberChairButtonTest] No CyberChairButtonController found!");
        }
        else
        {
            // Enable force test mode to allow manual control
            buttonController.forceTestMode = true;
            Debug.Log("[CyberChairButtonTest] Force test mode enabled on CyberChairButtonController");
        }
    }
    
    void Update()
    {
        if (buttonController == null) return;
        
        // Check for individual pump changes
        if (pump1On != lastPump1State)
        {
            buttonController.SetPump1State(pump1On);
            lastPump1State = pump1On;
            Debug.Log($"[Test] Pump 1 set to: {(pump1On ? "ON" : "OFF")}");
        }
        
        if (pump2On != lastPump2State)
        {
            buttonController.SetPump2State(pump2On);
            lastPump2State = pump2On;
            Debug.Log($"[Test] Pump 2 set to: {(pump2On ? "ON" : "OFF")}");
        }
        
        if (pump3On != lastPump3State)
        {
            buttonController.SetPump3State(pump3On);
            lastPump3State = pump3On;
            Debug.Log($"[Test] Pump 3 set to: {(pump3On ? "ON" : "OFF")}");
        }
        
        if (topDriveOn != lastTopDriveState)
        {
            buttonController.SetTopDriveState(topDriveOn);
            lastTopDriveState = topDriveOn;
            Debug.Log($"[Test] TopDrive set to: {(topDriveOn ? "ON (Rotating)" : "OFF (Stopped)")}");
        }
        
        // Check for all pumps on
        if (allPumpsOn && !lastAllOn)
        {
            buttonController.SetAllPumpStates(true, true, true);
            pump1On = pump2On = pump3On = true;
            lastPump1State = lastPump2State = lastPump3State = true;
            Debug.Log("[Test] All pumps turned ON");
        }
        lastAllOn = allPumpsOn;
        if (allPumpsOn) allPumpsOn = false; // Reset toggle
        
        // Check for all pumps off
        if (allPumpsOff && !lastAllOff)
        {
            buttonController.SetAllPumpStates(false, false, false);
            pump1On = pump2On = pump3On = false;
            lastPump1State = lastPump2State = lastPump3State = false;
            Debug.Log("[Test] All pumps turned OFF");
        }
        lastAllOff = allPumpsOff;
        if (allPumpsOff) allPumpsOff = false; // Reset toggle
        
        // Check for reset
        if (resetButtons)
        {
            buttonController.ResetAllButtons();
            pump1On = pump2On = pump3On = topDriveOn = false;
            lastPump1State = lastPump2State = lastPump3State = lastTopDriveState = false;
            resetButtons = false;
            Debug.Log("[Test] All buttons reset");
        }
        
        // Run sequence test
        if (runSequenceTest)
        {
            RunTestSequence();
        }
    }
    
    void RunTestSequence()
    {
        sequenceTimer += Time.deltaTime;
        
        if (sequenceTimer >= sequenceDelay)
        {
            sequenceTimer = 0f;
            
            switch (sequenceStep)
            {
                case 0:
                    Debug.Log("[Test Sequence] Step 1: Pump 1 ON");
                    buttonController.SetPump1State(true);
                    pump1On = true;
                    break;
                    
                case 1:
                    Debug.Log("[Test Sequence] Step 2: Pump 2 ON");
                    buttonController.SetPump2State(true);
                    pump2On = true;
                    break;
                    
                case 2:
                    Debug.Log("[Test Sequence] Step 3: Pump 3 ON");
                    buttonController.SetPump3State(true);
                    pump3On = true;
                    break;
                    
                case 3:
                    Debug.Log("[Test Sequence] Step 4: All pumps OFF");
                    buttonController.SetAllPumpStates(false, false, false);
                    pump1On = pump2On = pump3On = false;
                    break;
                    
                case 4:
                    Debug.Log("[Test Sequence] Step 5: All pumps ON");
                    buttonController.SetAllPumpStates(true, true, true);
                    pump1On = pump2On = pump3On = true;
                    break;
                    
                case 5:
                    Debug.Log("[Test Sequence] Step 6: Pumps 1 and 3 ON, Pump 2 OFF");
                    buttonController.SetAllPumpStates(true, false, true);
                    pump1On = true;
                    pump2On = false;
                    pump3On = true;
                    break;
                    
                case 6:
                    Debug.Log("[Test Sequence] Step 7: TopDrive ON");
                    buttonController.SetTopDriveState(true);
                    topDriveOn = true;
                    break;
                    
                case 7:
                    Debug.Log("[Test Sequence] Step 8: All ON (Pumps + TopDrive)");
                    buttonController.SetAllStates(true, true, true, true);
                    pump1On = pump2On = pump3On = topDriveOn = true;
                    break;
                    
                case 8:
                    Debug.Log("[Test Sequence] Complete - Resetting");
                    buttonController.ResetAllButtons();
                    pump1On = pump2On = pump3On = topDriveOn = false;
                    runSequenceTest = false;
                    sequenceStep = -1;
                    break;
            }
            
            sequenceStep++;
            lastPump1State = pump1On;
            lastPump2State = pump2On;
            lastPump3State = pump3On;
            lastTopDriveState = topDriveOn;
        }
    }
    
    void OnGUI()
    {
        if (buttonController == null) return;
        
        // Display current pump states
        GUI.Box(new Rect(10, 10, 200, 120), "Button States");
        
        bool p1, p2, p3, td;
        buttonController.GetAllStates(out p1, out p2, out p3, out td);
        
        GUI.Label(new Rect(20, 30, 180, 20), $"Pump 1: {(p1 ? "ON" : "OFF")}");
        GUI.Label(new Rect(20, 50, 180, 20), $"Pump 2: {(p2 ? "ON" : "OFF")}");
        GUI.Label(new Rect(20, 70, 180, 20), $"Pump 3: {(p3 ? "ON" : "OFF")}");
        GUI.Label(new Rect(20, 90, 180, 20), $"TopDrive: {(td ? "ON" : "OFF")}");
        
        // Quick control buttons
        if (GUI.Button(new Rect(10, 140, 95, 30), "All ON"))
        {
            buttonController.SetAllStates(true, true, true, true);
            pump1On = pump2On = pump3On = topDriveOn = true;
            lastPump1State = lastPump2State = lastPump3State = lastTopDriveState = true;
        }
        
        if (GUI.Button(new Rect(115, 140, 95, 30), "All OFF"))
        {
            buttonController.SetAllStates(false, false, false, false);
            pump1On = pump2On = pump3On = topDriveOn = false;
            lastPump1State = lastPump2State = lastPump3State = lastTopDriveState = false;
        }
        
        if (GUI.Button(new Rect(10, 175, 200, 30), "Run Test Sequence"))
        {
            runSequenceTest = true;
            sequenceStep = 0;
            sequenceTimer = sequenceDelay; // Start immediately
        }
    }
}