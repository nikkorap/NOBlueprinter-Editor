using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public static class BlueprinterAssets
    {
        private static readonly string ProjectRoot = Path.GetFullPath(Path.GetDirectoryName(Application.dataPath));

        public static void EnsureFolder(string folderPath)
        {
            var parts = folderPath.Split('/');
            var current = "Assets";
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        public static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var characters = value.Trim().ToCharArray();
            for (var i = 0; i < characters.Length; i++)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), characters[i]) >= 0 || "<>:\"/\\|?*".IndexOf(characters[i]) >= 0)
                    characters[i] = '_';
            }

            return new string(characters).TrimEnd(' ', '.');
        }

        public static string GetRuntimeTypeName(Type type)
        {
            if (type == null)
                return null;

            while (type.Assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal) && type.BaseType != null && typeof(UnityEngine.Object).IsAssignableFrom(type.BaseType))
                type = type.BaseType;

            return type.FullName + ", " + type.Assembly.GetName().Name;
        }

        public static AssetRef CreateAssetRef(UnityEngine.Object asset)
        {
            if (asset == null)
                return null;

            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            return new AssetRef
            {
                locator = assetPath,
                name = asset.name,
                type = GetRuntimeTypeName(asset.GetType())
            };
        }

        public static string[] GetModNames()
        {
            if (!AssetDatabase.IsValidFolder(ModRootFolder))
                return Array.Empty<string>();

            var folders = AssetDatabase.GetSubFolders(ModRootFolder);
            var names = new string[folders.Length];
            for (var i = 0; i < folders.Length; i++)
                names[i] = Path.GetFileName(folders[i]);

            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }

        public static string GetModFolderPath(string modName)
        {
            return string.IsNullOrEmpty(modName) ? null : ModRootFolder + "/" + modName;
        }

        public static string[] GetModAssetPaths(string modName)
        {
            return GetAssetPathsUnderFolder(GetModFolderPath(modName));
        }

        public static string[] GetGameAssetPaths()
        {
            return Array.FindAll(GetAssetPathsUnderFolder(GameAssetRootFolder), path => !IsStaleGameAssetPath(path));
        }

        public static void ClearAssetBundleLabels(IEnumerable<string> assetPaths)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in assetPaths)
                {
                    var importer = AssetImporter.GetAtPath(path);
                    if (importer == null || (string.IsNullOrEmpty(importer.assetBundleName) && string.IsNullOrEmpty(importer.assetBundleVariant)))
                    {
                        continue;
                    }

                    importer.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        public static bool IsGameAssetPath(string assetPath)
        {
            return IsPathUnder(assetPath, GameAssetRootFolder) && !IsStaleGameAssetPath(assetPath);
        }

        public static bool IsStaleGameAssetPath(string assetPath)
        {
            return IsPathUnder(assetPath, StaleGameAssetRootFolder);
        }

        private static string[] GetAssetPathsUnderFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return Array.Empty<string>();

            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { folderPath }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path) && !IsCodeOrAssemblyFile(path) && !IsModInfoFile(path))
                {
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);
            return paths.ToArray();
        }

        public static bool IsModInfoFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            path = path.Replace('\\', '/');
            var prefix = ModRootFolder + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var relativePath = path.Substring(prefix.Length);
            var separator = relativePath.IndexOf('/');
            return separator > 0 && relativePath.IndexOf('/', separator + 1) < 0 && string.Equals(relativePath.Substring(separator + 1), ModInfoFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCodeOrAssemblyFile(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".cs":
                case ".dll":
                case ".pdb":
                case ".mdb":
                case ".asmdef":
                case ".asmref":
                case ".rsp":
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsPathUnder(string assetPath, string folderPath)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folderPath))
                return false;

            assetPath = assetPath.Replace('\\', '/');
            folderPath = folderPath.Replace('\\', '/').TrimEnd('/');
            return string.Equals(assetPath, folderPath, StringComparison.OrdinalIgnoreCase) || assetPath.StartsWith(folderPath + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsPathInsideAssets(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(fullPath, assetsPath, comparison) || fullPath.StartsWith(assetsPath + Path.DirectorySeparatorChar, comparison) || fullPath.StartsWith(assetsPath + Path.AltDirectorySeparatorChar, comparison);
        }

        public static string ToAbsolutePath(string projectPath)
        {
            return Path.GetFullPath(Path.Combine(ProjectRoot, projectPath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
