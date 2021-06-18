using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Lz4;
using System.Diagnostics;
using AssetStudio;

// ported from https://github.com/HearthSim/UnityPack/
namespace rdbundle
{
    public static class BundleFile
    {
        private static readonly byte[] TYPE_STRINGS = UTF8Encoding.UTF8.GetBytes("AABB\0AnimationClip\0AnimationCurve\0AnimationState\0Array\0Base\0BitField\0bitset\0bool\0char\0ColorRGBA\0Component\0data\0deque\0double\0dynamic_array\0FastPropertyName\0first\0float\0Font\0GameObject\0Generic Mono\0GradientNEW\0GUID\0GUIStyle\0int\0list\0long long\0map\0Matrix4x4f\0MdFour\0MonoBehaviour\0MonoScript\0m_ByteSize\0m_Curve\0m_EditorClassIdentifier\0m_EditorHideFlags\0m_Enabled\0m_ExtensionPtr\0m_GameObject\0m_Index\0m_IsArray\0m_IsStatic\0m_MetaFlag\0m_Name\0m_ObjectHideFlags\0m_PrefabInternal\0m_PrefabParentObject\0m_Script\0m_StaticEditorFlags\0m_Type\0m_Version\0Object\0pair\0PPtr<Component>\0PPtr<GameObject>\0PPtr<Material>\0PPtr<MonoBehaviour>\0PPtr<MonoScript>\0PPtr<Object>\0PPtr<Prefab>\0PPtr<Sprite>\0PPtr<TextAsset>\0PPtr<Texture>\0PPtr<Texture2D>\0PPtr<Transform>\0Prefab\0Quaternionf\0Rectf\0RectInt\0RectOffset\0second\0set\0short\0size\0SInt16\0SInt32\0SInt64\0SInt8\0staticvector\0string\0TextAsset\0TextMesh\0Texture\0Texture2D\0Transform\0TypelessData\0UInt16\0UInt32\0UInt64\0UInt8\0unsigned int\0unsigned long long\0unsigned short\0vector\0Vector2f\0Vector3f\0Vector4f\0m_ScriptingClassIdentifier\0Gradient\0Gradient\0Type*\0int2_storage\0int3_storage\0BoundsInt\0");

        public enum BuildTarget
        {
            StandaloneOSX = 2,
            StandaloneWindows = 5,
            StandaloneLinux = 17,
            StandaloneWindows64 = 19,
            StandaloneLinux64 = 24,
            StandaloneLinuxUniversal = 25
        }

        public static void ReadBundleFile(string filename, out List<string> materials, out List<string> gameObjects)
        {
            using (var es = new EndianStream(File.OpenRead(filename), EndianType.BigEndian))
                ReadBundleStream(es, out materials, out gameObjects);
        }

        // returns list of materials
        public static void ReadBundleStream(EndianStream b_Stream, out List<string> materials, out List<string> gameObjects)
        {
            materials = null;
            gameObjects = null;
            var header = b_Stream.ReadStringToNull();
            if (header == "UnityFS")
            {
                var ver1 = b_Stream.ReadInt32();
                var ver2 = b_Stream.ReadStringToNull();
                var ver3 = b_Stream.ReadStringToNull();

                long bundleSize = ver1 < 6 ? b_Stream.ReadInt32() : b_Stream.ReadInt64();

                var cHdrSize = b_Stream.ReadInt32();
                var uHdrSize = b_Stream.ReadInt32();
                var hdrFlags = b_Stream.ReadInt32();
                var compression = hdrFlags & 0x3f;
                var eofMetadata = (hdrFlags & 0x80) != 0;
                long pos = 0;
                if (eofMetadata) {
                    pos = b_Stream.Position;
                    b_Stream.BaseStream.Seek(-cHdrSize, SeekOrigin.End);
                }
                var data = decompress(b_Stream.ReadBytes(cHdrSize), uHdrSize, compression);
                if (eofMetadata)
                    b_Stream.Position = pos;
                var hdr = new EndianStream(new MemoryStream(data), EndianType.BigEndian);
                var guid = hdr.ReadBytes(16);
                var numBlocks = hdr.ReadInt32();
                var blocks = new BlockInfo[numBlocks];
                for (int i = 0; i < numBlocks; i++) {
                    var uSize = hdr.ReadInt32();
                    var cSize = hdr.ReadInt32();
                    var flags = hdr.ReadInt16();
                    blocks[i] = new BlockInfo() { uSize = uSize, cSize = cSize, flags = flags};
                }
                var storage = new Storage(blocks, b_Stream);

                var numParts = hdr.ReadInt32();
                for (int i = 0; i < numParts; i++) {
                    var ofs = hdr.ReadInt64();
                    var size = hdr.ReadInt64();
                    var status = hdr.ReadInt32();
                    var name = hdr.ReadStringToNull();
                    var part = new BundlePart() { stream = new StreamPart(storage, ofs, size), name = name };
                    if (i == 0) //(status & 4) != 0)
                        LoadAssetFile(new StreamPart(storage, ofs, size), out materials, out gameObjects);
                }
            }
            else
                throw new Exception("Unknown header: " +
                    (header[0] > 'A' && header[0] < 'Z' ?
                        header.Substring(0, 8) :
                        string.Join(" ", header.Substring(0, 8).ToCharArray().Select(c => ((int)c).ToString("X2")))));
        }

