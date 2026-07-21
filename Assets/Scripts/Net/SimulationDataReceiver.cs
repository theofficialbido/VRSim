using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR || UNITY_STANDALONE
using NativeWebSocket;
#endif

[Serializable]
public class CameraControlData
{
    public string camera_action = "";     // "move", "zoom", "switch"
    public string direction = "";         // For move: "up", "down", "left", "right"
                                          // For zoom: "in", "out"
    public int camera_index = 0;          // For switch: 0-8 (Camera 1-9)
    public float movement_speed = 1.0f;   // Movement speed multiplier
    public float zoom_speed = 1.0f;       // Zoom speed multiplier
}

[Serializable]
public class SimulationData
{
    public float tds_position;           // TDS position in feet (0-120)
    public float tds_position_normalized; // Normalized position (0-1)
    public string tds_movement;          // "Up", "Down", or "Stop"
    public float rpm;                     // Rotation RPM
    public float wob;                     // Weight on bit
    public bool auto_drill;              // Auto-drill mode
    public bool pump_1_active;
    public bool pump_2_active;
    public bool pump_3_active;
    public float pump_1_spm;
    public float pump_2_spm;
    public float pump_3_spm;
    public float total_flow;              // Total flow rate in GPM
    
    // BOP Panel data
    public Dictionary<string, string> bop_states = new Dictionary<string, string>();     // BOP component states
    public BOPPressures bop_pressures = new BOPPressures();                // Pressure values
    public bool master_valve_open;
    public bool accumulator_on;
    public bool pump_running;
    
    // Camera control data
    public CameraControlData camera_control = new CameraControlData();
    
    // SWACO Panel data
    public SWACOPanelData swaco_panel = new SWACOPanelData();
}

[Serializable]
public class SWACOPanelData
{
    // Light states
    public bool hold_active = false;
    public bool reset_active = false;
    
    // Pressure readings for gauge needles
    public float standpipe_pressure = 0f;
    public float casing_pressure = 0f;
    public float pump_1_pressure = 0f;
    public float pump_2_pressure = 0f;
    public float choke_manifold_pressure = 0f;
    
    // Totalizer values
    public float total_strokes = 0f;
    public float total_volume = 0f;  // In barrels
    
    // Additional pump data
    public float mp1_speed = 0f;     // SPM (strokes per minute)
    public float mp2_speed = 0f;     // SPM (strokes per minute)
    public float mp1_pressure = 0f; // Duplicate for convenience
    public float mp2_pressure = 0f; // Duplicate for convenience
    
    // Flow rates
    public float total_flow_rate = 0f; // GPM
    public float instantaneous_flow = 0f;
    
    // Additional SWACO panel specific data
    public string panel_mode = "auto";  // "auto", "manual", "hold", etc.
    public bool alarm_active = false;
    public string alarm_message = "";
}

[Serializable]
public class BOPPressures
{
    public float annular;
    public float manifold;
    public float accumulator;
    public float air;
    
    // Optional: Direct needle rotation values from Python (0-1 normalized or degrees)
    public float annular_needle_rotation = -999f; // -999 means use calculated value
    public float manifold_needle_rotation = -999f;
    public float accumulator_needle_rotation = -999f;
    public float air_needle_rotation = -999f;
}

[Serializable]
public class BridgeData
{
    public float left_x;      // Left joystick X (rotation)
    public float left_y;      // Left joystick Y (TDS movement command)
    public float right_x;     // Right joystick X
    public float right_y;     // Right joystick Y
    public int button_states; // Button states as bit flags
    public SimulationData simulation_data;
    public float timestamp;
}

public class SimulationDataReceiver : MonoBehaviour
{
    [Header("WebSocket")]
    public string serverAddress = "ws://localhost:8765";  // Unity simulator bridge port
    public bool autoConnect = true;
    public float reconnectDelay = 3f;
    
    [Header("Top Drive Control")]
    public Transform topDriveObject;      // The Top Drive object to control
    public float minHeight = 0f;          // Minimum height (bottom position)
    public float maxHeight = 20f;         // Maximum height (top position)
    public bool useDirectPosition = true; // Use position data directly vs joystick control
    
    [Header("Rotation Control")]
    public Transform rotaryTableObject;   // Object that rotates (drill string)
    public float maxRotationSpeed = 250f; // Max RPM
    
