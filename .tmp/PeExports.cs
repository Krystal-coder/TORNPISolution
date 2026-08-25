using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class PeExports
{
    public static void Main(string[] args)
    {
        string path = args[0];
        using (var fs = File.OpenRead(path))
        using (var br = new BinaryReader(fs))
        {
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();
            fs.Seek(peOffset, SeekOrigin.Begin);
            br.ReadUInt32(); // PE sig
            ushort machine = br.ReadUInt16();
            ushort numSections = br.ReadUInt16();
            fs.Seek(12, SeekOrigin.Current);
            ushort optSize = br.ReadUInt16();
            fs.Seek(2, SeekOrigin.Current);
            long optStart = fs.Position;
            ushort magic = br.ReadUInt16();
            bool pe32plus = (magic == 0x20b);
            Console.WriteLine("Machine: 0x" + machine.ToString("X4") + (pe32plus ? " (x64)" : " (x86)"));
            long dirPos = optStart + (pe32plus ? 112 : 96);
            fs.Seek(dirPos, SeekOrigin.Begin);
            uint expRva = br.ReadUInt32();
            fs.Seek(4, SeekOrigin.Current);
            var sections = new List<long[]>();
            fs.Seek(optStart + optSize, SeekOrigin.Begin);
            for (int i = 0; i < numSections; i++)
            {
                fs.Seek(8, SeekOrigin.Current);
                uint vs = br.ReadUInt32();
                uint va = br.ReadUInt32();
                fs.Seek(12, SeekOrigin.Current);
                uint rp = br.ReadUInt32();
                fs.Seek(16, SeekOrigin.Current);
                sections.Add(new long[] { va, vs, rp });
            }
            Func<uint, long> rvaToFile = delegate(uint rva)
            {
                foreach (var s in sections)
                    if (rva >= s[0] && rva < s[0] + s[1]) return s[2] + (rva - s[0]);
                return -1;
            };
            long expPos = rvaToFile(expRva);
            fs.Seek(expPos + 24, SeekOrigin.Begin);
            uint numNames = br.ReadUInt32();
            uint addrOfNames = br.ReadUInt32();
            long namesPos = rvaToFile(addrOfNames);
            var sb = new StringBuilder();
            for (uint i = 0; i < numNames; i++)
            {
                fs.Seek(namesPos + i * 4, SeekOrigin.Begin);
                uint nameRva = br.ReadUInt32();
                fs.Seek(rvaToFile(nameRva), SeekOrigin.Begin);
                sb.Clear();
                byte b;
                while ((b = br.ReadByte()) != 0) sb.Append((char)b);
                Console.WriteLine(sb.ToString());
            }
        }
    }
}
