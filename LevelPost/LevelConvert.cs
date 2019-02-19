using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Drawing.Imaging;
using YamlDotNet.RepresentationModel;
using System.Globalization;

namespace LevelPost
{
    class ConvertStats
    {
        public int totalTextures;
        public int convertedTextures;
        public int missingTextures;
        public int builtInTextures;
        public int alreadyTextures;
        public int convertedEntities;
    }

    class ConvertSettings
    {
        public bool verbose;
        public List<string> texDirs;
        public List<string> ignoreTexDirs;
        public string bundleDir;
        public string bundleName;
        //public string bundlePrefix;
        public int texPointPx;
        public HashSet<string> bundleMaterials;
        public HashSet<string> bundleGameObjects;
    }

    interface ILevelMod
    {
        bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds);
        bool HandleCommand(object[] cmd, List<object[]> ncmds);
        bool IsChanged();
        void Finish(List<object[]> ncmds);
    }

    class TexMod : ILevelMod
    {
        private ConvertSettings settings;
        private Action<string> log;
        private ConvertStats stats;
        private Regex ignore = new Regex(@"^((alien|cc|ec|emissive|ice|ind|lava|mat|matcen|om|rockwall|solid|foundry|lavafall|lightblocks|metalbeam|security|stripewarning|titan|tn|utility|warningsign)_|transparent1$)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private Dictionary<Guid, string> assetNames = new Dictionary<Guid, string>();
        private Dictionary<Guid, Guid> texMatIds = new Dictionary<Guid, Guid>();
        private Dictionary<Guid, string> matNames = new Dictionary<Guid, string>();
        private readonly HashSet<Guid> updMats = new HashSet<Guid>();

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.settings = settings;
            this.log = log;
            this.stats = stats;

            var compObj = new Dictionary<Guid, Guid>();
            foreach (var cmd in cmds)
                if ((VT)cmd[0] == VT.CmdAssetRegisterMaterial)
                    matNames.Add((Guid)cmd[1], (string)cmd[3]);
                else if ((VT)cmd[0] == VT.CmdMaterialSetTexture &&
                    (string)cmd[2] == "_MainTex")
                    texMatIds.Add((Guid)cmd[3], (Guid)cmd[1]);

            return true;
        }

        private string FindTextureFile(string texName)
        {
            string texBase = texName + ".png";
            var dirs = ignore.IsMatch(texName) ? settings.texDirs : settings.texDirs.Concat(settings.ignoreTexDirs);
            foreach (var dir in dirs)
            {
                var fn = dir + Path.DirectorySeparatorChar + texBase;
                if (File.Exists(fn))
                    return fn;
            }
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
            return null;
        }

        // Return bitmap pixels in ABGR format, top->bottom order
        private static byte[] GetBitmapData(Bitmap bmp, out bool hasAlpha)
        {
            var bData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            // bData.Stride is positive for bottom->top source data
            int stride = -bData.Stride, width = bData.Width, height = bData.Height;
            var srcData = new byte[Math.Abs(stride) * height];
            var dstData = new byte[width * height * 4];
            System.Runtime.InteropServices.Marshal.Copy(bData.Scan0, srcData, 0, srcData.Length);
            bmp.UnlockBits(bData);
            int srcIdx = stride < 0 ? -stride * (height - 1) : 0, dstIdx = 0;
            bool hasAlphaInt = false;
            for (int h = 0; h < height; h++)
            {
                for (int w = 0; w < width; w++)
                {
                    dstData[dstIdx + 0] = srcData[srcIdx + 2];
                    dstData[dstIdx + 1] = srcData[srcIdx + 1];
                    dstData[dstIdx + 2] = srcData[srcIdx + 0];
                    var a = dstData[dstIdx + 3] = srcData[srcIdx + 3];
                    if (a != 255 && !hasAlphaInt)
                        hasAlphaInt = true;
                    srcIdx += 4;
                    dstIdx += 4;
                }
                srcIdx -= width * 4 - stride;
            }
            hasAlpha = hasAlphaInt;
            return dstData;
        }

        private object[] MakeTexCmd(string texName, Guid texGuid, out bool blocky)
        {
            blocky = false;
            string texFilename = FindTextureFile(texName);
            if (texFilename == null)
                return null;

            Bitmap bmp;
            try
            {
                bmp = new Bitmap(texFilename);
            }
            catch (Exception ex)
            {
                log("Error loading file " + texFilename + ": " + ex.Message);
                return null;
            }

            bool hasAlpha;
            var texData = GetBitmapData(bmp, out hasAlpha);
            blocky = bmp.Width <= settings.texPointPx;

            return new object[] { VT.CmdCreateTexture2D, texGuid, bmp.Width, bmp.Height,
                    hasAlpha ? "ARGB32" : "RGB24",
                    false,
                    blocky ? "Point" : "Bilinear",
                    texName, texData };
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT) cmd[0] == VT.CmdLoadAssetFromAssetBundle)
                assetNames.Add((Guid) cmd[3], (string) cmd[1]);
            if ((VT)cmd[0] == VT.CmdCreateTexture2D &&
                texMatIds.TryGetValue((Guid)cmd[1], out Guid matId) &&
                matNames.TryGetValue(matId, out string matName) &&
                matName.StartsWith("$CT$:"))
            {
                var texName = matName.Substring(5);
                var texCmd = MakeTexCmd(texName, (Guid)cmd[1], out bool blocky);
                if (texCmd == null)
                    return false;
                newCmds.Add(texCmd);
                updMats.Add(matId);
                log("Updated texture " + texName + (blocky ? " (blocky)" : ""));
                stats.convertedTextures++;
                stats.totalTextures++;
                return true;
            }
            if ((VT) cmd[0] == VT.CmdAssetRegisterMaterial)
            {
                stats.totalTextures++;
                var matGuid = (Guid)cmd[1];
                string texName = (string)cmd[3];
                if (texName.StartsWith("$CT$:")) // this is updated/logged/counted with CmdCreateTexture2D
                    return false;
                if (texName.StartsWith("$INTERNAL$:") || texName.Equals("$") || texName.StartsWith("$CT$:"))
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
                    return false;
                }

                var texGuid = Guid.NewGuid();
                var texCmd = MakeTexCmd(texName, texGuid, out bool blocky);
                if (texCmd == null)
                    return false;
                newCmds.Add(texCmd);

                /*
                // Load material from asset bundle
                if (bundle == Guid.Empty) {
                    bundle = Guid.NewGuid();
                    ncmds.Add(new object[]{VT.CmdLoadAssetBundle, levelName, "lpmaterials", bundle});
                }
                Guid asset = Guid.NewGuid();
                ncmds.Add(new object[] { VT.CmdLoadAssetFromAssetBundle, cmd[3] + ".mat", bundle, matGuid});
                */

                /*
                // Load shader from asset bundle if not yet loaded
                if (bundle == Guid.Empty) {
                    bundle = Guid.NewGuid();
                    ncmds.Add(new object[]{VT.CmdLoadAssetBundle, levelName, "lpshaders", bundle});
                }
                if (shaderGuid == Guid.Empty) {
                    shaderGuid = Guid.NewGuid();
                    ncmds.Add(new object[] { VT.CmdFindPrefabReference, "LPStandardShader", shaderGuid });
                }
                // Create new material with loaded shader
                var matGuid = Guid.NewGuid();
                var color = new object[] { VT.Color, 1.0f, 1.0f, 1.0f, 1.0f };
                var texOfs = new object[] { VT.Vector2, 0.0f, 0.0f };
                var texScale = new object[] { VT.Vector2, 1.0f, 1.0f };
                var kws = new object[] { VT.StringArray };
                ncmds.Add(new object[] { VT.CmdCreateMaterial, matGuid, shaderGuid, color, false, texGuid, texOfs, texScale, 0, kws, texName });
                */

                // Create new material with CmdAssetRegisterMaterial for invalid name "$CT$:" + texName,
                // which creates default green material with simple Diffuse shader,
                // and change material properties to our texture. Diffuse shader only has _MainTex :(
                newCmds.Add(new object[] { VT.CmdAssetRegisterMaterial, matGuid, cmd[2], "$CT$:" + texName });
                newCmds.Add(new object[] { VT.CmdMaterialSetTexture, matGuid, "_MainTex", texGuid });
                var color = new object[] { VT.Color, 1.0f, 1.0f, 1.0f, 1.0f };
                newCmds.Add(new object[] { VT.CmdMaterialSetColor, matGuid, "_Color", color });

                stats.convertedTextures++;
                stats.totalTextures++;
                var msgOpts = new List<string>();
                if (blocky)
                    msgOpts.Add("blocky");
                //if (hasAlpha)
                //    msgOpts.Add("alpha");
                log("Converted texture " + texName + (msgOpts.Count != 0 ? " (" + String.Join(", ", msgOpts) + ")" : ""));
                return true;
            }
            return false;
        }
        public bool IsChanged()
        {
            return stats.convertedTextures != 0;
        }
        public void Finish(List<object[]> ncmds)
        {
        }
    }

    class BunRef
    {
        private Guid bundle;
        private ConvertSettings settings;
        public Action<string> log;

        public void Init(ConvertSettings settings)
        {
            this.settings = settings;
        }

        private static string FmtCount(int n, string singular, string plural)
        {
            return n + " " + (n == 1 ? singular : plural);
        }

        public Guid GetGuid(List<object[]> newCmds)
        {
            // Load material from asset bundle
            if (bundle == Guid.Empty) {
                bundle = Guid.NewGuid();
                var parts = new List<string>();
                if (settings.bundleMaterials != null && settings.bundleMaterials.Count != 0)
                    parts.Add(FmtCount(settings.bundleMaterials.Count, "material", "materials"));
                if (settings.bundleGameObjects != null && settings.bundleGameObjects.Count != 0)
                    parts.Add(FmtCount(settings.bundleGameObjects.Count, "entity", "entities"));
                log("Using bundle " + Path.Combine(settings.bundleDir, "windows", settings.bundleName) +
                    (parts.Count != 0 ? " (" + String.Join(", ", parts) + ")" : ""));
                newCmds.Add(new object[]{VT.CmdLoadAssetBundle, settings.bundleDir, settings.bundleName, bundle });
            }
            return bundle;
        }
    }

    class BunTexMod : ILevelMod
    {
        private ConvertSettings settings;
        private Action<string> log;
        private ConvertStats stats;
        public BunRef bunRef;

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.settings = settings;
            this.log = log;
            this.stats = stats;
            return true;
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT)cmd[0] == VT.CmdLoadAssetFromAssetBundle && ((string)cmd[1]).EndsWith(".mat")) {
                stats.alreadyTextures++;
                stats.totalTextures++;
            }
            if ((VT)cmd[0] == VT.CmdAssetRegisterMaterial)
            {
                var matGuid = (Guid)cmd[1];
                string texName = (string)cmd[3];

                if (settings.bundleMaterials.Contains(texName))
                {
                    newCmds.Add(new object[] { VT.CmdLoadAssetFromAssetBundle, cmd[3] + ".mat", bunRef.GetGuid(newCmds), matGuid});

                    stats.convertedTextures++;
                    stats.totalTextures++;
                    log("Converted bundle texture " + texName);
                    return true;
                }
            }
            return false;
        }
        public bool IsChanged()
        {
            return stats.convertedTextures != 0;
        }
        public void Finish(List<object[]> ncmds)
        {
        }
    }

    class EntityReplaceMod : ILevelMod
    {
        private ConvertSettings settings;
        private Action<string> log;
        private ConvertStats stats;
        public BunRef bunRef;
        private readonly Dictionary<Guid, int> objIdx = new Dictionary<Guid, int>();
        private readonly Dictionary<Guid, string> prefabNames = new Dictionary<Guid, string>();
        private readonly Dictionary<string, Guid> newPrefabIds = new Dictionary<string, Guid>();
        private readonly Dictionary<string, string> prefabConvNames = new Dictionary<string, string>()
            { { "entity_PROP_N0000_MINE", "entity_mine" } };
        private readonly HashSet<Guid> convObj = new HashSet<Guid>();
        private readonly HashSet<Guid> convComp = new HashSet<Guid>();
        private readonly HashSet<string> unconvPrefabs= new HashSet<string>();

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.settings = settings;
            this.log = log;
            this.stats = stats;

            var compObj = new Dictionary<Guid, Guid>();
            foreach (var cmd in cmds)
                if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime)
                    compObj.Add((Guid)cmd[4], (Guid)cmd[3]);
                else if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                    (string)cmd[2] == "m_index" &&
                    cmd[5] is int &&
                    compObj.TryGetValue((Guid)cmd[1], out Guid obj))
                    objIdx.Add(obj, (int)cmd[5]);
            return true;
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT)cmd[0] == VT.CmdFindPrefabReference)
            {
                var prefabName = (string)cmd[1];
                var prefabId = (Guid)cmd[2];
                if (prefabConvNames.ContainsKey(prefabName)) // we'll likely load this from bundle, don't use find prefab
                {
                    prefabNames.Add(prefabId, prefabName);
                    return true;
                }
            } else if ((VT)cmd[0] == VT.CmdInstantiatePrefab) {
                var prefabId = (Guid)cmd[1];
                var objId = (Guid)cmd[2];
                if (prefabNames.TryGetValue(prefabId, out string prefabName))
                {
                    int idx = 0;
                    objIdx.TryGetValue(objId, out idx);
                    string newPrefabName = prefabConvNames[prefabName] + "_" + idx;
                    if (!settings.bundleGameObjects.Contains(newPrefabName))
                    {
                        log("Ignored entity " + prefabName + " index " + idx + ", " + newPrefabName + " is not in bundle.");
                        if (!unconvPrefabs.Contains(prefabName)) { // can't load, do use find prefab after all
                            unconvPrefabs.Add(prefabName);
                            newCmds.Add(new object[] { VT.CmdFindPrefabReference, prefabName, prefabId });
                        }
                        return false;
                    }
                    if (!newPrefabIds.TryGetValue(newPrefabName, out Guid newPrefabId))
                    {
                        newPrefabId = Guid.NewGuid();
                        newPrefabIds.Add(newPrefabName, newPrefabId);
                        newCmds.Add(new object[] { VT.CmdLoadAssetFromAssetBundle, newPrefabName, bunRef.GetGuid(newCmds), newPrefabId });
                    }
                    newCmds.Add(new object[] { cmd[0], newPrefabId, cmd[2], cmd[3] }); // instantiate new prefab
                    log("Converted bundle entity " + prefabName + " index " + idx + " to " + newPrefabName);
                    stats.convertedEntities++;
                    convObj.Add(objId);
                    return true;
                }
            /*
            } else if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime && convObj.Contains((Guid)cmd[3])) { // Skip get comp for converted
                convComp.Add((Guid)cmd[4]);
                return true;
            } else if ((VT) cmd[0] == VT.CmdGameObjectSetComponentProperty && convComp.Contains((Guid)cmd[1])) {
                return true;
            }
            */
            } else if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime && convObj.Contains((Guid)cmd[3])) { // Add instead of get comp for converted
                string type = (string)cmd[2];
                if (type == "PropBase")
                    type = "PropGeneric";
                newCmds.Add(new object[] { VT.CmdGameObjectAddComponent, cmd[3], cmd[4], type });
                convObj.Remove((Guid)cmd[3]); // Treat no longer as converted
                return true;
            }
            return false;
        }
        public bool IsChanged()
        {
            return stats.convertedEntities != 0;
        }
        public void Finish(List<object[]> ncmds)
        {
        }
    }


