using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Debug tool to test camera system and verify it's working
/// </summary>
public class CameraDebugger : MonoBehaviour
{
    [Header("Camera System Info")]
    public List<Camera> detectedCameras = new List<Camera>();
    public Camera currentActiveCamera;
    public int activeCameraIndex = -1;
    
    [Header("Manual Test Controls")]
    public bool testSwitchCamera = false;
    public int targetCameraIndex = 0;
    public bool testMoveUp = false;
    public bool testMoveDown = false;
    public bool testRotateLeft = false;
    public bool testRotateRight = false;
    public bool testZoomIn = false;
    public bool testZoomOut = false;
    
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotateSpeed = 50f;
    public float zoomSpeed = 20f;
    
    [Header("Debug Display")]
    public bool showDebugInfo = true;
    public bool showConsoleMessages = true;
    
    private float originalFOV;
    
    void Start()
    {
        Debug.Log("[CameraDebugger] Starting camera system debug...");
        DetectCameras();
    }
    
    void DetectCameras()
    {
        detectedCameras.Clear();
        Camera[] allCameras = FindObjectsOfType<Camera>();
        
        foreach (Camera cam in allCameras)
        {
            if (cam.gameObject.layer != LayerMask.NameToLayer("UI"))
            {
                detectedCameras.Add(cam);
                Debug.Log($"[CameraDebugger] Found camera: {cam.name} at position {cam.transform.position}");
            }
        }
        
        Debug.Log($"[CameraDebugger] Total cameras detected: {detectedCameras.Count}");
        
        // Find active camera
        for (int i = 0; i < detectedCameras.Count; i++)
        {
            if (detectedCameras[i].enabled)
            {
                currentActiveCamera = detectedCameras[i];
                activeCameraIndex = i;
                originalFOV = currentActiveCamera.fieldOfView;
                Debug.Log($"[CameraDebugger] Active camera: {currentActiveCamera.name} (index {i})");
                break;
            }
        }
        
        if (currentActiveCamera == null && detectedCameras.Count > 0)
        {
            // No camera is active, activate the first one
            detectedCameras[0].enabled = true;
            currentActiveCamera = detectedCameras[0];
            activeCameraIndex = 0;
            originalFOV = currentActiveCamera.fieldOfView;
            Debug.Log($"[CameraDebugger] Activated first camera: {currentActiveCamera.name}");
        }
    }
    
    void Update()
    {
        // Test camera switching
        if (testSwitchCamera)
        {
            testSwitchCamera = false;
            SwitchToCamera(targetCameraIndex);
        }
        
        // Test movement
        if (currentActiveCamera != null)
        {
            Transform cam = currentActiveCamera.transform;
            float deltaMove = moveSpeed * Time.deltaTime;
            float deltaRotate = rotateSpeed * Time.deltaTime;
            float deltaZoom = zoomSpeed * Time.deltaTime;
            
            if (testMoveUp)
            {
                cam.position += Vector3.up * deltaMove;
                if (showConsoleMessages) Debug.Log($"Moving UP: {cam.position}");
            }
            if (testMoveDown)
            {
                cam.position += Vector3.down * deltaMove;
                if (showConsoleMessages) Debug.Log($"Moving DOWN: {cam.position}");
            }
            if (testRotateLeft)
            {
                cam.Rotate(0, -deltaRotate, 0, Space.World);
                if (showConsoleMessages) Debug.Log($"Rotating LEFT: {cam.eulerAngles}");
            }
            if (testRotateRight)
            {
                cam.Rotate(0, deltaRotate, 0, Space.World);
                if (showConsoleMessages) Debug.Log($"Rotating RIGHT: {cam.eulerAngles}");
            }
            if (testZoomIn)
            {
                currentActiveCamera.fieldOfView = Mathf.Clamp(currentActiveCamera.fieldOfView - deltaZoom, 20f, 90f);
                if (showConsoleMessages) Debug.Log($"Zooming IN: FOV = {currentActiveCamera.fieldOfView}");
            }
            if (testZoomOut)
            {
                currentActiveCamera.fieldOfView = Mathf.Clamp(currentActiveCamera.fieldOfView + deltaZoom, 20f, 90f);
                if (showConsoleMessages) Debug.Log($"Zooming OUT: FOV = {currentActiveCamera.fieldOfView}");
            }
        }
    }
    
    void SwitchToCamera(int index)
    {
        if (detectedCameras.Count == 0)
        {
            Debug.LogError("[CameraDebugger] No cameras available!");
            return;
        }
        
        index = Mathf.Clamp(index, 0, detectedCameras.Count - 1);
        
        Debug.Log($"[CameraDebugger] Switching from camera {activeCameraIndex} to {index}");
        
        // Disable all cameras
        for (int i = 0; i < detectedCameras.Count; i++)
        {
            detectedCameras[i].enabled = false;
        }
        
        // Enable target camera
        detectedCameras[index].enabled = true;
        currentActiveCamera = detectedCameras[index];
        activeCameraIndex = index;
        
        Debug.Log($"[CameraDebugger] Now active: {currentActiveCamera.name}");
    }
    
    void OnGUI()
    {
        if (!showDebugInfo) return;
        
        // Create debug window
        int width = 400;
        int height = 300;
        GUI.Box(new Rect(10, 10, width, height), "Camera System Debug");
        
        int y = 30;
        GUI.Label(new Rect(20, y, width - 40, 20), $"Detected Cameras: {detectedCameras.Count}");
        y += 20;
        
        for (int i = 0; i < detectedCameras.Count && i < 5; i++)
        {
            if (detectedCameras[i] != null)
            {
                string status = (i == activeCameraIndex) ? " [ACTIVE]" : "";
                GUI.Label(new Rect(30, y, width - 50, 20), $"{i}: {detectedCameras[i].name}{status}");
                y += 20;
            }
        }
        
        y += 10;
        GUI.Label(new Rect(20, y, width - 40, 20), "Current Camera Info:");
        y += 20;
        
        if (currentActiveCamera != null)
        {
            GUI.Label(new Rect(30, y, width - 50, 20), $"Name: {currentActiveCamera.name}");
            y += 20;
            GUI.Label(new Rect(30, y, width - 50, 20), $"Position: {currentActiveCamera.transform.position}");
            y += 20;
            GUI.Label(new Rect(30, y, width - 50, 20), $"Rotation: {currentActiveCamera.transform.eulerAngles}");
            y += 20;
            GUI.Label(new Rect(30, y, width - 50, 20), $"FOV: {currentActiveCamera.fieldOfView:F1}");
            y += 20;
        }
        else
        {
            GUI.Label(new Rect(30, y, width - 50, 20), "No active camera!");
            y += 20;
        }
        
        // Quick test buttons
        y += 10;
        GUI.Label(new Rect(20, y, width - 40, 20), "Quick Tests:");
        y += 25;
        
        if (GUI.Button(new Rect(20, y, 80, 25), "Camera 1"))
        {
            SwitchToCamera(0);
        }
        if (GUI.Button(new Rect(105, y, 80, 25), "Camera 2"))
        {
            SwitchToCamera(1);
        }
        if (GUI.Button(new Rect(190, y, 80, 25), "Camera 3"))
        {
            SwitchToCamera(2);
        }
        
        y += 30;
        if (GUI.Button(new Rect(20, y, 100, 25), "Refresh Cameras"))
        {
            DetectCameras();
        }
    }
}