using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using VRSIM.Diagnostics;
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

            // -- the thing under test.
            var linkGo = new GameObject("Engine Link");
            var connection = linkGo.AddComponent<RigConnection>();
            connection.autoDiscover = true;
            connection.connectOnStart = true;
            connection.verboseLogging = true;
            connection.host = GuessLocalSubnetHint();

            linkGo.AddComponent<ConnectionHud>();

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