        class FilePtr
        {
            public int FileID;
            public long PathID;
        }

        static object ReadValue(EndianStream es, TreeNode type)
        {
            object v;
            var t = type.type;
            switch (t)
            {
                case "bool":
                    v = es.ReadByte() != 0;
                    break;
                case "SInt8":
                    v = es.ReadSByte();
                    break;
                case "UInt8":
                    v = es.ReadByte();
                    break;
                case "SInt16":
                    v = es.ReadInt16();
                    break;
                case "UInt16":
                    v = es.ReadUInt16();
                    break;
                case "SInt32":
                case "int":
                    v = es.ReadInt32();
                    break;
                case "UInt32":
                case "unsigned int":
                    v = es.ReadUInt32();
                    break;
                case "SInt64":
                    v = es.ReadInt64();
                    break;
                case "UInt64":
                    v = es.ReadUInt64();
                    break;
                case "float":
                    es.AlignStream(4);
                    v = es.ReadSingle();
                    break;
                case "double":
                    es.AlignStream(4);
                    v = es.ReadDouble();
                    break;
                case "string":
                    var ssize = es.ReadInt32();
                    v = UTF8Encoding.UTF8.GetString(es.ReadBytes(ssize));
                    if (type.children[0].postAlign)
                        es.AlignStream(4);
                    break;
                default:
                    var firstChild = type.children.Any() ? type.children[0] : null;
                    if (type.isArray)
                        firstChild = type;
                    if (t.StartsWith("PPtr<")) {
                        v = new FilePtr() { FileID = es.ReadInt32(), PathID = es.ReadInt64() };
                    } else if (firstChild != null && firstChild.isArray) {
                        var size = es.ReadInt32();
                        var arrayType = firstChild.children[1];
                        if (arrayType.type == "char" || arrayType.type == "UInt8") {
                            v = es.ReadBytes(size);
                        } else {
                            var a = new object[size];
                            for (int i = 0; i < size; i++)
                                a[i] = ReadValue(es, arrayType);
                            v = a;
                        }
                        if (firstChild.postAlign)
                            es.AlignStream(4);
                    } else if (t == "pair") {
                        v = Tuple.Create<object, object>(ReadValue(es, type.children[0]), ReadValue(es, type.children[1]));
                    } else {
                        var o = new Dictionary<string, object>();
                        foreach (var child in type.children)
                            o.Add(child.name, ReadValue(es, child));
                        v = o;
                    }
                    break;
            }
            if (type.postAlign)
                es.AlignStream(4);
            return v;
        }

        static void LoadAssetFile(Stream s, out List<string> materials, out List<string> gameObjects)
        {
            var es = new EndianStream(s, EndianType.BigEndian);
            var metaSize = es.ReadUInt32();
            var fileSize = es.ReadUInt32();
            var format = es.ReadInt32();
            var dataOffset = es.ReadUInt32();

            if (format >= 9) {
                var endianness = es.ReadUInt32();
                if (endianness == 0)
                    es.endian = EndianType.LittleEndian;
            }

            var types = new TypeMeta();
            types.Load(es, format);

            var longObjIds = format >= 14 || (format >=7 && es.ReadInt32() != 0);

            var numObjs = es.ReadUInt32();
            var objs = new AssetObject[numObjs];
            for (uint i = 0; i < numObjs; i++) {
                if (format >= 14)
                    es.AlignStream(4);
                objs[i] = LoadObject(es, format, types);
            }
            materials = new List<string>();
            gameObjects = new List<string>();
            foreach (var obj in objs) {
                types.TypeTrees.TryGetValue(obj.ClassId, out TreeNode type);
                es.Position = dataOffset + obj.DataOfs;
                if (obj.ClassId == 21) {
                    es.Position = dataOffset + obj.DataOfs;
                    materials.Add(es.ReadAlignedString(es.ReadInt32()));
                }
                if (obj.ClassId == 1)
                {
                    var v = (Dictionary<string, object>)ReadValue(es, type);
                    gameObjects.Add((string)v["m_Name"]);
                }
                /*
                if (obj.ClassId == 1 || obj.ClassId == 21) {
                    var v = (Dictionary<string, object>)ReadValue(es, type);
                    Debug.WriteLine(type.type + " " + v["m_Name"]);
                }
                */
                /*
                if (type.children.Count != 0 && type.children[0].type == "string") {
                    es.Position = dataOffset + obj.DataOfs;
                    Debug.WriteLine(es.ReadAlignedString(es.ReadInt32()));
                }
                */
            }
            //Debug.WriteLine(type);
        }

