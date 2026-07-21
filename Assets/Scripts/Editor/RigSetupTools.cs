using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRSIM.Binding;
using VRSIM.Net;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// One-click scene surgery, so that wiring the connection layer into a
    /// 5,700-object scene is not done by hand.
    ///
    /// These run through Unity's own API rather than editing the scene file,
    /// which matters because the Editor holds the scene in memory and would
    /// overwrite a direct file edit on its next save.
    /// </summary>
    public static class RigSetupTools
    {
        [MenuItem("VRSIM/Setup/Add RigConnection To Scene", priority = 20)]
        public static void AddRigConnection()
        {
            var existing = Object.FindObjectOfType<RigConnection>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog("Already present",
                    $"RigConnection is already on '{existing.gameObject.name}'.", "OK");
                return;
            }

            var host = GameObject.Find("Game Manager") ?? new GameObject("Game Manager");

            // The deleted SimulationDataReceiver leaves a missing-script entry.
            // Clearing it here saves a manual pass and stops it masking real
            // errors in the Console.
            var removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(host);

            Undo.RecordObject(host, "Add RigConnection");
            var connection = Undo.AddComponent<RigConnection>(host);
            connection.autoDiscover = true;
            connection.connectOnStart = true;

            EditorUtility.SetDirty(host);
            EditorSceneManagerMarkDirty();
            Selection.activeGameObject = host;

            EditorUtility.DisplayDialog("RigConnection added",
                $"Added RigConnection to '{host.name}'.\n\n" +
                (removed > 0 ? $"Also removed {removed} missing-script component(s).\n\n" : "") +
                "Presenters and bindings find it automatically, so nothing else " +
                "needs wiring. Save the scene to keep this.",
                "OK");
        }

        /// <summary>
        /// Reads the BOP panel's hand-wired lights and produces equivalent
        /// <see cref="RigIndicator"/> components bound to engine state paths.
        ///
        /// The point is to move from "a controller class with a named field per
        /// lamp" to "a component per lamp naming its own state path", without
        /// re-doing the twelve assignments by hand. After this, adding a lamp is
        /// Inspector work rather than a code change.
        ///
        /// Non-destructive: the old controller is left in place and disabled, so
        /// the result can be compared before anything is deleted.
        /// </summary>
        [MenuItem("VRSIM/Setup/Generate Rig Indicators From BOP Panel", priority = 21)]
        public static void GenerateIndicators()
        {
            var panel = Object.FindObjectOfType<BOPPanelController>();
            if (panel == null)
            {
                EditorUtility.DisplayDialog("Not found",
                    "No BOPPanelController in the open scene.", "OK");
                return;
            }

            // (label, green, red, state path)
            var map = new List<(string, GameObject, GameObject, string)>
            {
                ("Annular",    panel.annularGreenLight,   panel.annularRedLight,   "bop.components.annular_1"),
                ("Pipe Ram",   panel.pipeRamGreenLight,   panel.pipeRamRedLight,   "bop.components.pipe_ram_1"),
                ("Blind Ram",  panel.blindRamGreenLight,  panel.blindRamRedLight,  "bop.components.blind_shear_ram"),
                ("Shear Ram",  panel.shearRamGreenLight,  panel.shearRamRedLight,  "bop.components.blind_shear_ram"),
                ("Kill Line",  panel.killLineGreenLight,  panel.killLineRedLight,  "bop.components.kill_line"),
                ("Choke Line", panel.chokeLineGreenLight, panel.chokeLineRedLight, "bop.components.choke_line"),
                ("Master Valve", panel.masterValveGreenLight, panel.masterValveRedLight, "bop.master_valve"),
            };

            var root = GameObject.Find("Rig Indicators") ?? new GameObject("Rig Indicators");
            Undo.RegisterCreatedObjectUndo(root, "Generate Rig Indicators");

            int made = 0, skipped = 0;
            foreach (var (label, green, red, path) in map)
            {
                if (green == null && red == null) { skipped++; continue; }

                var go = new GameObject($"Indicator {label}");
                Undo.RegisterCreatedObjectUndo(go, "Generate Rig Indicators");
                go.transform.SetParent(root.transform, false);

                var indicator = go.AddComponent<RigIndicator>();
                indicator.statePath = path;
                indicator.greenLight = green;
                indicator.redLight = red;
                made++;
            }

            // Disabled rather than deleted: the generated indicators should be
            // seen working before the thing they replace is thrown away.
            panel.enabled = false;
            EditorUtility.SetDirty(panel);
            EditorSceneManagerMarkDirty();
            Selection.activeGameObject = root;

            EditorUtility.DisplayDialog("Indicators generated",
                $"Created {made} RigIndicator component(s) under 'Rig Indicators'." +
                (skipped > 0 ? $"\n{skipped} skipped (no lights assigned)." : "") +
                "\n\nBOPPanelController has been DISABLED, not deleted, so you can " +
                "compare behaviour first. Note the pipe ram indicator now tracks " +
                "pipe_ram_1 only — the old code combined all three, which needs a " +
                "rule the generic component does not have.\n\nSave the scene to keep this.",
                "OK");
        }

        private static void EditorSceneManagerMarkDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
