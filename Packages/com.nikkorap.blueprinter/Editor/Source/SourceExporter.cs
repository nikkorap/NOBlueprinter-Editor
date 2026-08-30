using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using ZipCompressionLevel = System.IO.Compression.CompressionLevel;

namespace Blueprinter
{
    public static class SourceExporter
    {
        public static void ExportMod(string modName, string displayName, string version, string outputPath)
        {
            var assetPaths = CollectSourceAssetPaths(modName);

            var manifest = new SourceManifest
            {
                modName = modName,
                displayName = string.IsNullOrWhiteSpace(displayName) ? modName : displayName,
                version = version ?? string.Empty
            };

            if (!CollectExternalReferences(assetPaths, manifest))
                return;

            var modFolder = BlueprinterAssets.GetModFolderPath(modName);
            if (!ValidateArchivePaths(assetPaths, modFolder))
                return;

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry(SourceArchive.ManifestFileName, ZipCompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(manifest, true));

                foreach (var assetPath in assetPaths)
                {
                    var archivePath = GetArchivePath(assetPath, modFolder);
                    AddProjectFile(zip, assetPath, archivePath);
                    AddProjectFile(zip, assetPath + ".meta", archivePath + ".meta");
                }
            }

            Debug.Log($"[Blueprinter] Exported source ZIP '{outputPath}'");
        }

        private static List<string> CollectSourceAssetPaths(string modName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dependencies = AssetDatabase.GetDependencies(BlueprinterAssets.GetModAssetPaths(modName), true);
            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrEmpty(dependency) || !BlueprinterAssets.IsPathUnder(dependency, BlueprinterSettings.RootFolder) || AssetDatabase.IsValidFolder(dependency) || BlueprinterAssets.IsCodeOrAssemblyFile(dependency) || BlueprinterAssets.IsModInfoFile(dependency) || BlueprinterAssets.IsGameAssetPath(dependency) || BlueprinterAssets.IsStaleGameAssetPath(dependency))
                    continue;

