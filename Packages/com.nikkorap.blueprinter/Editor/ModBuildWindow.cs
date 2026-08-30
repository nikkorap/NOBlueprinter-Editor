using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public class ModBuildWindow : EditorWindow
    {
        private const string BuildOutputPrefsKey = "Blueprinter_CopyTargetFolder";
        private const string SelectedModPrefsKey = "Blueprinter_SelectedBundle";

        private string[] _modNames = Array.Empty<string>();
        private int _modIndex;
        private string _version = ModInfo.DefaultVersion;
        private string _displayName = string.Empty;
        private string _outputFolder = string.Empty;

        [MenuItem("Blueprinter/Mod Builder", false, 41)]
        private static void Open()
        {
            var window = GetWindow<ModBuildWindow>("Mod Builder");
            window.minSize = new Vector2(300f, 150f);
        }

        private void OnEnable() => LoadAndRefresh();
        private void OnFocus() => LoadAndRefresh();

        private void LoadAndRefresh()
        {
            _outputFolder = EditorPrefs.GetString(BuildOutputPrefsKey, string.Empty);

            _modNames = BlueprinterAssets.GetModNames();
            if (_modNames.Length > 0)
            {
                var selectedMod = EditorPrefs.GetString(SelectedModPrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(selectedMod))
                {
                    var index = Array.IndexOf(_modNames, selectedMod);
                    if (index >= 0)
                        _modIndex = index;
                }

                _modIndex = Mathf.Clamp(_modIndex, 0, _modNames.Length - 1);
            }

            LoadCurrentModInfo();
        }

        private string CurrentModName => _modNames.Length == 0 ? null : _modNames[_modIndex];

        private void LoadCurrentModInfo()
        {
            var modName = CurrentModName;
            if (string.IsNullOrEmpty(modName))
                return;

            var info = ModInfo.Load(modName);
            _version = info.version;
            _displayName = info.displayName;
        }

        private void SaveCurrentModInfo()
        {
            var modName = CurrentModName;
            if (string.IsNullOrEmpty(modName))
                return;

            ModInfo.Save(modName, _displayName, _version);
        }

        private void OnGUI()
        {
            if (_modNames.Length == 0)
            {
                EditorGUILayout.LabelField("No mod folders found. Create one directly under '" + ModRootFolder + "'.");
                return;
            }

            EditorGUI.BeginChangeCheck();
            var newIndex = EditorGUILayout.Popup("Select Mod", _modIndex, _modNames);
            if (EditorGUI.EndChangeCheck())
            {
                _modIndex = Mathf.Clamp(newIndex, 0, _modNames.Length - 1);
                EditorPrefs.SetString(SelectedModPrefsKey, CurrentModName ?? string.Empty);
                LoadCurrentModInfo();
            }

            EditorGUILayout.Space();

            var modInfoChanged = false;

            EditorGUI.BeginChangeCheck();
            _displayName = EditorGUILayout.TextField("Display name", _displayName);
            if (EditorGUI.EndChangeCheck())
                modInfoChanged = true;

            var previousVersion = _version;
            var validVersion = ModBuilder.ValidateVersion(EditorGUILayout.TextField("Mod version", _version), out _version);
            if (_version != previousVersion)
                modInfoChanged = true;
            if (modInfoChanged && validVersion)
                SaveCurrentModInfo();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Build output folder");
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var newOutputFolder = EditorGUILayout.TextField(_outputFolder);
            if (EditorGUI.EndChangeCheck())
            {
                _outputFolder = newOutputFolder;
                EditorPrefs.SetString(BuildOutputPrefsKey, _outputFolder ?? string.Empty);
            }

            if (GUILayout.Button("Browse", GUILayout.Width(80f)))
            {
                var picked = EditorUtility.OpenFolderPanel("Select Build Output Folder", _outputFolder, string.Empty);
                if (!string.IsNullOrEmpty(picked))
                {
                    _outputFolder = picked;
                    EditorPrefs.SetString(BuildOutputPrefsKey, _outputFolder ?? string.Empty);
                }
            }

            EditorGUILayout.EndHorizontal();

            var outputInProtectedFolder = !string.IsNullOrEmpty(_outputFolder) && IsProtectedOutputPath(_outputFolder);
            if (outputInProtectedFolder)
                EditorGUILayout.LabelField("Select a folder outside Assets/Blueprinter/Mods and Assets/Blueprinter/_donotship.");

            EditorGUILayout.Space();

            var invalidOutput = string.IsNullOrEmpty(_outputFolder) || outputInProtectedFolder;
            EditorGUI.BeginDisabledGroup(invalidOutput || !validVersion);
            if (GUILayout.Button("Build Mod", GUILayout.Height(28f)) &&
                EditorUtility.DisplayDialog("Build Mod", "Build the mod?", "Build", "Cancel"))
                ModBuilder.Build(CurrentModName, _displayName, _version, _outputFolder);
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Export Source ZIP", GUILayout.Height(28f)))
                ExportSource(CurrentModName);
        }

        private static bool IsProtectedOutputPath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return BlueprinterAssets.IsPathUnder(fullPath, BlueprinterAssets.ToAbsolutePath(ModRootFolder)) ||
                   BlueprinterAssets.IsPathUnder(fullPath, BlueprinterAssets.ToAbsolutePath(GameAssetRootFolder));
        }

        private void ExportSource(string modName)
        {
            var directory = !string.IsNullOrEmpty(_outputFolder) && Directory.Exists(_outputFolder) ? _outputFolder : Path.GetDirectoryName(Application.dataPath);

            var baseName = BlueprinterAssets.SanitizeFileName(string.IsNullOrWhiteSpace(_displayName) ? modName : _displayName);
            var version = BlueprinterAssets.SanitizeFileName(_version);
            var defaultName = string.IsNullOrEmpty(version) ? baseName + SourceArchive.SourceZipSuffix : baseName + "_" + version + SourceArchive.SourceZipSuffix;

            var outputPath = EditorUtility.SaveFilePanel("Export Source ZIP", directory, defaultName, "zip");

            if (string.IsNullOrEmpty(outputPath))
                return;

            if (IsProtectedOutputPath(outputPath))
            {
                Debug.LogError("[Blueprinter] Source ZIP output must be outside Assets/Blueprinter/Mods and Assets/Blueprinter/_donotship");
                return;
            }

            if (!outputPath.EndsWith(SourceArchive.SourceZipSuffix, StringComparison.OrdinalIgnoreCase))
            {
                var stem = outputPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? outputPath.Substring(0, outputPath.Length - 4) : outputPath;
                outputPath = stem + SourceArchive.SourceZipSuffix;
            }

            SourceExporter.ExportMod(modName, _displayName, _version, outputPath);
        }
    }
}
