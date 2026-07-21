using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Diagnostic tool to check BOP light components and their states
/// </summary>
public class BOPLightDiagnostic : MonoBehaviour
{
    private BOPPanelController bopController;
    
    void Start()
    {
        bopController = GetComponent<BOPPanelController>();
        if (bopController == null)
        {
            bopController = FindObjectOfType<BOPPanelController>();
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Screen.width - 410, 10, 400, 600));
        GUILayout.Box("BOP Light Diagnostic");
        
        if (bopController == null)
        {
            GUILayout.Label("BOP Controller not found!");
            GUILayout.EndArea();
            return;
        }
        
        // Check each light
        CheckLight("Annular Green", bopController.annularGreenLight);
        CheckLight("Annular Red", bopController.annularRedLight);
        CheckLight("Pipe Ram Green", bopController.pipeRamGreenLight);
        CheckLight("Pipe Ram Red", bopController.pipeRamRedLight);
        CheckLight("Blind Ram Green", bopController.blindRamGreenLight);
        CheckLight("Blind Ram Red", bopController.blindRamRedLight);
        CheckLight("Kill Line Green", bopController.killLineGreenLight);
        CheckLight("Kill Line Red", bopController.killLineRedLight);
        CheckLight("Choke Line Green", bopController.chokeLineGreenLight);
        CheckLight("Choke Line Red", bopController.chokeLineRedLight);
        
        GUILayout.Space(10);
        
        // Manual light test buttons
        if (GUILayout.Button("Test All Green Lights ON"))
        {
            SetAllGreenLights(true);
        }
        
        if (GUILayout.Button("Test All Green Lights OFF"))
        {
            SetAllGreenLights(false);
        }
        
        if (GUILayout.Button("Force Update BOP States"))
        {
            if (bopController != null)
            {
                bopController.ForceUpdate();
            }
        }
        
        GUILayout.EndArea();
    }
    
    void CheckLight(string name, GameObject lightObj)
    {
        if (lightObj == null)
        {
            GUILayout.Label($"{name}: NULL - NOT ASSIGNED!");
            return;
        }
        
        string status = $"{name}: ";
        
        // Check GameObject active state
        status += lightObj.activeSelf ? "GO:ON " : "GO:OFF ";
        
        // Check Light component
        Light light = lightObj.GetComponent<Light>();
        if (light != null)
        {
            status += light.enabled ? "Light:ON " : "Light:OFF ";
            status += $"Int:{light.intensity:F1} ";
            status += $"Col:{light.color} ";
        }
        else
        {
            // Check for Light in children
            Light[] childLights = lightObj.GetComponentsInChildren<Light>(true);
            if (childLights.Length > 0)
            {
                status += $"ChildLights:{childLights.Length} ";
                foreach (var cl in childLights)
                {
                    status += cl.enabled ? "ON " : "OFF ";
                }
            }
            else
            {
                status += "NO LIGHT COMPONENT! ";
            }
        }
        
        // Check Renderer
        Renderer renderer = lightObj.GetComponent<Renderer>();
        if (renderer != null)
        {
            status += renderer.enabled ? "Rend:ON " : "Rend:OFF ";
        }
        
        GUILayout.Label(status);
    }
    
    void SetAllGreenLights(bool on)
    {
        if (bopController == null) return;
        
        SetLightDirectly(bopController.annularGreenLight, on);
        SetLightDirectly(bopController.pipeRamGreenLight, on);
        SetLightDirectly(bopController.blindRamGreenLight, on);
        SetLightDirectly(bopController.shearRamGreenLight, on);
        SetLightDirectly(bopController.killLineGreenLight, on);
        SetLightDirectly(bopController.chokeLineGreenLight, on);
    }
    
    void SetLightDirectly(GameObject lightObj, bool on)
    {
        if (lightObj == null) return;
        
        // Activate GameObject
        lightObj.SetActive(on);
        
        // Enable Light component with high intensity
        Light light = lightObj.GetComponent<Light>();
        if (light != null)
        {
            light.enabled = on;
            if (on)
            {
                light.intensity = 10f; // Very bright for testing
                light.range = 20f;
                if (lightObj.name.Contains("Green"))
                    light.color = Color.green;
                else if (lightObj.name.Contains("Red"))
                    light.color = Color.red;
            }
        }
        
        // Also check children
        Light[] childLights = lightObj.GetComponentsInChildren<Light>(true);
        foreach (var childLight in childLights)
        {
            childLight.enabled = on;
            if (on)
            {
                childLight.intensity = 10f;
                childLight.range = 20f;
            }
        }
        
        Debug.Log($"[Diagnostic] Set {lightObj.name} to {on}");
    }
}