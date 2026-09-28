// Finds story/dialogue text the English mod does not translate. Read-only on game files.
// Pass 1: decrypt only the first 64 KB of every bundle (ChaCha20 keystream from offset 0)
//         to read its file list, and sort bundles into known-story / known-other / unknown.
// Pass 2: fully unpack known-story and unknown bundles, extract every PackedText text asset,
//         and compare each line with LocalizationData.json.
using AtelierResleriana.AkatsukiGames.AssetBundleCache;
using AtelierResleriana.Encryption;
using AtelierResleriana.Plugin.Localization;
using AtelierResleriana.Text;
using AtelierResleriana.Unity;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

string game = args[0], outDir = args[1];
Directory.CreateDirectory(outDir);
var data = JsonNode.Parse(File.ReadAllText(Path.Combine(game, @"BepInEx\plugins\Resources\LocalizationData.json")));
var registry = data["localizationSerializedFileRegistry"];
var white = registry["whitelist"].AsArray().Select(x => x.GetValue<string>()).ToHashSet();
var black = registry["blacklist"].AsArray().Select(x => x.GetValue<string>()).ToHashSet();
var translated = new Dictionary<string, Dictionary<uint, bool>>();   // asset -> entry id -> has English
foreach (var (name, entries) in data["textAssetLocalizationData"].AsObject())
{
    var ids = new Dictionary<uint, bool>();
    foreach (var e in entries.AsArray())
    {
        bool en = e["properties"].AsObject().Any(p => !string.IsNullOrEmpty(p.Value["localizations"]?["en"]?.GetValue<string>()));
        ids[e["id"].GetValue<uint>()] = en;
    }
    translated[name] = ids;
}

string catalogPath = Directory.GetFiles(Path.Combine(game, @"AtelierResleriana_Data\ABCache\content_catalogs"), "*_catalog.json")[0];
var bundles = new CatalogReader().Read(catalogPath).FileCatalog.Bundles;
string dir = Path.Combine(game, @"AtelierResleriana_Data\ABCache\Bundles");
var process = typeof(AkatsukiAssetBundleEncryptionAlgorithm).GetMethod("ProcessData", BindingFlags.NonPublic | BindingFlags.Static);

byte[] DecryptPrefix(BundleInfo info, string file, int prefix)
{
    using var fs = File.OpenRead(file);
    const int headerAndHash = 12 + 16;   // magic, version, reserved, encrypted flag; then MD5
    if (info.Compression != 3) { var raw = new byte[Math.Min(prefix, fs.Length)]; fs.ReadExactly(raw); return raw; }
    long dataLength = fs.Length - headerAndHash;
    var head = new byte[headerAndHash]; fs.ReadExactly(head);
    bool encrypted = BitConverter.ToUInt32(head, 8) == 1;
    var enc = new byte[Math.Min(prefix, dataLength)]; fs.ReadExactly(enc);
    if (!encrypted) return enc;
    var hash = SHA512.HashData(Encoding.UTF8.GetBytes($"{info.BundleName}-{dataLength}-{info.Hash}-{info.Crc}"));
    return (byte[])process.Invoke(null, new object[] { enc, hash[..0x20], SHA512.HashData(hash) });
}

// Self-check: a decrypted prefix must equal the start of a full decryption.
foreach (var info in bundles.Where(b => b.Compression == 3 && File.Exists(Path.Combine(dir, $"{b.BundleName}_{b.Hash}"))).Take(20))
{
    string file = Path.Combine(dir, $"{info.BundleName}_{info.Hash}");
    byte[] full = new AkatsukiAssetBundleEncryptionAlgorithm(info.BundleName, info.FileSize, info.Hash, info.Crc).Decrypt(File.ReadAllBytes(file));
    byte[] part = DecryptPrefix(info, file, 1 << 16);
    if (!full.AsSpan(0, part.Length).SequenceEqual(part)) throw new Exception($"prefix decryption differs for {info.BundleName}");
}
Console.WriteLine("prefix decryption matches full decryption on 20 bundles");

