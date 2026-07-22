using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Swaps the old rig models in the open scene for the revamped ones in
    /// Assets/_Prefabs/Environments/3d/models, matching by general name.
    ///
    /// It works through the Editor API rather than by editing the scene file,
    /// because the Editor holds the scene in memory and would overwrite a direct
    /// file edit on its next save.
    ///
    /// <b>What it can and cannot do.</b> Placement it can do exactly: the new
    /// model lands on the old one's transform, parent, layer, tag and static
    /// flags, and any components sitting on the old root are carried across with
    /// their serialized values intact.
    ///
    /// Wiring it cannot do. Every controller in this project points at specific
    /// child objects -- the BOP lamps, the SWACO needles and readouts, the chair
    /// buttons, the joystick stick. The new models name their children
    /// <c>Cube.023</c>, <c>Plane.003</c> and <c>Text.001</c>, so there is nothing
    /// to match those references against. Guessing would produce a panel that
    /// lights a lamp belonging to the wrong ram, which on a well-control trainer
    /// is worse than a lamp that stays dark.
    ///
    /// So the swap deliberately stops at the point where judgement is needed and
    /// reports every reference it has left dangling, object by object and field
    /// by field. That report is the re-wiring worklist.
    ///
    /// Nothing is deleted. The old objects are deactivated and kept, so the two
    /// can be compared side by side and the swap undone with a single Ctrl+Z.
    /// </summary>
    public static class ModelSwap
    {
        private const string NewModelFolder = "Assets/_Prefabs/Environments/3d/models";

        /// <summary>
        /// Old model, and the revamped model that replaces it. Edit this table
        /// rather than the code below; the tool reports anything it cannot map
        /// instead of guessing.
        /// </summary>
        private static readonly (string OldModel, string NewModel, string Note)[] Mapping =
        {
            ("BOP Panel.glb",      "BOPFinished.glb",          "the finished BOP panel"),
            ("SWACO.glb",          "remote_choke_panel.glb",   "SWACO is the remote choke panel"),
            ("Tower.glb",          "mast.glb",                 "derrick/mast"),
            ("ChokeManifold.glb",  "sp_manifold.glb",          "standpipe/choke manifold"),
            ("Ctrl Room.glb",      "dog_house.glb",            "the doghouse is the control room"),
            ("Panel.glb",          "bop_panel.glb",            "secondary panel"),
            // Rig(Met2ata3).glb is the 150 MB monolith: it holds the substructure,
            // drill floor, derrick and more in one mesh set, so it maps to several
            // new files rather than one. Left out deliberately -- splitting it is a
            // placement job, not a rename, and doing it blind would scatter
            // geometry. Do it by hand once the rest is verified.
            // MudTank.glb has no counterpart in the new set at all.
        };

        [MenuItem("VRSIM/Setup/Model Swap/1. Preview (changes nothing)", priority = 30)]
        public static void Preview() => Run(apply: false);

        [MenuItem("VRSIM/Setup/Model Swap/2. Apply", priority = 31)]
        public static void Apply()
        {
            if (!EditorUtility.DisplayDialog("Swap models?",
                    "The old models will be deactivated (not deleted) and the new ones " +
                    "placed on their transforms.\n\n" +
                    "References into the old hierarchies -- BOP lamps, SWACO needles, " +
                    "chair buttons -- cannot be carried across and will need re-wiring. " +
                    "The Console will list every one of them.\n\n" +
                    "Run Preview first if you have not.",
                    "Swap", "Cancel"))
            {
                return;
            }
            Run(apply: true);
        }

        private static void Run(bool apply)
        {
            var targets = FindTargets();
            if (targets.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to swap",
                    "No objects in the open scene use any of the mapped old models.\n\n" +
                    "Either the swap has already run, or the scene uses models not listed " +
                    "in the Mapping table in ModelSwap.cs.", "OK");
                return;
            }

            var report = new List<string>();
            var dangling = new List<string>();
            var swapped = 0;

            foreach (var target in targets)
            {
                var newAssetPath = $"{NewModelFolder}/{target.NewModel}";
                var newAsset = AssetDatabase.LoadAssetAtPath<GameObject>(newAssetPath);
                if (newAsset == null)
                {
                    report.Add($"  SKIPPED {target.Root.name}: {newAssetPath} is not imported. " +
                               "Check it has a .meta and that Unity has reimported it.");
                    continue;
                }

                report.Add($"  {target.Root.name}  ({target.OldModel} -> {target.NewModel})");
                foreach (var line in DescribeReferences(target.Root))
                    dangling.Add($"  {target.Root.name}: {line}");

                if (!apply) { swapped++; continue; }

                var old = target.Root;
                var replacement = (GameObject)PrefabUtility.InstantiatePrefab(newAsset, old.transform.parent);
                Undo.RegisterCreatedObjectUndo(replacement, "Swap model");

                // Land exactly where the old one sat. Local, not world: the old
                // object may hang off a parent that is itself offset.
                replacement.transform.SetLocalPositionAndRotation(
                    old.transform.localPosition, old.transform.localRotation);
                replacement.transform.localScale = old.transform.localScale;
                replacement.transform.SetSiblingIndex(old.transform.GetSiblingIndex());

                replacement.layer = old.layer;
                replacement.tag = old.tag;
                GameObjectUtility.SetStaticEditorFlags(
                    replacement, GameObjectUtility.GetStaticEditorFlags(old));

                CarryOverComponents(old, replacement);

                // Renamed rather than deleted: the name is freed for the
                // replacement so anything matching by name still resolves, and
                // the original stays in the scene to compare against.
                Undo.RecordObject(old, "Swap model");
                var keptName = old.name;
                old.name = keptName + " (OLD - verify then delete)";
                replacement.name = keptName;
                Undo.RecordObject(old, "Swap model");
                old.SetActive(false);
                EditorUtility.SetDirty(old);

                swapped++;
            }

            var verb = apply ? "Swapped" : "Would swap";
            var summary = $"[ModelSwap] {verb} {swapped} model(s):\n" + string.Join("\n", report);
            if (dangling.Count > 0)
            {
                summary += "\n\nReferences that need re-wiring by hand after the swap:\n" +
                           string.Join("\n", dangling);
            }
            Debug.Log(summary);

            if (apply)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            }

            EditorUtility.DisplayDialog(
                apply ? "Model swap done" : "Model swap preview",
                $"{verb} {swapped} model(s).\n\n" +
                (dangling.Count > 0
                    ? $"{dangling.Count} component reference(s) point into the old hierarchies " +
                      "and cannot be carried across automatically. Every one is listed in the " +
                      "Console -- work through that list in the Inspector.\n\n"
                    : "No component references pointed into the old hierarchies.\n\n") +
                (apply
                    ? "The old objects are deactivated, not deleted. Verify the new ones, then " +
                      "delete the objects marked (OLD).\n\nCtrl+Z undoes the whole swap.\n\n" +
                      "Save the scene to keep this."
                    : "Nothing was changed. Run 'Apply' when the mapping looks right."),
                "OK");
        }

        private struct SwapTarget
        {
            public GameObject Root;
            public string OldModel;
            public string NewModel;
        }

        /// <summary>
        /// Finds the scene objects that came from a mapped old model.
        ///
        /// Matching is by the source asset of the meshes underneath, not by the
        /// object's name or a prefab link -- the monolith was unpacked into the
        /// scene, so several of these roots have no link back to the file they
        /// came from and only their meshes give them away.
        /// </summary>
        private static List<SwapTarget> FindTargets()
        {
            var found = new List<SwapTarget>();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in AllDescendants(root))
                {
                    var source = DominantMeshSource(child);
                    if (source == null) continue;

                    var match = Mapping.FirstOrDefault(m => source.EndsWith("/" + m.OldModel));
                    if (match.OldModel == null) continue;

                    // Only the topmost object of a model, not every sub-mesh.
                    if (found.Any(f => child.transform.IsChildOf(f.Root.transform))) continue;

                    found.Add(new SwapTarget
                    {
                        Root = child,
                        OldModel = match.OldModel,
                        NewModel = match.NewModel,
                    });
                }
            }
            return found;
        }

        /// <summary>Asset path the meshes under this object come from, if they agree.</summary>
        private static string DominantMeshSource(GameObject go)
        {
            var paths = new Dictionary<string, int>();
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (string.IsNullOrEmpty(path)) continue;
                paths.TryGetValue(path, out var n);
                paths[path] = n + 1;
            }
            if (paths.Count == 0) return null;
            return paths.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static IEnumerable<GameObject> AllDescendants(GameObject root)
        {
            yield return root;
            foreach (Transform child in root.transform)
                foreach (var descendant in AllDescendants(child.gameObject))
                    yield return descendant;
        }

        /// <summary>
        /// Copies the VRSIM components sitting on the old root onto the new one,
        /// serialized values and all. Their references to children inside the old
        /// model come across as they were and will point at the old hierarchy --
        /// which is exactly why those objects are kept rather than deleted, and
        /// why every one of them is reported.
        /// </summary>
        private static void CarryOverComponents(GameObject old, GameObject replacement)
        {
            foreach (var component in old.GetComponents<Component>())
            {
                if (component is Transform) continue;
                if (component is MeshFilter || component is Renderer || component is Collider) continue;

                ComponentUtility.CopyComponent(component);
                ComponentUtility.PasteComponentAsNew(replacement);
            }
        }

        /// <summary>
        /// Lists the object references a component holds that point inside this
        /// model, so the re-wiring worklist names the field rather than just the
        /// component.
        /// </summary>
        private static IEnumerable<string> DescribeReferences(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var type = behaviour.GetType();
                if (type.Namespace == null || !type.Namespace.StartsWith("VRSIM"))
                {
                    // Presenters in this project sit in the global namespace.
                    if (type.Name != "BOPPanelController" && type.Name != "SWACOPanelController" &&
                        type.Name != "CyberChairButtonController" && type.Name != "JoystickVisualizer")
                    {
                        continue;
                    }
                }

                var so = new SerializedObject(behaviour);
                var property = so.GetIterator();
                var fields = new List<string>();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    if (value == null) continue;

                    var target = value as GameObject ?? (value as Component)?.gameObject;
                    if (target == null) continue;
                    if (!target.transform.IsChildOf(root.transform)) continue;

                    fields.Add($"{property.displayName} -> {target.name}");
                }

                if (fields.Count > 0)
                    yield return $"{type.Name} needs re-wiring: {string.Join(", ", fields)}";
            }
        }
    }
}
