using System.IO;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public class BlueprinterSetupWindow : EditorWindow
    {
        private const string AssetRipperAssetsPrefsKey = "Blueprinter_AssetRipperExportPath";
        private const string DisclaimerText = "Blueprinter is an unofficial modding tool. Nuclear Option does not currently provide official mod support, and compatibility with future game versions is not guaranteed.\n\n" + "Game updates can irrecoverably break Blueprinter projects and may require substantial rework. Source ZIPs are intended to make updating easier, but they cannot guarantee that a project can be recovered. Keep backups of your project.\n\n" + "Do not redistribute base game files\n\n" + "Files under _donotship and Packages/nuclearoption are copied or extracted from Nuclear Option solely for use while authoring with Blueprinter. Do not include them in released mods or share them outside your project.";

        private string _gameVersion = "0.34.2";
        private bool _disclaimerAccepted;

        [MenuItem("Blueprinter/Project Setup", false, 20)]
        private static void Open()
        {
            var window = GetWindow<BlueprinterSetupWindow>("Project Setup");
            window.minSize = new Vector2(300f, 150f);
            window._disclaimerAccepted = false;
            window.Focus();
        }

        private void OnEnable()
        {
            BlueprinterAssets.EnsureFolder(ModRootFolder);

            var installedVersion = GameAssemblies.InstalledGameVersion;
            if (!string.IsNullOrEmpty(installedVersion))
                _gameVersion = installedVersion;
        }

        private void OnGUI()
        {
            if (!_disclaimerAccepted)
            {
                DrawDisclaimer();
                return;
            }

            DrawGameAssemblies();
            EditorGUILayout.Space(12f);
            DrawGameAssets();
            EditorGUILayout.Space(12f);
            if (GUILayout.Button("4. Refresh Op References", GUILayout.Height(26f)))
                OpReferenceIndex.Refresh();

            EditorGUILayout.Space(18f);
            EditorGUI.BeginDisabledGroup(!AssetDatabase.IsValidFolder(GameAssetRootFolder));
            if (GUILayout.Button("5. Build _donotship", GUILayout.Height(28f)))
                ModBuilder.BuildGameAssets();
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(18f);
            var isLocked = GameAssetLock.IsLocked;
            if (GUILayout.Button(isLocked ? "Unlock _donotship" : "Lock _donotship", GUILayout.Height(26f)))
            {
                if (!isLocked || EditorUtility.DisplayDialog(
                        "Unlock _donotship",
                        "_donotship contains imported game assets used by Blueprinter. Changing, renaming, or moving these files can break references and requires rebuilding _donotship.\n\nUnlock _donotship anyway?",
                        "Unlock",
                        "Cancel"))
                    GameAssetLock.IsLocked = !isLocked;
            }
        }

        private void DrawDisclaimer()
        {
            EditorGUILayout.LabelField("Before using Blueprinter", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(DisclaimerText, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("I Understand", GUILayout.Height(28f)))
                _disclaimerAccepted = true;
        }

        private void DrawGameAssemblies()
        {
            EditorGUILayout.LabelField("1. Enter game version");
            var validGameVersion = ModBuilder.ValidateVersion(EditorGUILayout.TextField(_gameVersion), out _gameVersion);

            EditorGUI.BeginDisabledGroup(!validGameVersion);
            if (GUILayout.Button("2. Import Game Assemblies", GUILayout.Height(28f)))
            {
                var previousPath = EditorPrefs.GetString(GameExecutablePrefsKey, string.Empty);
                var picked = EditorUtility.OpenFilePanel("Select NuclearOption.exe", string.IsNullOrEmpty(previousPath) ? string.Empty : Path.GetDirectoryName(previousPath), "exe");

                if (!string.IsNullOrEmpty(picked))
                {
                    EditorPrefs.SetString(GameExecutablePrefsKey, picked);
                    if (GameAssemblies.TryGetManagedFolder(picked, out var managedFolder))
                        GameAssemblies.Import(managedFolder, _gameVersion);
                    else
                        EditorUtility.DisplayDialog("Import Game Assemblies", "Select NuclearOption.exe from a Nuclear Option installation.", "OK");
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawGameAssets()
        {
            EditorGUI.BeginDisabledGroup(!GameAssemblies.IsInstalled);
            if (GUILayout.Button("3. Import Game Assets", GUILayout.Height(28f)))
            {
                var previousPath = EditorPrefs.GetString(AssetRipperAssetsPrefsKey, string.Empty);
                var picked = EditorUtility.OpenFolderPanel("Select AssetRipper ExportedProject/Assets", previousPath, string.Empty);

                if (!string.IsNullOrEmpty(picked))
                {
                    EditorPrefs.SetString(AssetRipperAssetsPrefsKey, picked);
                    if (AssetRipperImporter.IsAssetsFolder(picked))
                        AssetRipperImporter.Import(picked);
                    else
                        EditorUtility.DisplayDialog("Import Game Assets", "Select the AssetRipper ExportedProject/Assets folder.", "OK");
                }
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
