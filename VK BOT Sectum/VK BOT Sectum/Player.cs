using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class Player
    {
        public long? Id { get; set; }
        public long Money { get; set; }
        public int Diamonds { get; set; }
        public long? LastMessage { get; set; }
        public string Name { get; set; }
        public int ClanId { get; set; }
        public int Level { get; set; }
        public long Xp { get; set; }
        public bool Fight { get; set; }
        public int LocationId { get; set; }
        public int MovingTo { get; set; }
        public int StepsLeft { get; set; }
        public long Health { get; set; }
        public long MaxHealth { get; set; }
        public long Mana { get; set; }
        public long MaxMana { get; set; }
        public bool Grind { get; set; }
        public int AttackSteps { get; set; }
        public long AttackerId { get; set; }
        public bool AttackerType { get; set; }
        public int NextCall { get; set; }
        public List<int> Portals { get; set; }
        public Stat Stats { get; set; }
        public Equipment Equipt { get; set; }
        public Potion Pot { get; set; }
        public Pet Pet { get; set; }
    }
}
