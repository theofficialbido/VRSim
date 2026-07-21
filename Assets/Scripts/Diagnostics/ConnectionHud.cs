using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using VRSIM.Net;

namespace VRSIM.Diagnostics
{
    /// <summary>
    /// A world-space panel that shows, inside the headset, whether the link to
    /// the engine actually works.
    ///
    /// It builds its own canvas at runtime rather than relying on scene
    /// authoring, so it can be dropped onto an empty GameObject and will render
    /// identically in the Editor and in an APK. That matters because the whole
    /// point of it is to answer "does this work on the device?", and a panel
    /// that needs scene setup to appear is one more thing that can be wrong.
    ///
    /// It also runs an optional self-test that sends a real command on a timer,
    /// so a single glance confirms both directions of the protocol rather than
    /// just that bytes are arriving.
    /// </summary>
    [RequireComponent(typeof(RigConnection))]
    public class ConnectionHud : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Follows the camera so the panel is always readable. Turn off to place it by hand.")]
        public bool followCamera = true;
        public float distance = 1.6f;
        public float verticalOffset = -0.15f;
        public Vector2 panelSize = new Vector2(0.9f, 0.62f);

        [Header("Self-test")]
        [Tooltip("Periodically send a real command, to prove VR -> engine works.")]
        public bool runSelfTest = true;
        public float selfTestInterval = 5f;
        [Tooltip("Must be a control the engine declares in hello.ack.")]
        public string selfTestControl = "console.pump_1";

        private RigConnection _connection;
        private TextMeshProUGUI _text;
        private Transform _panel;
        private Camera _camera;

        private string _lastAck = "none yet";
        private string _lastEvent = "none yet";
        private int _commandsSent;
        private int _commandsAccepted;

        private void Awake()
        {
            _connection = GetComponent<RigConnection>();
            BuildCanvas();
        }

        private void OnEnable()
        {
            _connection.CommandAcked += OnAck;
            _connection.EngineEvent += OnEngineEvent;
        }

        private void OnDisable()
        {
            _connection.CommandAcked -= OnAck;
            _connection.EngineEvent -= OnEngineEvent;
        }

        private void Start()
        {
            _camera = Camera.main;
            if (runSelfTest) StartCoroutine(SelfTestLoop());
        }

        private void OnAck(AckPayload ack)
        {
            if (ack.Accepted) _commandsAccepted++;
            _lastAck = ack.Accepted
                ? $"<color=#7CFC98>accepted</color>"
                : $"<color=#FF8A7A>{ack.Status}: {ack.Reason}</color>";
        }

        private void OnEngineEvent(RigEventPayload evt)
        {
            var colour = evt.Severity == "critical" ? "#FF6B6B"
                       : evt.Severity == "warning" ? "#FFD166" : "#9AD5FF";
            _lastEvent = $"<color={colour}>{evt.Message}</color>";
        }

        private IEnumerator SelfTestLoop()
        {
            var on = true;
            while (true)
            {
                yield return new WaitForSeconds(selfTestInterval);
                if (_connection.Status != ConnectionStatus.Connected) continue;
                if (!_connection.HasControl(selfTestControl))
                {
                    // The engine never declared this control. Saying so is more
                    // useful than silently sending commands into a void.
                    _lastAck = $"<color=#FFD166>engine does not declare {selfTestControl}</color>";
                    continue;
                }

                on = !on;
                _commandsSent++;
                _connection.SendCommand(selfTestControl, on ? "on" : "off");
            }
        }

        private void LateUpdate()
        {
            if (followCamera)
            {
                if (_camera == null) _camera = Camera.main;
                if (_camera != null)
                {
                    var cam = _camera.transform;
                    var forward = cam.forward;
                    _panel.position = cam.position + forward * distance + Vector3.up * verticalOffset;
                    _panel.rotation = Quaternion.LookRotation(_panel.position - cam.position, Vector3.up);
                }
            }

            _text.text = Compose();
        }

