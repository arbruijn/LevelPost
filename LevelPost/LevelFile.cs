using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace LevelPost
{
    enum VT
    {
        String = 0,
        Bool = 1,
        Byte = 2,
        UInt = 7,
        Int = 8,
        Float = 11,
        Guid = 13,
        Vector2 = 14,
        Vector3 = 15,
        Vector4 = 16,
        Color = 17,
        Quaternion = 18,
        Enum = 19,
        Object = 20,
        Vector3b = 21,
        Matrix4x4 = 22,
        Color32 = 23,
        BoneWeight = 24,

        CmdFlag = 256,
        CmdDone = 0 | CmdFlag,
        CmdCreateAssetFile = 1 | CmdFlag,
        CmdAddAssetToAssetFile = 2 | CmdFlag,
        CmdInitializeGameManager = 3 | CmdFlag,
        CmdCreateGameObject = 4 | CmdFlag,
        CmdTransformSetParent = 5 | CmdFlag,
        CmdGameObjectSetName = 6 | CmdFlag,
        CmdGameObjectSetTag = 7 | CmdFlag,
        CmdGameObjectSetLayer = 8 | CmdFlag,
        CmdGameObjectAddComponent = 9 | CmdFlag,
        CmdGameObjectSetComponentProperty = 10 | CmdFlag,
        CmdAssetRegisterMaterial = 11 | CmdFlag,
        CmdFindPrefabReference = 12 | CmdFlag,
        CmdInstantiatePrefab = 13 | CmdFlag,
        CmdGetComponentAtRuntime = 14 | CmdFlag,
        CmdSaveAsset = 15 | CmdFlag,
        CmdLoadAssetBundle = 16 | CmdFlag,
        CmdLoadAssetFromAssetBundle = 17 | CmdFlag,
        CmdCreateMaterial = 18 | CmdFlag,
        CmdMaterialSetColor = 21 | CmdFlag,
        CmdMaterialSetTexture = 29 | CmdFlag,
        CmdCreateTexture2D = 33 | CmdFlag,

        FromAsset = 62,
        Unknown = 63,

        ExistingObjectFlag = 32,
        ObjectFlag = 64,
        SegmentLightInfo = ObjectFlag + 1,
        SegmentReflectionProbeInfo,
        LevelData__PortalDoorConnection,
        LevelData__SpawnPoint,
        LevelGeometry,
        Mesh,
        PortalPolygonData,
        PortalData,
        SegmentData,
        BSPTreeNode,
        AABB,
        AABBTreeNode,
        ChunkData,
        ChunkPortal,
        PortalGeomTriangle,
        PortalGeomData,
        PathDistanceData,

        ArrayFlag = 128,
        StringArray = String | ArrayFlag,
        IntArray = Int | ArrayFlag,
        IntArrayArray = 255,
        FloatArray = Float | ArrayFlag,
        Vector2Array = Vector2 | ArrayFlag,
        Vector3Array = Vector3 | ArrayFlag,
        Vector4Array = Vector4 | ArrayFlag,
        ColorArray = Color | ArrayFlag,
        Color32Array = Color32 | ArrayFlag,
        BoneWeightArray = BoneWeight | ArrayFlag,
        Matrix4x4Array = Matrix4x4 | ArrayFlag,

        SegmentLightInfoArray = SegmentLightInfo | ArrayFlag,
        SegmentReflectionProbeInfoArray = SegmentReflectionProbeInfo | ArrayFlag,
        //PortalDoorConnectionArray = PortalDoorConnection | ArrayFlag,
        //SpawnPointArray = SpawnPoint | ArrayFlag,
        PortalPolygonDataArray = PortalPolygonData | ArrayFlag,
        PortalDataArray = PortalData | ArrayFlag,
        SegmentDataArray = SegmentData | ArrayFlag,
        BSPTreeNodeArray = BSPTreeNode | ArrayFlag,
        AABBTreeNodeArray = AABBTreeNode | ArrayFlag,
        ChunkDataArray = ChunkData | ArrayFlag,
        ChunkPortalArray = ChunkPortal | ArrayFlag,
        PortalGeomTriangleArray = PortalGeomTriangle | ArrayFlag,
        PortalGeomDataArray = PortalGeomData | ArrayFlag,
        PathDistanceDataArray = PathDistanceData | ArrayFlag,

        // enums
        SegmentLightType = Int,
        SegmentReflectionProbeType = Int,
        PathfindingType = Int,
        ExitSegmentType = Int
    }

    public class Vector3
    {
        public float x, y, z;
    }

    public class Vector4
    {
        public float x, y, z, w;
    }

    public class Level
    {
        public int version;
        public List<object[]> cmds;
    }

    // A level file contains a set of commands to create/manipulate Unity objects
    static class LevelFile
    {
        private static readonly Dictionary<VT, string[]> fieldNames = new Dictionary<VT, string[]>
        {
            { VT.SegmentLightInfo, new [] { "lightType", "segIdx" } },
            { VT.SegmentReflectionProbeInfo, new [] { "probeType", "segIdx" } },
            { VT.LevelData__PortalDoorConnection, new [] { "portalIdx" } },
            { VT.LevelData__SpawnPoint, new [] { "pos", "orient", "seg", "team_mask" } },
            { VT.PortalPolygonData, new [] { "normal", "planeEqD", "vertIdxs" } },
            { VT.PortalData, new [] { "primSeg", "primSide", "secSeg", "secSide", "polygons" } },
            { VT.SegmentData, new [] { "vertIdxs", "center", "minPos", "maxPos", "sidePlaneEq", "portals", "chunkIdx", "decalFlags", "doorFlags", "dark", "pathfinding", "exitSeg", "deformHeights", "warpDestSegs" } },
            { VT.BSPTreeNode, new [] { "plantEq", "backNodeIdx", "frontNodeIdx" } },
            { VT.AABB, new [] { "min", "max" } },
            { VT.AABBTreeNode, new [] { "bounds", "minChild", "maxChild", "seg" } },
            { VT.ChunkData, new [] { "portalIdxs", "segIdxs", "isEnergy" } },
            { VT.ChunkPortal, new [] { "num", "chunk", "seg", "side", "connectedChunk", "connectedPortal", "portalGeom" } },
            { VT.PortalGeomTriangle, new [] { "firstVertIdx" } },
            { VT.PortalGeomData, new [] { "numTri", "startIdx" } },
            { VT.PathDistanceData, new [] { "dist", "pathLen", "secSeg", "secLastSeg" } },
            { VT.LevelGeometry, new [] { "name", "file", "segments", "portals", "segVerts", "segBSPIdx", "segBSPData", "segAABBTree", "chunks", "chunkPortals", "portalVerts", "portalTris", "portalData", "cmText", "segSegVis", "pathDist", "geomHash", "robotSpawnHash" } },

            { VT.Mesh, new [] { "name", "verts", "uv", "uv2", "uv3", "norms", "tangs", "colors", "colors32", "boneWeights", "bindposes", "tris" } },

            { VT.CmdCreateAssetFile, new string[] { "path", "newFileId" } }, // does nothing
            { VT.CmdAddAssetToAssetFile, new string[] { "fileId", "newAssetId", "type" }},
            { VT.CmdInitializeGameManager, new string[] { "name", "levelDataId" } },
            { VT.CmdCreateGameObject, new string[] { "newObjId", "newTransId" } },
            { VT.CmdTransformSetParent, new string[] { "transId", "parentTransId" } },
            { VT.CmdGameObjectSetName, new string[] { "objId", "name" } },
            { VT.CmdGameObjectSetTag, new string[] { "objId", "tag" } },
            { VT.CmdGameObjectSetLayer, new string[] { "objId", "layer" } },
            { VT.CmdGameObjectAddComponent, new string[] { "objId", "newCompId", "type" } },
            { VT.CmdGameObjectSetComponentProperty, new string[] { "compId", "propName", "map", "array", "value" } },
            { VT.CmdAssetRegisterMaterial, new string[] { "newMatId", "geomType", "name" } },
            { VT.CmdFindPrefabReference, new string[] { "name", "newPrefabId" } },
            { VT.CmdInstantiatePrefab, new string[] { "prefabId", "newObjId", "newTransId" } },
            { VT.CmdGetComponentAtRuntime, new string[] { "alsoChild", "name", "objId", "newCompId" } },
            { VT.CmdSaveAsset, new string[] { "id", "value" } },
            { VT.CmdLoadAssetBundle, new string[] { "dir", "file", "newBundleId" } },
            { VT.CmdLoadAssetFromAssetBundle, new string[] { "name", "bundleId", "newObjId" } },
            { VT.CmdCreateMaterial, new string[] { "newMatId", "shaderId", "gpuInst", "texId", "texOfs", "texScale", "queue", "kws", "name" } },
            { VT.CmdMaterialSetTexture, new string[] { "matId", "propName", "texId" } },
            { VT.CmdMaterialSetColor, new string[] { "matId", "propName", "color" } },
            { VT.CmdCreateTexture2D, new string[] { "newTexId", "width", "height", "fmt", "mipmap", "filter", "name", "pixels" } },
            { VT.CmdDone, new string[] {} }
        };

        private static readonly Dictionary<VT, VT[]> objTypes = new Dictionary<VT, VT[]>
        {
            { VT.SegmentLightInfo, new VT[] { VT.SegmentLightType, VT.Int, VT.Guid } },
            { VT.SegmentReflectionProbeInfo, new VT[] { VT.SegmentReflectionProbeType, VT.Int, VT.Guid } },
            { VT.LevelData__PortalDoorConnection, new VT[] { VT.Int, VT.Guid } },
            { VT.LevelData__SpawnPoint, new VT[] { VT.Vector3, VT.Quaternion, VT.Int, VT.Int } },
            { VT.PortalPolygonData, new VT[] { VT.Vector3, VT.Float, VT.IntArray } },
            { VT.PortalData, new VT[] { VT.Int, VT.Int, VT.Int, VT.Int, VT.PortalPolygonDataArray } },
            { VT.SegmentData, new VT[] { VT.IntArray, VT.Vector3, VT.Vector3, VT.Vector3, VT.Vector4Array,
                VT.IntArray, VT.Int, VT.UInt, VT.UInt, VT.Bool, VT.PathfindingType, VT.ExitSegmentType,
                VT.FloatArray, VT.IntArray} },
            { VT.BSPTreeNode, new VT[] { VT.Vector4, VT.Int, VT.Int } },
            { VT.AABB, new VT[] { VT.Vector3, VT.Vector3 } },
            { VT.AABBTreeNode, new VT[] { VT.AABB, VT.Int, VT.Int, VT.Int } },
            { VT.ChunkData, new VT[] { VT.IntArray, VT.IntArray, VT.Bool } },
            { VT.ChunkPortal, new VT[] { VT.Int, VT.Int, VT.Int,
                VT.Int, VT.Int, VT.Int, VT.Int } },
            { VT.PortalGeomTriangle, new VT[] { VT.Int } },
            { VT.PortalGeomData, new VT[] { VT.Int, VT.Int } },
            { VT.PathDistanceData, new VT[] { VT.Float, VT.Int, VT.Int, VT.Int } },
            { VT.LevelGeometry, new VT[] { VT.String, VT.String,
                VT.SegmentDataArray, VT.PortalDataArray, VT.Vector3Array, VT.IntArray,
                VT.BSPTreeNodeArray, VT.AABBTreeNodeArray, VT.ChunkDataArray, VT.ChunkPortalArray,
                VT.Vector3Array, VT.PortalGeomTriangleArray, VT.PortalGeomDataArray,
                VT.String, VT.IntArray, VT.PathDistanceDataArray, VT.String, VT.String } },
             /*{VT.Mesh, new VT[] { VT.String, VT.Vector3Array, VT.Vector2Array, VT.Vector2Array, VT.Vector2Array,
                VT.Vector3Array, VT.Vector4Array, VT.ColorArray, VT.IntArrayArray }},*/

            { VT.Vector2, new VT[] { VT.Float, VT.Float } },
            { VT.Vector3, new VT[] { VT.Float, VT.Float, VT.Float } },
            { VT.Vector3b, new VT[] { VT.Float, VT.Float, VT.Float } },
            { VT.Vector4, new VT[] { VT.Float, VT.Float, VT.Float, VT.Float } },
            { VT.Color, new VT[] { VT.Float, VT.Float, VT.Float, VT.Float } },
            { VT.Quaternion, new VT[] { VT.Float, VT.Float, VT.Float, VT.Float } },
            { VT.Matrix4x4, new VT[] { VT.Vector4, VT.Vector4, VT.Vector4, VT.Vector4 } },
            { VT.BoneWeight, new VT[] { VT.Int, VT.Int, VT.Int, VT.Int, VT.Float, VT.Float, VT.Float, VT.Float } },

            { VT.CmdCreateAssetFile, new VT[] { VT.String, VT.Guid } },
            { VT.CmdAddAssetToAssetFile, new VT[] { VT.Guid, VT.Guid, VT.String }},
            { VT.CmdInitializeGameManager, new VT[] { VT.String, VT.Guid } },
            { VT.CmdCreateGameObject, new VT[] { VT.Guid, VT.Guid } },
            { VT.CmdTransformSetParent, new VT[] { VT.Guid, VT.Guid } },
            { VT.CmdGameObjectSetName, new VT[] { VT.Guid, VT.String } },
            { VT.CmdGameObjectSetTag, new VT[] { VT.Guid, VT.String } },
            { VT.CmdGameObjectSetLayer, new VT[] { VT.Guid, VT.Int } },
            { VT.CmdGameObjectAddComponent, new VT[] { VT.Guid, VT.Guid, VT.String } },
            { VT.CmdGameObjectSetComponentProperty, new VT[] { VT.Guid, VT.String, VT.Byte, VT.Byte, VT.Unknown } },
            { VT.CmdAssetRegisterMaterial, new VT[] { VT.Guid, VT.Int, VT.String } },
            { VT.CmdFindPrefabReference, new VT[] { VT.String, VT.Guid } },
            { VT.CmdInstantiatePrefab, new VT[] { VT.Guid, VT.Guid, VT.Guid } },
            { VT.CmdGetComponentAtRuntime, new VT[] { VT.Bool, VT.String, VT.Guid, VT.Guid } },
            { VT.CmdSaveAsset, new VT[] { VT.Guid, VT.FromAsset } },
            { VT.CmdLoadAssetBundle, new VT[] { VT.String, VT.String, VT.Guid } },
            { VT.CmdLoadAssetFromAssetBundle, new VT[] { VT.String, VT.Guid, VT.Guid } },
            { VT.CmdCreateMaterial, new VT[] { VT.Guid, VT.Guid, VT.Color, VT.Bool, VT.Guid, VT.Vector2, VT.Vector2, VT.Int, VT.StringArray, VT.String } },
            { VT.CmdMaterialSetTexture, new VT[] { VT.Guid, VT.String, VT.Guid } },
            { VT.CmdMaterialSetColor, new VT[] { VT.Guid, VT.String, VT.Color } },
            { VT.CmdCreateTexture2D, new VT[] { VT.Guid, VT.Int, VT.Int, VT.String, VT.Bool, VT.String, VT.String, VT.Color32Array } },
            { VT.CmdDone, new VT[] {} }
            
        };

        private static readonly Dictionary<Type, VT> typeVT = new Dictionary<Type, VT> {
            { typeof(string), VT.String },
            { typeof(bool), VT.Bool },
            { typeof(byte), VT.Byte },
            { typeof(uint), VT.UInt },
            { typeof(int), VT.Int },
            { typeof(float), VT.Float },
            { typeof(Guid), VT.Guid },
            { typeof(Vector3), VT.Vector3 },
            { typeof(Vector4), VT.Vector4 }
        };

        private static string ReadString(Stream s)
        {
            int n = ReadInt32(s);
            return n == -1 ? null : UTF8Encoding.UTF8.GetString(ReadBytes(s, n));
        }

        private class FieldStream
        {
            public Stream stream;
            public int version;
        
            private object ReadMesh()
            {
                object[] ret = new object[12 + 1];

                int flags;
                if (version == 3)
                {
                    flags = 1;
                }
                else
                {
                    flags = ReadInt32(stream);
                }
            
                ret[0] = VT.Mesh;
                ret[1] = ReadString(stream);
                ret[2] = ReadField(VT.Vector3Array);
                ret[3] = ReadField(VT.Vector2Array);
                ret[4] = ReadField(VT.Vector2Array);
                ret[5] = ReadField(VT.Vector2Array);
                ret[6] = ReadField(VT.Vector3Array);
                ret[7] = ReadField(VT.Vector4Array);
                ret[8] = (flags & 1) != 0 ? ReadField(VT.ColorArray) : null;
                ret[9] = (flags & 2) != 0 ? ReadField(VT.Color32Array) : null;
                ret[10] = version >= 4 ? ReadField(VT.BoneWeightArray) : null;
                ret[11] = version >= 4 ? ReadField(VT.Matrix4x4Array) : null;
                ret[12] = ReadField(VT.IntArrayArray);
                return ret;
            }

            private void WriteMesh(object[] val)
            {
                int flags;
                if (version == 3)
                {
                    flags = 1;
                }
                else
                {
                    flags = (val[8] != null && ((object[])val[8]).Length != 0 ? 1 :0) +
                        (val[9] != null && ((object[])val[9]).Length != 0 ? 2 : 0);
                    WriteInt32(stream, flags);
                }

                WriteString(stream, (string)val[1]);
                WriteField(VT.Vector3Array, val[2]);
                WriteField(VT.Vector2Array, val[3]);
                WriteField(VT.Vector2Array, val[4]);
                WriteField(VT.Vector2Array, val[5]);
                WriteField(VT.Vector3Array, val[6]);
                WriteField(VT.Vector4Array, val[7]);
                if ((flags & 1) != 0)
                    WriteField(VT.ColorArray, val[8]);
                if ((flags & 2) != 0)
                    WriteField(VT.Color32Array, val[9]);
                if (version >= 4)
                {
                    WriteField(VT.BoneWeightArray, val[10]);
                    WriteField(VT.Matrix4x4Array, val[11]);
                }
                WriteField(VT.IntArrayArray, val[12]);
            }

            public object ReadField(VT type)
            {
                if ((type & VT.ArrayFlag) != 0) {
                    int n = ReadInt32(stream);
                    if (n == -1)
                        return null;
                    if (type == VT.Color32Array)
                        return ReadBytes(stream, n * 4);
                    var arr = new object[n + 1];
                    arr[0] = type;
                    type = type == VT.IntArrayArray ? VT.IntArray : (type & ~VT.ArrayFlag);
                    for (int i = 1; i <= n; i++)
                        arr[i] = ReadField(type);
                    return arr;
                }
                switch (type)
                {
                    case VT.Guid:
                        return new Guid(ReadBytes(stream, 16));
                    case VT.Int:
                        return ReadInt32(stream);
                    case VT.UInt:
                        return ReadUInt32(stream);
                    case VT.Float:
                        return ReadFloat(stream);
                    case VT.String:
                        return ReadString(stream);
                    case VT.Bool:
                        return stream.ReadByte() != 0;
                    case VT.Byte:
                        return (byte)stream.ReadByte();
                    case VT.Unknown:
                        VT tp = (VT)stream.ReadByte();
                        VT stp = tp & ~VT.ArrayFlag;
                        VT atp = tp & VT.ArrayFlag;

                        if (stp == VT.Enum) {
                            var enumName = ReadString(stream);
                            var enumval = new object[3];
                            enumval[0] = VT.Enum | atp;
                            enumval[1] = ReadField(VT.Int | atp);
                            enumval[2] = enumName;
                            return enumval;
                        }
                        if (stp == VT.Object) {
                            string typeName = ReadString(stream);
                            if (typeName.Contains("+"))
                                typeName = typeName.Replace("+", "__");
                            tp = ((VT)Enum.Parse(typeof(VT), typeName)) | atp;
                        }
                        return ReadField(tp);
                    case VT.Vector3:
                        var buf3 = ReadBytes(stream, sizeof(float) * 3);
                        return new Vector3() { x = BitConverter.ToSingle(buf3, 0), y = BitConverter.ToSingle(buf3, 4),
                            z = BitConverter.ToSingle(buf3, 8) };
                    case VT.Vector4:
                        var buf = ReadBytes(stream, sizeof(float) * 4);
                        return new Vector4() { x = BitConverter.ToSingle(buf, 0), y = BitConverter.ToSingle(buf, 4),
                                z = BitConverter.ToSingle(buf, 8), w = BitConverter.ToSingle(buf, 12) };
                    case VT.Mesh | VT.ObjectFlag:
                        return stream.ReadByte() == 0 ? null : ReadMesh();
                    case VT.Mesh | VT.ObjectFlag | VT.ExistingObjectFlag:
                        return ReadMesh();
                    default:
                        if ((type & VT.ObjectFlag) != 0) {
                            if ((type & VT.ExistingObjectFlag) == 0) {
                                int exists = stream.ReadByte();
                                if (exists == 0)
                                    return null;
                            } else
                                type &= ~VT.ExistingObjectFlag;
                        }
                        if (!objTypes.TryGetValue(type, out VT[] fldTypes))
                            throw new Exception("Unknown type " + type);

                        object[] ret = new object[fldTypes.Length + 1];
                        ret[0] = type;
                        for (int l = fldTypes.Length, i = 0; i < l; i++)
                        {
                            ret[i + 1] = ReadField(fldTypes[i]);
                        }
                        return ret;
                }
            }

            public void WriteField(VT type, object val)
            {
                if ((type & VT.ArrayFlag) != 0) {
                    if (val == null) {
                        WriteInt32(stream, -1);
                        return;
                    }
                    if (type == VT.Color32Array && val is byte[] bs)
                    {
                        WriteInt32(stream, bs.Length >> 2);
                        WriteBytes(stream, bs);
                    }
                    else
                    {
                        object[] a = (object[])val;
                        int n = a.Length - 1;
                        WriteInt32(stream, n);
                        type = type == VT.IntArrayArray ? VT.IntArray : (type & ~VT.ArrayFlag);
                        for (int i = 1; i <= n; i++)
                            WriteField(type, a[i]);
                    }
                    return;
                }
                switch (type)
                {
                    case VT.Guid:
                        WriteBytes(stream, ((Guid)val).ToByteArray());
                        return;
                    case VT.Int:
                        WriteInt32(stream, (Int32)val);
                        return;
                    case VT.UInt:
                        WriteUInt32(stream, (UInt32)val);
                        return;
                    case VT.Float:
                        WriteFloat(stream, (float)val);
                        return;
                    case VT.String:
                        WriteString(stream, (string)val);
                        return;
                    case VT.Bool:
                        stream.WriteByte((bool)val ? (byte)1 : (byte)0);
                        return;
                    case VT.Byte:
                        stream.WriteByte((byte)val);
                        return;
                    case VT.Vector3:
                        Vector3 v3 = (Vector3)val;
                        WriteBytes(stream, BitConverter.GetBytes(v3.x));
                        WriteBytes(stream, BitConverter.GetBytes(v3.y));
                        WriteBytes(stream, BitConverter.GetBytes(v3.z));
                        return;
                    case VT.Vector4:
                        Vector4 v4 = (Vector4)val;
                        WriteBytes(stream, BitConverter.GetBytes(v4.x));
                        WriteBytes(stream, BitConverter.GetBytes(v4.y));
                        WriteBytes(stream, BitConverter.GetBytes(v4.z));
                        WriteBytes(stream, BitConverter.GetBytes(v4.w));
                        return;
                    case VT.Unknown:
                        VT valtp;
                        if (val is object[] obj) {
                            valtp = (VT)obj[0];
                            if ((valtp & VT.ObjectFlag) != 0) {
                                stream.WriteByte((byte)(VT.Object | (valtp & VT.ArrayFlag)));
                                WriteString(stream, Enum.GetName(typeof(VT), valtp & ~VT.ArrayFlag).Replace("__", "+"));
                            } else if ((valtp & ~VT.ArrayFlag) == VT.Enum) {
                                stream.WriteByte((byte)valtp);
                                WriteString(stream, (string)obj[2]);
                                valtp = VT.Int | (valtp & VT.ArrayFlag);
                                val = obj[1];
                            } else {
                                stream.WriteByte((byte)valtp);
                            }
                        } else {
                            Type t = val.GetType();
                            VT atp = 0;
                            if (t.IsArray) {
                                atp = VT.ArrayFlag;
                                t = t.GetElementType();
                            }
                            if (!typeVT.TryGetValue(t, out valtp))
                                throw new Exception("Cannot write as unknown " + t);
                            valtp |= atp;
                            stream.WriteByte((byte)valtp);
                        }
                        WriteField(valtp, val);
                        return;
                    case VT.Mesh | VT.ObjectFlag:
                        stream.WriteByte(val == null ? (byte)0 : (byte)1);
                        if (val != null)
                            WriteMesh((object[])val);
                        return;
                    case VT.Mesh | VT.ObjectFlag | VT.ExistingObjectFlag:
                        WriteMesh((object[])val);
                        return;
                    default:
                        if ((type & VT.ObjectFlag) != 0) {
                            if ((type & VT.ExistingObjectFlag) == 0) {
                                stream.WriteByte(val == null ? (byte)0 : (byte)1);
                                if (val == null)
                                    return;
                            } else
                                type &= ~VT.ExistingObjectFlag;
                        }
                        if (!objTypes.TryGetValue(type, out VT[] fldTypes))
                            throw new Exception("Unknown type " + type);

                        object[] valobj = (object[])val;
                        for (int l = fldTypes.Length, i = 0; i < l; i++)
                        {
                            WriteField(fldTypes[i], valobj[i + 1]);
                        }
                        return;
                }
            }
        }

        private class CmdStream
        {
            private readonly FieldStream stream;
            private Dictionary<Guid, String> AssetTypes = new Dictionary<Guid, String>();
            public CmdStream(Stream s, int version)
            {
                stream = new FieldStream() { stream = s, version = version };
            }

            private VT AssetType(Guid id)
            {
                if (!AssetTypes.TryGetValue(id, out string typeName))
                    throw new Exception("Unknown asset " + id);
                if (typeName.StartsWith("UnityEngine."))
                    typeName = typeName.Substring(12);
                return (VT)Enum.Parse(typeof(VT), typeName) | VT.ExistingObjectFlag;
            }

            public object[] Read() {
                VT c = (VT)(ReadInt16(stream.stream) | (int)VT.CmdFlag);
                if (!objTypes.TryGetValue(c, out VT[] fldTypes))
                {
                    throw new Exception("Unknown command " + ((int)c & ~(int)VT.CmdFlag));
                }
                object[] cmd = new object[fldTypes.Length + 1];
                cmd[0] = c;
                for (int l = fldTypes.Length, i = 0; i < l; i++)
                {
                    VT t = fldTypes[i];
                    if (t == VT.FromAsset) 
                    {
                        t = AssetType((Guid)cmd[i]); // assume previous arg is asset id
                    }
                    cmd[i + 1] = stream.ReadField(t);
                }
                if (c == VT.CmdAddAssetToAssetFile)
                    AssetTypes.Add((Guid)cmd[2], (String)cmd[3]);
                return cmd;
            }

            public void Write(object[] cmd) {
                VT c = (VT)cmd[0];
                if (!objTypes.TryGetValue(c, out VT[] fldTypes))
                {
                    throw new Exception("Unknown command " + c);
                }

                WriteInt16(stream.stream, (Int16)(c & ~VT.CmdFlag));
                for (int l = fldTypes.Length, i = 0; i < l; i++)
                {
                    VT t = fldTypes[i];
                    if (t == VT.FromAsset)
                    {
                        t = AssetType((Guid)cmd[i]); // assume previous arg is asset id
                    }
                    stream.WriteField(t, cmd[i + 1]);
                }
                if (c == VT.CmdAddAssetToAssetFile)
                    AssetTypes.Add((Guid)cmd[2], (String)cmd[3]);
            }

            public List<object[]> ReadAll()
            {
                var cmds = new List<object[]>();
                for (;;)
                {
                    object[] cmd = Read();
                    cmds.Add(cmd);
                    if (cmd[0].Equals(VT.CmdDone))
                        break;
                }
                return cmds;
            }

            public void WriteAll(List<object[]> cmds)
            {
                foreach (var cmd in cmds)
                {
                    Write(cmd);
                }
            }
        }

        static byte[] ReadBytes(Stream s, int n)
        {
            byte[] buf = new byte[n];
            s.Read(buf, 0, n);
            return buf;
        }

        static Int32 ReadInt32(Stream s)
        {
            return BitConverter.ToInt32(ReadBytes(s, sizeof(Int32)), 0);
        }

        static float ReadFloat(Stream s)
        {
            return BitConverter.ToSingle(ReadBytes(s, sizeof(float)), 0);
        }

        static UInt32 ReadUInt32(Stream s)
        {
            return BitConverter.ToUInt32(ReadBytes(s, sizeof(UInt32)), 0);
        }

        static Int16 ReadInt16(Stream s)
        {
            return BitConverter.ToInt16(ReadBytes(s, sizeof(Int16)), 0);
        }


        static void WriteBytes(Stream s, byte[] buf)
        {
            s.Write(buf, 0, buf.Length);
        }
        static void WriteInt32(Stream s, int n)
        {
            WriteBytes(s, new byte[]{(byte)n, (byte)(n >> 8), (byte)(n >> 16), (byte)(n >> 24) });
        }
        static void WriteUInt32(Stream s, uint n)
        {
            WriteBytes(s, new byte[]{(byte)n, (byte)(n >> 8), (byte)(n >> 16), (byte)(n >> 24) });
        }
        static void WriteFloat(Stream s, float f)
        {
            WriteBytes(s, BitConverter.GetBytes(f));
        }
        static void WriteInt16(Stream s, int n)
        {
            WriteBytes(s, new byte[]{(byte)n, (byte)(n >> 8) });
        }
        static void WriteString(Stream s, string str)
        {
            if (str == null) {
                WriteInt32(s, -1);
            }
            else
            {
                WriteInt32(s, str.Length);
                WriteBytes(s, UTF8Encoding.UTF8.GetBytes(str));
            }
        }
        static void WriteFloats(Stream s, float[] a)
        {
            for (int i = 0; i < a.Length; i++)
                WriteBytes(s, BitConverter.GetBytes(a[i]));
        }

        static string FmtFields(object[] cmd)
        {
            string s = "";
            LevelFile.fieldNames.TryGetValue((VT)cmd[0], out string[] fieldNames);
            for (int l = cmd.Length, i = 1; i < l; i++) {
                if (fieldNames != null)
                    s += fieldNames[i - 1] + ":";
                object v = cmd[i];
                if (v is object[] va) {
                    v = va[0];
                    if (v is VT vt)
                        if (vt == VT.Enum)
                            v = String.Format("({1}){0}", va[1], va[2]);
                        else if (vt == VT.Vector2)
                            v = String.Format("[{0}, {1}]", va[1], va[2]);
                        else if (vt == VT.Vector3 || vt == VT.Vector3b)
                            v = String.Format("[{0}, {1}, {2}]", va[1], va[2], va[3]);
                        else if (vt == VT.Quaternion || vt == VT.Vector4 || vt == VT.Color)
                            v = String.Format("[{0}, {1}, {2}, {3}]", va[1], va[2], va[3], va[4]);
                        else if (vt.HasFlag(VT.ArrayFlag))
                            if (vt == VT.IntArrayArray && va.Length == 2)
                                v = "IntArray[1][" + (((object[])va[1]).Length - 1) + "]";
                            else
                                v = (vt == VT.IntArrayArray ? "IntArray" : (vt & ~VT.ArrayFlag).ToString()) +
                                    "[" + (va.Length - 1) + "]";
                } else if (v is Vector3 v3) {
                    v = String.Format("[{0}, {1}, {2}]", v3.x, v3.y, v3.z);
                }
                s += String.Format(i + 1 == l ? "{0}" : "{0}, ", v);
            }
            return s;
        }

        public static string FmtCmd(object[] cmd)
        {
            if ((VT)cmd[0] == VT.CmdDone) // otherwise CmdFlag
                return "CmdDone";
            string s = cmd[0] + " " + FmtFields(cmd);
            if ((VT)cmd[0] == VT.CmdSaveAsset)
                s += "\n " + FmtFields(cmd[2] as object[]);
            return s;
        }

        public static Level ReadLevel(string filename)
        {
            using (FileStream s = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                var sig = ReadInt32(s);
                if (sig != 0x52657631)
                    throw new Exception("Invalid file header");
                int version = ReadInt32(s);
                if (version != 3 && version != 4)
                    throw new Exception("Unknown file version " + version);
                ReadInt32(s);
                return new Level() { version = version, cmds = new CmdStream(s, version).ReadAll() };
            }
        }

        public static void WriteLevel(string filename, Level level)
        {
            var tmpFilename = filename + ".tmp";
            using (FileStream s = new FileStream(tmpFilename, FileMode.Create, FileAccess.Write))
            {
                WriteInt32(s, 0x52657631);
                WriteInt32(s, level.version);
                WriteInt32(s, 1);
                new CmdStream(s, level.version).WriteAll(level.cmds);
            }
            File.Delete(filename);
            File.Move(tmpFilename, filename);
        }
    }
}
