using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VK_BOT_Sectum
{
    class AuctionItem
    {
        public int Id { get; set; }
        public int ItemId { get; set; }
        public int Level { get; set; }
        public long OwnerId { get; set; }
        public long BeterId { get; set; }
        public int Bet { get; set; }
        public int Left { get; set; }
    }
}
