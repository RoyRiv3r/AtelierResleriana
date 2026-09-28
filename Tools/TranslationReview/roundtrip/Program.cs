// Issue #4 check: does the mod's MessagePack re-encoding reproduce the game's master data
// byte for byte when nothing is changed? (The plugin re-encodes every table it localizes,
// and the whole container, every time the game loads its master data.)
using AtelierResleriana.Encryption;
using AtelierResleriana.MasterData;

string persistent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow\KOEI TECMO GAMES CO_, LTD_\レスレリアーナのアトリエ");
string version = File.ReadAllText(args[0]).Trim();
using var decrypted = new MemoryStream();
MasterDataEncryptionAlgorithm.FromVersion(version).Decrypt(File.OpenRead(Path.Combine(persistent, "CachedMasterData.bin")), decrypted);
byte[] container = decrypted.ToArray();

var files = new MasterDataReader().Read(new MemoryStream(container)).ToArray();
var serializer = new MasterDataSerializer();
int same = 0, sameValues = 0, differ = 0, failed = 0;
foreach (var f in files)
{
    try
    {
        byte[] again = serializer.Serialize(serializer.Deserialize(f.Bytes));
        if (again.AsSpan().SequenceEqual(f.Bytes)) same++;
        else if (System.Text.Json.JsonSerializer.Serialize(serializer.Deserialize(again)) == System.Text.Json.JsonSerializer.Serialize(serializer.Deserialize(f.Bytes))) { sameValues++; if (sameValues <= 3) Console.WriteLine($"table {f.Name}: bytes differ but decoded values identical; first bytes {Convert.ToHexString(f.Bytes, 0, 8)} vs {Convert.ToHexString(again, 0, 8)}"); }
        else { differ++; if (differ <= 15) Console.WriteLine($"table {f.Name}: {f.Bytes.Length} -> {again.Length} bytes, first difference at {FirstDiff(f.Bytes, again)}"); }
    }
    catch (Exception e) { failed++; Console.WriteLine($"table {f.Name}: {e.GetType().Name} {e.Message}"); }
}
Console.WriteLine($"tables: {same} byte-identical, {sameValues} same values in a different encoding, {differ} DIFFERENT VALUES, {failed} failed ({files.Length} total)");

using var rewritten = new MemoryStream();
new MasterDataWriter().Write(rewritten, files);
byte[] c2 = rewritten.ToArray();
Console.WriteLine($"container: {(c2.AsSpan().SequenceEqual(container) ? "identical" : $"different ({container.Length} -> {c2.Length} bytes, first difference at {FirstDiff(container, c2)})")}");

static long FirstDiff(byte[] a, byte[] b) { int n = Math.Min(a.Length, b.Length); for (int i = 0; i < n; i++) if (a[i] != b[i]) return i; return n; }
