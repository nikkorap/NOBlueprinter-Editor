using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    public class SourceImportItem
    {
        public SourceReference Reference;
        public UnityEngine.Object Replacement;

        public bool IsScript => !string.IsNullOrEmpty(Reference.assembly);
        public string DisplayName => IsScript ? Reference.type ?? "<unknown script>" : Reference.path ?? "<unknown asset>";
    }

    public class SourceImportSession
    {
        public string ArchivePath;
        public SourceManifest Manifest;
        public List<string> ArchiveFiles = new List<string>();
        public Dictionary<string, string> GuidMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<SourceImportItem> Mappings = new List<SourceImportItem>();
    }

    public static class SourceImporter
    {
        private struct ObjectId
        {
            public string Guid;
            public long FileId;
        }

        [MenuItem("Blueprinter/Import Source ZIP", false, 40)]
        private static void ImportSourceZip()
        {
            var path = EditorUtility.OpenFilePanel("Import Source ZIP", string.Empty, "zip");
            if (string.IsNullOrEmpty(path))
                return;

            var session = CreateSession(path);
            if (session == null)
                return;

            if (session.Mappings.All(item => item.Replacement != null))
                ApplyImport(session);
            else
                SourceImportWindow.Open(session);
        }

        public static SourceImportSession CreateSession(string archivePath)
        {
            var session = new SourceImportSession { ArchivePath = archivePath };
            try
            {
                using (var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    session.Manifest = ReadManifest(zip);
                    if (session.Manifest == null)
                        return null;

                    var destinationRoot = BlueprinterAssets.GetModFolderPath(session.Manifest.modName);
                    var existingModPath = FindExistingModPath(session.Manifest.modName);
                    if (!string.IsNullOrEmpty(existingModPath))
                    {
                        Debug.LogError($"[Blueprinter] Mod folder already exists '{existingModPath}'");
                        return null;
                    }

                    var entries = BuildEntryIndex(zip, destinationRoot);
                    if (entries == null)
                        return null;

                    session.ArchiveFiles = entries.Keys.OrderBy(path => path, StringComparer.Ordinal).ToList();
                    if (!ValidateArchiveFiles(entries, session.GuidMap))
                        return null;
                }
            }
            catch (InvalidDataException exception)
            {
                Debug.LogError($"[Blueprinter] Failed to read source archive {exception.Message}");
                return null;
            }
            var scripts = new Dictionary<string, MonoScript>(StringComparer.Ordinal);
            foreach (var script in RuntimeScriptInfo.GetAll())
                scripts[SourceArchive.ScriptKey(script.AssemblyName, script.FullTypeName)] = script.Script;

            session.Mappings = ResolveReferences(session.Manifest, scripts);
            return session;
        }

        public static void ApplyImport(SourceImportSession session)
        {
            var unmappedReferences = session.Mappings.Count(item => item.Replacement == null);
            var remaps = BuildRemaps(session.Mappings);
            WriteArchiveFiles(session, remaps);
            ModInfo.Save(session.Manifest.modName, session.Manifest.displayName, session.Manifest.version);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BlueprinterAssets.ClearAssetBundleLabels(session.ArchiveFiles.Where(path => !string.Equals(Path.GetExtension(path), ".meta", StringComparison.OrdinalIgnoreCase)));

            if (unmappedReferences > 0)
                Debug.LogWarning($"[Blueprinter] Imported '{session.Manifest.modName}' with {unmappedReferences} unresolved references");
            else
                Debug.Log($"[Blueprinter] Imported '{session.Manifest.modName}'");
        }

        private static SourceManifest ReadManifest(ZipArchive zip)
        {
            var entry = zip.GetEntry(SourceArchive.ManifestFileName);
            if (entry == null)
            {
                Debug.LogError($"[Blueprinter] Missing '{SourceArchive.ManifestFileName}'");
                return null;
            }

            string json;
            using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                json = reader.ReadToEnd();

            var manifest = JsonUtility.FromJson<SourceManifest>(json);
            if (manifest == null)
            {
                Debug.LogError("[Blueprinter] Invalid source manifest");
                return null;
            }

            if (string.IsNullOrEmpty(manifest.modName))
            {
                Debug.LogError("[Blueprinter] Source manifest missing mod name");
                return null;
            }

            if (!string.Equals(BlueprinterAssets.SanitizeFileName(manifest.modName), manifest.modName, StringComparison.Ordinal))
            {
                Debug.LogError($"[Blueprinter] Invalid source mod name '{manifest.modName}'");
                return null;
            }

            return manifest;
        }

        private static Dictionary<string, ZipArchiveEntry> BuildEntryIndex(ZipArchive zip, string destinationRoot)
        {
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            var pathCaseMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                var archivePath = entry.FullName.Replace('\\', '/');
                if (string.Equals(archivePath, SourceArchive.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!IsSafeProjectPath(archivePath) || !archivePath.StartsWith(destinationRoot + "/", StringComparison.Ordinal))
                {
                    Debug.LogError($"[Blueprinter] Source path '{archivePath}' outside '{destinationRoot}'");
                    return null;
                }

                var assetPath = archivePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? archivePath.Substring(0, archivePath.Length - ".meta".Length) : archivePath;
                if (!RegisterPathCasing(assetPath, pathCaseMap, out var existingPath, out var conflictingPath))
                {
                    Debug.LogError($"[Blueprinter] Source path casing collision '{existingPath}' '{conflictingPath}'");
                    return null;
                }

                if (BlueprinterAssets.IsCodeOrAssemblyFile(assetPath))
                {
                    Debug.LogError($"[Blueprinter] Source archive contains code '{archivePath}'");
                    return null;
                }

                if (entries.ContainsKey(archivePath))
                {
                    Debug.LogError($"[Blueprinter] Source path collision '{archivePath}'");
                    return null;
                }
                entries[archivePath] = entry;
            }

            return entries;
        }

        private static string FindExistingModPath(string modName)
        {
            foreach (var existingName in BlueprinterAssets.GetModNames())
            {
                if (string.Equals(existingName, modName, StringComparison.OrdinalIgnoreCase))
                    return BlueprinterAssets.GetModFolderPath(existingName);
            }

            var modRoot = BlueprinterAssets.ToAbsolutePath(BlueprinterSettings.ModRootFolder);
            if (!Directory.Exists(modRoot))
                return null;

            foreach (var path in Directory.EnumerateFileSystemEntries(modRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (string.Equals(name, modName, StringComparison.OrdinalIgnoreCase))
                    return BlueprinterSettings.ModRootFolder + "/" + name;
            }

            return null;
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

        private static bool IsSafeProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.IndexOf(':') >= 0)
            {
                return false;
            }

            foreach (var part in path.Split('/'))
            {
                if (string.IsNullOrEmpty(part) || part == "." || part == "..")
                    return false;
            }

            return true;
        }

        private static bool ValidateArchiveFiles(Dictionary<string, ZipArchiveEntry> entries, Dictionary<string, string> guidMap)
        {
            foreach (var assetPath in entries.Keys.Where(path => !string.Equals(Path.GetExtension(path), ".meta", StringComparison.OrdinalIgnoreCase)))
            {
                var metaPath = assetPath + ".meta";
                if (!entries.TryGetValue(metaPath, out var metaEntry))
                {
                    Debug.LogError($"[Blueprinter] Missing source meta '{assetPath}'");
                    return false;
                }

                string guid;
                using (var reader = new StreamReader(metaEntry.Open(), Encoding.UTF8))
                    guid = UnitySerializedText.ReadMetaGuid(reader);

                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"[Blueprinter] Invalid source GUID '{metaPath}'");
                    return false;
                }

                var existingPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(existingPath))
                {
                    var replacement = GUID.Generate().ToString();
                    guidMap[guid] = replacement;
                }
            }

            return true;
        }

        private static List<SourceImportItem> ResolveReferences(SourceManifest manifest, Dictionary<string, MonoScript> scripts)
        {
            var mappings = new List<SourceImportItem>();
            foreach (var reference in manifest.references)
            {
                UnityEngine.Object replacement;
                if (!string.IsNullOrEmpty(reference.assembly))
                {
                    scripts.TryGetValue(SourceArchive.ScriptKey(reference.assembly, reference.type), out var script);
                    replacement = script;
                }
                else
                {
                    var isExternalAsset = SourceArchive.IsExternalAssetPath(reference.path);
                    var assetPath = isExternalAsset ? reference.path : string.IsNullOrEmpty(reference.path) ? null : BlueprinterSettings.GameAssetRootFolder + "/" + reference.path.Replace('\\', '/');
                    if (!isExternalAsset && !BlueprinterAssets.IsGameAssetPath(assetPath))
                    {
                        replacement = null;
                    }
                    else
                    {
                        var asset = SourceArchive.ResolveAsset(assetPath, reference.fileId);
                        replacement = asset != null && (string.IsNullOrEmpty(reference.type) || string.Equals(BlueprinterAssets.GetRuntimeTypeName(asset.GetType()), reference.type, StringComparison.Ordinal)) ? asset : null;
                    }
                }

                mappings.Add(new SourceImportItem { Reference = reference, Replacement = replacement });
            }

            return mappings.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static Dictionary<string, ObjectId> BuildRemaps(IEnumerable<SourceImportItem> mappings)
        {
            var result = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in mappings)
            {
                if (mapping.Replacement == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mapping.Replacement, out var guid, out long fileId))
                {
                    continue;
                }

                if (!mapping.IsScript && SourceArchive.IsPrefabAssetReference(mapping.Reference.path, mapping.Reference.fileId))
                    fileId = mapping.Reference.fileId;

                result[SourceArchive.ReferenceKey(mapping.Reference.guid, mapping.Reference.fileId)] =
                    new ObjectId { Guid = guid, FileId = fileId };
            }

            return result;
        }

        private static int WriteArchiveFiles(SourceImportSession session, Dictionary<string, ObjectId> remaps)
        {
            var replacements = 0;
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                using (var file = new FileStream(session.ArchivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var zip = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    foreach (var projectPath in session.ArchiveFiles)
                    {
                        var destination = BlueprinterAssets.ToAbsolutePath(projectPath);
                        replacements += WriteEntry(zip.GetEntry(projectPath), destination, remaps, session.GuidMap);
                    }
                }
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            return replacements;
        }

        private static int WriteEntry(ZipArchiveEntry entry, string destination, Dictionary<string, ObjectId> remaps, Dictionary<string, string> guidMap)
        {
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var isMeta = string.Equals(Path.GetExtension(entry.FullName), ".meta", StringComparison.OrdinalIgnoreCase);
            var isSerializedText = isMeta;
            if (!isMeta)
            {
                using (var stream = entry.Open())
                    isSerializedText = UnitySerializedText.IsSerializedText(stream);
            }

            if (isSerializedText)
            {
                string text;
                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                    text = reader.ReadToEnd();

                var rewritten = text;
                if (isMeta)
                {
                    string metaGuid;
                    using (var reader = new StringReader(text))
                        metaGuid = UnitySerializedText.ReadMetaGuid(reader);

                    if (!string.IsNullOrEmpty(metaGuid) && guidMap.TryGetValue(metaGuid, out var replacementGuid))
                        rewritten = UnitySerializedText.ReplaceMetaGuid(rewritten, replacementGuid);
                }

                var replacements = 0;
                rewritten = UnitySerializedText.ObjectReferenceRegex.Replace(rewritten, match =>
                {
                    if (!long.TryParse(match.Groups[1].Value, out var fileId))
                        return match.Value;

                    var key = SourceArchive.ReferenceKey(match.Groups[2].Value, fileId);
                    if (remaps.TryGetValue(key, out var target))
                    {
                        replacements++;
                        return UnitySerializedText.RewriteObjectReference(match, target.Guid, target.FileId.ToString());
                    }

                    if (guidMap.TryGetValue(match.Groups[2].Value, out var remappedGuid))
                    {
                        replacements++;
                        return UnitySerializedText.RewriteObjectReference(match, remappedGuid, fileId.ToString());
                    }

                    return match.Value;
                });
                rewritten = UnitySerializedText.RewriteAssetReferenceGuids(rewritten, guidMap, out var addressableReplacements);
                replacements += addressableReplacements;

                if (!string.Equals(text, rewritten, StringComparison.Ordinal))
                {
                    using (var writer = new StreamWriter(new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None), new UTF8Encoding(false)))
                    {
                        writer.Write(rewritten);
                    }

                    return replacements;
                }
            }

            using (var input = entry.Open())
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            return 0;
        }
    }
}
