using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Blueprinter
{
    public static class GameAssemblies
    {
        public const string GeneratedPackagePath = "Packages/nuclearoption";
        private const string LegacyGeneratedPackagePath = "Packages/NuclearOption";
        private const string GameName = "NuclearOption";
        private const string GameVersionFileName = "game-version.txt";

        public static readonly string[] ScriptAssemblies =
        {
            "Assembly-CSharp.dll",
            "Assembly-CSharp-firstpass.dll"
        };

        public static bool IsInstalled => File.Exists(BlueprinterAssets.ToAbsolutePath(GeneratedPackagePath + "/package.json")) && File.Exists(BlueprinterAssets.ToAbsolutePath(GeneratedPackagePath + "/Assembly-CSharp.dll"));

        public static string InstalledGameVersion
        {
            get
            {
                var path = BlueprinterAssets.ToAbsolutePath(GeneratedPackagePath + "/" + GameVersionFileName);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
            }
        }

        public static bool TryGetManagedFolder(string gameExecutable, out string managedFolder)
        {
            managedFolder = null;
            if (string.IsNullOrWhiteSpace(gameExecutable) || !File.Exists(gameExecutable))
                return false;

            var gameRoot = Path.GetDirectoryName(gameExecutable);
            if (string.IsNullOrEmpty(gameRoot))
                return false;

            managedFolder = Path.Combine(gameRoot, "NuclearOption_Data", "Managed");
            if (!Directory.Exists(managedFolder))
                return false;

            foreach (var fileName in ScriptAssemblies)
            {
                if (!File.Exists(Path.Combine(managedFolder, fileName)))
                    return false;
            }

            return true;
        }

        public static void Import(string managedFolder, string gameVersion)
        {
            var targetPackagePath = BlueprinterAssets.ToAbsolutePath(GeneratedPackagePath);
            var legacyPackagePath = BlueprinterAssets.ToAbsolutePath(LegacyGeneratedPackagePath);
            var providedAssemblies = GetProvidedAssemblyNames();

            AssetDatabase.DisallowAutoRefresh();
            try
            {
                if (Directory.Exists(targetPackagePath))
                    Directory.Delete(targetPackagePath, true);
                if (!string.Equals(legacyPackagePath, targetPackagePath, StringComparison.Ordinal) && Directory.Exists(legacyPackagePath))
                    Directory.Delete(legacyPackagePath, true);
                Directory.CreateDirectory(targetPackagePath);

                foreach (var fileName in ScriptAssemblies)
                {
                    CopyAssembly(Path.Combine(managedFolder, fileName), targetPackagePath);
                }

                foreach (var sourcePath in Directory.GetFiles(managedFolder, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var fileName = Path.GetFileName(sourcePath);
                    if (Array.Exists(ScriptAssemblies, name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (IsUnityReservedAssembly(fileName) || providedAssemblies.Contains(Path.GetFileNameWithoutExtension(fileName)))
                    {
                        continue;
                    }

                    CopyAssembly(sourcePath, targetPackagePath);
                }

                File.WriteAllText(Path.Combine(targetPackagePath, "package.json"), BuildPackageJson(gameVersion));
                File.WriteAllText(Path.Combine(targetPackagePath, GameVersionFileName), gameVersion);

                GameAssemblySync.Synchronize();
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Client.Resolve();
            CompilationPipeline.RequestScriptCompilation();
            Debug.Log("[Blueprinter] Imported game assemblies");
        }

        private static bool IsUnityReservedAssembly(string fileName)
        {
            return fileName.StartsWith("UnityEngine.", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("UnityEditor.", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "mscorlib.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(fileName, "netstandard.dll", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> GetProvidedAssemblyNames()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in CompilationPipeline.GetAssemblies())
                result.Add(assembly.name);

            var apiLevel = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup);
            foreach (var directory in CompilationPipeline.GetSystemAssemblyDirectories(apiLevel))
            {
                foreach (var dllPath in Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
                    result.Add(Path.GetFileNameWithoutExtension(dllPath));
            }

            foreach (var precompiledName in CompilationPipeline.GetPrecompiledAssemblyNames())
            {
                var path = CompilationPipeline.GetPrecompiledAssemblyPathFromAssemblyName(precompiledName);
                if (string.IsNullOrEmpty(path))
                    path = CompilationPipeline.GetPrecompiledAssemblyPathFromAssemblyName(Path.GetFileNameWithoutExtension(precompiledName));

                if (!IsGeneratedPackagePath(path))
                    result.Add(Path.GetFileNameWithoutExtension(precompiledName));
            }

            return result;
        }

        private static bool IsGeneratedPackagePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var absolutePath = (Path.IsPathRooted(path) ? Path.GetFullPath(path) : BlueprinterAssets.ToAbsolutePath(path)).Replace('\\', '/');
            var comparison = Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var generatedTarget = BlueprinterAssets.ToAbsolutePath(GeneratedPackagePath).Replace('\\', '/').TrimEnd('/') + "/";
            var legacyTarget = BlueprinterAssets.ToAbsolutePath(LegacyGeneratedPackagePath).Replace('\\', '/').TrimEnd('/') + "/";
            return absolutePath.StartsWith(generatedTarget, comparison) || absolutePath.StartsWith(legacyTarget, comparison);
        }

        private static void CopyAssembly(string sourcePath, string destinationFolder)
        {
            var fileName = Path.GetFileName(sourcePath);
            var destinationPath = Path.Combine(destinationFolder, fileName);
            File.Copy(sourcePath, destinationPath, true);
            File.WriteAllText(destinationPath + ".meta", BuildAssemblyMeta(fileName));
        }

        private static string BuildAssemblyMeta(string fileName)
        {
            return
                "fileFormatVersion: 2\n" +
                $"guid: {GetAssemblyGuid(fileName)}\n" +
                "PluginImporter:\n" +
                "  externalObjects: {}\n" +
                "  serializedVersion: 2\n" +
                "  iconMap: {}\n" +
                "  executionOrder: {}\n" +
                "  defineConstraints: []\n" +
                "  isPreloaded: 0\n" +
                "  isOverridable: 0\n" +
                "  isExplicitlyReferenced: 1\n" +
                "  validateReferences: 0\n" +
                "  platformData:\n" +
                "  - first:\n" +
                "      '': Any\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        Exclude Editor: 0\n" +
                "        Exclude Linux: 0\n" +
                "        Exclude Linux64: 0\n" +
                "        Exclude LinuxUniversal: 0\n" +
                "        Exclude OSXUniversal: 0\n" +
                "        Exclude Win: 0\n" +
                "        Exclude Win64: 0\n" +
                "  - first:\n" +
                "      Any: \n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings: {}\n" +
                "  - first:\n" +
                "      Editor: Editor\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "        DefaultValueInitialized: true\n" +
                "        OS: AnyOS\n" +
                "  - first:\n" +
                "      Facebook: Win\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  - first:\n" +
                "      Facebook: Win64\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  - first:\n" +
                "      Standalone: Linux\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: x86\n" +
                "  - first:\n" +
                "      Standalone: Linux64\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: x86_64\n" +
                "  - first:\n" +
                "      Standalone: LinuxUniversal\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings: {}\n" +
                "  - first:\n" +
                "      Standalone: OSXUniversal\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  - first:\n" +
                "      Standalone: Win\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  - first:\n" +
                "      Standalone: Win64\n" +
                "    second:\n" +
                "      enabled: 1\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  - first:\n" +
                "      Windows Store Apps: WindowsStoreApps\n" +
                "    second:\n" +
                "      enabled: 0\n" +
                "      settings:\n" +
                "        CPU: AnyCPU\n" +
                "  userData: \n" +
                "  assetBundleName: \n" +
                "  assetBundleVariant: \n";
        }

        private static string GetAssemblyGuid(string fileName)
        {
            var shortName = Path.GetFileNameWithoutExtension(fileName);
            using (var md5 = MD5.Create())
                return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes(shortName))).ToString("N");
        }

        private static string BuildPackageJson(string gameVersion)
        {
            return
                "{\n" +
                $"  \"name\": \"{GameName.ToLowerInvariant()}\",\n" +
                $"  \"version\": \"{gameVersion}\",\n" +
                $"  \"displayName\": \"{GameName} Assemblies\",\n" +
                "  \"description\": \"Generated locally by Blueprinter from the installed game.\",\n" +
                "  \"unity\": \"2022.3\"\n" +
                "}\n";
        }
    }
}