                result.Add(dependency.Replace('\\', '/'));
            }

            return result.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static bool CollectExternalReferences(List<string> assetPaths, SourceManifest manifest)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var internalGuids = new HashSet<string>(assetPaths.Select(AssetDatabase.AssetPathToGUID).Where(guid => !string.IsNullOrEmpty(guid)), StringComparer.OrdinalIgnoreCase);
            var scripts = new Dictionary<string, RuntimeScriptInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var script in RuntimeScriptInfo.GetAll())
                scripts[SourceArchive.ReferenceKey(script.Guid, script.FileId)] = script;

            foreach (var assetPath in assetPaths)
            {
                var metaPath = assetPath + ".meta";
                if (!File.Exists(BlueprinterAssets.ToAbsolutePath(metaPath)))
                {
                    Debug.LogError($"[Blueprinter] Missing source meta '{assetPath}'");
                    return false;
                }

                if (!CollectExternalReferencesFromFile(assetPath, internalGuids, scripts, seen, manifest) || !CollectExternalReferencesFromFile(metaPath, internalGuids, scripts, seen, manifest))
                {
                    return false;
                }
            }

            manifest.references = manifest.references
                .OrderBy(reference => reference.assembly, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reference => reference.type, StringComparer.Ordinal)
                .ThenBy(reference => reference.fileId)
                .ToList();
            return true;
        }

        private static bool CollectExternalReferencesFromFile(string projectPath, HashSet<string> internalGuids, Dictionary<string, RuntimeScriptInfo> scripts, HashSet<string> seen, SourceManifest manifest)
        {
            var absolutePath = BlueprinterAssets.ToAbsolutePath(projectPath);
            if (!projectPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !UnitySerializedText.IsSerializedText(absolutePath))
            {
                return true;
            }

            var text = File.ReadAllText(absolutePath, Encoding.UTF8);
            foreach (Match match in UnitySerializedText.ObjectReferenceRegex.Matches(text))
            {
                if (!CollectReference(projectPath, match, internalGuids, scripts, seen, manifest))
                    return false;
            }

            return true;
        }

        private static bool CollectReference(string sourceFile, Match match, HashSet<string> internalGuids, Dictionary<string, RuntimeScriptInfo> scripts, HashSet<string> seen, SourceManifest manifest)
        {
            if (!long.TryParse(match.Groups[1].Value, out var fileId))
                return true;

            var guid = match.Groups[2].Value;
            if (internalGuids.Contains(guid))
                return true;

            var key = SourceArchive.ReferenceKey(guid, fileId);
            if (!seen.Add(key))
                return true;

            if (scripts.TryGetValue(key, out var script))
            {
                manifest.references.Add(new SourceReference
                {
                    guid = guid,
                    fileId = fileId,
                    assembly = script.AssemblyName,
                    type = script.FullTypeName
                });
                return true;
            }

            var referencedPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(referencedPath) && BlueprinterAssets.IsCodeOrAssemblyFile(referencedPath))
                return true;

            if (BlueprinterAssets.IsStaleGameAssetPath(referencedPath))
            {
                Debug.LogError($"[Blueprinter] Replace stale game asset '{referencedPath}' in '{sourceFile}'");
                return false;
            }

            if (BlueprinterAssets.IsGameAssetPath(referencedPath))
            {
                var asset = SourceArchive.ResolveAsset(referencedPath, fileId);
                if (asset == null)
                {
                    Debug.LogError($"[Blueprinter] Unresolved _donotship reference {guid}:{fileId} in '{sourceFile}'");
                    return false;
                }

                manifest.references.Add(new SourceReference
                {
                    guid = guid,
                    fileId = fileId,
                    path = referencedPath.Substring(BlueprinterSettings.GameAssetRootFolder.Length + 1),
                    type = BlueprinterAssets.GetRuntimeTypeName(asset.GetType())
                });
                return true;
            }

            if (SourceArchive.IsExternalAssetPath(referencedPath) && !BlueprinterAssets.IsPathUnder(referencedPath, BlueprinterSettings.RootFolder))
            {
                var asset = SourceArchive.ResolveAsset(referencedPath, fileId);
                manifest.references.Add(new SourceReference
                {
                    guid = guid,
                    fileId = fileId,
                    path = referencedPath.Replace('\\', '/'),
                    type = asset == null ? null : BlueprinterAssets.GetRuntimeTypeName(asset.GetType())
                });
                return true;
            }

            if (BlueprinterAssets.IsPathUnder(referencedPath, BlueprinterSettings.RootFolder))
            {
                Debug.LogError($"[Blueprinter] Missing Blueprinter dependency '{referencedPath}' in '{sourceFile}'");
                return false;
            }

            return true;
        }

        private static bool ValidateArchivePaths(IEnumerable<string> assetPaths, string modFolder)
        {
            var archivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathCaseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetPath in assetPaths)
            {
                var archivePath = GetArchivePath(assetPath, modFolder);
                if (!RegisterPathCasing(archivePath, pathCaseMap, out var existingPath, out var conflictingPath))
                {
                    Debug.LogError($"[Blueprinter] Source path casing collision '{existingPath}' '{conflictingPath}'");
                    return false;
                }

                if (archivePaths.Add(archivePath))
                    continue;

                Debug.LogError($"[Blueprinter] Source dependency collision '{archivePath}'");
                return false;
            }

            return true;
        }

        private static bool RegisterPathCasing(string path, Dictionary<string, string> pathCaseMap, out string existingPath, out string conflictingPath)
        {
            var parts = path.Replace('\\', '/').Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                current += "/" + parts[i];
                if (pathCaseMap.TryGetValue(current, out existingPath))
                {
                    if (!string.Equals(existingPath, current, StringComparison.Ordinal))
                    {
                        conflictingPath = current;
                        return false;
                    }
                }
                else
                {
                    pathCaseMap[current] = current;
                }
            }

            existingPath = null;
            conflictingPath = null;
            return true;
        }

        private static string GetArchivePath(string assetPath, string modFolder)
        {
            assetPath = assetPath.Replace('\\', '/');
            if (BlueprinterAssets.IsPathUnder(assetPath, modFolder))
                return assetPath;

            return modFolder + "/_Dependencies/" + assetPath.Substring("Assets/".Length);
        }

        private static void AddProjectFile(ZipArchive zip, string projectPath, string archivePath)
        {
            var entry = zip.CreateEntry(archivePath.Replace('\\', '/'), ZipCompressionLevel.Optimal);
            using (var input = new FileStream(BlueprinterAssets.ToAbsolutePath(projectPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = entry.Open())
                input.CopyTo(output);
        }
    }
}
