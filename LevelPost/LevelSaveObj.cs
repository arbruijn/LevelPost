using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LevelPost
{
    internal class LevelSaveObj
    {
        int VertOfs = 1;
        Action<string> Log;

        private void DumpMesh(StreamWriter f, object[] mesh)
        {
            string name = (string)mesh[1];
            object[] verts = (object[])mesh[2], uvs = (object[])mesh[3];
            object[] norms = (object[])mesh[6], tris = (object[])mesh[12];
            f.WriteLine("o " + name);
            for (int n = verts.Length, i = 1; i < n; i++)
            {
                Vector3 vert = (Vector3)verts[i];
                f.WriteLine("v " + -vert.x + " " + vert.y + " " + vert.z);
            }
            for (int n = norms.Length, i = 1; i < n; i++)
            {
                Vector3 vert = (Vector3)norms[i];
                f.WriteLine("vn " + -vert.x + " " + vert.y + " " + vert.z);
            }
            for (int n = uvs.Length, i = 1; i < n; i++)
            {
                object[] vert = (object[])uvs[i];
                f.WriteLine("vt " + (float)vert[1] + " " + -(float)vert[2]);
            }

            for (int subn = tris.Length, subi = 1; subi < subn; subi++)
            {
                object[] sub = (object[])tris[subi];
                for (int fn = sub.Length, fi = 1; fi < fn; fi += 3)
                {
                    var line = new StringBuilder("f");
                    for (int vn = 3, vi = vn - 1; vi >= 0; vi--)
                    {
                        int v = (int)sub[fi + vi];
                        line.Append(' ');
                        line.Append(VertOfs + v); // vert
                        line.Append('/');
                        line.Append(VertOfs + v); // tex
                        line.Append('/');
                        line.Append(VertOfs + v); // norm
                    }
                    f.WriteLine(line);
                }
            }
            VertOfs += verts.Length - 1;
        }

        internal static void SaveObj(string filename, string outFilename, Action<string> log)
        {
            new LevelSaveObj() { Log = log }.Run(filename, outFilename);
        }

        private void Run(string filename, string outFilename)
        {
            var matNames = new Dictionary<Guid, string>();
            var objMats = new Dictionary<Guid, string>();
            var meshMats = new Dictionary<Guid, string>();
            var compObj = new Dictionary<Guid, Guid>();
            var compType = new Dictionary<Guid, string>();
            var usedMatNames = new HashSet<string>();

            using (var f = new StreamWriter(outFilename))
            {
                var cmds = LevelFile.ReadLevel(filename).cmds;
                foreach (var cmd in cmds)
                {
                    if ((VT)cmd[0] == VT.CmdAssetRegisterMaterial)
                        matNames.Add((Guid)cmd[1], (string)cmd[3]);
                    if ((VT)cmd[0] == VT.CmdLoadAssetFromAssetBundle)
                    {
                        string name = (string)cmd[1];
                        if (name.EndsWith(".mat"))
                            name = name.Substring(0, name.Length - 4);
                        matNames.Add((Guid)cmd[3], name);
                    }
                    if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty && (string)cmd[2] == "sharedMaterial")
                        objMats.Add(compObj[(Guid)cmd[1]], matNames[(Guid)cmd[5]]);
                    if ((VT)cmd[0] == VT.CmdGameObjectAddComponent)
                    {
                        compObj[(Guid)cmd[2]] = (Guid)cmd[1];
                        compType[(Guid)cmd[2]] = (string)cmd[3];
                    }
                }
                foreach (var cmd in cmds)
                {
                    if ((VT)cmd[0] == VT.CmdSaveAsset && (VT)((object[])cmd[2])[0] == VT.Mesh)
                    {
                        object[] mesh = (object[])cmd[2];
                        string name = (string)mesh[1];
                        if (!name.Contains("__RenderMesh"))
                            continue;
                        //Log("mesh " + name);
                        var matName = meshMats[(Guid)cmd[1]];
                        f.WriteLine("usemtl " + matName);
                        DumpMesh(f, mesh);
                        usedMatNames.Add(matName);
                    }
                    if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty && (string)cmd[2] == "sharedMesh" &&
                        compType[(Guid)cmd[1]] == "MeshFilter")
                        meshMats.Add((Guid)cmd[5], objMats[compObj[(Guid)cmd[1]]]);
                }
            }
            using (var fmtl = new StreamWriter(Path.ChangeExtension(outFilename, "mtl")))
                foreach (var matName in usedMatNames)
                {
                    fmtl.WriteLine("newmtl " + matName);
                    fmtl.WriteLine("illum 2");
                    fmtl.WriteLine("Kd 1.00 1.00 1.00");
                    fmtl.WriteLine("Ka 0.00 0.00 0.00");
                    fmtl.WriteLine("Ks 0.00 0.00 0.00");
                    fmtl.WriteLine("d 1.0");
                    fmtl.WriteLine("map_Kd " + matName + ".png");
                }
        }
    }
}