# Blueprinter Editor Guide
Blueprinter uses local placeholder copies of Nuclear Option assets for making mods. They are stored in `_donotship` and are not included in mod bundles. The Blueprinter Mod restores those references to matching game assets at runtime, and ops add additional game integration.
The full setup process can take 1–3 hours depending on hardware.

## 1. Requirements
- Nuclear Option (https://store.steampowered.com/app/2168680/Nuclear_Option/)
- Unity Hub (https://unity.com/download) and a Unity account
- Unity Editor 2022.3.62f2
- git (https://git-scm.com)
- AssetRipper (https://github.com/AssetRipper/AssetRipper)
- Basic Unity Editor knowledge

## 2. Export Game Assets with AssetRipper
AssetRipper exports the Nuclear Option assets that Blueprinter imports as local placeholders.
AssetRipper file windows can open behind its browser window. Check the taskbar if a window does not appear.
1. Start AssetRipper.
2. Select `File` > `Settings`.
3. Set `Script Content Level` to `Level 1`.
4. Set `Script Export Format` to `Decompilation`.
5. Save the settings.
6. Select `File` > `Open File`.
7. Navigate to the game install folder and select `NuclearOption.exe`.
8. Select `Export` > `Export all files`.
9. Select an output folder.
10. Enable `Create Subfolder`.
11. Click `Export Unity Project`.
12. Wait until the AssetRipper console shows `Export : Finished post-export`.
13. Close AssetRipper.

## 3. Unity Project Setup
1. Download the latest Blueprinter Unity Project ZIP from the Releases page and extract it somewhere.
2. Open Unity Hub.
3. Click `Add` > `Add project from disk`.
4. Select the extracted Blueprinter project folder and open it.
5. On the top bar select `Blueprinter` > `Project Setup`.
6. Enter the installed game version under `1. Enter game version`.
7. Click `2. Import Game Assemblies`
8. Navigate to the game install folder and select `NuclearOption.exe`.
9. Click `3. Import Game Assets` 
10. Navigate to the Assetripper output folder and select the `ExportedProject/Assets` folder.
11. If prompted, click `Import TMP Essentials`.
12. **DO NOT IMPORT `TMP Examples & Extras`.**
13. Click `4. Refresh Op References`.
14. Click `5. Build _donotship`.
Building `_donotship` prepares the imported game assets for mod builds, that will take a while.
`_donotship` contains the imported placeholder assets and is locked by default. This folder is read only.
If the mod uses custom script DLLs, place them under `Assets/Plugins`.

## 4. Example Mod
A source ZIP contains editable Blueprinter mod source. Blueprinter includes `Assets/Blueprinter/Example/myfirstmod.source.zip` as an example.

1. Select `Blueprinter` > `Import Source ZIP`.
2. Select `Assets/Blueprinter/Example/myfirstmod.source.zip`.
3. Click `Import`.
4. Open the imported mod under `Assets/Blueprinter/Mods`.

The example shows how mod assets and ops work.

## 5. Mod Folder and Assets
Each mod uses one folder directly under `Assets/Blueprinter/Mods`, such as `Assets/Blueprinter/Mods/MyAircraftMod`. Create or import mod assets inside this folder.
Imported game assets have the `_PLACEHOLDER` suffix. These are assets that exist in Nuclear Option.
You can reference `_PLACEHOLDER` assets like normal Unity assets. Blueprinter excludes them from the mod bundle and writes a manifest.
Blueprinter Mod reads the manifest and uses it to restore references to matching game assets at runtime.
Do not rename, move, modify, or distribute files from `_donotship`.

## 6. Blueprinter Ops
Blueprinter ops are custom game integration functions.
1. Right-click inside the mod folder in the `Project` window.
2. Select `Create` > `Blueprinter`.
Keep each Op inside the mod folder.
- `OpAddAircraftToHangars`: Select an aircraft (either modded or vanilla), then add the hangars (either modded or vanilla) that can spawn it.
- `OpAddWeaponToHardpoint`: Select a weapon (either modded or vanilla), add target aircraft (either modded or vanilla), and select the allowed hardpoints.
- `OpAddLoadingScreens`: Add sprite assets to the loading screen pool.
- `OpAddMissions`: Add mission files and select their mission groups.

## 7. Build a Mod
1. Select `Blueprinter` > `Mod Builder`.
2. Select the mod under `Select Mod`.
3. Enter the `Display name`.
4. Enter the `Mod version` in `1.2.3` format.
5. Select a `Build output folder` outside `Assets/Blueprinter/Mods` and `Assets/Blueprinter/_donotship`.
6. Click `Build Mod`.
Blueprinter creates `<DisplayName>_<Version>.nobp` in the selected output folder.

## 8. Source ZIPs
A source ZIP contains mod contents and a manifest used to rebuild game asset and script references in another Blueprinter project.
### Export a Source ZIP
1. Select `Blueprinter` > `Mod Builder`.
2. Select the mod.
3. Click `Export Source ZIP`.
4. Select a location outside `Assets/Blueprinter/Mods` and `Assets/Blueprinter/_donotship`.
### Import a Source ZIP
1. Select `Blueprinter` > `Import Source ZIP`.
2. Select the `.source.zip` file.
3. Replace unresolved references if needed.
4. Click `Import`.
Blueprinter imports the mod under `Assets/Blueprinter/Mods`.

## 9. Other Tools
### Validate references
1. Open a prefab in Prefab Mode.
2. Select `Blueprinter` > `Tools` > `Validate references`.
Blueprinter checks the prefab for missing references and reports them in the Console.

## 10. FAQ (wip)

I cant see the blueprinter tab on the top bar
- Check the console, if it says `Library\ScriptAssemblies\Assembly-CSharp.dll: The target path already exists and is read-only` then make sure you don't have any `*.cs` files in `Assets\`, most commonly caused by importing `TMP Examples & Extras`. If you did import that then delete `assets/TextMesh Pro/Examples & Extras` folder
