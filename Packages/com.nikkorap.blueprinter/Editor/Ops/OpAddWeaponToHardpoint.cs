using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    [CreateAssetMenu(menuName = "Blueprinter/OpAddWeaponToHardpoint", fileName = "OpAddWeaponToHardpoint")]
    public class OpAddWeaponToHardpoint : OpCore
    {
        public string weaponJsonKey;
        public List<AircraftTarget> aircraft = new List<AircraftTarget>();

        [Serializable]
        public class AircraftTarget
        {
            public string aircraftJsonKey;
            public List<int> hardpointIndices = new List<int>();
        }

        public override string opId => "OpAddWeaponToHardpoint";
        public override bool IsEmpty => aircraft == null || aircraft.Count == 0;

        public override object BuildPayload()
        {
            if (!RequireValue(weaponJsonKey, nameof(weaponJsonKey)))
                return null;

            if (aircraft == null || aircraft.Count == 0)
            {
                Debug.LogError($"[Blueprinter] [{opId}] No aircraft targets");
                return null;
            }

            foreach (var target in aircraft)
            {
                if (target == null || !RequireValue(target.aircraftJsonKey, nameof(AircraftTarget.aircraftJsonKey)))
                    return null;

                if (target.hardpointIndices == null || target.hardpointIndices.Count == 0)
                {
                    Debug.LogError($"[Blueprinter] [{opId}] No hardpoints for '{target.aircraftJsonKey}'");
                    return null;
                }

                if (target.hardpointIndices.Exists(index => index < 0))
                {
                    Debug.LogError($"[Blueprinter] [{opId}] Invalid hardpoint for '{target.aircraftJsonKey}'");
                    return null;
                }
            }

            return this;
        }
    }

    [CustomEditor(typeof(OpAddWeaponToHardpoint))]
    public class OpAddWeaponToHardpointEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var weapon = serializedObject.FindProperty("weaponJsonKey");
            var aircraft = serializedObject.FindProperty("aircraft");
            var index = OpReferenceIndex.Load();

            OpReferenceIndex.DrawEntries("Known Weapon", weapon, index?.Weapons, value => value);
            EditorGUILayout.PropertyField(weapon, new GUIContent("Weapon JSON Key"));
            EditorGUILayout.Space();

            var removeIndex = -1;
            for (var aircraftIndex = 0; aircraftIndex < aircraft.arraySize; aircraftIndex++)
            {
                var target = aircraft.GetArrayElementAtIndex(aircraftIndex);
                var aircraftKey = target.FindPropertyRelative("aircraftJsonKey");
                var hardpointIndices = target.FindPropertyRelative("hardpointIndices");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Aircraft {aircraftIndex + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeIndex = aircraftIndex;
                EditorGUILayout.EndHorizontal();

                OpReferenceIndex.DrawEntries("Known Aircraft", aircraftKey, index?.Aircraft, entry => entry.jsonKey);
                EditorGUILayout.PropertyField(aircraftKey, new GUIContent("Aircraft JSON Key"));

                var hardpointNames = index?.Aircraft.Find(entry => entry.jsonKey == aircraftKey.stringValue)?.hardpointNames;
                if (hardpointNames != null && hardpointNames.Count > 0)
                {
                    EditorGUILayout.LabelField("Known Hardpoints", EditorStyles.boldLabel);
                    for (var hardpointIndex = 0; hardpointIndex < hardpointNames.Count; hardpointIndex++)
                    {
                        var selectedIndex = -1;
                        for (var i = 0; i < hardpointIndices.arraySize; i++)
                        {
                            if (hardpointIndices.GetArrayElementAtIndex(i).intValue != hardpointIndex)
                                continue;

                            selectedIndex = i;
                            break;
                        }

                        var selected = selectedIndex >= 0;
                        var next = EditorGUILayout.ToggleLeft(hardpointNames[hardpointIndex], selected);
                        if (next == selected)
                            continue;

                        if (next)
                            hardpointIndices.GetArrayElementAtIndex(hardpointIndices.arraySize++).intValue = hardpointIndex;
                        else
                            hardpointIndices.DeleteArrayElementAtIndex(selectedIndex);

                        var sortedIndices = new List<int>();
                        for (var i = 0; i < hardpointIndices.arraySize; i++)
                            sortedIndices.Add(hardpointIndices.GetArrayElementAtIndex(i).intValue);
                        sortedIndices.Sort();
                        for (var i = 0; i < sortedIndices.Count; i++)
                            hardpointIndices.GetArrayElementAtIndex(i).intValue = sortedIndices[i];
                    }
                }

                EditorGUILayout.PropertyField(hardpointIndices, new GUIContent("Hardpoint Indices"), true);
                EditorGUILayout.Space();
            }

            if (removeIndex >= 0)
                aircraft.DeleteArrayElementAtIndex(removeIndex);

            if (GUILayout.Button("Add Aircraft"))
            {
                var targetIndex = aircraft.arraySize;
                aircraft.arraySize++;
                var target = aircraft.GetArrayElementAtIndex(targetIndex);
                target.FindPropertyRelative("aircraftJsonKey").stringValue = string.Empty;
                target.FindPropertyRelative("hardpointIndices").arraySize = 0;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
