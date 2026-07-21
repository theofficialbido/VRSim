using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        private static void EditorSceneManagerMarkDirty()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
