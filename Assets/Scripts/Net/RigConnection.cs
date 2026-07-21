using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json;
using UnityEngine;
using VRSIM.State;

namespace VRSIM.Net
{
    public enum ConnectionStatus
    {
        Idle,
        Discovering,
        Connecting,
        Handshaking,
        Connected,
        Reconnecting,
        /// <summary>Terminal. Retrying cannot help; the user must act.</summary>
        Fatal,
    }

    /// <summary>
    /// The client half of VRSIM protocol v1.
    ///
    /// Transport and protocol only -- it owns the socket, the handshake, the
    /// sequence numbers and the reconnect policy, and it publishes what it
    /// receives. It deliberately knows nothing about rigs, gauges or levers, so
    /// that presentation code can be built and changed without touching
    /// networking. The class it replaces mixed transport, JSON parsing, top
    /// drive motion, camera control and debug UI in one 970-line file.
    ///
    /// No platform #ifs. NativeWebSocket's only platform guard is for WebGL;
    /// on Android it uses System.Net.WebSockets, so the Quest build gets the
    /// same code path as the Editor.
    /// </summary>
    public class RigConnection : MonoBehaviour
    {
        [Header("Engine address")]
        [Tooltip("IP or hostname of the PC running the engine. Leave blank to rely on discovery.")]
        public string host = "192.168.1.100";
        public int port = 8765;

        [Tooltip("Broadcast to find the PC before falling back to the address above.")]
        public bool autoDiscover = true;
        public float discoveryTimeout = 3f;

        [Header("Behaviour")]
        public bool connectOnStart = true;
        [Tooltip("Seconds of silence before sending a ping.")]
        public float idlePingAfter = 5f;
        [Tooltip("Seconds to wait for a pong before treating the link as dead.")]
        public float pongTimeout = 3f;
        [Tooltip("Seconds without state before the display is marked stale.")]
        public float staleAfter = 2f;
        [Tooltip("Seconds to wait for a command ack before giving up on it.")]
        public float commandTimeout = 2f;

        [Header("Diagnostics")]
        public bool verboseLogging;

        // ---- public surface -------------------------------------------------

        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Idle;

        /// <summary>Human-readable explanation of the current status, for display in-headset.</summary>
        public string StatusDetail { get; private set; } = "Not started";

        public RigState State { get; } = new RigState();

        /// <summary>Round-trip time in milliseconds, from the last ping. -1 until measured.</summary>
        public float LatencyMs { get; private set; } = -1f;

        public string EngineVersion { get; private set; } = "";
        public ControlSpec[] Controls { get; private set; } = Array.Empty<ControlSpec>();
        public string ResolvedEndpoint { get; private set; } = "";

        public event Action<ConnectionStatus, string> StatusChanged;
        public event Action<RigEventPayload> EngineEvent;
        public event Action<AckPayload> CommandAcked;

        // ---- internals ------------------------------------------------------

        private WebSocket _socket;
        private long _outSeq;
        private long _lastInSeq;
        private bool _helloAcked;
        private float _lastMessageTime;
        private float _lastPingSentAt = -1f;
        private int _reconnectAttempt;
        private Coroutine _lifecycle;
        private string _lastErrorCode;
        private string _lastErrorMessage;

        private readonly Dictionary<string, PendingCommand> _pending = new Dictionary<string, PendingCommand>();

        private struct PendingCommand
        {
            public string Control;
            public float SentAt;
            public Action<AckPayload> OnResult;
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            Formatting = Formatting.None,
        };

        private void Start()
        {
            // The engine pushes state continuously; if Unity stops stepping
            // when the headset loses focus, the socket backs up and the first
            // frame after refocus applies a burst of stale deltas.
            Application.runInBackground = true;

            if (connectOnStart) Connect();
        }

        public void Connect()
        {
            if (_lifecycle != null) StopCoroutine(_lifecycle);
            _lifecycle = StartCoroutine(Lifecycle());
        }

        public void Disconnect()
        {
            if (_lifecycle != null) { StopCoroutine(_lifecycle); _lifecycle = null; }
            CloseSocket();
            SetStatus(ConnectionStatus.Idle, "Disconnected by request");
        }