        private class AssetObject
        {
            public UInt64 PathId;
            public uint DataOfs;
            public uint Size;
            public int TypeId, ClassId;
        }

        static AssetObject LoadObject(EndianStream es, int format, TypeMeta types)
        {
            var obj = new AssetObject();
            obj.PathId = es.ReadUInt64();
            obj.DataOfs = es.ReadUInt32();
            obj.Size = es.ReadUInt32();
            if (format < 17) {
                obj.TypeId = es.ReadInt32();
                obj.ClassId = es.ReadInt16();
            } else {
                obj.TypeId = es.ReadInt32();
                obj.ClassId = types.ClassIds[obj.TypeId];
            }
            if (format <= 16)
                es.ReadInt16();
            if (format == 15 || format == 16)
                es.ReadByte();
            return obj;
        }

        private class TreeNode
        {
            public int version;
            public bool isArray;
            public string type, name;
            public int size, index, flags;
            public List<TreeNode> children = new List<TreeNode>();
            public bool postAlign { get { return (flags & 0x4000) != 0; } }
        }
        
        static string GetTypeString(byte[] data, int ofs)
        {
            if (ofs < 0) {
                ofs &= 0x7ffffff;
                data = TYPE_STRINGS;
            }
            int i = ofs;
            while (data[i] != 0)
                i++;
            return UTF8Encoding.UTF8.GetString(data, ofs, i - ofs);
        }

        static TreeNode LoadTree(EndianStream es, int format)
        {
            var numNodes = es.ReadInt32();
            var dataSize = es.ReadInt32();
            var nodeData = es.ReadBytes(numNodes * 24);
            var data = es.ReadBytes(dataSize);
            var nes = new EndianStream(new MemoryStream(nodeData), es.endian);
            nes.endian = es.endian; // workaround bug EndianStream
            var parents = new List<TreeNode>();
            TreeNode top = null;
            for (uint i = 0; i < numNodes; i++)
            {
                var version = nes.ReadUInt16();
                var depth = nes.ReadByte();
                var cur = new TreeNode();
                parents.RemoveRange(depth, parents.Count - depth);
                if (parents.Any())
                    parents.Last().children.Add(cur);
                else
                    top = cur;
                parents.Add(cur);
                cur.version = version;
                cur.isArray = nes.ReadByte() != 0;
                cur.type = GetTypeString(data, nes.ReadInt32());
                cur.name = GetTypeString(data, nes.ReadInt32());
                cur.size = nes.ReadInt32();
                cur.index = nes.ReadInt32();
                cur.flags = nes.ReadInt32();
            }
            return top;
        }

        private class TypeMeta
        {
            public List<int> ClassIds;
            public Dictionary<int, byte[]> Hashes;
            public Dictionary<int, TreeNode> TypeTrees;
            public string version;
            public BuildTarget target;

            public void Load(EndianStream es, int format)
            {
                ClassIds = new List<int>();
                Hashes = new Dictionary<int, byte[]>();
                TypeTrees = new Dictionary<int, TreeNode>();

                version = es.ReadStringToNull();
                target = (BuildTarget)es.ReadInt32();
                Debug.WriteLine(version + " target " + target);
                if (format >= 13) {
                    var hasTypeTrees = es.ReadByte() != 0;
                    var num = es.ReadInt32();
                    for (int i = 0; i < num; i++) {
                        int classId = es.ReadInt32();
                        if (format >= 17) {
                            var unk0 = es.ReadByte();
                            var scriptId = es.ReadInt16();
                            if (classId == 114) {
                                if (scriptId >= 0)
                                    classId = -2 - scriptId;
                                else
                                    classId = -1;
                            }
                        }
                        ClassIds.Add(classId);
                        Hashes.Add(classId, es.ReadBytes(classId < 0 ? 32 : 16));
                        if (hasTypeTrees)
                            TypeTrees.Add(classId, LoadTree(es, format));
                    }
                }
            }
        }

        private class BlockInfo
        {
            public int uSize, cSize, flags;
        }

        private class Storage : Stream
        {
            private BlockInfo[] blocks;
            private EndianStream stream;
            private long pos;
            private long curStart, curEnd;
            private byte[] curData;
            private long baseOfs;
            
            public Storage(BlockInfo[] blocks, EndianStream stream)
            {
                this.blocks = blocks;
                this.stream = stream;
                baseOfs = stream.Position;
            }

