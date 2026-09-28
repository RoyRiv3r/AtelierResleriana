using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.IO;

namespace AtelierResleriana.Plugin.Localization
{
    /// <summary>
    /// A read-only managed view of the stream the game hands to AssetBundle.LoadFromStream.
    /// It only ever calls Length, Seek and Read on the game's stream, and disposing it never
    /// closes that stream: Unity reads from it after the plugin is done.
    /// Reads go through one reused Il2Cpp buffer, so no bundle-sized Il2Cpp array is made.
    /// </summary>
    public sealed class GameStreamAdapter : Stream
    {
        private const int BufferSize = 1 << 20;

        [ThreadStatic]
        private static Il2CppStructArray<byte> SharedBuffer;

        private readonly Il2CppSystem.IO.Stream gameStream;
        private readonly long length;
        private long position;

        public GameStreamAdapter(Il2CppSystem.IO.Stream gameStream)
        {
            this.gameStream = gameStream;
            length = gameStream.Length;
            position = gameStream.Seek(0, Il2CppSystem.IO.SeekOrigin.Begin);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => position + offset,
                SeekOrigin.End => length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            position = gameStream.Seek(target, Il2CppSystem.IO.SeekOrigin.Begin);
            return position;
        }

        public override unsafe int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));

            Il2CppStructArray<byte> il2cppBuffer = SharedBuffer ??= new Il2CppStructArray<byte>(BufferSize);
            byte* source = (byte*)IntPtr.Add(il2cppBuffer.Pointer, 4 * IntPtr.Size).ToPointer();

            int read = gameStream.Read(il2cppBuffer, 0, Math.Min(count, BufferSize));
            if (read > 0)
            {
                fixed (byte* destination = &buffer[offset])
                {
                    Buffer.MemoryCopy(source, destination, count, read);
                }
                position += read;
            }
            return read;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Deliberately does not dispose the game's stream.
        protected override void Dispose(bool disposing) { }
    }
}
