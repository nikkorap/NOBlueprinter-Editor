using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public static class ModBuilder
    {
        private const string ManifestAssetPath = GeneratedFolder + "/patch_manifest.json";

        public static bool ValidateVersion(string input, out string value)
        {
            value = string.Empty;
            foreach (var character in input)
            {
                if ((character >= '0' && character <= '9') || character == '.')
                    value += character;
            }

            return Version.TryParse(value, out var version) && version.Build != -1 && version.Revision == -1 && version.ToString() == value;
        }

        private class GameAssetInfo
        {
            public LocationRef Location;
            public long LocalFileId;
            public AssetPatch Patch;
        }

        private class ManifestBuildContext
        {
            public HashSet<string> IncludedAssetPaths;
            public Dictionary<string, GameAssetInfo> GameAssets;
            public Dictionary<int, GameAssetInfo> UrpRendererAssetsByIndex;
            public PatchManifest Manifest;
            public bool HasErrors;
        }

        public static void Build(string modName, string displayName, string version, string outputFolder)
        {
            displayName = string.IsNullOrWhiteSpace(displayName) ? modName : displayName;
            var modAssetPaths = BlueprinterAssets.GetModAssetPaths(modName);

            var includedAssetPaths = GetIncludedAssetPaths(modAssetPaths);
            if (includedAssetPaths == null || !PrefabHashWriter.Write(modAssetPaths))
                return;

            var manifest = BuildPatchManifest(displayName, version, modAssetPaths, includedAssetPaths);
            if (manifest == null)
                return;

            BlueprinterAssets.EnsureFolder(GeneratedFolder);
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(ManifestAssetPath, json, Encoding.UTF8);
            AssetDatabase.ImportAsset(ManifestAssetPath, ImportAssetOptions.ForceUpdate);

            var buildAssetPaths = new string[modAssetPaths.Length + 1];
            Array.Copy(modAssetPaths, buildAssetPaths, modAssetPaths.Length);
            buildAssetPaths[buildAssetPaths.Length - 1] = ManifestAssetPath;
            Array.Sort(buildAssetPaths, StringComparer.Ordinal);

            var cacheFolder = Path.Combine("BlueprinterCache", "Assetbundles");
            Directory.CreateDirectory(cacheFolder);

            var gameAssetPaths = BlueprinterAssets.GetGameAssetPaths();
            var bundleName = modName.ToLowerInvariant();
            if (BuildAssetBundles(cacheFolder, bundleName, buildAssetPaths, gameAssetPaths))
                CopyBuiltMod(cacheFolder, bundleName, displayName, version, outputFolder);
        }

        public static void BuildGameAssets()
        {
            var gameAssetPaths = BlueprinterAssets.GetGameAssetPaths();
            if (gameAssetPaths.Length == 0)
            {
                Debug.LogError("[Blueprinter] No game assets found to build");
                return;
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[Blueprinter] Windows build support is not installed");
                return;
            }

            var cacheFolder = Path.Combine("BlueprinterCache", "Assetbundles");
            Directory.CreateDirectory(cacheFolder);

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = GameAssetBundleName,
                    assetNames = gameAssetPaths
                }
            };

            if (BuildPipeline.BuildAssetBundles(cacheFolder, builds, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64) == null)
            {
                Debug.LogError($"[Blueprinter] Failed to build bundle '{GameAssetBundleName}'");
                return;
            }

            Debug.Log($"[Blueprinter] Built bundle '{GameAssetBundleName}'");
        }

        private static GameAssetInfo GetOrCreateGameAssetInfo(string path, ManifestBuildContext context)
        {
            if (!BlueprinterAssets.IsGameAssetPath(path))
                return null;

            if (context.GameAssets.TryGetValue(path, out var gameAssetInfo))
                return gameAssetInfo;

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (mainAsset == null)
                return null;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mainAsset, out _, out long localFileId))
            {
                Debug.LogError($"[Blueprinter] Missing game asset identity '{path}'");
                context.HasErrors = true;
                return null;
            }

            var name = mainAsset.name ?? string.Empty;
            if (name.EndsWith(PlaceholderSuffix, StringComparison.Ordinal))
                name = name.Substring(0, name.Length - PlaceholderSuffix.Length);
            // AssetRipper may append a collision suffix
            if (name.EndsWith("_0", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 2);
            var type = BlueprinterAssets.GetRuntimeTypeName(mainAsset.GetType());

            gameAssetInfo = new GameAssetInfo
            {
                LocalFileId = localFileId,
                Location = new LocationRef
                {
                    id = $"{name}|{type}",
                    asset = new AssetRef
                    {
                        name = name,
                        locator = name,
                        type = type
                    }
                }
            };
            context.GameAssets[path] = gameAssetInfo;
            return gameAssetInfo;
        }

        private static bool TryGetGameAssetInfo(UnityEngine.Object refObj, ManifestBuildContext context, out GameAssetInfo gameAssetInfo)
        {
            gameAssetInfo = null;
            var path = AssetDatabase.GetAssetPath(refObj);
            if (string.IsNullOrEmpty(path))
            {
                var sourceObj = PrefabUtility.GetCorrespondingObjectFromSource(refObj);
                if (sourceObj == null)
                    return false;

                refObj = sourceObj;
                path = AssetDatabase.GetAssetPath(sourceObj);
                if (string.IsNullOrEmpty(path))
                    return false;
            }

            gameAssetInfo = GetOrCreateGameAssetInfo(path, context);
            if (gameAssetInfo == null)
                return false;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(refObj, out _, out long referencedFileId))
            {
                Debug.LogError($"[Blueprinter] Missing game asset reference identity '{path}'");
                context.HasErrors = true;
                return false;
            }

            if (referencedFileId == gameAssetInfo.LocalFileId)
                return true;

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            if (refObj is UnityEngine.Audio.AudioMixerGroup && mainAsset is UnityEngine.Audio.AudioMixer)
                return true;

            Debug.LogError($"[Blueprinter] Unsupported game subasset '{refObj.name}' in '{path}'");
            context.HasErrors = true;
            return false;
        }
        private static PatchManifest BuildPatchManifest(string displayName, string version, string[] modAssetPaths, HashSet<string> includedAssetPaths)
        {
            var manifest = new PatchManifest
            {
                modName = displayName,
                modVersion = version,
                gameVersion = GameAssemblies.InstalledGameVersion
            };

            var context = new ManifestBuildContext
            {
                IncludedAssetPaths = includedAssetPaths,
                GameAssets = new Dictionary<string, GameAssetInfo>(StringComparer.OrdinalIgnoreCase),
                Manifest = manifest
            };
            context.UrpRendererAssetsByIndex = BuildUrpRendererAssetMap(context);
            if (context.HasErrors)
                return null;

            var sortedAssetPaths = new List<string>(includedAssetPaths);
            sortedAssetPaths.Sort(StringComparer.Ordinal);
            foreach (var path in sortedAssetPaths)
                ScanAssetPath(path, context);

            if (context.HasErrors)
                return null;

            if (!OpBuilder.Build(modAssetPaths, manifest))
                return null;

            return manifest;
        }

        private static void ScanAssetPath(string path, ManifestBuildContext context)
        {
            var mainPrefab = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;

            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset == null || asset is GameObject || asset is Component || asset is MonoScript || asset is DefaultAsset || asset is OpCore)
                    continue;

                ScanReferences(new SerializedObject(asset), asset.name, BlueprinterAssets.CreateAssetRef(asset), null, null, 0, context);
            }

            if (mainPrefab != null)
                ScanPrefab(path, mainPrefab, context);
        }

        private static void ScanPrefab(string path, GameObject prefabRoot, ManifestBuildContext context)
        {
            var modAssetRef = BlueprinterAssets.CreateAssetRef(prefabRoot);
            var rootTransform = prefabRoot.transform;

            foreach (var transform in prefabRoot.GetComponentsInChildren<Transform>(true))
            {
                var hierarchyPath = AnimationUtility.CalculateTransformPath(transform, rootTransform);
                var perTypeIndex = new Dictionary<Type, int>();

                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    var type = component.GetType();
                    perTypeIndex.TryGetValue(type, out var componentIndex);
                    perTypeIndex[type] = componentIndex + 1;

                    var locationId = string.IsNullOrEmpty(hierarchyPath) ? $"{prefabRoot.name}/{type.Name}#{componentIndex}" : $"{prefabRoot.name}/{hierarchyPath}/{type.Name}#{componentIndex}";

                    ScanReferences(new SerializedObject(component), locationId, modAssetRef, hierarchyPath, BlueprinterAssets.GetRuntimeTypeName(type), componentIndex, context);
                }
            }
        }

        // URP renderer data uses an index instead of an object reference
        private static Dictionary<int, GameAssetInfo> BuildUrpRendererAssetMap(ManifestBuildContext context)
        {
            var urpPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset", new[] { GameAssetRootFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (BlueprinterAssets.IsGameAssetPath(path))
                    urpPaths.Add(path);
            }

            urpPaths.Sort(StringComparer.Ordinal);
            foreach (var path in urpPaths)
            {
                var urpAsset = AssetDatabase.LoadMainAssetAtPath(path);
                if (urpAsset == null)
                    continue;

                var serialized = new SerializedObject(urpAsset);
                var renderersProperty = serialized.FindProperty("m_RendererDataList") ?? serialized.FindProperty("m_RendererData");
                if (renderersProperty == null || !renderersProperty.isArray)
                    continue;

                var renderers = new Dictionary<int, GameAssetInfo>();
                for (var i = 0; i < renderersProperty.arraySize; i++)
                {
                    var element = renderersProperty.GetArrayElementAtIndex(i);
                    if (element.propertyType != SerializedPropertyType.ObjectReference || element.objectReferenceValue == null)
                    {
                        continue;
                    }

                    if (TryGetGameAssetInfo(element.objectReferenceValue, context, out var rendererAsset))
                    {
                        renderers.Add(i, rendererAsset);
                    }
                }

                if (renderers.Count > 0)
                    return renderers;
            }

            return new Dictionary<int, GameAssetInfo>();
        }

        private static AssetPatch GetOrCreatePatch(ManifestBuildContext context, GameAssetInfo gameAssetInfo)
        {
            if (gameAssetInfo.Patch != null)
                return gameAssetInfo.Patch;

            var patch = new AssetPatch { GameAsset = gameAssetInfo.Location };

            gameAssetInfo.Patch = patch;
            context.Manifest.Patches.Add(patch);

            return patch;
        }

        private static bool BuildAssetBundles(string cacheFolder, string targetBundleName, string[] targetAssetPaths, string[] gameAssetPaths)
        {
            var builds = new List<AssetBundleBuild>
            {
                new AssetBundleBuild
                {
                    assetBundleName = targetBundleName,
                    assetNames = targetAssetPaths
                }
            };

            if (gameAssetPaths.Length > 0)
            {
                builds.Add(new AssetBundleBuild
                {
                    assetBundleName = GameAssetBundleName,
                    assetNames = gameAssetPaths
                });
            }

            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("[Blueprinter] Windows build support is not installed");
                return false;
            }

            if (BuildPipeline.BuildAssetBundles(cacheFolder, builds.ToArray(), BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64) == null)
            {
                Debug.LogError($"[Blueprinter] Failed to build bundle '{targetBundleName}'");
                return false;
            }

            return true;
        }

        private static void CopyBuiltMod(string cacheFolder, string targetBundleName, string displayName, string version, string outputFolder)
        {
            var sourcePath = Path.Combine(cacheFolder, targetBundleName);
            Directory.CreateDirectory(outputFolder);
            var outputName = string.IsNullOrWhiteSpace(version) ? displayName : $"{displayName}_{version}";
            outputName = BlueprinterAssets.SanitizeFileName(outputName);
            if (string.IsNullOrEmpty(outputName))
                outputName = BlueprinterAssets.SanitizeFileName(targetBundleName);

            var destinationPath = Path.Combine(outputFolder, outputName + ".nobp");
            File.Copy(sourcePath, destinationPath, overwrite: true);

            Debug.Log($"[Blueprinter] Built '{destinationPath}'");
        }
        private static readonly Dictionary<string, string> RuntimeMemberPathRemap = new Dictionary<string, string>()
        {
            { "m_Material",   "material" },
            { "m_Materials",  "sharedMaterials" },
            { "m_audioClip",  "clip" },
            { "m_Avatar",     "avatar" },
            { "m_Controller", "runtimeAnimatorController" },
            { "m_Shader",     "shader" },
            { "m_Sprite",     "sprite" },
            { "OutputAudioMixerGroup",  "outputAudioMixerGroup" },
            { "m_OutputAudioMixerGroup", "outputAudioMixerGroup" },
            { "m_TargetTexture", "targetTexture" },
        };

        private static string ToRuntimeMemberPath(string unityPath, string componentType)
        {
            var canonical = unityPath.Replace(".Array.data[", "[");

            const string MeshKey = "m_Mesh";
            if (canonical == MeshKey || canonical.StartsWith(MeshKey + "[", StringComparison.Ordinal) || canonical.StartsWith(MeshKey + ".", StringComparison.Ordinal))
            {
                var suffix = canonical.Substring(MeshKey.Length);

                if (!string.IsNullOrEmpty(componentType) && componentType.StartsWith("UnityEngine.ParticleSystemRenderer", StringComparison.Ordinal))
                {
                    return "mesh" + suffix;
                }

                if (!string.IsNullOrEmpty(componentType) && (componentType.StartsWith("UnityEngine.MeshFilter", StringComparison.Ordinal) || componentType.StartsWith("UnityEngine.SkinnedMeshRenderer", StringComparison.Ordinal) || componentType.StartsWith("UnityEngine.MeshCollider", StringComparison.Ordinal)))
                {
                    return "sharedMesh" + suffix;
                }

                return canonical;
            }

            if (RuntimeMemberPathRemap.TryGetValue(canonical, out var mappedExact))
                return mappedExact;

            foreach (var kvp in RuntimeMemberPathRemap)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (canonical.StartsWith(key + "[", StringComparison.Ordinal) || canonical.StartsWith(key + ".", StringComparison.Ordinal))
                {
                    return value + canonical.Substring(key.Length);
                }
            }

            return canonical;
        }

        private static HashSet<string> GetIncludedAssetPaths(string[] modAssetPaths)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dependency in AssetDatabase.GetDependencies(modAssetPaths, true))
            {
                if (BlueprinterAssets.IsStaleGameAssetPath(dependency))
                {
                    Debug.LogError($"[Blueprinter] Replace stale game asset '{dependency}' before building");
                    return null;
                }

                if (string.IsNullOrEmpty(dependency) || BlueprinterAssets.IsCodeOrAssemblyFile(dependency) || BlueprinterAssets.IsModInfoFile(dependency) || BlueprinterAssets.IsGameAssetPath(dependency))
                {
                    continue;
                }

                result.Add(dependency);
            }

            return result;
        }

        private static void ScanReferences(SerializedObject serializedObject, string locationId, AssetRef modAssetRef, string hierarchyPath, string componentType, int componentIndex, ManifestBuildContext context)
        {
            var property = serializedObject.GetIterator();
            var normalizedHierarchy = string.IsNullOrEmpty(hierarchyPath) ? null : hierarchyPath;

            while (property.NextVisible(true))
            {
                if (TryReadAddressableGuid(property, out var guid))
                {
                    AddAddressableOverride(context, guid, modAssetRef);
                    continue;
                }

                if (property.propertyType == SerializedPropertyType.Integer && IsUrpRendererIndexProperty(property, componentType) && context.UrpRendererAssetsByIndex.TryGetValue(property.intValue, out var rendererAsset))
                {
                    var patch = GetOrCreatePatch(context, rendererAsset);
                    patch.PatchLocations.Add(new LocationRef
                    {
                        id = locationId,
                        asset = modAssetRef,
                        hierarchyPath = normalizedHierarchy,
                        componentType = componentType,
                        componentIndex = componentIndex,
                        memberPath = ToRuntimeMemberPath(property.propertyPath, componentType)
                    });

                    continue;
                }

                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                var referencedObject = property.objectReferenceValue;
                if (referencedObject == null || !TryGetGameAssetInfo(referencedObject, context, out var gameAssetInfo))
                    continue;

                var memberPath = ToRuntimeMemberPath(property.propertyPath, componentType);
                if (referencedObject is UnityEngine.Audio.AudioMixerGroup && string.Equals(gameAssetInfo.Location.asset.type, BlueprinterAssets.GetRuntimeTypeName(typeof(UnityEngine.Audio.AudioMixer)), StringComparison.Ordinal))
                {
                    memberPath += "::" + referencedObject.name;
                }

                var assetPatch = GetOrCreatePatch(context, gameAssetInfo);
                assetPatch.PatchLocations.Add(new LocationRef
                {
                    id = locationId,
                    asset = modAssetRef,
                    hierarchyPath = normalizedHierarchy,
                    componentType = componentType,
                    componentIndex = componentIndex,
                    memberPath = memberPath
                });
            }
        }

        private static bool IsUrpRendererIndexProperty(SerializedProperty property, string componentType)
        {
            const string ownerType = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";
            return property.propertyPath == "m_RendererIndex" && !string.IsNullOrEmpty(componentType) && (string.Equals(componentType, ownerType, StringComparison.Ordinal) || componentType.StartsWith(ownerType + ",", StringComparison.Ordinal));
        }

        private static bool TryReadAddressableGuid(SerializedProperty property, out string guid)
        {
            guid = null;

            if (property.propertyType != SerializedPropertyType.Generic || string.IsNullOrEmpty(property.type) || !property.type.StartsWith("AssetReference", StringComparison.Ordinal))
            {
                return false;
            }

            var guidProperty = property.FindPropertyRelative("m_AssetGUID");
            if (guidProperty == null || string.IsNullOrEmpty(guidProperty.stringValue))
                return false;

            guid = guidProperty.stringValue;
            return true;
        }

        private static void AddAddressableOverride(ManifestBuildContext context, string assetGuid, AssetRef sourceAssetRef)
        {
            var sourceName = sourceAssetRef.locator;
            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError($"[Blueprinter] Missing addressable '{assetGuid}' from '{sourceName}'");
                context.HasErrors = true;
                return;
            }

            if (!context.IncludedAssetPaths.Contains(assetPath))
            {
                Debug.LogError($"[Blueprinter] Addressable outside mod '{assetPath}' from '{sourceName}'");
                context.HasErrors = true;
                return;
            }

            var targetAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (targetAsset == null)
            {
                Debug.LogError($"[Blueprinter] Failed to load addressable '{assetPath}' from '{sourceName}'");
                context.HasErrors = true;
                return;
            }

            foreach (var entry in context.Manifest.Addressables)
            {
                if (string.Equals(entry.guid, assetGuid, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            context.Manifest.Addressables.Add(new AddressableOverride
            {
                guid = assetGuid,
                subObjectName = string.Empty,
                subObjectType = string.Empty,
                BundleAsset = BlueprinterAssets.CreateAssetRef(targetAsset)
            });
        }
    }
}
