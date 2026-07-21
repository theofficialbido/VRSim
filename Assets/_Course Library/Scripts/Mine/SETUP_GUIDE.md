# Pansim Unity Hardware Control Setup Guide

## Overview
This guide explains how to set up and use the Pansim hardware control system in Unity for joystick integration.

## Prerequisites

1. **Unity Version**: Unity 2021.3 or later
2. **NativeWebSocket Package**: Required for WebSocket communication
3. **Python Server**: Pansim Python application running with joystick broadcaster

## Installation Steps

### 1. Install NativeWebSocket Package

1. Open Unity Package Manager (Window → Package Manager)
2. Click '+' → 'Add package from git URL'
3. Enter: `https://github.com/endel/NativeWebSocket.git#upm`
4. Click 'Add'

### 2. Import Scripts

Copy all scripts from `Pansim/Unity/Scripts/` to your Unity project:
```
Assets/_Course Library/Scripts/Mine/
├── PansimHardwareController.cs    # Core WebSocket controller
├── LeftJoystickController.cs      # Left joystick control component
├── RightJoystickController.cs     # Right joystick/camera control
├── HardwareManager.cs             # Unified hardware interface
└── PansimControlExample.cs        # Example implementation
```

### 3. Basic Setup

#### Step 1: Create Hardware Controller
1. Create an empty GameObject: `GameObject → Create Empty`
2. Name it "HardwareController"
3. Add component: `PansimHardwareController`
4. Configure settings:
   - Server URL: `ws://localhost:8765`
   - Auto Connect: ✓
   - Auto Reconnect: ✓
   - Enable Debug UI: ✓

#### Step 2: Add Hardware Manager
1. Create another empty GameObject: "HardwareManager"
2. Add component: `HardwareManager`
3. Configure:
   - Auto Initialize: ✓
   - Show Debug Panel: ✓

#### Step 3: Setup Object Controls
1. For Top Drive control:
   - Add `LeftJoystickController` to your Top Drive object
   - Configure:
     - Movement Mode: Translate
     - Move Axis: (0, 1, 0) for Y-axis
     - Move Speed: 5
     - Use Limits: ✓
     - Min Limit: 0
     - Max Limit: 20

2. For Camera control:
   - Add `RightJoystickController` to your Camera object
   - Configure:
     - Enable Rotation: ✓
     - Rotation Speed: 50
     - Enable Zoom: ✓
     - Zoom Mode: FieldOfView
     - Min Zoom: 20
     - Max Zoom: 90

## Usage

### Method 1: Using Component Controllers

```csharp
// The controllers work automatically once configured
// No code required - just add components and configure in Inspector
```

### Method 2: Using Hardware Manager API

```csharp
using Pansim.Controllers;

public class MyController : MonoBehaviour
{
    private HardwareManager hardware;
    
    void Start()
    {
        hardware = HardwareManager.Instance;
    }
    
    void Update()
    {
        if (hardware.IsConnected())
        {
            // Get joystick values
            float leftY = hardware.GetAxis("vertical");
            Vector2 rightJoystick = hardware.GetRightJoystick();
            
            // Check buttons
            if (hardware.GetButton(0))
            {
                // Button 0 is pressed
            }
        }
    }
}
```

### Method 3: Direct Hardware Controller Access

```csharp
using Pansim.Hardware;

public class DirectControl : MonoBehaviour
{
    void Start()
    {
        var controller = PansimHardwareController.Instance;
        controller.OnDataReceived += OnDataReceived;
    }
    
    void OnDataReceived(HardwareData data)
    {
        // Process raw joystick data
        float leftX = data.left_x;
        float leftY = data.left_y;
        // etc...
    }
}
```

## Joystick Mapping

### Standard Mapping
- **Left Joystick**:
  - X-axis: Horizontal movement/rotation
  - Y-axis: Vertical movement (Top Drive)

- **Right Joystick**:
  - X-axis: Camera rotation
  - Y-axis: Camera zoom

### Unity Input Axes
- Axis 0: Left Joystick X
- Axis 1: Left Joystick Y
- Axis 2: Right Joystick X
- Axis 3: Right Joystick Y

## Testing

### 1. Start Python Server
```bash
cd C:\Users\user\PycharmProjects\Pansim
python run_pansim.py
```

Look for: "Dual Joystick Broadcaster started on port 8765"

### 2. In Unity
1. Enter Play Mode
2. Check debug panel (top-left corner)
3. Status should show "Connected" in green
4. Move joysticks to see values update

### 3. Verify Controls
- Move left joystick up/down → Top Drive moves
- Move right joystick left/right → Camera rotates
- Move right joystick up/down → Camera zooms

## Troubleshooting

### Connection Issues

**Problem**: "Not Connected" status
**Solutions**:
1. Ensure Python server is running first
2. Check Windows Firewall for port 8765
3. Try using `ws://127.0.0.1:8765` instead of localhost
4. Check Unity console for error messages

**Problem**: Joysticks not detected
**Solutions**:
1. Check USB connections
2. Verify joysticks work in Windows Game Controllers
3. Restart Python server after connecting joysticks

### Input Issues

**Problem**: Reversed controls
**Solutions**:
1. Toggle "Invert" options in controller components
2. Swap USB ports and restart Python server

**Problem**: Jittery movement
**Solutions**:
1. Increase Dead Zone value (default 0.1)
2. Increase Smoothing value (default 0.1)
3. Reduce movement speed values

### Performance Issues

**Problem**: Lag or delayed response
**Solutions**:
1. Check network latency
2. Reduce verbose logging
3. Disable debug UI panels
4. Ensure Python server isn't overloaded

## Advanced Configuration

### Custom Input Mappings

In HardwareManager, configure Input Mappings:
1. Add new mapping in Inspector
2. Set Action Name (e.g., "DrillRotation")
3. Choose Input Type (e.g., LeftJoystickX)
4. Set Sensitivity and Invert as needed
5. Add UnityEvent listeners for actions

### Event-Driven Programming

```csharp
void Start()
{
    var hardware = HardwareManager.Instance;
    
    // Subscribe to events
    hardware.OnHardwareConnected.AddListener(() => {
        Debug.Log("Ready to control!");
    });
    
    hardware.OnLeftJoystickVertical.AddListener(value => {
        // React to left joystick Y movement
        MoveTopDrive(value);
    });
    
    hardware.OnButtonPressed.AddListener(buttonIndex => {
        HandleButton(buttonIndex);
    });
}
```

### WebSocket Configuration

Modify server URL for different setups:
- Local: `ws://localhost:8765`
- Network: `ws://192.168.1.100:8765`
- Docker: `ws://host.docker.internal:8765`

## Best Practices

1. **Always check connection status** before processing input
2. **Use dead zones** to prevent drift
3. **Implement safety limits** for all movements
4. **Add emergency stop** functionality
5. **Log important events** for debugging
6. **Test with keyboard fallback** during development

## Support

For issues or questions:
1. Check Unity console for detailed error messages
2. Enable verbose logging in components
3. Review Python server logs
4. Verify hardware connections

## Version Compatibility

- Scripts Version: 1.0.0
- Compatible with Pansim Python: 1.0+
- Requires NativeWebSocket: 1.1.0+
- Unity: 2021.3 LTS or later