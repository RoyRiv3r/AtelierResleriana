// Offline check of BundleDirectoryReader against the mod's original full UnityFS reader,
// on real bundles from the game's ABCache (decrypted in memory with the mod's own code).
using AtelierResleriana.AkatsukiGames.AssetBundleCache;
using AtelierResleriana.Encryption;
using AtelierResleriana.Plugin.Localization;
using AtelierResleriana.Unity;
using System.Diagnostics;

string game = args[0];
int limit = args.Length > 1 ? int.Parse(args[1]) : int.MaxValue;
string catalogPath = Directory.GetFiles(Path.Combine(game, @"AtelierResleriana_Data\ABCache\content_catalogs"), "*_catalog.json")[0];
Catalog catalog = new CatalogReader().Read(catalogPath);
string bundles = Path.Combine(game, @"AtelierResleriana_Data\ABCache\Bundles");

int ok = 0, bad = 0, missing = 0, blocksAtEnd = 0;
long bytesRead = 0, bytesTotal = 0;
var sw = Stopwatch.StartNew();
foreach (BundleInfo info in catalog.FileCatalog.Bundles.OrderBy(b => b.BundleName.GetHashCode()).Take(limit))
{
    string file = Path.Combine(bundles, $"{info.BundleName}_{info.Hash}");
    if (!File.Exists(file)) { missing++; continue; }
    byte[] data = File.ReadAllBytes(file);
    if (info.Compression == 3)
        data = new AkatsukiAssetBundleEncryptionAlgorithm(info.BundleName, info.FileSize, info.Hash, info.Crc).Decrypt(data);

    try
    {
        var strict = new StrictStream(data, chunk: 4093);
        string[] fast = BundleDirectoryReader.ReadDirectoryPaths(strict);
        UnityFSFile reference = new UnityFSFileReader().Read(new MemoryStream(data));
        string[] slow = reference.Metadata.DirectoryInfos.Select(d => d.Path).ToArray();
        if ((reference.Header.Flags & 0x80) != 0) blocksAtEnd++;
        bytesRead += strict.BytesRead; bytesTotal += data.Length;
        if (fast.SequenceEqual(slow)) ok++;
        else { bad++; Console.WriteLine($"MISMATCH {info.BundleName}: [{string.Join(",", fast)}] vs [{string.Join(",", slow)}]"); }
    }
    catch (Exception e) { bad++; Console.WriteLine($"ERROR {info.BundleName}: {e.GetType().Name} {e.Message}"); }
}
Console.WriteLine($"{ok} match, {bad} bad, {missing} not downloaded, {blocksAtEnd} with blocks-info-at-end; " +
    $"read {bytesRead / 1024} KB of {bytesTotal / 1048576} MB ({100.0 * bytesRead / Math.Max(1, bytesTotal):F3}%) in {sw.Elapsed.TotalSeconds:F0}s");
return bad == 0 ? 0 : 1;

// Behaves like the game's stream as the adapter uses it: short reads, and it fails loudly on
// anything other than Length / Seek(Begin) / Read, or on being disposed.
sealed class StrictStream : Stream
{
    readonly byte[] data; readonly int chunk; long pos;
    public long BytesRead;
    public StrictStream(byte[] data, int chunk) { this.data = data; this.chunk = chunk; }
    public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => pos; set => throw new InvalidOperationException("Position set"); }
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (origin != SeekOrigin.Begin) throw new InvalidOperationException($"Seek {origin}");
        return pos = offset;
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = (int)Math.Min(Math.Min(count, chunk), data.Length - pos);
        if (n <= 0) return 0;
        Array.Copy(data, pos, buffer, offset, n); pos += n; BytesRead += n; return n;
    }
    public override int ReadByte() => throw new InvalidOperationException("ReadByte");
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) => throw new InvalidOperationException("Disposed the game's stream");
}
