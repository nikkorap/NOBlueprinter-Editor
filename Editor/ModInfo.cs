using System;
using System.IO;
using UnityEngine;
using static Blueprinter.BlueprinterSettings;

namespace Blueprinter
{
    [Serializable]
    public class ModInfo
    {
        public const string DefaultVersion = "0.0.1";

        public string displayName;
        public string version;

        public static ModInfo Load(string modName)
        {
            var path = BlueprinterAssets.GetModFolderPath(modName) + "/" + ModInfoFileName;
            var absolutePath = BlueprinterAssets.ToAbsolutePath(path);
            if (!File.Exists(absolutePath))
            {
                return new ModInfo
                {
                    displayName = modName,
                    version = DefaultVersion
                };
            }

            var info = JsonUtility.FromJson<ModInfo>(File.ReadAllText(absolutePath));
            if (info == null)
                info = new ModInfo();

            if (string.IsNullOrWhiteSpace(info.displayName))
                info.displayName = modName;
            if (string.IsNullOrEmpty(info.version))
                info.version = DefaultVersion;

            return info;
        }

        public static void Save(string modName, string displayName, string version)
        {
            var path = BlueprinterAssets.GetModFolderPath(modName) + "/" + ModInfoFileName;
            var info = new ModInfo
            {
                displayName = string.IsNullOrWhiteSpace(displayName) ? modName : displayName,
                version = version ?? string.Empty
            };

            var absolutePath = BlueprinterAssets.ToAbsolutePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(info, true) + "\n");
        }
    }
}
