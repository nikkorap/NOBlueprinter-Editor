using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public class GameAssetLock : AssetModificationProcessor
    {
        private const string SessionKey = "Blueprinter.GameAssetLock.Locked";

        public static bool IsLocked
        {
            get => SessionState.GetBool(SessionKey, true);
            set => SessionState.SetBool(SessionKey, value);
        }

        private static bool IsProtectedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');
            return string.Equals(path, GameAssetRootFolder + ".meta", StringComparison.OrdinalIgnoreCase) || BlueprinterAssets.IsPathUnder(path, GameAssetRootFolder);
        }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (!IsLocked || paths == null || paths.Length == 0)
                return paths;

            var allowed = new List<string>(paths.Length);
            foreach (var path in paths)
            {
                if (!IsProtectedPath(path))
                    allowed.Add(path);
            }

            return allowed.ToArray();
        }

        private static bool IsOpenForEdit(string[] assetOrMetaFilePaths, List<string> outNotEditablePaths, StatusQueryOptions statusQueryOptions)
        {
            if (!IsLocked || assetOrMetaFilePaths == null)
                return true;

            var allEditable = true;
            foreach (var path in assetOrMetaFilePaths)
            {
                if (!IsProtectedPath(path))
                    continue;

                outNotEditablePaths.Add(path);
                allEditable = false;
            }

            return allEditable;
        }

        private static AssetMoveResult OnWillMoveAsset(string sourcePath, string destinationPath)
        {
            if (!IsLocked || (!IsProtectedPath(sourcePath) && !IsProtectedPath(destinationPath)))
                return AssetMoveResult.DidNotMove;

            Debug.LogWarning("[Blueprinter] Unlock _donotship before moving imported assets");
            return AssetMoveResult.FailedMove;
        }

        private static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            if (!IsLocked || !IsProtectedPath(assetPath))
                return AssetDeleteResult.DidNotDelete;

            Debug.LogWarning("[Blueprinter] Unlock _donotship before deleting imported assets");
            return AssetDeleteResult.FailedDelete;
        }
    }
}
