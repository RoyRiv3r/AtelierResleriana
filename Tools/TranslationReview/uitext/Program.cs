// Extracts the game's current menu text (SystemText, BuiltinText, ErrorText) from the
// asset bundles in ABCache, as JSON, using the English mod's own readers. Read-only.
using AtelierResleriana.AkatsukiGames.AssetBundleCache;
using AtelierResleriana.Encryption;
using AtelierResleriana.Text;
using AtelierResleriana.Unity;
using System.Collections.Concurrent;
using System.Text;

string game = args[0], outDir = args[1];
var wanted = new HashSet<string> { "SystemText", "BuiltinText", "ErrorText" };
string catalogPath = Directory.GetFiles(Path.Combine(game, @"AtelierResleriana_Data\ABCache\content_catalogs"), "*_catalog.json")[0];
var bundles = new CatalogReader().Read(catalogPath).FileCatalog.Bundles;
string dir = Path.Combine(game, @"AtelierResleriana_Data\ABCache\Bundles");

// Text bundles are small; try likely names first, then everything under 2 MB.
var ordered = bundles.OrderByDescending(b => b.BundleName.Contains("text", StringComparison.OrdinalIgnoreCase))
                     .Where(b => b.FileSize < 2_000_000).ToArray();
Console.WriteLine($"{bundles.Length} bundles, scanning {ordered.Length} small ones");
var found = new ConcurrentDictionary<string, string>();
Directory.CreateDirectory(outDir);
Parallel.ForEach(ordered, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (info, state) =>
{
    if (found.Count == wanted.Count) { state.Stop(); return; }
    string file = Path.Combine(dir, $"{info.BundleName}_{info.Hash}");
    if (!File.Exists(file)) return;
    try
    {
        byte[] data = File.ReadAllBytes(file);
        if (info.Compression == 3) data = new AkatsukiAssetBundleEncryptionAlgorithm(info.BundleName, info.FileSize, info.Hash, info.Crc).Decrypt(data);
        var fs = new UnityFSFileReader().Read(new MemoryStream(data));
        foreach (var d in fs.Metadata.DirectoryInfos.Where(d => d.Path.StartsWith("CAB-") && !Path.HasExtension(d.Path)))
        {
            var sf = new SerializedFileReader().Read(fs.GetDirectoryStream(d));
            foreach (var o in sf.Objects.Where(o => o.ClassId == ClassIds.TextAsset))
            {
                var so = sf.GetSerializedObject(o);
                string name = Encoding.ASCII.GetString((byte[])so["m_Name"]);
                if (!wanted.Contains(name)) continue;
                var bytes = (byte[])so["m_Script"];
                var reader = new PackedTextReader();
                if (!reader.IsValid(bytes)) continue;
                File.WriteAllText(Path.Combine(outDir, name + ".json"), reader.Read(bytes).ToJson());
                found[name] = info.BundleName;
                Console.WriteLine($"{name} <- {info.BundleName}");
            }
        }
    }
    catch { }
});
Console.WriteLine($"found {found.Count}/{wanted.Count}");
