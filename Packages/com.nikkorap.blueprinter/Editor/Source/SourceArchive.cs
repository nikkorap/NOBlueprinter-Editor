using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace Blueprinter
{
    [Serializable]
    public class SourceManifest
    {
        public string modName;
        public string displayName;
        public string version;
        public List<SourceReference> references = new List<SourceReference>();
    }

    // Runtime scripts use assembly and type
    // Game assets use relative paths
    // Project assets use Assets or Packages paths
    [Serializable]
    public class SourceReference
    {
        public string guid;
        public long fileId;
        public string path;
        public string assembly;
        public string type;
    }

    public static class SourceArchive
    {
        public const string ManifestFileName = "source_manifest.json";
        public const string SourceZipSuffix = ".source.zip";
        private const long PrefabAssetFileId = 100100000;

        public static string ReferenceKey(string guid, long fileId)
        {
            return guid + ":" + fileId;
        }

        public static string ScriptKey(string assemblyName, string fullTypeName)
        {
            return assemblyName + ":" + fullTypeName;
        }

        public static bool IsExternalAssetPath(string path)
        {
            return !string.IsNullOrEmpty(path) && (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsPrefabAssetReference(string assetPath, long fileId)
        {
            return fileId == PrefabAssetFileId && string.Equals(Path.GetExtension(assetPath), ".prefab", StringComparison.OrdinalIgnoreCase);
        }

        public static UnityEngine.Object ResolveAsset(string assetPath, long fileId)
        {
            if (IsPrefabAssetReference(assetPath, fileId))
                return AssetDatabase.LoadMainAssetAtPath(assetPath);

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long candidateFileId) && candidateFileId == fileId)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
