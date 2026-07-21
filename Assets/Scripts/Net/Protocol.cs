using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VRSIM.Net
{
    /// <summary>
    /// Wire format for VRSIM protocol v1. See docs/protocol.md, which is the
    /// contract -- where this code and that document disagree, the document is
    /// right and this file is the bug.
    /// </summary>
    public static class Protocol
    {
        public const int Version = 1;

        // Engine -> client
        public const string HelloAck = "hello.ack";
        public const string StateSnapshot = "state.snapshot";
        public const string StateDelta = "state.delta";
        public const string Event = "event";
        public const string Ack = "ack";
        public const string Error = "error";
        public const string Pong = "pong";

        // Client -> engine
        public const string Hello = "hello";
        public const string Command = "command";
        public const string Resync = "resync";
        public const string Ping = "ping";

        // Close codes. 4400 in particular must never be retried blindly --
        // reconnecting to an engine that speaks another version just loops.
        public const ushort CloseUnsupportedVersion = 4400;
        public const ushort CloseMalformed = 4401;
        public const ushort CloseAlreadyControlled = 4409;
        public const ushort CloseInternal = 4500;

        public static string DescribeCloseCode(ushort code)
        {
            switch (code)
            {
                case CloseUnsupportedVersion: return "engine speaks a different protocol version";
                case CloseMalformed: return "engine rejected a malformed message";
                case CloseAlreadyControlled: return "another headset already has control";
                case CloseInternal: return "engine internal error";
                case 1000: return "closed normally";
                default: return $"connection closed ({code})";
            }
        }
    }

    /// <summary>One message on the wire.</summary>
    public class Envelope
    {
        [JsonProperty("v")] public int Version = Protocol.Version;
        [JsonProperty("type")] public string Type;
        [JsonProperty("seq")] public long Seq;
        [JsonProperty("ts")] public double Timestamp;

        /// <summary>
        /// Left as a JToken rather than a typed field because a state.delta is
        /// an RFC 7386 merge patch: an arbitrary subtree of the state where an
        /// explicit null means "remove this key". Deserialising that into a
        /// POCO would silently lose the distinction between "absent" (leave
        /// alone) and "null" (delete).
        /// </summary>
        [JsonProperty("payload")] public JToken Payload;

        public static Envelope Create(string type, long seq, object payload = null)
        {
            return new Envelope
            {
                Version = Protocol.Version,
                Type = type,
                Seq = seq,
                Timestamp = Math.Round(
                    (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds, 3),
                Payload = payload == null ? new JObject() : JToken.FromObject(payload),
            };
        }

        public T PayloadAs<T>() where T : class
        {
            return Payload?.ToObject<T>();
        }
    }

    public class HelloAckPayload
    {
        [JsonProperty("engine_version")] public string EngineVersion;
        [JsonProperty("tick_hz")] public float TickHz = 20f;
        [JsonProperty("controls")] public ControlSpec[] Controls = Array.Empty<ControlSpec>();
    }

    /// <summary>
    /// One entry of the engine's declared controllable surface. The client
    /// validates its own bindings against this at startup so a desynchronised
    /// build is caught immediately rather than when a trainee pulls a lever
    /// that turns out to do nothing.
    /// </summary>
    public class ControlSpec
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("kind")] public string Kind;      // valve | switch | button | analog | axis
        [JsonProperty("states")] public string[] States; // for valve/switch/button
        [JsonProperty("min")] public float Min;
        [JsonProperty("max")] public float Max;
    }

    public class AckPayload
    {
        [JsonProperty("cmd_id")] public string CommandId;
        [JsonProperty("status")] public string Status;  // accepted | rejected | superseded
        [JsonProperty("reason")] public string Reason;

        public bool Accepted => Status == "accepted";
        public bool Rejected => Status == "rejected";
        public bool Superseded => Status == "superseded";
    }

    public class RigEventPayload
    {
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("severity")] public string Severity; // info | warning | critical
        [JsonProperty("message")] public string Message;
        [JsonProperty("data")] public JObject Data;
    }

    public class CommandPayload
    {
        [JsonProperty("cmd_id")] public string CommandId;
        [JsonProperty("control")] public string Control;
        [JsonProperty("action")] public string Action = "set";
        [JsonProperty("value")] public object Value;
    }
}
