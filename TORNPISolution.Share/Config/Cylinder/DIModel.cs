using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TORNPISolution.Share.Config.Cylinder
{
    public class DIModel
    {
        public int id { get; set; }
        public string? Describe { get; set; }
        public bool IsExtend { get; set; }
        public bool Enabled { get; set; }
        public short Channel { get; set; }
    }
}
