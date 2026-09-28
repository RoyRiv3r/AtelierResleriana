// Times what the English mod does, the first time a story bundle loads, on the game's thread:
// parse the bundle, read every object, write the serialized file back, repack with LZ4 (fast).
// The text replacement itself is left out (it is a small part); decryption is the game's own work.
using AtelierResleriana.AkatsukiGames.AssetBundleCache;
using AtelierResleriana.Encryption;
using AtelierResleriana.Unity;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

string game = args[0], wanted = args[1];
var reg = JsonNode.Parse(File.ReadAllText(Path.Combine(game, @"BepInEx\plugins\Resources\LocalizationData.json")))["localizationSerializedFileRegistry"]["whitelist"].AsArray().Select(x => x.GetValue<string>()).ToHashSet();
var bundles = new CatalogReader().Read(Directory.GetFiles(Path.Combine(game, @"AtelierResleriana_Data\ABCache\content_catalogs"), "*_catalog.json")[0]).FileCatalog.Bundles;
string dir = Path.Combine(game, @"AtelierResleriana_Data\ABCache\Bundles");
foreach (var info in bundles)
{
    string file = Path.Combine(dir, $"{info.BundleName}_{info.Hash}");
    if (!File.Exists(file)) continue;
    byte[] bytes = File.ReadAllBytes(file);
    if (info.Compression == 3) bytes = new AkatsukiAssetBundleEncryptionAlgorithm(info.BundleName, info.FileSize, info.Hash, info.Crc).Decrypt(bytes);
    var ufs0 = new UnityFSFileReader().Read(new MemoryStream(bytes));
    if (!ufs0.Metadata.DirectoryInfos.Any(d => reg.Contains(d.Path))) continue;
    var dd = ufs0.Metadata.DirectoryInfos.First(d => reg.Contains(d.Path));
    var probe = new SerializedFileReader().Read(ufs0.GetDirectoryStream(dd));
    if (!probe.Objects.Any(o => o.ClassId == ClassIds.TextAsset && Encoding.ASCII.GetString((byte[])probe.GetSerializedObject(o)["m_Name"]) == wanted)) continue;

    for (int run = 0; run < 3; run++)
    {
        var sw = Stopwatch.StartNew(); var t = new List<string>();
        void Lap(string name) { t.Add($"{name} {sw.ElapsedMilliseconds}"); sw.Restart(); }
        var ufs = new UnityFSFileReader().Read(new MemoryStream(bytes)); Lap("unpack bundle");
        var dirs = new List<UnityFSFileDirectory>();
        foreach (var d in ufs.Metadata.DirectoryInfos)
        {
            byte[] db = ufs.GetDirectoryBytes(d);
            if (reg.Contains(d.Path))
            {
                Lap("get file");
                var sf = new SerializedFileReader().Read(new MemoryStream(db)); Lap("read serialized file");
                bool decodeAll = args.Length > 2 && args[2] == "decode-all";
                var objs = sf.Objects.Select((o, i) => (decodeAll || o.ClassId == ClassIds.TextAsset)
                    ? new SerializedFileWriter.SerializedFileObject { PathId = o.PathId, TypeId = o.TypeId, SerializedObject = sf.GetSerializedObject(o), DataIndex = i }
                    : Raw(o, i)).ToArray(); Lap("decode text assets");
                var w = new SerializedFileWriter(new SerializedFileWriter.Options { IsBigEndian = sf.Header.IsBigEndian, UnityVersion = sf.Metadata.UnityVersion, TargetPlatform = sf.Metadata.TargetPlatform, EnableTypeTree = sf.Metadata.EnableTypeTree });
                db = ((MemoryStream)w.Write(sf.Types, objs, sf.ScriptReferences, sf.AssetReferences, sf.ReferenceTypes, sf.UserInformation)).ToArray(); Lap("write serialized file");
                if (args.Length > 3 && run == 0) File.WriteAllBytes(args[3], db);
            }
            dirs.Add(new UnityFSFileDirectory { Path = d.Path, Bytes = db, Flags = d.Flags });
        }
        var repacked = ((MemoryStream)new UnityFSFileWriter(new UnityFSFileWriter.Options { Compression = UnityFSFileCompression.Lz4 }).Write(dirs)).ToArray(); Lap("LZ4 repack");
        Console.WriteLine($"run {run + 1}: " + string.Join(" | ", t) + " ms");
    }
    return;
}
Console.WriteLine("not found");

// Built separately so the file still compiles against the original writer (no RawData there).
static SerializedFileWriter.SerializedFileObject Raw(SerializedFileObject o, int i) =>
#if RAW
    new SerializedFileWriter.SerializedFileObject { PathId = o.PathId, TypeId = o.TypeId, RawData = o.Data, DataIndex = i };
#else
    throw new InvalidOperationException("original writer: use decode-all");
#endif