#if TWEAKS
    class EntityTweaker : ILevelMod
    {
        private Action<string> log;

        private List<Tuple<string, Guid>> prefabInsts = new List<Tuple<string, Guid>>();
        private Dictionary<Tuple<string, Guid>, Guid> comps = new Dictionary<Tuple<string, Guid>, Guid>();
        private Dictionary<string, YamlMappingNode> yamlEnts = null;
        private Dictionary<Guid, string> assetNames = new Dictionary<Guid, string>();
        private bool changed = false;

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.log = log;
            var lpFilename = new Regex(@"[.][a-z]{1,5}$", RegexOptions.IgnoreCase).Replace(levelFilename, "_levelpost.txt");
            if (File.Exists(lpFilename))
            {
                var yaml = new YamlStream();
                try
                {
                    using (var stream = File.OpenText(lpFilename))
                    {
                        yaml.Load(stream);
                        var map = (YamlMappingNode)yaml.Documents[0].RootNode;
                        if (map.Children.TryGetValue(new YamlScalarNode("entities"), out YamlNode ents))
                        {
                            yamlEnts = new Dictionary<string, YamlMappingNode>();
                            foreach (var c in ((YamlMappingNode)ents).Children)
                                yamlEnts.Add(c.Key.ToString().ToLowerInvariant(), (YamlMappingNode)c.Value);
                        }
                    }
                    log("Loaded " + lpFilename);
                }
                catch (Exception ex)
                {
                    log("Error loading " + lpFilename + ": " + ex.Message);
                    return false;
                }
            }
            return true;
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT)cmd[0] == VT.CmdFindPrefabReference)
                assetNames.Add((Guid)cmd[2], (string)cmd[1]);
            if ((VT)cmd[0] == VT.CmdInstantiatePrefab && yamlEnts != null && assetNames.TryGetValue((Guid)cmd[1], out string name))
            {
                prefabInsts.Add(new Tuple<string,Guid>(name, (Guid)cmd[2]));
            }
            if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime)
            {
                comps.Add(new Tuple<string, Guid>((string)cmd[2], (Guid)cmd[3]), (Guid)cmd[4]);
            }
            return false;
        }
    
        public bool IsChanged()
        {
            return changed;
        }

        public void Finish(List<object[]> ncmds)
        {
            foreach (var x in prefabInsts)
            {
                string name = x.Item1;
                Guid go = (Guid)x.Item2;
                if (yamlEnts.TryGetValue(name.ToLowerInvariant(), out YamlMappingNode ent))
                {
                    foreach (var compMap in ent.Children)
                    {
                        string compName = compMap.Key.ToString();
                        Guid comp;
                        if (!comps.TryGetValue(new Tuple<string, Guid>(compName, go), out comp))
                        {
                            comp = Guid.NewGuid();
                            ncmds.Add(new object[] { VT.CmdGetComponentAtRuntime, false, compName, go, comp });
                        }
                        foreach (var prop in ((YamlMappingNode)compMap.Value).Children)
                        {
                            var sval = prop.Value.ToString();
                            float fval;
                            float.TryParse(sval, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out fval);
                            string propName = prop.Key.ToString();
                            object val =
                                propName == "m_fire_projectile" ? (object)(new object[] { VT.Enum, (int)fval, "Overload.ProjPrefab" }) :
                                propName == "robot_type" ? (object)(new object[] { VT.Enum, (int)fval, "Overload.EnemyType" }) :
                                propName == "hideFlags" ? (object)(new object[] { VT.Enum, (int)fval, "UnityEngine.HideFlags" }) :
                                propName == "m_firing_distribution" ? (object)(new object[] { VT.Enum, (int)fval, "Overload.FiringDistribution" }) :
                                propName.StartsWith("AI_robot_") || propName.StartsWith("AI_legal_") ? sval.Equals("true", StringComparison.InvariantCultureIgnoreCase) ? true : false :
                                propName.StartsWith("m_burst_fire_") ? (int)fval :
                                propName == "m_bonus_drop1" || propName == "m_bonus_drop2" ?
                                    (object)(new object[] { VT.Enum, (int)fval, "Overload.ItemPrefab" }) :
                                fval;
                            ncmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, comp, propName, (byte)0, (byte)0, val });
                            log("Set " + name + " " + compMap.Key + " " + prop.Key + " to " + val);
                            changed = true;
                        }
                    }
                }
            }
        }
    }
