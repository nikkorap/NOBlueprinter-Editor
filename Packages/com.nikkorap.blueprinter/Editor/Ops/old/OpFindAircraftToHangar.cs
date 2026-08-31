using System;
using UnityEditor;
using UnityEngine;

namespace Blueprinter.Editor.Ops
{
    // [CreateAssetMenu(menuName = "Blueprinter/OpFindAircraftToHangar", fileName = "OpFindAircraftToHangar")]
    public class OpFindAircraftToHangar : OpCore
    {
        public GameObject TargetUnitPrefab;
        public string TargetHangarObjectName;
        public string[] AircraftDefinitions = Array.Empty<string>();

        [Serializable]
        private class PayloadDto
        {
            public string HangarKey;
            public string[] AircraftNames;
        }

        public override string opId => "OpFindAircraftToHangar";

        public override object BuildPayload()
        {
            if (TargetUnitPrefab == null)
            {
                Debug.LogError($"[Blueprinter] [{opId}] Missing '{nameof(TargetUnitPrefab)}'");
                return null;
            }

            if (!RequireValue(TargetHangarObjectName, nameof(TargetHangarObjectName)))
                return null;

            return new PayloadDto
            {
                HangarKey = $"{TargetUnitPrefab.name}__{TargetHangarObjectName}",
                AircraftNames = AircraftDefinitions ?? Array.Empty<string>()
            };
        }

        [CustomEditor(typeof(OpFindAircraftToHangar))]
        public class OpFindAircraftToHangarEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                EditorGUILayout.HelpBox("This is a deprecated op, you should probably replace this with OpAddAircraftToHangars", MessageType.Warning);
                DrawDefaultInspector();
            }
        }
    }
}
