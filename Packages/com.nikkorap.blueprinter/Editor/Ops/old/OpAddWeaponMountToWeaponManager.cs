using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    // [CreateAssetMenu(menuName = "Blueprinter/OpAddWeaponMountToWeaponManager", fileName = "OpAddWeaponMountToWeaponManager")]
    public class OpAddWeaponMountToWeaponManager : OpCore
    {
        [Tooltip("WeaponMount asset from this mod.")]
        public UnityEngine.Object weaponMount;

        [Serializable]
        public class WeaponManagerEntry
        {
            public string weaponManagerId;
            public bool[] hardpointSetSelections = Array.Empty<bool>();
        }

        public WeaponManagerEntry[] weaponManagers = Array.Empty<WeaponManagerEntry>();

        [Serializable]
        private class PayloadDto
        {
            public AssetRef bundleAsset;
            public WeaponManagerTargetDto[] weaponManagers;
        }

        [Serializable]
        private class WeaponManagerTargetDto
        {
            public AssetRef gameAsset;
            public int[] hardpointSetIndices;
        }

        public override string opId => "OpAddWeaponMountToWeaponManager";

        public override object BuildPayload()
        {
            var modAsset = RequireModAsset(weaponMount, nameof(weaponMount), "WeaponMount");
            if (modAsset == null)
                return null;

            var targets = new List<WeaponManagerTargetDto>();
            foreach (var entry in weaponManagers ?? Array.Empty<WeaponManagerEntry>())
            {
                if (entry == null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.weaponManagerId))
                {
                    Debug.LogError($"[Blueprinter] [{opId}] Weapon manager ID missing");
                    return null;
                }

                targets.Add(new WeaponManagerTargetDto
                {
                    gameAsset = new AssetRef
                    {
                        locator = "BaseGame",
                        name = entry.weaponManagerId,
                        type = "WeaponManager, Assembly-CSharp"
                    },
                    hardpointSetIndices = BuildHardpointIndices(entry.hardpointSetSelections)
                });
            }

            return new PayloadDto
            {
                bundleAsset = modAsset,
                weaponManagers = targets.ToArray()
            };
        }

        private static int[] BuildHardpointIndices(bool[] selections)
        {
            if (selections == null)
                return Array.Empty<int>();

            var result = new List<int>();
            for (var i = 0; i < selections.Length; i++)
            {
                if (selections[i])
                    result.Add(i);
            }

            return result.ToArray();
        }

        [CustomEditor(typeof(OpAddWeaponMountToWeaponManager))]
        internal sealed class OpAddWeaponMountToWeaponManagerEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                EditorGUILayout.HelpBox("This is a deprecated op, you should probably replace this with OpAddWeaponToHardpoint", MessageType.Warning);
                DrawDefaultInspector();
            }
        }
    }
}