            public override long Seek(long ofs, SeekOrigin org = SeekOrigin.Begin)
            {
                switch (org)
                {
                    case SeekOrigin.Begin:
                        pos = ofs;
                        break;
                    case SeekOrigin.Current:
                        pos += ofs;
                        break;
                    case SeekOrigin.End:
                        pos = Length + ofs;
                        break;
                }
                return pos;
            }

            public override int Read(byte[] buf, int bufOfs, int count)
            {
                for (int left = count;;) {
                    if (pos >= curStart && pos < curEnd) {
                        int n = curEnd - pos < left ? (int)(curEnd - pos) : left;
                        Array.Copy(curData, pos - curStart, buf, bufOfs, n);
                        pos += n;
                        bufOfs += n;
                        left -= n;
                        if (left == 0)
                            return count;
                    }
                    curStart = 0;
                    long blkOfs = 0;
                    curData = null;
                    for (int i = 0; i < blocks.Length; i++) {
                        int uSize = blocks[i].uSize;
                        if (pos < curStart + uSize) {
                            curEnd = curStart + uSize;
                            stream.Position = baseOfs + blkOfs;
                            curData = decompress(stream.ReadBytes(blocks[i].cSize), uSize, blocks[i].flags & 0x3f);
                            break;
                        }
                        blkOfs += blocks[i].cSize;
                    }
                    if (curData == null)
                        return count - left;
                }
            }
            public override long Length { get { long len = 0; for (int i = 0; i < blocks.Length; i++) len += blocks[i].uSize; return len; } }
            public override long Position { get { return pos; } set { Seek(value); } }
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override bool CanRead { get { return true; } }
            public override void Flush() { throw new NotImplementedException(); }
            public override void SetLength(long len) { throw new NotImplementedException(); }
            public override void Write(byte[] buf, int bufOfs, int count) { throw new NotImplementedException(); }
        }

        private class StreamPart : Stream
        {
            private Stream baseStream;
            private long baseOffset;
            private long size;
            private long pos;

            public StreamPart(Stream baseStream, long baseOffset, long size)
            {
                this.baseStream = baseStream;
                this.baseOffset = baseOffset;
                this.size = size;
                this.pos = 0;
            }

            public override long Seek(long ofs, SeekOrigin org = SeekOrigin.Begin)
            {
                switch (org)
                {
                    case SeekOrigin.Begin:
                        pos = ofs;
                        break;
                    case SeekOrigin.Current:
                        pos += ofs;
                        break;
                    case SeekOrigin.End:
                        pos = size;
                        break;
                }
                return pos;
            }

            public override int Read(byte[] buf, int bufOfs, int count)
            {
                if (pos >= size)
                    return 0;
                if (count > size - pos)
                    count = (int)(size - pos);
                baseStream.Position = pos + baseOffset;
                int n = baseStream.Read(buf, bufOfs, count);
                pos += n;
                return n;
            }
            public override long Length { get { return size; } }
            public override long Position { get { return pos; } set { Seek(value); } }
            public override bool CanSeek { get { return baseStream.CanSeek; } }
            public override bool CanWrite { get { return false; } }
            public override bool CanRead { get { return baseStream.CanRead; } }
            public override void Flush() { throw new NotImplementedException(); }
            public override void SetLength(long len) { throw new NotImplementedException(); }
            public override void Write(byte[] buf, int bufOfs, int count) { throw new NotImplementedException(); }
        }

        private class BundlePart
        {
            public Stream stream;
            public string name;
        }

        private static void lz4decompress(byte[] src, byte[] dst)
        {
            using (var inputStream = new MemoryStream(src))
            {
                var decoder = new Lz4DecoderStream(inputStream);

                for (int i = 0; ; )
                {
                    int nRead = decoder.Read(dst, i, dst.Length - i);
                    if (nRead == 0)
                        break;
                    i += nRead;
                }
            }
        }

        private static byte[] decompress(byte[] data, int uSize, int compression)
        {
            if (compression == 0)
                return data;
            if (compression == 1) {
                var dst = new byte[uSize];
                rdbundle.LzmaDec.LzmaDecode(data, dst);
                return dst;

                /*
                var c = new SharpCompress.Compressors.LZMA.Decoder();

                var inStream = new MemoryStream(data);

                byte[] prop = new byte[5];
                if (inStream.Read(prop, 0, 5) != 5)
                    throw new Exception("missing header");

                c.SetDecoderProperties(prop);

                c.Code(inStream, new MemoryStream(dst), -1, -1, null);
                return dst;
                */
            }
            if (compression == 2 || compression == 3) {
                var dst = new byte[uSize];
                lz4decompress(data, dst); 
                return dst;
            }
            throw new Exception("Unknown compression " + compression);
                
        }
    }
}
