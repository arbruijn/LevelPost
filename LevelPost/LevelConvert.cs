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
        public int delProbes;
        public int hiddenProbes;
        public int convertedProbes;
        public int changedProbes;
    }

    /// <summary>
    /// Action to take for Reflection Probes
    /// </summary>
    enum ReflectionProbeHandling
    {
        /// <summary>
        /// Do nothing.
        /// </summary>
        Keep,
        /// <summary>
        /// Remove the default probes.
        /// </summary>
        Remove,
        /// <summary>
        /// Force on the default probes.
        /// </summary>
        Hide,
        /// <summary>
        /// Remove the default probes and convert Box Lava Normal triggers to reflection probes.
        /// </summary>
        BoxLavaNormal,
        /// <summary>
        /// Remove the default probes and convert Box Lava Alient triggers to reflection probes.
        /// </summary>
        BoxLavaAlien,
    }

    class ReflectionProbeHandlingValues
    {
        public static ReflectionProbeHandling[] Get()
        {
            return (ReflectionProbeHandling[])Enum.GetValues(typeof(ReflectionProbeHandling));
        }
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
        public Dictionary<string, string> bundleMaterials;
        public HashSet<string> bundleGameObjects;
        public HashSet<string> bundleAudioClips;
        public ReflectionProbeHandling probeHandling;
        public bool boxLavaProbeOneTimeOnly;
        public int probeRes;
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
                newCmds.Add(texCmd);
                newCmds.Add(new object[] { VT.CmdMaterialSetTexture, matGuid, "_MainTex", texGuid });
                var color = new object[] { VT.Color, 1.0f, 1.0f, 1.0f, 1.0f };
                newCmds.Add(new object[] { VT.CmdMaterialSetColor, matGuid, "_Color", color });

                stats.convertedTextures++;
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

        public Guid GetGuid(List<object[]> newCmds)
        {
            // Load material from asset bundle
            if (bundle == Guid.Empty) {
                bundle = Guid.NewGuid();
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
        private HashSet<Guid> matRemoveCT = new HashSet<Guid>();
        private Dictionary<Guid, Guid> texMatIds = new Dictionary<Guid, Guid>();

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.settings = settings;
            this.log = log;
            this.stats = stats;

            foreach (var cmd in cmds)
                if ((VT)cmd[0] == VT.CmdMaterialSetTexture &&
                    (string)cmd[2] == "_MainTex")
                    texMatIds.Add((Guid)cmd[3], (Guid)cmd[1]);

            return true;
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT)cmd[0] == VT.CmdLoadAssetFromAssetBundle && ((string)cmd[1]).EndsWith(".mat")) {
                stats.alreadyTextures++;
                stats.totalTextures++;
            }

            // 
            if (((VT)cmd[0] == VT.CmdMaterialSetTexture || (VT)cmd[0] == VT.CmdMaterialSetColor) &&
                matRemoveCT.Contains((Guid)cmd[1]))
                return true;
            if ((VT)cmd[0] == VT.CmdCreateTexture2D &&
                texMatIds.TryGetValue((Guid)cmd[1], out Guid matId) &&
                matRemoveCT.Contains(matId))
                return true;

            if ((VT)cmd[0] == VT.CmdAssetRegisterMaterial)
            {
                var matGuid = (Guid)cmd[1];
                string texName = (string)cmd[3];
                bool wasCT = false;

                if (texName.StartsWith("$CT$:"))
                {
                    wasCT = true;
                    texName = texName.Substring(5);
                }
                if (settings.bundleMaterials.TryGetValue(texName.ToLowerInvariant(), out string matName))
                {
                    newCmds.Add(new object[] { VT.CmdLoadAssetFromAssetBundle, matName + ".mat", bunRef.GetGuid(newCmds), matGuid});

                    if (wasCT)
                        matRemoveCT.Add(matGuid);

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

    class ReflectionProbeMod : ILevelMod
    {
        private ConvertSettings settings;
        private Action<string> log;
        private ConvertStats stats;
        private readonly Dictionary<Guid, Vector3> objSize = new Dictionary<Guid, Vector3>();
        private readonly Dictionary<Guid, int> objRptDelay = new Dictionary<Guid, int>();
        private readonly Dictionary<Guid, int> objImportance = new Dictionary<Guid, int>();
        private readonly Dictionary<Guid, bool> objOneTime = new Dictionary<Guid, bool>();
        private readonly Dictionary<Guid, string> prefabNames = new Dictionary<Guid, string>();
        private readonly Dictionary<string, Guid> newPrefabIds = new Dictionary<string, Guid>();
        private readonly Dictionary<string, string> prefabConvNames = new Dictionary<string, string>()
            { { "entity_PROP_N0000_MINE", "entity_mine" } };
        private readonly HashSet<Guid> RemoveObj = new HashSet<Guid>();
        private readonly HashSet<Guid> convComp = new HashSet<Guid>();
        private readonly HashSet<string> unconvPrefabs = new HashSet<string>();
        private readonly Dictionary<Guid, Guid> compObj = new Dictionary<Guid, Guid>();

        public bool Init(string levelFilename, ConvertSettings settings, Action<string> log, ConvertStats stats, List<object[]> cmds)
        {
            this.settings = settings;
            this.log = log;
            this.stats = stats;

            if (settings.probeHandling == ReflectionProbeHandling.Remove
                || settings.probeHandling == ReflectionProbeHandling.BoxLavaNormal 
                || settings.probeHandling == ReflectionProbeHandling.BoxLavaAlien)
                foreach (var cmd in cmds)
                    if ((VT)cmd[0] == VT.CmdGameObjectAddComponent && (string)cmd[3] == "ReflectionProbe")
                        RemoveObj.Add((Guid)cmd[1]);
            if (settings.probeHandling == ReflectionProbeHandling.BoxLavaNormal || settings.probeHandling == ReflectionProbeHandling.BoxLavaAlien)
            {
                foreach (var cmd in cmds)
                    if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime)
                        compObj.Add((Guid)cmd[4], (Guid)cmd[3]);
                    else if ((VT)cmd[0] == VT.CmdGameObjectAddComponent)
                        compObj.Add((Guid)cmd[2], (Guid)cmd[1]);
                    else if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                        (string)cmd[2] == "m_size" &&
                        cmd[5] is object[] val &&
                        (VT)val[0] == VT.Vector3b &&
                        compObj.TryGetValue((Guid)cmd[1], out Guid obj) &&
                        !objSize.ContainsKey(obj))
                            objSize.Add(obj, new Vector3() { x = (float)val[1], y = (float)val[2], z = (float)val[3] });
                    else if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                        (string)cmd[2] == "m_repeat_delay" &&
                        cmd[5] is float val2 &&
                        compObj.TryGetValue((Guid)cmd[1], out Guid obj2) &&
                        !objRptDelay.ContainsKey(obj2))
                            objRptDelay.Add(obj2, (int)val2);
                    else if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                        (string)cmd[2] == "importance" &&
                        cmd[5] is int val3 &&
                        compObj.TryGetValue((Guid)cmd[1], out Guid obj3) &&
                        !objImportance.ContainsKey(obj3)) {
                            objImportance.Add(obj3, val3);
                            RemoveObj.Remove(obj3);
                    } else if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                       (string)cmd[2] == "m_one_time" &&
                       cmd[5] is bool val4 &&
                       compObj.TryGetValue((Guid)cmd[1], out Guid obj4) &&
                       !objOneTime.ContainsKey(obj4))
                    {
                            objOneTime.Add(obj4, val4);
                    }
            }
            return true;
        }

        public bool HandleCommand(object[] cmd, List<object[]> newCmds)
        {
            if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                (string)cmd[2] == "m_level_reflection_probes" &&
                (settings.probeHandling != ReflectionProbeHandling.Keep))
            {
                if (settings.probeHandling == ReflectionProbeHandling.Hide) // no message if also removed
                {
                    int n = ((object[])cmd[5]).Length - 1;
                    //log("Hidden " + n + " probes");
                    stats.hiddenProbes += n;
                }
                newCmds.Add(new object[] { cmd[0], cmd[1], cmd[2], cmd[3], cmd[4], new object[] { VT.SegmentReflectionProbeInfoArray }  });
                return true;
            }
            /*
            if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                (string)cmd[2] == "m_level_lights" &&
                (settings.defaultProbeRemove || settings.defaultProbeHide))
            {
                if (!settings.defaultProbeRemove)
                {
                    log("Hidden " + (((object[])cmd[5]).Length - 1) + " lights");
                    stats.hiddenProbes++;
                }
                newCmds.Add(new object[] { cmd[0], cmd[1], cmd[2], cmd[3], cmd[4], new object[] { VT.SegmentLightInfoArray } });
                return true;
            }
            */
            if ((VT)cmd[0] == VT.CmdCreateGameObject && RemoveObj.Contains((Guid)cmd[1]))
            {
                RemoveObj.Add((Guid)cmd[2]);
                stats.delProbes++;
                return true;
            }
            if ((VT)cmd[0] == VT.CmdGameObjectAddComponent && RemoveObj.Contains((Guid)cmd[1]))
            {
                RemoveObj.Add((Guid)cmd[2]);
                return true;
            }
            if ((VT)cmd[0] == VT.CmdGetComponentAtRuntime && RemoveObj.Contains((Guid)cmd[3]))
            {
                RemoveObj.Add((Guid)cmd[4]);
                return true;
            }
            if (((VT)cmd[0] == VT.CmdGameObjectSetName ||
                (VT)cmd[0] == VT.CmdTransformSetParent ||
                (VT)cmd[0] == VT.CmdGameObjectSetComponentProperty ||
                (VT)cmd[0] == VT.CmdGameObjectSetLayer ||
                (VT)cmd[0] == VT.CmdGameObjectSetTag) && RemoveObj.Contains((Guid)cmd[1]))
                return true;

            if ((VT)cmd[0] == VT.CmdFindPrefabReference)
            {
                prefabNames.Add((Guid)cmd[2], (string)cmd[1]);
                return false;
            }
            if ((VT)cmd[0] == VT.CmdGameObjectSetComponentProperty &&
                (string)cmd[2] == "resolution" &&
                cmd[5] is int val1 &&
                compObj.TryGetValue((Guid)cmd[1], out Guid obj1) &&
                objImportance.ContainsKey(obj1) &&
                val1 != settings.probeRes) {
                //log("Changed resolution for custom probe to " + settings.probeRes);
                newCmds.Add(new object[] { cmd[0], cmd[1], cmd[2], cmd[3], cmd[4], settings.probeRes });
                stats.changedProbes++;
                return true;
            }


            if ((VT)cmd[0] == VT.CmdInstantiatePrefab)
            {
                string prefabName;
                objOneTime.TryGetValue((Guid)cmd[2], out bool isOneTime);
                bool shouldAddPrefab = false;
                switch(settings.probeHandling)
                {
                    case ReflectionProbeHandling.BoxLavaNormal:
                        shouldAddPrefab = prefabNames.TryGetValue((Guid)cmd[1], out prefabName)
                            && prefabName == "entity_TRIGGER_BOX_LAVA_NORMAL";
                        break;
                    case ReflectionProbeHandling.BoxLavaAlien:
                        shouldAddPrefab = prefabNames.TryGetValue((Guid)cmd[1], out prefabName)
                            && prefabName == "entity_TRIGGER_BOX_LAVA_ALIEN";
                        break;
                    default:
                        break;
                }
                shouldAddPrefab &= (!settings.boxLavaProbeOneTimeOnly || isOneTime);

                if(shouldAddPrefab)
                {
                    Guid objId = (Guid)cmd[2], transId = (Guid)cmd[3], rpId = Guid.NewGuid();
                    newCmds.Add(new object[] { VT.CmdCreateGameObject, objId, transId });
                    newCmds.Add(new object[] { VT.CmdGameObjectAddComponent, objId, rpId, "ReflectionProbe" });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "mode", (byte)0, (byte)0,
                        new object[] { VT.Enum, 0, "UnityEngine.Rendering.ReflectionProbeMode" } });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "refreshMode", (byte)0, (byte)0,
                        new object[] { VT.Enum, 0, "UnityEngine.Rendering.ReflectionProbeRefreshMode" } });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "boxProjection", (byte)0, (byte)0, true });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "resolution", (byte)0, (byte)0, settings.probeRes });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "intensity", (byte)0, (byte)0, 1.5f });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "clearFlags", (byte)0, (byte)0,
                        new object[] { VT.Enum, 0, "UnityEngine.Rendering.ReflectionProbeClearFlags" } });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "backgroundColor", (byte)0, (byte)0,
                        new object[] { VT.Color, 0f, 0f, 0f, 1f } });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "cullingMask", (byte)0, (byte)0, -134220801 });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "center", (byte)0, (byte)0,
                        new Vector3() { x = 0f, y = 0f, z = 0f } });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "size", (byte)0, (byte)0,
                        objSize[objId] });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "farClipPlane", (byte)0, (byte)0, 100f });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "nearClipPlane", (byte)0, (byte)0, 0.3f });
                    newCmds.Add(new object[] { VT.CmdGameObjectSetComponentProperty, rpId, "importance", (byte)0, (byte)0, objRptDelay[objId] });

                    RemoveObj.Add(objId);

                    stats.convertedProbes++;

                    return true;
                    /*
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
                            if (!unconvPrefabs.Contains(prefabName))
                            { // can't load, do use find prefab after all
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
                    */
                }
            }
            return false;
        }
        public bool IsChanged()
        {
            return stats.delProbes != 0 || stats.hiddenProbes != 0 || stats.convertedProbes != 0 || stats.changedProbes != 0;
        }
        public void Finish(List<object[]> ncmds)
        {
            if (stats.delProbes != 0)
                log("Removed " + stats.delProbes + " default reflection probe" + (stats.delProbes != 1 ? "s" : ""));
            if (stats.hiddenProbes != 0)
                log("Forced on " + stats.hiddenProbes + " default reflection probe" + (stats.hiddenProbes != 1 ? "s" : ""));
            if (stats.convertedProbes != 0)
                log("Converted " + stats.convertedProbes + " triggers to reflection probe" + (stats.convertedProbes != 1 ? "s" : ""));
            if (stats.changedProbes != 0)
                log("Changed " + stats.changedProbes + " reflection probe" + (stats.changedProbes != 1 ? "s" : ""));
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

            if (settings.probeHandling != ReflectionProbeHandling.Keep)
                mods.Add(new ReflectionProbeMod());

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
