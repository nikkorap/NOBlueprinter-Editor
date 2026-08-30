using System;
using System.Collections.Generic;

namespace Blueprinter
{
    [Serializable]
    public class PatchManifest
    {
        public string modName;
        public int schemaVersion = 3;
        public string modVersion;
        public string gameVersion;

        public List<AssetPatch> Patches = new List<AssetPatch>();
        public List<Op> Ops = new List<Op>();
        public List<AddressableOverride> Addressables = new List<AddressableOverride>();
    }

    [Serializable]
    public class AssetPatch
    {
        public LocationRef GameAsset;
        public List<LocationRef> PatchLocations = new List<LocationRef>();
    }

    [Serializable]
    public class LocationRef
    {
        public string id;
        public AssetRef asset;
        public string hierarchyPath;
        public string componentType;
        public int componentIndex = 0;
        public string memberPath;
    }

    [Serializable]
    public class AssetRef
    {
        public string locator;
        public string name;
        public string type;
    }

    [Serializable]
    public class AddressableOverride
    {
        public string guid;
        public string subObjectName;
        public string subObjectType;
        public AssetRef BundleAsset;
    }

    [Serializable]
    public class Op
    {
        public string opId;
        public string payloadJson;
    }
}
