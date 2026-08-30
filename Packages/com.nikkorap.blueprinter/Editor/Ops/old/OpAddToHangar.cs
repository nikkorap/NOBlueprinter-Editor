using System;
using System.Linq;
using UnityEngine;

namespace Blueprinter
{
    public enum HangarId
    {
        [InspectorName("Revetment")] revetment1__revetment1,
        [InspectorName("Hardened Shelter")] shelter1__shelter1,
        [InspectorName("Medium Hangar")] hangar_med__hangar_med,
        [InspectorName("Helipad")] helipad__helipad,

        [InspectorName("Helipad (Hyperion)")] fleetCarrier1__hangar_F,
        [InspectorName("hangar_R1 (Hyperion)")] fleetCarrier1__hangar_R1,
        [InspectorName("hangar_R2 (Hyperion)")] fleetCarrier1__hangar_R2,
        [InspectorName("hangar_R3 (Hyperion)")] fleetCarrier1__hangar_R3,

        [InspectorName("Helipad Small (Annex)")] AssaultCarrier1__hangar_F,
        [InspectorName("Helipad Large (Annex)")] AssaultCarrier1__hangar_M,
        [InspectorName("hangar_R1 (Annex)")] AssaultCarrier1__hangar_R1,
        [InspectorName("hangar_R2 (Annex)")] AssaultCarrier1__hangar_R2,
        [InspectorName("hangar_R3 (Annex)")] AssaultCarrier1__hangar_R3,

        [InspectorName("hangar (Dynamo)")] Destroyer1__Hull_hangarFloor
    }

    public class OpAddToHangar : OpCore
    {
        [Tooltip("AircraftDefinition asset from this mod.")]
        public UnityEngine.Object AircraftDefinition;
        public HangarId[] Hangars = new[] { HangarId.revetment1__revetment1 };

        [Serializable]
        private class PayloadDto
        {
            public AssetRef BundleAsset;
            public string[] Hangars;
        }

        public override string opId => "OpAddToHangar";

        public override object BuildPayload()
        {
            var assetRef = RequireModAsset(AircraftDefinition, nameof(AircraftDefinition), "AircraftDefinition");
            if (assetRef == null)
                return null;

            return new PayloadDto
            {
                BundleAsset = assetRef,
                Hangars = (Hangars ?? Array.Empty<HangarId>()).Select(h => h.ToString()).ToArray()
            };
        }
    }
}
