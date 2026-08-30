using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    public class SourceImportWindow : EditorWindow
    {
        private SourceImportSession _session;
        private List<SourceImportItem> _mappings;
        private Vector2 _scroll;

        public static void Open(SourceImportSession session)
        {
            var window = GetWindow<SourceImportWindow>(true, "Import Source ZIP", true);
            window._session = session;
            window._mappings = session.Mappings.Where(item => item.Replacement == null).ToList();
            window.minSize = new Vector2(500f, 250f);
            window.Show();
        }

        private void OnGUI()
        {
            if (_session?.Manifest == null)
            {
                Close();
                return;
            }

            EditorGUILayout.LabelField(_session.Manifest.displayName ?? _session.Manifest.modName, EditorStyles.boldLabel);

            var unmappedCount = _mappings.Count(item => item.Replacement == null);
            if (unmappedCount > 0)
            {
                var message = unmappedCount == 1 ? "1 reference will remain unresolved." : $"{unmappedCount} references will remain unresolved.";
                EditorGUILayout.LabelField(message);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var mapping in _mappings)
                DrawReference(mapping);
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Import", GUILayout.Height(28f)))
            {
                SourceImporter.ApplyImport(_session);
                Close();
            }
        }

        private void DrawReference(SourceImportItem mapping)
        {
            var reference = mapping.Reference;
            if (mapping.IsScript)
            {
                EditorGUILayout.LabelField($"{mapping.DisplayName} ({reference.assembly})", EditorStyles.boldLabel);
                var selected = (MonoScript)EditorGUILayout.ObjectField("Replacement", mapping.Replacement as MonoScript, typeof(MonoScript), false);
                if (selected != mapping.Replacement)
                {
                    if (selected == null || MonoImporter.GetAllRuntimeMonoScripts().Contains(selected))
                        mapping.Replacement = selected;
                    else
                        Debug.LogWarning($"[Blueprinter] Not a runtime MonoScript '{selected.name}'");
                }
            }
            else
            {
                EditorGUILayout.LabelField($"{mapping.DisplayName} ({reference.type ?? "<unknown type>"})", EditorStyles.boldLabel);
                var selected = EditorGUILayout.ObjectField("Replacement", mapping.Replacement, typeof(UnityEngine.Object), false);
                if (selected != mapping.Replacement)
                {
                    if (selected == null)
                    {
                        mapping.Replacement = null;
                    }
                    else if (!SourceArchive.IsExternalAssetPath(reference.path) && !BlueprinterAssets.IsGameAssetPath(AssetDatabase.GetAssetPath(selected)))
                    {
                        Debug.LogWarning("[Blueprinter] Game asset replacement must be inside _donotship");
                    }
                    else if (!string.IsNullOrEmpty(reference.type) && BlueprinterAssets.GetRuntimeTypeName(selected.GetType()) != reference.type)
                    {
                        Debug.LogWarning("[Blueprinter] Wrong replacement type");
                    }
                    else
                    {
                        mapping.Replacement = selected;
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }
    }
}
