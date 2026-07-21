using UnityEngine;
using UnityEditor;

/// <summary>
/// Utility script to remove or disable all joystick controllers except the visualizer
/// </summary>
public class JoystickControllerRemover : MonoBehaviour
{
    [Header("Run this once to clean up joystick controllers")]
    public bool removeControllers = false;
    public bool disableControllers = false;
    
    void Start()
    {
        if (removeControllers || disableControllers)
        {
            CleanupJoystickControllers();
        }
    }
    
    public void CleanupJoystickControllers()
    {
        Debug.Log("[JoystickControllerRemover] Starting cleanup...");
        
        // Find and handle RightJoystickController
        var rightControllers = FindObjectsOfType<MonoBehaviour>();
        foreach (var comp in rightControllers)
        {
            if (comp.GetType().Name == "RightJoystickController")
            {
                if (removeControllers)
                {
                    Debug.Log($"[JoystickControllerRemover] Removing RightJoystickController from {comp.gameObject.name}");
                    DestroyImmediate(comp);
                }
                else if (disableControllers)
                {
                    Debug.Log($"[JoystickControllerRemover] Disabling RightJoystickController on {comp.gameObject.name}");
                    comp.enabled = false;
                }
            }
            else if (comp.GetType().Name == "LeftJoystickController")
            {
                if (removeControllers)
                {
                    Debug.Log($"[JoystickControllerRemover] Removing LeftJoystickController from {comp.gameObject.name}");
                    DestroyImmediate(comp);
                }
                else if (disableControllers)
                {
                    Debug.Log($"[JoystickControllerRemover] Disabling LeftJoystickController on {comp.gameObject.name}");
                    comp.enabled = false;
                }
            }
            else if (comp.GetType().Name == "PansimHardwareController")
            {
                if (disableControllers)
                {
                    Debug.Log($"[JoystickControllerRemover] Disabling PansimHardwareController on {comp.gameObject.name}");
                    comp.enabled = false;
                }
            }
        }
        
        // Keep JoystickVisualizer
        var visualizers = FindObjectsOfType<MonoBehaviour>();
        int visualizerCount = 0;
        foreach (var comp in visualizers)
        {
            if (comp.GetType().Name == "JoystickVisualizer")
            {
                visualizerCount++;
                Debug.Log($"[JoystickControllerRemover] Keeping JoystickVisualizer on {comp.gameObject.name}");
            }
        }
        
        Debug.Log($"[JoystickControllerRemover] Cleanup complete. Found {visualizerCount} visualizer(s) to keep.");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(JoystickControllerRemover))]
public class JoystickControllerRemoverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        JoystickControllerRemover remover = (JoystickControllerRemover)target;
        
        GUILayout.Space(10);
        
        if (GUILayout.Button("Disable All Joystick Controllers", GUILayout.Height(30)))
        {
            remover.disableControllers = true;
            remover.removeControllers = false;
            remover.CleanupJoystickControllers();
            remover.disableControllers = false;
        }
        
        if (GUILayout.Button("Remove All Joystick Controllers", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Remove Controllers", 
                "This will permanently remove all joystick controller components (except visualizer). Are you sure?", 
                "Yes", "No"))
            {
                remover.removeControllers = true;
                remover.disableControllers = false;
                remover.CleanupJoystickControllers();
                remover.removeControllers = false;
            }
        }
    }
}
#endif