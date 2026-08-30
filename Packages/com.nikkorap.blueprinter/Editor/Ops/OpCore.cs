using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    public abstract class OpCore : ScriptableObject
    {
        public abstract string opId { get; }

        public virtual bool IsEmpty => false;

        public abstract object BuildPayload();

        public AssetRef RequireModAsset(UnityEngine.Object asset, string fieldName, string expectedTypeName)
        {
            if (asset == null)
            {
                Debug.LogError($"[Blueprinter] [{opId}] Missing '{fieldName}'");
                return null;
            }

            if (asset.GetType().Name != expectedTypeName)
            {
                Debug.LogError($"[Blueprinter] [{opId}] Expected '{expectedTypeName}' for '{fieldName}' got '{asset.GetType().Name}'");
                return null;
            }

            var path = AssetDatabase.GetAssetPath(asset);
            if (BlueprinterAssets.IsGameAssetPath(path))
            {
                Debug.LogError($"[Blueprinter] [{opId}] Game asset not allowed for '{fieldName}'");
                return null;
            }

            var assetRef = BlueprinterAssets.CreateAssetRef(asset);
            if (assetRef == null)
                Debug.LogError($"[Blueprinter] [{opId}] Failed asset reference '{fieldName}'");

            return assetRef;
        }

        public bool RequireValue(string value, string fieldName)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return true;

            Debug.LogError($"[Blueprinter] [{opId}] Empty '{fieldName}'");
            return false;
        }

        public AssetRef[] RequireModAssets(IEnumerable<UnityEngine.Object> assets)
        {
            if (assets == null)
                return Array.Empty<AssetRef>();

            var result = new List<AssetRef>();
            foreach (var asset in assets)
            {
                if (asset == null)
                    continue;

                var path = AssetDatabase.GetAssetPath(asset);
                if (BlueprinterAssets.IsGameAssetPath(path))
                {
                    Debug.LogError($"[Blueprinter] [{opId}] Game asset in mod asset list '{path}'");
                    return null;
                }

                var assetRef = BlueprinterAssets.CreateAssetRef(asset);
                if (assetRef != null)
                    result.Add(assetRef);
            }

            return result.ToArray();
        }
    }
}