        private string Compose()
        {
            var s = _connection.State;
            var sb = new StringBuilder(1024);

            sb.AppendLine("<b>VRSIM  ·  engine link</b>");
            sb.AppendLine($"<size=70%>protocol v{Protocol.Version}</size>");
            sb.AppendLine();

            sb.AppendLine($"{StatusColour(_connection.Status)}  {_connection.Status}</color>");
            sb.AppendLine($"<size=80%>{_connection.StatusDetail}</size>");

            if (_connection.Status == ConnectionStatus.Connected)
            {
                var latency = _connection.LatencyMs < 0 ? "measuring…" : $"{_connection.LatencyMs:0} ms";
                sb.AppendLine($"<size=80%>engine {_connection.EngineVersion} · {_connection.Controls.Length} controls · {latency}</size>");
            }
            sb.AppendLine();

            if (!s.HasData)
            {
                sb.AppendLine("<color=#AAAAAA>waiting for first snapshot…</color>");
                return sb.ToString();
            }

            // Stale is called out loudly and separately from disconnected. A
            // frozen gauge showing a plausible number reads as live, which is
            // the more dangerous of the two failures.
            if (s.IsStale)
                sb.AppendLine($"<color=#FF6B6B><b>STALE — no update for {s.SecondsSinceUpdate:0.0}s</b></color>\n");

            var d = s.Snapshot.Drilling;
            sb.AppendLine($"bit depth   <b>{d.BitDepthFt:0.0}</b> ft");
            sb.AppendLine($"rpm         <b>{d.Rpm:0}</b>      wob <b>{d.WobKlbs:0.0}</b> klbs");
            sb.AppendLine($"hookload    <b>{d.HookloadKlbs:0.0}</b> klbs");
            sb.AppendLine($"tds         <b>{d.TdsPositionFt:0.0}</b> ft ({d.TdsMovement})");

            var p = s.Snapshot.Pumps;
            sb.AppendLine($"pumps       {Dot(p.Pump1.Active)} {Dot(p.Pump2.Active)} {Dot(p.Pump3.Active)}   flow <b>{p.TotalFlowGpm:0}</b> gpm");

            var bop = s.Snapshot.Bop;
            sb.AppendLine($"annular     {ValveColour(bop.ComponentState("annular_1"))}   master {ValveColour(bop.MasterValve)}");
            sb.AppendLine();

            sb.AppendLine($"<size=80%>self-test  {_commandsAccepted}/{_commandsSent} accepted · last {_lastAck}</size>");
            sb.AppendLine($"<size=80%>last event {_lastEvent}</size>");

            return sb.ToString();
        }

        private static string Dot(bool on) =>
            on ? "<color=#7CFC98>●</color>" : "<color=#666666>●</color>";

        private static string ValveColour(string state)
        {
            switch (state)
            {
                case "open": return "<color=#7CFC98>open</color>";
                case "closed": return "<color=#FF8A7A>closed</color>";
                case "moving": return "<color=#FFD166>moving…</color>";
                default: return $"<color=#AAAAAA>{state}</color>";
            }
        }

        private static string StatusColour(ConnectionStatus status)
        {
            switch (status)
            {
                case ConnectionStatus.Connected: return "<color=#7CFC98>●";
                case ConnectionStatus.Fatal: return "<color=#FF6B6B>●";
                case ConnectionStatus.Reconnecting: return "<color=#FFD166>●";
                default: return "<color=#9AD5FF>●";
            }
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("Connection HUD Canvas", typeof(Canvas), typeof(CanvasGroup));
            canvasGo.transform.SetParent(transform, false);
            _panel = canvasGo.transform;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000f, 700f);
            // Scale the 1000x700 reference resolution down to the requested
            // physical size in metres.
            rect.localScale = Vector3.one * (panelSize.x / 1000f);

            var bg = new GameObject("Background", typeof(UnityEngine.UI.Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = bgRect.offsetMax = Vector2.zero;
            bg.GetComponent<UnityEngine.UI.Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.88f);

            var textGo = new GameObject("Text", typeof(TextMeshProUGUI));
            textGo.transform.SetParent(canvasGo.transform, false);
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(28f, 24f);
            textRect.offsetMax = new Vector2(-28f, -24f);

            _text = textGo.GetComponent<TextMeshProUGUI>();
            _text.fontSize = 30f;
            _text.color = Color.white;
            _text.richText = true;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.text = "starting…";
        }
    }
}
