using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LevelPost
{
    class LevelDump
    {
        public static List<string> DumpLines(string filename)
        {
            var lines = new List<string>();
            foreach (var cmd in LevelFile.ReadLevel(filename).cmds)
            {
                lines.Add(LevelFile.FmtCmd(cmd));
                #if false
                if ((VT)cmd[0] == VT.CmdSaveAsset && (VT)((object [])cmd[2])[0] == VT.LevelGeometry)
                {
                    object[] segs = (object[])((object[])cmd[2])[3];
                    object[] segSeg = (object[])((object[])cmd[2])[15];
                    int segCount = segs.Length - 1;
                    for (int i = 1, l = segSeg.Length; i < l; i++) 
                    {
                        if ((int)segSeg[i] == 1)
                            lines.Add(String.Format("{0} - {1}", (i - 1) / segCount, (i - 1) % segCount));
                    }
                }
                #endif
            }
            return lines;
        }
    }
}
