using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using VRSIM.Diagnostics;
using VRSIM.Interaction;
using VRSIM.Net;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Builds a minimal scene whose only job is to answer "does the link to the
    /// engine work on the actual headset?".
    ///
    /// It is generated rather than hand-authored so that it is reproducible and
    /// carries no incidental dependency on the 5,700-object main scene -- if
    /// the trial fails, the cause is the network or the build, not something
    /// in the rig.
    /// </summary>
    public static class ConnectionTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Connection Test.unity";

        [MenuItem("VRSIM/Build Connection Test Scene", priority = 0)]
        public static void Build()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog(
                    "Overwrite connection test scene?",
                    $"{ScenePath} already exists and will be replaced.\n\n" +
                    "Any changes you made to it by hand will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // -- lighting and a floor, purely so the panel has spatial context.
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2f;

            // -- XR rig. Without a TrackedPoseDriver the camera does not follow
            //    the head, which in VR is not merely wrong but nauseating.
            var originGo = new GameObject("XR Origin");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(offset.transform, false);
            cameraGo.tag = "MainCamera";

            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraGo.AddComponent<AudioListener>();

            var driver = cameraGo.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.positionInput = new InputActionProperty(
                new InputAction("Head Position", binding: "<XRHMD>/centerEyePosition",
                                expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction("Head Rotation", binding: "<XRHMD>/centerEyeRotation",
                                expectedControlType: "Quaternion"));

            var origin = originGo.AddComponent<XROrigin>();
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

            // -- tracked controllers. Needed so there is something to press
            //    with; without them the scene can only be looked at.
            var leftTip = BuildController(offset.transform, "Left Controller", "LeftHand");
            var rightTip = BuildController(offset.transform, "Right Controller", "RightHand");

            // -- the thing under test.
            var linkGo = new GameObject("Engine Link");
            var connection = linkGo.AddComponent<RigConnection>();
            connection.autoDiscover = true;
            connection.connectOnStart = true;
            connection.verboseLogging = true;
            connection.host = GuessLocalSubnetHint();

            // The automatic self-test drives pump 3, which has no button, so it
            // cannot fight the manual buttons for the same control.
            var hud = linkGo.AddComponent<ConnectionHud>();
            hud.selfTestControl = "console.pump_3";

            // -- the headline test: aim, pull the trigger, and watch the PC.
            //    A momentary press verified by a counter increasing, so every
            //    press is individually provable rather than being lost in a
            //    toggle that happens to already be in the right position.
            var diag = BuildButton(null, connection, leftTip, rightTip, 0f,
                "diag.test_button", "diag.button_presses", "press", "press", "", "");
            diag.verifyMode = RigCommandButton.VerifyMode.ValueIncreases;
            diag.kind = RigCommandButton.ButtonKind.Momentary;
            diag.transform.position = new Vector3(0f, 1.35f, 0.6f);
            diag.transform.localScale = new Vector3(0.34f, 0.09f, 0.16f);
            diag.name = "PRESS ME (diag.test_button)";

            // -- command buttons, to test the VR -> engine direction.
            var panel = new GameObject("Command Buttons").transform;
            panel.position = new Vector3(0f, 1.0f, 0.55f);

            BuildButton(panel, connection, leftTip, rightTip, -0.30f,
                "console.pump_1", "pumps.pump_1.active", "on", "off", "True", "False");
            BuildButton(panel, connection, leftTip, rightTip, -0.10f,
                "console.pump_2", "pumps.pump_2.active", "on", "off", "True", "False");
            BuildButton(panel, connection, leftTip, rightTip, 0.10f,
                "console.auto_drill", "drilling.auto_drill", "on", "off", "True", "False");
            // The annular is the interesting one: it reports "moving" for 2.5s
            // before it closes, so the button visibly stays amber after the ack
            // and only greens when state catches up.
            BuildButton(panel, connection, leftTip, rightTip, 0.30f,
                "bop.annular_1", "bop.components.annular_1", "closed", "open", "closed", "open");

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettingsFirst(ScenePath);

            EditorUtility.DisplayDialog(
                "Connection test scene ready",
                $"Created {ScenePath} and made it the first scene in Build Settings, " +
                "so an APK will boot straight into it.\n\n" +
                "Next:\n" +
                "1. On the PC, run:  python tools/mock_engine/mock_engine.py\n" +
                "2. Allow it through Windows Firewall on PRIVATE networks.\n" +
                "3. Select 'Engine Link' and set 'host' to the PC's IP if discovery fails.\n" +
                "4. File > Build And Run with the headset attached.",
                "OK");

            Selection.activeGameObject = linkGo;
            Debug.Log($"[VRSIM] Built {ScenePath}. Engine Link host hint: {connection.host}");
        }

        /// <summary>
        /// A tracked controller with a visible tip. Returns the tip transform,
        /// which is what presses buttons.
        /// </summary>
        private static Transform BuildController(Transform parent, string name, string hand)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var driver = go.AddComponent<TrackedPoseDriver>();
            driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.positionInput = new InputActionProperty(
                new InputAction($"{name} Position", binding: $"<XRController>{{{hand}}}/devicePosition",
                                expectedControlType: "Vector3"));
            driver.rotationInput = new InputActionProperty(
                new InputAction($"{name} Rotation", binding: $"<XRController>{{{hand}}}/deviceRotation",
                                expectedControlType: "Quaternion"));

            // A small sphere so the controller is visible, and slightly ahead of
            // the grip so it reads as a fingertip rather than a fist.
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.045f);
            tip.transform.localScale = Vector3.one * 0.028f;
            // No collider: it must not block the pointer's own raycast.
            Object.DestroyImmediate(tip.GetComponent<Collider>());

            // Aim-and-trigger, in addition to touching buttons directly.
            var pointer = go.AddComponent<ControllerRayPointer>();
            pointer.hand = hand;

            return tip.transform;
        }

        private static RigCommandButton BuildButton(Transform parent, RigConnection connection,
                                                    Transform leftTip, Transform rightTip, float x,
                                                    string controlId, string statePath,
                                                    string onCommand, string offCommand,
                                                    string onState, string offState)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = $"Button {controlId}";
            if (parent != null) body.transform.SetParent(parent, false);
            body.transform.localPosition = new Vector3(x, 0f, 0f);
            body.transform.localScale = new Vector3(0.13f, 0.05f, 0.09f);
            // Collider kept: the pointer raycasts against it.

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(body.transform, false);
            // The parent cube is non-uniformly scaled, so undo it or the text shears.
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
            button.activators = new System.Collections.Generic.List<Transform> { leftTip, rightTip };
            return button;
        }

        /// <summary>
        /// Pre-fills the manual fallback with this machine's LAN address, which
        /// is almost always the right answer during bring-up and saves reading
        /// it off ipconfig. Discovery is tried first regardless.
        /// </summary>
        private static string GuessLocalSubnetHint()
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
                // Non-fatal: the field is only a fallback, and discovery runs first.
            }
            return "192.168.1.100";
        }

        private static void AddToBuildSettingsFirst(string path)
        {
            var existing = EditorBuildSettings.scenes;
            var kept = new System.Collections.Generic.List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(path, true),
            };
            foreach (var scene in existing)
            {
                if (scene.path != path) kept.Add(scene);
            }
            EditorBuildSettings.scenes = kept.ToArray();
        }

        [MenuItem("VRSIM/Make Main Scene First In Build", priority = 1)]
        public static void RestoreMainScene()
        {
            AddToBuildSettingsFirst("Assets/Scenes/Main Scene.unity");
            Debug.Log("[VRSIM] Main Scene is now first in Build Settings.");
        }
    }
}
