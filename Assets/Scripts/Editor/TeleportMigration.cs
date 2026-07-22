using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRSIM.Locomotion;

namespace VRSIM.EditorTools
{
    /// <summary>
    /// Replaces the twelve one-per-destination teleport classes with a single
    /// <see cref="TeleportToAnchor"/> per button, carrying the anchor across and
    /// repointing each button's OnClick.
    ///
    /// The new component is put on the button rather than on Game Manager, so
    /// selecting a button shows where it goes. The old arrangement kept all
    /// twelve on one object, which meant the destination and the thing that
    /// triggers it were nowhere near each other.
    ///
    /// Non-destructive: the old components are left in place and their OnClick
    /// entries repointed, so the result can be tested before anything is
    /// deleted.
    /// </summary>
    public static class TeleportMigration
    {
        [MenuItem("VRSIM/Setup/Migrate Teleport Scripts", priority = 23)]
        public static void Migrate()
        {
            // The old classes are the ones named Teleport* that carry an anchor
            // field and are not the replacement itself.
            var legacy = Object.FindObjectsOfType<MonoBehaviour>()
                .Where(m => m != null
                            && m.GetType().Name.StartsWith("Teleport")
                            && !(m is TeleportToAnchor)
                            && new SerializedObject(m).FindProperty("anchor") != null)
                .ToList();

            if (legacy.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing to migrate",
                    "No legacy Teleport* components found in the open scene.", "OK");
                return;
            }

            var buttons = Object.FindObjectsOfType<Button>(true);
            int rewired = 0, created = 0, orphaned = 0;
            var report = new List<string>();

            foreach (var old in legacy)
            {
                var so = new SerializedObject(old);
                var anchorProp = so.FindProperty("anchor");
                var providerProp = so.FindProperty("provider");
                var anchor = anchorProp?.objectReferenceValue
                    as UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationAnchor;
                var provider = providerProp?.objectReferenceValue
                    as UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider;

                var callers = buttons.Where(b => TargetsComponent(b, old)).ToList();
                if (callers.Count == 0)
                {
                    // Nothing invokes it. Worth reporting rather than silently
                    // migrating a destination no button can reach.
                    orphaned++;
                    report.Add($"  {old.GetType().Name}: no button calls it (anchor: {NameOf(anchor)})");
                    continue;
                }

                foreach (var button in callers)
                {
                    var replacement = Undo.AddComponent<TeleportToAnchor>(button.gameObject);
                    replacement.anchor = anchor;
                    replacement.provider = provider;
                    created++;

                    if (Repoint(button, old, replacement)) rewired++;
                    report.Add($"  {button.name} -> {NameOf(anchor)}");
                }
            }

            EditorUtility.SetDirty(buttons.Length > 0 ? buttons[0] : legacy[0]);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                legacy[0].gameObject.scene);

            Debug.Log($"[TeleportMigration] {created} component(s) created, {rewired} " +
                      $"button(s) repointed:\n" + string.Join("\n", report));

            EditorUtility.DisplayDialog("Teleport migration done",
                $"Created {created} TeleportToAnchor component(s) on the buttons and " +
                $"repointed {rewired} OnClick entr(ies).\n" +
                (orphaned > 0 ? $"\n{orphaned} legacy component(s) had no button calling them " +
                                "— see the Console.\n" : "") +
                "\nThe old components are still there and untouched. Test the buttons, " +
                "then delete them from Game Manager.\n\nSave the scene to keep this.",
                "OK");
        }

        /// <summary>True if any persistent OnClick entry points at this component.</summary>
        private static bool TargetsComponent(Button button, Object target)
        {
            var calls = new SerializedObject(button)
                .FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null) return false;
            for (var i = 0; i < calls.arraySize; i++)
            {
                var element = calls.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("m_Target")?.objectReferenceValue == target)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Repoints the button's OnClick from the old component to the new one.
        ///
        /// Both expose a no-argument Teleport(), so only the target object and
        /// its type name change — the method name and call mode stay as they
        /// are, which keeps the edit minimal and reversible.
        /// </summary>
        private static bool Repoint(Button button, Object old, TeleportToAnchor replacement)
        {
            var so = new SerializedObject(button);
            var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls == null) return false;

            var changed = false;
            for (var i = 0; i < calls.arraySize; i++)
            {
                var element = calls.GetArrayElementAtIndex(i);
                var targetProp = element.FindPropertyRelative("m_Target");
                if (targetProp?.objectReferenceValue != old) continue;

                targetProp.objectReferenceValue = replacement;
                var typeName = element.FindPropertyRelative("m_TargetAssemblyTypeName");
                if (typeName != null)
                    typeName.stringValue = typeof(TeleportToAnchor).FullName + ", Assembly-CSharp";

                var method = element.FindPropertyRelative("m_MethodName");
                if (method != null) method.stringValue = "Teleport";
                changed = true;
            }

            if (changed) so.ApplyModifiedProperties();
            return changed;
        }

        private static string NameOf(Object o) => o == null ? "<no anchor>" : o.name;
    }
}
