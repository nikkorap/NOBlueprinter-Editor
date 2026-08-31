using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public static class AssetRipperImporter
    {
        private struct ObjectId
        {
            public string Guid;
            public long FileId;
        }

        private class ImportAsset
        {
            public string SourcePath;
            public string DestinationPath;
            public string ExportGuid;
            public string ProjectGuid;
            public string PreviewShader;
        }

        private static readonly HashSet<string> SkippedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scripts",
            "plugins",
            "scenes",
            "editor",
            "lightingsettings",
            "streamingassets",
            "missions",
            "cubemap",
            "resources",
            "textasset"
        };

        private static readonly HashSet<string> IncludedResourceFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "fonts & materials",
            "maps",
            "shaders",
            "style sheets",
            "stylesystem"
        };

        private static readonly Regex NamespaceRegex = new Regex(@"^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled);

        public static void Import(string assetsFolder)
        {
            GameAssetLock.IsLocked = false;
            try
            {
                ImportCore(assetsFolder);
            }
            finally
            {
                GameAssetLock.IsLocked = true;
            }
        }

        private static void ImportCore(string assetsFolder)
        {
            var runtimeScriptsByType = BuildRuntimeScriptIndex(RuntimeScriptInfo.GetAll());
            var pathCaseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!BuildExistingPathCaseMap(pathCaseMap))
                return;

            var assets = new List<ImportAsset>();
            var scriptPathsByGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var shaderRemaps = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            var destinationSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!ScanExport(assetsFolder, assets, scriptPathsByGuid, shaderRemaps, pathCaseMap, destinationSources))
                return;

            if (assets.Count == 0)
            {
                Debug.LogError("[Blueprinter] No game assets found in AssetRipper export");
                return;
            }

            BlueprinterAssets.EnsureFolder(GameAssetRootFolder);

            var assetGuidRemaps = assets.ToDictionary(asset => asset.ExportGuid, asset => asset.ProjectGuid, StringComparer.OrdinalIgnoreCase);

            AssetDatabase.DisallowAutoRefresh();
            try
            {
                MoveStaleAssets(assets);
                WriteAssets(assets, scriptPathsByGuid, runtimeScriptsByType, shaderRemaps, assetGuidRemaps);
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            AssetDatabase.Refresh();
            BlueprinterAssets.ClearAssetBundleLabels(assets.Select(asset => asset.DestinationPath));
            Debug.Log("[Blueprinter] Imported game assets");
        }

        private static bool ScanExport(string assetsFolder, List<ImportAsset> assets, Dictionary<string, string> scriptPathsByGuid, Dictionary<string, ObjectId> shaderRemaps, Dictionary<string, string> pathCaseMap, Dictionary<string, string> destinationSources)
        {
            foreach (var sourcePath in Directory.EnumerateFiles(assetsFolder, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
                if (extension == ".meta")
                    continue;

                var relativePath = sourcePath.Substring(assetsFolder.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                if (extension == ".cs")
                {
                    if (relativePath.StartsWith("Editor/", StringComparison.OrdinalIgnoreCase) || relativePath.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0 || !File.Exists(sourcePath + ".meta"))
                        continue;

                    if (!TryReadExportGuid(sourcePath, relativePath, out var scriptGuid))
                        return false;

                    scriptPathsByGuid[scriptGuid] = sourcePath;
                    continue;
                }

                if (BlueprinterAssets.IsCodeOrAssemblyFile(sourcePath) || IsInSkippedFolder(relativePath))
                    continue;

                if (!File.Exists(sourcePath + ".meta"))
                {
                    Debug.LogWarning($"[Blueprinter] Skipping AssetRipper file without meta '{relativePath}'");
                    continue;
                }

                if (!TryReadExportGuid(sourcePath, relativePath, out var exportGuid))
                    return false;

                string previewShader = null;
                if (extension == ".shader")
                {
                    var shaderText = File.ReadAllText(sourcePath, Encoding.UTF8);
                    var shaderName = AssetRipperPreviewShaders.ReadShaderName(shaderText);
                    var existingShader = string.IsNullOrEmpty(shaderName) ? null : Shader.Find(shaderName);
                    if (existingShader != null)
                    {
                        var existingPath = AssetDatabase.GetAssetPath(existingShader);
                        if (!string.IsNullOrEmpty(existingPath) && !BlueprinterAssets.IsPathUnder(existingPath, GameAssetRootFolder) && !BlueprinterAssets.IsPathUnder(existingPath, ModRootFolder) && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existingShader, out var existingGuid, out long existingFileId) && !string.IsNullOrEmpty(existingGuid) && existingFileId > 0)
                        {
                            shaderRemaps[exportGuid] = new ObjectId
                            {
                                Guid = existingGuid,
                                FileId = existingFileId
                            };
                            continue;
                        }
                    }

                    if (AssetRipperPreviewShaders.IsDummyShader(shaderText))
                    {
                        if (!AssetRipperPreviewShaders.TryBuildPreviewShader(shaderText, out previewShader))
                        {
                            Debug.LogWarning($"[Blueprinter] Preview shader failed '{relativePath}' using AssetRipper shader");
                        }
                    }
                }

                var destinationPath = GetDestinationPath(relativePath);
                var originalDestinationPath = destinationPath;
                while (destinationSources.ContainsKey(destinationPath))
                {
                    var directory = Path.GetDirectoryName(destinationPath).Replace('\\', '/');
                    destinationPath = directory + "/__dedupe/" + Path.GetFileName(destinationPath);
                }

                if (!string.Equals(destinationPath, originalDestinationPath, StringComparison.Ordinal))
                    Debug.LogWarning($"[Blueprinter] Remapped asset '{relativePath}' -> '{destinationPath}'");

                destinationPath = CanonicalizePath(destinationPath, pathCaseMap);
                if (destinationSources.TryGetValue(destinationPath, out var existingSource))
                {
                    Debug.LogError($"[Blueprinter] Asset path collision '{existingSource}' '{relativePath}'");
                    return false;
                }
                destinationSources[destinationPath] = relativePath;

                if (!TryResolveProjectGuid(destinationPath, exportGuid, out var projectGuid))
                    return false;

                assets.Add(new ImportAsset
                {
                    SourcePath = sourcePath,
                    DestinationPath = destinationPath,
                    ExportGuid = exportGuid,
                    ProjectGuid = projectGuid,
                    PreviewShader = previewShader
                });
            }

            return true;
        }

        private static bool BuildExistingPathCaseMap(Dictionary<string, string> pathCaseMap)
        {
            var rootPath = BlueprinterAssets.ToAbsolutePath("Assets");
            var projectPath = "Assets";
            pathCaseMap[projectPath] = projectPath;
            foreach (var part in GameAssetRootFolder.Substring("Assets/".Length).Split('/'))
            {
                string match = null;
                if (Directory.Exists(rootPath))
                {
                    foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
                    {
                        if (!string.Equals(Path.GetFileName(directory), part, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (match != null)
                        {
                            Debug.LogError($"[Blueprinter] Existing asset path casing collision '{match}' '{directory}'");
                            return false;
                        }
                        match = directory;
                    }
                }

                var candidate = projectPath + "/" + part;
                if (match == null)
                {
                    projectPath = candidate;
                    rootPath = Path.Combine(rootPath, part);
                }
                else
                {
                    projectPath += "/" + Path.GetFileName(match);
                    rootPath = match;
                }
                pathCaseMap[candidate] = projectPath;
            }

            if (!Directory.Exists(rootPath))
                return true;

            rootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var absolutePath in Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = absolutePath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
                var existingProjectPath = CanonicalizePath(GameAssetRootFolder, pathCaseMap) + "/" + relativePath;
                var assetPath = existingProjectPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? existingProjectPath.Substring(0, existingProjectPath.Length - ".meta".Length) : existingProjectPath;
                if (!RegisterPathCasing(assetPath, pathCaseMap, out var existingPath, out var conflictingPath) || !RegisterPathCasing(existingProjectPath, pathCaseMap, out existingPath, out conflictingPath))
                {
                    Debug.LogError($"[Blueprinter] Existing asset path casing collision '{existingPath}' '{conflictingPath}'");
                    return false;
                }
            }

            return true;
        }

        private static string CanonicalizePath(string path, Dictionary<string, string> pathCaseMap)
        {
            var parts = path.Replace('\\', '/').Split('/');
            var current = parts[0];
            if (pathCaseMap.TryGetValue(current, out var existingPath))
                current = existingPath;
            else
                pathCaseMap[current] = current;

            for (var i = 1; i < parts.Length; i++)
            {
                var candidate = current + "/" + parts[i];
                if (pathCaseMap.TryGetValue(candidate, out existingPath))
                    current = existingPath;
                else
                {
                    current = candidate;
                    pathCaseMap[current] = current;
                }
            }

            return current;
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

        private static bool TryReadExportGuid(string sourcePath, string relativePath, out string guid)
        {
            var metaPath = sourcePath + ".meta";
            if (!File.Exists(metaPath))
            {
                Debug.LogError($"[Blueprinter] Missing AssetRipper meta '{relativePath}'");
                guid = null;
                return false;
            }

            guid = UnitySerializedText.ReadMetaGuid(metaPath);
            if (!string.IsNullOrEmpty(guid))
                return true;

            Debug.LogError($"[Blueprinter] Invalid AssetRipper GUID '{relativePath}'");
            return false;
        }

        private static void WriteAssets(List<ImportAsset> assets, Dictionary<string, string> scriptPathsByGuid, Dictionary<string, ObjectId?> runtimeScriptsByType, Dictionary<string, ObjectId> shaderRemaps, Dictionary<string, string> assetGuidRemaps)
        {
            var resolvedScripts = new Dictionary<string, ObjectId?>(StringComparer.OrdinalIgnoreCase);

            foreach (var asset in assets)
            {
                var sourcePath = asset.SourcePath;
                var metaPath = sourcePath + ".meta";
                var destinationPath = BlueprinterAssets.ToAbsolutePath(asset.DestinationPath);
                var destinationMetaPath = destinationPath + ".meta";

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                if (asset.PreviewShader != null)
                {
                    File.WriteAllText(destinationPath, asset.PreviewShader, new UTF8Encoding(false));
                }
                else if (UnitySerializedText.IsSerializedText(sourcePath))
                {
                    var yaml = File.ReadAllText(sourcePath, Encoding.UTF8);
                    var rewrittenYaml = RewriteReferences(yaml, scriptPathsByGuid, runtimeScriptsByType, resolvedScripts, shaderRemaps, assetGuidRemaps);

                    if (string.Equals(yaml, rewrittenYaml, StringComparison.Ordinal))
                        File.Copy(sourcePath, destinationPath, true);
                    else
                        File.WriteAllText(destinationPath, rewrittenYaml, new UTF8Encoding(false));
                }
                else
                {
                    File.Copy(sourcePath, destinationPath, true);
                }

                var meta = File.ReadAllText(metaPath, Encoding.UTF8);
                var rewrittenMeta = UnitySerializedText.ReplaceMetaGuid(meta, asset.ProjectGuid);
                rewrittenMeta = RewriteReferences(rewrittenMeta, scriptPathsByGuid, runtimeScriptsByType, resolvedScripts, shaderRemaps, assetGuidRemaps);

                if (string.Equals(meta, rewrittenMeta, StringComparison.Ordinal))
                    File.Copy(metaPath, destinationMetaPath, true);
                else
                    File.WriteAllText(destinationMetaPath, rewrittenMeta, new UTF8Encoding(false));

            }
        }

        private static string RewriteReferences(string text, Dictionary<string, string> scriptPathsByGuid, Dictionary<string, ObjectId?> runtimeScriptsByType, Dictionary<string, ObjectId?> resolvedScripts, Dictionary<string, ObjectId> shaderRemaps, Dictionary<string, string> assetGuidRemaps)
        {
            var rewritten = UnitySerializedText.ObjectReferenceRegex.Replace(text, match =>
            {
                var exportGuid = match.Groups[2].Value;

                if (shaderRemaps.TryGetValue(exportGuid, out var shader))
                    return UnitySerializedText.RewriteObjectReference(match, shader.Guid, shader.FileId.ToString());

                if (assetGuidRemaps.TryGetValue(exportGuid, out var assetGuid) && !string.Equals(exportGuid, assetGuid, StringComparison.OrdinalIgnoreCase))
                {
                    return UnitySerializedText.RewriteObjectReference(match, assetGuid, match.Groups[1].Value);
                }

                if (!scriptPathsByGuid.TryGetValue(exportGuid, out var scriptPath))
                    return match.Value;

                if (!resolvedScripts.TryGetValue(exportGuid, out var script))
                {
                    if (TryResolveScript(scriptPath, runtimeScriptsByType, out var resolvedScript))
                        script = resolvedScript;

                    resolvedScripts[exportGuid] = script;
                }

                return script.HasValue ? UnitySerializedText.RewriteObjectReference(match, script.Value.Guid, script.Value.FileId.ToString()) : match.Value;
            });

            return UnitySerializedText.RewriteAssetReferenceGuids(rewritten, assetGuidRemaps, out _);
        }

        private static Dictionary<string, ObjectId?> BuildRuntimeScriptIndex(IEnumerable<RuntimeScriptInfo> scripts)
        {
            var result = new Dictionary<string, ObjectId?>(StringComparer.Ordinal);
            foreach (var script in scripts)
            {
                var reference = new ObjectId { Guid = script.Guid, FileId = script.FileId };
                if (result.ContainsKey(script.FullTypeName))
                    result[script.FullTypeName] = null;
                else
                    result.Add(script.FullTypeName, reference);
            }

            return result;
        }

        private static bool TryResolveScript(string scriptPath, Dictionary<string, ObjectId?> runtimeScriptsByType, out ObjectId result)
        {
            result = default;
            var typeName = Path.GetFileNameWithoutExtension(scriptPath);
            var ns = ReadNamespaceFromCs(scriptPath);
            var fullTypeName = string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}";

            if (runtimeScriptsByType.TryGetValue(fullTypeName, out var match) && match.HasValue)
            {
                result = match.Value;
                return true;
            }

            Debug.LogWarning(runtimeScriptsByType.ContainsKey(fullTypeName) ? $"[Blueprinter] Ambiguous runtime script '{fullTypeName}' references unresolved" : $"[Blueprinter] Missing runtime script '{fullTypeName}' references unresolved");
            return false;
        }

        private static bool TryResolveProjectGuid(string destinationPath, string exportGuid, out string projectGuid)
        {
            var destinationMetaPath = BlueprinterAssets.ToAbsolutePath(destinationPath + ".meta");
            if (File.Exists(destinationMetaPath))
            {
                projectGuid = UnitySerializedText.ReadMetaGuid(destinationMetaPath);
                if (!string.IsNullOrEmpty(projectGuid))
                    return true;

                Debug.LogError($"[Blueprinter] Invalid imported asset GUID '{destinationPath}'");
                return false;
            }

            projectGuid = string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(exportGuid)) ? exportGuid : GUID.Generate().ToString();
            return true;
        }

        private static void MoveStaleAssets(List<ImportAsset> assets)
        {
            // Move meta files with stale assets to preserve Unity identity
            var expectedPaths = new HashSet<string>(assets.Select(asset => asset.DestinationPath), StringComparer.OrdinalIgnoreCase);
            var movedCount = 0;

            foreach (var assetPath in BlueprinterAssets.GetGameAssetPaths())
            {
                if (expectedPaths.Contains(assetPath))
                    continue;

                var relativePath = assetPath.Substring(GameAssetRootFolder.Length).TrimStart('/');
                var stalePath = StaleGameAssetRootFolder + "/" + relativePath;
                var source = BlueprinterAssets.ToAbsolutePath(assetPath);
                var destination = BlueprinterAssets.ToAbsolutePath(stalePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Move(source, destination);

                var sourceMeta = source + ".meta";
                File.Move(sourceMeta, destination + ".meta");

                movedCount++;
            }

            if (movedCount > 0)
                Debug.LogWarning($"[Blueprinter] Replace references to {movedCount} stale game assets in _stale then delete them");
        }

        private static string GetDestinationPath(string exportRelativePath)
        {
            var directory = Path.GetDirectoryName(exportRelativePath) ?? string.Empty;
            var extension = Path.GetExtension(exportRelativePath);
            var filename = Path.GetFileNameWithoutExtension(exportRelativePath);
            if (filename.Length > 2 && filename.EndsWith("_0", StringComparison.Ordinal))
                filename = filename.Substring(0, filename.Length - 2);

            var destinationDirectory = string.IsNullOrEmpty(directory) ? GameAssetRootFolder : GameAssetRootFolder + "/" + directory.Replace(Path.DirectorySeparatorChar, '/');

            return destinationDirectory + "/" + filename + PlaceholderSuffix + extension;
        }

        private static bool IsInSkippedFolder(string relativePath)
        {
            var separator = relativePath.IndexOf('/');
            if (separator < 0)
                return false;

            var topLevelFolder = relativePath.Substring(0, separator);
            if (!SkippedFolderNames.Contains(topLevelFolder))
                return false;

            if (!string.Equals(topLevelFolder, "resources", StringComparison.OrdinalIgnoreCase))
                return true;

            var resourcePath = relativePath.Substring(separator + 1);
            var resourceSeparator = resourcePath.IndexOf('/');
            var resourceFolder = resourceSeparator < 0 ? resourcePath : resourcePath.Substring(0, resourceSeparator);

            return !IncludedResourceFolderNames.Contains(resourceFolder);
        }

        public static bool IsAssetsFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            var directory = new DirectoryInfo(path);
            return string.Equals(directory.Name, "Assets", StringComparison.OrdinalIgnoreCase) && directory.Parent != null && string.Equals(directory.Parent.Name, "ExportedProject", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadNamespaceFromCs(string csPath)
        {
            foreach (var line in File.ReadLines(csPath))
            {
                var match = NamespaceRegex.Match(line);
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }
    }
}
