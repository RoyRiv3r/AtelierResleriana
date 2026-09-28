// Dumps the game's Japanese master data (the source text the English mod translates)
// to jp/<table>.json, using the English mod's own readers. Read-only on game files.
using AtelierResleriana.BestHTTP;
using AtelierResleriana.MasterData;
using System.Text.Json;

string persistent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    @"AppData\LocalLow\KOEI TECMO GAMES CO_, LTD_\レスレリアーナのアトリエ");
string outDir = args.Length > 0 ? args[0] : "jp";

var entries = new LibraryReader().Read(File.OpenRead(Path.Combine(persistent, "Library"))).ToArray();
foreach (var e in entries) Console.WriteLine($"cache entry: {e.Uri} ({e.BodyLength} B, index {e.MappedNameIndex})");
var master = entries.Where(e => e.Uri.AbsolutePath.Contains("/master_data/")).OrderBy(e => e.LastAccess).Last();
string version = master.Uri.Segments[2].TrimEnd('/');
Console.WriteLine($"master data version {version}");

IEnumerable<MasterDataFile> files = null;
foreach (string candidate in new[] { Path.Combine(persistent, "HTTPCache", master.MappedNameIndex.ToString()), Path.Combine(persistent, "CachedMasterData.bin") })
{
    try
    {
        using var stream = File.OpenRead(candidate);
        files = (await new MasterDataReader().ReadEncryptedAsync(stream, version)).ToArray();
        Console.WriteLine($"decrypted {candidate}");
        break;
    }
    catch (Exception ex) { Console.WriteLine($"{candidate}: {ex.GetType().Name} {ex.Message}"); }
}
if (files == null) return 1;

Directory.CreateDirectory(outDir);
var serializer = new MasterDataSerializer();
var options = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = true };
int n = 0;
foreach (var f in files)
{
    File.WriteAllText(Path.Combine(outDir, f.Name + ".json"), JsonSerializer.Serialize(serializer.Deserialize(f.Bytes), options));
    n++;
}
File.WriteAllText(Path.Combine(outDir, "_version.txt"), version);
Console.WriteLine($"wrote {n} tables to {outDir}");
return 0;
