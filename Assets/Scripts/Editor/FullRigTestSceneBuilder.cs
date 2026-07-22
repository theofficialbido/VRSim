using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.UI;
using VRSIM.Diagnostics;
using VRSIM.Interaction;
using VRSIM.Locomotion;
using VRSIM.Net;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Builds one scene that exercises everything at once: the engine link, both
    /// directions of the protocol, both styles of teleport, continuous
    /// locomotion, and the panel lamps.
    ///
    /// It exists because the pieces have only ever been proven separately. The
    /// connection scene proves the socket, the BOP scene proves the lamps, and
    /// the main scene is too heavy to tell you which of them broke. This one is
    /// small enough to hold 72 Hz on a Quest 2 with room to spare, so anything
    /// that stutters here is the code and not the rig.
    ///
    /// Generated rather than hand-authored, so it is reproducible and can be
    /// rebuilt from scratch after any change.
    /// </summary>
    public static class FullRigTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Full Rig Test.unity";

        private static readonly Color GreenOn = new Color(0.20f, 1f, 0.35f);
        private static readonly Color RedOn = new Color(1f, 0.20f, 0.15f);
        private static readonly Color Amber = new Color(1f, 0.75f, 0.10f);

        /// <summary>BOP components, per docs/protocol.md. Ids must match exactly.</summary>
        private static readonly (string Id, string Label)[] BopRows =
        {
            ("annular_1", "ANNULAR"),
            ("pipe_ram_1", "PIPE RAM"),
            ("blind_shear_ram", "BLIND SHEAR"),
            ("kill_line", "KILL LINE"),
            ("choke_line", "CHOKE LINE"),
        };

        [MenuItem("VRSIM/Build Full Rig Test Scene", priority = 2)]
        public static void Build()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Overwrite full rig test scene?",
                    $"{ScenePath} already exists and will be replaced.\n\n" +
                    "Any changes you made to it by hand will be lost.", "Replace", "Cancel"))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            var deck = BuildGroundAndDecks();

            var manager = new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();
            var rig = BuildRig(manager);

            var connection = BuildEngineLink();
            BuildCommandButtons(connection, rig);
            BuildBopPanel(connection);
            BuildTeleportButtons(rig, deck);
            BuildInstructions();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            MakeFirstInBuild(ScenePath);

            EditorUtility.DisplayDialog("Full rig test scene ready",
                $"Created {ScenePath} and made it first in Build Settings.\n\n" +
                "It covers:\n" +
                "  - engine link, with the connection HUD\n" +
                "  - VR -> engine: command buttons with round-trip verification\n" +
                "  - engine -> VR: BOP lamps driven by state\n" +
                "  - teleport by ray onto the ground and the raised deck\n" +
                "  - teleport by button, through TeleportToAnchor\n" +
                "  - continuous move on the left stick, snap turn on the right\n\n" +
                "To run it:\n" +
                "1. On the PC:  python tools/mock_engine/mock_engine.py\n" +
                "2. Allow it through Windows Firewall on PRIVATE networks.\n" +
                "3. Select 'Engine Link' and set 'host' if discovery fails.\n" +
                "4. Build And Run with the headset attached.", "OK");

            Debug.Log($"[VRSIM] Built {ScenePath}.");
        }

        // ---- world -------------------------------------------------------------

        private static void BuildLighting()
        {
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Hard;
            light.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        }

        /// <summary>
        /// Ground plus a raised deck, so teleporting between levels is covered
        /// and not just movement across a flat floor -- getting up onto the drill
        /// floor is the case that actually matters on the rig.
        /// </summary>
        private static Transform BuildGroundAndDecks()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground (teleport area)";
            ground.transform.localScale = Vector3.one * 4f;
            ground.AddComponent<TeleportationArea>();

            var deck = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deck.name = "Raised Deck (teleport area)";
            deck.transform.position = new Vector3(6f, 1.5f, 0f);
            deck.transform.localScale = new Vector3(6f, 3f, 6f);
            deck.AddComponent<TeleportationArea>();

            return deck.transform;
        }

        // ---- rig ---------------------------------------------------------------

        private struct Rig
        {
            public GameObject Origin;
            public Transform LeftTip;
            public Transform RightTip;
        }

        private static Rig BuildRig(XRInteractionManager manager)
        {
            var originGo = new GameObject("XR Origin");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(offset.transform, false);
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            // 0.01, not the 0.3 the main scene ships: at 0.3 anything you lean
            // towards or reach for is clipped away.
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraGo.AddComponent<AudioListener>();
            AddPoseDriver(cameraGo, "Head", "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // LocomotionMediator pulls in XRBodyTransformer through RequireComponent.
            var mediator = originGo.AddComponent<LocomotionMediator>();
            mediator.xrOrigin = origin;

            var teleport = originGo.AddComponent<TeleportationProvider>();
            teleport.mediator = mediator;

            var move = originGo.AddComponent<ContinuousMoveProvider>();
            move.mediator = mediator;
            move.moveSpeed = 2.2f;          // brisk walk; faster reads as nauseating
            move.enableStrafe = true;
            move.forwardSource = cameraGo.transform;
            BindStick(move.leftHandMoveInput, "Move", "<XRController>{LeftHand}/thumbstick");

            // Snap rather than smooth turn: smooth turning is the single most
            // reliable way to make people sick in VR.
            var turn = originGo.AddComponent<SnapTurnProvider>();
            turn.mediator = mediator;
            turn.turnAmount = 45f;
            BindStick(turn.rightHandTurnInput, "Turn", "<XRController>{RightHand}/thumbstick");

            var left = BuildController(offset.transform, manager, teleport, "Left Controller", "LeftHand");
            var right = BuildController(offset.transform, manager, teleport, "Right Controller", "RightHand");

            return new Rig { Origin = originGo, LeftTip = left, RightTip = right };
        }

        private static Transform BuildController(Transform parent, XRInteractionManager manager,
                                                 TeleportationProvider teleport,
                                                 string name, string hand)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            AddPoseDriver(go, name, $"<XRController>{{{hand}}}/devicePosition",
                          $"<XRController>{{{hand}}}/deviceRotation");

            // XRI ray, for selecting teleport areas and anchors.
            var ray = go.AddComponent<XRRayInteractor>();
            ray.interactionManager = manager;
            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            ray.selectInput.inputActionPerformed =
                new InputAction($"{name} Teleport", InputActionType.Button,
                                $"<XRController>{{{hand}}}/triggerPressed");

            var line = go.AddComponent<LineRenderer>();
            line.widthMultiplier = 0.005f;
            line.material = new Material(Shader.Find("Sprites/Default"));
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();

            // A visible fingertip that presses the command buttons directly, and
            // the project's own pointer for anything implementing IRigPressable.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            tip.transform.localScale = Vector3.one * 0.028f;
            Object.DestroyImmediate(tip.GetComponent<Collider>());

            var pointer = go.AddComponent<ControllerRayPointer>();
            pointer.hand = hand;

            return tip.transform;
        }

        private static void AddPoseDriver(GameObject go, string label, string position, string rotation)
        {
            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.positionInput = new InputActionProperty(
                new InputAction($"{label} Position", binding: position, expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction($"{label} Rotation", binding: rotation, expectedControlType: "Quaternion"));
        }

        private static void BindStick(XRInputValueReader<Vector2> reader, string label, string binding)
        {
            reader.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
            reader.inputAction = new InputAction(label, InputActionType.Value, binding,
                                                 expectedControlType: "Vector2");
        }

        // ---- engine ------------------------------------------------------------

        private static RigConnection BuildEngineLink()
        {
            var go = new GameObject("Engine Link");
            var connection = go.AddComponent<RigConnection>();
            connection.autoDiscover = true;
            connection.connectOnStart = true;
            connection.verboseLogging = true;
            connection.host = LocalAddress();

            // Self-test drives pump 3, which has no button here, so it cannot
            // fight a manual press for the same control.
            var hud = go.AddComponent<ConnectionHud>();
            hud.selfTestControl = "console.pump_3";
            return connection;
        }

        private static void BuildCommandButtons(RigConnection connection, Rig rig)
        {
            var panel = new GameObject("Command Buttons").transform;
            panel.position = new Vector3(0f, 1.05f, 1.1f);

            MakeButton(panel, connection, rig, -0.30f, "console.pump_1",
                       "pumps.pump_1.active", "on", "off", "True", "False");
            MakeButton(panel, connection, rig, -0.10f, "console.pump_2",
                       "pumps.pump_2.active", "on", "off", "True", "False");
            MakeButton(panel, connection, rig, 0.10f, "console.auto_drill",
                       "drilling.auto_drill", "on", "off", "True", "False");
            // The annular reports "moving" for 2.5 s before it closes, so this
            // button visibly stays amber after the ack and only greens when state
            // catches up. That gap is the whole point of the round-trip check.
            MakeButton(panel, connection, rig, 0.30f, "bop.annular_1",
                       "bop.components.annular_1", "closed", "open", "closed", "open");
        }

        private static void MakeButton(Transform parent, RigConnection connection, Rig rig, float x,
                                       string controlId, string statePath, string onCommand,
                                       string offCommand, string onState, string offState)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = $"Button {controlId}";
            body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(x, 0f, 0f);
            body.transform.localScale = new Vector3(0.13f, 0.05f, 0.09f);

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(body.transform, false);
            labelGo.transform.localScale = new Vector3(1f / 0.13f, 1f / 0.05f, 1f / 0.09f) * 0.01f;
            labelGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            var label = labelGo.AddComponent<TextMeshPro>();
            label.fontSize = 3.2f;
            label.alignment = TextAlignmentOptions.Center;
            label.text = controlId;
            label.rectTransform.sizeDelta = new Vector2(18f, 6f);

            var button = body.AddComponent<RigCommandButton>();
            button.connection = connection;
            button.controlId = controlId;
            button.statePath = statePath;
            button.onCommandValue = onCommand;
            button.offCommandValue = offCommand;
            button.onStateValue = onState;
            button.offStateValue = offState;
            button.label = label;
            button.activators = new List<Transform> { rig.LeftTip, rig.RightTip };
        }

        // ---- panel -------------------------------------------------------------

        /// <summary>
        /// A lamp row per BOP component, driven by state rather than by the press
        /// that requested it -- the panel can never claim a ram is shut that the
        /// engine does not agree is shut.
        /// </summary>
        private static void BuildBopPanel(RigConnection connection)
        {
            var panel = new GameObject("BOP Panel").transform;
            panel.position = new Vector3(-1.6f, 1.35f, 1.1f);
            panel.rotation = Quaternion.Euler(0f, -20f, 0f);

            var controller = panel.gameObject.AddComponent<BOPPanelController>();
            controller.connection = connection;
            controller.components = new List<BOPPanelController.ComponentLights>();

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.EnableKeyword("_EMISSION");

            for (var i = 0; i < BopRows.Length; i++)
            {
                var row = BopRows[i];
                var y = -i * 0.14f;

                MakeLabel(panel, new Vector3(-0.30f, y, 0f), row.Label);
                var green = MakeLamp(panel, new Vector3(0.02f, y, 0f), material, row.Id + " green");
                var red = MakeLamp(panel, new Vector3(0.14f, y, 0f), material, row.Id + " red");

                controller.components.Add(new BOPPanelController.ComponentLights
                {
                    componentId = row.Id,
                    label = row.Label,
                    lights = new List<BOPPanelController.StateLight>
                    {
                        new BOPPanelController.StateLight { state = "open", light = green, colour = GreenOn },
                        // Same object as "open", in amber. The controller collects
                        // what should be lit before switching anything off, so
                        // sharing a lamp across states does not black the row out.
                        new BOPPanelController.StateLight { state = "moving", light = green, colour = Amber },
                        new BOPPanelController.StateLight { state = "closed", light = red, colour = RedOn },
                    },
                });
            }
        }

        private static GameObject MakeLamp(Transform parent, Vector3 local, Material material, string name)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = name;
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = local;
            lamp.transform.localScale = Vector3.one * 0.075f;
            lamp.GetComponent<Renderer>().sharedMaterial = material;
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            return lamp;
        }

        private static void MakeLabel(Transform parent, Vector3 local, string text)
        {
            var go = new GameObject($"Label {text}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * 0.01f;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = 4f;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.rectTransform.sizeDelta = new Vector2(26f, 8f);
        }

        // ---- teleport buttons ---------------------------------------------------

        /// <summary>
        /// World-space buttons driving <see cref="TeleportToAnchor"/>, the same
        /// path the main scene uses after the teleport migration -- one component
        /// on the panel, and each button passes its own destination through the
        /// OnClick argument.
        /// </summary>
        private static void BuildTeleportButtons(Rig rig, Transform deck)
        {
            var groundAnchor = MakeAnchor("Anchor - Ground", new Vector3(-3f, 0f, 3f));
            var deckAnchor = MakeAnchor("Anchor - Raised Deck",
                                        deck.position + new Vector3(0f, 1.5f, 0f));

            var canvasGo = new GameObject("Teleport Buttons",
                typeof(Canvas), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.transform.position = new Vector3(1.6f, 1.35f, 1.1f);
            canvasGo.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(400f, 260f);
            rect.localScale = Vector3.one * 0.0016f;

            // One component for the whole panel; each button carries its own
            // destination in the OnClick argument.
            var teleporter = canvasGo.AddComponent<TeleportToAnchor>();

            MakeTeleportButton(canvasGo.transform, teleporter, groundAnchor, "GROUND", 60f);
            MakeTeleportButton(canvasGo.transform, teleporter, deckAnchor, "RAISED DECK", -60f);

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem));
                events.AddComponent<XRUIInputModule>();
            }
        }

        private static TeleportationAnchor MakeAnchor(string name, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.position = position + new Vector3(0f, 0.01f, 0f);
            go.transform.localScale = new Vector3(0.8f, 0.01f, 0.8f);

            var anchor = go.AddComponent<TeleportationAnchor>();
            var marker = new GameObject("Anchor Transform");
            marker.transform.SetParent(go.transform, false);
            anchor.teleportAnchorTransform = marker.transform;
            return anchor;
        }

        private static void MakeTeleportButton(Transform parent, TeleportToAnchor teleporter,
                                               TeleportationAnchor anchor, string text, float y)
        {
            var go = new GameObject($"Teleport {text}", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(340f, 96f);
            rect.anchoredPosition = new Vector2(0f, y);
            go.GetComponent<Image>().color = new Color(0.16f, 0.22f, 0.32f, 0.95f);

            var labelGo = new GameObject("Text", typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;

            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 34f;
            label.alignment = TextAlignmentOptions.Center;

            // A persistent listener, exactly as if it had been dragged in: the
            // destination lives on the button, so nothing needs a component per
            // teleport target.
            UnityEventTools.AddObjectPersistentListener(
                go.GetComponent<Button>().onClick, teleporter.TeleportTo, anchor);
        }

        // ---- plumbing ------------------------------------------------------------

        private static void BuildInstructions()
        {
            var go = new GameObject("Instructions");
            go.transform.position = new Vector3(0f, 2.15f, 1.1f);
            go.transform.localScale = Vector3.one * 0.01f;

            var text = go.AddComponent<TextMeshPro>();
            text.text =
                "<b>VRSIM full rig test</b>\n" +
                "<size=70%>left stick move · right stick snap turn\n" +
                "trigger to teleport onto ground or deck\n" +
                "touch a button to command the engine\n" +
                "lamps follow engine state, not your press</size>";
            text.fontSize = 5f;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = new Vector2(60f, 20f);
        }

        private static string LocalAddress()
        {
            try
            {
                foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                {
                    if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    var text = address.ToString();
                    if (!text.StartsWith("127.")) return text;
                }
            }
            catch (System.Exception)
            {
                // Non-fatal: discovery runs first and this is only the fallback.
            }
            return "192.168.1.100";
        }

        private static void MakeFirstInBuild(string path)
        {
            var kept = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.path != path) kept.Add(scene);
            EditorBuildSettings.scenes = kept.ToArray();
        }
    }
}
