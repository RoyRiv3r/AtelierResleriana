using AtelierResleriana.Localization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace AtelierResleriana.Plugin.Localization
{
    public static class LocalizationOverrides
    {
        public const string FileName = "LocalizationOverrides.json";

        /// <summary>
        /// Applies corrections kept in a separate file beside the localization data, so that an
        /// automatic update of the data does not wipe them out. Format:
        /// { "masterData": { locale: { table: { id: { field: text } } } },
        ///   "textAssets": { locale: { asset: { entryId: { propertyId: text } } } } }
        /// </summary>
        public static (int Applied, int Skipped) Apply(LocalizationData localizationData, string overridesPath)
        {
            int applied = 0, skipped = 0;
            JsonNode overrides = JsonNode.Parse(File.ReadAllText(overridesPath));

            if (overrides?["masterData"] is JsonObject masterData)
            {
                foreach (var (locale, tables) in masterData)
                {
                    if (tables is not JsonObject tableObject ||
                        !localizationData.MasterDataLocalizationData.TryGetValue(locale, out var localeTables))
                    {
                        continue;
                    }

                    foreach (var (table, rows) in tableObject)
                    {
                        if (rows is not JsonObject rowObject) continue;
                        if (!localeTables.TryGetValue(table, out JsonNode tableNode) || tableNode is not JsonArray tableArray)
                        {
                            tableArray = new JsonArray();
                            localeTables[table] = tableArray;
                        }

                        var byId = new Dictionary<string, JsonObject>();
                        foreach (JsonNode entry in tableArray)
                        {
                            if (entry is JsonObject entryObject && entryObject["id"] != null)
                            {
                                byId[entryObject["id"].ToString()] = entryObject;
                            }
                        }

                        foreach (var (id, fields) in rowObject)
                        {
                            if (fields is not JsonObject fieldObject || !long.TryParse(id, out long numericId)) { skipped++; continue; }
                            if (!byId.TryGetValue(id, out JsonObject target))
                            {
                                target = new JsonObject { ["id"] = numericId };
                                tableArray.Add(target);
                                byId[id] = target;
                            }
                            foreach (var (field, value) in fieldObject)
                            {
                                target[field] = value?.GetValue<string>();
                                applied++;
                            }
                        }
                    }
                }
            }

            if (overrides?["textAssets"] is JsonObject textAssets)
            {
                foreach (var (locale, assets) in textAssets)
                {
                    if (assets is not JsonObject assetObject) continue;
                    foreach (var (asset, entries) in assetObject)
                    {
                        if (entries is not JsonObject entryObject ||
                            !localizationData.TextAssetLocalizationData.TryGetValue(asset, out var assetEntries))
                        {
                            skipped++;
                            continue;
                        }

                        var byId = assetEntries.ToDictionary(x => x.Id.ToString());
                        var added = new List<PackedTextEntryLocalization>();
                        foreach (var (id, properties) in entryObject)
                        {
                            if (properties is not JsonObject propertyObject || !uint.TryParse(id, out uint entryId)) { skipped++; continue; }
                            if (!byId.TryGetValue(id, out var entry))
                            {
                                // Lines the game has but the localization data does not know yet.
                                entry = new PackedTextEntryLocalization { Id = entryId };
                                byId[id] = entry;
                                added.Add(entry);
                            }
                            foreach (var (propertyId, value) in propertyObject)
                            {
                                if (!uint.TryParse(propertyId, out uint key)) { skipped++; continue; }
                                if (!entry.Properties.TryGetValue(key, out var property))
                                {
                                    property = new PackedTextEntryLocalization.Property { Text = "" };
                                    entry.Properties[key] = property;
                                }
                                property.Localizations[locale] = value?.GetValue<string>();
                                applied++;
                            }
                        }
                        if (added.Count > 0)
                        {
                            localizationData.TextAssetLocalizationData[asset] = assetEntries.Concat(added).ToArray();
                        }
                    }
                }
            }

            return (applied, skipped);
        }
    }
}
