#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using ZipCompressionLevel = System.IO.Compression.CompressionLevel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BlueprinterMigration
{
    public class LegacySourceExporter : EditorWindow
    {
        private const string DoNotShipBundleName = "_donotship";
        private const string BlueprinterRoot = "Assets/Blueprinter";
        private const string DoNotShipRoot = BlueprinterRoot + "/" + DoNotShipBundleName;
        private const string GeneratedManifestPath = "Assets/Blueprinter/Generated/patch_manifest.json";
        private const string ManifestFileName = "source_manifest.json";
        private const string SourceZipSuffix = ".source.zip";
        private const long PrefabAssetFileId = 100100000;

        private static readonly Regex ObjectReferenceRegex = new Regex(@"\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-fA-F]{32}),\s*type:\s*(\d+)\}", RegexOptions.Compiled);

        private static readonly string ProjectRoot = Path.GetDirectoryName(Application.dataPath);

        private Blueprinter.BlueprinterBuildConfig _config;
        private Blueprinter.BlueprinterBuildConfig.BundleEntry[] _bundles = Array.Empty<Blueprinter.BlueprinterBuildConfig.BundleEntry>();
        private string[] _bundleNames = Array.Empty<string>();
        private int _selectedBundle;

        [MenuItem("Blueprinter/Export Legacy Source ZIP")]
        private static void Open()
        {
            var config = FindBuildConfig();
            if (config == null)
            {
                Debug.LogError("[Blueprinter Migration] Missing BlueprinterBuildConfig");
                return;
            }

            var window = GetWindow<LegacySourceExporter>("Export Legacy Source ZIP");
            window.minSize = new Vector2(500f, 250f);
            window._config = config;
            window.RefreshBundles();
        }

        private void OnEnable()
        {
            if (_config == null)
                _config = FindBuildConfig();
            if (_config != null)
                RefreshBundles();
        }

        private void OnGUI()
        {
            if (_config == null)
            {
                Close();
                return;
            }

            if (EditorSettings.serializationMode != SerializationMode.ForceText)
                EditorGUILayout.LabelField("Asset Serialization Mode must be Force Text. Change it under Project Settings > Editor before exporting.");

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Select Mod", GUILayout.Width(100f));

                if (_bundleNames.Length == 0)
                    EditorGUILayout.LabelField("No configured mods found.");
                else
                    _selectedBundle = EditorGUILayout.Popup(Mathf.Clamp(_selectedBundle, 0, _bundleNames.Length - 1), _bundleNames);
            }

            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_bundleNames.Length == 0 || EditorSettings.serializationMode != SerializationMode.ForceText))
            {
                if (GUILayout.Button("Export Source ZIP", GUILayout.Height(32f)))
                    ExportSelectedBundle();
            }
        }

        private void RefreshBundles()
        {
            var configuredBundles = _config.bundles ?? new List<Blueprinter.BlueprinterBuildConfig.BundleEntry>();
            _bundleNames = AssetDatabase.GetAllAssetBundleNames().Where(name => !string.Equals(name, DoNotShipBundleName, StringComparison.OrdinalIgnoreCase)).ToArray();
            _bundles = _bundleNames.Select(name => configuredBundles.FirstOrDefault(entry => entry != null && string.Equals(entry.bundleName, name, StringComparison.Ordinal)) ?? new Blueprinter.BlueprinterBuildConfig.BundleEntry { bundleName = name }).ToArray();

            _selectedBundle = 0;
            if (!string.IsNullOrEmpty(_config.selectedBundle))
            {
                var index = Array.IndexOf(_bundleNames, _config.selectedBundle);
                if (index >= 0)
                    _selectedBundle = index;
            }
        }

        private static Blueprinter.BlueprinterBuildConfig FindBuildConfig()
        {
            var guids = AssetDatabase.FindAssets("t:BlueprinterBuildConfig");
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<Blueprinter.BlueprinterBuildConfig>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private void ExportSelectedBundle()
        {
            var bundle = _bundles[_selectedBundle];
            var bundleName = bundle.bundleName;
            var displayName = string.IsNullOrWhiteSpace(bundle.displayName) ? bundleName : bundle.displayName.Trim();
            var version = bundle.version?.Trim() ?? string.Empty;
            var outputDirectory = !string.IsNullOrWhiteSpace(_config.copyTargetFolder) && Directory.Exists(_config.copyTargetFolder) ? _config.copyTargetFolder : ProjectRoot;
            var baseName = SanitizeFileName(displayName);
            var fileVersion = string.IsNullOrEmpty(version) ? string.Empty : SanitizeFileName(version);
            var defaultName = string.IsNullOrEmpty(fileVersion) ? baseName + SourceZipSuffix : baseName + "_" + fileVersion + SourceZipSuffix;
            var outputPath = EditorUtility.SaveFilePanel("Export Legacy Source ZIP", outputDirectory, defaultName, "zip");

            if (string.IsNullOrEmpty(outputPath))
                return;

            if (!outputPath.EndsWith(SourceZipSuffix, StringComparison.OrdinalIgnoreCase))
            {
                if (outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    outputPath = outputPath.Substring(0, outputPath.Length - 4);
                outputPath += SourceZipSuffix;
            }

            try
            {
                ExportBundle(bundleName, displayName, version, outputPath);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void ExportBundle(string bundleName, string displayName, string version, string outputPath)
        {
            var modName = SanitizeFileName(bundleName);
            var bundleAssetPaths = GetEffectiveBundleAssetPaths(bundleName);
            if (bundleAssetPaths.Count == 0)
            {
                Debug.LogWarning($"[Blueprinter Migration] No source assets for '{bundleName}'");
                return;
            }

            EditorUtility.DisplayProgressBar("Export Legacy Source ZIP", "Collecting dependencies...", 0.1f);
            var assetPaths = CollectSourceAssetPaths(bundleAssetPaths);
            if (assetPaths.Count == 0)
            {
                foreach (var bundleAssetPath in bundleAssetPaths)
                    LogSourceAssetRejection(bundleAssetPath);
                return;
            }

            foreach (var assetPath in assetPaths)
            {
                if (File.Exists(ToAbsoluteProjectPath(assetPath + ".meta")))
                    continue;

                Debug.LogError($"[Blueprinter Migration] Missing source meta '{assetPath}'");
                return;
            }

            var modFolder = "Assets/Blueprinter/Mods/" + modName;
            var rootPaths = new HashSet<string>(bundleAssetPaths, StringComparer.OrdinalIgnoreCase);
            if (!ValidateArchivePaths(assetPaths, rootPaths, modFolder))
                return;

            EditorUtility.DisplayProgressBar("Export Legacy Source ZIP", "Collecting external references...", 0.35f);
            var manifest = new SourceManifest
            {
                modName = modName,
                displayName = displayName ?? bundleName,
                version = version ?? string.Empty
            };
            if (!CollectExternalReferences(assetPaths, manifest))
                return;

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            EditorUtility.DisplayProgressBar("Export Legacy Source ZIP", "Writing source ZIP...", 0.7f);
            using (var file = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var manifestEntry = zip.CreateEntry(ManifestFileName, ZipCompressionLevel.Optimal);
                using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(false)))
                    writer.Write(JsonUtility.ToJson(manifest, true));

                for (var i = 0; i < assetPaths.Count; i++)
                {
                    var assetPath = assetPaths[i];
                    EditorUtility.DisplayProgressBar("Export Legacy Source ZIP", assetPath, 0.7f + 0.3f * (i / (float)assetPaths.Count));

                    var archivePath = GetArchivePath(assetPath, rootPaths, modFolder);
                    AddProjectFile(zip, assetPath, archivePath);
                    AddProjectFile(zip, assetPath + ".meta", archivePath + ".meta");
                }
            }

            Debug.Log($"[Blueprinter Migration] Exported '{bundleName}' to '{outputPath}'");
        }

        private static List<string> GetEffectiveBundleAssetPaths(string bundleName)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in AssetDatabase.GetAssetPathsFromAssetBundle(bundleName))
            {
                if (string.IsNullOrEmpty(path))
                    continue;

                if (!AssetDatabase.IsValidFolder(path))
                {
                    if (IsExportableAssetPath(path))
                        result.Add(NormalizeProjectPath(path));
                    continue;
                }

                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { path }))
                {
                    var childPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(childPath) || AssetDatabase.IsValidFolder(childPath))
                        continue;

                    if (!string.Equals(AssetDatabase.GetImplicitAssetBundleName(childPath), bundleName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (IsExportableAssetPath(childPath))
                        result.Add(NormalizeProjectPath(childPath));
                }
            }

            return result.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static void LogSourceAssetRejection(string assetPath)
        {
            if (!IsBlueprinterPath(assetPath))
                Debug.LogError($"[Blueprinter Migration] Source asset outside Assets/Blueprinter '{assetPath}'");
            else if (AssetDatabase.IsValidFolder(assetPath))
                Debug.LogError($"[Blueprinter Migration] Source asset is folder '{assetPath}'");
            else if (IsDoNotShipPath(assetPath))
                Debug.LogError($"[Blueprinter Migration] Bundle source is _donotship '{assetPath}'");
            else if (string.Equals(assetPath, GeneratedManifestPath, StringComparison.OrdinalIgnoreCase))
                Debug.LogError($"[Blueprinter Migration] Bundle source is generated manifest '{assetPath}'");
            else if (!IsExportableAssetPath(assetPath))
                Debug.LogError($"[Blueprinter Migration] Source asset not exportable '{assetPath}'");
            else if (!File.Exists(ToAbsoluteProjectPath(NormalizeProjectPath(assetPath))))
                Debug.LogError($"[Blueprinter Migration] Source file missing '{assetPath}'");
        }

        private static List<string> CollectSourceAssetPaths(IEnumerable<string> bundleAssetPaths)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetPath in bundleAssetPaths)
            {
                TryAddSourcePath(assetPath, result);
                foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
                    TryAddSourcePath(dependency, result);
            }

            return result.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static void TryAddSourcePath(string assetPath, HashSet<string> result)
        {
            if (string.IsNullOrEmpty(assetPath) || !IsBlueprinterPath(assetPath) || AssetDatabase.IsValidFolder(assetPath) || IsDoNotShipPath(assetPath) || string.Equals(assetPath, GeneratedManifestPath, StringComparison.OrdinalIgnoreCase) || !IsExportableAssetPath(assetPath))
            {
                return;
            }

            var normalizedPath = NormalizeProjectPath(assetPath);
            if (File.Exists(ToAbsoluteProjectPath(normalizedPath)))
                result.Add(normalizedPath);
        }

        private static bool IsExportableAssetPath(string assetPath)
        {
            var extension = Path.GetExtension(assetPath);
            return !string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".mdb", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".asmref", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".rsp", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CollectExternalReferences(List<string> assetPaths, SourceManifest manifest)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var internalGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in assetPaths)
            {
                var guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                    internalGuids.Add(guid);
            }

            var scriptMap = BuildScriptReferenceMap();
            for (var i = 0; i < assetPaths.Count; i++)
            {
                var assetPath = assetPaths[i];
                EditorUtility.DisplayProgressBar("Export Legacy Source ZIP", "Inspecting " + assetPath, 0.35f + 0.35f * (i / (float)assetPaths.Count));

                if (!CollectExternalReferencesFromFile(assetPath, internalGuids, scriptMap, seen, manifest) || !CollectExternalReferencesFromFile(assetPath + ".meta", internalGuids, scriptMap, seen, manifest))
                {
                    return false;
                }
            }

            manifest.references = manifest.references.OrderBy(reference => reference.assembly, StringComparer.OrdinalIgnoreCase).ThenBy(reference => reference.path, StringComparer.OrdinalIgnoreCase).ThenBy(reference => reference.type, StringComparer.Ordinal).ThenBy(reference => reference.fileId).ToList();
            return true;
        }

        private static bool CollectExternalReferencesFromFile(string projectPath, HashSet<string> internalGuids, Dictionary<string, ReferenceTarget> scriptMap, HashSet<string> seen, SourceManifest manifest)
        {
            var absolutePath = ToAbsoluteProjectPath(projectPath);
            string text;
            if (projectPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                text = File.ReadAllText(absolutePath, Encoding.UTF8);
            else if (!TryReadSerializedText(absolutePath, out text))
                return true;

            foreach (Match match in ObjectReferenceRegex.Matches(text))
            {
                if (!long.TryParse(match.Groups[1].Value, out var fileId))
                    continue;

                var guid = match.Groups[2].Value;
                if (internalGuids.Contains(guid))
                    continue;

                var key = ReferenceKey(guid, fileId);
                if (!seen.Add(key))
                    continue;

                if (scriptMap.TryGetValue(key, out var script))
                {
                    manifest.references.Add(new SourceReference
                    {
                        guid = guid,
                        fileId = fileId,
                        assembly = script.AssemblyName,
                        type = script.FullTypeName
                    });
                    continue;
                }

                var referencedPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(referencedPath) && !IsExportableAssetPath(referencedPath))
                    continue;

                if (IsDoNotShipPath(referencedPath))
                {
                    var baseAsset = ResolveBaseReference(referencedPath, fileId);
                    manifest.references.Add(new SourceReference
                    {
                        guid = guid,
                        fileId = fileId,
                        path = NormalizeProjectPath(referencedPath).Substring(DoNotShipRoot.Length).TrimStart('/'),
                        type = baseAsset?.TypeName
                    });
                    continue;
                }

                if (!string.IsNullOrEmpty(referencedPath) && (referencedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || referencedPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) && !IsBlueprinterPath(referencedPath))
                {
                    var externalAsset = ResolveBaseReference(referencedPath, fileId);
                    manifest.references.Add(new SourceReference
                    {
                        guid = guid,
                        fileId = fileId,
                        path = NormalizeProjectPath(referencedPath),
                        type = externalAsset?.TypeName
                    });
                    continue;
                }

                if (IsBlueprinterPath(referencedPath))
                {
                    Debug.LogError($"[Blueprinter Migration] Missing Blueprinter dependency '{referencedPath}' in '{projectPath}'");
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, ReferenceTarget> BuildScriptReferenceMap()
        {
            var result = new Dictionary<string, ReferenceTarget>(StringComparer.OrdinalIgnoreCase);
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                var target = CreateTarget(script);
                if (target == null || string.IsNullOrEmpty(target.FullTypeName) || string.IsNullOrEmpty(target.Guid))
                    continue;

                result[ReferenceKey(target.Guid, target.FileId)] = target;
            }

            return result;
        }

        private static ReferenceTarget ResolveBaseReference(string assetPath, long fileId)
        {
            if (fileId == PrefabAssetFileId && string.Equals(Path.GetExtension(assetPath), ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return CreateTarget(AssetDatabase.LoadMainAssetAtPath(assetPath));
            }

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out _, out long candidateFileId) && candidateFileId == fileId)
                {
                    return CreateTarget(asset);
                }
            }

            return null;
        }

        private static ReferenceTarget CreateTarget(UnityEngine.Object asset)
        {
            if (asset == null)
                return null;

            var target = new ReferenceTarget
            {
                TypeName = GetTypeName(asset.GetType())
            };
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out target.Guid, out target.FileId);

            if (asset is MonoScript script)
            {
                var type = script.GetClass();
                target.AssemblyName = type?.Assembly?.GetName()?.Name ?? string.Empty;
                target.FullTypeName = type?.FullName;
            }

            return target;
        }

        private static string GetTypeName(Type type)
        {
            if (type == null)
                return null;

            while (type.Assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal) && type.BaseType != null && typeof(UnityEngine.Object).IsAssignableFrom(type.BaseType))
            {
                type = type.BaseType;
            }

            return type.FullName + ", " + type.Assembly.GetName().Name;
        }

        private static bool IsBlueprinterPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = NormalizeProjectPath(assetPath);
            return string.Equals(assetPath, BlueprinterRoot, StringComparison.OrdinalIgnoreCase) || assetPath.StartsWith(BlueprinterRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDoNotShipPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            assetPath = NormalizeProjectPath(assetPath);
            return string.Equals(assetPath, DoNotShipRoot, StringComparison.OrdinalIgnoreCase) || assetPath.StartsWith(DoNotShipRoot + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadSerializedText(string path, out string text)
        {
            text = null;
            using (var reader = new StreamReader(path, Encoding.UTF8, true))
            {
                var firstLine = reader.ReadLine();
                if (firstLine == null || !firstLine.StartsWith("%YAML", StringComparison.Ordinal))
                    return false;
            }

            text = File.ReadAllText(path, Encoding.UTF8);
            return true;
        }

        private static bool ValidateArchivePaths(IEnumerable<string> assetPaths, HashSet<string> rootPaths, string modFolder)
        {
            var archivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathCaseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetPath in assetPaths)
            {
                var archivePath = GetArchivePath(assetPath, rootPaths, modFolder);
                if (!RegisterPathCasing(archivePath, pathCaseMap, out var existingPath, out var conflictingPath))
                {
                    Debug.LogError($"[Blueprinter Migration] Source path casing collision '{existingPath}' '{conflictingPath}'");
                    return false;
                }

                if (archivePaths.Add(archivePath))
                    continue;

                Debug.LogError($"[Blueprinter Migration] Source dependency collision '{archivePath}'");
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

        private static string GetArchivePath(string assetPath, HashSet<string> rootPaths, string modFolder)
        {
            var relativePath = NormalizeProjectPath(assetPath).Substring("Assets/".Length);
            return rootPaths.Contains(assetPath) ? modFolder + "/" + relativePath : modFolder + "/_Dependencies/" + relativePath;
        }

        private static void AddProjectFile(ZipArchive zip, string projectPath, string archivePath)
        {
            var entry = zip.CreateEntry(NormalizeProjectPath(archivePath), ZipCompressionLevel.Optimal);
            using (var input = new FileStream(ToAbsoluteProjectPath(projectPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = entry.Open())
                input.CopyTo(output);
        }

        private static string ToAbsoluteProjectPath(string projectPath)
        {
            return Path.Combine(ProjectRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ReferenceKey(string guid, long fileId)
        {
            return (guid ?? string.Empty) + ":" + fileId;
        }

        private static string NormalizeProjectPath(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "mod";

            var characters = value.Trim().ToCharArray();
            var invalid = Path.GetInvalidFileNameChars();
            for (var i = 0; i < characters.Length; i++)
            {
                if (invalid.Contains(characters[i]) || "<>:\"/\\|?*".IndexOf(characters[i]) >= 0)
                    characters[i] = '_';
            }

            var result = new string(characters).TrimEnd(' ', '.');
            return string.IsNullOrEmpty(result) ? "mod" : result;
        }

        private class ReferenceTarget
        {
            public string Guid;
            public long FileId;
            public string TypeName;
            public string AssemblyName;
            public string FullTypeName;
        }

        [Serializable]
        private class SourceManifest
        {
            public string modName;
            public string displayName;
            public string version;
            public List<SourceReference> references = new List<SourceReference>();
        }

        [Serializable]
        private class SourceReference
        {
            public string guid;
            public long fileId;
            public string path;
            public string assembly;
            public string type;
        }
    }
}
#endif
