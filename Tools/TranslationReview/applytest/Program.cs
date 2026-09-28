// End-to-end check of LocalizationOverrides.json along the path the plugin uses in game:
// load LocalizationData.json, apply the overrides (the plugin's own code, linked in),
// localize the game's real master data exactly like LocalizationService.TryLocalize does,
// then read the result back and verify each correction in report/changes.tsv landed.
using AtelierResleriana.BestHTTP;
using AtelierResleriana.Localization;
using AtelierResleriana.MasterData;
using AtelierResleriana.Plugin.Localization;
using AtelierResleriana.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string review = args[0], dataPath = args[1];
var data = JsonSerializer.Deserialize<LocalizationData>(File.ReadAllText(dataPath), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
var (applied, skipped) = LocalizationOverrides.Apply(data, Path.Combine(review, "..", "..", "AtelierResleriana.Plugin.Localization", "Resources", "LocalizationOverrides.json"));
Console.WriteLine($"overrides: applied {applied}, skipped {skipped}");

// Same updater configuration as LocalizationService's constructor.
var matcher = new StringFormatParameterMatcher();
var updater = new MasterDataFileRuntimeUpdater(new MasterDataFileRuntimeUpdater.Options
{
    ShouldUpdate = (b, u) => b is string bs && u is string us && matcher.IsMatch(bs, us)
});
var serializer = new MasterDataSerializer();

string persistent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\KOEI TECMO GAMES CO_, LTD_\レスレリアーナのアトリエ");
string version = File.ReadAllText(Path.Combine(review, "jp", "_version.txt")).Trim();
var files = (await new MasterDataReader().ReadEncryptedAsync(File.OpenRead(Path.Combine(persistent, "CachedMasterData.bin")), version)).ToArray();

var result = new Dictionary<string, JsonArray>();
foreach (var f in files)
{
    if (!data.MasterDataLocalizationData["en"].TryGetValue(f.Name, out var update)) continue;
    object md = serializer.Deserialize(f.Bytes);
    updater.UpdateEntities(md, update);
    byte[] bytes = serializer.Serialize(md);
    if (args.Length > 2) { Directory.CreateDirectory(args[2]); File.WriteAllBytes(Path.Combine(args[2], f.Name + ".orig"), f.Bytes); File.WriteAllBytes(Path.Combine(args[2], f.Name + ".game"), bytes); }
    result[f.Name] = JsonNode.Parse(JsonSerializer.Serialize(serializer.Deserialize(bytes))) as JsonArray;
}

int ok = 0, wrong = 0, rejected = 0; var examples = new List<string>();
foreach (string line in File.ReadAllLines(Path.Combine(review, "report", "changes.tsv")).Skip(1))
{
    var p = line.Split('\t');
    if (p[0] != "master") continue;
    string table = p[1], id = p[2], field = p[3], expected = p[7].Replace("\\n", "\n");
    var row = result[table].OfType<JsonObject>().FirstOrDefault(r => r["id"]?.ToString() == id);
    string actual = row?[field]?.GetValue<string>();
    if (actual == expected) ok++;
    else if (actual == p[5].Replace("\\n", "\n")) { rejected++; if (examples.Count < 8) examples.Add($"kept Japanese  {table}/{id}/{field}: {expected}"); }
    else { wrong++; if (examples.Count < 8) examples.Add($"MISMATCH {table}/{id}/{field}: got [{actual}] want [{expected}]"); }
}
Console.WriteLine($"game data: {ok} corrections in effect, {rejected} refused by the plugin's placeholder check, {wrong} wrong");
examples.ForEach(Console.WriteLine);

int uiOk = 0, uiBad = 0;
foreach (string line in File.ReadAllLines(Path.Combine(review, "report", "changes.tsv")).Skip(1))
{
    var p = line.Split('\t');
    if (p[0] != "ui" && p[0] != "ui-new") continue;
    var entry = data.TextAssetLocalizationData[p[1]].First(e => e.Id.ToString() == p[2]);
    if (entry.Properties[uint.Parse(p[3])].Localizations["en"] == p[7].Replace("\\n", "\n")) uiOk++; else uiBad++;
}
Console.WriteLine($"menu text: {uiOk} corrections in effect, {uiBad} wrong");
return wrong + uiBad == 0 ? 0 : 1;