    [Header("Pump Indicators")]
    public GameObject pump1Indicator;
    public GameObject pump2Indicator;
    public GameObject pump3Indicator;
    public TMPro.TextMeshProUGUI flowRateText;
    
    [Header("Camera Control")]
    public List<Camera> availableCameras = new List<Camera>();  // List of cameras to cycle through
    public float cameraMovementSpeed = 5f;   // Base movement speed (deprecated, kept for compatibility)
    public float cameraRotationSpeed = 50f;  // Base rotation speed
    public float cameraZoomSpeed = 10f;      // Base zoom speed
    public float minFOV = 20f;               // Minimum field of view (zoomed in)
    public float maxFOV = 60f;               // Maximum field of view (zoomed out)
    public float maxLookUpAngle = 60f;       // Maximum angle to look up
    public float maxLookDownAngle = 60f;     // Maximum angle to look down
    
    [Header("Display")]
    public TMPro.TextMeshProUGUI tdsPositionText;
    public TMPro.TextMeshProUGUI rpmText;
    public TMPro.TextMeshProUGUI statusText;
    public TMPro.TextMeshProUGUI cameraStatusText;  // Display current camera info
    
    // NOTE: All BOP control functionality has been moved to BOPPanelController
    // SimulationDataReceiver now only handles data reception and storage
    // To control BOP displays and lights, use the BOPPanelController component
    
    [Header("Debug")]
    public bool showDebugGUI = true;
    
#if UNITY_EDITOR || UNITY_STANDALONE
    private WebSocket websocket;
#endif
    
    public BridgeData currentData { get; private set; }
    private bool connected = false;
    private string connectionStatus = "Initializing...";
    
    // Camera control variables
    private int currentCameraIndex = 0;
    private Camera activeCamera;
    private bool isCameraMoving = false;
    private string currentCameraMovement = "";
    private bool isCameraZooming = false;
    private string currentZoomDirection = "";
    
