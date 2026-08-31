namespace Blueprinter
{
    public static class BlueprinterSettings
    {
        public const string PlaceholderSuffix = "_PLACEHOLDER";
        public const string GameExecutablePrefsKey = "Blueprinter_NuclearOptionExePath";

        public const string RootFolder = "Assets/Blueprinter";
        public const string ModRootFolder = RootFolder + "/Mods";
        public const string GameAssetBundleName = "_donotship";
        public const string GameAssetRootFolder = RootFolder + "/" + GameAssetBundleName;
        public const string StaleGameAssetRootFolder = GameAssetRootFolder + "/_stale";
        public const string GeneratedFolder = RootFolder + "/Generated";
        public const string ModInfoFileName = "modinfo.json";
    }
}