#endif

    class LevelConvert
    {
        public static ConvertStats Convert(string levelFilename, ConvertSettings settings, Action<string> log)
        {
            var level = LevelFile.ReadLevel(levelFilename);

            var stats = new ConvertStats();

            var mods = new List<ILevelMod>();

            if (settings.bundleName != null && settings.bundleName != "")
            {
                var bufRef = new BunRef() { log = log };
                bufRef.Init(settings);
                if (settings.bundleMaterials != null)
                    mods.Add(new BunTexMod() { bunRef = bufRef });
                if (settings.bundleGameObjects != null)
                    mods.Add(new EntityReplaceMod() { bunRef = bufRef });
            }

            mods.Add(new TexMod());
            #if TWEAKS
            mods.Add(new EntityTweaker());
            #endif

            foreach (var mod in mods)
                if (!mod.Init(levelFilename, settings, log, stats, level.cmds))
                    return stats;

            var newCmds = new List<object[]>();

            foreach (var cmd in level.cmds)
            {
                if ((VT)cmd[0] != VT.CmdDone &&
                    !mods.Any(mod => mod.HandleCommand(cmd, newCmds)))
                    newCmds.Add(cmd);
            }

            foreach (var mod in mods)
                mod.Finish(newCmds);

            newCmds.Add(new object[] { VT.CmdDone });

            if (mods.Any(mod => mod.IsChanged()))
                LevelFile.WriteLevel(levelFilename, new Level() { version = level.version, cmds = newCmds });
            return stats;
        }
    }
}