// Pass 1
var kinds = new ConcurrentDictionary<string, string>();
int missing = 0, failed = 0;
Parallel.ForEach(bundles, new ParallelOptions { MaxDegreeOfParallelism = 2 }, info =>
{
    string file = Path.Combine(dir, $"{info.BundleName}_{info.Hash}");
    if (!File.Exists(file)) { Interlocked.Increment(ref missing); return; }
    try
    {
        string[] paths;
        try { paths = BundleDirectoryReader.ReadDirectoryPaths(new MemoryStream(DecryptPrefix(info, file, 1 << 16))); }
        catch { paths = BundleDirectoryReader.ReadDirectoryPaths(new MemoryStream(DecryptPrefix(info, file, int.MaxValue))); }
        var cabs = paths.Where(p => p.StartsWith("CAB-") && !Path.HasExtension(p)).ToArray();
        kinds[info.BundleName] = cabs.Any(white.Contains) ? "story" : cabs.Length > 0 && cabs.All(black.Contains) ? "other" : cabs.Length == 0 ? "no-cab" : "unknown";
    }
    catch { Interlocked.Increment(ref failed); }
});
var byKind = kinds.GroupBy(k => k.Value).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"pass 1: {string.Join(", ", byKind.Select(k => $"{k.Key} {k.Value}"))}; not downloaded {missing}; unreadable {failed}");

// Pass 2
var report = new ConcurrentBag<string>();
var dump = new ConcurrentDictionary<string, JsonArray>();
var toScan = bundles.Where(b => kinds.TryGetValue(b.BundleName, out var k) && (k == "story" || k == "unknown")).ToArray();
Parallel.ForEach(toScan, new ParallelOptions { MaxDegreeOfParallelism = 2 }, info =>
{
    try
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(dir, $"{info.BundleName}_{info.Hash}"));
        if (info.Compression == 3) bytes = new AkatsukiAssetBundleEncryptionAlgorithm(info.BundleName, info.FileSize, info.Hash, info.Crc).Decrypt(bytes);
        var ufs = new UnityFSFileReader().Read(new MemoryStream(bytes));
        foreach (var d in ufs.Metadata.DirectoryInfos.Where(d => d.Path.StartsWith("CAB-") && !Path.HasExtension(d.Path)))
        {
            var sf = new SerializedFileReader().Read(ufs.GetDirectoryStream(d));
            foreach (var o in sf.Objects.Where(o => o.ClassId == ClassIds.TextAsset))
            {
                var so = sf.GetSerializedObject(o);
                var script = (byte[])so["m_Script"];
                var reader = new PackedTextReader();
                if (!reader.IsValid(script)) continue;
                string name = Encoding.ASCII.GetString((byte[])so["m_Name"]);
                var packed = JsonNode.Parse(reader.Read(script).ToJson());
                var props = packed["properties"].AsArray();
                uint idProp = props.First(p => p["type"].GetValue<int>() == 0)["id"].GetValue<uint>();
                var textProps = props.Where(p => p["type"].GetValue<int>() == 2).Select(p => p["id"].GetValue<uint>().ToString()).ToArray();
                int lines = 0, untranslated = 0;
                var missingLines = new JsonArray();
                foreach (var e in packed["entries"].AsArray())
                {
                    string text = string.Join(" ", textProps.Select(p => e[p]?.GetValue<string>() ?? ""));
                    if (!text.Any(c => c >= 0x3040 && c <= 0x9fff)) continue;
                    lines++;
                    uint id = e[idProp.ToString()].GetValue<uint>();
                    if (!(translated.TryGetValue(name, out var ids) && ids.TryGetValue(id, out bool en) && en))
                    {
                        untranslated++;
                        missingLines.Add(JsonNode.Parse(e.ToJsonString()));
                    }
                }
                if (lines == 0) continue;
                report.Add($"{name}\t{kinds[info.BundleName]}\t{lines}\t{untranslated}\t{(translated.ContainsKey(name) ? "in-mod-data" : "not-in-mod-data")}\t{d.Path}");
                if (untranslated > 0) dump[name] = missingLines;
            }
        }
    }
    catch (Exception ex) { report.Add($"ERROR {info.BundleName}\t{ex.Message}"); }
});
File.WriteAllLines(Path.Combine(outDir, "scenes.tsv"), new[] { "asset\tbundle_kind\tjapanese_lines\tuntranslated_lines\tmod_data\tcab" }.Concat(report.OrderBy(x => x)));
File.WriteAllText(Path.Combine(outDir, "untranslated_lines.json"), JsonSerializer.Serialize(dump.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value),
    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
var rows = report.Where(r => !r.StartsWith("ERROR")).Select(r => r.Split('\t')).ToArray();
Console.WriteLine($"pass 2: {rows.Length} text assets with Japanese; {rows.Count(r => r[3] != "0")} have untranslated lines ({rows.Sum(r => int.Parse(r[3]))} lines of {rows.Sum(r => int.Parse(r[2]))}); errors {report.Count(r => r.StartsWith("ERROR"))}");
