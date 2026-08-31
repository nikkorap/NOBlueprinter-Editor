# Blueprinter Editor (WIP still testing)
An unofficial Unity package for creating Nuclear Option mods.

Blueprinter lets you use game assets, components, and ScriptableObject types in blueprinter mods without redistributing the original game files.

Nuclear Option does not provide official mod support.

Blueprinter bepinex Mod (https://github.com/nikkorap/NOBlueprinter-Releases)

## Requirements
- Nuclear Option (https://store.steampowered.com/app/2168680/Nuclear_Option/)
- Unity Hub (https://unity.com/download) and a Unity account
- Unity Editor 2022.3.62f2
- AssetRipper (https://github.com/AssetRipper/AssetRipper)
- Basic Unity Editor knowledge

## Getting Started
Download the latest Blueprinter Unity Project ZIP from the Releases page, then follow the [Blueprinter Editor Guide](GUIDE.md) to open and set up the project. 

For a more visual guide you can refer to [Draken’s Blueprinter Guide](https://docs.google.com/presentation/d/1DRlvonA4_T1rgQ8DB_7Mmis6tjg7ARDbe6uS63d4hSA/edit?usp=sharing)


## Migrating from Old Projects

To move an existing mod from an older Blueprinter project:

1. Copy `Legacy~/LegacySourceExporter.cs` from this repository into `Assets/Editor/LegacySourceExporter.cs` in the old project
2. Press `Ctrl+R` to recompile scripts
3. Open the old project and select `Blueprinter` > `Export Legacy Source ZIP`
4. Select the mod and click `Export Source ZIP`
5. Open the new Blueprinter project
6. Select `Blueprinter` > `Import Source ZIP` and import the exported `.source.zip`

