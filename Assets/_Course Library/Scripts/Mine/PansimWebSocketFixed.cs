using System;
using System.Collections;
using UnityEngine;
using NativeWebSocket;

namespace Pansim.Hardware
{
    public class PansimWebSocketFixed : MonoBehaviour
    {
        [Header("Connection Settings")]
        [SerializeField] private string serverUrl = "ws://localhost:8765";
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private float reconnectDelay = 3f;
        
        [Header("Debug")]
        [SerializeField] private bool showDebug = true;
        [SerializeField] private bool verboseLogging = false;
        
        private WebSocket websocket;
        private bool isConnected = false;
        private string status = "Disconnected";
        private float lastMessageTime;
        private int messageCount = 0;
        
        // Joystick data
        private float leftX, leftY, rightX, rightY;
        
        // Singleton
        private static PansimWebSocketFixed _instance;
        public static PansimWebSocketFixed Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<PansimWebSocketFixed>();
                return _instance;
            }
        }
        
        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        
        void Start()
        {
            if (autoConnect)
                ConnectWebSocket();
        }
        
        public void ConnectWebSocket()
        {
            StartCoroutine(ConnectCoroutine());
        }
        
        IEnumerator ConnectCoroutine()
        {
            status = "Connecting...";
            Debug.Log($"[PansimWS] Connecting to {serverUrl}");
            
            // Clean up any existing connection
            if (websocket != null)
            {
                try 
                { 
                    websocket.CancelConnection();
                    websocket.Close();
                }
                catch { }
                websocket = null;
            }
            
            // Create new WebSocket
            websocket = new WebSocket(serverUrl);
            
            // Setup callbacks
            websocket.OnOpen += OnWebSocketOpen;
            websocket.OnError += OnWebSocketError;
            websocket.OnClose += OnWebSocketClose;
            websocket.OnMessage += OnWebSocketMessage;
            
            // Connect
            yield return websocket.Connect();
            
            // Wait a moment
            yield return new WaitForSeconds(0.5f);
            
            // Check connection
            if (websocket.State == WebSocketState.Open)
            {
                Debug.Log("[PansimWS] Connection successful!");
                isConnected = true;
                status = "Connected";
                lastMessageTime = Time.time;
            }
            else
            {
                Debug.LogError($"[PansimWS] Connection failed. State: {websocket.State}");
                status = "Failed";
                isConnected = false;
            }
        }
        
        void OnWebSocketOpen()
        {
            Debug.Log("[PansimWS] WebSocket opened");
            isConnected = true;
            status = "Connected";
            lastMessageTime = Time.time;
        }
        
        void OnWebSocketError(string error)
        {
            Debug.LogError($"[PansimWS] Error: {error}");
            status = $"Error: {error}";
        }
        
        void OnWebSocketClose(WebSocketCloseCode code)
        {
            Debug.Log($"[PansimWS] Closed: {code}");
            isConnected = false;
            status = "Disconnected";
            
            // Auto reconnect
            if (autoConnect && Application.isPlaying)
            {
                Invoke(nameof(ConnectWebSocket), reconnectDelay);
            }
        }
        
        void OnWebSocketMessage(byte[] data)
        {
            try
            {
                string json = System.Text.Encoding.UTF8.GetString(data);
                
                // Parse JSON manually to avoid issues
                // Expected format: {"left_x":0,"left_y":0,"right_x":0,"right_y":0}
                
                if (json.Contains("left_x"))
                {
                    // Simple parsing
                    leftX = ExtractFloat(json, "left_x");
                    leftY = ExtractFloat(json, "left_y");
                    rightX = ExtractFloat(json, "right_x");
                    rightY = ExtractFloat(json, "right_y");
                    
                    messageCount++;
                    lastMessageTime = Time.time;
                    
                    if (verboseLogging && (leftX != 0 || leftY != 0 || rightX != 0 || rightY != 0))
                    {
                        Debug.Log($"[PansimWS] Joy: L({leftX:F2},{leftY:F2}) R({rightX:F2},{rightY:F2})");
                    }
                }
            }
            catch (Exception e)
            {
                if (verboseLogging)
                    Debug.LogWarning($"[PansimWS] Parse error: {e.Message}");
            }
        }
        
        float ExtractFloat(string json, string key)
        {
            try
            {
                int keyIndex = json.IndexOf($"\"{key}\"");
                if (keyIndex < 0) keyIndex = json.IndexOf($"{key}");
                if (keyIndex < 0) return 0f;
                
                int colonIndex = json.IndexOf(':', keyIndex);
                if (colonIndex < 0) return 0f;
                
                int commaIndex = json.IndexOf(',', colonIndex);
                int braceIndex = json.IndexOf('}', colonIndex);
                
                int endIndex = commaIndex > 0 && commaIndex < braceIndex ? commaIndex : braceIndex;
                if (endIndex < 0) endIndex = json.Length;
                
                string valueStr = json.Substring(colonIndex + 1, endIndex - colonIndex - 1).Trim();
                
                if (float.TryParse(valueStr, System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out float value))
                {
                    return value;
                }
            }
            catch { }
            return 0f;
        }
        
        void Update()
        {
            if (websocket != null)
            {
                #if !UNITY_WEBGL || UNITY_EDITOR
                try
                {
                    websocket.DispatchMessageQueue();
                }
                catch (Exception e)
                {
                    if (verboseLogging)
                        Debug.LogError($"[PansimWS] Dispatch error: {e.Message}");
                }
                #endif
            }
        }
        
        void OnGUI()
        {
            if (!showDebug) return;
            
            GUILayout.BeginArea(new Rect(10, 10, 300, 250));
            GUI.Box(new Rect(0, 0, 300, 250), "");
            
            GUILayout.Label("PANSIM WEBSOCKET (FIXED)");
            GUILayout.Space(5);
            
            GUI.color = isConnected ? Color.green : Color.red;
            GUILayout.Label($"Status: {status}");
            GUI.color = Color.white;
            
            GUILayout.Label($"URL: {serverUrl}");
            
            if (websocket != null)
                GUILayout.Label($"State: {websocket.State}");
            
            GUILayout.Label($"Messages: {messageCount}");
            
            if (messageCount > 0)
            {
                GUILayout.Label($"Last: {Time.time - lastMessageTime:F1}s ago");
                
                GUILayout.Space(5);
                GUILayout.Label("Joystick Values:");
                GUILayout.Label($"LEFT:  X={leftX:F2}  Y={leftY:F2}");
                GUILayout.Label($"RIGHT: X={rightX:F2}  Y={rightY:F2}");
            }
            
            GUILayout.Space(10);
            
            if (!isConnected)
            {
                if (GUILayout.Button("Connect"))
                {
                    ConnectWebSocket();
                }
            }
            else
            {
                if (GUILayout.Button("Disconnect"))
                {
                    DisconnectWebSocket();
                }
            }
            
            GUILayout.EndArea();
        }
        
        public void DisconnectWebSocket()
        {
            autoConnect = false;
            
            if (websocket != null)
            {
                StartCoroutine(DisconnectCoroutine());
            }
        }
        
        IEnumerator DisconnectCoroutine()
        {
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                yield return websocket.Close();
            }
            websocket = null;
            isConnected = false;
            status = "Disconnected";
        }
        
        void OnDestroy()
        {
            DisconnectWebSocket();
        }
        
        // Public API
        public bool IsConnected() => isConnected;
        public float GetLeftX() => leftX;
        public float GetLeftY() => leftY;
        public float GetRightX() => rightX;
        public float GetRightY() => rightY;
        public Vector2 GetLeftJoystick() => new Vector2(leftX, leftY);
        public Vector2 GetRightJoystick() => new Vector2(rightX, rightY);
    }
}