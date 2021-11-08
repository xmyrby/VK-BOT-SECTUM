using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class Item
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Level { get; set; }
        public int MinLevel { get; set; }
        public string Type { get; set; }
        public long Cost { get; set; }
        public List<Bonus> Bonuses { get; set; }
        public List<Request> Requests { get; set; }
        public List<Spell> Spells { get; set; }
        public Enchant Enchant { get; set; }
    }
}
