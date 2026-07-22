using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Restores the BOP panel's hand-wired lights into the per-state assignment
    /// list, after the controller moved from twelve named fields to a list of
    /// components each carrying its own lights.
    ///
    /// The old assignments were captured to BopLightWiring.json while they were
    /// still in the scene file. That capture matters: once the named fields
    /// stopped existing on the class, Unity would drop those values from the
    /// scene the next time it saved, and twelve lights would have to be dragged
    /// back in by hand.
    ///
    /// Objects are resolved by their scene-local file id through GlobalObjectId,
    /// which is the same identity the scene file stores, so this reconnects the
    /// exact objects that were wired -- not ones matched by name.
    /// </summary>
    public static class BopLightMigration
    {
        private const string WiringPath = "Assets/Scripts/Editor/BopLightWiring.json";

        // JsonUtility fills these by reflection, which the compiler cannot see,
        // so it reports all twenty as never assigned and buries real warnings.
#pragma warning disable 0649
        [Serializable]
        private class Wiring
        {
            public string sceneGuid;
            public string scenePath;
            public Fields fields;
        }

        [Serializable]
        private class Fields
        {
            public string annularGreenLight, annularRedLight;
            public string pipeRamGreenLight, pipeRamRedLight;
            public string blindRamGreenLight, blindRamRedLight;
            public string shearRamGreenLight, shearRamRedLight;
            public string killLineGreenLight, killLineRedLight;
            public string chokeLineGreenLight, chokeLineRedLight;
            public string masterValveGreenLight, masterValveRedLight, masterValveObject;
            public string annularPressure3D, manifoldPressure3D, accumulatorPressure3D, airPressure3D;
        }
#pragma warning restore 0649

        private static readonly Color GreenOn = new Color(0.25f, 0.95f, 0.35f);
        private static readonly Color RedOn = new Color(0.95f, 0.25f, 0.20f);
        private static readonly Color Amber = new Color(1f, 0.78f, 0.20f);

        [MenuItem("VRSIM/Setup/Restore BOP Light Wiring", priority = 22)]
        public static void Restore()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<BOPPanelController>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Not found",
                    "No BOPPanelController in the open scene.", "OK");
                return;
            }
            if (!File.Exists(WiringPath))
            {
                EditorUtility.DisplayDialog("No capture file",
                    $"{WiringPath} is missing. It holds the original light assignments.", "OK");
                return;
            }

            var wiring = JsonUtility.FromJson<Wiring>(File.ReadAllText(WiringPath));
            var f = wiring.fields;

            // Each part, with a light per state. "moving" reuses the green lamp
            // in amber, because the panel has two lamps per part and a ram in
            // transit needs to look different from both open and closed.
            var plan = new List<(string id, string label, string green, string red, string[] combine)>
            {
                ("annular_1",       "Annular",    f.annularGreenLight,   f.annularRedLight,   null),
                ("pipe_ram_1",      "Pipe Rams",  f.pipeRamGreenLight,   f.pipeRamRedLight,
                    new[] { "pipe_ram_2", "pipe_ram_3" }),
                ("blind_shear_ram", "Blind Ram",  f.blindRamGreenLight,  f.blindRamRedLight,  null),
                ("blind_shear_ram", "Shear Ram",  f.shearRamGreenLight,  f.shearRamRedLight,  null),
                ("kill_line",       "Kill Line",  f.killLineGreenLight,  f.killLineRedLight,  null),
                ("choke_line",      "Choke Line", f.chokeLineGreenLight, f.chokeLineRedLight, null),
            };

            Undo.RecordObject(panel, "Restore BOP light wiring");
            panel.components = new List<BOPPanelController.ComponentLights>();

            int wired = 0, missing = 0;
            foreach (var (id, label, greenId, redId, combine) in plan)
            {
                var green = Resolve(wiring.sceneGuid, greenId);
                var red = Resolve(wiring.sceneGuid, redId);
                if (green == null && red == null) { missing++; continue; }

                var entry = new BOPPanelController.ComponentLights
                {
                    componentId = id,
                    label = label,
                    lights = new List<BOPPanelController.StateLight>(),
                    alsoCombine = new List<string>(combine ?? Array.Empty<string>()),
                };
                if (green != null)
                {
                    entry.lights.Add(new BOPPanelController.StateLight
                        { state = "open", light = green, colour = GreenOn });
                    entry.lights.Add(new BOPPanelController.StateLight
                        { state = "moving", light = green, colour = Amber });
                    wired++;
                }
                if (red != null)
                {
                    entry.lights.Add(new BOPPanelController.StateLight
                        { state = "closed", light = red, colour = RedOn });
                    entry.lights.Add(new BOPPanelController.StateLight
                        { state = "fault", light = red, colour = RedOn });
                    wired++;
                }
                panel.components.Add(entry);
            }

            panel.masterValve = new BOPPanelController.ComponentLights
            {
                componentId = "master_valve",
                label = "Master Valve",
                lights = new List<BOPPanelController.StateLight>(),
                alsoCombine = new List<string>(),
            };
            var mvGreen = Resolve(wiring.sceneGuid, f.masterValveGreenLight);
            var mvRed = Resolve(wiring.sceneGuid, f.masterValveRedLight);
            if (mvGreen != null)
            {
                panel.masterValve.lights.Add(new BOPPanelController.StateLight
                    { state = "open", light = mvGreen, colour = GreenOn });
                wired++;
            }
            if (mvRed != null)
            {
                panel.masterValve.lights.Add(new BOPPanelController.StateLight
                    { state = "closed", light = mvRed, colour = RedOn });
                wired++;
            }

            var valve = Resolve(wiring.sceneGuid, f.masterValveObject);
            if (valve != null) panel.masterValveObject = valve;

            panel.annularPressure3D = ResolveText(wiring.sceneGuid, f.annularPressure3D);
            panel.manifoldPressure3D = ResolveText(wiring.sceneGuid, f.manifoldPressure3D);
            panel.accumulatorPressure3D = ResolveText(wiring.sceneGuid, f.accumulatorPressure3D);
            panel.airPressure3D = ResolveText(wiring.sceneGuid, f.airPressure3D);

            EditorUtility.SetDirty(panel);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = panel.gameObject;

            EditorUtility.DisplayDialog("BOP wiring restored",
                $"Restored {wired} light assignment(s) across {panel.components.Count} components." +
                (missing > 0 ? $"\n{missing} skipped (nothing was assigned)." : "") +
                "\n\nMaster valve lamps were never assigned in the scene, so those " +
                "entries are empty and ready for you to fill in.\n\n" +
                "Check the Inspector, then SAVE THE SCENE.",
                "OK");
        }

        /// <summary>
        /// Resolves a scene-local file id to its GameObject. GlobalObjectId uses
        /// the same identity the scene file records, so this reconnects the
        /// exact object rather than guessing by name.
        /// </summary>
        private static GameObject Resolve(string sceneGuid, string fileId)
        {
            if (string.IsNullOrEmpty(fileId)) return null;
            var text = $"GlobalObjectId_V1-2-{sceneGuid}-{fileId}-0";
            if (!GlobalObjectId.TryParse(text, out var id)) return null;
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            return obj as GameObject ?? (obj as Component)?.gameObject;
        }

        private static TextMeshPro ResolveText(string sceneGuid, string fileId)
        {
            if (string.IsNullOrEmpty(fileId)) return null;
            var text = $"GlobalObjectId_V1-2-{sceneGuid}-{fileId}-0";
            if (!GlobalObjectId.TryParse(text, out var id)) return null;
            var obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            return obj as TextMeshPro ?? (obj as GameObject)?.GetComponent<TextMeshPro>();
        }
    }
}
