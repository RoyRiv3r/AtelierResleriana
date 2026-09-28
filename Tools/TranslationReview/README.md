# Translation review tools

Tools used to build `AtelierResleriana.Plugin.Localization/Resources/LocalizationOverrides.json`
and to check the plugin against the installed game. Everything here reads game files and
never writes to the game folder. Extracted game data is git-ignored; regenerate it locally.

| Tool | What it does |
|---|---|
| `extract/` | Decrypts the game's cached master data (`CachedMasterData.bin` in LocalLow) and writes the Japanese tables to `jp/`. |
| `uitext/` | Extracts the current `SystemText`, `BuiltinText` and `ErrorText` from the game's asset bundles to `ui/`. |
| `compare.py` | Compares the mod's English with the official global English published by Barrel Wisdom (`barrelwisdom/<kind>.<en,ja>.json` from `https://barrelwisdom.com/api/A25/<kind>/<lang>/`), aligned by the Japanese text. |
| `build_overrides.py` | Builds the corrections file from `jp/`, `Localization/` and `translations/`; see its docstring for the rules. Writes `report/changes.tsv` (every change, with its Japanese, old and new English) and `report/untranslated.tsv`. |
| `make_todo.py`, `merge_todo.py` | Split still-Japanese strings into numbered batches under `todo/`, and merge hand translations back into `translations/` (checking placeholders and line breaks). |
| `applytest/` | Applies the corrections along the plugin's own path (its `LocalizationOverrides` code and the master data updater) to the game's real master data and checks that every correction takes effect. |
| `roundtrip/` | Checks that re-encoding master data keeps every value (issue #4); `compare_dump.py` decodes independently with Python `msgpack` + `lz4`. |
| `story/` | Scans every story bundle and lists dialogue lines that have no English in the localization data. |
| `bundletest/` | Checks `BundleDirectoryReader` against the full UnityFS reader on real bundles, through a stream that fails on anything but Length/Seek/Read. |
| `repacktime/` | Times the plugin's repack of a story bundle and checks the output is byte-identical to the original writer. |

Typical run, with the game installed and run at least once:

```
dotnet run --project extract -c Release -- jp
python build_overrides.py
dotnet run --project applytest -c Release -- . ../../Localization/LocalizationData.json
```
