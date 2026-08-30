using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    [CreateAssetMenu(menuName = "Blueprinter/OpAddAircraftToHangars", fileName = "OpAddAircraftToHangars")]
    public class OpAddAircraftToHangars : OpCore
    {
        public string aircraftJsonKey;
        public List<HangarTarget> hangars = new List<HangarTarget>();

        [Serializable]
        public class HangarTarget
        {
            public string hangarUnitJsonKey;
            public List<string> hangarNames = new List<string>();
        }

        public override string opId => "OpAddAircraftToHangars";
        public override bool IsEmpty => hangars == null || hangars.Count == 0;

        public override object BuildPayload()
        {
            if (!RequireValue(aircraftJsonKey, nameof(aircraftJsonKey)))
                return null;

            if (hangars == null || hangars.Count == 0)
            {
                Debug.LogError($"[Blueprinter] [{opId}] No hangar targets");
                return null;
            }

            foreach (var target in hangars)
            {
                if (target == null || !RequireValue(target.hangarUnitJsonKey, nameof(HangarTarget.hangarUnitJsonKey)))
                    return null;

                if (target.hangarNames == null || target.hangarNames.Count == 0)
                {
                    Debug.LogError($"[Blueprinter] [{opId}] No hangars for '{target.hangarUnitJsonKey}'");
                    return null;
                }

                if (target.hangarNames.Exists(string.IsNullOrWhiteSpace))
                {
                    Debug.LogError($"[Blueprinter] [{opId}] Invalid hangar for '{target.hangarUnitJsonKey}'");
                    return null;
                }
            }

            return this;
        }
    }

    [CustomEditor(typeof(OpAddAircraftToHangars))]
    public class OpAddAircraftToHangarsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var aircraft = serializedObject.FindProperty("aircraftJsonKey");
            var hangars = serializedObject.FindProperty("hangars");
            var index = OpReferenceIndex.Load();

            OpReferenceIndex.DrawEntries("Known Aircraft", aircraft, index?.Aircraft, entry => entry.jsonKey);
            EditorGUILayout.PropertyField(aircraft, new GUIContent("Aircraft JSON Key"));
            EditorGUILayout.Space();

            var removeIndex = -1;
            for (var hangarIndex = 0; hangarIndex < hangars.arraySize; hangarIndex++)
            {
                var target = hangars.GetArrayElementAtIndex(hangarIndex);
                var hangarUnitKey = target.FindPropertyRelative("hangarUnitJsonKey");
                var hangarNames = target.FindPropertyRelative("hangarNames");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Hangar Unit {hangarIndex + 1}", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                    removeIndex = hangarIndex;
                EditorGUILayout.EndHorizontal();

                OpReferenceIndex.DrawEntries("Known Hangar Unit", hangarUnitKey, index?.HangarUnits, entry => entry.jsonKey);
                EditorGUILayout.PropertyField(hangarUnitKey, new GUIContent("Hangar Unit JSON Key"));

                var knownHangarNames = index?.HangarUnits.Find(entry => entry.jsonKey == hangarUnitKey.stringValue)?.hangarNames;
                if (knownHangarNames != null && knownHangarNames.Count > 0)
                {
                    EditorGUILayout.LabelField("Known Hangars", EditorStyles.boldLabel);
                    foreach (var hangarName in knownHangarNames)
                    {
                        var selectedIndex = -1;
                        for (var i = 0; i < hangarNames.arraySize; i++)
                        {
                            if (hangarNames.GetArrayElementAtIndex(i).stringValue != hangarName)
                                continue;

                            selectedIndex = i;
                            break;
                        }

                        var selected = selectedIndex >= 0;
                        var next = EditorGUILayout.ToggleLeft(hangarName, selected);
                        if (next == selected)
                            continue;

                        if (next)
                            hangarNames.GetArrayElementAtIndex(hangarNames.arraySize++).stringValue = hangarName;
                        else
                            hangarNames.DeleteArrayElementAtIndex(selectedIndex);
                    }
                }

                EditorGUILayout.PropertyField(hangarNames, new GUIContent("Hangar Names"), true);
                EditorGUILayout.Space();
            }

            if (removeIndex >= 0)
                hangars.DeleteArrayElementAtIndex(removeIndex);

            if (GUILayout.Button("Add Hangar Unit"))
            {
                var targetIndex = hangars.arraySize;
                hangars.arraySize++;
                var target = hangars.GetArrayElementAtIndex(targetIndex);
                target.FindPropertyRelative("hangarUnitJsonKey").stringValue = string.Empty;
                target.FindPropertyRelative("hangarNames").arraySize = 0;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