        private IEnumerator Lifecycle()
        {
            while (true)
            {
                _lastErrorCode = null;
                _lastErrorMessage = null;

                var endpoint = "";
                if (autoDiscover)
                {
                    SetStatus(ConnectionStatus.Discovering, "Looking for the engine on this network...");
                    DiscoveredHost? found = null;
                    yield return RigDiscovery.Discover(discoveryTimeout, h => found = h);
                    if (found.HasValue)
                        endpoint = $"ws://{found.Value.Address}:{found.Value.EnginePort}";
                }

                if (string.IsNullOrWhiteSpace(endpoint))
                {
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        SetStatus(ConnectionStatus.Fatal,
                            "No engine found and no address configured. Set 'host' on RigConnection.");
                        yield break;
                    }
                    endpoint = $"ws://{host}:{port}";
                }

                ResolvedEndpoint = endpoint;
                SetStatus(ConnectionStatus.Connecting, $"Connecting to {endpoint}");

                yield return OpenSocket(endpoint);

                // OpenSocket returns when the socket closes. Decide whether to retry.
                if (_lastErrorCode == "unsupported_version" || _lastErrorCode == "malformed_message")
                {
                    // Retrying cannot fix either of these. Looping would just
                    // hide the problem behind a spinner.
                    SetStatus(ConnectionStatus.Fatal, _lastErrorMessage ?? _lastErrorCode);
                    yield break;
                }

                State.Clear();
                _reconnectAttempt++;
                var delay = Mathf.Min(0.5f * Mathf.Pow(2, _reconnectAttempt - 1), 10f);
                delay += UnityEngine.Random.Range(0f, 0.3f); // jitter, so N headsets don't sync up

                var why = _lastErrorCode == "already_controlled"
                    ? "Another headset has control"
                    : _lastErrorMessage ?? "Connection lost";
                SetStatus(ConnectionStatus.Reconnecting, $"{why}. Retrying in {delay:0.0}s");
                yield return new WaitForSecondsRealtime(delay);
            }
        }

        private IEnumerator OpenSocket(string endpoint)
        {
            _helloAcked = false;
            _outSeq = 0;
            _lastInSeq = 0;
            _pending.Clear();

            var closed = false;

            // A coroutine cannot yield out of a catch clause, so the failure is
            // recorded and acted on after the try block.
            string createError = null;
            try
            {
                _socket = new WebSocket(endpoint);
            }
            catch (Exception ex)
            {
                createError = ex.Message;
            }

            if (createError != null)
            {
                _lastErrorMessage = $"Could not open {endpoint}: {createError}";
                Debug.LogWarning($"[RigConnection] {_lastErrorMessage}");
                yield break;
            }

            _socket.OnOpen += () =>
            {
                _lastMessageTime = Time.realtimeSinceStartup;
                SetStatus(ConnectionStatus.Handshaking, "Connected, negotiating protocol...");
                Send(Protocol.Hello, new { client = "vrsim-quest", app_version = Application.version });
            };

            _socket.OnMessage += bytes =>
            {
                _lastMessageTime = Time.realtimeSinceStartup;
                HandleMessage(Encoding.UTF8.GetString(bytes));
            };

            _socket.OnError += err =>
            {
                if (verboseLogging) Debug.LogWarning($"[RigConnection] socket error: {err}");
                _lastErrorMessage = err;
            };

            _socket.OnClose += code =>
            {
                closed = true;
                if (verboseLogging)
                    Debug.Log($"[RigConnection] closed: {code} ({Protocol.DescribeCloseCode((ushort)code)})");
            };

            // Connect() completes only when the socket closes, so it is started
            // rather than awaited; the loop below drives the message pump.
            _socket.Connect();

            while (!closed)
            {
                _socket.DispatchMessageQueue();
                Tick();
                yield return null;
            }

            CloseSocket();
        }

        /// <summary>Per-frame liveness, staleness and command-timeout checks.</summary>
        private void Tick()
        {
            var now = Time.realtimeSinceStartup;

            if (_helloAcked)
            {
                State.IsStale = now - _lastMessageTime > staleAfter;

                if (_lastPingSentAt < 0f && now - _lastMessageTime > idlePingAfter)
                {
                    _lastPingSentAt = now;
                    Send(Protocol.Ping);
                }
                else if (_lastPingSentAt >= 0f && now - _lastPingSentAt > pongTimeout)
                {
                    Debug.LogWarning("[RigConnection] no pong; treating link as dead");
                    _lastErrorMessage = "Engine stopped responding";
                    CloseSocket();
                    return;
                }
            }

            if (_pending.Count == 0) return;
            List<string> expired = null;
            foreach (var kv in _pending)
            {
                if (now - kv.Value.SentAt <= commandTimeout) continue;
                (expired ?? (expired = new List<string>())).Add(kv.Key);
            }
            if (expired == null) return;

            foreach (var id in expired)
            {
                var pending = _pending[id];
                _pending.Remove(id);
                Debug.LogWarning($"[RigConnection] command {id} on {pending.Control} was never acked");
                // Report as a rejection so the caller springs the control back
                // rather than leaving it showing a position the rig is not in.
                var timeoutAck = new AckPayload
                {
                    CommandId = id,
                    Status = "rejected",
                    Reason = "No response from engine",
                };
                pending.OnResult?.Invoke(timeoutAck);
                CommandAcked?.Invoke(timeoutAck);
            }
        }

        private void HandleMessage(string json)
        {
            Envelope envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<Envelope>(json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RigConnection] could not parse frame: {ex.Message}");
                return;
            }
            if (envelope == null) return;

            if (envelope.Version != Protocol.Version)
            {
                _lastErrorCode = "unsupported_version";
                _lastErrorMessage = $"Engine speaks v{envelope.Version}, this app speaks v{Protocol.Version}";
                CloseSocket();
                return;
            }

