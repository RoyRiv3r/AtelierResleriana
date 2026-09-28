using AtelierResleriana.Compression;
using AtelierResleriana.Unity;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace AtelierResleriana.Plugin.Localization
{
    /// <summary>
    /// Reads only the header and directory list of a UnityFS bundle: the few KB needed to
    /// decide whether the bundle holds anything to localize, without reading its data.
    /// Uses nothing but Seek and Read, so it never disturbs a stream it does not own.
    /// </summary>
    public static class BundleDirectoryReader
    {
        private const int HeaderPeekSize = 256;

        public static string[] ReadDirectoryPaths(Stream stream)
        {
            long length = stream.Length;

            byte[] head = new byte[(int)Math.Min(length, HeaderPeekSize)];
            stream.Seek(0, SeekOrigin.Begin);
            ReadExactly(stream, head, head.Length);

            int position = 0;
            string signature = ReadNullTerminatedString(head, ref position);
            if (signature != UnityFSFileHeader.Signature)
            {
                throw new InvalidDataException($"Invalid signature: {signature}");
            }

            uint version = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(position)); position += 4;
            ReadNullTerminatedString(head, ref position); // Player version.
            string engineVersionText = ReadNullTerminatedString(head, ref position);
            position += 8; // File size.
            uint compressedMetadataSize = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(position)); position += 4;
            uint uncompressedMetadataSize = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(position)); position += 4;
            uint flags = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(position)); position += 4;

            long metadataPosition;
            if ((flags & (uint)UnityFSFileFlags.BlocksInfoAtTheEnd) != 0)
            {
                metadataPosition = length - compressedMetadataSize;
            }
            else
            {
                // Same alignment rules as UnityFSFileReader.ReadMetadata.
                EngineVersion engineVersion = engineVersionText;
                if (engineVersion == new EngineVersion(0, 0, 0))
                {
                    engineVersion = new EngineVersion(2022, 3, 6, "f1");
                }

                int aligned = (position + 15) & ~15;
                if (version >= 7)
                {
                    position = aligned;
                }
                else if (engineVersion >= new EngineVersion(2019, 4, 0))
                {
                    bool allZeros = true;
                    for (int i = position; i < aligned && i < head.Length; i++)
                    {
                        allZeros &= head[i] == 0;
                    }
                    if (allZeros)
                    {
                        position = aligned;
                    }
                }
                metadataPosition = position;
            }

            byte[] compressedMetadata = new byte[compressedMetadataSize];
            stream.Seek(metadataPosition, SeekOrigin.Begin);
            ReadExactly(stream, compressedMetadata, compressedMetadata.Length);

            byte[] metadata = (UnityFSFileCompression)(flags & (uint)UnityFSFileFlags.CompressionTypeMask) switch
            {
                UnityFSFileCompression.None => compressedMetadata,
                UnityFSFileCompression.Lz4 or UnityFSFileCompression.Lz4hc => new Lz4CompressionAlgorithm().Decompress(compressedMetadata, (int)uncompressedMetadataSize),
                var other => throw new NotSupportedException($"Bundle metadata compression {other} is not supported.")
            };

            int offset = 16; // Uncompressed data hash.
            int blockCount = BinaryPrimitives.ReadInt32BigEndian(metadata.AsSpan(offset)); offset += 4;
            offset += blockCount * 10; // Uncompressed size, compressed size, flags.
            int directoryCount = BinaryPrimitives.ReadInt32BigEndian(metadata.AsSpan(offset)); offset += 4;

            string[] paths = new string[directoryCount];
            for (int i = 0; i < directoryCount; i++)
            {
                offset += 20; // Offset, size, flags.
                paths[i] = ReadNullTerminatedString(metadata, ref offset);
            }

            return paths;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Bundle stream ended after {total} of {count} bytes.");
                }
                total += read;
            }
        }

        private static string ReadNullTerminatedString(byte[] bytes, ref int position)
        {
            int end = Array.IndexOf(bytes, (byte)0, position);
            if (end < 0)
            {
                throw new InvalidDataException("Unterminated string in bundle header.");
            }
            string value = Encoding.ASCII.GetString(bytes, position, end - position);
            position = end + 1;
            return value;
        }
    }
}