    void Start()
    {
        Debug.Log("[Unity] SimulationDataReceiver Start() called");
        
        // Force Unity to run in background (even when not focused)
        Application.runInBackground = true;
        
        currentData = new BridgeData();
        currentData.simulation_data = new SimulationData();
        currentData.simulation_data.camera_control = new CameraControlData();
        
        Debug.Log("[Unity] Initializing camera system...");
        // Initialize camera system
        InitializeCameraSystem();
        
#if UNITY_EDITOR || UNITY_STANDALONE
        if (autoConnect)
            StartCoroutine(Connect());
#else
        connectionStatus = "WebSocket not supported on this platform";
#endif
    }
    
#if UNITY_EDITOR || UNITY_STANDALONE
    IEnumerator Connect()
    {
        while (true)
        {
            if (!connected && websocket == null)
            {
                connectionStatus = "Connecting to simulator bridge...";
                bool connectionAttemptFailed = false;
                
                try
                {
                    websocket = new WebSocket(serverAddress);
                    
                    websocket.OnOpen += () => {
                        Debug.Log("[Unity] Connected to simulator bridge");
                        connected = true;
                        connectionStatus = "Connected";
                    };
                    
                    websocket.OnError += (e) => {
                        Debug.LogError("[Unity] WebSocket error: " + e);
                        connectionStatus = "Error: " + e;
                    };
                    
                    websocket.OnClose += (e) => {
                        Debug.Log("[Unity] Disconnected from simulator bridge");
                        connected = false;
                        connectionStatus = "Disconnected";
                        websocket = null;
                    };
                    
                    websocket.OnMessage += (bytes) => {
                        try
                        {
                            var json = System.Text.Encoding.UTF8.GetString(bytes);
                            
                            // First try normal parsing
                            currentData = JsonUtility.FromJson<BridgeData>(json);
                            
                            if (currentData.simulation_data == null)
                                currentData.simulation_data = new SimulationData();
                            
                            // Manually parse camera_control from JSON string since JsonUtility has issues with nested objects
                            if (json.Contains("camera_control"))
                            {
                                // Extract camera_control JSON manually
                                int startIndex = json.IndexOf("\"camera_control\"");
                                if (startIndex != -1)
                                {
                                    int braceStart = json.IndexOf("{", startIndex);
                                    int braceEnd = json.IndexOf("}", braceStart);
                                    if (braceStart != -1 && braceEnd != -1)
                                    {
                                        string cameraJson = json.Substring(braceStart, braceEnd - braceStart + 1);
                                        
                                        // Parse camera control
                                        if (currentData.simulation_data.camera_control == null)
                                            currentData.simulation_data.camera_control = new CameraControlData();
                                        
                                        // Extract values manually
                                        currentData.simulation_data.camera_control.camera_action = ExtractStringValue(cameraJson, "camera_action");
                                        currentData.simulation_data.camera_control.direction = ExtractStringValue(cameraJson, "direction");
                                        currentData.simulation_data.camera_control.camera_index = ExtractIntValue(cameraJson, "camera_index");
                                        
                                        // Debug camera control data
                                        if (!string.IsNullOrEmpty(currentData.simulation_data.camera_control.camera_action))
                                        {
                                            Debug.Log($"[Unity] Camera Control Received - Action: {currentData.simulation_data.camera_control.camera_action}, " +
                                                     $"Direction: {currentData.simulation_data.camera_control.direction}, " +
                                                     $"Index: {currentData.simulation_data.camera_control.camera_index}");
                                        }
                                    }
                                }
                            }
                            
                            // Manually parse bop_states dictionary since JsonUtility doesn't handle Dictionary well
                            if (json.Contains("bop_states") && currentData.simulation_data != null)
                            {
                                if (currentData.simulation_data.bop_states == null)
                                    currentData.simulation_data.bop_states = new Dictionary<string, string>();
                                else
                                    currentData.simulation_data.bop_states.Clear();
                                
                                // Find bop_states in JSON
                                int bopStartIndex = json.IndexOf("\"bop_states\"");
                                if (bopStartIndex != -1)
                                {
                                    int bopBraceStart = json.IndexOf("{", bopStartIndex);
                                    // Find the matching closing brace
                                    int bopBraceEnd = bopBraceStart;
                                    int braceCount = 0;
                                    for (int i = bopBraceStart; i < json.Length; i++)
                                    {
                                        if (json[i] == '{') braceCount++;
                                        else if (json[i] == '}') 
                                        {
                                            braceCount--;
                                            if (braceCount == 0)
                                            {
                                                bopBraceEnd = i;
                                                break;
                                            }
                                        }
                                    }
                                    
                                    if (bopBraceStart != -1 && bopBraceEnd != -1 && bopBraceEnd > bopBraceStart)
                                    {
                                        string bopJson = json.Substring(bopBraceStart + 1, bopBraceEnd - bopBraceStart - 1);
                                        
                                        // Better parsing that handles component names with spaces
                                        // Look for pattern: "key": "value"
                                        int currentPos = 0;
                                        while (currentPos < bopJson.Length)
                                        {
                                            // Find the next quote for key
                                            int keyStart = bopJson.IndexOf('"', currentPos);
                                            if (keyStart == -1) break;
                                            
                                            int keyEnd = bopJson.IndexOf('"', keyStart + 1);
                                            if (keyEnd == -1) break;
                                            
                                            string key = bopJson.Substring(keyStart + 1, keyEnd - keyStart - 1);
                                            
                                            // Find the colon
                                            int colonPos = bopJson.IndexOf(':', keyEnd);
                                            if (colonPos == -1) break;
                                            
                                            // Find the value quotes
                                            int valueStart = bopJson.IndexOf('"', colonPos);
                                            if (valueStart == -1) break;
                                            
                                            int valueEnd = bopJson.IndexOf('"', valueStart + 1);
                                            if (valueEnd == -1) break;
                                            
                                            string value = bopJson.Substring(valueStart + 1, valueEnd - valueStart - 1);
                                            
                                            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                                            {
                                                currentData.simulation_data.bop_states[key] = value;
                                                
                                                // Log Kill Line and Choke Line specifically
                                                if (key == "Kill Line" || key == "Choke Line")
                                                {
                                                    Debug.Log($"[SimulationDataReceiver] IMPORTANT: {key} = {value}");
                                                }
                                            }
                                            
                                            currentPos = valueEnd + 1;
                                        }
                                        
                                        // Log BOP states periodically
                                        if (Time.frameCount % 300 == 0) // Every 5 seconds
                                        {
                                            Debug.Log($"[SimulationDataReceiver] BOP states: {currentData.simulation_data.bop_states.Count} components");
                                            if (currentData.simulation_data.bop_states.ContainsKey("Kill Line"))
                                                Debug.Log($"  Kill Line: {currentData.simulation_data.bop_states["Kill Line"]}");
                                            if (currentData.simulation_data.bop_states.ContainsKey("Choke Line"))
                                                Debug.Log($"  Choke Line: {currentData.simulation_data.bop_states["Choke Line"]}");
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError("[Unity] Failed to parse data: " + ex.Message);
                            Debug.LogError("[Unity] JSON sample: " + System.Text.Encoding.UTF8.GetString(bytes).Substring(0, Math.Min(500, bytes.Length)));
                        }
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogError("[Unity] Failed to create WebSocket: " + ex.Message);
                    connectionStatus = "Connection failed";
                    websocket = null;
                    connectionAttemptFailed = true;
                }
                
                // Connect outside of try-catch to avoid yield in catch
                if (!connectionAttemptFailed && websocket != null)
                {
                    yield return websocket.Connect();
                }
                
                if (!connected)
                {
                    yield return new WaitForSeconds(reconnectDelay);
                }
            }
            
            yield return new WaitForSeconds(1f);
        }
    }
#endif
    
    void Update()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        // Always process WebSocket messages, even when Unity doesn't have focus
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            websocket.DispatchMessageQueue();
        }
#endif
        
        if (connected && currentData != null && currentData.simulation_data != null)
        {
            // Debug: Check if camera control data exists
            if (currentData.simulation_data.camera_control != null && 
                !string.IsNullOrEmpty(currentData.simulation_data.camera_control.camera_action))
            {
                Debug.Log($"[Unity Update] Camera action detected: {currentData.simulation_data.camera_control.camera_action}");
            }
            
            // Process camera control commands
            ProcessCameraControl();
            
            // Update Top Drive position based on TDS position data
            if (topDriveObject != null)
            {
                if (useDirectPosition)
                {
                    // Use the actual TDS position from simulation
                    // Map 0-120 feet to minHeight-maxHeight in Unity units
                    // TDS position: 0 feet = bottom (minHeight), 120 feet = top (maxHeight)
                    float normalizedPos = currentData.simulation_data.tds_position / 120f;
                    float targetY = Mathf.Lerp(minHeight, maxHeight, normalizedPos); // Direct mapping: 0ft=minHeight, 120ft=maxHeight
                    
                    Vector3 pos = topDriveObject.position;
                    pos.y = Mathf.Lerp(pos.y, targetY, Time.deltaTime * 5f); // Smooth movement
                    topDriveObject.position = pos;
                }
                else
                {
                    // Use joystick control for movement
                    if (currentData.left_y != 0)
                    {
                        Vector3 pos = topDriveObject.position;
                        pos.y -= currentData.left_y * Time.deltaTime * 5f; // Movement speed
                        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);
                        topDriveObject.position = pos;
                    }
                }
            }
            
            // Update rotation based on RPM
            if (rotaryTableObject != null && currentData.simulation_data.rpm > 0)
            {
                float rotationSpeed = (currentData.simulation_data.rpm / 60f) * 360f; // Convert RPM to degrees/second
                rotaryTableObject.Rotate(0, rotationSpeed * Time.deltaTime, 0, Space.Self);
            }
            
            // Update pump indicators
            if (pump1Indicator != null)
                pump1Indicator.SetActive(currentData.simulation_data.pump_1_active);
            if (pump2Indicator != null)
                pump2Indicator.SetActive(currentData.simulation_data.pump_2_active);
            if (pump3Indicator != null)
                pump3Indicator.SetActive(currentData.simulation_data.pump_3_active);
            
            // Update text displays
            if (tdsPositionText != null)
                tdsPositionText.text = $"TDS: {currentData.simulation_data.tds_position:F1} ft";
            
            if (rpmText != null)
                rpmText.text = $"RPM: {currentData.simulation_data.rpm:F0}";
            
            if (flowRateText != null)
                flowRateText.text = $"Flow: {currentData.simulation_data.total_flow:F1} GPM";
            
            if (statusText != null)
            {
                string status = $"Status: {connectionStatus}\n";
                status += $"Movement: {currentData.simulation_data.tds_movement}\n";
                if (currentData.simulation_data.auto_drill)
                    status += "AUTO-DRILL ACTIVE";
                statusText.text = status;
            }
            
            // NOTE: BOP control functionality has been moved to BOPPanelController
            // The BOP data is still received and stored in currentData.simulation_data.bop_states
            // and currentData.simulation_data.bop_pressures for the BOPPanelController to read
        }
    }
    
    // BOP control methods have been moved to BOPPanelController
    // The following methods are now handled by BOPPanelController:
    // - UpdateBOPPressureDisplays() 
    // - UpdateBOPStatusLights()
    // - UpdateDualBOPLights()
    // - SetLightState()
    // - FormatPressureText()
    // - GetPressureColor()
    // 
    // BOP data is still received and stored in:
    // - currentData.simulation_data.bop_states (component states)
    // - currentData.simulation_data.bop_pressures (pressure values)
    // These data structures remain intact for BOPPanelController to read
    
    // Camera Control Methods
    void InitializeCameraSystem()
    {
        // If no cameras are assigned, try to find all cameras in the scene
        if (availableCameras.Count == 0)
        {
            Camera[] sceneCameras = FindObjectsOfType<Camera>();
            Debug.Log($"[Unity] Found {sceneCameras.Length} cameras in scene");
            
            foreach (Camera cam in sceneCameras)
            {
                // Add cameras but skip UI cameras
                if (cam.gameObject.layer != LayerMask.NameToLayer("UI"))
                {
                    availableCameras.Add(cam);
                    Debug.Log($"[Unity] Added camera: {cam.name} to available cameras");
                }
            }
        }
        
        Debug.Log($"[Unity] Total available cameras for control: {availableCameras.Count}");
        
        // Set first camera as active if available
        if (availableCameras.Count > 0)
        {
            SetActiveCamera(0);
        }
        else
        {
            Debug.LogWarning("[Unity] No cameras available for camera control system");
        }
    }
    
    void ProcessCameraControl()
    {
        if (currentData == null || currentData.simulation_data == null || currentData.simulation_data.camera_control == null)
        {
            return;
        }
            
        var cameraData = currentData.simulation_data.camera_control;
        
        // Check if this is a stop command (empty action or empty direction for move/zoom)
        if (string.IsNullOrEmpty(cameraData.camera_action) || 
            (cameraData.camera_action == "move" && string.IsNullOrEmpty(cameraData.direction)) ||
            (cameraData.camera_action == "zoom" && string.IsNullOrEmpty(cameraData.direction)))
        {
            // Stop all movement and zoom
            if (isCameraMoving || isCameraZooming)
            {
                Debug.Log("[Unity] Stopping camera movement/zoom");
                StopCameraMovement();
                StopCameraZoom();
            }
            return;
        }
        
        // Handle camera switching
        if (cameraData.camera_action == "switch" && cameraData.camera_index >= 0)
        {
            Debug.Log($"[Unity] Switching to camera index: {cameraData.camera_index}");
            SetActiveCamera(cameraData.camera_index);
            // Don't affect movement/zoom when switching cameras
        }
        // Handle continuous camera movement
        else if (cameraData.camera_action == "move" && !string.IsNullOrEmpty(cameraData.direction))
        {
            if (!isCameraMoving || currentCameraMovement != cameraData.direction)
            {
                Debug.Log($"[Unity] Starting camera movement: {cameraData.direction}");
                StartCameraMovement(cameraData.direction, cameraData.movement_speed);
                // Stop zoom when starting movement
                if (isCameraZooming)
                {
                    StopCameraZoom();
                }
            }
        }
        // Handle continuous zoom
        else if (cameraData.camera_action == "zoom" && !string.IsNullOrEmpty(cameraData.direction))
        {
            if (!isCameraZooming || currentZoomDirection != cameraData.direction)
            {
                Debug.Log($"[Unity] Starting camera zoom: {cameraData.direction}");
                StartCameraZoom(cameraData.direction, cameraData.zoom_speed);
                // Stop movement when starting zoom
                if (isCameraMoving)
                {
                    StopCameraMovement();
                }
            }
        }
        
        // Apply continuous movement and zoom
        if (isCameraMoving)
        {
            ApplyCameraMovement();
        }
        
        if (isCameraZooming)
        {
            ApplyCameraZoom();
        }
    }
    
    void SetActiveCamera(int index)
    {
        if (availableCameras.Count == 0)
        {
            Debug.LogError("[Unity] No cameras available to switch to!");
            return;
        }
            
        // Ensure index is within bounds
        index = Mathf.Clamp(index, 0, availableCameras.Count - 1);
        currentCameraIndex = index;
        
        Debug.Log($"[Unity] Setting camera {index} as active (total cameras: {availableCameras.Count})");
        
        // Disable all cameras except the selected one
        for (int i = 0; i < availableCameras.Count; i++)
        {
            if (availableCameras[i] != null)
            {
                bool shouldBeActive = (i == index);
                availableCameras[i].enabled = shouldBeActive;
                Debug.Log($"[Unity] Camera {i} ({availableCameras[i].name}) enabled: {shouldBeActive}");
            }
        }
        
        if (availableCameras[index] != null)
        {
            activeCamera = availableCameras[index];
            Debug.Log($"[Unity] Active camera set to: {activeCamera.name}");
        }
        else
        {
            Debug.LogError($"[Unity] Camera at index {index} is null!");
        }
        
        // Update status
        if (cameraStatusText != null)
        {
            cameraStatusText.text = $"Camera {index + 1} Active";
        }
        
        Debug.Log($"[Unity] Successfully switched to Camera {index + 1}");
    }
    
    void StartCameraMovement(string direction, float speedMultiplier)
    {
        isCameraMoving = true;
        currentCameraMovement = direction;
        
        // Store speed multiplier if provided
        if (speedMultiplier > 0)
        {
            // You can use this to adjust movement speed
        }
        
        Debug.Log($"[Unity] Started camera movement: {direction}");
    }
    
    void StopCameraMovement()
    {
        isCameraMoving = false;
        currentCameraMovement = "";
        Debug.Log("[Unity] Stopped camera movement");
    }
    
    void ApplyCameraMovement()
    {
        if (activeCamera == null || string.IsNullOrEmpty(currentCameraMovement))
        {
            return;
        }
            
        Transform camTransform = activeCamera.transform;
        float rotSpeed = cameraRotationSpeed * Time.deltaTime;
        
        switch (currentCameraMovement.ToLower())
        {
            case "up":
                // Rotate camera up (look up) - check current X rotation first
                Vector3 currentEuler = camTransform.eulerAngles;
                float xAngle = currentEuler.x;
                
                // Normalize angle to -180 to 180 range
                if (xAngle > 180) xAngle -= 360;
                
                // Only rotate if within limits
                if (xAngle > -maxLookUpAngle)
                {
                    camTransform.Rotate(-rotSpeed, 0, 0, Space.Self);
                }
                break;
                
            case "down":
                // Rotate camera down (look down) - check current X rotation first
                Vector3 currentEulerDown = camTransform.eulerAngles;
                float xAngleDown = currentEulerDown.x;
                
                // Normalize angle to -180 to 180 range
                if (xAngleDown > 180) xAngleDown -= 360;
                
                // Only rotate if within limits
                if (xAngleDown < maxLookDownAngle)
                {
                    camTransform.Rotate(rotSpeed, 0, 0, Space.Self);
                }
                break;
                
            case "left":
                // Rotate camera left (turn left) - no limits on Y rotation
                camTransform.Rotate(0, -rotSpeed, 0, Space.World);
                break;
                
            case "right":
                // Rotate camera right (turn right) - no limits on Y rotation
                camTransform.Rotate(0, rotSpeed, 0, Space.World);
                break;
        }
    }
    
    void StartCameraZoom(string direction, float speedMultiplier)
    {
        isCameraZooming = true;
        currentZoomDirection = direction;
        
        // Store speed multiplier if provided
        if (speedMultiplier > 0)
        {
            // You can use this to adjust zoom speed
        }
        
        Debug.Log($"[Unity] Started camera zoom: {direction}");
    }
    
    void StopCameraZoom()
    {
        isCameraZooming = false;
        currentZoomDirection = "";
        Debug.Log("[Unity] Stopped camera zoom");
    }
    
    void ApplyCameraZoom()
    {
        if (activeCamera == null || string.IsNullOrEmpty(currentZoomDirection))
        {
            return;
        }
            
        float zoomSpeed = cameraZoomSpeed * Time.deltaTime;
        float currentFOV = activeCamera.fieldOfView;
        float newFOV = currentFOV;
        
        switch (currentZoomDirection.ToLower())
        {
            case "in":
                // Decrease FOV to zoom in
                newFOV = Mathf.Clamp(currentFOV - zoomSpeed, minFOV, maxFOV);
                activeCamera.fieldOfView = newFOV;
                Debug.Log($"[Unity] Zooming IN - FOV: {currentFOV:F1} -> {newFOV:F1}");
                break;
            case "out":
                // Increase FOV to zoom out
                newFOV = Mathf.Clamp(currentFOV + zoomSpeed, minFOV, maxFOV);
                activeCamera.fieldOfView = newFOV;
                Debug.Log($"[Unity] Zooming OUT - FOV: {currentFOV:F1} -> {newFOV:F1}");
                break;
        }
    }
    
    // Method to cycle to next camera
    public void CycleToNextCamera()
    {
        if (availableCameras.Count > 0)
        {
            int nextIndex = (currentCameraIndex + 1) % availableCameras.Count;
            SetActiveCamera(nextIndex);
        }
    }
    
    // Method to cycle to previous camera
    public void CycleToPreviousCamera()
    {
        if (availableCameras.Count > 0)
        {
            int prevIndex = currentCameraIndex - 1;
            if (prevIndex < 0) prevIndex = availableCameras.Count - 1;
            SetActiveCamera(prevIndex);
        }
    }
    
    void OnGUI()
    {
        if (!showDebugGUI) return;
        
        int w = 400;
        int h = 600;  // Increased height to accommodate BOP Panel info
        
        GUI.Box(new Rect(10, 10, w, h), "Simulation Data Receiver");
        
        GUI.color = connected ? Color.green : Color.red;
        GUI.Label(new Rect(20, 30, w-20, 20), "Status: " + connectionStatus);
        GUI.color = Color.white;
        
        if (connected && currentData != null && currentData.simulation_data != null)
        {
            var sim = currentData.simulation_data;
            
            GUI.Label(new Rect(20, 50, w-20, 20), $"TDS Position: {sim.tds_position:F1} ft ({sim.tds_position_normalized:F2})");
            GUI.Label(new Rect(20, 70, w-20, 20), $"TDS Movement: {sim.tds_movement}");
            GUI.Label(new Rect(20, 90, w-20, 20), $"RPM: {sim.rpm:F0}");
            GUI.Label(new Rect(20, 110, w-20, 20), $"Auto-Drill: {(sim.auto_drill ? "ON" : "OFF")}");
            
            GUI.Label(new Rect(20, 140, w-20, 20), "Pumps:");
            GUI.Label(new Rect(30, 160, w-30, 20), $"Pump 1: {(sim.pump_1_active ? "ON" : "OFF")} - {sim.pump_1_spm:F0} SPM");
            GUI.Label(new Rect(30, 180, w-30, 20), $"Pump 2: {(sim.pump_2_active ? "ON" : "OFF")} - {sim.pump_2_spm:F0} SPM");
            GUI.Label(new Rect(30, 200, w-30, 20), $"Pump 3: {(sim.pump_3_active ? "ON" : "OFF")} - {sim.pump_3_spm:F0} SPM");
            GUI.Label(new Rect(30, 220, w-30, 20), $"Total Flow: {sim.total_flow:F1} GPM");
            
            GUI.Label(new Rect(20, 250, w-20, 20), $"Joystick: L({currentData.left_x:F2},{currentData.left_y:F2}) R({currentData.right_x:F2},{currentData.right_y:F2})");
            
            // Camera control info
            GUI.Label(new Rect(20, 280, w-20, 20), "Camera Control:");
            GUI.Label(new Rect(30, 300, w-30, 20), $"Active Camera: {currentCameraIndex + 1} of {availableCameras.Count}");
            
            string cameraStatus = "Ready";
            if (isCameraMoving)
                cameraStatus = $"Rotating: {currentCameraMovement}";
            else if (isCameraZooming)
                cameraStatus = $"Zooming: {currentZoomDirection}";
            
            GUI.Label(new Rect(30, 320, w-30, 20), $"Status: {cameraStatus}");
            
            if (activeCamera != null)
            {
                GUI.Label(new Rect(30, 340, w-30, 20), $"FOV: {activeCamera.fieldOfView:F1}°");
            }
            
            // BOP Panel Status
            GUI.Label(new Rect(20, 370, w-20, 20), "BOP Panel Status:");
            
            // BOP Component States
            if (sim.bop_states != null && sim.bop_states.Count > 0)
            {
                int yOffset = 390;
                int columnWidth = 180;
                int itemHeight = 20;
                int column = 0;
                int row = 0;
                
                foreach (var kvp in sim.bop_states)
                {
                    // Display in two columns for better readability
                    int x = 30 + (column * columnWidth);
                    int y = yOffset + (row * itemHeight);
                    
                    // Color code based on state
                    if (kvp.Value.ToLower().Contains("open"))
                        GUI.color = Color.green;
                    else if (kvp.Value.ToLower().Contains("closed"))
                        GUI.color = Color.red;
                    else if (kvp.Value.ToLower().Contains("transitioning"))
                        GUI.color = Color.yellow;
                    else
                        GUI.color = Color.white;
                    
                    GUI.Label(new Rect(x, y, columnWidth - 10, itemHeight), $"{kvp.Key}: {kvp.Value}");
                    
                    // Move to next position
                    column++;
                    if (column >= 2)
                    {
                        column = 0;
                        row++;
                    }
                }
                
                GUI.color = Color.white;
                yOffset += (row + 1) * itemHeight + 10;
            }
            else
            {
                GUI.Label(new Rect(30, 390, w-30, 20), "No BOP data available");
            }
            
            // BOP Pressures
            int pressureY = 520;
            GUI.Label(new Rect(20, pressureY, w-20, 20), "BOP Pressures:");
            GUI.Label(new Rect(30, pressureY + 20, w-30, 20), $"Annular: {sim.bop_pressures.annular:F1} psi");
            GUI.Label(new Rect(30, pressureY + 40, w-30, 20), $"Manifold: {sim.bop_pressures.manifold:F1} psi");
            GUI.Label(new Rect(30, pressureY + 60, w-30, 20), $"Accumulator: {sim.bop_pressures.accumulator:F1} psi");
            
            // Master Valve Status
            GUI.color = sim.master_valve_open ? Color.green : Color.red;
            GUI.Label(new Rect(200, pressureY + 20, w-200, 20), $"Master Valve: {(sim.master_valve_open ? "OPEN" : "CLOSED")}");
            GUI.color = Color.white;
        }
        else
        {
            GUI.Label(new Rect(20, 70, w-20, 40), "Waiting for data from Python simulator...");
            
#if UNITY_EDITOR || UNITY_STANDALONE
            if (GUI.Button(new Rect(20, 120, 100, 25), "Reconnect"))
            {
                StartCoroutine(Connect());
            }
#endif
        }
    }
    
    // Helper methods for JSON parsing
    string ExtractStringValue(string json, string key)
    {
        string searchKey = "\"" + key + "\":";
        int startIndex = json.IndexOf(searchKey);
        if (startIndex == -1) return "";
        
        startIndex += searchKey.Length;
        // Skip whitespace
        while (startIndex < json.Length && char.IsWhiteSpace(json[startIndex]))
            startIndex++;
        
        if (startIndex >= json.Length || json[startIndex] != '"') return "";
        
        startIndex++; // Skip opening quote
        int endIndex = json.IndexOf('"', startIndex);
        if (endIndex == -1) return "";
        
        return json.Substring(startIndex, endIndex - startIndex);
    }
    
    int ExtractIntValue(string json, string key)
    {
        string searchKey = "\"" + key + "\":";
        int startIndex = json.IndexOf(searchKey);
        if (startIndex == -1) return 0;
        
        startIndex += searchKey.Length;
        // Skip whitespace
        while (startIndex < json.Length && char.IsWhiteSpace(json[startIndex]))
            startIndex++;
        
        if (startIndex >= json.Length) return 0;
        
        // Find the end of the number
        int endIndex = startIndex;
        while (endIndex < json.Length && (char.IsDigit(json[endIndex]) || json[endIndex] == '-'))
            endIndex++;
        
        if (endIndex > startIndex)
        {
            string numStr = json.Substring(startIndex, endIndex - startIndex);
            int result;
            if (int.TryParse(numStr, out result))
                return result;
        }
        
        return 0;
    }
    
    void OnDestroy()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (websocket != null)
        {
            websocket.Close();
            websocket = null;
        }
#endif
    }
}