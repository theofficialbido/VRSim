using System.Collections;
using UnityEngine;
using NativeWebSocket;

public class SimpleWebSocketTest : MonoBehaviour
{
    [Header("WebSocket Settings")]
    public string serverUrl = "ws://localhost:8765";
    public bool autoConnect = true;
    
    private WebSocket websocket;
    private float lastMessageTime;
    private int messageCount = 0;
    private string lastMessage = "";
    private string connectionStatus = "Not Connected";
    private bool isConnecting = false;
    
    // Joystick values
    private float leftX, leftY, rightX, rightY;
    
    void Start()
    {
        Debug.Log("[SimpleTest] Starting WebSocket test...");
        Debug.Log($"[SimpleTest] Will connect to: {serverUrl}");
        
        if (autoConnect)
        {
            StartCoroutine(Connect());
        }
    }
    
    IEnumerator Connect()
    {
        if (isConnecting)
        {
            Debug.Log("[SimpleTest] Already connecting, skipping...");
            yield break;
        }
        
        isConnecting = true;
        Debug.Log($"[SimpleTest] Attempting connection to {serverUrl}");
        connectionStatus = "Connecting...";
        
        // Clean up any existing connection
        if (websocket != null)
        {
            Debug.Log("[SimpleTest] Cleaning up existing WebSocket");
            try 
            {
                websocket.Close();
            }
            catch { }
            websocket = null;
        }
        
        // Small delay before connecting
        yield return new WaitForSeconds(0.5f);
        
        websocket = new WebSocket(serverUrl);
        
        websocket.OnOpen += () =>
        {
            Debug.Log("[SimpleTest] *** WebSocket OPENED! ***");
            connectionStatus = "Connected";
            lastMessageTime = Time.time;
        };
        
        websocket.OnError += (e) =>
        {
            Debug.LogError($"[SimpleTest] WebSocket ERROR: {e}");
            connectionStatus = $"Error: {e}";
        };
        
        websocket.OnClose += (e) =>
        {
            Debug.Log($"[SimpleTest] WebSocket CLOSED: {e}");
            connectionStatus = "Closed";
            isConnecting = false;
        };
        
        websocket.OnMessage += (bytes) =>
        {
            var message = System.Text.Encoding.UTF8.GetString(bytes);
            messageCount++;
            
            // Parse joystick data
            try
            {
                // Simple JSON parsing for {"left_x":0,"left_y":0,"right_x":0,"right_y":0}
                if (message.Contains("left_x"))
                {
                    leftX = ExtractValue(message, "left_x");
                    leftY = ExtractValue(message, "left_y");
                    rightX = ExtractValue(message, "right_x");
                    rightY = ExtractValue(message, "right_y");
                }
            }
            catch { }
            
            if (messageCount <= 5 || messageCount % 50 == 0)  // Log first 5 and every 50th
            {
                Debug.Log($"[SimpleTest] Message #{messageCount}: {message}");
            }
            
            lastMessage = message;
            lastMessageTime = Time.time;
        };
        
        // Connect
        Debug.Log("[SimpleTest] Calling websocket.Connect()...");
        yield return websocket.Connect();
        
        // Wait a bit
        yield return new WaitForSeconds(1f);
        
        Debug.Log($"[SimpleTest] After Connect() - State: {websocket.State}");
        
        if (websocket.State == WebSocketState.Open)
        {
            Debug.Log("[SimpleTest] *** CONNECTION SUCCESSFUL! ***");
            connectionStatus = "Connected";
        }
        else
        {
            Debug.LogError($"[SimpleTest] Connection failed! State: {websocket.State}");
            connectionStatus = "Failed";
        }
        
        isConnecting = false;
    }
    
    float ExtractValue(string json, string key)
    {
        try
        {
            int index = json.IndexOf($"\"{key}\":");
            if (index < 0) return 0;
            
            int start = json.IndexOf(':', index) + 1;
            int end = json.IndexOfAny(new char[] { ',', '}' }, start);
            
            string value = json.Substring(start, end - start);
            return float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0;
        }
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
            catch (System.Exception e)
            {
                Debug.LogError($"[SimpleTest] Error dispatching: {e.Message}");
            }
            #endif
        }
    }
    
    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 450, 280));
        GUI.Box(new Rect(0, 0, 450, 280), "SIMPLE WEBSOCKET TEST");
        GUILayout.Space(30);
        
        GUILayout.Label($"URL: {serverUrl}");
        
        // Status with color
        GUI.color = connectionStatus == "Connected" ? Color.green : 
                   connectionStatus == "Connecting..." ? Color.yellow : Color.red;
        GUILayout.Label($"Status: {connectionStatus}");
        GUI.color = Color.white;
        
        if (websocket != null)
        {
            GUILayout.Label($"WebSocket State: {websocket.State}");
        }
        
        GUILayout.Label($"Messages Received: {messageCount}");
        
        if (messageCount > 0)
        {
            GUILayout.Label($"Time since last: {Time.time - lastMessageTime:F1}s");
            
            // Show joystick values
            GUILayout.Space(5);
            GUILayout.Label("Joystick Values:");
            GUILayout.Label($"LEFT:  X={leftX:F3}  Y={leftY:F3}");
            GUILayout.Label($"RIGHT: X={rightX:F3}  Y={rightY:F3}");
            
            GUILayout.Space(5);
            if (lastMessage.Length > 0)
            {
                GUILayout.Label($"Raw: {lastMessage.Substring(0, Mathf.Min(lastMessage.Length, 80))}");
            }
        }
        
        GUILayout.Space(10);
        
        if (!isConnecting && (websocket == null || websocket.State == WebSocketState.Closed))
        {
            if (GUILayout.Button("Connect"))
            {
                StartCoroutine(Connect());
            }
        }
        else if (websocket != null && websocket.State == WebSocketState.Open)
        {
            if (GUILayout.Button("Disconnect"))
            {
                CloseConnection();
            }
        }
        
        GUILayout.EndArea();
        
        // Help text
        if (connectionStatus != "Connected")
        {
            GUILayout.BeginArea(new Rect(470, 10, 350, 200));
            GUI.Box(new Rect(0, 0, 350, 200), "HELP");
            GUILayout.Space(30);
            
            GUILayout.Label("To connect:");
            GUILayout.Label("1. Start Python server:");
            GUI.color = Color.cyan;
            GUILayout.Label("   python start_unity_websocket.py");
            GUI.color = Color.white;
            GUILayout.Label("   OR");
            GUI.color = Color.cyan;
            GUILayout.Label("   start_unity_server.bat");
            GUI.color = Color.white;
            GUILayout.Label("");
            GUILayout.Label("2. Click Connect button");
            GUILayout.Label("");
            GUILayout.Label("3. Check Unity Console for errors");
            
            GUILayout.EndArea();
        }
    }
    
    void CloseConnection()
    {
        if (websocket != null)
        {
            Debug.Log("[SimpleTest] Closing connection...");
            websocket.Close();
            websocket = null;
            connectionStatus = "Disconnected";
        }
    }
    
    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            CloseConnection();
        }
    }
    
    void OnApplicationFocus(bool hasFocus)
    {
        // Keep connection alive
        if (!hasFocus)
        {
            Debug.Log("[SimpleTest] Lost focus");
        }
    }
    
    void OnDestroy()
    {
        CloseConnection();
    }
}