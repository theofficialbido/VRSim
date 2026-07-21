using UnityEngine;
using Pansim.Hardware;
using Pansim.Controllers;

/// <summary>
/// Example script showing how to use the Pansim hardware control system
/// </summary>
public class PansimControlExample : MonoBehaviour
{
    [Header("Objects to Control")]
    [SerializeField] private Transform topDrive;
    [SerializeField] private Transform drillString;
    [SerializeField] private Transform cameraRig;
    
    [Header("Control Settings")]
    [SerializeField] private float topDriveSpeed = 5f;
    [SerializeField] private float drillRotationSpeed = 100f;
    [SerializeField] private float cameraRotateSpeed = 50f;
    
    [Header("Limits")]
    [SerializeField] private float minHeight = 0f;
    [SerializeField] private float maxHeight = 20f;
    
    private HardwareManager hardwareManager;
    private bool isInitialized = false;
    
    void Start()
    {
        // Get the hardware manager
        hardwareManager = HardwareManager.Instance;
        
        if (hardwareManager == null)
        {
            Debug.LogError("[PansimControlExample] HardwareManager not found!");
            enabled = false;
            return;
        }
        
        // Subscribe to hardware events
        hardwareManager.OnHardwareConnected.AddListener(OnHardwareConnected);
        hardwareManager.OnHardwareDisconnected.AddListener(OnHardwareDisconnected);
        
        // Register custom actions
        hardwareManager.RegisterAction("TopDriveControl", OnTopDriveControl);
        hardwareManager.RegisterAction("CameraControl", OnCameraControl);
        
        // Subscribe to button events
        hardwareManager.OnButtonPressed.AddListener(OnButtonPressed);
        
        isInitialized = true;
        Debug.Log("[PansimControlExample] Initialized");
    }
    
    void Update()
    {
        if (!isInitialized || !hardwareManager.IsConnected())
            return;
        
        // Method 1: Direct axis reading
        float leftY = hardwareManager.GetAxis("vertical");
        float rightX = hardwareManager.GetAxis("look horizontal");
        
        // Control top drive with left joystick Y
        if (topDrive != null && Mathf.Abs(leftY) > 0.01f)
        {
            MoveTopDrive(leftY);
        }
        
        // Rotate camera with right joystick X
        if (cameraRig != null && Mathf.Abs(rightX) > 0.01f)
        {
            RotateCamera(rightX);
        }
        
        // Method 2: Using full joystick vectors
        Vector2 leftJoystick = hardwareManager.GetLeftJoystick();
        Vector2 rightJoystick = hardwareManager.GetRightJoystick();
        
        // Rotate drill string with left joystick X
        if (drillString != null && Mathf.Abs(leftJoystick.x) > 0.01f)
        {
            drillString.Rotate(0, leftJoystick.x * drillRotationSpeed * Time.deltaTime, 0);
        }
    }
    
    private void MoveTopDrive(float input)
    {
        float moveAmount = -input * topDriveSpeed * Time.deltaTime;
        Vector3 newPosition = topDrive.position;
        newPosition.y += moveAmount;
        newPosition.y = Mathf.Clamp(newPosition.y, minHeight, maxHeight);
        topDrive.position = newPosition;
    }
    
    private void RotateCamera(float input)
    {
        float rotateAmount = input * cameraRotateSpeed * Time.deltaTime;
        cameraRig.Rotate(0, rotateAmount, 0, Space.World);
    }
    
    private void OnTopDriveControl(float value)
    {
        // This is called when the "TopDriveControl" action changes
        Debug.Log($"Top Drive Control: {value}");
    }
    
    private void OnCameraControl(float value)
    {
        // This is called when the "CameraControl" action changes
        Debug.Log($"Camera Control: {value}");
    }
    
    private void OnButtonPressed(int buttonIndex)
    {
        Debug.Log($"Button {buttonIndex} pressed!");
        
        switch (buttonIndex)
        {
            case 0: // Emergency stop
                EmergencyStop();
                break;
            case 1: // Reset position
                ResetPositions();
                break;
            case 2: // Toggle drilling
                ToggleDrilling();
                break;
        }
    }
    
    private void OnHardwareConnected()
    {
        Debug.Log("[PansimControlExample] Hardware connected!");
        // Optionally reset or calibrate when hardware connects
    }
    
    private void OnHardwareDisconnected()
    {
        Debug.Log("[PansimControlExample] Hardware disconnected!");
        // Optionally stop all movement when hardware disconnects
        EmergencyStop();
    }
    
    private void EmergencyStop()
    {
        Debug.Log("EMERGENCY STOP!");
        // Stop all movement
        enabled = false;
        
        // Could also trigger other safety mechanisms
    }
    
    private void ResetPositions()
    {
        Debug.Log("Resetting positions...");
        
        if (topDrive != null)
        {
            Vector3 pos = topDrive.position;
            pos.y = (minHeight + maxHeight) / 2f;
            topDrive.position = pos;
        }
        
        if (cameraRig != null)
        {
            cameraRig.rotation = Quaternion.identity;
        }
        
        if (drillString != null)
        {
            drillString.rotation = Quaternion.identity;
        }
    }
    
    private void ToggleDrilling()
    {
        // Implement drilling toggle logic
        Debug.Log("Toggle drilling");
    }
    
    void OnDestroy()
    {
        if (hardwareManager != null)
        {
            hardwareManager.UnregisterAction("TopDriveControl");
            hardwareManager.UnregisterAction("CameraControl");
            
            hardwareManager.OnHardwareConnected.RemoveListener(OnHardwareConnected);
            hardwareManager.OnHardwareDisconnected.RemoveListener(OnHardwareDisconnected);
            hardwareManager.OnButtonPressed.RemoveListener(OnButtonPressed);
        }
    }
}