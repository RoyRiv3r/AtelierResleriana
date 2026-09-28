using System.IO;
using System.Text;
using Universal.Common;

namespace AtelierResleriana.Unity
{
    public class SerializedFileWriter
    {
        public const uint Version = 22U;

        public bool IsBigEndian { get; set; }
        public string UnityVersion { get; set; }
        public int TargetPlatform { get; set; }
        public bool EnableTypeTree { get; set; }

        public SerializedFileWriter() : this(new Options()) { }
        public SerializedFileWriter(Options options)
        {
            IsBigEndian = options.IsBigEndian;
            UnityVersion = options.UnityVersion;
            TargetPlatform = options.TargetPlatform;
            EnableTypeTree = options.EnableTypeTree;
        }

        public Stream Write(
            SerializedFileType[] types, 
            SerializedFileObject[] objects, 
            SerializedFileObjectReference[] scriptReferences,
            SerializedFileAssetReference[] assetReferences,
            SerializedFileType[] referenceTypes,
            string userInformation)
        {
            Stream stream = new MemoryStream();

            BinaryWriter binaryWriter = new BinaryWriter(stream, Encoding.UTF8, true, Endian.Big);

            binaryWriter.Write((uint)0); // Metadata size - unused by version 22.
            binaryWriter.Write((uint)0); // File size - unused by version 22.
            binaryWriter.Write(Version);
            binaryWriter.Write((uint)0); // Data offset - unused by version 22.
            binaryWriter.Write((bool)IsBigEndian);
            binaryWriter.Write(new byte[3]);

            long metadataSizeStreamPosition = stream.Position;
            binaryWriter.Write((uint)0); // Metadata size - actual.
            long fileSizeStreamPosition = stream.Position;
            binaryWriter.Write((long)0); // File size - actual.
            long dataOffsetStreamPosition = stream.Position;
            binaryWriter.Write((long)0); // Data offset.
            binaryWriter.Write((long)0); // Unknown value.

            long headerSize = stream.Position;

            if (!IsBigEndian)
            {
                binaryWriter = new BinaryWriter(stream, Encoding.UTF8, true, Endian.Little);
            }

            binaryWriter.WriteNullTerminatedString(UnityVersion, Encoding.UTF8);
            binaryWriter.Write((int)TargetPlatform);
            binaryWriter.Write((bool)EnableTypeTree);

            binaryWriter.Write((int)types.Count());
            foreach (SerializedFileType type in types)
            {
                WriteType(binaryWriter, type, false);
            }

            binaryWriter.Write((int)objects.Length);

            using Stream dataStream = new MemoryStream();
            using BinaryWriter dataStreamWriter = new BinaryWriter(dataStream, Encoding.UTF8, true, IsBigEndian ? Endian.Big : Endian.Little);

            // Create a dictionary to store offsets and sizes keyed by object
            var objectDataInfo = new Dictionary<SerializedFileObject, (long offset, uint size)>();

            // First pass: Write the object data to the data stream in DataIndex order.
            // Sorted once: re-sorting every object to find the last one made this quadratic
            // (seconds for a story bundle with ~18,000 objects).
            SerializedFileObject[] dataOrder = objects.OrderBy(o => o.DataIndex).ToArray();
            SerializedFileObject lastObject = dataOrder.LastOrDefault();
            foreach (var obj in dataOrder)
            {
                long offset = dataStream.Position;

                if (obj.RawData != null)
                {
                    // Unchanged object: copy its original bytes instead of re-encoding them.
                    dataStream.Write(obj.RawData, 0, obj.RawData.Length);
                }
                else
                {
                    SerializedFileType type = types[obj.TypeId];
                    SerializedObjectWriter serializedObjectWriter = new SerializedObjectWriter(new SerializedObjectWriter.Options()
                    {
                        IsBigEndian = IsBigEndian
                    });

                    serializedObjectWriter.Write(obj.SerializedObject, type.Node).CopyTo(dataStream);
                }
                uint size = (uint)(dataStream.Position - offset);

                // Store the offset and size for this object
                objectDataInfo[obj] = (offset, size);

                // Align for the next object, if not the last one
                if (obj != lastObject)
                {
                    dataStreamWriter.Align(8);
                }
            }

            // Second pass: Write object infos in the original input order
            foreach (var obj in objects)
            {
                var (offset, size) = objectDataInfo[obj];

                binaryWriter.Align(4);
                binaryWriter.Write((long)obj.PathId);
                binaryWriter.Write((long)offset);
                binaryWriter.Write((uint)size);
                binaryWriter.Write((int)obj.TypeId);
            }

            /*
            binaryWriter.Write((int)objects.Length);

            using Stream dataStream = new MemoryStream();
            using BinaryWriter dataStreamWriter = new BinaryWriter(dataStream, Encoding.UTF8, true, IsBigEndian ? Endian.Big : Endian.Little);
            foreach (var @object in objects)
            {
                long offset = dataStream.Position;
                SerializedObjectWriter serializedObjectWriter = new SerializedObjectWriter(new SerializedObjectWriter.Options()
                {
                    IsBigEndian = IsBigEndian
                });
                SerializedFileType type = types[@object.TypeId];
                serializedObjectWriter.Write(@object.SerializedObject, type.Node).CopyTo(dataStream);
                uint size = (uint)(dataStream.Position - offset);
                if (@object != objects[objects.Length - 1])
                {
                    dataStreamWriter.Align(8);
                }

                binaryWriter.Align(4);
                binaryWriter.Write((long)@object.PathId);
                binaryWriter.Write((long)offset);
                binaryWriter.Write((uint)size);
                binaryWriter.Write((int)@object.TypeId);

                //Console.WriteLine($"[W] {offset} ({size}) [{@object.SerializedObject.ClassId}]: {@object.PathId}");
            }
            */

            binaryWriter.Write((int)scriptReferences.Count());
            foreach (var scriptReference in scriptReferences)
            {
                binaryWriter.Write((int)scriptReference.Index);
                binaryWriter.Align(4);
                binaryWriter.Write((long)scriptReference.PathId);
            }

            binaryWriter.Write((int)assetReferences.Count());
            foreach (var assetReference in assetReferences)
            {
                binaryWriter.WriteNullTerminatedString(string.Empty, Encoding.UTF8);
                binaryWriter.Write(assetReference.Guid.Value.ToByteArray());
                binaryWriter.Write((int)assetReference.Type);
                binaryWriter.WriteNullTerminatedString(assetReference.Path, Encoding.UTF8);
            }

            binaryWriter.Write((int)referenceTypes.Count());
            foreach (var referenceType in referenceTypes)
            {
                WriteType(binaryWriter, referenceType, true);
            }

            binaryWriter.WriteNullTerminatedString(userInformation, Encoding.UTF8);
            uint metadataSize = (uint)(stream.Position - headerSize);

            binaryWriter.Align(16);

            long dataOffset = stream.Position;

            dataStream.Seek(0, SeekOrigin.Begin);
            dataStream.CopyTo(stream);

            long fileSize = stream.Length;

            binaryWriter = new BinaryWriter(stream, Encoding.UTF8, true, Endian.Big);

            stream.Position = metadataSizeStreamPosition;
            binaryWriter.Write((uint)metadataSize);
            stream.Position = dataOffsetStreamPosition;
            binaryWriter.Write((long)dataOffset);
            stream.Position = fileSizeStreamPosition;
            binaryWriter.Write((long)fileSize);

            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        internal void WriteType(BinaryWriter binaryWriter, SerializedFileType type, bool isRefType)
        {
            binaryWriter.Write((int)type.ClassId);
            binaryWriter.Write((bool)type.IsStrippedType);
            binaryWriter.Write((short)type.ScriptTypeIndex);

            if ((isRefType && type.ScriptTypeIndex >= 0) ||
                (type.ClassId < 0) ||
                (type.ClassId == 114))
            {
                binaryWriter.Write(type.ScriptId ?? new byte[16]);
            }
            binaryWriter.Write(type.OldTypeHash ?? new byte[16]);

            if (EnableTypeTree)
            {
                WriteTypeTreeNodeBlob(binaryWriter, type.Node);

                if (isRefType)
                {
                    binaryWriter.WriteNullTerminatedString(type.ClassName ?? "", Encoding.UTF8);
                    binaryWriter.WriteNullTerminatedString(type.Namespace ?? "", Encoding.UTF8);
                    binaryWriter.WriteNullTerminatedString(type.AssemblyName ?? "", Encoding.UTF8);
                }
                else
                {
                    // Ensure we have a valid dependencies array
                    int[] dependencies = type.Dependencies ?? Array.Empty<int>();

                    binaryWriter.Write((int)dependencies.Length);
                    foreach (int dependency in dependencies)
                    {
                        binaryWriter.Write((int)dependency);
                    }
                }
            }
        }

        internal void WriteTypeTreeNodeBlob(BinaryWriter binaryWriter, SerializedFileTypeTreeNode node)
        {
            // First, collect all nodes in pre-order traversal
            var nodes = new List<SerializedFileTypeTreeNode>();
            CollectNodes(node, nodes);

            // Create string buffer manager with common strings
            var commonStrings = CommonStringOffsets.Value;

            var stringManager = new StringBufferManager(commonStrings);

            // Write node count
            binaryWriter.Write((int)nodes.Count);

            // First pass to register all strings with the string manager
            foreach (var n in nodes)
            {
                stringManager.GetStringOffset(n.Type);
                stringManager.GetStringOffset(n.Name);
            }

            // Now calculate and write string buffer size
            binaryWriter.Write((int)stringManager.GetBufferSize());

            // Write node data
            foreach (var n in nodes)
            {
                binaryWriter.Write((short)n.Version);
                binaryWriter.Write((byte)n.Level);
                binaryWriter.Write((byte)n.TypeFlags);
                binaryWriter.Write(stringManager.GetStringOffset(n.Type));
                binaryWriter.Write(stringManager.GetStringOffset(n.Name));
                binaryWriter.Write((int)n.Size);
                binaryWriter.Write((int)(n.Index ?? 0));
                binaryWriter.Write((int)n.MetaFlag);
                binaryWriter.Write((ulong)0); // RefTypeHash for Version >= 19
            }

            // Write string buffer
            foreach (var strOffset in stringManager.GetBufferStrings().OrderBy(x => x.Value))
            {
                binaryWriter.WriteNullTerminatedString(strOffset.Key, Encoding.UTF8);
            }
        }

        // Offsets of Unity's common strings: each is the running total of the UTF-8 lengths (plus
        // terminator) of the strings before it. Computed once; it used to be recomputed from
        // scratch for every string of every type, which made writing a file's types quadratic.
        // Read-only: StringBufferManager only looks strings up in it.
        private static readonly System.Lazy<Dictionary<string, uint>> CommonStringOffsets = new System.Lazy<Dictionary<string, uint>>(() =>
        {
            var offsets = new Dictionary<string, uint>();
            uint offset = 0;
            foreach (string str in TypePackage.Instance.TypeTreeBlob.CommonString.GetStrings(TypePackage.Instance.TypeTreeBlob.StringBuffer))
            {
                offsets.Add(str, offset);
                offset += (uint)Encoding.UTF8.GetByteCount(str) + 1;
            }
            return offsets;
        });

        private void CollectNodes(SerializedFileTypeTreeNode node, List<SerializedFileTypeTreeNode> nodes)
        {
            nodes.Add(node);
            foreach (var child in node.Children)
            {
                CollectNodes(child, nodes);
            }
        }

        public class Options
        {
            public bool IsBigEndian { get; set; } = false;
            public string UnityVersion { get; set; } = "0.0.0";
            public int TargetPlatform { get; set; } = 19;
            public bool EnableTypeTree { get; set; } = true;
        }

        internal class StringBufferManager
        {
            private Dictionary<string, uint> stringToOffset = new Dictionary<string, uint>();
            private Dictionary<string, uint> commonStringToOffset;
            private uint currentOffset = 0;

            public StringBufferManager(Dictionary<string, uint> commonStrings)
            {
                this.commonStringToOffset = commonStrings ?? new Dictionary<string, uint>();
            }

            public uint GetStringOffset(string str)
            {
                if (string.IsNullOrEmpty(str))
                {
                    return 0;
                }

                // First check if it's a common string
                if (commonStringToOffset.TryGetValue(str, out uint commonOffset))
                {
                    // Set highest bit to 1 to indicate it's a common string
                    return commonOffset | 0x80000000;
                }

                // If not found in common strings, get or create offset in string buffer
                if (!stringToOffset.TryGetValue(str, out uint offset))
                {
                    offset = currentOffset;
                    stringToOffset[str] = offset;
                    currentOffset += (uint)Encoding.UTF8.GetByteCount(str) + 1; // +1 for null terminator
                }

                return offset;
            }

            public IEnumerable<KeyValuePair<string, uint>> GetBufferStrings()
            {
                return stringToOffset;
            }

            public uint GetBufferSize()
            {
                return currentOffset;
            }
        }

        public class SerializedFileObject
        {
            public long PathId { get; set; }
            public int TypeId { get; set; }
            public int DataIndex { get; set; }
            public SerializedObject SerializedObject { get; set; }
            /// <summary>Original object bytes, written as-is when set (SerializedObject is then ignored).</summary>
            public byte[] RawData { get; set; }
        }
    }
}
