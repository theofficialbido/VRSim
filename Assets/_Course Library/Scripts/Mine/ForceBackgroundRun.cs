using UnityEngine;

/// <summary>
/// Forces Unity to continue running and processing when it loses focus
/// This is essential for real-time communication with external applications
/// </summary>
[DefaultExecutionOrder(-1000)] // Execute before other scripts
public class ForceBackgroundRun : MonoBehaviour
{
    private static bool isInitialized = false;
    
    void Awake()
    {
        if (!isInitialized)
        {
            // Force Unity to run in background
            Application.runInBackground = true;
            
            // Make this persistent across scene loads
            DontDestroyOnLoad(gameObject);
            
            isInitialized = true;
            
            Debug.Log("[ForceBackgroundRun] Unity will now run in background");
            Debug.Log($"[ForceBackgroundRun] runInBackground = {Application.runInBackground}");
        }
        else
        {
            // Destroy duplicate instances
            Destroy(gameObject);
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        // Ensure it stays enabled even after focus changes
        if (!hasFocus)
        {
            Application.runInBackground = true;
            Debug.Log("[ForceBackgroundRun] Lost focus - ensuring background running");
        }
        else
        {
            Debug.Log("[ForceBackgroundRun] Gained focus");
        }
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log("[ForceBackgroundRun] Application paused - keeping background running");
            Application.runInBackground = true;
        }
    }
}