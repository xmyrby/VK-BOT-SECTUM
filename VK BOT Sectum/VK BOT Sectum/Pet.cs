using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class Pet
    {
        public int Id { get; set; }
        public int Level { get; set; }
        public string Name { get; set; }
        public List<Bonus> Bonuses { get; set; }
        public List<Spell> Spells { get; set; }
    }
}
