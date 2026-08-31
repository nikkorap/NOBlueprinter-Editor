using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    public static class PrefabHashWriter
    {
        private const string PrefabHashPropertyName = "_prefabHash";

        public static bool Write(string[] assetPaths)
        {
            foreach (var path in assetPaths)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                var prefabGuid = AssetDatabase.AssetPathToGUID(path);
                var changed = false;

                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || component.GetType().FullName != "Mirage.NetworkIdentity")
                        continue;

                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string componentGuid, out long _))
                    {
                        Debug.LogError($"[Blueprinter] Unidentified NetworkIdentity '{path}'");
                        return false;
                    }

                    // Nested prefab contents are written with their own prefab asset
                    if (!string.Equals(componentGuid, prefabGuid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var serialized = new SerializedObject(component);
                    var property = serialized.FindProperty(PrefabHashPropertyName);
                    if (property == null || property.propertyType != SerializedPropertyType.Integer)
                    {
                        Debug.LogError($"[Blueprinter] NetworkIdentity missing int '{PrefabHashPropertyName}' in '{path}'");
                        return false;
                    }

                    var hash = StableHash32(path);
                    if (hash == 0)
                        hash = 1;

                    if (property.intValue == hash)
                        continue;

                    property.intValue = hash;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed = true;
                }

                if (!changed)
                    continue;

                PrefabUtility.SavePrefabAsset(prefab, out var saved);
                if (!saved)
                {
                    Debug.LogError($"[Blueprinter] Failed to save prefab hash '{path}'");
                    return false;
                }
            }

            return true;
        }

        private static int StableHash32(string value)
        {
            unchecked
            {
                const uint offsetBasis = 2166136261u;
                const uint prime = 16777619u;

                var hash = offsetBasis;
                foreach (var b in Encoding.UTF8.GetBytes(value))
                {
                    hash ^= b;
                    hash *= prime;
                }

                return (int)hash;
            }
        }
    }
}
