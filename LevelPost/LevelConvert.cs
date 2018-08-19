using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;

namespace LevelPost
{
    class ConvertStats
    {
        public int totalTextures;
        public int convertedTextures;
        public int missingTextures;
        public int builtInTextures;
        public int alreadyTextures;
    }

    class ConvertSettings
    {
        //public bool ignoreBuiltIn;
        public bool verbose;
        public List<string> texDirs;
        public List<string> ignoreTexDirs;
    }

    class LevelConvert
    {
        public static ConvertStats Convert(string filename, ConvertSettings settings, Action<string> log)
        {
            var stats = new ConvertStats();
            var level = LevelFile.ReadLevel(filename);
            var ncmds = new List<object[]>();
            //Guid bundle = Guid.Empty;
            //Guid bundle2 = Guid.Empty;
            var shaderGuid = Guid.Empty;
            var ignore = new Regex(@"^((alien|cc|ec|emissive|ice|ind|lava|mat|matcen|om|rockwall|solid|foundry|lavafall|lightblocks|metalbeam|security|stripewarning|titan|tn|utility|warningsign)_|transparent1$)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var assetNames = new Dictionary<Guid, string>();
            foreach (var cmd in level.cmds)
            {
                if ((VT)cmd[0] == VT.CmdLoadAssetFromAssetBundle)
                    assetNames.Add((Guid)cmd[3], (string)cmd[1]);
                if ((VT)cmd[0] == VT.CmdAssetRegisterMaterial)
                {
                    stats.totalTextures++;
                    string texName = (string)cmd[3];
                    if (texName.StartsWith("$INTERNAL$:") || texName.Equals("$"))
                    {
                        if (texName.Equals("$"))
                        {
                            // cannot find original texture name for now
                        }
                        else if (assetNames.TryGetValue(new Guid(texName.Substring("$INTERNAL$:".Length)), out string assetName))
                        {
                            log("Already converted " + assetName);
                        }
                        else
                        {
                            log("Already converted unknown " + texName);
                        }
                        stats.alreadyTextures++;
                        ncmds.Add(cmd);
                        continue;
                    }
                    /*
                    if (0 && ignore != null && ignore.IsMatch(texName))
                    {
                        stats.builtInTextures++;
                        if (settings.verbose)
                            log("Ignored texture " + texName);
                        ncmds.Add(cmd);
                        continue;
                    }
                    */
                    /*
                    if (bundle == Guid.Empty) {
                        bundle = Guid.NewGuid();
                        ncmds.Add(new object[]{VT.CmdLoadAssetBundle, "cl", "cltex", bundle});
                    }
                    Guid asset = Guid.NewGuid();
                    ncmds.Add(new object[] { VT.CmdLoadAssetFromAssetBundle, cmd[3] + ".mat", bundle, asset});
                    */
                    string texBase = texName + ".png";
                    string texFilename = null;
                    var dirs = ignore.IsMatch(texName) ? settings.texDirs : settings.texDirs.Concat(settings.ignoreTexDirs);
                    foreach (var dir in dirs)
                    {
                        var fn = dir + @"\" + texBase;
                        if (File.Exists(fn))
                        {
                            texFilename = fn;
                            break;
                        }
                    }
                    if (texFilename == null) {
                        if (ignore.IsMatch(texName))
                        {
                            stats.builtInTextures++;
                            if (settings.verbose)
                                log("Ignored texture " + texName);
                        }
                        else
                        {
                            stats.missingTextures++;
                            log("Missing file " + texBase);
                        }
                        ncmds.Add(cmd);
                        continue;
                    }
                    Bitmap bmp;
                    try {
                        bmp = new Bitmap(texFilename);
                    } catch (Exception ex) {
                        log("Error loading file " + texFilename + ": " + ex.Message);
                        ncmds.Add(cmd);
                        continue;
                    }
                    BitmapData bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    int stride = -bData.Stride, width = bData.Width, height = bData.Height;
                    byte[] srcData = new byte[Math.Abs(stride) * height];
                    byte[] dstData = new byte[width * height * 4];
                    System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, srcData, 0, srcData.Length);
                    bmp.UnlockBits(bData);
                    int srcIdx = stride < 0 ? -stride * (height - 1) : 0, dstIdx = 0;
                    for (int h = 0; h < height; h++) {
                        for (int w = 0; w < width; w++)
                        {
                            //var x = srcData[srcIdx];
                            //dstData[dstIdx] = data[src + 2];
                            //data[i + 2] = x;
                            dstData[dstIdx + 0] = srcData[srcIdx + 2];
                            dstData[dstIdx + 1] = srcData[srcIdx + 1];
                            dstData[dstIdx + 2] = srcData[srcIdx + 0];
                            dstData[dstIdx + 3] = srcData[srcIdx + 3];
                            srcIdx += 4;
                            dstIdx += 4;
                        }
                        srcIdx -= width * 4 - stride;
                    }

                    log("Converted texture " + texName);

                    /*
                    if (shaderGuid == Guid.Empty) {
                        shaderGuid = Guid.NewGuid();
                        ncmds.Add(new object[] { VT.CmdFindPrefabReference, "Default-Material", shaderGuid });
                    }
                    */

                    var texGuid = Guid.NewGuid();
                    ncmds.Add(new object[] { VT.CmdCreateTexture2D, texGuid, bmp.Width, bmp.Height, "RGB24", false, "Bilinear", texName,
                       dstData });
                    /*
                    var matGuid = Guid.NewGuid();
                    var color = new object[] { VT.Color, 1.0f, 1.0f, 1.0f, 1.0f };
                    var texOfs = new object[] { VT.Vector2, 0.0f, 0.0f };
                    var texScale = new object[] { VT.Vector2, 1.0f, 1.0f };
                    var kws = new object[] { VT.StringArray };
                    ncmds.Add(new object[] { VT.CmdCreateMaterial, matGuid, shaderGuid, color, false, texGuid, texOfs, texScale, 0, kws, texName });
                    //ncmds.Add(new object[] { cmd[0], cmd[1], cmd[2], "$INTERNAL$:" + matGuid });
                    */
                    //ncmds.Add(new object[] { VT.CmdAssetRegisterMaterial, matGuid, shaderGuid, color, false, texGuid, texOfs, texScale, 0, kws, texName });
                    ncmds.Add(new object[] { cmd[0], cmd[1], cmd[2], "$" });
                    ncmds.Add(new object[] { VT.CmdMaterialSetTexture, cmd[1], "_MainTex", texGuid });
                    var color = new object[] { VT.Color, 1.0f, 1.0f, 1.0f, 1.0f };
                    ncmds.Add(new object[] { VT.CmdMaterialSetColor, cmd[1], "_Color", color });


                    stats.convertedTextures++;
                }
                else
                {
                    ncmds.Add(cmd);
                }
            }
            
            /*
            foreach (var cmd in ncmds)
            {
                Debug.WriteLine(Fmt(cmd));
            }
            */

            if (stats.convertedTextures != 0)
                LevelFile.WriteLevel(filename, new Level() { version = level.version, cmds = ncmds });
            return stats;
        }
    }
}
