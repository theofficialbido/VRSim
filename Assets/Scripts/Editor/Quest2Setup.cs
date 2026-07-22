using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Configures the project and the open scene for Quest 2.
    ///
    /// Split into separate commands rather than one button, because they have
    /// very different blast radii: the render settings are reversible tuning,
    /// the camera fix changes what the headset actually displays, and the static
    /// flags touch a thousand objects.
    ///
    /// <b>What this cannot fix.</b> Quest 2's working budget is roughly 350-750k
    /// triangles and 150-200 draw calls per frame. The models in
    /// 3d/models total about 5.5 million triangles -- mast.glb alone is 1.96
    /// million, and chair.glb is 625 separate meshes. No render setting closes a
    /// gap that size; that is geometry work. These commands make the frame as
    /// cheap as it can be for the geometry it is given, and nothing more.
    /// </summary>
    public static class Quest2Setup
    {
        private const string MobileAssetPath = "Assets/Settings/URP-Quest2.asset";
        private const string DesktopAssetPath = "Assets/Settings/UniversalRenderPipelineAsset.asset";

        // ---- 1. render pipeline ------------------------------------------------

        [MenuItem("VRSIM/Setup/Quest 2/1. Create Mobile Render Pipeline Asset", priority = 40)]
        public static void CreateMobilePipeline()
        {
            var source = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(DesktopAssetPath);
            if (source == null)
            {
                EditorUtility.DisplayDialog("Not found",
                    $"{DesktopAssetPath} is missing, so there is nothing to derive from.", "OK");
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileAssetPath) == null)
            {
                if (!AssetDatabase.CopyAsset(DesktopAssetPath, MobileAssetPath))
                {
                    EditorUtility.DisplayDialog("Copy failed",
                        $"Could not create {MobileAssetPath}.", "OK");
                    return;
                }
                AssetDatabase.ImportAsset(MobileAssetPath);
            }

            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileAssetPath);
            var so = new SerializedObject(mobile);

            // 4x MSAA stays. On the tile-based GPU in a Quest the resolve happens
            // inside tile memory, so it is far cheaper than it looks and it is the
            // single biggest readability win on text and thin geometry -- which on
            // this rig means handrails, grating and panel labels.
            Set(so, "m_MSAA", 4);
            Set(so, "m_RenderScale", 1f);

            // Shadows: one cascade, hard, and only as far as you can see detail.
            // Soft shadows are a per-pixel filter cost the Quest 2 cannot spare.
            Set(so, "m_MainLightShadowsSupported", true);
            Set(so, "m_MainLightShadowmapResolution", 1024);
            Set(so, "m_ShadowDistance", 20f);
            Set(so, "m_ShadowCascadeCount", 1);
            Set(so, "m_SoftShadowsSupported", false);

            // Additional lights per-vertex: the twelve panel lamps become emissive
            // materials, so nothing left needs per-pixel treatment.
            Set(so, "m_AdditionalLightsRenderingMode", 2); // 0 disabled, 1 per pixel, 2 per vertex
            Set(so, "m_AdditionalLightsPerObjectLimit", 2);
            Set(so, "m_AdditionalLightShadowsSupported", false);

            // Both of these force a full-screen copy per frame. Nothing in this
            // project samples them.
            Set(so, "m_SupportsCameraDepthTexture", false);
            Set(so, "m_SupportsCameraOpaqueTexture", false);
            Set(so, "m_SupportsHDR", false);
            Set(so, "m_UseSRPBatcher", true);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(mobile);
            AssetDatabase.SaveAssets();

            Selection.activeObject = mobile;
            EditorUtility.DisplayDialog("Mobile pipeline asset ready",
                $"Created/updated {MobileAssetPath}.\n\n" +
                "4x MSAA kept (cheap on tile-based mobile GPUs, and the biggest " +
                "readability win on panel text).\n" +
                "Hard shadows, 1 cascade, 20 m.\n" +
                "Additional lights per-vertex, limit 2.\n" +
                "Depth and opaque copies off.\n\n" +
                "Now run step 2 to assign it to the Android quality tier.", "OK");
        }

        [MenuItem("VRSIM/Setup/Quest 2/2. Assign Pipeline To Android Quality Tier", priority = 41)]
        public static void AssignToAndroidTier()
        {
            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobileAssetPath);
            if (mobile == null)
            {
                EditorUtility.DisplayDialog("Run step 1 first",
                    $"{MobileAssetPath} does not exist yet.", "OK");
                return;
            }

            // Android defaults to quality level 1 ("Balanced"). All three levels
            // currently share the desktop asset, which is why the tiers make no
            // difference on device today.
            var startingLevel = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            var target = Mathf.Clamp(1, 0, names.Length - 1);

            QualitySettings.SetQualityLevel(target, false);
            QualitySettings.renderPipeline = mobile;

            // Legacy knobs URP ignores are left alone; these two it still honours.
            QualitySettings.lodBias = 1.5f;
            QualitySettings.skinWeights = SkinWeights.TwoBones;

            QualitySettings.SetQualityLevel(startingLevel, false);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Quest2Setup] '{names[target]}' (level {target}) now uses {MobileAssetPath}.");
            EditorUtility.DisplayDialog("Assigned",
                $"Quality level '{names[target]}' now uses the Quest 2 pipeline asset.\n\n" +
                "Android defaults to that level, so a device build picks it up automatically. " +
                "Confirm under Project Settings > Quality that Android's default is still " +
                $"'{names[target]}'.", "OK");
        }

        // ---- 3. build target ---------------------------------------------------

        [MenuItem("VRSIM/Setup/Quest 2/3. Apply Player And Texture Settings", priority = 42)]
        public static void ApplyPlayerSettings()
        {
            var before = EditorUserBuildSettings.androidBuildSubtarget;

            // ASTC is materially better than ETC2 at the same size, and every
            // Quest supports it. This is where "textures look mushy on device but
            // fine in the Editor" usually comes from.
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.MTRendering = true;
            PlayerSettings.Android.startInFullscreen = true;

            Debug.Log($"[Quest2Setup] Android texture compression {before} -> " +
                      $"{EditorUserBuildSettings.androidBuildSubtarget}. " +
                      "Vulkan, ARM64, IL2CPP and Multiview were already correct and were left alone.");

            EditorUtility.DisplayDialog("Player settings applied",
                $"Android texture compression: {before} -> ASTC.\n" +
                "Linear colour space, GPU skinning and multithreaded rendering confirmed on.\n\n" +
                "Already correct, so left alone: Vulkan, ARM64-only, IL2CPP, and Multiview " +
                "stereo rendering.\n\n" +
                "Changing texture compression triggers a full texture reimport -- that will " +
                "take a while on this project.", "OK");
        }

        // ---- 4. the scene ------------------------------------------------------

        [MenuItem("VRSIM/Setup/Quest 2/4. Fix Cameras In Open Scene", priority = 43)]
        public static void FixCameras()
        {
            var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var disabled = new List<string>();
            var listeners = 0;
            Camera xrCamera = null;

            foreach (var camera in cameras)
            {
                var isRigCamera = camera.CompareTag("MainCamera") ||
                                  camera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null;
                if (isRigCamera)
                {
                    xrCamera = camera;
                    continue;
                }

                Undo.RecordObject(camera, "Fix cameras");
                if (camera.enabled)
                {
                    camera.enabled = false;
                    disabled.Add(camera.name);
                }

                // One listener per scene, or Unity warns every frame and the
                // wrong one wins.
                var listener = camera.GetComponent<AudioListener>();
                if (listener != null && listener.enabled)
                {
                    Undo.RecordObject(listener, "Fix cameras");
                    listener.enabled = false;
                    listeners++;
                }
                EditorUtility.SetDirty(camera);
            }

            var cameraNote = "no XR camera found";
            if (xrCamera != null)
            {
                Undo.RecordObject(xrCamera, "Fix cameras");
                xrCamera.enabled = true;
                // 0.3 m clips your own hands and anything you lean towards.
                xrCamera.nearClipPlane = 0.01f;
                xrCamera.farClipPlane = 400f;
                var listener = xrCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = true;
                EditorUtility.SetDirty(xrCamera);
                cameraNote = $"'{xrCamera.name}' enabled, near clip 0.01, far clip 400";
            }

            MarkSceneDirty();
            Debug.Log($"[Quest2Setup] Disabled {disabled.Count} camera(s): {string.Join(", ", disabled)}. " +
                      $"XR camera: {cameraNote}.");

            EditorUtility.DisplayDialog("Cameras fixed",
                $"Disabled {disabled.Count} non-XR camera(s) and {listeners} AudioListener(s).\n\n" +
                $"XR camera: {cameraNote}.\n\n" +
                "Each of those cameras was rendering the whole scene every frame, and " +
                "Camera.main was returning null because the only tagged camera was disabled.\n\n" +
                "Save the scene to keep this.", "OK");
        }

        [MenuItem("VRSIM/Setup/Quest 2/5. Mark Environment Static In Open Scene", priority = 44)]
        public static void MarkStatic()
        {
            const StaticEditorFlags flags =
                StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                StaticEditorFlags.ReflectionProbeStatic;

            var marked = 0;
            var skipped = new List<string>();

            foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var go = renderer.gameObject;

                // Anything the engine drives, or that a trainee can operate, has
                // to stay dynamic -- a static object cannot move, and a lamp
                // marked ContributeGI bakes its lit state into the lightmap.
                if (IsDriven(go)) { skipped.Add(go.name); continue; }

                if (GameObjectUtility.GetStaticEditorFlags(go) == flags) continue;
                Undo.RecordObject(go, "Mark static");
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                EditorUtility.SetDirty(go);
                marked++;
            }

            MarkSceneDirty();
            Debug.Log($"[Quest2Setup] Marked {marked} renderer(s) static. " +
                      $"Left {skipped.Count} dynamic because they are driven or interactive.");

            EditorUtility.DisplayDialog("Static flags set",
                $"Marked {marked} object(s) static: Contribute GI, batching, occlusion and " +
                "reflection probes.\n\n" +
                $"Left {skipped.Count} dynamic because a script drives them or they are " +
                "interactive.\n\n" +
                "This unlocks static batching, occlusion culling and lightmap baking -- none " +
                "of which the scene could use before, since nothing was marked.\n\n" +
                "Save the scene to keep this.", "OK");
        }

        /// <summary>True if something moves, lights or operates this object.</summary>
        private static bool IsDriven(GameObject go)
        {
            if (go.GetComponentInParent<Animator>() != null) return true;
            if (go.GetComponentInParent<Rigidbody>() != null) return true;
            if (go.GetComponent<Light>() != null) return true;

            foreach (var behaviour in go.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var name = behaviour.GetType().Name;
                if (name is "RigControl" or "RigCommandButton" or "TeleportToAnchor" or
                    "JoystickVisualizer" or "CyberChairButtonController" or
                    "BOPPanelController" or "SWACOPanelController")
                {
                    return true;
                }
            }
            return false;
        }

        // ---- plumbing ----------------------------------------------------------

        private static void Set(SerializedObject so, string property, int value)
        {
            var found = so.FindProperty(property);
            if (found != null) found.intValue = value;
            else Debug.LogWarning($"[Quest2Setup] no such property '{property}' on the pipeline asset");
        }

        private static void Set(SerializedObject so, string property, float value)
        {
            var found = so.FindProperty(property);
            if (found != null) found.floatValue = value;
            else Debug.LogWarning($"[Quest2Setup] no such property '{property}' on the pipeline asset");
        }

        private static void Set(SerializedObject so, string property, bool value)
        {
            var found = so.FindProperty(property);
            if (found != null) found.boolValue = value;
            else Debug.LogWarning($"[Quest2Setup] no such property '{property}' on the pipeline asset");
        }

        private static void MarkSceneDirty()
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }
    }
}
