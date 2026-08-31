#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PrefabMissingReferenceChecker
{
    [MenuItem("Blueprinter/Tools/Validate references")]
    private static void ValidateCurrentPrefabStage()
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null || !stage.prefabContentsRoot)
        {
            EditorUtility.DisplayDialog("Validation", "Open a prefab in Prefab Mode first.", "OK");
            return;
        }

        ValidatePrefab(stage.prefabContentsRoot);
    }

    public static int ValidatePrefabAsset(GameObject prefabAsset)
    {
        if (!prefabAsset) return 0;

        var path = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogError("[Prefab Validation] Argument is not a prefab asset.", prefabAsset);
            return 0;
        }

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            return ValidatePrefab(root);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static int ValidatePrefab(GameObject prefabRoot)
    {
        if (!prefabRoot) return 0;

        int missingRefs = 0;
        foreach (var t in prefabRoot.GetComponentsInChildren<Transform>(true))
        {
            var rel = AnimationUtility.CalculateTransformPath(t, prefabRoot.transform);
            var ownerPath = string.IsNullOrEmpty(rel) ? prefabRoot.name : $"{prefabRoot.name}/{rel}";

            var comps = t.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                var c = comps[i];

                var it = new SerializedObject(c).GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    if (it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                    {
                        missingRefs++;
                        Debug.LogError($"[MissingRef] {ownerPath}.{c.GetType().Name}.{it.propertyPath.Replace(".Array.data[", "[")}", c);
                    }
                }
            }
        }

        if (missingRefs == 0)
            Debug.Log($"[Prefab Validation] OK: {prefabRoot.name}", prefabRoot);
        else
            Debug.LogWarning(
                $"[Prefab Validation] {prefabRoot.name}: missing refs={missingRefs}",
                prefabRoot
            );

        return missingRefs;
    }
}
#endif
