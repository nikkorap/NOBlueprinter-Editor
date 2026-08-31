using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    public class OpReferenceIndex : ScriptableObject
    {
        private const string AssetPath = GeneratedFolder + "/OpReferenceIndex.asset";

        [Serializable]
        public class AircraftEntry
        {
            public string jsonKey;
            public List<string> hardpointNames = new List<string>();
        }

        [Serializable]
        public class HangarUnitEntry
        {
            public string jsonKey;
            public List<string> hangarNames = new List<string>();
        }

        public List<AircraftEntry> Aircraft = new List<AircraftEntry>();
        public List<string> Weapons = new List<string>();
        public List<HangarUnitEntry> HangarUnits = new List<HangarUnitEntry>();

        public static OpReferenceIndex Load()
        {
            return AssetDatabase.LoadAssetAtPath<OpReferenceIndex>(AssetPath);
        }

        public static void Refresh()
        {
            var newAircraft = new List<AircraftEntry>();
            var newWeapons = new List<string>();
            var newHangarUnits = new List<HangarUnitEntry>();

            if (!ScanAssets(newAircraft, newWeapons, newHangarUnits))
                return;

            newAircraft.Sort((a, b) => string.CompareOrdinal(a.jsonKey, b.jsonKey));
            newWeapons.Sort(StringComparer.Ordinal);
            newHangarUnits.Sort((a, b) => string.CompareOrdinal(a.jsonKey, b.jsonKey));

            BlueprinterAssets.EnsureFolder(GeneratedFolder);
            var index = Load();
            if (index == null)
            {
                index = CreateInstance<OpReferenceIndex>();
                AssetDatabase.CreateAsset(index, AssetPath);
            }

            index.Aircraft = newAircraft;
            index.Weapons = newWeapons;
            index.HangarUnits = newHangarUnits;
            EditorUtility.SetDirty(index);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Blueprinter] Indexed {newAircraft.Count} aircraft {newWeapons.Count} weapons {newHangarUnits.Count} hangar units");
        }

        private static bool ScanAssets(List<AircraftEntry> aircraft, List<string> weapons, List<HangarUnitEntry> hangarUnits)
        {
            var roots = new List<string>();
            if (AssetDatabase.IsValidFolder(GameAssetRootFolder))
                roots.Add(GameAssetRootFolder);
            if (AssetDatabase.IsValidFolder(ModRootFolder))
                roots.Add(ModRootFolder);
            if (roots.Count == 0)
            {
                Debug.LogError("[Blueprinter] Nothing available to index");
                return false;
            }

            var unitKeys = new HashSet<string>(StringComparer.Ordinal);
            var weaponKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:AircraftDefinition t:BuildingDefinition t:ShipDefinition t:WeaponMount", roots.ToArray()))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (BlueprinterAssets.IsStaleGameAssetPath(path))
                    continue;

                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null)
                    continue;

                var assetType = asset.GetType().Name;
                var serialized = new SerializedObject(asset);
                var jsonKey = serialized.FindProperty("jsonKey")?.stringValue;
                if (string.IsNullOrWhiteSpace(jsonKey))
                    continue;

                var jsonKeys = assetType == "WeaponMount" ? weaponKeys : unitKeys;
                if (!jsonKeys.Add(jsonKey))
                {
                    Debug.LogError($"[Blueprinter] Duplicate jsonKey '{jsonKey}'");
                    return false;
                }

                if (assetType == "WeaponMount")
                {
                    weapons.Add(jsonKey);
                    continue;
                }

                AircraftEntry aircraftEntry = null;
                if (assetType == "AircraftDefinition")
                {
                    aircraftEntry = new AircraftEntry { jsonKey = jsonKey };
                    aircraft.Add(aircraftEntry);
                }

                var prefab = serialized.FindProperty("unitPrefab")?.objectReferenceValue as GameObject;
                if (prefab == null)
                    continue;

                var foundWeaponManager = false;
                var hangarNames = new List<string>();
                foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (component == null)
                        continue;

                    var typeName = component.GetType().Name;
                    if (aircraftEntry != null && !foundWeaponManager && typeName == "WeaponManager")
                    {
                        AddHardpoints(component, aircraftEntry);
                        foundWeaponManager = true;
                    }

                    if (typeName != "Hangar")
                        continue;

                    var hangarName = component.name;
                    if (hangarName.EndsWith(PlaceholderSuffix, StringComparison.Ordinal))
                        hangarName = hangarName.Substring(0, hangarName.Length - PlaceholderSuffix.Length);

                    if (hangarNames.Contains(hangarName))
                    {
                        Debug.LogError($"[Blueprinter] Duplicate hangar '{hangarName}' on '{jsonKey}'");
                        return false;
                    }

                    hangarNames.Add(hangarName);
                }

                if (hangarNames.Count > 0)
                {
                    hangarNames.Sort(StringComparer.Ordinal);
                    hangarUnits.Add(new HangarUnitEntry
                    {
                        jsonKey = jsonKey,
                        hangarNames = hangarNames
                    });
                }
            }

            return true;
        }

        private static void AddHardpoints(UnityEngine.Object weaponManager, AircraftEntry aircraft)
        {
            var property = new SerializedObject(weaponManager).FindProperty("hardpointSets");
            if (property == null || !property.isArray)
                return;

            for (var i = 0; i < property.arraySize; i++)
            {
                var name = property.GetArrayElementAtIndex(i).FindPropertyRelative("name")?.stringValue;
                aircraft.hardpointNames.Add(name ?? string.Empty);
            }
        }

        public static void DrawEntries<T>(string label, SerializedProperty property, IList<T> entries, Func<T, string> getValue)
        {
            if (entries == null || entries.Count == 0)
                return;

            var options = new string[entries.Count + 1];
            options[0] = "None";
            var selected = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                var value = getValue(entries[i]);
                options[i + 1] = value;
                if (value == property.stringValue)
                    selected = i + 1;
            }

            var next = EditorGUILayout.Popup(label, selected, options);
            if (next != selected)
                property.stringValue = next == 0 ? string.Empty : getValue(entries[next - 1]);
        }
    }
}
