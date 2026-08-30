using System;
using System.Linq;
using UnityEngine;

namespace Blueprinter.Editor.Ops
{
    public enum MissionGroup
    {
        [InspectorName("Free Flight")] FreeFlight,
        [InspectorName("Tutorials")] Tutorials,
        [InspectorName("Missions")] Missions,
    }

    [CreateAssetMenu(menuName = "Blueprinter/OpAddMissions", fileName = "OpAddMissions")]
    public class OpAddMissions : OpCore
    {
        [Tooltip("Mission assets from this mod.")]
        public TextAsset[] Missions = Array.Empty<TextAsset>();
        public MissionGroup[] MissionGroups = { MissionGroup.Missions };

        [Serializable]
        private class PayloadDto
        {
            public AssetRef[] MissionAssets;
            public string[] MissionGroups;
        }

        public override string opId => "OpAddMissions";
        public override bool IsEmpty => Missions == null || Missions.Length == 0;

        public override object BuildPayload()
        {
            var missionAssets = RequireModAssets(Missions);
            if (missionAssets == null)
                return null;

            return new PayloadDto
            {
                MissionAssets = missionAssets,
                MissionGroups = (MissionGroups ?? Array.Empty<MissionGroup>()).Select(group => group.ToString()).ToArray()
            };
        }
    }
}
