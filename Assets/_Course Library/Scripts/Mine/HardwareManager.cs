using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Pansim.Hardware;

namespace Pansim.Controllers
{
    /// <summary>
    /// Manages all hardware inputs and provides unified control interface
    /// </summary>
    public class HardwareManager : MonoBehaviour
    {
        private static HardwareManager _instance;
        public static HardwareManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<HardwareManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("HardwareManager");
                        _instance = go.AddComponent<HardwareManager>();
                    }
                }
                return _instance;
            }
        }

        [Header("Hardware Configuration")]
        [SerializeField] private bool autoInitialize = true;
        [SerializeField] private float connectionCheckInterval = 1f;

        [Header("Input Mapping")]
        [SerializeField] private InputMapping[] inputMappings;

        [Header("Global Settings")]
        [SerializeField] private float globalDeadZone = 0.1f;
        [SerializeField] private bool enableVibration = true;
        [SerializeField] private bool enableAudioFeedback = true;

        [Header("Events")]
        public UnityEvent OnHardwareConnected;
        public UnityEvent OnHardwareDisconnected;
        public UnityEvent<float> OnLeftJoystickVertical;
        public UnityEvent<float> OnLeftJoystickHorizontal;
        public UnityEvent<float> OnRightJoystickVertical;
        public UnityEvent<float> OnRightJoystickHorizontal;
        public UnityEvent<int> OnButtonPressed;
        public UnityEvent<int> OnButtonReleased;

        [Header("Debug")]
        [SerializeField] private bool showDebugPanel = true;
        [SerializeField] private bool logInputEvents = false;

        // Component references
        private PansimHardwareController hardwareController;
        private Dictionary<string, InputAction> actionMap;
        private bool wasConnected = false;
        private float lastConnectionCheck;

        // Input state tracking
        private HardwareData previousData;
        private float[] axisValues = new float[4];
        private bool[] buttonStates = new bool[32];

        [Serializable]
        public class InputMapping
        {
            public string actionName;
            public InputType inputType;
            public int inputIndex;
            public float sensitivity = 1f;
            public bool invert = false;
            public UnityEvent<float> onValueChanged;
        }

        [Serializable]
        public class InputAction
        {
            public string name;
            public float value;
            public bool isActive;
            public Action<float> callback;
        }

        public enum InputType
        {
            LeftJoystickX,
            LeftJoystickY,
            RightJoystickX,
            RightJoystickY,
            Button
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeActionMap();
        }

        private void Start()
        {
            if (autoInitialize)
            {
                Initialize();
            }
        }

        public void Initialize()
        {
            // Get hardware controller
            hardwareController = PansimHardwareController.Instance;

            if (hardwareController == null)
            {
                Debug.LogError("[HardwareManager] PansimHardwareController not found!");
                return;
            }

            // Subscribe to hardware events
            hardwareController.OnConnectionStateChanged += OnConnectionStateChanged;
            hardwareController.OnDataReceived += OnDataReceived;
            hardwareController.OnError += OnHardwareError;

            // Initialize previous data
            previousData = new HardwareData();

            Debug.Log("[HardwareManager] Initialized successfully");
        }

        private void InitializeActionMap()
        {
            actionMap = new Dictionary<string, InputAction>();

            if (inputMappings != null)
            {
                foreach (var mapping in inputMappings)
                {
                    if (!string.IsNullOrEmpty(mapping.actionName))
                    {
                        actionMap[mapping.actionName] = new InputAction
                        {
                            name = mapping.actionName,
                            value = 0f,
                            isActive = false
                        };
                    }
                }
            }
        }

        private void Update()
        {
            // Periodic connection check
            if (Time.time - lastConnectionCheck > connectionCheckInterval)
            {
                lastConnectionCheck = Time.time;
                CheckConnectionStatus();
            }

            // Process input mappings
            ProcessInputMappings();
        }

        private void CheckConnectionStatus()
        {
            if (hardwareController == null) return;

            bool isConnected = hardwareController.IsConnected();

            if (isConnected != wasConnected)
            {
                wasConnected = isConnected;

                if (isConnected)
                {
                    OnHardwareConnected?.Invoke();
                    Debug.Log("[HardwareManager] Hardware connected");
                }
                else
                {
                    OnHardwareDisconnected?.Invoke();
                    Debug.Log("[HardwareManager] Hardware disconnected");
                }
            }
        }

        private void OnConnectionStateChanged(PansimHardwareController.ConnectionState state)
        {
            if (logInputEvents)
            {
                Debug.Log($"[HardwareManager] Connection state changed: {state}");
            }
        }

        private void OnDataReceived(HardwareData data)
        {
            // Update axis values
            axisValues[0] = ApplyDeadZone(data.left_x);
            axisValues[1] = ApplyDeadZone(data.left_y);
            axisValues[2] = ApplyDeadZone(data.right_x);
            axisValues[3] = ApplyDeadZone(data.right_y);

            // Fire axis events
            if (Mathf.Abs(data.left_x - previousData.left_x) > 0.01f)
                OnLeftJoystickHorizontal?.Invoke(axisValues[0]);

            if (Mathf.Abs(data.left_y - previousData.left_y) > 0.01f)
                OnLeftJoystickVertical?.Invoke(axisValues[1]);

            if (Mathf.Abs(data.right_x - previousData.right_x) > 0.01f)
                OnRightJoystickHorizontal?.Invoke(axisValues[2]);

            if (Mathf.Abs(data.right_y - previousData.right_y) > 0.01f)
                OnRightJoystickVertical?.Invoke(axisValues[3]);

            // Process buttons
            ProcessButtonStates(data.button_states);

            // Store for next frame
            previousData = data;

            if (logInputEvents && data.HasInput())
            {
                Debug.Log($"[HardwareManager] Input: L({data.left_x:F2},{data.left_y:F2}) R({data.right_x:F2},{data.right_y:F2}) B:{data.button_states}");
            }
        }

        private void ProcessButtonStates(int buttonData)
        {
            for (int i = 0; i < 32; i++)
            {
                bool isPressed = (buttonData & (1 << i)) != 0;
                bool wasPressed = buttonStates[i];

                if (isPressed && !wasPressed)
                {
                    OnButtonPressed?.Invoke(i);
                    if (logInputEvents)
                        Debug.Log($"[HardwareManager] Button {i} pressed");
                }
                else if (!isPressed && wasPressed)
                {
                    OnButtonReleased?.Invoke(i);
                    if (logInputEvents)
                        Debug.Log($"[HardwareManager] Button {i} released");
                }

                buttonStates[i] = isPressed;
            }
        }

        private void ProcessInputMappings()
        {
            if (inputMappings == null || hardwareController == null || !hardwareController.IsConnected())
                return;

            foreach (var mapping in inputMappings)
            {
                float value = 0f;

                switch (mapping.inputType)
                {
                    case InputType.LeftJoystickX:
                        value = axisValues[0];
                        break;
                    case InputType.LeftJoystickY:
                        value = axisValues[1];
                        break;
                    case InputType.RightJoystickX:
                        value = axisValues[2];
                        break;
                    case InputType.RightJoystickY:
                        value = axisValues[3];
                        break;
                    case InputType.Button:
                        value = buttonStates[mapping.inputIndex] ? 1f : 0f;
                        break;
                }

                // Apply modifiers
                if (mapping.invert)
                    value = -value;
                
                value *= mapping.sensitivity;

                // Update action map
                if (actionMap.ContainsKey(mapping.actionName))
                {
                    var action = actionMap[mapping.actionName];
                    if (Mathf.Abs(action.value - value) > 0.01f)
                    {
                        action.value = value;
                        action.isActive = Mathf.Abs(value) > 0.01f;
                        mapping.onValueChanged?.Invoke(value);
                    }
                }
            }
        }

        private float ApplyDeadZone(float value)
        {
            if (Mathf.Abs(value) < globalDeadZone)
                return 0f;

            // Rescale to remove deadzone gap
            float sign = Mathf.Sign(value);
            float magnitude = Mathf.Abs(value);
            return sign * ((magnitude - globalDeadZone) / (1f - globalDeadZone));
        }

        private void OnHardwareError(string error)
        {
            Debug.LogError($"[HardwareManager] Hardware error: {error}");
        }

        // Public API Methods
        public float GetAxis(string axisName)
        {
            if (actionMap.ContainsKey(axisName))
                return actionMap[axisName].value;

            // Fallback to direct axis access
            switch (axisName.ToLower())
            {
                case "leftx":
                case "left x":
                case "horizontal":
                    return axisValues[0];
                case "lefty":
                case "left y":
                case "vertical":
                    return axisValues[1];
                case "rightx":
                case "right x":
                case "look horizontal":
                    return axisValues[2];
                case "righty":
                case "right y":
                case "look vertical":
                    return axisValues[3];
                default:
                    return 0f;
            }
        }

        public bool GetButton(int buttonIndex)
        {
            if (buttonIndex < 0 || buttonIndex >= buttonStates.Length)
                return false;
            return buttonStates[buttonIndex];
        }

        public bool GetButtonDown(int buttonIndex)
        {
            // This would need frame tracking for proper implementation
            return GetButton(buttonIndex);
        }

        public bool GetButtonUp(int buttonIndex)
        {
            // This would need frame tracking for proper implementation
            return !GetButton(buttonIndex);
        }

        public Vector2 GetLeftJoystick()
        {
            return new Vector2(axisValues[0], axisValues[1]);
        }

        public Vector2 GetRightJoystick()
        {
            return new Vector2(axisValues[2], axisValues[3]);
        }

        public bool IsConnected()
        {
            return hardwareController != null && hardwareController.IsConnected();
        }

        public void Vibrate(float intensity, float duration)
        {
            if (!enableVibration || hardwareController == null)
                return;

            // Implement vibration logic here if hardware supports it
            Debug.Log($"[HardwareManager] Vibrate: {intensity} for {duration}s");
        }

        public void RegisterAction(string actionName, Action<float> callback)
        {
            if (!actionMap.ContainsKey(actionName))
            {
                actionMap[actionName] = new InputAction
                {
                    name = actionName,
                    value = 0f,
                    isActive = false
                };
            }
            actionMap[actionName].callback = callback;
        }

        public void UnregisterAction(string actionName)
        {
            if (actionMap.ContainsKey(actionName))
            {
                actionMap[actionName].callback = null;
            }
        }

        // Debug UI
        private void OnGUI()
        {
            if (!showDebugPanel) return;

            DrawManagerDebugPanel();
        }

        private void DrawManagerDebugPanel()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 310, 10, 300, 400));
            GUI.Box(new Rect(0, 0, 300, 400), "");

            GUILayout.Label("HARDWARE MANAGER", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });
            GUILayout.Space(5);

            // Connection status
            GUI.color = IsConnected() ? Color.green : Color.red;
            GUILayout.Label($"Status: {(IsConnected() ? "Connected" : "Disconnected")}");
            GUI.color = Color.white;

            // Joystick values
            GUILayout.Space(10);
            GUILayout.Label("JOYSTICK VALUES:");
            DrawAxisBar("Left X", axisValues[0]);
            DrawAxisBar("Left Y", axisValues[1]);
            DrawAxisBar("Right X", axisValues[2]);
            DrawAxisBar("Right Y", axisValues[3]);

            // Button states
            GUILayout.Space(10);
            GUILayout.Label("BUTTON STATES:");
            GUILayout.BeginHorizontal();
            for (int i = 0; i < 8; i++)
            {
                GUI.color = buttonStates[i] ? Color.green : Color.gray;
                GUILayout.Label(i.ToString(), GUILayout.Width(30));
            }
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            // Active actions
            if (actionMap.Count > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label("ACTIVE ACTIONS:");
                foreach (var action in actionMap.Values)
                {
                    if (action.isActive)
                    {
                        GUILayout.Label($"{action.name}: {action.value:F2}");
                    }
                }
            }

            // Input mappings
            if (inputMappings != null && inputMappings.Length > 0)
            {
                GUILayout.Space(10);
                GUILayout.Label($"MAPPINGS: {inputMappings.Length} configured");
            }

            // Controls
            GUILayout.Space(10);
            if (GUILayout.Button("Toggle Logging"))
            {
                logInputEvents = !logInputEvents;
            }

            if (hardwareController != null && !IsConnected())
            {
                if (GUILayout.Button("Reconnect Hardware"))
                {
                    hardwareController.StartConnection();
                }
            }

            GUILayout.EndArea();
        }

        private void DrawAxisBar(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(50));
            
            Rect barRect = GUILayoutUtility.GetRect(150, 15);
            GUI.Box(barRect, "");
            
            if (Mathf.Abs(value) > 0.01f)
            {
                float barWidth = Mathf.Abs(value) * 75;
                float barX = value > 0 ? barRect.x + 75 : barRect.x + 75 - barWidth;
                GUI.color = value > 0 ? Color.green : Color.red;
                GUI.Box(new Rect(barX, barRect.y, barWidth, 15), "");
                GUI.color = Color.white;
            }
            
            GUI.color = Color.gray;
            GUI.Box(new Rect(barRect.x + 75, barRect.y, 1, 15), "");
            GUI.color = Color.white;
            
            GUILayout.Label($"{value:F2}", GUILayout.Width(40));
            GUILayout.EndHorizontal();
        }

        private void OnDestroy()
        {
            if (hardwareController != null)
            {
                hardwareController.OnConnectionStateChanged -= OnConnectionStateChanged;
                hardwareController.OnDataReceived -= OnDataReceived;
                hardwareController.OnError -= OnHardwareError;
            }
        }
    }
}