using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class Stat
    {
        public int Deaths { get; set; }
        public int SkillPoints { get; set; }
        public int MobsKilled { get; set; }
        public int PlayersKilled { get; set; }
        public int BossesKilled { get; set; }
        public int CaravansKilled { get; set; }
        public int Tournaments { get; set; }
        public int DamageRate { get; set; }
        public int AttackRate { get; set; }
        public int DefenseRate { get; set; }
        public int CriticalRate { get; set; }
        public int HealRate { get; set; }
        public int MagicRate { get; set; }
        public bool Tournament{ get; set; }
        public int Glory { get; set; }
        public bool Part{ get; set; }
    }
}
