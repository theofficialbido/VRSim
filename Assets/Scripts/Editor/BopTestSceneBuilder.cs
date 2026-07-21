using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using VRSIM.Diagnostics;
using VRSIM.Net;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Builds a standalone BOP panel with every lamp created and assigned, so
    /// the per-state light configuration can be watched working.
    ///
    /// Generated rather than hand-authored, and with no dependency on the main
    /// scene or the rig art, so that if a lamp does not light the cause is the
    /// engine link or the binding -- not something else in a 5,700-object scene.
    /// </summary>
    public static class BopTestSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/BOP Light Test.unity";
        private const string LampMaterialPath = "Assets/Materials/BopLamp.mat";

        private static readonly Color GreenOn = new Color(0.20f, 1f, 0.35f);
        private static readonly Color RedOn = new Color(1f, 0.20f, 0.15f);
        private static readonly Color Amber = new Color(1f, 0.75f, 0.10f);

        /// <summary>Panel rows: engine component id, label, extra ids sharing the lamp.</summary>
        private static readonly (string id, string label, string[] combine)[] Rows =
        {
            ("annular_1",       "ANNULAR",     null),
            ("pipe_ram_1",      "PIPE RAMS",   new[] { "pipe_ram_2", "pipe_ram_3" }),
            ("blind_shear_ram", "BLIND RAM",   null),
            ("blind_shear_ram", "SHEAR RAM",   null),
            ("kill_line",       "KILL LINE",   null),
            ("choke_line",      "CHOKE LINE",  null),
        };

        [MenuItem("VRSIM/Build BOP Light Test Scene", priority = 1)]
        public static void Build()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Overwrite BOP light test scene?",
                    $"{ScenePath} already exists and will be replaced.", "Replace", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lampMaterial = CreateLampMaterial();

            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2f;

            BuildXrRig();

            // -- panel backboard
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "BOP Panel";
            board.transform.position = new Vector3(0f, 1.35f, 1.2f);
            board.transform.localScale = new Vector3(1.5f, 1.0f, 0.06f);
            Object.DestroyImmediate(board.GetComponent<Collider>());

            var title = MakeLabel(board.transform, new Vector3(0f, 0.42f, -0.55f), 0.09f,
                                  "BOP STATUS", TextAlignmentOptions.Center);
            title.color = new Color(0.85f, 0.9f, 1f);

            var host = new GameObject("Panel Controller");
            var connection = host.AddComponent<RigConnection>();
            connection.autoDiscover = true;
            connection.connectOnStart = true;
            connection.verboseLogging = true;
            connection.host = LocalAddress();

            var panel = host.AddComponent<BOPPanelController>();
            panel.connection = connection;
            panel.components = new List<BOPPanelController.ComponentLights>();

            // -- one row per component, each with its own green and red lamp
            var top = 0.26f;
            var step = 0.115f;
            for (var i = 0; i < Rows.Length; i++)
            {
                var (id, label, combine) = Rows[i];
                var y = top - i * step;

                MakeLabel(board.transform, new Vector3(-0.28f, y, -0.55f), 0.055f,
                          label, TextAlignmentOptions.Right);

                var green = MakeLamp(board.transform, new Vector3(0.13f, y, -0.6f), lampMaterial,
                                     $"{label} Green");
                var red = MakeLamp(board.transform, new Vector3(0.30f, y, -0.6f), lampMaterial,
                                   $"{label} Red");

                panel.components.Add(new BOPPanelController.ComponentLights
                {
                    componentId = id,
                    label = label,
                    alsoCombine = new List<string>(combine ?? new string[0]),
                    lights = new List<BOPPanelController.StateLight>
                    {
                        // Green lamp: on for open, amber while travelling.
                        new BOPPanelController.StateLight { state = "open",   light = green, colour = GreenOn },
                        new BOPPanelController.StateLight { state = "moving", light = green, colour = Amber },
                        // Red lamp: on for closed and for fault.
                        new BOPPanelController.StateLight { state = "closed", light = red, colour = RedOn },
                        new BOPPanelController.StateLight { state = "fault",  light = red, colour = RedOn },
                    },
                });
            }

            // -- master valve gets its own row, and its own pair
            var masterY = top - Rows.Length * step - 0.03f;
            MakeLabel(board.transform, new Vector3(-0.28f, masterY, -0.55f), 0.055f,
                      "MASTER VALVE", TextAlignmentOptions.Right);
            var mvGreen = MakeLamp(board.transform, new Vector3(0.13f, masterY, -0.6f), lampMaterial, "Master Green");
            var mvRed = MakeLamp(board.transform, new Vector3(0.30f, masterY, -0.6f), lampMaterial, "Master Red");
            panel.masterValve = new BOPPanelController.ComponentLights
            {
                componentId = "master_valve",
                label = "Master Valve",
                alsoCombine = new List<string>(),
                lights = new List<BOPPanelController.StateLight>
                {
                    new BOPPanelController.StateLight { state = "open",   light = mvGreen, colour = GreenOn },
                    new BOPPanelController.StateLight { state = "closed", light = mvRed,   colour = RedOn },
                },
            };

            // Connection status, so a dark panel is never ambiguous between
            // "all lamps off" and "not connected".
            var hud = host.AddComponent<ConnectionHud>();
            hud.runSelfTest = false;
            hud.followCamera = true;
            hud.distance = 1.4f;
            hud.verticalOffset = -0.55f;
            hud.panelSize = new Vector2(0.7f, 0.5f);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            MakeFirstInBuild(ScenePath);

            EditorUtility.DisplayDialog("BOP light test scene ready",
                $"Created {ScenePath} with 7 components, 14 lamps, all assigned.\n\n" +
                "To watch the lights:\n" +
                "1. On the PC run:\n" +
                "     python tools/mock_engine/mock_engine.py --demo-bop\n" +
                "2. Press Play.\n\n" +
                "Each component toggles in turn. Watch a lamp go green (open), " +
                "amber for 2.5s (moving), then the red lamp (closed).",
                "OK");

            Selection.activeGameObject = host;
        }

        private static Material CreateLampMaterial()
        {
            Directory.CreateDirectory("Assets/Materials");
            // Unlit, so a lamp reads as lit from any angle without depending on
            // the emission keyword -- a MaterialPropertyBlock cannot enable
            // shader keywords, so an emissive Lit material would stay dark.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = "BopLamp" };
            AssetDatabase.CreateAsset(material, LampMaterialPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Material>(LampMaterialPath);
        }

        private static GameObject MakeLamp(Transform parent, Vector3 local, Material material, string name)
        {
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = name;
            lamp.transform.SetParent(parent, false);
            lamp.transform.localPosition = local;
            // The board is non-uniformly scaled, so undo it or the lamps distort.
            lamp.transform.localScale = new Vector3(0.055f / 1.5f, 0.055f / 1.0f, 0.055f / 0.06f);
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.GetComponent<Renderer>().sharedMaterial = material;
            // Off by default: the controller switches on whichever matches state.
            lamp.SetActive(false);
            return lamp;
        }

        private static TextMeshPro MakeLabel(Transform parent, Vector3 local, float size,
                                             string text, TextAlignmentOptions align)
        {
            var go = new GameObject($"Label {text}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(1f / 1.5f, 1f / 1.0f, 1f / 0.06f) * 0.1f;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = size * 60f;
            label.alignment = align;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(4.5f, 0.7f);
            return label;
        }

        private static void BuildXrRig()
        {
            var originGo = new GameObject("XR Origin");
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(offset.transform, false);
            cameraGo.tag = "MainCamera";
            cameraGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);

            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 200f;
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
        }

        private static string LocalAddress()
        {
            try
            {
                foreach (var address in System.Net.Dns.GetHostAddresses(System.Net.Dns.GetHostName()))
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                        && !address.ToString().StartsWith("127."))
                        return address.ToString();
            }
            catch (System.Exception) { /* discovery runs first anyway */ }
            return "127.0.0.1";
        }

        private static void MakeFirstInBuild(string path)
        {
            var kept = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != path) kept.Add(s);
            EditorBuildSettings.scenes = kept.ToArray();
        }
    }
}
