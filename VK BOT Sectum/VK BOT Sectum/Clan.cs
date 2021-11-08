using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class Clan
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public long OwnerId { get; set; }
        public int MinLevel { get; set; }
        public int  Members { get; set; }
    }
}
