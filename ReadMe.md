# Atelier Resleriana
Atelier Resleriana is a Unity IL2CPP game made by Koei Tecmo. This project aims to provide localization support for the game through a BepInEx plugin.

## AtelierResleriana.Plugin.Localization
This BepInEx plugin enables custom text localization for Atelier Resleriana (JP) on Steam.

### Limitations
- Text sourced from the game's master data is not currently supported.

### Features
- Preserves the original text format and structure
- Non-invasive patching that maintains game stability
- Localization strategy that involves generative AI, leveraging the large official localization of the Global server before it was discontinued to draw on styles and localization information.

### How It Works
The plugin works through multiple layers of interception:

1. **Simple UI Text**: Direct replacement of strings in UI components, using a static mapping.
2. **TextAsset Resources**: Intercepts and replaces text assets as they're loaded.
3. **Asset Bundles**: Can optionally unpack, modify, and repack asset bundles to localize story content - do note that this will significantly increase loading times.

## Technical Details

- Uses IL2CPP interop to interface with the Unity game engine
- Handles multiple Unity subsystems: TextMeshPro, Addressables (ProvideHandle), and AssetBundle
- Works with binary formats: UnityFS files and Serialized Files
- Implements custom readers/writers for game-specific formats like PackedText

This plugin represents a sophisticated approach to game localization that preserves the original game's structure while replacing text content in multiple layers of the game's architecture.
### Changes in 2.1.1 (this fork)
- **Loading performance.** Asset bundles are no longer copied in full on every load: only the header and file list are read from the game's stream, and a bundle is copied only when it contains text to translate. Repacking a story bundle now re-encodes only the TextAssets it changes and copies every other object through unchanged, and two quadratic loops in `SerializedFileWriter` are gone (the chapter 16 story bundle: about 5 s on the main thread before, about 110 ms after, with byte-identical output).
- **Translation corrections** in `Resources/LocalizationOverrides.json`, applied on top of `LocalizationData.json` at load so the automatic data update does not remove them: machine translations replaced by the official global English for identical Japanese, strings left in Japanese translated (including the chapter-skip story summaries, issue #3), consistent "Part 3" / "Rerun" naming, lines the data was missing, and the game's own `tracking_id` values restored.
- **Tools** in `Tools/TranslationReview` to rebuild and verify all of the above; see its README.

### Installation
A separate pre-packaged zip is provided as a release on Github so extract into the appropriate directory.
Download the zip's contents and extract it to C:\Program Files (x86)\Steam\steamapps\common\AtelierResleriana

### Configuration
The BepInEx configuration file for this plugin is located at:

`[Game Installation Directory]\BepInEx\config\AtelierResleriana.Plugin.Localization.cfg`

For most Steam users, the full path would be:

`C:\Program Files (x86)\Steam\steamapps\common\AtelierResleriana\BepInEx\config\AtelierResleriana.Plugin.Localization.cfg`

This configuration file is automatically generated the first time you run the game with the plugin installed. You can edit this file in any text editor to customize the plugin's behavior.

### Credits
Special thanks to:
* [resleriana-db](https://github.com/theBowja/resleriana-db/tree/main) - for providing insight on the text storage format used by the game.
* [resleriana_sos](https://github.com/CatClighed/resleriana_sos) - highlighting the actual textual assets that exist.
* [MessagePack-CSharp](https://github.com/MessagePack-CSharp) - this project has to reimplement the whole package and functionality for .NET 6.0 and also in a different namespace and assembly as the base-game uses it.

### Disclaimer
This project is not affiliated with or endorsed by Koei Tecmo Games or any of its subsidiaries. All game assets, including text content, artwork, and other media, are the property of their respective owners. This plugin is intended for personal use only and should be used in accordance with all applicable laws and terms of service.

The localization content used by this plugin is derived from officially licensed versions of the game. Users are responsible for ensuring they own legitimate copies of the relevant game versions before using this plugin.

All trademarks, registered trademarks, product names, and company names or logos mentioned herein are the property of their respective owners.

### License
This project's code is provided "as is", without warranty of any kind. Users may fork and modify the code for personal use, but must respect all intellectual property rights related to Atelier Resleriana and its assets.

# Notes
The content catalog is found at
C:\Program Files (x86)\Steam\steamapps\common\AtelierResleriana\AtelierResleriana_Data\ABCache\content_catalogs\{version}_catalog.json

An example version is 1739426221_Zsl3aInZ_YANay4q.
This can be fetched from https://asset.resleriana.jp/asset/{version}/{platform}/catalog.json
Example being https://asset.resleriana.jp/asset/1739426221_Zsl3aInZ_YANay4q/StandaloneWindows64/catalog.json