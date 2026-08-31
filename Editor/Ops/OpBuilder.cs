using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Blueprinter
{
    public static class OpBuilder
    {
        [Serializable]
        private class EncyclopediaPayload
        {
            public AssetRef[] entries;
        }

        private static readonly string[] EncyclopediaTypeNames =
        {
            "AircraftDefinition",
            "VehicleDefinition",
            "MissileDefinition",
            "BuildingDefinition",
            "ShipDefinition",
            "SceneryDefinition",
            "UnitDefinition",
            "WeaponMount"
        };

        public static bool Build(string[] modAssetPaths, PatchManifest manifest)
        {
            foreach (var path in modAssetPaths)
            {
                var op = AssetDatabase.LoadAssetAtPath<OpCore>(path);
                if (op == null)
                    continue;

                if (op.IsEmpty)
                {
                    Debug.LogWarning($"[Blueprinter] [{op.opId}] No entries, skipping");
                    continue;
                }

                var payload = op.BuildPayload();
                if (payload == null)
                    return false;

                manifest.Ops.Add(new Op
                {
                    opId = op.opId,
                    payloadJson = JsonUtility.ToJson(payload)
                });
            }

            var encyclopediaEntries = BuildEncyclopediaEntries(modAssetPaths);
            if (encyclopediaEntries.Count == 0)
                return true;

            manifest.Ops.Add(new Op
            {
                opId = "OpAddToEncyclopedia",
                payloadJson = JsonUtility.ToJson(new EncyclopediaPayload
                {
                    entries = encyclopediaEntries.ToArray()
                })
            });
            return true;
        }

        private static List<AssetRef> BuildEncyclopediaEntries(IEnumerable<string> modAssetPaths)
        {
            var entries = new List<AssetRef>();

            foreach (var path in modAssetPaths)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null || Array.IndexOf(EncyclopediaTypeNames, asset.GetType().Name) < 0)
                        continue;

                    entries.Add(BlueprinterAssets.CreateAssetRef(asset));
                }
            }

            return entries;
        }
    }
}
