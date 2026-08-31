using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Blueprinter
{
    [InitializeOnLoad]
    public static class GameAssemblySync
    {
        static GameAssemblySync()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            EditorApplication.delayCall += Synchronize;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            var fileName = Path.GetFileName(assemblyPath);
            if (Array.Exists(GameAssemblies.ScriptAssemblies, name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)))
            {
                Synchronize();
            }
        }

        public static void Synchronize()
        {
            var scriptAssemblyRoot = BlueprinterAssets.ToAbsolutePath("Library/ScriptAssemblies");

            if (!GameAssemblies.IsInstalled)
            {
                foreach (var fileName in GameAssemblies.ScriptAssemblies)
                    ClearReadOnly(Path.Combine(scriptAssemblyRoot, fileName));

                return;
            }

            var packageRoot = BlueprinterAssets.ToAbsolutePath(GameAssemblies.GeneratedPackagePath);
            Directory.CreateDirectory(scriptAssemblyRoot);

            foreach (var fileName in GameAssemblies.ScriptAssemblies)
            {
                var source = Path.Combine(packageRoot, fileName);
                if (!File.Exists(source))
                    continue;

                var destination = Path.Combine(scriptAssemblyRoot, fileName);
                ClearReadOnly(destination);

                try
                {
                    if (File.Exists(destination))
                        FileUtil.ReplaceFile(source, destination);
                    else
                        File.Copy(source, destination, true);

                    File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Blueprinter] Failed to synchronize '{fileName}' {exception.Message}");
                }
            }
        }

        private static void ClearReadOnly(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch
            {
            }
        }
    }
}
