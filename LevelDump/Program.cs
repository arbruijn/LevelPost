using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LevelDump
{
    class Program
    {
        static void Main(string[] args)
        {
            foreach (var line in LevelPost.LevelDump.DumpLines(args[0]))
                Console.WriteLine(line);
        }
    }
}