            // Gap detection. A delta applied across a gap silently corrupts the
            // mirror, so recover with a full snapshot instead.
            if (envelope.Type == Protocol.StateDelta && _lastInSeq > 0 && envelope.Seq != _lastInSeq + 1)
            {
                Debug.LogWarning($"[RigConnection] sequence gap {_lastInSeq} -> {envelope.Seq}; resyncing");
                _lastInSeq = envelope.Seq;
                Send(Protocol.Resync);
                return;
            }
            if (envelope.Seq > 0) _lastInSeq = envelope.Seq;

            switch (envelope.Type)
            {
                case Protocol.HelloAck:
                {
                    var ack = envelope.PayloadAs<HelloAckPayload>();
                    EngineVersion = ack?.EngineVersion ?? "unknown";
                    Controls = ack?.Controls ?? Array.Empty<ControlSpec>();
                    _helloAcked = true;
                    _reconnectAttempt = 0;
                    if (verboseLogging)
                        Debug.Log($"[RigConnection] engine {EngineVersion}, {Controls.Length} controls");
                    break;
                }
                case Protocol.StateSnapshot:
                    State.ApplySnapshot(envelope.Payload);
                    SetStatus(ConnectionStatus.Connected, $"Connected to {ResolvedEndpoint}");
                    break;

                case Protocol.StateDelta:
                    State.ApplyDelta(envelope.Payload);
                    break;

                case Protocol.Ack:
                {
                    var ack = envelope.PayloadAs<AckPayload>();
                    if (ack?.CommandId != null && _pending.TryGetValue(ack.CommandId, out var pending))
                    {
                        _pending.Remove(ack.CommandId);
                        pending.OnResult?.Invoke(ack);
                    }
                    if (ack != null) CommandAcked?.Invoke(ack);
                    break;
                }
                case Protocol.Event:
                {
                    var evt = envelope.PayloadAs<RigEventPayload>();
                    if (evt != null)
                    {
                        if (verboseLogging) Debug.Log($"[RigConnection] event {evt.Kind}: {evt.Message}");
                        EngineEvent?.Invoke(evt);
                    }
                    break;
                }
                case Protocol.Pong:
                    if (_lastPingSentAt >= 0f)
                        LatencyMs = (Time.realtimeSinceStartup - _lastPingSentAt) * 1000f;
                    _lastPingSentAt = -1f;
                    break;

                case Protocol.Error:
                {
                    // The authoritative disconnect reason -- application close
                    // codes do not survive NativeWebSocket, which collapses
                    // everything outside 1000-1015 to Undefined.
                    _lastErrorCode = (string)envelope.Payload?["code"];
                    _lastErrorMessage = (string)envelope.Payload?["message"];
                    Debug.LogWarning($"[RigConnection] engine error [{_lastErrorCode}] {_lastErrorMessage}");
                    break;
                }
            }
        }

        // ---- sending --------------------------------------------------------

        private void Send(string type, object payload = null)
        {
            if (_socket == null || _socket.State != WebSocketState.Open) return;
            _outSeq++;
            var envelope = Envelope.Create(type, _outSeq, payload);
            _socket.SendText(JsonConvert.SerializeObject(envelope, JsonSettings));
        }

        /// <summary>
        /// Sends an actuation and returns its command id.
        ///
        /// A command is a request, not an assertion: this does not change local
        /// state. The caller should show its control moving immediately, but
        /// treat the position as unconfirmed until <paramref name="onResult"/>
        /// reports acceptance and the change comes back as state.
        /// </summary>
        public string SendCommand(string control, object value, string action = "set",
                                  Action<AckPayload> onResult = null)
        {
            if (Status != ConnectionStatus.Connected)
            {
                onResult?.Invoke(new AckPayload
                {
                    Status = "rejected",
                    Reason = "Not connected to the engine",
                });
                return null;
            }

            var id = "c-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _pending[id] = new PendingCommand
            {
                Control = control,
                SentAt = Time.realtimeSinceStartup,
                OnResult = onResult,
            };
            Send(Protocol.Command, new CommandPayload
            {
                CommandId = id,
                Control = control,
                Action = action,
                Value = value,
            });
            if (verboseLogging) Debug.Log($"[RigConnection] -> {control} = {value} ({id})");
            return id;
        }

        /// <summary>True if the engine declared this control in its hello.ack.</summary>
        public bool HasControl(string id)
        {
            foreach (var c in Controls)
                if (c.Id == id) return true;
            return false;
        }

        // ---- plumbing -------------------------------------------------------

        private void SetStatus(ConnectionStatus status, string detail)
        {
            if (Status == status && StatusDetail == detail) return;
            Status = status;
            StatusDetail = detail;
            if (verboseLogging) Debug.Log($"[RigConnection] {status}: {detail}");
            StatusChanged?.Invoke(status, detail);
        }

        private void CloseSocket()
        {
            if (_socket == null) return;
            try { _socket.Close(); }
            catch (Exception ex) { Debug.LogWarning($"[RigConnection] close failed: {ex.Message}"); }
            _socket = null;
            _helloAcked = false;
            _lastPingSentAt = -1f;
        }

        private void OnDestroy() => CloseSocket();
        private void OnApplicationQuit() => CloseSocket();
    }
}
