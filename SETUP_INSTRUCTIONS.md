# Unity Setup Instructions for Pansim Integration

## Important: Enable Background Running

For Unity to receive data from Pansim while the Driller Screen is in focus, Unity must be configured to run in background.

### Method 1: Automatic (Recommended)
1. Add `ForceBackgroundRun.cs` script to any GameObject in your scene
2. This script will automatically enable background running

### Method 2: Manual Unity Settings
1. Go to **Edit > Project Settings > Player**
2. Under **Resolution and Presentation**
3. Check **"Run In Background"** option
4. This ensures Unity continues processing when it loses focus

### Method 3: Build Settings (For Standalone Builds)
1. Go to **File > Build Settings**
2. Click **Player Settings**
3. Under **Resolution and Presentation**
4. Enable **"Run In Background"**

## Setting Up the WebSocket Connection

1. Add `SimulationDataReceiver.cs` to a GameObject in your scene
2. Set the server address to `ws://localhost:8766`
3. Assign your Top Drive object to the `topDriveObject` field
4. Configure height limits (minHeight, maxHeight) for your scene scale

## Visualizing Joystick Movement

1. Add `JoystickVisualizer.cs` to your joystick model
2. Assign the joystick stick/handle to the `joystickStick` field
3. Set `joystickType` to "Left" for TDS control
4. The joystick will automatically animate based on control inputs

## Testing the Connection

1. Start the Pansim application first
2. Launch Unity (Play mode or build)
3. Check the Unity Console for connection messages
4. You should see:
   - "[Unity] Connected to simulator bridge"
   - "[ForceBackgroundRun] Unity will now run in background"

## Troubleshooting

### Unity doesn't receive updates when not focused:
- Ensure `Application.runInBackground = true` is set
- Check that ForceBackgroundRun script is in the scene
- Verify in Player Settings that "Run In Background" is enabled

### Data shows as zero:
- Check that Pansim is running BEFORE Unity
- Verify port 8766 is not blocked by firewall
- Look for error messages in Unity Console

### Connection drops when switching windows:
- This is fixed by enabling background running
- The WebSocket will maintain connection even when Unity loses focus

## Important Notes

- Unity MUST run in background mode for real-time updates
- The WebSocket connection uses port 8766 (different from hardware port 8765)
- Data is sent every 100ms from Python to Unity
- All scripts have Application.runInBackground = true to ensure continuous operation