using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NativeWebSocket;

namespace Pansim.Hardware
{
    /// <summary>
    /// Main hardware controller for Pansim - Manages WebSocket connection and data distribution
    /// </summary>
    public class PansimHardwareController : MonoBehaviour
    {
        private static PansimHardwareController _instance;
        public static PansimHardwareController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<PansimHardwareController>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("PansimHardwareController");
                        _instance = go.AddComponent<PansimHardwareController>();
                    }
                }
                return _instance;
            }
        }

        [Header("WebSocket Configuration")]
        [SerializeField] private string serverUrl = "ws://localhost:8766";
        [SerializeField] private float reconnectDelay = 3f;
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private bool autoReconnect = true;
        [SerializeField] private float connectionTimeout = 5f;

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugUI = true;
        [SerializeField] private bool verboseLogging = false;

        // WebSocket instance
        private WebSocket websocket;

        // Connection state
        public enum ConnectionState
        {
            Disconnected,
            Connecting,
            Connected,
            Error,
            Reconnecting
        }

        private ConnectionState _connectionState = ConnectionState.Disconnected;
        public ConnectionState CurrentState => _connectionState;

        // Hardware data
        private HardwareData currentData = new HardwareData();
        public HardwareData CurrentData => currentData;

        // Events
        public event Action<ConnectionState> OnConnectionStateChanged;
        public event Action<HardwareData> OnDataReceived;
        public event Action<string> OnError;

        // Connection metrics
        private float lastMessageTime;
        private int reconnectAttempts;
        private int messagesReceived;
        private float connectionStartTime;

        // Joystick axes mapping
        public enum JoystickAxis
        {
            LeftX = 0,
            LeftY = 1,
            RightX = 2,
            RightY = 3
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
        }

        private void Start()
        {
            if (autoConnect)
            {
                StartConnection();
            }
        }

        public void StartConnection()
        {
            StopAllCoroutines();
            StartCoroutine(ConnectionManager());
        }

        public void StopConnection()
        {
            StopAllCoroutines();
            DisconnectWebSocket();
            autoReconnect = false;
        }

        private IEnumerator ConnectionManager()
        {
            // Initial delay to let Unity initialize
            yield return new WaitForSeconds(0.5f);
            
            while (true)
            {
                if (_connectionState == ConnectionState.Disconnected)
                {
                    yield return ConnectToServer();

                    if (_connectionState != ConnectionState.Connected && autoReconnect)
                    {
                        reconnectAttempts++;
                        SetConnectionState(ConnectionState.Reconnecting);
                        
                        if (verboseLogging)
                            Debug.Log($"[PansimHardware] Reconnect attempt {reconnectAttempts} in {reconnectDelay}s");
                        
                        yield return new WaitForSeconds(reconnectDelay);
                    }
                }
                else if (_connectionState == ConnectionState.Connected)
                {
                    // Only check timeout if we're expecting regular messages
                    // Don't disconnect immediately - the Python server might not send constant data
                    if (messagesReceived > 0 && Time.time - lastMessageTime > connectionTimeout * 2)
                    {
                        Debug.LogWarning($"[PansimHardware] No messages for {connectionTimeout * 2} seconds, checking connection");
                        // Wait a bit more before disconnecting
                        yield return new WaitForSeconds(2f);
                        
                        // If still no messages, then disconnect
                        if (Time.time - lastMessageTime > connectionTimeout * 2 + 2)
                        {
                            Debug.LogWarning("[PansimHardware] Connection timeout confirmed, disconnecting");
                            DisconnectWebSocket();
                        }
                    }
                }

                yield return new WaitForSeconds(2f); // Check less frequently
            }
        }

        private IEnumerator ConnectToServer()
        {
            if (_connectionState == ConnectionState.Connecting)
                yield break;

            SetConnectionState(ConnectionState.Connecting);
            connectionStartTime = Time.time;

            bool connectionSuccessful = false;
            string errorMessage = null;

            try
            {
                Debug.Log($"[PansimHardware] Connecting to {serverUrl}");

                // Clean up any existing websocket
                if (websocket != null)
                {
                    try { websocket.Close(); } catch { }
                    websocket = null;
                }

                websocket = new WebSocket(serverUrl);

                // Setup event handlers
                websocket.OnOpen += HandleWebSocketOpen;
                websocket.OnError += HandleWebSocketError;
                websocket.OnClose += HandleWebSocketClose;
                websocket.OnMessage += HandleWebSocketMessage;
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }

            if (errorMessage == null && websocket != null)
            {
                // Connect - moved outside try-catch
                yield return websocket.Connect();

                // Wait a bit longer for connection to stabilize
                yield return new WaitForSeconds(1f);

                if (websocket != null && websocket.State == WebSocketState.Open)
                {
                    // Don't set state here - let OnOpen handler do it
                    // Just update the time and success flag
                    lastMessageTime = Time.time;
                    reconnectAttempts = 0;
                    connectionSuccessful = true;
                    Debug.Log("[PansimHardware] Connection established, waiting for messages");
                    
                    // Connection is established
                }
                else
                {
                    errorMessage = websocket != null ? 
                        $"WebSocket state is {websocket.State}" : 
                        "WebSocket is null after connection";
                }
            }

            if (!connectionSuccessful)
            {
                Debug.LogError($"[PansimHardware] Connection failed: {errorMessage ?? "Unknown error"}");
                SetConnectionState(ConnectionState.Error);
                OnError?.Invoke(errorMessage ?? "Connection failed");
                DisconnectWebSocket();
            }
        }

        private void HandleWebSocketOpen()
        {
            Debug.Log("[PansimHardware] WebSocket opened");
            SetConnectionState(ConnectionState.Connected);
            lastMessageTime = Time.time;
        }

        private void HandleWebSocketError(string error)
        {
            Debug.LogError($"[PansimHardware] WebSocket error: {error}");
            SetConnectionState(ConnectionState.Error);
            OnError?.Invoke(error);
        }

        private void HandleWebSocketClose(WebSocketCloseCode code)
        {
            Debug.Log($"[PansimHardware] WebSocket closed: {code}");
            SetConnectionState(ConnectionState.Disconnected);
        }

        private void HandleWebSocketMessage(byte[] bytes)
        {
            try
            {
                lastMessageTime = Time.time;
                messagesReceived++;

                string json = System.Text.Encoding.UTF8.GetString(bytes);
                
                // Try to parse as joystick data
                var tempData = JsonUtility.FromJson<HardwareData>(json);
                if (tempData != null)
                {
                    currentData = tempData;
                    
                    if (verboseLogging && currentData.HasInput())
                    {
                        Debug.Log($"[PansimHardware] Data received: L({currentData.left_x:F2},{currentData.left_y:F2}) R({currentData.right_x:F2},{currentData.right_y:F2})");
                    }

                    OnDataReceived?.Invoke(currentData);
                }
                else if (verboseLogging)
                {
                    Debug.Log($"[PansimHardware] Received message: {json}");
                }
            }
            catch (Exception e)
            {
                // Don't log every parse error - might be ping/pong or other messages
                if (verboseLogging)
                    Debug.LogWarning($"[PansimHardware] Failed to parse message: {e.Message}");
            }
        }

        private void Update()
        {
            // Dispatch WebSocket messages
            #if !UNITY_WEBGL || UNITY_EDITOR
            if (websocket != null)
            {
                try
                {
                    // Only dispatch if we're in a valid state
                    if (websocket.State == WebSocketState.Open || websocket.State == WebSocketState.Connecting)
                    {
                        websocket.DispatchMessageQueue();
                    }
                }
                catch (Exception e)
                {
                    if (verboseLogging)
                        Debug.LogWarning($"[PansimHardware] Error dispatching messages: {e.Message}");
                }
            }
            #endif
        }

        private void SetConnectionState(ConnectionState newState)
        {
            if (_connectionState != newState)
            {
                _connectionState = newState;
                OnConnectionStateChanged?.Invoke(newState);
            }
        }

        private void DisconnectWebSocket()
        {
            if (websocket != null)
            {
                try
                {
                    websocket.Close();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[PansimHardware] Error closing WebSocket: {e.Message}");
                }
                websocket = null;
            }

            SetConnectionState(ConnectionState.Disconnected);
        }

        // Public API Methods
        public float GetAxis(JoystickAxis axis)
        {
            if (_connectionState != ConnectionState.Connected)
                return 0f;

            switch (axis)
            {
                case JoystickAxis.LeftX: return currentData.left_x;
                case JoystickAxis.LeftY: return currentData.left_y;
                case JoystickAxis.RightX: return currentData.right_x;
                case JoystickAxis.RightY: return currentData.right_y;
                default: return 0f;
            }
        }

        public Vector2 GetLeftJoystick()
        {
            return new Vector2(currentData.left_x, currentData.left_y);
        }

        public Vector2 GetRightJoystick()
        {
            return new Vector2(currentData.right_x, currentData.right_y);
        }

        public bool IsConnected()
        {
            return _connectionState == ConnectionState.Connected;
        }

        // Debug UI
        private void OnGUI()
        {
            if (!enableDebugUI) return;

            DrawDebugPanel();
        }

        private void DrawDebugPanel()
        {
            GUILayout.BeginArea(new Rect(10, 10, 350, 300));
            GUI.Box(new Rect(0, 0, 350, 300), "");

            GUILayout.Label("PANSIM HARDWARE CONTROLLER", GetHeaderStyle());
            GUILayout.Space(5);

            // Connection status
            GUI.color = GetStatusColor();
            GUILayout.Label($"Status: {_connectionState}", GetStatusStyle());
            GUI.color = Color.white;

            GUILayout.Label($"Server: {serverUrl}");

            if (_connectionState == ConnectionState.Connected)
            {
                float uptime = Time.time - connectionStartTime;
                GUILayout.Label($"Uptime: {uptime:F1}s | Messages: {messagesReceived}");
            }
            else if (reconnectAttempts > 0)
            {
                GUILayout.Label($"Reconnect Attempts: {reconnectAttempts}");
            }

            // Joystick values
            GUILayout.Space(10);
            GUILayout.Label("JOYSTICK VALUES:", GetHeaderStyle());
            DrawJoystickBar("LEFT X", currentData.left_x);
            DrawJoystickBar("LEFT Y", currentData.left_y);
            DrawJoystickBar("RIGHT X", currentData.right_x);
            DrawJoystickBar("RIGHT Y", currentData.right_y);

            // Buttons
            GUILayout.Space(10);
            DrawControlButtons();

            GUILayout.EndArea();

            // Troubleshooting panel
            if (_connectionState != ConnectionState.Connected)
            {
                DrawTroubleshootingPanel();
            }
        }

        private void DrawJoystickBar(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label + ":", GUILayout.Width(60));
            
            // Draw bar background
            Rect barRect = GUILayoutUtility.GetRect(150, 20);
            GUI.Box(barRect, "");
            
            // Draw value bar
            if (Mathf.Abs(value) > 0.01f)
            {
                float barWidth = Mathf.Abs(value) * 75;
                float barX = value > 0 ? barRect.x + 75 : barRect.x + 75 - barWidth;
                GUI.color = value > 0 ? Color.green : Color.red;
                GUI.Box(new Rect(barX, barRect.y, barWidth, 20), "");
                GUI.color = Color.white;
            }
            
            // Draw center line
            GUI.color = Color.gray;
            GUI.Box(new Rect(barRect.x + 75, barRect.y, 2, 20), "");
            GUI.color = Color.white;
            
            GUILayout.Label($"{value:F2}", GUILayout.Width(50));
            GUILayout.EndHorizontal();
        }

        private void DrawControlButtons()
        {
            if (_connectionState == ConnectionState.Disconnected)
            {
                if (GUILayout.Button("Connect"))
                {
                    autoReconnect = true;
                    StartConnection();
                }
            }
            else if (_connectionState == ConnectionState.Connected)
            {
                if (GUILayout.Button("Disconnect"))
                {
                    StopConnection();
                }
            }
            else if (_connectionState == ConnectionState.Error || _connectionState == ConnectionState.Reconnecting)
            {
                if (GUILayout.Button("Retry Now"))
                {
                    reconnectAttempts = 0;
                    StartConnection();
                }
            }
        }

        private void DrawTroubleshootingPanel()
        {
            GUILayout.BeginArea(new Rect(370, 10, 300, 180));
            GUI.Box(new Rect(0, 0, 300, 180), "");

            GUILayout.Label("TROUBLESHOOTING:", GetHeaderStyle());
            GUILayout.Space(5);

            GUILayout.Label("1. Ensure Python server is running:");
            GUI.color = Color.cyan;
            GUILayout.Label("   python run_pansim.py");
            GUI.color = Color.white;

            GUILayout.Label("2. Check joysticks are connected");
            GUILayout.Label("3. Verify port 8766 is not blocked");
            GUILayout.Label("4. Check Windows Firewall settings");

            if (_connectionState == ConnectionState.Error)
            {
                GUILayout.Space(5);
                GUI.color = Color.yellow;
                GUILayout.Label("Try: ws://127.0.0.1:8766");
                GUI.color = Color.white;
            }

            GUILayout.EndArea();
        }

        private Color GetStatusColor()
        {
            switch (_connectionState)
            {
                case ConnectionState.Connected: return Color.green;
                case ConnectionState.Connecting: return Color.yellow;
                case ConnectionState.Reconnecting: return Color.yellow;
                case ConnectionState.Error: return Color.red;
                default: return Color.gray;
            }
        }

        private GUIStyle GetHeaderStyle()
        {
            return new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        }

        private GUIStyle GetStatusStyle()
        {
            return new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
        }

        private void OnDestroy()
        {
            DisconnectWebSocket();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                DisconnectWebSocket();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && verboseLogging)
            {
                Debug.Log("[PansimHardware] Application lost focus");
            }
        }
    }

    /// <summary>
    /// Hardware data structure matching Python server format
    /// </summary>
    [Serializable]
    public class HardwareData
    {
        public float left_x;
        public float left_y;
        public float right_x;
        public float right_y;
        public int button_states;
        public float timestamp;

        public bool HasInput()
        {
            return Mathf.Abs(left_x) > 0.01f || Mathf.Abs(left_y) > 0.01f ||
                   Mathf.Abs(right_x) > 0.01f || Mathf.Abs(right_y) > 0.01f;
        }

        public void Reset()
        {
            left_x = left_y = right_x = right_y = 0f;
            button_states = 0;
        }
    }
}