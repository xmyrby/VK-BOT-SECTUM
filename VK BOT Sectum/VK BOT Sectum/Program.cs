using System;
using System.Collections.Generic;
using VkNet;
using VkNet.Enums.Filters;
using VkNet.Enums.SafetyEnums;
using VkNet.Model;
using VkNet.Model.RequestParams;
using VkNet.Utils;
using MySql.Data.MySqlClient;
using VkNet.Model.Keyboard;

namespace VK_BOT_Sectum
{
    class Program
    {
        static readonly List<Location> locations = new List<Location>();

        static readonly List<Mob> mobs = new List<Mob>();

        static readonly Random rnd = new Random();

        static readonly VkApi api = new VkApi();

        static System.Collections.ObjectModel.Collection<Message> messages;

        static MySqlConnection connection;

        static MySqlCommand command;

        #region Получение питомца
        static Pet GetPet(long? playerdId)
        {
            Pet pet = new Pet();
            string spells = "";
            command.CommandText = $"SELECT `id`,`name`,`bonusesnames`,`bonusesvalues`,`spellsnames`,`petlevel` FROM `pets`,`equipment` WHERE `playerid`={playerdId} AND `id`=`petid`";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                pet.Id = reader.GetInt32("id");
                pet.Name = reader.GetString("name");
                pet.Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues"));
                pet.Level = reader.GetInt32("petlevel");
                spells = reader.GetString("spellsnames");
            }
            reader.Close();

            pet.Spells = GetSpells(spells);
            return pet;
        }
        #endregion

        #region Получение названия предмета
        static string GetItemName(int itemId)
        {
            command.CommandText = $"SELECT `name` FROM `items` WHERE `id`={itemId % 10000}";
            MySqlDataReader reader = command.ExecuteReader();
            string name = "";
            while (reader.Read())
            {
                name = reader.GetString("name");
            }
            reader.Close();
            return name;
        }
        #endregion

        #region Получение игроков в клане
        static List<Player> GetPlayersInClan(int clanId)
        {
            List<Player> players = new List<Player>();
            List<long?> ids = new List<long?>();

            command.CommandText = $"SELECT `id` FROM `players` WHERE `clanid`={clanId} ORDER BY `level` DESC";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64("id"));
            }
            reader.Close();
            int count = ids.Count;
            for (int i = 0; i < count; i++)
            {
                players.Add(GetPlayer(ids[i], null));
            }

            return players;
        }
        #endregion

        #region Получение клана
        static Clan GetClan(int clanId)
        {
            Clan clan = new Clan();
            command.CommandText = $"SELECT * FROM `clans` WHERE `id`={clanId}";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                clan.Id = reader.GetInt16("id");
                clan.Name = reader.GetString("name");
                clan.Description = reader.GetString("description");
                clan.OwnerId = reader.GetInt64("ownerid");
                clan.MinLevel = reader.GetInt16("minlevel");
                clan.Members = reader.GetInt16("members");
            }
            reader.Close();
            return clan;
        }
        #endregion

        #region Выпадение лута с босса
        static void BossDrop(long? playerId, string dropList, int itemLevel)
        {
            int count = dropList.Split('_').Length;
            for (int i = 0; i < count; i++)
            {
                int rawcount = 0;
                command.CommandText = $"SELECT COUNT(*) as count FROM `inventory` WHERE `playerid`={playerId}";
                MySqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rawcount = reader.GetInt32("count");
                }
                reader.Close();

                if (rawcount < 10)
                {
                    int chance = rnd.Next(0, 5);
                    if (chance <= 1)
                    {
                        int itemId = Convert.ToInt32(dropList.Split('_')[i]);

                        chance = rnd.Next(0, 100);
                        int enchanteId = 0;
                        if (chance <= 10)
                        {
                            int enchCount = 0;
                            command.CommandText = $"SELECT COUNT(*) as `count` FROM `enchants`";
                            reader = command.ExecuteReader();
                            while (reader.Read())
                            {
                                enchCount = reader.GetInt32("count");
                            }
                            reader.Close();

                            enchanteId = rnd.Next(0, enchCount) + 1;
                        }

                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({playerId},{enchanteId * 10000 + itemId},{itemLevel})";
                        command.ExecuteNonQuery();

                        Item item = GetInventoryItem(enchanteId * 10000 + itemId);
                        string enchant = "";
                        if (item.Enchant != null)
                        {
                            enchant = $" (Зачарование: {item.Enchant.Name})";
                        }

                        Message($"💼 Получен новый предмет: {item.Name}{enchant} (🔱 Уровень: {itemLevel})", playerId, true);
                    }
                }
            }
        }
        #endregion

        #region Получение магазина
        static List<Item> GetShop(long? playerId, int playerLevel)
        {
            List<Item> shop = new List<Item>();

            command.CommandText = $"SELECT * FROM `shops` WHERE `playerid`={playerId}";
            MySqlDataReader reader = command.ExecuteReader();
            if (reader.HasRows)
            {
                reader.Read();
                string idsStr = reader.GetString("itemsids");
                string levelsStr = reader.GetString("itemslevels");
                reader.Close();

                int count = idsStr.Split('_').Length;
                for (int i = 0; i < count; i++)
                {
                    Item item = GetInventoryItem(Convert.ToInt32(idsStr.Split('_')[i]));
                    item.Level = Convert.ToInt32(levelsStr.Split('_')[i]);
                    for (int j = 0; j < item.Bonuses.Count; j++)
                    {
                        item.Cost += Math.Abs(item.Bonuses[j].Value + item.Level) * 151;
                    }
                    item.Cost += Convert.ToInt64(item.Level * (item.Level * (item.Level / 4) * 2.5));
                    shop.Add(item);
                }
            }
            else
            {
                reader.Close();

                List<int> allIds = new List<int>();
                command.CommandText = $"SELECT `id` FROM `items` WHERE `minlevel`<={playerLevel}";
                reader = command.ExecuteReader();
                while (reader.Read())
                {
                    allIds.Add(reader.GetInt32("id"));
                }
                reader.Close();
                string ids = "";
                string levels = "";
                for (int i = 0; i < 10; i++)
                {
                    int id = rnd.Next(0, allIds.Count);
                    int level = rnd.Next(Math.Max(1, playerLevel - 10), playerLevel + 1);
                    if (ids == "")
                    {
                        ids = $"{allIds[id]}";
                        levels = $"{level}";
                    }
                    else
                    {
                        ids += $"_{allIds[id]}";
                        levels += $"_{level}";
                    }
                    Item item = GetInventoryItem(allIds[id]);
                    item.Level = level;

                    for (int j = 0; j < item.Bonuses.Count; j++)
                    {
                        item.Cost += Math.Abs(item.Bonuses[j].Value + item.Level) * 251;
                    }
                    item.Cost += Convert.ToInt64(item.Level * (item.Level * (item.Level / 4) * 2.5));

                    shop.Add(item);
                }

                command.CommandText = $"INSERT INTO `shops`(`playerid`,`itemsids`,`itemslevels`) VALUES({playerId},'{ids}','{levels}')";
                command.ExecuteNonQuery();
            }

            return shop;
        }
        #endregion

        #region Выпадение лута
        static void Drop(long? playerId, int locationId)
        {
            int chance;
            for (int i = 0; i < rnd.Next(1, 6); i++)
            {
                int rawcount = 0;
                command.CommandText = $"SELECT COUNT(*) as `count` FROM `inventory` WHERE `playerid`={playerId}";
                MySqlDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rawcount = reader.GetInt32("count");
                }
                reader.Close();
                if (rawcount < 10)
                {
                    int level = GetLocation(locationId).EntityLevel;
                    List<int> itemsIds = new List<int>();
                    chance = rnd.Next(0, 100);

                    if (chance <= 25)
                    {
                        command.CommandText = $"SELECT * FROM `items` WHERE `minlevel`<={level}";
                        reader = command.ExecuteReader();
                        while (reader.Read())
                        {
                            itemsIds.Add(reader.GetInt32("id"));
                        }
                        reader.Close();

                        chance = rnd.Next(0, 100);
                        int enchanteId = 0;
                        if (chance <= 5)
                        {
                            int count = 0;
                            command.CommandText = $"SELECT COUNT(*) as `count` FROM `enchants`";
                            reader = command.ExecuteReader();
                            while (reader.Read())
                            {
                                count = reader.GetInt32("count");
                            }
                            reader.Close();

                            enchanteId = rnd.Next(0, count) + 1;
                        }

                        int itemLevel = Math.Max(1, rnd.Next(level - 1, level + 2));
                        int itemId = itemsIds[rnd.Next(0, itemsIds.Count)];

                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({playerId},{enchanteId * 10000 + itemId},{itemLevel})";
                        command.ExecuteNonQuery();

                        Item item = GetInventoryItem(enchanteId * 10000 + itemId);
                        string enchant = "";
                        if (item.Enchant != null)
                        {
                            enchant = $" (Зачарование: {item.Enchant.Name})";
                        }

                        Message($"💼 Получен новый предмет: {item.Name}{enchant} (🔱 Уровень: {itemLevel})", playerId, true);
                    }
                }
                else
                {
                    break;
                }
            }
        }
        #endregion

        #region Выпадение зелий
        static void DropPotions(long? playerId)
        {
            command.CommandText = $"SELECT * FROM `potions` WHERE `playerid`={playerId}";
            MySqlDataReader reader = command.ExecuteReader();

            int heal = 0;
            int mana = 0;

            while (reader.Read())
            {
                mana = reader.GetInt16("manapotion");
                heal = reader.GetInt16("healpotion");
            }
            reader.Close();

            string healAnswer = "";
            int healCount = 0;
            if (heal < 10)
            {
                healCount = rnd.Next(0, Math.Min(9 - heal, 3));
            }

            if (healCount > 0 && heal < 10)
            {
                healAnswer = $"💉 Получено зелий лечения: {healCount}";
            }

            string manaAnswer = "";
            int manaCount = 0;

            if (mana < 10)
            {
                manaCount = rnd.Next(0, Math.Min(9 - mana, 3));
            }

            if (manaCount > 0 && mana < 10)
            {
                manaAnswer = $"⚗ Получено зелий маны: {manaCount}";
            }

            if (manaAnswer != "" || healAnswer != "")
            {
                command.CommandText = $"UPDATE `potions` SET `healpotion`=`healpotion`+{healCount},`manapotion`=`manapotion`+{manaCount} WHERE `playerid`={playerId}";
                command.ExecuteNonQuery();

                Message($"{healAnswer}\n{manaAnswer}", playerId, true);
            }
        }
        #endregion

        #region Получение информации о питомце
        static string GetPetInfo(Pet pet)
        {
            string answer = $"{pet.Name} 🔱 {pet.Level}";

            if (pet.Bonuses.Count > 0)
            {
                answer += $"\n💫 ";
            }
            for (int i = 0; i < pet.Bonuses.Count; i++)
            {
                if (pet.Bonuses[i].Name == "damagerate")
                {
                    answer += $"🔪 + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                else if (pet.Bonuses[i].Name == "attackrate")
                {
                    answer += $"👊🏻 + {(pet.Bonuses[i].Value + pet.Level - 1) * 5}";
                }
                else if (pet.Bonuses[i].Name == "defenserate")
                {
                    answer += $"🛡 + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                else if (pet.Bonuses[i].Name == "criticalrate")
                {
                    answer += $"💥 + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                else if (pet.Bonuses[i].Name == "healrate")
                {
                    answer += $"💚 + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                else if (pet.Bonuses[i].Name == "magicrate")
                {
                    answer += $"✨ + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                else if (pet.Bonuses[i].Name == "manarate")
                {
                    answer += $"💙 + {pet.Bonuses[i].Value + pet.Level - 1}";
                }
                if (i < pet.Bonuses.Count - 1)
                {
                    answer += " | ";
                }
            }

            if (pet.Spells.Count > 0)
            {
                for (int i = 0; i < pet.Spells.Count; i++)
                {
                    if (pet.Spells[i].Type == "fire")
                    {
                        answer += $"\n🔥 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "fireball")
                    {
                        answer += $"\n☄ {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "burst")
                    {
                        answer += $"\n🌩 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "flash")
                    {
                        answer += $"\n⚡ {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "heal")
                    {
                        answer += $"\n💚 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "plague")
                    {
                        answer += $"\n🕷 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "ice")
                    {
                        answer += $"\n❄ {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "echo")
                    {
                        answer += $"\n🔗 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "vortex")
                    {
                        answer += $"\n🌪 {pet.Spells[i].Name}";
                    }
                    else if (pet.Spells[i].Type == "block")
                    {
                        answer += $"\n🛡 {pet.Spells[i].Name}";
                    }
                }
            }

            return answer;
        }
        #endregion

        #region Получение информации о предмете
        static string GetItemInfo(Item item, int itemLevel, string type)
        {
            int level;
            string answer;
            if (itemLevel > 0)
            {
                level = itemLevel;
                answer = $" 🔱 {itemLevel}";
            }
            else
            {
                level = item.Level;
                answer = $" 🔱 {item.Level}";
            }

            if (item.Enchant != null)
            {
                answer += $"\n🔮 Зачарование: {item.Enchant.Name}";
                if (type != "task")
                {
                    for (int i = 0; i < item.Enchant.Bonuses.Count; i++)
                    {
                        if (item.Enchant.Bonuses[i].Name == "damagerate")
                        {
                            answer += $"\n🔪 Увеличение наносимого урона на {item.Enchant.Bonuses[i].Value}%";
                        }
                        else if (item.Enchant.Bonuses[i].Name == "defenserate")
                        {
                            answer += $"\n🛡 Уменьшение получаемого урона на {item.Enchant.Bonuses[i].Value}%";
                        }
                        else if (item.Enchant.Bonuses[i].Name == "criticalrate")
                        {
                            answer += $"\n💥 Увеличение критического урона на {item.Enchant.Bonuses[i].Value}%";
                        }
                        else if (item.Enchant.Bonuses[i].Name == "healrate")
                        {
                            answer += $"\n💚 Увеличение уровня лечения на {item.Enchant.Bonuses[i].Value}%";
                        }
                        else if (item.Enchant.Bonuses[i].Name == "magicrate")
                        {
                            answer += $"\n✨ Увеличение наносимого магического урона на {item.Enchant.Bonuses[i].Value}%";
                        }
                    }
                }
            }

            if (type != "task")
            {
                if (item.Bonuses.Count > 0)
                {
                    answer += $"\n💫 ";
                }
                for (int i = 0; i < item.Bonuses.Count; i++)
                {
                    if (item.Bonuses[i].Name == "damagerate")
                    {
                        answer += $"🔪 + {item.Bonuses[i].Value + level - 1}";
                    }
                    else if (item.Bonuses[i].Name == "attackrate")
                    {
                        answer += $"👊🏻 + {(item.Bonuses[i].Value + level - 1) * 5}";
                    }
                    else if (item.Bonuses[i].Name == "defenserate")
                    {
                        answer += $"🛡 + {item.Bonuses[i].Value + level - 1}";
                    }
                    else if (item.Bonuses[i].Name == "criticalrate")
                    {
                        answer += $"💥 + {item.Bonuses[i].Value + level - 1}";
                    }
                    else if (item.Bonuses[i].Name == "healrate")
                    {
                        answer += $"💚 + {item.Bonuses[i].Value + level - 1}";
                    }
                    else if (item.Bonuses[i].Name == "magicrate")
                    {
                        answer += $"✨ + {item.Bonuses[i].Value + level - 1}";
                    }
                    else if (item.Bonuses[i].Name == "manarate")
                    {
                        answer += $"💙 + {item.Bonuses[i].Value + level - 1}";
                    }
                    if (i < item.Bonuses.Count - 1)
                    {
                        answer += " | ";
                    }
                }
                if (item.Requests.Count > 0)
                {
                    answer += $"\n⚙ 🔱 {item.MinLevel + (level - item.MinLevel)}";
                }
                for (int i = 0; i < item.Requests.Count; i++)
                {
                    if (i < item.Requests.Count)
                    {
                        answer += " | ";
                    }

                    if (item.Requests[i].Name == "damagerate")
                    {
                        answer += $"🔪 {item.Requests[i].Value + level - 1}";
                    }
                    else if (item.Requests[i].Name == "attackrate")
                    {
                        answer += $"👊🏻 {item.Requests[i].Value + level - 1}";
                    }
                    else if (item.Requests[i].Name == "defenserate")
                    {
                        answer += $"🛡 {item.Requests[i].Value + level - 1}";
                    }
                    else if (item.Requests[i].Name == "criticalrate")
                    {
                        answer += $"💥 {item.Requests[i].Value + level - 1}";
                    }
                    else if (item.Requests[i].Name == "healrate")
                    {
                        answer += $"💚 {item.Requests[i].Value + level - 1}";
                    }
                    else if (item.Requests[i].Name == "magicrate")
                    {
                        answer += $"✨ {item.Requests[i].Value + level - 1}";
                    }
                }
            }

            if (item.Spells.Count > 0)
            {
                for (int i = 0; i < item.Spells.Count; i++)
                {
                    if (item.Spells[i].Type == "fire")
                    {
                        answer += $"\n🔥 {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "fireball")
                    {
                        answer += $"\n☄ {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "burst")
                    {
                        answer += $"\n🌩 {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "flash")
                    {
                        answer += $"\n⚡ {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "heal")
                    {
                        answer += $"\n💚 {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "plague")
                    {
                        answer += $"\n🕷 {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "ice")
                    {
                        answer += $"\n❄ {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "echo")
                    {
                        answer += $"\n🔗 {item.Spells[i].Name}";
                    }
                    else if (item.Spells[i].Type == "vortex")
                    {
                        answer += $"\n🌪 {item.Spells[i].Name}";
                    }
                }
            }

            if (type == "shop" || type == "inventory")
            {
                if (type == "inventory")
                {
                    for (int i = 0; i < item.Bonuses.Count; i++)
                    {
                        item.Cost += Math.Abs(item.Bonuses[i].Value + item.Level) * 251;
                    }
                    item.Cost += Convert.ToInt64(level * (level * (level / 4) * 2.5));

                    item.Cost = Convert.ToInt64(item.Cost / 3);
                }

                answer += $"\n💰 {item.Cost}";
            }



            return answer;
        }
        #endregion

        #region Получение списка телепортов
        static List<int> GetTeleports(string list)
        {
            List<int> teleports = new List<int>();
            int count = list.Split('_').Length;
            for (int i = 0; i < count; i++)
            {
                teleports.Add(Convert.ToInt32(list.Split('_')[i]));
            }
            return teleports;
        }
        #endregion

        #region Получение суммы бонусов
        static List<int> GetBonuses(List<Item> items)
        {
            int arBonus = 0;
            int drBonus = 0;
            int crBonus = 0;
            int hrBonus = 0;
            int dmrBonus = 0;
            int mrBonus = 0;
            int maBonus = 0;

            int drEBonus = 0;
            int crEBonus = 0;
            int hrEBonus = 0;
            int dmrEBonus = 0;
            int mrEBonus = 0;

            for (int j = 0; j < items.Count; j++)
            {
                if (items[j].Id != 0)
                {
                    for (int i = 0; i < items[j].Bonuses.Count; i++)
                    {

                        if (items[j].Bonuses[i].Name == "attackrate")
                        {
                            arBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "defenserate")
                        {
                            drBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "criticalrate")
                        {
                            crBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "healrate")
                        {
                            hrBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "damagerate")
                        {
                            dmrBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "magicrate")
                        {
                            mrBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                        else if (items[j].Bonuses[i].Name == "manarate")
                        {
                            maBonus += items[j].Bonuses[i].Value + items[j].Level - 1;
                        }
                    }
                    if (items[j].Enchant != null)
                    {
                        for (int i = 0; i < items[j].Enchant.Bonuses.Count; i++)
                        {
                            if (items[j].Enchant.Bonuses[i].Name == "defenserate")
                            {
                                drEBonus += items[j].Enchant.Bonuses[i].Value;
                            }
                            else if (items[j].Enchant.Bonuses[i].Name == "criticalrate")
                            {
                                crEBonus += items[j].Enchant.Bonuses[i].Value;
                            }
                            else if (items[j].Enchant.Bonuses[i].Name == "healrate")
                            {
                                hrEBonus += items[j].Enchant.Bonuses[i].Value;
                            }
                            else if (items[j].Enchant.Bonuses[i].Name == "damagerate")
                            {
                                dmrEBonus += items[j].Enchant.Bonuses[i].Value;
                            }
                            else if (items[j].Enchant.Bonuses[i].Name == "magicrate")
                            {
                                mrEBonus += items[j].Enchant.Bonuses[i].Value;
                            }
                        }
                    }
                }
            }

            return new List<int>() { arBonus, drBonus, crBonus, hrBonus, dmrBonus, mrBonus, maBonus, drEBonus, crEBonus, hrEBonus, dmrEBonus, mrEBonus };
        }

        #endregion

        #region Получение списка заклинаний
        static List<Spell> GetSpells(string spellsList)
        {
            List<Spell> spells = new List<Spell>();
            int count = spellsList.Split('_').Length;
            for (int i = 0; i < count; i++)
            {
                command.CommandText = $"SELECT * FROM `spells` WHERE `spell`='{spellsList.Split('_')[i]}'";
                MySqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    spells.Add(new Spell()
                    {
                        Name = reader.GetString("name"),
                        Type = reader.GetString("type")
                    });
                }

                reader.Close();
            }
            return spells;
        }
        #endregion

        #region Получение списка бонусов при создании
        static List<Bonus> GetBonuses(string namesList, string valuesList)
        {
            List<Bonus> bonuses = new List<Bonus>();
            int count = namesList.Split('_').Length;
            for (int i = 0; i < count; i++)
            {
                bonuses.Add(new Bonus()
                {
                    Name = namesList.Split('_')[i],
                    Value = Convert.ToInt32(valuesList.Split('_')[i])
                });
            }
            return bonuses;
        }
        #endregion

        #region Получение списка требований предмета при создании
        static List<Request> GetRequests(string namesList, string valuesList)
        {
            List<Request> requests = new List<Request>();
            int count = namesList.Split('_').Length;
            for (int i = 0; i < count; i++)
            {
                requests.Add(new Request()
                {
                    Name = namesList.Split('_')[i],
                    Value = Convert.ToInt32(valuesList.Split('_')[i])
                });
            }
            return requests;
        }
        #endregion

        #region Получение предмета в инвентаре
        static Item GetInventoryItem(int itemId)
        {
            Item item = new Item();
            string spells = "";
            command.CommandText = $"SELECT * FROM `items` WHERE `id`={itemId % 10000}";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                item.Id = reader.GetInt32("id");
                item.Name = reader.GetString("name");
                item.MinLevel = reader.GetInt32("minlevel");
                item.Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues"));
                item.Requests = GetRequests(reader.GetString("requestsnames"), reader.GetString("requestsvalues"));
                spells = reader.GetString("spellsnames");
                item.Type = reader.GetString("type");
            }
            reader.Close();

            if (itemId / 10000 > 0)
            {
                string spells1 = "";
                command.CommandText = $"SELECT * FROM `enchants` WHERE `id`={itemId / 10000}";
                reader = command.ExecuteReader();
                while (reader.Read())
                {
                    item.Enchant = new Enchant()
                    {
                        Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues")),
                        Name = reader.GetString("name")
                    };
                    if (item.Type == "weapon" || item.Type == "book" || item.Type == "shield")
                    {
                        spells1 = reader.GetString("spellsnames");
                    }
                }
                reader.Close();
                spells += '_' + spells1;
            }

            item.Spells = GetSpells(spells);
            return item;
        }
        #endregion

        #region Получение надетого предмета
        static Item GetEquipedItem(int itemId, long? playerId)
        {
            Item item = new Item();
            string spells = "";
            command.CommandText = $"SELECT * FROM `items`,`equipment` WHERE `id`={itemId % 10000} AND `playerid` = {playerId}";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                item.Id = reader.GetInt32("id");
                item.Name = reader.GetString("name");
                item.MinLevel = reader.GetInt16("minlevel");
                item.Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues"));
                item.Requests = GetRequests(reader.GetString("requestsnames"), reader.GetString("requestsvalues"));
                spells = reader.GetString("spellsnames");
                item.Type = reader.GetString("type");
                if (item.Type == "helmet")
                {
                    item.Level = reader.GetInt16("helmetlevel");
                }
                else if (item.Type == "plate")
                {
                    item.Level = reader.GetInt16("platelevel");
                }
                else if (item.Type == "pants")
                {
                    item.Level = reader.GetInt16("pantslevel");
                }
                else if (item.Type == "boots")
                {
                    item.Level = reader.GetInt16("bootslevel");
                }
                else if (item.Type == "weapon")
                {
                    item.Level = reader.GetInt16("weaponlevel");
                }
                else if (item.Type == "shield")
                {
                    item.Level = reader.GetInt16("shieldlevel");
                }
                else if (item.Type == "rune")
                {
                    item.Level = reader.GetInt16("runelevel");
                }
                else if (item.Type == "book")
                {
                    item.Level = reader.GetInt16("booklevel");
                }
            }
            reader.Close();

            if (itemId / 10000 > 0)
            {
                string spells1 = "";
                command.CommandText = $"SELECT * FROM `enchants` WHERE `id`={itemId / 10000}";
                reader = command.ExecuteReader();
                while (reader.Read())
                {
                    item.Enchant = new Enchant()
                    {
                        Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues")),
                        Name = reader.GetString("name")
                    };
                    if (item.Type == "weapon" || item.Type == "book" || item.Type == "shield")
                    {
                        spells1 = reader.GetString("spellsnames");
                    }
                }
                reader.Close();
                spells += '_' + spells1;
            }

            item.Spells = GetSpells(spells);
            return item;
        }
        #endregion#region Получение надетого предмета

        #region Получение надетого предмета на турнире
        static Item GetEquipedTournamentItem(int itemId, long? playerId)
        {
            Item item = new Item();
            string spells = "";
            command.CommandText = $"SELECT * FROM `items`,`equipment` WHERE `id`={itemId % 10000} AND `playerid` = {playerId}";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                item.Id = reader.GetInt32("id");
                item.Name = reader.GetString("name");
                item.MinLevel = reader.GetInt16("minlevel");
                item.Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues"));
                item.Requests = GetRequests(reader.GetString("requestsnames"), reader.GetString("requestsvalues"));
                spells = reader.GetString("spellsnames");
                item.Type = reader.GetString("type");
                item.Level = 100;
            }
            reader.Close();

            if (itemId / 10000 > 0)
            {
                string spells1 = "";
                command.CommandText = $"SELECT * FROM `enchants` WHERE `id`={itemId / 10000}";
                reader = command.ExecuteReader();
                while (reader.Read())
                {
                    item.Enchant = new Enchant()
                    {
                        Bonuses = GetBonuses(reader.GetString("bonusesnames"), reader.GetString("bonusesvalues")),
                        Name = reader.GetString("name")
                    };
                    if (item.Type == "weapon" || item.Type == "book" || item.Type == "shield")
                    {
                        spells1 = reader.GetString("spellsnames");
                    }
                }
                reader.Close();
                spells += '_' + spells1;
            }
            item.Spells = GetSpells(spells);
            return item;
        }
        #endregion

        #region Отправка сообщения
        static void Message(string message, long? id, bool keys)
        {
            KeyboardBuilder keyboard = new KeyboardBuilder();

            if (keys)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Статистика", Color = KeyboardButtonColor.Primary });
                keyboard.AddButton(new AddButtonParams { Label = "Улучшить персонажа", Color = KeyboardButtonColor.Primary });
                keyboard.AddLine();
                keyboard.AddButton(new AddButtonParams { Label = "Снаряжение", Color = KeyboardButtonColor.Primary });
                keyboard.AddButton(new AddButtonParams { Label = "Инвентарь", Color = KeyboardButtonColor.Primary });
                keyboard.AddLine();
                keyboard.AddButton(new AddButtonParams { Label = "Локация", Color = KeyboardButtonColor.Default });
                keyboard.AddButton(new AddButtonParams { Label = "Помощь", Color = KeyboardButtonColor.Default });
                keyboard.AddLine();
                keyboard.AddButton(new AddButtonParams { Label = "Клан", Color = KeyboardButtonColor.Default });
                keyboard.AddButton(new AddButtonParams { Label = "Турнир", Color = KeyboardButtonColor.Default });
                keyboard.AddLine();
                keyboard.AddButton(new AddButtonParams { Label = "Онлайн", Color = KeyboardButtonColor.Positive });
                keyboard.AddButton(new AddButtonParams { Label = "Онлайн локации", Color = KeyboardButtonColor.Positive });
                keyboard.AddLine();
                keyboard.AddButton(new AddButtonParams { Label = "Гринд", Color = KeyboardButtonColor.Negative });
                keyboard.AddButton(new AddButtonParams { Label = "Вызвать всех на бой", Color = KeyboardButtonColor.Negative });
            }

            try
            {
                api.Messages.Send(new MessagesSendParams
                {
                    RandomId = rnd.Next(0, 1000000000),
                    PeerId = id,
                    Message = message,
                    Keyboard = keyboard.Build()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        #endregion

        #region Отправка сообщения об улучшении
        static void UpdateMessage(string message, long? id)
        {
            KeyboardBuilder keyboard = new KeyboardBuilder();

            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень урона", Color = KeyboardButtonColor.Negative });
            keyboard.AddLine();
            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень силы", Color = KeyboardButtonColor.Negative });
            keyboard.AddLine();
            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень защиты", Color = KeyboardButtonColor.Negative });
            keyboard.AddLine();
            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень боя", Color = KeyboardButtonColor.Negative });
            keyboard.AddLine();
            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень лечения", Color = KeyboardButtonColor.Negative });
            keyboard.AddLine();
            keyboard.AddButton(new AddButtonParams { Label = "Увеличить уровень волшебства", Color = KeyboardButtonColor.Negative });

            try
            {
                api.Messages.Send(new MessagesSendParams
                {
                    RandomId = rnd.Next(0, 1000000000),
                    PeerId = id,
                    Message = message,
                    Keyboard = keyboard.Build()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        #endregion

        #region Отправка сообщения о снятии
        static void TakeOffMessage(string message, long? id, Player player)
        {
            KeyboardBuilder keyboard = new KeyboardBuilder();

            int count = 0;
            if (player.Equipt.HelmetId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять шлем", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (player.Equipt.PlateId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять броню", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.PantsId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять штаны", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.BootsId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять ботинки", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.WeaponId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять оружие", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.ShieldId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять щит", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.RuneId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять руну", Color = KeyboardButtonColor.Negative });
                count++;
            }
            if (count == 2)
            {
                keyboard.AddLine();
                count = 0;
            }
            if (player.Equipt.BookId != 0)
            {
                keyboard.AddButton(new AddButtonParams { Label = "Снять книгу", Color = KeyboardButtonColor.Negative });
            }

            try
            {
                api.Messages.Send(new MessagesSendParams
                {
                    RandomId = rnd.Next(0, 1000000000),
                    PeerId = id,
                    Message = message,
                    Keyboard = keyboard.Build()
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
        #endregion

        #region Получение данных игрока
        static Player GetPlayer(long? playerId, User user)
        {
            try
            {
                Player player;
                command.CommandText = $"SELECT * FROM `players`,`stats`,`equipment`,`potions` WHERE `id`={playerId} AND `id`=`stats`.`playerid` AND `stats`.`playerid`=`equipment`.`playerid` AND `equipment`.`playerid`=`potions`.`playerid`";
                MySqlDataReader reader = command.ExecuteReader();
                if (reader.HasRows)
                {
                    reader.Read();
                    player = new Player
                    {
                        Id = reader.GetInt64("id"),
                        Money = reader.GetInt64("money"),
                        Diamonds = reader.GetInt32("diamonds"),
                        LastMessage = reader.GetInt64("lastmessage"),
                        Name = reader.GetString("name"),
                        ClanId = reader.GetInt16("clanid"),
                        Level = reader.GetInt16("level"),
                        Xp = reader.GetInt64("xp"),
                        Fight = reader.GetBoolean("fight"),
                        LocationId = reader.GetInt16("locationid"),
                        MovingTo = reader.GetInt16("movingto"),
                        StepsLeft = reader.GetInt16("stepsleft"),
                        Health = reader.GetInt64("health"),
                        MaxHealth = reader.GetInt64("maxhealth"),
                        Mana = reader.GetInt64("mana"),
                        MaxMana = reader.GetInt64("maxmana"),
                        Grind = reader.GetBoolean("grind"),
                        AttackSteps = reader.GetInt16("attacksteps"),
                        AttackerId = reader.GetInt64("attackerid"),
                        AttackerType = reader.GetBoolean("attackertype"),
                        NextCall = reader.GetInt16("nextcall"),
                        Portals = GetTeleports(reader.GetString("portals")),
                        Stats = new Stat
                        {
                            Deaths = reader.GetInt16("deaths"),
                            SkillPoints = reader.GetInt16("skillpoints"),
                            MobsKilled = reader.GetInt16("mobskilled"),
                            PlayersKilled = reader.GetInt16("playerskilled"),
                            BossesKilled = reader.GetInt16("bosseskilled"),
                            CaravansKilled = reader.GetInt16("caravanskilled"),
                            Tournaments = reader.GetInt16("tournaments"),
                            DamageRate = reader.GetInt16("damagerate"),
                            AttackRate = reader.GetInt16("attackrate"),
                            DefenseRate = reader.GetInt16("defenserate"),
                            CriticalRate = reader.GetInt16("criticalrate"),
                            HealRate = reader.GetInt16("healrate"),
                            MagicRate = reader.GetInt16("magicrate"),
                            Tournament = reader.GetBoolean("tournament"),
                            Glory = reader.GetInt32("glory"),
                            Part = reader.GetBoolean("part")
                        },
                        Equipt = new Equipment
                        {
                            HelmetId = reader.GetInt32("helmetid"),
                            PlateId = reader.GetInt32("plateid"),
                            PantsId = reader.GetInt32("pantsid"),
                            BootsId = reader.GetInt32("bootsid"),
                            WeaponId = reader.GetInt32("weaponid"),
                            ShieldId = reader.GetInt32("shieldid"),
                            RuneId = reader.GetInt32("runeid"),
                            BookId = reader.GetInt32("bookid")
                        },
                        Pot = new Potion
                        {
                            Heal = reader.GetInt16("healpotion"),
                            Mana = reader.GetInt16("manapotion")
                        }
                    };
                    reader.Close();
                }
                else
                {
                    reader.Close();
                    command.CommandText = $"INSERT INTO `players`(`id`,`money`,`diamonds`,`lastmessage`,`name`,`clanid`,`level`,`xp`,`fight`,`locationid`,`movingto`,`stepsleft`,`health`,`maxhealth`,`mana`,`maxmana`,`grind`,`attacksteps`,`attackerid`,`attackertype`,`nextcall`,`portals`,`cemetry`) VALUES({user.Id},1000,1,0,'{user.FirstName} {user.LastName}',0,1,0,0,1,0,0,10,10,0,0,0,0,0,0,0,'9',5); INSERT INTO `stats`(`playerid`,`deaths`,`skillpoints`,`mobskilled`,`playerskilled`,`bosseskilled`,`caravanskilled`,`tournaments`,`damagerate`,`attackrate`,`defenserate`,`criticalrate`,`healrate`,`magicrate`,`tournament`,`glory`,`part`) VALUES({user.Id},0,3,0,0,0,0,0,2,5,5,1,3,0,0,0,1); INSERT INTO `equipment`(`playerid`,`helmetid`,`plateid`,`pantsid`,`bootsid`,`weaponid`,`shieldid`,`runeid`,`bookid`,`petid`,`helmetlevel`,`platelevel`,`pantslevel`,`bootslevel`,`weaponlevel`,`shieldlevel`,`runelevel`,`booklevel`,`petlevel`) VALUES({playerId},0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);INSERT INTO `potions`(`playerid`,`healpotion`,`manapotion`) VALUES({playerId},0,0)";
                    command.ExecuteNonQuery();
                    player = new Player
                    {
                        Id = playerId,
                        Money = 1000,
                        Diamonds = 0,
                        LastMessage = 0,
                        Name = $"{user.FirstName} {user.LastName}",
                        ClanId = 0,
                        Level = 1,
                        Xp = 0,
                        Fight = false,
                        LocationId = 1,
                        MovingTo = 0,
                        StepsLeft = 0,
                        Health = 10,
                        MaxHealth = 10,
                        Grind = false,
                        AttackSteps = 0,
                        AttackerId = 0,
                        AttackerType = false,
                        NextCall = 0,
                        Portals = new List<int>() { 9 },
                        Stats = new Stat
                        {
                            Deaths = 0,
                            SkillPoints = 3,
                            MobsKilled = 0,
                            PlayersKilled = 0,
                            BossesKilled = 0,
                            CaravansKilled = 0,
                            Tournaments = 0,
                            DamageRate = 2,
                            AttackRate = 5,
                            DefenseRate = 5,
                            CriticalRate = 1,
                            HealRate = 3,
                            MagicRate = 0,
                            Tournament = false,
                            Glory = 0,
                            Part = true
                        },
                        Equipt = new Equipment
                        {
                            HelmetId = 0,
                            PlateId = 0,
                            PantsId = 0,
                            BootsId = 0,
                            WeaponId = 0,
                            ShieldId = 0,
                            RuneId = 0,
                            BookId = 0
                        },
                        Pot = new Potion
                        {
                            Heal = 0,
                            Mana = 0
                        }
                    };
                }
                return player;
            }
            catch (Exception e)
            {
                return GetPlayer(user.Id, user);
            }
        }
        #endregion

        #region Получение данных локации
        static Location GetLocation(int locationId)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i].Id == locationId)
                {
                    return locations[i];
                }
            }
            return null;
        }
        #endregion

        #region Получение данных моба
        static Mob GetMob(int mobId, int locationId)
        {
            Mob mob = new Mob();
            for (int i = 0; i < mobs.Count; i++)
            {
                if (mobId == mobs[i].Id)
                {
                    mob.Id = mobs[i].Id;
                    mob.Name = mobs[i].Name;
                    mob.Health = mobs[i].Health;
                    mob.Damage = mobs[i].Damage;
                    mob.Level = GetLocation(locationId).EntityLevel;
                    mob.Damage = Convert.ToInt32(mob.Damage + mob.Damage * ((mob.Level - 1) * 0.75));
                    mob.Health = Convert.ToInt32(mob.Health + mob.Health * ((mob.Level - 1) * 0.55));
                    mob.MaxHealth = mob.Health;
                    return mob;
                }
            }
            return null;
        }

        #endregion

        #region Получение списка доступных локаций
        static List<string> GetAvailableLocations(int locationId)
        {
            List<string> locationsIds = new List<string>();
            command.CommandText = $"SELECT `lr`.`location1` as `l1`,`lr`.`location2` as `l2`,`lr`.`steps` FROM `locations` as `l` JOIN `locations_relations` as `lr` ON `lr`.`location1`=`l`.`id` OR `lr`.`location2`=`l`.`id` WHERE `l`.`id`={locationId}";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt16("l1") == locationId)
                {
                    locationsIds.Add(reader.GetInt16("l2").ToString() + "_" + reader.GetInt16("steps").ToString());
                }
                else
                {
                    locationsIds.Add(reader.GetInt16("l1").ToString() + "_" + reader.GetInt16("steps").ToString());
                }
            }
            reader.Close();

            return locationsIds;
        }
        #endregion

        #region Получение онлайн игроков
        static List<Player> GetOnline(long? playerId, System.Collections.ObjectModel.ReadOnlyCollection<User> users)
        {
            List<Player> players = new List<Player>();

            for (int i = 0; i < users.Count; i++)
            {
                if (users[i].Online == true && users[i].Id != playerId)
                {
                    players.Add(GetPlayer(users[i].Id, users[i]));
                }
            }
            return players;
        }
        #endregion

        #region Получение игроков на локации
        static List<Player> GetPlayersOnLocation(int locationId, long? playerId, System.Collections.ObjectModel.ReadOnlyCollection<User> users)
        {
            List<Player> players = new List<Player>();
            for (int i = 0; i < users.Count; i++)
            {
                Player player = GetPlayer(users[i].Id, users[i]);
                if (player.LocationId == locationId && player.Fight == false && player.Id != playerId && player.StepsLeft == 0)
                {
                    players.Add(GetPlayer(users[i].Id, users[i]));
                }
            }
            return players;
        }
        #endregion

        static void Main()
        {
            api.Authorize(new ApiAuthParams
            {
                AccessToken = "303008961e7b06b769ff19669b2689ecfe533b9364958de2d8c151e6667fb5c56cb4a4c6e1318b83db1de"
            });

            string connectionParameters = "Server=localhost;Database=sectumdictator;Port=3306;User=root;Password=;SslMode=none";
            connection = new MySqlConnection(connectionParameters);
            connection.Open();
            command = new MySqlCommand() { Connection = connection };

            command.CommandText = $"SELECT * FROM `locations`";
            MySqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                locations.Add(new Location
                {
                    Id = reader.GetInt16("id"),
                    Name = reader.GetString("name"),
                    Pvp = reader.GetBoolean("pvp"),
                    EntityLevel = reader.GetInt16("entitylevel"),
                    Battle = reader.GetInt16("battle")
                });
            }
            reader.Close();

            command.CommandText = $"SELECT * FROM `mobs`";
            reader = command.ExecuteReader();
            while (reader.Read())
            {
                mobs.Add(new Mob
                {
                    Id = reader.GetInt16("id"),
                    Name = reader.GetString("name"),
                    Health = reader.GetInt64("health"),
                    Damage = reader.GetInt64("damage"),
                    Level = 0,
                    MaxHealth = 0
                });
            }
            reader.Close();

            while (true)
            {
                System.Collections.ObjectModel.ReadOnlyCollection<User> users = api.Groups.GetMembers(new GroupsGetMembersParams() { GroupId = "204849626", Fields = UsersFields.Online });

                #region Получение заданий сервера
                command.CommandText = "SELECT * FROM `tasks`";
                reader = command.ExecuteReader();
                List<string> TasksNames = new List<string>();
                List<long> TasksPlayersIds = new List<long>();
                List<int> TasksIds = new List<int>();
                while (reader.Read())
                {
                    TasksNames.Add(reader.GetString("task"));
                    TasksPlayersIds.Add(reader.GetInt64("playerid"));
                    TasksIds.Add(reader.GetInt32("id"));
                }
                reader.Close();
                #endregion

                #region Турниры
                command.CommandText = "SELECT * FROM `tournaments` ORDER BY `id` DESC LIMIT 1";
                reader = command.ExecuteReader();
                Tournament tournament = null;
                while (reader.Read())
                {
                    tournament = new Tournament()
                    {
                        Id = reader.GetInt16("id"),
                        Stage = reader.GetInt16("stage"),
                        NextStage = reader.GetInt16("nextstage"),
                        Members = reader.GetInt16("members")
                    };
                }
                reader.Close();


                if (tournament.Stage == 0 && tournament.NextStage == 0 && tournament.Members > 0)
                {
                    Message($"🏆 Турнир начался!", 2000000001, false);
                    tournament.Stage = 1;
                    tournament.NextStage = 5;
                }

                command.CommandText = $"UPDATE `players` SET `stepsleft`=`stepsleft`-1,`xp`=`xp`+2 WHERE `stepsleft`>0;UPDATE `players` SET `nextcall`=`nextcall`-1 WHERE `nextcall`>0;UPDATE `bosses` SET `attacksteps` = `attacksteps`-1 WHERE `emerge`=0 AND `attacksteps`>0;UPDATE `bosses` SET `emerge`=`emerge`-1 WHERE `emerge`>0;UPDATE `tournaments` SET `stage`=1, `nextstage`=5 WHERE `stage`=0 AND `nextstage`=0 AND `members`>0;UPDATE `tournaments` SET `nextstage`=`nextstage`-1 WHERE `nextstage`>0 AND `stage`=0;UPDATE `auction` SET `left`=`left`-1 WHERE `left`>0 AND `beterid`!=0";
                command.ExecuteNonQuery();

                command.CommandText = "SELECT * FROM `auction` WHERE `left`<=0";
                reader = command.ExecuteReader();

                List<AuctionItem> soldItems = new List<AuctionItem>();

                while (reader.Read())
                {
                    soldItems.Add(new AuctionItem()
                    {
                        Id = reader.GetInt32("id"),
                        ItemId = reader.GetInt32("itemid"),
                        Level = reader.GetInt16("level"),
                        OwnerId = reader.GetInt64("ownerid"),
                        BeterId = reader.GetInt64("beterid"),
                        Bet = reader.GetInt32("bet"),
                        Left = reader.GetInt16("left")
                    });
                }
                reader.Close();

                int soldCount = soldItems.Count;

                for (int i = 0; i < soldCount; i++)
                {
                    command.CommandText = $"UPDATE `players` SET `diamonds`=`diamonds`+{soldItems[i].Bet} WHERE `id`={soldItems[i].OwnerId};INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({soldItems[i].BeterId},{soldItems[i].ItemId},{soldItems[i].Level});DELETE FROM `auction` WHERE `id`={soldItems[i].Id}";
                    command.ExecuteNonQuery();
                    Message($"📦 Лот №{soldItems[i].Id} успешно продан за {soldItems[i].Bet} Алмазов", soldItems[i].OwnerId, true);
                    Message($"📦 Лот №{soldItems[i].Id} успешно куплен за {soldItems[i].Bet} Алмазов", soldItems[i].BeterId, true);
                }

                if (tournament.Stage == 1)
                {
                    command.CommandText = "SELECT COUNT(*) as `count` FROM `players`,`stats` WHERE `fight` = 0 AND `tournament` = 1 AND `stats`.`playerid`=`players`.`id`";
                    int count = 0;
                    reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        count = reader.GetInt32("count");
                    }
                    reader.Close();

                    if (count == tournament.Members)
                    {
                        if (tournament.NextStage == -1)
                        {
                            if (count > 1)
                            {
                                tournament.NextStage = 5;
                                command.CommandText = $"UPDATE `tournaments` SET `nextstage`=5 WHERE `id`={tournament.Id}";
                                command.ExecuteNonQuery();
                            }
                            else
                            {
                                long id = 0;
                                string name = "";
                                command.CommandText = $"SELECT `id`,`name` FROM `players`,`stats` WHERE `stats`.`playerid`=`players`.`id` AND `stats`.`tournament`=1";
                                reader = command.ExecuteReader();
                                while (reader.Read())
                                {
                                    id = reader.GetInt64("id");
                                    name = reader.GetString("name");
                                }
                                reader.Close();

                                command.CommandText = $"UPDATE `tournaments` SET `stage`=2,`nextstage`=6,`members`=0,`winner`={id} WHERE `id`={tournament.Id};UPDATE `stats` SET `tournaments`=`tournaments`+1, `tournament`=0, `glory`=`glory`+100 WHERE `playerid`={id};UPDATE `players` SET `diamonds`=`diamonds`+5 WHERE `id`={id};INSERT INTO `tournaments`(stage,nextstage,members,winner) VALUES(0,1000,0,0)";
                                command.ExecuteNonQuery();

                                Message($"👑 @id{id} ({name}) Победил в турнире и получил 💎 5 алмазов и 🎗 100 очков славы\n🏆 Следующий турнир через 1000 ходов", 2000000001, false);
                                Message($"👑 Вы победили в турнире и получили 💎 5 алмазов и 🎗 100 очков славы", id, true);
                            }
                        }
                        if (count % 2 != 0 && tournament.NextStage == 5)
                        {
                            long id = 0;
                            command.CommandText = "SELECT `id` FROM `players`,`stats` WHERE `stats`.`tournament`=0 AND `players`.`level`=(SELECT MAX(`level`) FROM `players`,`stats` WHERE `players`.`id`=`stats`.`playerid` AND `stats`.`tournament`=0 AND `stats`.`part`=1) AND `players`.`id`=`stats`.`playerid`";
                            reader = command.ExecuteReader();
                            while (reader.Read())
                            {
                                id = reader.GetInt64("id");
                            }
                            reader.Close();
                            command.CommandText = $"UPDATE `players` SET `movingto`=0, `stepsleft`=0, `grind`=0, `fight` = 0, `diamonds`=`diamonds`+1 WHERE `id`={id}";
                            command.ExecuteNonQuery();

                            command.CommandText = $"UPDATE `tournaments` SET `members`=`members`+1 WHERE `id` = {tournament.Id};UPDATE `stats` SET `tournament`=1 WHERE `playerid`={id}";
                            command.ExecuteNonQuery();
                            Message("🏆 Из-за недостатка игроков вы вынужденно участвуете в турнире (💎 Награда: 1 Алмаз)", id, true);
                        }
                        else if (tournament.NextStage != 0)
                        {
                            command.CommandText = $"UPDATE `tournaments` SET `nextstage`=`nextstage`-1 WHERE `id`={tournament.Id}";
                            command.ExecuteNonQuery();
                        }
                        else
                        {
                            List<Player> ids = new List<Player>();
                            command.CommandText = $"SELECT `id`,`level`,`name` FROM `players`,`stats` WHERE `stats`.`playerid`=`players`.`id` AND `stats`.`tournament`=1 ORDER BY `players`.`level` DESC";
                            reader = command.ExecuteReader();
                            while (reader.Read())
                            {
                                ids.Add(new Player()
                                {
                                    Name = reader.GetString("name"),
                                    Id = reader.GetInt64("id"),
                                    Level = reader.GetInt32("level")
                                });
                            }
                            reader.Close();

                            int idsCount = ids.Count;
                            string answer = "🏆 Турнирная сетка:\n";

                            for (int i = 0; i < idsCount / 2; i++)
                            {

                                answer += $"\n@id{ids[i].Id} ({ids[i].Name}) 🆚 @id{ids[idsCount - i - 1].Id} ({ids[idsCount - i - 1].Name})";

                                Message($"⚔ Вы начали турнирный бой с игроком: {ids[idsCount - i - 1].Name}", ids[i].Id, false);
                                Message($"⚔ С вами начал турнирный бой игрок: {ids[i].Name}", ids[idsCount - i - 1].Id, false);
                                command.CommandText = $"UPDATE `players` SET `fight`=1, `attacksteps`=5,`attackerid`={ids[idsCount - i - 1].Id},`attackertype`=1,`health`=(10 + 10 * (100 *0.55)),`mana`=`maxmana` WHERE `id`={ids[i].Id};UPDATE `players` SET `fight`=1, `attacksteps`=10,`attackerid`={ids[i].Id},`attackertype`=1,`health`=(10 + 10 * (100 *0.55)),`mana`=`maxmana` WHERE `id`={ids[idsCount - i - 1].Id};UPDATE `tournaments` SET `nextstage`=-1 WHERE `id`={tournament.Id}";
                                command.ExecuteNonQuery();
                            }

                            Message(answer, 2000000001, false);
                        }
                    }
                }
                else if (tournament.Stage == 0 && tournament.NextStage == 10)
                {
                    Message($"🏆 До начала турнира осталось 10 ходов, успейте присоединиться!", 2000000001, false);
                }
                #endregion

                #region Боссы
                List<int> bossesSteps = new List<int>();
                List<int> bossesLocations = new List<int>();
                List<int> bossesIds = new List<int>();
                List<int> bossesHealth = new List<int>();
                List<string> bossesDrops = new List<string>();
                List<string> bossesPlayers = new List<string>();
                command.CommandText = $"SELECT * FROM `bosses` WHERE `emerge`=0";
                reader = command.ExecuteReader();
                while (reader.Read())
                {
                    bossesSteps.Add(reader.GetInt32("attacksteps"));
                    if (reader.GetInt32("attacksteps") == 0)
                    {
                        bossesLocations.Add(reader.GetInt32("locationid"));
                        bossesIds.Add(reader.GetInt32("mobid"));
                        bossesHealth.Add(reader.GetInt32("healthleft"));
                        bossesPlayers.Add(reader.GetString("players"));
                        bossesDrops.Add(reader.GetString("drop"));
                    }
                }
                reader.Close();

                for (int i = 0; i < bossesIds.Count; i++)
                {
                    if (bossesSteps[i] == 0)
                    {
                        List<Player> players = new List<Player>();

                        if (bossesPlayers[i] != "")
                        {
                            for (int j = 0; j < bossesPlayers[i].Split('_').Length; j++)
                            {
                                players.Add(GetPlayer(Convert.ToInt64(bossesPlayers[i].Split('_')[j]), null));
                            }
                        }

                        long fullDamage = 0;
                        Mob mob = GetMob(bossesIds[i], bossesLocations[i]);
                        mob.Health = bossesHealth[i];
                        string answer = "";

                        int defenseRate = mob.Level * 4;
                        int attackRate = mob.Level * 5;
                        int criticalRate = mob.Level * 2;
                        bool stan = false;

                        for (int j = 0; j < players.Count; j++)
                        {
                            Player player = players[j];
                            player.Pet = GetPet(player.Id);
                            command.CommandText = $"UPDATE `players` SET `fight`=1 WHERE `id`={player.Id}";
                            command.ExecuteNonQuery();
                            List<int> bonuses = GetBonuses(new List<Item>() { GetEquipedItem(player.Equipt.HelmetId, player.Id), GetEquipedItem(player.Equipt.PlateId, player.Id), GetEquipedItem(player.Equipt.PantsId, player.Id), GetEquipedItem(player.Equipt.BootsId, player.Id), GetEquipedItem(player.Equipt.WeaponId, player.Id), GetEquipedItem(player.Equipt.ShieldId, player.Id), GetEquipedItem(player.Equipt.RuneId, player.Id), GetEquipedItem(player.Equipt.BookId, player.Id), new Item() { Bonuses = player.Pet.Bonuses, Level = player.Pet.Level, Id = player.Pet.Id } });
                            double ratio = (Convert.ToDouble(mob.Level * 10) / Convert.ToDouble(mob.Level * 10 + defenseRate));
                            long damage = Convert.ToInt32(((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100));
                            long petDamage = Convert.ToInt32(((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100)) / 3;

                            int hitChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) / (Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) + defenseRate) * player.Level / Convert.ToDouble(player.Level + mob.Level));
                            int spellChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) / (Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) + defenseRate) * player.Level / Convert.ToDouble(player.Level + mob.Level));
                            spellChance = Math.Max(spellChance, 10);
                            int criticalChance = Convert.ToInt32(50 * Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) + defenseRate * 2));
                            int healChance = Convert.ToInt32(Convert.ToDouble(player.Stats.HealRate + bonuses[3]) * 17.5) / player.Level;

                            int chance = rnd.Next(1, 101);
                            bool spell = false;
                            bool potion = false;

                            if (player.Health <= player.MaxHealth / 4)
                            {
                                if (player.Pot.Heal > 0)
                                {
                                    player.Health = Math.Min(player.MaxHealth, player.Health + player.MaxHealth / 4);
                                    answer += $"\n\n🌿 {player.Name} применяет 💉 Зелье здоровья и восстанавливает {player.MaxHealth / 4} здоровья";
                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id};UPDATE `potions` SET `healpotion`=`healpotion`-1 WHERE `playerid`={player.Id}";
                                    command.ExecuteNonQuery();
                                    potion = true;
                                    damage = 0;
                                }
                            }
                            else if (player.Mana <= player.MaxMana / 4 && player.MaxMana > 0)
                            {
                                if (player.Pot.Mana > 0)
                                {
                                    player.Mana = Math.Min(player.MaxMana, player.Mana + player.MaxMana / 4);
                                    answer += $"\n\n🌿 {player.Name} применяет ⚗ Зелье маны и восстанавливает {player.MaxMana / 4} маны";
                                    command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id};UPDATE `potions` SET `manapotion`=`manapotion`-1 WHERE `playerid`={player.Id}";
                                    command.ExecuteNonQuery();
                                    potion = true;
                                    damage = 0;
                                }
                            }

                            if (potion == false)
                            {
                                List<Spell> spells = new List<Spell>();
                                Item item = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                for (int l = 0; l < item.Spells.Count; l++)
                                {
                                    spells.Add(item.Spells[l]);
                                }
                                item = GetEquipedItem(player.Equipt.BookId, player.Id);
                                for (int l = 0; l < item.Spells.Count; l++)
                                {
                                    spells.Add(item.Spells[l]);
                                }
                                item = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                for (int l = 0; l < item.Spells.Count; l++)
                                {
                                    spells.Add(item.Spells[l]);
                                }
                                if (spells.Count > 0 && player.Mana > (player.Stats.MagicRate + bonuses[5]) / 5)
                                {
                                    if (chance <= spells.Count * 25)
                                    {
                                        spell = true;
                                        player.Mana -= (player.Stats.MagicRate + bonuses[5]) / 5;
                                        command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();

                                        chance = rnd.Next(1, 101);

                                        if (chance <= spellChance)
                                        {

                                            int spellId = rnd.Next(0, spells.Count);

                                            if (spells[spellId].Type == "fire")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                            }
                                            else if (spells[spellId].Type == "fireball")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                            }
                                            else if (spells[spellId].Type == "burst")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {damage} урона 💥 оглушая {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                stan = true;
                                            }
                                            else if (spells[spellId].Type == "heal")
                                            {
                                                damage = 0;
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100))} здоровья\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)));
                                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                            }
                                            else if (spells[spellId].Type == "plague")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {damage} здоровья у {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                player.Health = Math.Min(player.MaxHealth, player.Health + damage);
                                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                            }
                                            else if (spells[spellId].Type == "flash")
                                            {
                                                damage = 0;
                                                answer += $"\n\n✨ {player.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                stan = true;
                                            }
                                            else if (spells[spellId].Type == "echo")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                            }
                                            else if (spells[spellId].Type == "ice")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {damage} урона 💥 замораживая {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                stan = true;
                                            }
                                            else if (spells[spellId].Type == "vortex")
                                            {
                                                damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                answer += $"\n\n✨ {player.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                            }
                                        }
                                        else
                                        {
                                            damage = 0;
                                            answer += $"\n\n✨ {player.Name} не смог применить заклинание\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                        }
                                    }
                                }
                            }

                            if (spell == false && potion == false)
                            {
                                if (chance <= hitChance)
                                {
                                    chance = rnd.Next(1, 101);
                                    if (chance <= criticalChance)
                                    {
                                        damage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / defenseRate)) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));
                                        answer += $"\n\n⚔ {player.Name} 💥 попадает критически и наносит 🖤 {damage} урона";
                                    }
                                    else
                                    {
                                        answer += $"\n\n⚔ {player.Name} 🥊 попадает и наносит 🖤 {damage} урона";
                                    }
                                }
                                else
                                {
                                    damage = 0;
                                    if (player.Health < player.MaxHealth)
                                    {
                                        chance = rnd.Next(1, 101);

                                        if (chance <= healChance)
                                        {
                                            player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                            answer += $"\n\n💚 {player.Name} восстанавливает {Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100))} здоровья";
                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();
                                        }
                                        else
                                        {
                                            answer += $"\n\n⚔ {player.Name} 💭 промахивается";
                                        }
                                    }
                                    else
                                    {
                                        answer += $"\n\n⚔ {player.Name} 💭 промахивается";
                                    }
                                }
                            }

                            if (player.Pet.Id != 0)
                            {
                                List<Spell> spells = new List<Spell>();
                                for (int l = 0; l < player.Pet.Spells.Count; l++)
                                {
                                    if (player.Pet.Spells[l].Type != "block")
                                    {
                                        spells.Add(player.Pet.Spells[l]);
                                    }
                                }
                                chance = rnd.Next(1, 101);

                                if (chance <= spells.Count * 25)
                                {
                                    int spellId = rnd.Next(0, spells.Count);

                                    if (spells[spellId].Type == "fire")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {petDamage} урона";
                                    }
                                    else if (spells[spellId].Type == "fireball")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {petDamage} урона";
                                    }
                                    else if (spells[spellId].Type == "burst")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {petDamage} урона 💥 оглушая {mob.Name}";
                                        stan = true;
                                    }
                                    else if (spells[spellId].Type == "heal")
                                    {
                                        petDamage = 0;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает хозяину {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3} здоровья";
                                        player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3);
                                        command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();
                                    }
                                    else if (spells[spellId].Type == "plague")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {petDamage} здоровья у {mob.Name} для хозяина";
                                        player.Health = Math.Min(player.MaxHealth, player.Health + petDamage);
                                        command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();
                                    }
                                    else if (spells[spellId].Type == "flash")
                                    {
                                        petDamage = 0;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {mob.Name}";
                                        stan = true;
                                    }
                                    else if (spells[spellId].Type == "echo")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {petDamage} урона";
                                    }
                                    else if (spells[spellId].Type == "ice")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {petDamage} урона 💥 замораживая {mob.Name}";
                                        stan = true;
                                    }
                                    else if (spells[spellId].Type == "vortex")
                                    {
                                        petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {petDamage} урона";
                                    }
                                }
                                else
                                {
                                    if (chance <= hitChance)
                                    {
                                        chance = rnd.Next(1, 101);
                                        if (chance <= criticalChance)
                                        {
                                            petDamage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / defenseRate)) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                            answer += $"\n🦊 {player.Pet.Name} 💥 попадает критически и наносит 🖤 {petDamage} урона";
                                        }
                                        else
                                        {
                                            answer += $"\n🦊 {player.Pet.Name} 🥊 попадает и наносит 🖤 {petDamage} урона";
                                        }
                                    }
                                    else
                                    {
                                        petDamage = 0;
                                        answer += $"\n🦊 {player.Pet.Name} 💭 промахивается";
                                    }
                                }
                            }
                            else
                            {
                                petDamage = 0;
                            }

                            fullDamage += damage + petDamage;
                        }
                        if (mob.Health - fullDamage > 0 && players.Count > 0)
                        {
                            command.CommandText = $"UPDATE `bosses` SET `attacksteps`=5,`healthleft`=`healthleft`-{fullDamage} WHERE `mobid`={bossesIds[i]}";
                            command.ExecuteNonQuery();

                            answer += $"\n\n❤ Здоровье босса: {mob.Health - fullDamage}/{mob.MaxHealth}";

                            Player player = players[rnd.Next(0, players.Count)];
                            player.Pet = GetPet(player.Id);
                            if (stan == false)
                            {
                                List<int> bonuses = GetBonuses(new List<Item>() { GetEquipedItem(player.Equipt.HelmetId, player.Id), GetEquipedItem(player.Equipt.PlateId, player.Id), GetEquipedItem(player.Equipt.PantsId, player.Id), GetEquipedItem(player.Equipt.BootsId, player.Id), GetEquipedItem(player.Equipt.WeaponId, player.Id), GetEquipedItem(player.Equipt.ShieldId, player.Id), GetEquipedItem(player.Equipt.RuneId, player.Id), GetEquipedItem(player.Equipt.BookId, player.Id), new Item() { Bonuses = player.Pet.Bonuses, Level = player.Pet.Level, Id = player.Pet.Id } });
                                double ratio = (Convert.ToDouble(player.Level * 10) / Convert.ToDouble(player.Level * 10 + player.Stats.DefenseRate + bonuses[1]));
                                long damage = Convert.ToInt32(mob.Damage * ratio * ((100 - Convert.ToDouble(bonuses[7])) / 100));

                                int hitChance = Convert.ToInt32(200 * attackRate / (attackRate + Convert.ToDouble(player.Stats.DefenseRate + bonuses[1])) * mob.Level / (player.Level + mob.Level));
                                int criticalChance = Convert.ToInt32(50 * criticalRate / (criticalRate + Convert.ToDouble(player.Stats.DefenseRate + bonuses[1]) * 2));
                                int chance = rnd.Next(1, 101);
                                if (chance <= hitChance)
                                {
                                    chance = rnd.Next(1, 101);
                                    if (chance <= criticalChance)
                                    {
                                        damage *= Convert.ToInt32((150 + criticalRate * (criticalRate / Convert.ToDouble(player.Stats.DefenseRate + bonuses[1]))) / 100);
                                        answer += $"\n\n⚔ {mob.Name} 💥 попадает критически и наносит 🖤 {damage} урона игроку {player.Name}";
                                    }
                                    else
                                    {
                                        answer += $"\n\n⚔ {mob.Name} 🥊 попадает и наносит 🖤 {damage} урона игроку {player.Name}";
                                    }
                                }
                                else
                                {
                                    damage = 0;
                                    answer += $"\n\n⚔ {mob.Name} 💭 промахивается";
                                }

                                if (player.Pet.Id != 0)
                                {
                                    List<Spell> spells = new List<Spell>();
                                    for (int l = 0; l < player.Pet.Spells.Count; l++)
                                    {
                                        if (player.Pet.Spells[l].Type == "block")
                                        {
                                            spells.Add(player.Pet.Spells[l]);
                                        }
                                    }

                                    chance = rnd.Next(1, 101);

                                    if (chance <= spells.Count * 25)
                                    {
                                        int spellId = rnd.Next(0, spells.Count);
                                        answer += $"\n🦊 {player.Pet.Name} применяет заклинание 🛡 {spells[spellId].Name} и блокирует {damage} урона";
                                        damage = 0;
                                    }
                                }

                                if (player.Health - damage > 0)
                                {
                                    answer += $"\n❤ Здоровье игрока {player.Name}: {player.Health - damage}/{player.MaxHealth}";
                                    command.CommandText = $"UPDATE `players` SET `attacksteps`=4,`health`={player.Health - damage} WHERE `id`={player.Id};";
                                    command.ExecuteNonQuery();
                                }
                                else
                                {
                                    long gold = Convert.ToInt64(player.Level * rnd.Next(25, 51));

                                    answer += $"\n☠ {player.Name} погибает и теряет 💰 {gold} золота";

                                    string newPlayers = "";

                                    for (int j = 0; j < players.Count; j++)
                                    {
                                        if (players[j].Id != player.Id)
                                        {
                                            if (newPlayers == "")
                                            {
                                                newPlayers += players[j].Id;
                                            }
                                            else
                                            {
                                                newPlayers += $"_{players[j].Id}";
                                            }
                                        }
                                    }
                                    command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`={Math.Max(0, player.Money - gold)},`health`=`maxhealth`,`mana`=`maxmana`,`locationid`=`cemetry`,`grind`=0 WHERE `id`={player.Id};UPDATE `stats` SET `deaths`=`deaths`+1 WHERE `playerid`={player.Id};UPDATE `bosses` SET `players` = '{newPlayers}' WHERE `mobid`={mob.Id}";
                                    command.ExecuteNonQuery();
                                }
                            }
                            else
                            {
                                answer += $"\n\n⚔ {mob.Name} 💥 оглушён";
                            }
                        }
                        else if (players.Count > 0)
                        {
                            long gold = Convert.ToInt64(mob.Level * rnd.Next(25, 51) * 4);
                            int xp = Convert.ToInt32(Convert.ToDouble(mob.Level + mob.Level / 2) * 100 / 2);

                            string type = "";
                            string update = "";

                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={bossesLocations[i]}";
                            reader = command.ExecuteReader();
                            if (reader.HasRows)
                            {
                                reader.Read();
                                type = reader.GetString("type");
                            }
                            reader.Close();
                            if (type == "caravan")
                            {
                                gold *= 2;
                                update = "`caravanskilled`=`caravanskilled`+1";
                                answer += $"\n\n☠ Караван уничтожен";
                            }
                            else if (type == "boss")
                            {
                                update = "`bosseskilled`=`bosseskilled`+1";
                                answer += $"\n\n☠ Босс повержен";
                            }
                            answer += $"\n\n📊 Опыта получено: {xp}\n💰 Золота получено: {gold}";
                            command.CommandText = $"UPDATE `bosses` SET `attacksteps`=5, `healthleft`={mob.MaxHealth}, `emerge`=`emergence`,`players`='' WHERE `mobid`={bossesIds[i]}";
                            command.ExecuteNonQuery();

                            for (int j = 0; j < players.Count; j++)
                            {
                                command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`={Math.Max(0, players[j].Money + gold)},`xp`=`xp`+{xp} WHERE `id`={players[j].Id};UPDATE `stats` SET {update} WHERE `playerid`={players[j].Id}";
                                command.ExecuteNonQuery();
                            }
                        }

                        for (int j = 0; j < players.Count; j++)
                        {
                            if (mob.Health - fullDamage <= 0)
                            {
                                Message(answer, players[j].Id, true);
                                BossDrop(players[j].Id, bossesDrops[i], mob.Level);
                                DropPotions(players[j].Id);
                            }
                            else
                            {
                                Message(answer, players[j].Id, false);
                            }
                        }
                    }
                }
                #endregion

                for (int u = 0; u < users.Count; u++)
                {
                    Player player = GetPlayer(users[u].Id, users[u]);

                    player.Pet = GetPet(player.Id);

                    List<int> bonuses = GetBonuses(new List<Item>() { GetEquipedItem(player.Equipt.HelmetId, player.Id), GetEquipedItem(player.Equipt.PlateId, player.Id), GetEquipedItem(player.Equipt.PantsId, player.Id), GetEquipedItem(player.Equipt.BootsId, player.Id), GetEquipedItem(player.Equipt.WeaponId, player.Id), GetEquipedItem(player.Equipt.ShieldId, player.Id), GetEquipedItem(player.Equipt.RuneId, player.Id), GetEquipedItem(player.Equipt.BookId, player.Id), new Item() { Bonuses = player.Pet.Bonuses, Level = player.Pet.Level, Id = player.Pet.Id } });

                    #region Задания сервера

                    int tasksCount = TasksNames.Count;

                    for (int i = 0; i < tasksCount; i++)
                    {
                        if (TasksPlayersIds[i] == player.Id)
                        {
                            if (TasksNames[i] == "stats")
                            {
                                if (player.NextCall <= 0)
                                {
                                    string clanNameT = "";

                                    if (player.ClanId != 0)
                                    {
                                        clanNameT = $"\n🏛 Клан: {GetClan(player.ClanId).Name}";
                                    }
                                    string answerT = $"👤 Имя: { player.Name}\n🎗 Очки славы: {player.Stats.Glory}{clanNameT}\n\n🔱 Уровень: { player.Level}\n💫 Очков умений: { player.Stats.SkillPoints}\n❤ Здоровье: { player.Health}/{ player.MaxHealth}\n💙 Мана: {player.Mana}/{player.MaxMana}\n📊 Опыт: { player.Xp}/{ (player.Level + player.Level / 2) * 100}\n💰 Золото: { player.Money}\n💎 Алмазы: {player.Diamonds}\n\n☠ Смертей: { player.Stats.Deaths}\n🐜 Мобов убито: { player.Stats.MobsKilled}\n💀 Игроков убито: { player.Stats.PlayersKilled}\n👹 Боссов убито: { player.Stats.BossesKilled}\n🐪 Караванов перехвачено: {player.Stats.CaravansKilled}\n🏆 Турниров выиграно: {player.Stats.Tournaments}\n";

                                    if (bonuses[4] == 0)
                                    {
                                        answerT += $"\n🔪 Уровень урона: { Convert.ToInt32(player.Stats.DamageRate + player.Stats.DamageRate * ((player.Level - 1) * 0.25))}";
                                    }
                                    else
                                    {
                                        answerT += $"\n🔪 Уровень урона: { Convert.ToInt32(player.Stats.DamageRate + player.Stats.DamageRate * ((player.Level - 1) * 0.25))} + {bonuses[4]}";
                                    }
                                    if (bonuses[0] == 0)
                                    {
                                        answerT += $"\n👊🏻 Уровень силы: {player.Stats.AttackRate}";
                                    }
                                    else
                                    {
                                        answerT += $"\n👊🏻 Уровень силы: {player.Stats.AttackRate} + {bonuses[0] * 5}";
                                    }
                                    if (bonuses[1] == 0)
                                    {
                                        answerT += $"\n🛡 Уровень защиты: {player.Stats.DefenseRate}";
                                    }
                                    else
                                    {
                                        answerT += $"\n🛡 Уровень защиты: {player.Stats.DefenseRate} + {bonuses[1]}";
                                    }
                                    if (bonuses[2] == 0)
                                    {
                                        answerT += $"\n💥 Уровень боя: {player.Stats.CriticalRate}";
                                    }
                                    else
                                    {
                                        answerT += $"\n💥 Уровень боя: {player.Stats.CriticalRate} + {bonuses[2]}";
                                    }
                                    if (bonuses[3] == 0)
                                    {
                                        answerT += $"\n💚 Уровень лечения: {player.Stats.HealRate}";
                                    }
                                    else
                                    {
                                        answerT += $"\n💚 Уровень лечения: {player.Stats.HealRate} + {bonuses[3]}";
                                    }
                                    if (bonuses[5] == 0)
                                    {
                                        answerT += $"\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                    }
                                    else
                                    {
                                        answerT += $"\n✨ Уровень волшебства: {player.Stats.MagicRate} + {bonuses[5]}";
                                    }

                                    Message(answerT, 2000000001, false);

                                    command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id};DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                                else
                                {
                                    Message("🚫 Нужно подождать", 2000000001, false);

                                    command.CommandText = $"DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                            }
                            else if (TasksNames[i] == "inventory")
                            {
                                if (player.NextCall <= 0)
                                {
                                    List<Item> items = new List<Item>();
                                    command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                    reader = command.ExecuteReader();
                                    while (reader.Read())
                                    {
                                        items.Add(new Item()
                                        {
                                            Id = reader.GetInt32("itemid"),
                                            Level = reader.GetInt16("level")
                                        });
                                    }
                                    reader.Close();

                                    string answer = $"💼 Инвентарь игрока {player.Name} ";

                                    if (items.Count > 0)
                                    {
                                        answer += ": ";
                                        for (int j = 0; j < items.Count; j++)
                                        {
                                            answer += "\n\n";
                                            Item item = GetInventoryItem(items[j].Id);
                                            items[j].Bonuses = item.Bonuses;
                                            items[j].MinLevel = item.MinLevel;
                                            items[j].Name = item.Name;
                                            items[j].Requests = item.Requests;
                                            items[j].Type = item.Type;

                                            if (item.Type == "helmet")
                                            {
                                                answer += $"🧢 ";
                                            }
                                            else if (item.Type == "plate")
                                            {
                                                answer += $"👕 ";
                                            }
                                            else if (item.Type == "pants")
                                            {
                                                answer += $"👖 ";
                                            }
                                            else if (item.Type == "boots")
                                            {
                                                answer += $"👟 ";
                                            }
                                            else if (item.Type == "weapon")
                                            {
                                                answer += $"🔪 ";
                                            }
                                            else if (item.Type == "shield")
                                            {
                                                answer += $"🛡 ";
                                            }
                                            else if (item.Type == "rune")
                                            {
                                                answer += $"🀄 ";
                                            }
                                            else if (item.Type == "book")
                                            {
                                                answer += $"📖 ";
                                            }

                                            answer += $"{items[j].Name} ";
                                            answer += GetItemInfo(item, items[j].Level, "task");
                                        }
                                    }
                                    answer += $"\n\n💉 Зелья здоровья: {player.Pot.Heal}/10\n⚗ Зелья маны: {player.Pot.Mana}/10";

                                    Message(answer, 2000000001, false);
                                    command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id};DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                                else
                                {
                                    Message("🚫 Нужно подождать", 2000000001, false);

                                    command.CommandText = $"DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                            }
                            else if (TasksNames[i] == "equipment")
                            {
                                if (player.NextCall <= 0)
                                {
                                    string equipment = $"🥋 Снаряжение игрока {player.Name} :\n";
                                    equipment += "\n🧢 Голова: ";
                                    if (player.Equipt.HelmetId > 0)
                                    {
                                        Item helmet = GetEquipedItem(player.Equipt.HelmetId, player.Id);
                                        equipment += helmet.Name;
                                        equipment += GetItemInfo(helmet, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n👕 Тело: ";
                                    if (player.Equipt.PlateId > 0)
                                    {
                                        Item plate = GetEquipedItem(player.Equipt.PlateId, player.Id);
                                        equipment += plate.Name;
                                        equipment += GetItemInfo(plate, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n👖 Ноги: ";
                                    if (player.Equipt.PantsId > 0)
                                    {
                                        Item pants = GetEquipedItem(player.Equipt.PantsId, player.Id);
                                        equipment += pants.Name;
                                        equipment += GetItemInfo(pants, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n👟 Ботинки: ";
                                    if (player.Equipt.BootsId > 0)
                                    {
                                        Item boots = GetEquipedItem(player.Equipt.BootsId, player.Id);
                                        equipment += boots.Name;
                                        equipment += GetItemInfo(boots, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n🔪 Оружие: ";
                                    if (player.Equipt.WeaponId > 0)
                                    {
                                        Item weapon = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                        equipment += weapon.Name;
                                        equipment += GetItemInfo(weapon, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n🛡 Щит: ";
                                    if (player.Equipt.ShieldId > 0)
                                    {
                                        Item shield = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                        equipment += shield.Name;
                                        equipment += GetItemInfo(shield, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n🀄 Руна: ";
                                    if (player.Equipt.RuneId > 0)
                                    {
                                        Item rune = GetEquipedItem(player.Equipt.RuneId, player.Id);
                                        equipment += rune.Name;
                                        equipment += GetItemInfo(rune, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    equipment += "\n\n📖 Книга: ";
                                    if (player.Equipt.BookId > 0)
                                    {
                                        Item book = GetEquipedItem(player.Equipt.BookId, player.Id);
                                        equipment += book.Name;
                                        equipment += GetItemInfo(book, 0, "task");
                                    }
                                    else
                                    {
                                        equipment += "Пусто";
                                    }

                                    Message(equipment, 2000000001, false);
                                    command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id};DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                                else
                                {
                                    Message("🚫 Нужно подождать", 2000000001, false);

                                    command.CommandText = $"DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                            }
                            else if (TasksNames[i] == "pet")
                            {
                                if (player.NextCall <= 0)
                                {
                                    Pet pet = GetPet(player.Id);
                                    if (pet.Id != 0)
                                    {
                                        Message($"🦊 Питомец игрока {player.Name}: {GetPetInfo(pet)}", 2000000001, false);
                                        command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id};DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                        command.ExecuteNonQuery();
                                    }
                                    else
                                    {
                                        Message($"🦊 У игрока {player.Name} нет питомца", 2000000001, false);
                                        command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id};DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                        command.ExecuteNonQuery();
                                    }
                                }
                                else
                                {
                                    Message("🚫 Нужно подождать", 2000000001, false);

                                    command.CommandText = $"DELETE FROM `tasks` WHERE `id`={TasksIds[i]}";
                                    command.ExecuteNonQuery();
                                }
                            }
                            break;
                        }
                    }
                    #endregion

                    int manaRatio = 0;

                    if (player.MaxMana > 0)
                    {
                        manaRatio = Convert.ToInt32(Convert.ToDouble(player.Mana) / Convert.ToDouble(player.MaxMana));
                    }

                    #region Бой
                    if (player.Fight)
                    {
                        string type = "";

                        command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                        reader = command.ExecuteReader();
                        if (reader.HasRows)
                        {
                            reader.Read();
                            type = reader.GetString("type");
                        }
                        reader.Close();

                        if (player.Stats.Tournament == false)
                        {
                            player.MaxMana = bonuses[6];

                            command.CommandText = $"UPDATE `players` SET `maxmana`={bonuses[6]} WHERE `id`={player.Id};UPDATE `players` SET `mana`=`maxmana` WHERE `id`={player.Id} AND `mana`>`maxmana`";
                            command.ExecuteNonQuery();

                            if (type != "boss" && type != "caravan")
                            {
                                if (player.AttackerType)
                                {
                                    if (player.AttackSteps == 0)
                                    {
                                        Player enemy = GetPlayer(player.AttackerId, users[u]);
                                        player.Pet = GetPet(player.Id);
                                        enemy.Pet = GetPet(enemy.Id);

                                        List<int> enemyBonuses = GetBonuses(new List<Item>() { GetEquipedItem(enemy.Equipt.HelmetId, enemy.Id), GetEquipedItem(enemy.Equipt.PlateId, enemy.Id), GetEquipedItem(enemy.Equipt.PantsId, enemy.Id), GetEquipedItem(enemy.Equipt.BootsId, enemy.Id), GetEquipedItem(enemy.Equipt.WeaponId, enemy.Id), GetEquipedItem(enemy.Equipt.ShieldId, enemy.Id), GetEquipedItem(enemy.Equipt.RuneId, enemy.Id), GetEquipedItem(enemy.Equipt.BookId, enemy.Id), new Item() { Bonuses = enemy.Pet.Bonuses, Level = enemy.Pet.Level, Id = enemy.Pet.Id } });

                                        double ratio = (Convert.ToDouble(enemy.Level * 10) / Convert.ToDouble(enemy.Level * 10 + enemy.Stats.DefenseRate + enemyBonuses[1]));
                                        long damage = Convert.ToInt32(((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                        long petDamage = damage / 3;
                                        int criticalChance = Convert.ToInt32(50 * Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) + Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1]) * 2));
                                        int hitChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) / (Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) + Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1])) * player.Level / Convert.ToDouble(player.Level + enemy.Level));
                                        int spellChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) / (Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) + Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1])) * player.Level / Convert.ToDouble(player.Level + enemy.Level));
                                        spellChance = Math.Max(spellChance, 10);
                                        int healChance = Convert.ToInt32(Convert.ToDouble(player.Stats.HealRate + bonuses[3]) * 17.5) / player.Level;
                                        string attackText = "";
                                        int chance = rnd.Next(1, 101);

                                        bool spell = false;
                                        bool stan = false;
                                        bool potion = false;

                                        if (player.Health <= player.MaxHealth / 4)
                                        {
                                            int pot = rnd.Next(0, 2);

                                            if (player.Pot.Mana > 0 && pot == 0)
                                            {
                                                player.Health = Math.Min(player.MaxHealth, player.Health + player.MaxHealth / 4);
                                                attackText = $"🌿 {player.Name} применяет 💉 Зелье здоровья и восстанавливает {player.MaxHealth / 4} здоровья";
                                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id};UPDATE `potions` SET `healpotion`=`healpotion`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                potion = true;
                                                damage = 0;
                                            }
                                        }
                                        else if (player.Mana <= player.MaxMana / 4 && player.MaxMana > 0)
                                        {
                                            int pot = rnd.Next(0, 2);

                                            if (player.Pot.Mana > 0 && pot == 0)
                                            {
                                                player.Mana = Math.Min(player.MaxMana, player.Mana + player.MaxMana / 4);
                                                attackText = $"🌿 { player.Name} применяет ⚗ Зелье маны и восстанавливает {player.MaxMana / 4} маны";
                                                command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id};UPDATE `potions` SET `manapotion`=`manapotion`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                potion = true;
                                                damage = 0;
                                            }
                                        }

                                        if (potion == false)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            Item item = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            item = GetEquipedItem(player.Equipt.BookId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            item = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            if (spells.Count > 0 && player.Mana > (player.Stats.MagicRate + bonuses[5]) / 5)
                                            {
                                                if (chance <= spells.Count * 25)
                                                {
                                                    spell = true;
                                                    player.Mana -= (player.Stats.MagicRate + bonuses[5]) / 5;

                                                    command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();

                                                    chance = rnd.Next(1, 101);

                                                    if (chance <= spellChance)
                                                    {
                                                        int spellId = rnd.Next(0, spells.Count);

                                                        if (spells[spellId].Type == "fire")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "fireball")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "burst")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {damage} урона 💥 оглушая {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "heal")
                                                        {
                                                            damage = 0;
                                                            attackText = $"✨ {player.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100))} здоровья\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)));
                                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                        }
                                                        else if (spells[spellId].Type == "plague")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {damage} здоровья у {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            player.Health = Math.Min(player.MaxHealth, player.Health + damage);
                                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                        }
                                                        else if (spells[spellId].Type == "flash")
                                                        {
                                                            damage = 0;
                                                            attackText = $"✨ {player.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "echo")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "ice")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {damage} урона 💥 замораживая {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "vortex")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                    }
                                                    else
                                                    {
                                                        damage = 0;
                                                        attackText = $"✨ {player.Name} не смог применить заклинание\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    }
                                                }
                                            }
                                        }

                                        if (spell == false && potion == false)
                                        {
                                            if (chance <= hitChance)
                                            {
                                                chance = rnd.Next(1, 101);
                                                if (chance <= criticalChance)
                                                {
                                                    damage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1]))) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                                    attackText = $"⚔ {player.Name} 💥 попадает критически и наносит 🖤 {damage} урона";
                                                }
                                                else
                                                {
                                                    attackText = $"⚔ {player.Name} 🥊 попадает и наносит 🖤 {damage} урона";
                                                }
                                            }
                                            else
                                            {
                                                damage = 0;
                                                if (player.Health < player.MaxHealth)
                                                {
                                                    chance = rnd.Next(1, 101);

                                                    if (chance <= healChance)
                                                    {
                                                        player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                                        attackText = $"💚 {player.Name} восстанавливает {Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100))} здоровья";
                                                        command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                    }
                                                    else
                                                    {
                                                        attackText = $"⚔ {player.Name} 💭 промахивается";
                                                    }
                                                }
                                                else
                                                {
                                                    attackText = $"⚔ {player.Name} 💭 промахивается";
                                                }
                                            }
                                        }

                                        if (player.Pet.Id != 0)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            for (int i = 0; i < player.Pet.Spells.Count; i++)
                                            {
                                                if (player.Pet.Spells[i].Type != "block")
                                                {
                                                    spells.Add(player.Pet.Spells[i]);
                                                }
                                            }
                                            chance = rnd.Next(1, 101);

                                            if (chance <= spells.Count * 25)
                                            {
                                                int spellId = rnd.Next(0, spells.Count);

                                                if (spells[spellId].Type == "fire")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "fireball")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "burst")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {petDamage} урона 💥 оглушая {enemy.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "heal")
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает хозяину {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3} здоровья";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3);
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "plague")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {petDamage} здоровья у {enemy.Name} для хозяина";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + petDamage);
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "flash")
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {enemy.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "echo")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "ice")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {petDamage} урона 💥 замораживая {enemy.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "vortex")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                            }
                                            else
                                            {
                                                if (chance <= hitChance)
                                                {
                                                    chance = rnd.Next(1, 101);
                                                    if (chance <= criticalChance)
                                                    {
                                                        petDamage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1]))) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                                        attackText += $"\n🦊 {player.Pet.Name} 💥 попадает критически и наносит 🖤 {petDamage} урона";
                                                    }
                                                    else
                                                    {
                                                        attackText += $"\n🦊 {player.Pet.Name} 🥊 попадает и наносит 🖤 {petDamage} урона";
                                                    }
                                                }
                                                else
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} 💭 промахивается";
                                                }
                                            }
                                        }
                                        else
                                        {
                                            petDamage = 0;
                                        }

                                        if (enemy.Pet.Id != 0)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            for (int i = 0; i < enemy.Pet.Spells.Count; i++)
                                            {
                                                if (enemy.Pet.Spells[i].Type == "block")
                                                {
                                                    spells.Add(enemy.Pet.Spells[i]);
                                                }
                                            }

                                            chance = rnd.Next(1, 101);

                                            if (chance <= spells.Count * 25)
                                            {
                                                int spellId = rnd.Next(0, spells.Count);
                                                attackText += $"\n🦊 {enemy.Pet.Name} применяет заклинание 🛡 {spells[spellId].Name} и блокирует {damage + petDamage} урона";
                                                damage = 0;
                                                petDamage = 0;
                                                stan = false;
                                            }
                                        }

                                        if (enemy.Health - damage - petDamage > 0)
                                        {
                                            Message($"{attackText}\n❤ Здоровье врага: {enemy.Health - damage - petDamage}/{enemy.MaxHealth}\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}", player.Id, false);
                                            Message($"{attackText}\n❤ Здоровье врага: {player.Health}/{player.MaxHealth}\n❤ Ваше здоровье: {enemy.Health - damage - petDamage}/{enemy.MaxHealth}", enemy.Id, false);
                                        }
                                        else
                                        {
                                            long gold = enemy.Money / 4;
                                            int xp = Convert.ToInt32(Convert.ToDouble(enemy.Level + enemy.Level / 2) * 100 / 8);
                                            Message($"⚔ PVP Информатор\n\n👑 Победитель: {player.Name} @id{player.Id} (👁‍🗨)\n🔱 Уровень: {player.Level}\n❤ Оставшееся здоровье: {player.Health}/{player.MaxHealth}\n📊 Опыта получено: {xp}\n💰 Золота получено: {gold}\n\n💀 Проигравший: {enemy.Name} @id{enemy.Id} (👁‍🗨)\n🔱 Уровень: {enemy.Level}\n💰 Золота потеряно: {gold}", 2000000001, false);
                                            Message($"{attackText}\n☠ Враг мёртв\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}\n\n📊 Опыта получено: {xp}\n💰 Золота получено: {gold}", player.Id, true);
                                            Message($"{attackText}\n❤ Здоровье врага: {player.Health}/{player.MaxHealth}\n☠ Вы мертвы\n\n💰 Золота потеряно: {gold}", enemy.Id, true);

                                            command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`=`money`+{gold},`xp`=`xp`+{xp} WHERE `id`={player.Id};UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`={Math.Max(0, enemy.Money - gold)},`health`=`maxhealth`,`mana`=`maxmana`,`locationid`=`cemetry`,`grind`=0 WHERE `id`={enemy.Id};UPDATE `stats` SET `playerskilled`=`playerskilled`+1 WHERE `playerid`={player.Id};UPDATE `stats` SET `deaths`=`deaths`+1 WHERE `playerid`={enemy.Id}";
                                            command.ExecuteNonQuery();

                                            enemy = GetPlayer(enemy.Id, users[u]);
                                            player = GetPlayer(player.Id, users[u]);
                                        }

                                        if (player.Fight == true)
                                        {
                                            if (stan)
                                            {
                                                command.CommandText = $"UPDATE `players` SET `attacksteps`=5 WHERE `id`={player.Id};UPDATE `players` SET `health`={enemy.Health - damage - petDamage},`attacksteps`=10 WHERE `id`={enemy.Id};";
                                                command.ExecuteNonQuery();
                                            }
                                            else
                                            {
                                                command.CommandText = $"UPDATE `players` SET `attacksteps`=10 WHERE `id`={player.Id};UPDATE `players` SET `health`={enemy.Health - damage - petDamage} WHERE `id`={enemy.Id};";
                                                command.ExecuteNonQuery();
                                            }
                                        }
                                    }
                                    else
                                    {
                                        command.CommandText = $"UPDATE `players` SET `attacksteps`=`attacksteps`-1 WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();
                                    }
                                }
                                else
                                {
                                    Mob mob = GetMob(Convert.ToInt32(player.AttackerId % 1000), player.LocationId);
                                    mob.Health = (player.AttackerId - player.AttackerId % 1000) / 1000;
                                    player.Pet = GetPet(player.Id);
                                    if (player.AttackSteps == 0)
                                    {
                                        int defenseRate = Convert.ToInt32(mob.Level * (Convert.ToDouble(player.Stats.DefenseRate + bonuses[1]) / player.Level));
                                        double ratio = (Convert.ToDouble(mob.Level * 10) / Convert.ToDouble(mob.Level * 10 + defenseRate));
                                        long damage = Convert.ToInt32(((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100));
                                        long petDamage = Convert.ToInt32(((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100)) / 3;
                                        int hitChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) / (Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) + defenseRate) * player.Level / Convert.ToDouble(player.Level + mob.Level));
                                        int spellChance = Convert.ToInt32(200 * Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) / (Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) + defenseRate) * player.Level / Convert.ToDouble(player.Level + mob.Level));
                                        spellChance = Math.Max(spellChance, 10);
                                        int criticalChance = Convert.ToInt32(50 * Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) + defenseRate * 2));
                                        int healChance = Convert.ToInt32(Convert.ToDouble(player.Stats.HealRate + bonuses[3]) * 17.5) / player.Level;
                                        string attackText = "";
                                        int chance = rnd.Next(1, 101);
                                        bool spell = false;
                                        bool stan = false;
                                        bool potion = false;

                                        if (player.Health <= player.MaxHealth / 4)
                                        {
                                            int pot = rnd.Next(0, 2);

                                            if (player.Pot.Mana > 0 && pot == 0)
                                            {
                                                player.Health = Math.Min(player.MaxHealth, player.Health + player.MaxHealth / 4);
                                                attackText = $"🌿 {player.Name} применяет 💉 Зелье здоровья и восстанавливает {player.MaxHealth / 4} здоровья";
                                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id};UPDATE `potions` SET `healpotion`=`healpotion`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                potion = true;
                                                damage = 0;
                                            }
                                        }
                                        else if (player.Mana <= player.MaxMana / 4 && player.MaxMana > 0)
                                        {
                                            int pot = rnd.Next(0, 2);

                                            if (player.Pot.Mana > 0 && pot == 0)
                                            {
                                                player.Mana = Math.Min(player.MaxMana, player.Mana + player.MaxMana / 4);
                                                attackText = $"🌿 {player.Name} применяет ⚗ Зелье маны и восстанавливает {player.MaxMana / 4} маны";
                                                command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id};UPDATE `potions` SET `manapotion`=`manapotion`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                potion = true;
                                                damage = 0;
                                            }
                                        }
                                        if (potion == false)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            Item item = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            item = GetEquipedItem(player.Equipt.BookId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            item = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                            for (int l = 0; l < item.Spells.Count; l++)
                                            {
                                                spells.Add(item.Spells[l]);
                                            }
                                            if (spells.Count > 0 && player.Mana > (player.Stats.MagicRate + bonuses[5]) / 5)
                                            {
                                                if (chance <= spells.Count * 25)
                                                {
                                                    spell = true;
                                                    player.Mana -= (player.Stats.MagicRate + bonuses[5]) / 5;
                                                    command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();

                                                    chance = rnd.Next(1, 101);

                                                    if (chance <= spellChance)
                                                    {
                                                        int spellId = rnd.Next(0, spells.Count);

                                                        if (spells[spellId].Type == "fire")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "fireball")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "burst")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {damage} урона 💥 оглушая {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "heal")
                                                        {
                                                            damage = 0;
                                                            attackText = $"✨ {player.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100))} здоровья\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)));
                                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                        }
                                                        else if (spells[spellId].Type == "plague")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {damage} здоровья у {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            player.Health = Math.Min(player.MaxHealth, player.Health + damage);
                                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                        }
                                                        else if (spells[spellId].Type == "flash")
                                                        {
                                                            damage = 0;
                                                            attackText = $"✨ {player.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "echo")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                        else if (spells[spellId].Type == "ice")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {damage} урона 💥 замораживая {mob.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                            stan = true;
                                                        }
                                                        else if (spells[spellId].Type == "vortex")
                                                        {
                                                            damage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100));
                                                            attackText = $"✨ {player.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                        }
                                                    }
                                                    else
                                                    {
                                                        damage = 0;
                                                        attackText = $"✨ {player.Name} не смог применить заклинание\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    }
                                                }
                                            }
                                        }

                                        if (spell == false && potion == false)
                                        {
                                            if (chance <= hitChance)
                                            {
                                                chance = rnd.Next(1, 101);
                                                if (chance <= criticalChance)
                                                {
                                                    damage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / defenseRate)) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                                    attackText = $"⚔ {player.Name} 💥 попадает критически и наносит 🖤 {damage} урона";
                                                }
                                                else
                                                {
                                                    attackText = $"⚔ {player.Name} 🥊 попадает и наносит 🖤 {damage} урона";
                                                }
                                            }
                                            else
                                            {
                                                damage = 0;
                                                if (player.Health < player.MaxHealth)
                                                {
                                                    chance = rnd.Next(1, 101);

                                                    if (chance <= healChance)
                                                    {
                                                        player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                                        attackText = $"💚 {player.Name} восстанавливает {Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100))} здоровья";
                                                        command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                    }
                                                    else
                                                    {
                                                        attackText = $"⚔ {player.Name} 💭 промахивается";
                                                    }
                                                }
                                                else
                                                {
                                                    attackText = $"⚔ {player.Name} 💭 промахивается";
                                                }
                                            }
                                        }

                                        if (player.Pet.Id != 0)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            for (int i = 0; i < player.Pet.Spells.Count; i++)
                                            {
                                                if (player.Pet.Spells[i].Type != "block")
                                                {
                                                    spells.Add(player.Pet.Spells[i]);
                                                }
                                            }
                                            chance = rnd.Next(1, 101);

                                            if (chance <= spells.Count * 25)
                                            {
                                                int spellId = rnd.Next(0, spells.Count);

                                                if (spells[spellId].Type == "fire")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "fireball")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "burst")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {petDamage} урона 💥 оглушая {mob.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "heal")
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает хозяину {Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3} здоровья";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.MagicRate + bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3);
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "plague")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {petDamage} здоровья у {mob.Name} для хозяина";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + petDamage);
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "flash")
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {mob.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "echo")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                                else if (spells[spellId].Type == "ice")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble((player.Stats.MagicRate + bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {petDamage} урона 💥 замораживая {mob.Name}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "vortex")
                                                {
                                                    petDamage = Convert.ToInt64(Convert.ToDouble(player.Stats.MagicRate + bonuses[5] + ((Convert.ToDouble(player.Stats.DamageRate) + Convert.ToDouble(player.Stats.DamageRate) * (Convert.ToDouble(player.Level - 1) * 0.25)) + bonuses[4])) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3;
                                                    attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {petDamage} урона";
                                                }
                                            }
                                            else
                                            {
                                                if (chance <= hitChance)
                                                {
                                                    chance = rnd.Next(1, 101);
                                                    if (chance <= criticalChance)
                                                    {
                                                        petDamage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / defenseRate)) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                                        attackText += $"\n🦊 {player.Pet.Name} 💥 попадает критически и наносит 🖤 {petDamage} урона";
                                                    }
                                                    else
                                                    {
                                                        attackText += $"\n🦊 {player.Pet.Name} 🥊 попадает и наносит 🖤 {petDamage} урона";
                                                    }
                                                }
                                                else
                                                {
                                                    petDamage = 0;
                                                    attackText += $"\n🦊 {player.Pet.Name} 💭 промахивается";
                                                }
                                            }
                                        }
                                        else
                                        {
                                            petDamage = 0;
                                        }

                                        if (mob.Health - damage - petDamage > 0)
                                        {
                                            Message($"{attackText}\n❤ Здоровье моба: {mob.Health - damage - petDamage}/{mob.MaxHealth}\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}", player.Id, false);
                                        }
                                        else
                                        {
                                            long gold = Convert.ToInt64(mob.Level * rnd.Next(25, 51));
                                            int xp = Convert.ToInt32(Convert.ToDouble(mob.Level + mob.Level / 2) * 100 / 8);
                                            Message($"{attackText}\n☠ Моб мёртв\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}\n\n📊 Опыта получено: {xp}\n💰 Золота получено: {gold}", player.Id, true);

                                            command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`={Math.Max(0, player.Money + gold)},`xp`=`xp`+{xp} WHERE `id`={player.Id};UPDATE `stats` SET `mobskilled`=`mobskilled`+1 WHERE `playerid`={player.Id}";
                                            command.ExecuteNonQuery();

                                            player = GetPlayer(player.Id, users[u]);

                                            Drop(player.Id, player.LocationId);
                                            DropPotions(player.Id);
                                        }

                                        if (player.Fight == true)
                                        {
                                            if (stan == false)
                                            {
                                                command.CommandText = $"UPDATE `players` SET `attacksteps`=10, `attackerid`={mob.Id + 1000 * (mob.Health - damage - petDamage)} WHERE `id`={player.Id};";
                                                command.ExecuteNonQuery();
                                            }
                                            else
                                            {
                                                command.CommandText = $"UPDATE `players` SET `attacksteps`=4, `attackerid`={mob.Id + 1000 * (mob.Health - damage - petDamage)} WHERE `id`={player.Id};";
                                                command.ExecuteNonQuery();
                                            }
                                        }
                                    }
                                    else if (player.AttackSteps == 5)
                                    {
                                        int criticalRate = Convert.ToInt32(mob.Level * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / player.Level));
                                        int attackRate = Convert.ToInt32(mob.Level * (Convert.ToDouble(player.Stats.AttackRate + bonuses[0] * 5) / player.Level));
                                        double ratio = (Convert.ToDouble(player.Level * 10) / Convert.ToDouble(player.Level * 10 + player.Stats.DefenseRate + bonuses[1]));
                                        long damage = Convert.ToInt32(mob.Damage * ratio * ((100 - Convert.ToDouble(bonuses[7])) / 100));
                                        int hitChance = Convert.ToInt32(200 * attackRate / (attackRate + Convert.ToDouble(player.Stats.DefenseRate + bonuses[1])) * mob.Level / (player.Level + mob.Level));
                                        int criticalChance = Convert.ToInt32(50 * criticalRate / (criticalRate + Convert.ToDouble(player.Stats.DefenseRate + bonuses[1]) * 2));
                                        string attackText;
                                        int chance = rnd.Next(1, 101);
                                        if (chance <= hitChance)
                                        {
                                            chance = rnd.Next(1, 101);
                                            if (chance <= criticalChance)
                                            {
                                                damage *= Convert.ToInt32((150 + criticalRate * (criticalRate / Convert.ToDouble(player.Stats.DefenseRate + bonuses[1]))) / 100);

                                                attackText = $"⚔ {mob.Name} 💥 попадает критически и наносит 🖤 {damage} урона";
                                            }
                                            else
                                            {
                                                attackText = $"⚔ {mob.Name} 🥊 попадает и наносит 🖤 {damage} урона";
                                            }
                                        }
                                        else
                                        {
                                            damage = 0;
                                            attackText = $"⚔ {mob.Name} 💭 промахивается";
                                        }

                                        if (player.Pet.Id != 0)
                                        {
                                            List<Spell> spells = new List<Spell>();
                                            for (int i = 0; i < player.Pet.Spells.Count; i++)
                                            {
                                                if (player.Pet.Spells[i].Type == "block")
                                                {
                                                    spells.Add(player.Pet.Spells[i]);
                                                }
                                            }

                                            chance = rnd.Next(1, 101);

                                            if (chance <= spells.Count * 25)
                                            {
                                                int spellId = rnd.Next(0, spells.Count);
                                                attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🛡 {spells[spellId].Name} и блокирует {damage} урона";
                                                damage = 0;
                                            }
                                        }

                                        if (player.Health - damage > 0)
                                        {
                                            Message($"{attackText}\n❤ Здоровье моба: {mob.Health}/{mob.MaxHealth}\n❤ Ваше здоровье: {player.Health - damage}/{player.MaxHealth}", player.Id, false);
                                        }
                                        else
                                        {
                                            long gold = Convert.ToInt64(player.Level * rnd.Next(25, 51));
                                            Message($"{attackText}\n❤ Здоровье моба: {mob.Health}/{mob.MaxHealth}\n☠ Вы мертвы\n\n💰 Золота потеряно: {gold}", player.Id, true);

                                            command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`money`={Math.Max(0, player.Money - gold)},`health`=`maxhealth`,`mana`=`maxmana`,`locationid`=`cemetry`,`grind`=0 WHERE `id`={player.Id};UPDATE `stats` SET `deaths`=`deaths`+1 WHERE `playerid`={player.Id}";
                                            command.ExecuteNonQuery();

                                            player = GetPlayer(player.Id, users[u]);
                                        }

                                        if (player.Fight == true)
                                        {
                                            command.CommandText = $"UPDATE `players` SET `attacksteps`=4,`health`={player.Health - damage} WHERE `id`={player.Id};";
                                            command.ExecuteNonQuery();
                                        }
                                    }
                                    else
                                    {
                                        command.CommandText = $"UPDATE `players` SET `attacksteps`=`attacksteps`-1 WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                        else
                        {
                            bonuses = GetBonuses(new List<Item>() { GetEquipedTournamentItem(player.Equipt.HelmetId, player.Id), GetEquipedTournamentItem(player.Equipt.PlateId, player.Id), GetEquipedTournamentItem(player.Equipt.PantsId, player.Id), GetEquipedTournamentItem(player.Equipt.BootsId, player.Id), GetEquipedTournamentItem(player.Equipt.WeaponId, player.Id), GetEquipedTournamentItem(player.Equipt.ShieldId, player.Id), GetEquipedTournamentItem(player.Equipt.RuneId, player.Id), GetEquipedTournamentItem(player.Equipt.BookId, player.Id), new Item() { Bonuses = player.Pet.Bonuses, Level = 100, Id = player.Pet.Id } });

                            player.MaxMana = bonuses[6];
                            player.Mana = Convert.ToInt64(player.MaxMana * manaRatio);
                            player.MaxHealth = Convert.ToInt64(10 + 10 * (100 * 0.55));

                            command.CommandText = $"UPDATE `players` SET `maxmana`={bonuses[6]},`mana`=`maxmana`*{manaRatio} WHERE `id`={player.Id};UPDATE `players` SET `mana`=`maxmana` WHERE `id`={player.Id} AND `mana`>`maxmana`";
                            command.ExecuteNonQuery();

                            if (player.AttackSteps == 0)
                            {
                                Player enemy = GetPlayer(player.AttackerId, users[u]);
                                player.Pet = GetPet(player.Id);
                                enemy.Pet = GetPet(enemy.Id);
                                enemy.MaxHealth = Convert.ToInt64(10 + 10 * (100 * 0.55));

                                List<int> enemyBonuses = GetBonuses(new List<Item>() { GetEquipedTournamentItem(enemy.Equipt.HelmetId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.PlateId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.PantsId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.BootsId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.WeaponId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.ShieldId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.RuneId, enemy.Id), GetEquipedTournamentItem(enemy.Equipt.BookId, enemy.Id), new Item() { Bonuses = enemy.Pet.Bonuses, Level = 100, Id = enemy.Pet.Id } });
                                double ratio = (Convert.ToDouble(100 * 10) / Convert.ToDouble(100 * 10 + 5 + enemyBonuses[1]));
                                long damage = Convert.ToInt32(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));

                                long petDamage = Convert.ToInt32(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) * ratio * ((100 + Convert.ToDouble(bonuses[10])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;

                                int criticalChance = Convert.ToInt32(50 * Convert.ToDouble(1 + bonuses[2]) / (Convert.ToDouble(1 + bonuses[2]) + Convert.ToDouble(5 + enemyBonuses[1]) * 2));
                                int hitChance = Convert.ToInt32(200 * Convert.ToDouble(5 + bonuses[0] * 5) / (Convert.ToDouble(5 + bonuses[0] * 5) + Convert.ToDouble(5 + enemyBonuses[1])) * 100 / Convert.ToDouble(100 + 100));

                                int spellChance = 10;

                                if (bonuses[5] != 0)
                                {
                                    spellChance = Convert.ToInt32(200 * bonuses[5] / (Convert.ToDouble(bonuses[5]) + Convert.ToDouble(5 + enemyBonuses[1])) * 100 / Convert.ToDouble(100 + 100));
                                }
                                int healChance = Convert.ToInt32(Convert.ToDouble(3 + bonuses[3]) * 17.5) / 100;

                                string attackText = "";
                                int chance = rnd.Next(1, 101);

                                bool spell = false;
                                bool stan = false;
                                bool potion = false;

                                if (player.Health <= player.MaxHealth / 4)
                                {
                                    int pot = rnd.Next(0, 2);

                                    if (player.Pot.Heal > 0 && pot == 0)
                                    {
                                        player.Health = Math.Min(player.MaxHealth, player.Health + player.MaxHealth / 4);
                                        attackText = $"🌿 {player.Name} применяет 💉 Зелье здоровья и восстанавливает {player.MaxHealth / 4} здоровья";
                                        command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id};UPDATE `potions` SET `healpotion`=`healpotion`-1 WHERE `playerid`={player.Id}";
                                        command.ExecuteNonQuery();
                                        potion = true;
                                        damage = 0;
                                    }
                                }
                                else if (player.Mana <= player.MaxMana / 4 && player.MaxMana > 0)
                                {
                                    int pot = rnd.Next(0, 2);

                                    if (player.Pot.Mana > 0 && pot == 0)
                                    {
                                        player.Mana = Math.Min(player.MaxMana, player.Mana + player.MaxMana / 4);
                                        attackText = $"🌿 { player.Name} применяет ⚗ Зелье маны и восстанавливает {player.MaxMana / 4} маны";
                                        command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id};UPDATE `potions` SET `manapotion`=`manapotion`-1 WHERE `playerid`={player.Id}";
                                        command.ExecuteNonQuery();
                                        potion = true;
                                        damage = 0;
                                    }
                                }

                                if (potion == false)
                                {
                                    List<Spell> spells = new List<Spell>();
                                    Item item = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                    for (int l = 0; l < item.Spells.Count; l++)
                                    {
                                        spells.Add(item.Spells[l]);
                                    }
                                    item = GetEquipedItem(player.Equipt.BookId, player.Id);
                                    for (int l = 0; l < item.Spells.Count; l++)
                                    {
                                        spells.Add(item.Spells[l]);
                                    }
                                    item = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                    for (int l = 0; l < item.Spells.Count; l++)
                                    {
                                        spells.Add(item.Spells[l]);
                                    }
                                    if (spells.Count > 0 && player.Mana > (bonuses[5]) / 5)
                                    {
                                        if (chance <= spells.Count * 25)
                                        {
                                            spell = true;
                                            player.Mana -= (bonuses[5]) / 5;

                                            command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();

                                            chance = rnd.Next(1, 101);

                                            if (chance <= spellChance)
                                            {
                                                int spellId = rnd.Next(0, spells.Count);

                                                if (spells[spellId].Type == "fire")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble(bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                }
                                                else if (spells[spellId].Type == "fireball")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                }
                                                else if (spells[spellId].Type == "burst")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {damage} урона 💥 оглушая {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "heal")
                                                {
                                                    damage = 0;
                                                    attackText = $"✨ {player.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает {Convert.ToInt32((bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100))} здоровья\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)));
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "plague")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {damage} здоровья у {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    player.Health = Math.Min(player.MaxHealth, player.Health + damage);
                                                    command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                }
                                                else if (spells[spellId].Type == "flash")
                                                {
                                                    damage = 0;
                                                    attackText = $"✨ {player.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "echo")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                }
                                                else if (spells[spellId].Type == "ice")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {damage} урона 💥 замораживая {enemy.Name}\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                    stan = true;
                                                }
                                                else if (spells[spellId].Type == "vortex")
                                                {
                                                    damage = Convert.ToInt64(Convert.ToDouble(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) + bonuses[5]) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100));
                                                    attackText = $"✨ {player.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {damage} урона\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                                }
                                            }
                                            else
                                            {
                                                damage = 0;
                                                attackText = $"✨ {player.Name} не смог применить заклинание\n💙 Уровень маны: {player.Mana}/{player.MaxMana}";
                                            }
                                        }
                                    }
                                }

                                if (spell == false && potion == false)
                                {
                                    if (chance <= hitChance)
                                    {
                                        chance = rnd.Next(1, 101);
                                        if (chance <= criticalChance)
                                        {
                                            damage *= Convert.ToInt32((150 + Convert.ToDouble(1 + bonuses[2]) * (Convert.ToDouble(1 + bonuses[2]) / Convert.ToDouble(5 + enemyBonuses[1]))) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                            attackText = $"⚔ {player.Name} 💥 попадает критически и наносит 🖤 {damage} урона";
                                        }
                                        else
                                        {
                                            attackText = $"⚔ {player.Name} 🥊 попадает и наносит 🖤 {damage} урона";
                                        }
                                    }
                                    else
                                    {
                                        damage = 0;
                                        if (player.Health < player.MaxHealth)
                                        {
                                            chance = rnd.Next(1, 101);

                                            if (chance <= healChance)
                                            {
                                                player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((3 + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                                attackText = $"💚 {player.Name} восстанавливает {Convert.ToInt32((3 + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100))} здоровья";
                                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                            }
                                            else
                                            {
                                                attackText = $"⚔ {player.Name} 💭 промахивается";
                                            }
                                        }
                                        else
                                        {
                                            attackText = $"⚔ {player.Name} 💭 промахивается";
                                        }
                                    }
                                }

                                if (player.Pet.Id != 0)
                                {
                                    List<Spell> spells = new List<Spell>();
                                    for (int i = 0; i < player.Pet.Spells.Count; i++)
                                    {
                                        if (player.Pet.Spells[i].Type != "block")
                                        {
                                            spells.Add(player.Pet.Spells[i]);
                                        }
                                    }
                                    chance = rnd.Next(1, 101);

                                    if (chance <= spells.Count * 25)
                                    {
                                        int spellId = rnd.Next(0, spells.Count);

                                        if (spells[spellId].Type == "fire")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble(bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔥 {spells[spellId].Name} и наносит {petDamage} урона";
                                        }
                                        else if (spells[spellId].Type == "fireball")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 1.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ☄ {spells[spellId].Name} и наносит {petDamage} урона";
                                        }
                                        else if (spells[spellId].Type == "burst")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 1.35) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌩 {spells[spellId].Name} и наносит {petDamage} урона 💥 оглушая {enemy.Name}";
                                            stan = true;
                                        }
                                        else if (spells[spellId].Type == "heal")
                                        {
                                            petDamage = 0;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 💚 {spells[spellId].Name} и восстанавливает хозяину {Convert.ToInt32((bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3} здоровья";
                                            player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((bonuses[5]) * ((100 + Convert.ToDouble(bonuses[11])) / 100)) / 3);
                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();
                                        }
                                        else if (spells[spellId].Type == "plague")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🕷 {spells[spellId].Name} и крадёт {petDamage} здоровья у {enemy.Name} для хозяина";
                                            player.Health = Math.Min(player.MaxHealth, player.Health + petDamage);
                                            command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();
                                        }
                                        else if (spells[spellId].Type == "flash")
                                        {
                                            petDamage = 0;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ⚡ {spells[spellId].Name} и 💥 оглушает {enemy.Name}";
                                            stan = true;
                                        }
                                        else if (spells[spellId].Type == "echo")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) + bonuses[5]) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🔗 {spells[spellId].Name} и наносит {petDamage} урона";
                                        }
                                        else if (spells[spellId].Type == "ice")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble((bonuses[5]) * 0.5) * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание ❄ {spells[spellId].Name} и наносит {petDamage} урона 💥 замораживая {enemy.Name}";
                                            stan = true;
                                        }
                                        else if (spells[spellId].Type == "vortex")
                                        {
                                            petDamage = Convert.ToInt64(Convert.ToDouble(((2 + 2 * (Convert.ToDouble(100 - 1) * 0.25)) + bonuses[4]) + bonuses[5]) * 0.85 * ratio * ((100 + Convert.ToDouble(bonuses[11])) / 100) * ((100 - Convert.ToDouble(enemyBonuses[7])) / 100)) / 3;
                                            attackText += $"\n🦊 {player.Pet.Name} применяет заклинание 🌪 {spells[spellId].Name} и наносит {petDamage} урона";
                                        }
                                    }
                                    else
                                    {
                                        if (chance <= hitChance)
                                        {
                                            chance = rnd.Next(1, 101);
                                            if (chance <= criticalChance)
                                            {
                                                petDamage *= Convert.ToInt32((150 + Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) * (Convert.ToDouble(player.Stats.CriticalRate + bonuses[2]) / Convert.ToDouble(enemy.Stats.DefenseRate + enemyBonuses[1]))) / 100 * ((100 + Convert.ToDouble(bonuses[8])) / 100));

                                                attackText += $"\n🦊 {player.Pet.Name} 💥 попадает критически и наносит 🖤 {petDamage} урона";
                                            }
                                            else
                                            {
                                                attackText += $"\n🦊 {player.Pet.Name} 🥊 попадает и наносит 🖤 {petDamage} урона";
                                            }
                                        }
                                        else
                                        {
                                            petDamage = 0;
                                            attackText += $"\n🦊 {player.Pet.Name} 💭 промахивается";
                                        }
                                    }
                                }
                                else
                                {
                                    petDamage = 0;
                                }

                                if (enemy.Pet.Id != 0)
                                {
                                    List<Spell> spells = new List<Spell>();
                                    for (int i = 0; i < enemy.Pet.Spells.Count; i++)
                                    {
                                        if (enemy.Pet.Spells[i].Type == "block")
                                        {
                                            spells.Add(enemy.Pet.Spells[i]);
                                        }
                                    }

                                    chance = rnd.Next(1, 101);

                                    if (chance <= spells.Count * 25)
                                    {
                                        int spellId = rnd.Next(0, spells.Count);
                                        attackText += $"\n🦊 {enemy.Pet.Name} применяет заклинание 🛡 {spells[spellId].Name} и блокирует {damage + petDamage} урона";
                                        damage = 0;
                                        petDamage = 0;
                                        stan = false;
                                    }
                                }

                                if (enemy.Health - damage - petDamage > 0)
                                {
                                    Message($"{attackText}\n❤ Здоровье врага: {enemy.Health - damage - petDamage}/{enemy.MaxHealth}\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}", player.Id, false);
                                    Message($"{attackText}\n❤ Здоровье врага: {player.Health}/{player.MaxHealth}\n❤ Ваше здоровье: {enemy.Health - damage - petDamage}/{enemy.MaxHealth}", enemy.Id, false);
                                }
                                else
                                {
                                    int xp = Convert.ToInt32(Convert.ToDouble(enemy.Level + enemy.Level / 2) * 100 / 8);

                                    player.Stats.Glory += 10;
                                    enemy.Stats.Glory = Math.Max(0, enemy.Stats.Glory - 5);

                                    Message($"{attackText}\n☠ Враг мёртв\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}\n\n🎗 Очки славы: {player.Stats.Glory} (+10)\n📊 Опыта получено: {xp * 5}", player.Id, true);
                                    Message($"{attackText}\n❤ Здоровье врага: {player.Health}/{player.MaxHealth}\n☠ Вы мертвы\n\n🎗 Очки славы: {enemy.Stats.Glory} (-5)\n📊 Опыта получено: {xp}", enemy.Id, true);

                                    command.CommandText = $"UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`xp`=`xp`+{xp * 5},`health`=`maxhealth`,`mana`=`maxmana` WHERE `id`={player.Id};UPDATE `players` SET `fight`=0,`attacksteps`=0,`attackerid`=0,`attackertype`=0,`health`=`maxhealth`,`mana`=`maxmana`,`xp`=`xp`+{xp} WHERE `id`={enemy.Id};UPDATE `stats` SET `playerskilled`=`playerskilled`+1,`glory`=`glory`+10 WHERE `playerid`={player.Id};UPDATE `stats` SET `deaths`=`deaths`+1,`tournament`=0,`glory`={enemy.Stats.Glory} WHERE `playerid`={enemy.Id};UPDATE `tournaments` SET `members`=`members`-1 WHERE `id`={tournament.Id}";
                                    command.ExecuteNonQuery();

                                    if (tournament.Members == 2)
                                    {
                                        command.CommandText = $"UPDATE `players` SET `diamonds`=`diamonds`+3 WHERE `id`={enemy.Id};UPDATE `stats` SET `glory`=`glory`+50 WHERE `playerid`={enemy.Id}";
                                        command.ExecuteNonQuery();

                                        Message($"👑 @id{enemy.Id} ({enemy.Name}) Занял 2 место в турнире и получил 💎 3 алмаза и 🎗 50 очков славы", 2000000001, false);
                                        Message($"👑 Вы заняли 2 место в турнире и получили 💎 3 алмаза и 🎗 50 очков славы", enemy.Id, true);
                                    }

                                    enemy = GetPlayer(enemy.Id, users[u]);
                                    player = GetPlayer(player.Id, users[u]);
                                }

                                if (player.Fight == true)
                                {
                                    if (stan)
                                    {
                                        command.CommandText = $"UPDATE `players` SET `attacksteps`=5 WHERE `id`={player.Id};UPDATE `players` SET `health`={enemy.Health - damage - petDamage},`attacksteps`=10 WHERE `id`={enemy.Id};";
                                        command.ExecuteNonQuery();
                                    }
                                    else
                                    {
                                        command.CommandText = $"UPDATE `players` SET `attacksteps`=10 WHERE `id`={player.Id};UPDATE `players` SET `health`={enemy.Health - damage - petDamage} WHERE `id`={enemy.Id};";
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                            else
                            {
                                command.CommandText = $"UPDATE `players` SET `attacksteps`=`attacksteps`-1 WHERE `id`={player.Id}";
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                    else
                    {
                        player.MaxMana = bonuses[6];

                        command.CommandText = $"UPDATE `players` SET `maxmana`={bonuses[6]} WHERE `id`={player.Id};UPDATE `players` SET `mana`=`maxmana` WHERE `id`={player.Id} AND `mana`>`maxmana`";
                        command.ExecuteNonQuery();

                        if (player.Health < player.MaxHealth)
                        {
                            int chance = rnd.Next(1, 101);
                            int healChance = Convert.ToInt32(Convert.ToDouble(player.Stats.HealRate + bonuses[3]) * 17.5) / player.Level;
                            if (chance <= healChance)
                            {
                                player.Health = Math.Min(player.MaxHealth, player.Health + Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                command.CommandText = $"UPDATE `players` SET `health`={player.Health} WHERE `id`={player.Id}";
                                command.ExecuteNonQuery();
                                player = GetPlayer(player.Id, users[u]);
                            }
                        }
                        if (player.Mana < player.MaxMana)
                        {
                            int chance = rnd.Next(1, 101);
                            int healChance = Convert.ToInt32(Convert.ToDouble(player.Stats.HealRate + bonuses[3]) * 17.5) / player.Level;
                            if (chance <= healChance)
                            {
                                player.Mana = Math.Min(player.MaxMana, player.Mana + Convert.ToInt32((player.Stats.HealRate + bonuses[3]) * ((100 + Convert.ToDouble(bonuses[9])) / 100)));
                                command.CommandText = $"UPDATE `players` SET `mana`={player.Mana} WHERE `id`={player.Id}";
                                command.ExecuteNonQuery();
                                player = GetPlayer(player.Id, users[u]);
                            }
                        }
                    }
                    #endregion

                    if (player.StepsLeft == 0 && player.MovingTo != 0)
                    {
                        command.CommandText = $"UPDATE `players` SET `locationid`=`movingto`,`movingto`=0 WHERE `id`={player.Id}";
                        command.ExecuteNonQuery();
                        player = GetPlayer(users[u].Id, users[u]);
                        string answer = $"👣 Вы пришли в локацию: {GetLocation(player.LocationId).Name}!";
                        string type = "";

                        command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                        reader = command.ExecuteReader();
                        if (reader.HasRows)
                        {
                            reader.Read();
                            type = reader.GetString("type");
                        }
                        reader.Close();

                        switch (type)
                        {
                            case "portal":
                                {
                                    bool hasTp = false;
                                    for (int i = 0; i < player.Portals.Count; i++)
                                    {
                                        if (player.Portals[i] == player.LocationId)
                                        {
                                            hasTp = true;
                                        }
                                    }
                                    if (hasTp)
                                    {
                                        answer += "\n\n🌀 Здесь есть портал, просмотреть список порталов командой - Портал";
                                    }
                                    else if (hasTp == false)
                                    {
                                        answer += "\n\n🌀 Вы открыли новый портал, просмотреть список порталов командой - Портал";
                                        command.CommandText = $"UPDATE `players` SET `portals`=CONCAT(`portals`,'_{player.LocationId}') WHERE `id`={player.Id}";
                                        command.ExecuteNonQuery();
                                        player = GetPlayer(player.Id, users[u]);
                                    }
                                    break;
                                }
                            case "shop":
                                {
                                    command.CommandText = $"DELETE FROM `shops` WHERE `playerid`={player.Id}";
                                    command.ExecuteNonQuery();
                                    answer += "\n\n💳 Здесь есть магазин, просмотреть список товаров командой - Магазин";
                                    break;
                                }
                            case "boss":
                                {
                                    answer += "\n\n👹 Здесь есть босс, команда для помощи - Рейд";
                                    break;
                                }
                            case "cemetry":
                                {
                                    answer += "\n\n⚰ Здесь есть кладбище, установить точку возрождения - Кладбище";
                                    break;
                                }
                            case "caravan":
                                {
                                    answer += "\n\n🐪 Здесь есть караван, команда для помощи - Перехват";
                                    break;
                                }
                            case "potionshop":
                                {
                                    answer += "\n\n🌿 Здесь есть зельеварня, просмотреть список товаров командой - Зельеварня";
                                    break;
                                }
                            case "chest":
                                {
                                    answer += "\n\n🗃 Здесь есть хранилище, просмотреть список предметов в хранилище - Хранилище";
                                    break;
                                }
                            case "sump":
                                {
                                    answer += "\n\n🗑 Здесь есть колодец, просмотреть информацию - Колодец";
                                    break;
                                }
                            default: break;
                        }

                        Message(answer, player.Id, true);
                    }

                    if (player.Xp >= (player.Level + player.Level / 2) * 100)
                    {
                        bool keys = true;
                        int points = 1;
                        if ((player.Level + 1) % 5 == 0)
                        {
                            points = 3;
                        }
                        command.CommandText = $"UPDATE `players` SET `xp`={player.Xp - ((player.Level + player.Level / 2) * 100) },`money`=`money`+`level`*50,`maxhealth`=(10 + 10 * ((`level`) * 0.55)),`health`=`maxhealth`, `mana`=`maxmana`, `level`=`level`+1  WHERE `id`={player.Id};UPDATE `stats` SET `skillpoints`=`skillpoints`+{points} WHERE `playerid`={player.Id}";
                        command.ExecuteNonQuery();

                        player = GetPlayer(users[u].Id, users[u]);

                        if (player.Fight == true || player.MovingTo != 0)
                        {
                            keys = false;
                        }
                        Message($"🔱 Достигнут {player.Level} уровень!\n💫 Получено очков умений: {points} (Доступно:{player.Stats.SkillPoints})\n💰 Получено золота: {(player.Level - 1) * 50}", player.Id, keys);
                    }
                    MessageGetHistoryObject history = api.Messages.GetHistory(new MessagesGetHistoryParams
                    {
                        UserId = users[u].Id,
                        Count = 1
                    });
                    messages = history.Messages.ToCollection();

                    if (messages.Count > 0)
                    {
                        try
                        {
                            if (messages[0].FromId == player.Id && messages[0].Id != player.LastMessage)
                            {
                                if (player.Fight)
                                {
                                    if (player.AttackerType == false && player.AttackerId == 0 && player.AttackSteps == 0)
                                    {
                                        if (messages[0].Text.ToLower() == "выйти")
                                        {
                                            int mobid = 0;
                                            string players = "";
                                            string newPlayers = "";
                                            command.CommandText = $"SELECT * FROM `bosses` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                mobid = reader.GetInt32("mobid");
                                                players = reader.GetString("players");
                                                newPlayers = "";
                                            }

                                            int count = players.Split('_').Length;
                                            for (int i = 0; i < count; i++)
                                            {
                                                if (players.Split('_')[i] != player.Id.ToString())
                                                {
                                                    if (newPlayers == "")
                                                    {
                                                        newPlayers += players.Split('_')[i];
                                                    }
                                                    else
                                                    {
                                                        newPlayers += $"_{players.Split('_')[i]}";
                                                    }
                                                }
                                            }

                                            reader.Close();

                                            command.CommandText = $"UPDATE `bosses` SET `players` = '{newPlayers}' WHERE `locationid`={player.LocationId};UPDATE `players` SET `fight` = 0 WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();

                                            player = GetPlayer(player.Id, users[u]);

                                            Message("🚪 Вы вышли", player.Id, true);
                                        }
                                        else
                                        {
                                            Message("🚫 Сейчас нельзя использовать команды", player.Id, false);
                                        }
                                    }
                                    else
                                    {
                                        Message("🚫 В бою нельзя использовать команды", player.Id, false);
                                    }
                                }
                                else if (player.StepsLeft > 0)
                                {
                                    Message($"🚫 Во время путешествия нельзя использовать команды (Осталось шагов: {player.StepsLeft})", player.Id, false);
                                }
                                else
                                {
                                    string source = messages[0].Text.Replace("\\n", String.Empty).Replace("\n", String.Empty).Replace("#", String.Empty).Replace("'", String.Empty);
                                    string message = source.ToLower();

                                    try
                                    {
                                        if (message == "начать" || message == "помощь" || message == "команды")
                                        {
                                            command.CommandText = $"UPDATE `players` SET `lastmessage`={messages[0].Id} WHERE `id`={player.Id}";
                                            command.ExecuteNonQuery();

                                            Message($"📰 Список команд:\n\n🔱 Статистика - просмотр статистики\n✅ Онлайн - посмотрите кто онлайн\n✅ Онлайн локации - посмотрите кто онлайн в вашей локации\n👤 Имя - сменить имя (💎 Стоимость: 1)\n🌍 Локация - информация о том, где вы сейчас находитесь\n👣 Идти - идти в соседнюю локацию\n🏹 Гринд - запустить/остановить гринд на локации\n💫 Улучшить персонажа - открыть меню улучшения персонажа\n🥋 Снаряжение - открыть список снаряжения\n🗣 Вызвать всех на бой - позвать игроков на бой в вашу локацию\n💼 Инвентарь - показать все вещи в инвентаре\n⚙ Надеть - перенести предмет из инвентаря в снаряжение\n⚙ Выбросить - выбросить предмет из инвентаря\n🌀 Портал - перемещение через порталы\n💳 Магазин - открыть список предметов магазина\n💰 Купить/Продать - купить или продать предмет в магазине\n⚰ Кладбище - Установить точку возрождения на кладбище\n🏛 Клан - посмотреть информацию о клане\n🎁 Подарить - подарить золото клану\n🏆 Турнир - турнирное меню\n📈 Топ - просмотр топа игроков\n🏆 Авто - выключить/включить автоматическое участие в турнире (при недостатке игроков)\n⚖ Аукцион - просмотр предметов на аукционе\n📦 Отправить/Забрать - отправить или забрать предмет на аукционе\n📦 Отправки - список отправленных на аукцион предметов\n🔖 Ставка - сделать ставку на аукционный предмет\n🗃 Хранилище - просмотр вещей в хранилище\n⚙ Положить/Вытащить - положить/вытащить предмет в хранилище\n🗑 Колодец - посмотреть информацию о колодце\n⚙ Кинуть - кинуть предмет в колодец для получения зачарования\n🦊 Питомец - посмотреть информацию о питомце\n⚙ Снять - снять предмет", users[u].Id, true);
                                        }
                                        else if (message == "статистика" || message == "статка" || message == "стата" || message == "профиль")
                                        {
                                            string clanName = "";

                                            if (player.ClanId != 0)
                                            {
                                                clanName = $"\n🏛 Клан: {GetClan(player.ClanId).Name}";
                                            }

                                            string petName = "";

                                            Pet pet = GetPet(player.Id);
                                            if (pet.Id != 0)
                                            {
                                                petName = $"\n🦊 Питомец: {player.Pet.Name}";
                                            }

                                            string answer = $"👤 Имя: { player.Name}{petName}\n🎗 Очки славы: {player.Stats.Glory}{clanName}\n\n🔱 Уровень: { player.Level}\n💫 Очков умений: { player.Stats.SkillPoints}\n❤ Здоровье: { player.Health}/{ player.MaxHealth}\n💙 Мана: {player.Mana}/{player.MaxMana}\n📊 Опыт: { player.Xp}/{ (player.Level + player.Level / 2) * 100}\n💰 Золото: { player.Money}\n💎 Алмазы: {player.Diamonds}\n\n☠ Смертей: { player.Stats.Deaths}\n🐜 Мобов убито: { player.Stats.MobsKilled}\n💀 Игроков убито: { player.Stats.PlayersKilled}\n👹 Боссов убито: { player.Stats.BossesKilled}\n🐪 Караванов перехвачено: {player.Stats.CaravansKilled}\n🏆 Турниров выиграно: {player.Stats.Tournaments}\n";

                                            if (bonuses[4] == 0)
                                            {
                                                answer += $"\n🔪 Уровень урона: { Convert.ToInt32(player.Stats.DamageRate + player.Stats.DamageRate * ((player.Level - 1) * 0.25))}";
                                            }
                                            else
                                            {
                                                answer += $"\n🔪 Уровень урона: { Convert.ToInt32(player.Stats.DamageRate + player.Stats.DamageRate * ((player.Level - 1) * 0.25))} + {bonuses[4]}";
                                            }
                                            if (bonuses[0] == 0)
                                            {
                                                answer += $"\n👊🏻 Уровень силы: {player.Stats.AttackRate}";
                                            }
                                            else
                                            {
                                                answer += $"\n👊🏻 Уровень силы: {player.Stats.AttackRate} + {bonuses[0] * 5}";
                                            }
                                            if (bonuses[1] == 0)
                                            {
                                                answer += $"\n🛡 Уровень защиты: {player.Stats.DefenseRate}";
                                            }
                                            else
                                            {
                                                answer += $"\n🛡 Уровень защиты: {player.Stats.DefenseRate} + {bonuses[1]}";
                                            }
                                            if (bonuses[2] == 0)
                                            {
                                                answer += $"\n💥 Уровень боя: {player.Stats.CriticalRate}";
                                            }
                                            else
                                            {
                                                answer += $"\n💥 Уровень боя: {player.Stats.CriticalRate} + {bonuses[2]}";
                                            }
                                            if (bonuses[3] == 0)
                                            {
                                                answer += $"\n💚 Уровень лечения: {player.Stats.HealRate}";
                                            }
                                            else
                                            {
                                                answer += $"\n💚 Уровень лечения: {player.Stats.HealRate} + {bonuses[3]}";
                                            }
                                            if (bonuses[5] == 0)
                                            {
                                                answer += $"\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                            }
                                            else
                                            {
                                                answer += $"\n✨ Уровень волшебства: {player.Stats.MagicRate} + {bonuses[5]}";
                                            }

                                            Message(answer, player.Id, true);
                                        }
                                        else if (message == "питомец")
                                        {
                                            Pet pet = GetPet(player.Id);
                                            if (pet.Id != 0)
                                            {
                                                Message($"🦊 {GetPetInfo(pet)}", player.Id, true);
                                            }
                                            else
                                            {
                                                Message($"🦊 У вас нет питомца", player.Id, true);
                                            }
                                        }
                                        else if (message == "снять")
                                        {
                                            TakeOffMessage("⚙ Выберите вещь, чтобы снять её", player.Id, player);
                                        }
                                        else if (message.Split(' ')[0] == "снять")
                                        {
                                            int rawcount = 0;
                                            command.CommandText = $"SELECT COUNT(*) as count FROM `inventory` WHERE `playerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                rawcount = reader.GetInt32("count");
                                            }
                                            reader.Close();

                                            if (rawcount < 10)
                                            {
                                                if (message.Split(' ')[1] == "шлем")
                                                {
                                                    if (player.Equipt.HelmetId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.HelmetId},(SELECT `helmetlevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `helmetid`=0, `helmetlevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.HelmetId = 0;
                                                        TakeOffMessage("⚙ Вы сняли шлем", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет шлема", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "броню")
                                                {
                                                    if (player.Equipt.PlateId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.PlateId},(SELECT `platelevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `plateid`=0, `platelevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.PlateId = 0;
                                                        TakeOffMessage("⚙ Вы сняли броню", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет брони", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "штаны")
                                                {
                                                    if (player.Equipt.PantsId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.PantsId},(SELECT `pantslevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `pantsid`=0, `pantslevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.PantsId = 0;
                                                        TakeOffMessage("⚙ Вы сняли штаны", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет штанов", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "ботинки")
                                                {
                                                    if (player.Equipt.BootsId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.BootsId},(SELECT `bootslevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `bootsid`=0, `bootslevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.BootsId = 0;
                                                        TakeOffMessage("⚙ Вы сняли ботинки", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет ботинок", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "оружие")
                                                {
                                                    if (player.Equipt.WeaponId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.WeaponId},(SELECT `weaponlevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `weaponid`=0, `weaponlevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.WeaponId = 0;
                                                        TakeOffMessage("⚙ Вы сняли оружие", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет оружия", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "щит")
                                                {
                                                    if (player.Equipt.ShieldId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.ShieldId},(SELECT `shieldlevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `shieldid`=0, `shieldlevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.ShieldId = 0;
                                                        TakeOffMessage("⚙ Вы сняли щит", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет щита", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "руну")
                                                {
                                                    if (player.Equipt.RuneId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.RuneId},(SELECT `runelevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `runeid`=0, `runelevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.RuneId = 0;
                                                        TakeOffMessage("⚙ Вы сняли руну", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет руны", player.Id, player);
                                                    }
                                                }
                                                else if (message.Split(' ')[1] == "книгу")
                                                {
                                                    if (player.Equipt.BookId != 0)
                                                    {
                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{player.Equipt.BookId},(SELECT `booklevel` FROM `equipment` WHERE `playerid`={player.Id}));UPDATE `equipment` SET `bookid`=0, `booklevel`=0 WHERE `playerid`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player.Equipt.BookId = 0;
                                                        TakeOffMessage("⚙ Вы сняли книгу", player.Id, player);
                                                    }
                                                    else
                                                    {
                                                        TakeOffMessage("🚫 На вас нет книги", player.Id, player);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Неправильная команда", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Нет места в инвентаре", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "аукцион" || message.Split(' ')[0] == "аук")
                                        {
                                            try
                                            {
                                                int page = 1;
                                                if (message.Split(' ').Length > 1)
                                                {
                                                    page = Int32.Parse(message.Split(' ')[1]);
                                                }

                                                List<AuctionItem> items = new List<AuctionItem>();
                                                command.CommandText = $"SELECT * FROM `auction` WHERE `level`>={player.Level / 2} AND `level`<={player.Level} AND `ownerid`!={player.Id} ORDER BY `left` DESC";
                                                reader = command.ExecuteReader();

                                                string answer = "";

                                                int count = 0;

                                                while (reader.Read())
                                                {
                                                    items.Add(new AuctionItem()
                                                    {
                                                        Id = reader.GetInt32("id"),
                                                        ItemId = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        OwnerId = reader.GetInt64("ownerid"),
                                                        BeterId = reader.GetInt64("beterid"),
                                                        Bet = reader.GetInt32("bet"),
                                                        Left = reader.GetInt16("left")
                                                    });
                                                    count++;
                                                }
                                                reader.Close();
                                                if (count == 0)
                                                {
                                                    answer += $"⚖ Аукцион пуст\n";
                                                }
                                                else
                                                {
                                                    answer += $"⚖ Предметов на аукционе {count}:\n";
                                                }
                                                int itemsCount = Math.Min(items.Count - 10 * (page - 1), 10);
                                                page = Math.Min(page, Convert.ToInt32(Math.Ceiling(Convert.ToDouble(count) / 10)));
                                                for (int i = 0; i < itemsCount; i++)
                                                {
                                                    string bet = "";
                                                    if (items[i + 10 * (page - 1)].BeterId == 0)
                                                    {
                                                        bet = $"🔖 Ставок нет, стоимость первой ставки 💎 {1 + items[i + 10 * (page - 1)].Bet + items[i + 10 * (page - 1)].Bet / 3}";
                                                    }
                                                    else if (items[i + 10 * (page - 1)].BeterId == player.Id)
                                                    {
                                                        bet = "🔖 Ставка сделана";
                                                    }
                                                    else
                                                    {
                                                        bet = $"🔖 Последняя ставка: {items[i + 10 * (page - 1)].Bet} (💎 Перебить {1 + items[i + 10 * (page - 1)].Bet + items[i + 10 * (page - 1)].Bet / 3})";
                                                    }
                                                    Item item = GetInventoryItem(items[i + 10 * (page - 1)].ItemId);

                                                    string enchant = "";

                                                    if (item.Enchant != null)
                                                    {
                                                        enchant = $" ({item.Enchant.Name}) ";
                                                    }

                                                    answer += $"\n📦 {items[i + 10 * (page - 1)].Id} | {item.Name}{enchant} 🔱 {items[i + 10 * (page - 1)].Level}\n{bet}\n⏲ Осталось ходов: {items[i + 10 * (page - 1)].Left}\n";
                                                }

                                                if (count > 10)
                                                {
                                                    answer += $"\n📄 Страница {page} из {Convert.ToInt32(Math.Ceiling(Convert.ToDouble(count) / 10))}";
                                                }

                                                answer += "\n\n📦 Отправить предмет на аукцион команда - Отправить + номер предмета в инвентаре (Можно отправить 3 предмета) + начальная стоимость\n📦 Список отправленных предметов команда - отправки\n📦 Информация лота команда - Инфо + номер лота\n🔖 Поставить на предмет команда - Ставка + номер лота";

                                                Message(answer, player.Id, true);
                                            }
                                            catch (Exception e)
                                            {
                                                Message("🚫 Неправильная страница", player.Id, true);
                                            }
                                        }
                                        else
                                        if (message.Split(' ')[0] == "отправить")
                                        {
                                            int count = 0;
                                            command.CommandText = $"SELECT COUNT(*) as `count` FROM `auction` WHERE `ownerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                count = reader.GetInt32("count");
                                            }
                                            reader.Close();

                                            if (count < 3)
                                            {
                                                List<Item> items = new List<Item>();
                                                command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    items.Add(new Item()
                                                    {
                                                        Id = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        MinLevel = reader.GetInt32("id")
                                                    });
                                                    Console.WriteLine(items[items.Count - 1].Id);
                                                }
                                                reader.Close();
                                                int id = Convert.ToInt32(message.Split(' ')[1]);
                                                int cost = Convert.ToInt32(message.Split(' ')[2]);
                                                if (cost > 0)
                                                {
                                                    if (id <= items.Count)
                                                    {
                                                        int idInv = items[id - 1].MinLevel;
                                                        Item item = GetInventoryItem(items[id - 1].Id);
                                                        item.Level = items[id - 1].Level;

                                                        command.CommandText = $"INSERT INTO `auction`(`itemid`,`level`,`ownerid`,`beterid`,`bet`) VALUES({items[id - 1].Id},{item.Level},{player.Id},0,{cost})";
                                                        command.ExecuteNonQuery();

                                                        command.CommandText = $"DELETE FROM `inventory` WHERE `id`={idInv}";
                                                        command.ExecuteNonQuery();
                                                        Message($"📦 Вы отправили на аукцион {item.Name} за {cost} алмазов", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неправильный номер предмета", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Слишком маленькая стоимость", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы уже отправили на аукцион 3 предмета", player.Id, true);
                                            }
                                        }
                                        else if (message == "отправки")
                                        {
                                            List<AuctionItem> items = new List<AuctionItem>();
                                            command.CommandText = $"SELECT * FROM `auction` WHERE `ownerid`={player.Id} ORDER BY `left` DESC";
                                            reader = command.ExecuteReader();

                                            string answer = "";

                                            int count = 0;

                                            while (reader.Read())
                                            {
                                                items.Add(new AuctionItem()
                                                {
                                                    Id = reader.GetInt32("id"),
                                                    ItemId = reader.GetInt32("itemid"),
                                                    Level = reader.GetInt16("level"),
                                                    OwnerId = reader.GetInt64("ownerid"),
                                                    BeterId = reader.GetInt64("beterid"),
                                                    Bet = reader.GetInt32("bet"),
                                                    Left = reader.GetInt16("left")
                                                });
                                                count++;
                                            }
                                            reader.Close();

                                            if (count == 0)
                                            {
                                                answer = "⚖ Нет отправленных предметов\n";
                                            }
                                            else
                                            {
                                                answer = $"⚖ Отправленные вами предмететы {count}/3:\n";
                                            }

                                            count = items.Count;

                                            for (int i = 0; i < count; i++)
                                            {
                                                string bet = "";

                                                if (items[i].BeterId == 0)
                                                {
                                                    bet = "🔖 Ставок нет";
                                                }
                                                else
                                                {
                                                    bet = $"🔖 Последняя ставка: {items[i].Bet} (💎 Перебить {1 + items[i].Bet + items[i].Bet / 3})";
                                                }
                                                Item item = GetInventoryItem(items[i].ItemId);

                                                string enchant = "";

                                                if (item.Enchant != null)
                                                {
                                                    enchant = $" ({item.Enchant.Name}) ";
                                                }
                                                answer += $"\n📦 {items[i].Id} | {item.Name}{enchant} 🔱 {items[i].Level}\n{bet}\n⏲ Осталось ходов: {items[i].Left}\n";
                                            }

                                            answer += "\n\n📦 Отправить предмет на аукцион команда - Отправить + номер предмета в инвентаре + начальная стоимость\n📦 Забрать предмет с аукциона команда - Забрать + номер лота\n📦 Информация лота команда - Инфо + номер лота";

                                            Message(answer, player.Id, true);
                                        }
                                        else if (message.Split(' ')[0] == "забрать")
                                        {
                                            int rawcount = 0;
                                            command.CommandText = $"SELECT COUNT(*) as count FROM `inventory` WHERE `playerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                rawcount = reader.GetInt32("count");
                                            }
                                            reader.Close();

                                            if (rawcount < 10)
                                            {
                                                int id = Convert.ToInt32(message.Split(' ')[1]);

                                                List<AuctionItem> items = new List<AuctionItem>();
                                                command.CommandText = $"SELECT * FROM `auction` WHERE `ownerid`={player.Id} ORDER BY `left` DESC";
                                                reader = command.ExecuteReader();

                                                int count = 0;

                                                while (reader.Read())
                                                {
                                                    items.Add(new AuctionItem()
                                                    {
                                                        Id = reader.GetInt32("id"),
                                                        ItemId = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        OwnerId = reader.GetInt64("ownerid"),
                                                        BeterId = reader.GetInt64("beterid"),
                                                        Bet = reader.GetInt32("bet"),
                                                        Left = reader.GetInt16("left")
                                                    });
                                                    count++;
                                                }
                                                reader.Close();

                                                int ind = -1;

                                                for (int i = 0; i < count; i++)
                                                {
                                                    if (items[i].Id == id)
                                                    {
                                                        ind = i;
                                                    }
                                                }

                                                if (ind != -1)
                                                {
                                                    if (items[ind].BeterId == 0)
                                                    {
                                                        command.CommandText = $"DELETE FROM `auction` WHERE `id`={id}";
                                                        command.ExecuteNonQuery();

                                                        command.CommandText = $"INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{items[ind].ItemId},{items[ind].Level})";
                                                        command.ExecuteNonQuery();
                                                        Message($"📦 Вы забрали с аукциона {GetItemName(items[ind].ItemId)}", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Нельзя забрать предмет на который сделана ставка", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Неправильный номер предмета", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Нет места в инвентаре", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "ставка")
                                        {
                                            int id = Convert.ToInt32(message.Split(' ')[1]);

                                            List<AuctionItem> items = new List<AuctionItem>();
                                            command.CommandText = $"SELECT * FROM `auction` WHERE `id`={id}";
                                            reader = command.ExecuteReader();

                                            if (reader.HasRows)
                                            {
                                                AuctionItem item = null;
                                                while (reader.Read())
                                                {
                                                    item = new AuctionItem()
                                                    {
                                                        Id = reader.GetInt32("id"),
                                                        ItemId = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        OwnerId = reader.GetInt64("ownerid"),
                                                        BeterId = reader.GetInt64("beterid"),
                                                        Bet = reader.GetInt32("bet"),
                                                        Left = reader.GetInt16("left")
                                                    };
                                                }
                                                reader.Close();

                                                if (item.BeterId != player.Id)
                                                {
                                                    if (item.OwnerId != player.Id)
                                                    {
                                                        if (player.Diamonds >= 1 + item.Bet + item.Bet / 3)
                                                        {
                                                            command.CommandText = $"UPDATE `players` SET `diamonds`=`diamonds`+{item.Bet} WHERE `id`={item.BeterId};UPDATE `players` SET `diamonds`=`diamonds`-{1 + item.Bet + item.Bet / 3} WHERE `id`={player.Id};UPDATE `auction` SET `bet`={1 + item.Bet + item.Bet / 3},`beterid`={player.Id} WHERE `id`={item.Id}";
                                                            command.ExecuteNonQuery();

                                                            Message($"🔖 Игрок {player.Name} перебил вашу ставку на лот №{item.Id}", item.BeterId, true);
                                                            Message($"🔖 Вы сделали ставку {1 + item.Bet + item.Bet / 3} алмазов на {GetItemName(item.ItemId)}", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            Message("🚫 Недостаточно алмазов", player.Id, true);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Нельзя сделать ставку на свой же предмет", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы уже сделали ставку на этот предмет", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Неправильный номер предмета", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "инфо")
                                        {
                                            int id = Convert.ToInt32(message.Split(' ')[1]);
                                            command.CommandText = $"SELECT id,level,itemid FROM `auction` WHERE `id`={id}";
                                            reader = command.ExecuteReader();

                                            if (reader.HasRows)
                                            {
                                                int itemId = 0;
                                                int level = 0;
                                                while (reader.Read())
                                                {
                                                    itemId = reader.GetInt32("itemid");
                                                    level = reader.GetInt16("level");
                                                }
                                                reader.Close();

                                                Item item = GetInventoryItem(itemId);

                                                string answer = $"📦 Информация о лоте №{id}:\n\n";

                                                if (item.Type == "helmet")
                                                {
                                                    answer += $"🧢 ";
                                                }
                                                else if (item.Type == "plate")
                                                {
                                                    answer += $"👕 ";
                                                }
                                                else if (item.Type == "pants")
                                                {
                                                    answer += $"👖 ";
                                                }
                                                else if (item.Type == "boots")
                                                {
                                                    answer += $"👟 ";
                                                }
                                                else if (item.Type == "weapon")
                                                {
                                                    answer += $"🔪 ";
                                                }
                                                else if (item.Type == "shield")
                                                {
                                                    answer += $"🛡 ";
                                                }
                                                else if (item.Type == "rune")
                                                {
                                                    answer += $"🀄 ";
                                                }
                                                else if (item.Type == "book")
                                                {
                                                    answer += $"📖 ";
                                                }

                                                answer += $"{item.Name} {GetItemInfo(GetInventoryItem(itemId), level, "inventory")}";

                                                Message(answer, player.Id, true);
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Неправильный номер предмета", player.Id, true);
                                            }
                                        }
                                        else if (message == "топ" || message == "лидеры" || message == "таблица лидеров")
                                        {
                                            List<Player> players = new List<Player>();
                                            command.CommandText = "SELECT `name`,`id`,`glory`,`level` FROM `players`,`stats` WHERE `playerid`=`id` ORDER BY `glory` DESC LIMIT 10";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                players.Add(new Player()
                                                {
                                                    Id = reader.GetInt64("id"),
                                                    Name = reader.GetString("name"),
                                                    Level = reader.GetInt32("level"),
                                                    Stats = new Stat() { Glory = reader.GetInt32("glory") }
                                                });
                                            }
                                            reader.Close();

                                            int count = players.Count;

                                            string answer = "📈 Таблица лидеров:\n";

                                            for (int i = 0; i < count; i++)
                                            {
                                                answer += $"\n🎗 {players[i].Stats.Glory} | {players[i].Name} ";
                                                if (player.Id == players[i].Id)
                                                {
                                                    answer += "(Вы)";
                                                }
                                                else
                                                {
                                                    answer += $"@id{players[i].Id} (👁‍🗨)";
                                                }
                                            }

                                            Message(answer, player.Id, true);
                                        }
                                        else if (message == "авто")
                                        {
                                            if (player.Stats.Part == true)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `part`=0 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                Message($"🏆 Автоматическое участие выключено", player.Id, true);
                                            }
                                            else
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `part`=1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                Message($"🏆 Автоматическое участие включено", player.Id, true);
                                            }
                                        }
                                        else if (message == "турнир" || message == "турик")
                                        {
                                            string answer = "🏆 Статус турнира: ";

                                            if (tournament.Stage == 0)
                                            {
                                                answer += "Подготовка";
                                                if (player.Stats.Tournament == false)
                                                {
                                                    answer += "\n\n🚪 Присоединиться к турниру команда - На турнир";
                                                }
                                                else
                                                {
                                                    answer += "\n\n🚪 Выйти из турнира команда - Покинуть турнир";
                                                }
                                                answer += $"\n\n⏲ Начало схваток через {tournament.NextStage} ходов\n\n👥 Участников: {tournament.Members}\n🏆 Авто - выключить/включить автоматическое участие в турнире (при недостатке игроков)";
                                            }
                                            else
                                            {
                                                answer += $"Схватки\n\n👥 Участников: {tournament.Members}";
                                            }
                                            Message($"{answer}", player.Id, true);
                                        }
                                        else if (message == "на турнир" || message == "на турик")
                                        {
                                            if (player.Stats.Tournament == false)
                                            {
                                                if (tournament.Stage == 0)
                                                {
                                                    if (player.Level >= 10)
                                                    {
                                                        command.CommandText = $"UPDATE `tournaments` SET `members`=`members`+1 WHERE `id`={tournament.Id};UPDATE `stats` SET `tournament`=1 WHERE `playerid`={player.Id};UPDATE `players` SET `grind`=0 WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();

                                                        Message("🚪 Вы присоединились к турниру\n\n🚪 Выйти из турнира команда - Покинуть турнир", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Слишком низкий уровень (Минимальный - 10)", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Уже нельзя присоединиться к турниру", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы уже участвуете в турнире", player.Id, true);
                                            }
                                        }
                                        else if (message == "покинуть турнир" || message == "покинуть турик")
                                        {
                                            if (player.Stats.Tournament == true)
                                            {
                                                if (tournament.Stage == 0)
                                                {
                                                    command.CommandText = $"UPDATE `tournaments` SET `members`=`members`-1 WHERE `id`={tournament.Id};UPDATE `stats` SET `tournament`=0 WHERE `playerid`={player.Id}";
                                                    command.ExecuteNonQuery();

                                                    Message("🚪 Вы вышли из турнира", player.Id, true);
                                                }
                                                else
                                                {
                                                    Message("🚫 Уже нельзя выйти из турнира", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не участвуете в турнире", player.Id, true);
                                            }
                                        }
                                        else if (message == "колодец")
                                        {
                                            Message("⚙ Киньте предмет в колодец, чтобы зачаровать его команда - Кинуть + номер предмета\n\n🔮 Вероятность зачарования 80%\n💭 Вероятность, что зачарование не наложится 15%\n💥 Вероятность потерять предмет 5%\n\n💰 Цена равна тройной цене предмета", player.Id, true);
                                        }
                                        else if (message.Split(' ')[0] == "кинуть")
                                        {
                                            bool sump = false;
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='sump' AND `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                sump = true;
                                            }
                                            reader.Close();

                                            if (sump)
                                            {
                                                List<Item> items = new List<Item>();
                                                command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    items.Add(new Item()
                                                    {
                                                        Id = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        MinLevel = reader.GetInt32("id")
                                                    });
                                                }
                                                reader.Close();

                                                int id = Convert.ToInt32(message.Split(' ')[1]);
                                                if (id <= items.Count)
                                                {
                                                    int chance = rnd.Next(0, 100);
                                                    int enchanteId = 0;
                                                    Item item = GetInventoryItem(items[id - 1].Id);
                                                    item.Level = items[id - 1].Level;
                                                    long cost = 0;
                                                    for (int i = 0; i < item.Bonuses.Count; i++)
                                                    {
                                                        cost += Math.Abs(item.Bonuses[i].Value) * 251;
                                                    }
                                                    cost += Convert.ToInt64(item.Level * (item.Level * (item.Level / 4) * 2.5));
                                                    if (player.Money >= cost)
                                                    {
                                                        if (chance <= 5)
                                                        {

                                                            command.CommandText = $"DELETE FROM `inventory` WHERE `id`={items[id - 1].MinLevel};UPDATE `players` SET `money`=`money`-{cost} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();

                                                            Message($"💥 Вы потеряли {item.Name} и {cost} золота", player.Id, true);

                                                        }
                                                        else if (chance <= 15)
                                                        {
                                                            command.CommandText = $"UPDATE `players` SET `money`=`money`-{cost} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();

                                                            Message($"💭 Зачарование не наложилось, потеряно {cost} золота", player.Id, true);
                                                        }
                                                        else if (chance <= 80)
                                                        {
                                                            int count = 0;
                                                            command.CommandText = $"SELECT COUNT(*) as `count` FROM `enchants`";
                                                            reader = command.ExecuteReader();
                                                            while (reader.Read())
                                                            {
                                                                count = reader.GetInt32("count");
                                                            }
                                                            reader.Close();

                                                            enchanteId = rnd.Next(0, count) + 1;

                                                            item = GetInventoryItem(items[id - 1].Id % 10000 + enchanteId * 10000);

                                                            string enchant = "";

                                                            if (item.Enchant != null)
                                                            {
                                                                enchant = $"{item.Enchant.Name}";
                                                            }

                                                            command.CommandText = $"UPDATE `inventory` SET `itemid` = {items[id - 1].Id % 10000 + enchanteId * 10000} WHERE `id`={items[id - 1].MinLevel};UPDATE `players` SET `money`=`money`-{cost} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();

                                                            Message($"🔮 Вы успешно зачаровали {item.Name} на {enchant} за {cost} золота", player.Id, true);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Message($"🚫 Нужно еще {cost - player.Money} золота", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Неправильный номер предмета", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Здесь нет колодца", player.Id, true);
                                            }
                                        }
                                        else if (message == "клан")
                                        {
                                            if (player.ClanId == 0)
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                            else
                                            {
                                                Clan clan = GetClan(player.ClanId);
                                                string answer = $"🏛 Клан: {clan.Name}\n\n📃 Описание: ";
                                                if (clan.Description == "")
                                                {
                                                    answer += "Отсутствует\n";
                                                }
                                                else
                                                {
                                                    answer += $"{clan.Description}\n";
                                                }

                                                if (clan.MinLevel != 0)
                                                {
                                                    answer += $"🔱 Уровень для входа: {clan.MinLevel}\n";
                                                }

                                                answer += $"👑 Владелец: {GetPlayer(clan.OwnerId, null).Name}";

                                                if (clan.OwnerId == player.Id)
                                                {
                                                    answer += " (Вы)";
                                                }
                                                else
                                                {
                                                    answer += $" @id{clan.OwnerId}(👁‍🗨)";
                                                }

                                                List<Player> players = GetPlayersInClan(clan.Id);
                                                int count = players.Count;

                                                answer += $"\n\n👥 Участники ({count}/10):\n\n";

                                                for (int i = 0; i < count; i++)
                                                {
                                                    answer += $"👤 {i + 1} | {players[i].Name} 🔱 {players[i].Level}";
                                                    if (players[i].Id == player.Id)
                                                    {
                                                        answer += " (Вы)";
                                                    }
                                                    else
                                                    {
                                                        answer += $" @id{players[i].Id} (👁‍🗨)";
                                                    }
                                                    answer += "\n";
                                                }
                                                if (clan.OwnerId == player.Id)
                                                {
                                                    answer += "\n\n🏛 Название клана + название - установить новое название\n📃 Описание клана + описание - установить новое описание\n🔱 Уровень входа + уровень - установить минимальный уровень для входа\n✉ Отправить сообщение - Сообщение + текст\n🛑 Выгнать игрока из клана - Выгнать + номер игрока";
                                                }
                                                answer += "\n\n🏛 Выйти из клана - команда для выхода\n🎁 Подарить золото клану - Подарить + количество";
                                                Message(answer, player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "выгнать")
                                        {
                                            if (player.ClanId == 0)
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                            else
                                            {
                                                Clan clan = GetClan(player.ClanId);
                                                if (clan.OwnerId == player.Id)
                                                {
                                                    List<Player> players = GetPlayersInClan(clan.Id);

                                                    int id = Convert.ToInt32(message.Split(' ')[1]);

                                                    int count = players.Count;

                                                    if (id >= 1 && id <= count)
                                                    {
                                                        command.CommandText = $"UPDATE `players` SET `clanid`=0 WHERE `id`={players[id - 1].Id};UPDATE `clans` SET `members`=`members`-1 WHERE `id`={player.ClanId}";
                                                        command.ExecuteNonQuery();
                                                        Message($"🛑 Вы выгнали {players[id - 1].Name} из клана", player.Id, true);
                                                        Message($"🛑 Вас выгнали из клана", players[id - 1].Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неправильный номер участника", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не владелец клана", player.Id, true);
                                                }
                                            }
                                        }
                                        else if (message == "кланы")
                                        {
                                            if (player.ClanId == 0)
                                            {
                                                List<int> ids = new List<int>();
                                                command.CommandText = $"SELECT `id` FROM `clans` WHERE `minlevel`<={player.Level} AND `members`<10";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    ids.Add(reader.GetInt32("id"));
                                                }
                                                reader.Close();
                                                int count = ids.Count;
                                                string answer = $"📃 Список доступных кланов ({count}):\n";
                                                for (int i = 0; i < count; i++)
                                                {
                                                    Clan clan = GetClan(ids[i]);
                                                    answer += $"\n🏛 {clan.Id} | {clan.Name} 🔱 {clan.MinLevel}+ 👥 {clan.Members}/10";
                                                }
                                                answer += "\n\n🏛 Войти в клан - Войти + номер клана";
                                                Message(answer, player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Вы уже состоите в клане", player.Id, true);
                                            }
                                        }
                                        else if (message != "войти" && message.Split(' ')[0] == "войти")
                                        {
                                            if (player.ClanId == 0)
                                            {
                                                try
                                                {
                                                    Clan clan = GetClan(Convert.ToInt32(message.Split(' ')[1]));

                                                    if (clan.MinLevel > player.Level)
                                                    {
                                                        Message("🚫 Слишком низкий уровень", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        if (clan.Members >= 10)
                                                        {
                                                            Message("🚫 Клан полон", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            command.CommandText = $"UPDATE `clans` SET `members`=`members`+1 WHERE `id`={clan.Id};UPDATE `players` SET `clanid`={clan.Id} WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                            Message($"🏛 Вы присоединились к клану {clan.Name}", player.Id, true);
                                                        }
                                                    }
                                                }
                                                catch
                                                {
                                                    Message("🚫 Неправильный номер клана", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы уже состоите в клане", player.Id, true);
                                            }
                                        }
                                        else if (message == "выйти из клана")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                Clan clan = GetClan(player.ClanId);

                                                List<Player> players = GetPlayersInClan(clan.Id);
                                                int count = players.Count;
                                                if (count == 1)
                                                {
                                                    command.CommandText = $"DELETE FROM `clans` WHERE `id`={clan.Id};UPDATE `players` SET `clanid`=0 WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                    Message($"🏛 Вы вышли из клана {clan.Name}", player.Id, true);
                                                }
                                                else
                                                {
                                                    if (clan.OwnerId == player.Id)
                                                    {
                                                        long? newOwnerId = 0;
                                                        for (int i = 0; i < count; i++)
                                                        {
                                                            if (players[i].Id != player.Id && newOwnerId == 0)
                                                            {
                                                                newOwnerId = players[i].Id;
                                                            }
                                                        }
                                                        command.CommandText = $"UPDATE `clans` SET `members`=`members`-1, `ownerid`={newOwnerId} WHERE `id`={clan.Id};UPDATE `players` SET `clanid`=0 WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        Message($"🏛 Вы вышли из клана {clan.Name}", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        command.CommandText = $"UPDATE `clans` SET `members`=`members`-1 WHERE `id`={clan.Id};UPDATE `players` SET `clanid`=0 WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        Message($"🏛 Вы вышли из клана {clan.Name}", player.Id, true);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "создать" && message.Split(' ')[1] == "клан")
                                        {
                                            if (player.ClanId == 0)
                                            {
                                                if (player.Diamonds >= 10)
                                                {
                                                    try
                                                    {
                                                        string name = source;
                                                        string clanName = "";
                                                        for (int i = 0; i < name.Split(' ').Length - 2; i++)
                                                        {
                                                            clanName += name.Split(' ')[i + 2];
                                                            if (name.Split(' ').Length - 3 != i)
                                                            {
                                                                clanName += ' ';
                                                            }
                                                        }
                                                        if (clanName.Length < 5)
                                                        {
                                                            Message("🚫 Слишком короткое название клана (минимум 5 символов)", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            command.CommandText = $"INSERT INTO `clans`(`name`,`description`,`ownerid`,`minlevel`,`members`) VALUES('{clanName}','',{player.Id},0,1); UPDATE `players` SET `diamonds`=`diamonds`-10,`clanid`=(SELECT MAX(`id`) FROM `clans`) WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                            Message($"🏛 Клан {clanName} создан", player.Id, true);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        Message("🚫 Неправильное имя клана", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Недостаточно алмазов", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы уже состоите в клане", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "описание" && message.Split(' ')[1] == "клана")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                Clan clan = GetClan(player.ClanId);

                                                if (clan.OwnerId == player.Id)
                                                {
                                                    try
                                                    {
                                                        string name = source;
                                                        string clanDescription = "";
                                                        for (int i = 0; i < name.Split(' ').Length - 2; i++)
                                                        {
                                                            clanDescription += name.Split(' ')[i + 2];
                                                            if (name.Split(' ').Length - 3 != i)
                                                            {
                                                                clanDescription += ' ';
                                                            }
                                                        }

                                                        command.CommandText = $"UPDATE `clans` SET `description`='{clanDescription}' WHERE `id`={clan.Id}";
                                                        command.ExecuteNonQuery();
                                                        Message($"🏛 Описание клана установлено", player.Id, true);
                                                    }
                                                    catch
                                                    {
                                                        Message("🚫 Неправильное описание клана", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не владелец клана", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "название" && message.Split(' ')[1] == "клана")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                Clan clan = GetClan(player.ClanId);

                                                if (clan.OwnerId == player.Id)
                                                {
                                                    try
                                                    {
                                                        string name = source;
                                                        string clanName = "";
                                                        for (int i = 0; i < name.Split(' ').Length - 2; i++)
                                                        {
                                                            clanName += name.Split(' ')[i + 2];
                                                            if (name.Split(' ').Length - 3 != i)
                                                            {
                                                                clanName += ' ';
                                                            }
                                                        }
                                                        if (clanName.Length < 5)
                                                        {
                                                            Message("🚫 Слишком короткое название клана (минимум 5 символов)", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            command.CommandText = $"UPDATE `clans` SET `name`='{clanName}' WHERE `id`={clan.Id}";
                                                            command.ExecuteNonQuery();
                                                            Message($"🏛 Название клана изменено", player.Id, true);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        Message("🚫 Неправильное имя клана", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не владелец клана", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "уровень" && message.Split(' ')[1] == "входа")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                Clan clan = GetClan(player.ClanId);

                                                if (clan.OwnerId == player.Id)
                                                {
                                                    try
                                                    {
                                                        int minLevel = Convert.ToInt32(message.Split(' ')[2]);
                                                        if (minLevel < 0 || minLevel > player.Level)
                                                        {
                                                            Message("🚫 Уровень входа должен быть положительным и меньше или равен вашему уровню", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            command.CommandText = $"UPDATE `clans` SET `minlevel`='{minLevel}' WHERE `id`={clan.Id}";
                                                            command.ExecuteNonQuery();
                                                            Message($"🏛 Уровень входа изменён", player.Id, true);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        Message("🚫 Неправильный уровень входа", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не владелец клана", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "подарить")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                if (player.NextCall == 0)
                                                {
                                                    try
                                                    {
                                                        int money = Convert.ToInt32(message.Split(' ')[1]);
                                                        List<long> ids = new List<long>();
                                                        command.CommandText = $"SELECT `id` FROM `players` WHERE `clanid`={player.ClanId}";
                                                        reader = command.ExecuteReader();

                                                        while (reader.Read())
                                                        {
                                                            ids.Add(reader.GetInt64("id"));
                                                        }

                                                        reader.Close();

                                                        int count = ids.Count;
                                                        if (money >= (count - 1) * 100)
                                                        {
                                                            money = Convert.ToInt32(money / (count - 1));
                                                            command.CommandText = $"UPDATE `players` SET `money`=`money`-{money * (count - 1)}, `nextcall`=20 WHERE `id`={player.Id}";
                                                            command.ExecuteNonQuery();
                                                            for (int i = 0; i < count; i++)
                                                            {
                                                                if (player.Id != ids[i])
                                                                {
                                                                    command.CommandText = $"UPDATE `players` SET `money`=`money`+{money} WHERE `id`={ids[i]}";
                                                                    command.ExecuteNonQuery();
                                                                    Message($"🎁 {player.Name} подарил вам {money} золота", ids[i], true);
                                                                }
                                                                else
                                                                {
                                                                    Message($"🎁 Вы подарили клану {money * (count - 1)} золота", ids[i], true);
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            Message($"🚫 Минимальная сумма для подарка в этом клане: {(count - 1) * 100} золота", player.Id, true);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        Message("🚫 Неправильное количество денег", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message($"🚫 Следующий подарок можно подарить через {player.NextCall} ходов", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "сообщение")
                                        {
                                            if (player.ClanId != 0)
                                            {
                                                Clan clan = GetClan(player.ClanId);

                                                if (clan.OwnerId == player.Id)
                                                {
                                                    if (player.NextCall == 0)
                                                    {
                                                        string sourceMessage = source;
                                                        string newMessage = "";
                                                        for (int i = 0; i < sourceMessage.Split(' ').Length - 1; i++)
                                                        {
                                                            newMessage += sourceMessage.Split(' ')[i + 1];
                                                            if (sourceMessage.Split(' ').Length - 2 != i)
                                                            {
                                                                newMessage += ' ';
                                                            }
                                                        }

                                                        List<long> ids = new List<long>();
                                                        command.CommandText = $"SELECT `id` FROM `players` WHERE `clanid`={player.ClanId}";
                                                        reader = command.ExecuteReader();

                                                        while (reader.Read())
                                                        {
                                                            ids.Add(reader.GetInt64("id"));
                                                        }

                                                        reader.Close();

                                                        int count = ids.Count;
                                                        command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        for (int i = 0; i < count; i++)
                                                        {
                                                            if (player.Id != ids[i])
                                                            {

                                                                Message($"✉ Владелец клана отправил сообщение: {newMessage}", ids[i], true);
                                                            }
                                                            else
                                                            {
                                                                Message($"✉ Сообщение отправлено клану", ids[i], true);
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Message($"🚫 Следующее сообщение можно отправить клану через {player.NextCall} ходов", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не владелец клана", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не состоите в клане\n🏛 Для создания напишите команду - Создать клан + название (💎 Стоимость: 10)\n🏛 Для поиска напишите команду - Кланы", player.Id, true);
                                            }
                                        }
                                        else if (message == "онлайн")
                                        {
                                            List<Player> players = GetOnline(player.Id, users);
                                            string answer = $"✅ Онлайн игроки ({players.Count}):\n";
                                            for (int i = 0; i < players.Count; i++)
                                            {
                                                answer += $"\n🌐 {players[i].Name} | 🔱 Уровень: {players[i].Level} @id{players[i].Id} (👁‍🗨)";
                                            }
                                            Message(answer, player.Id, true);
                                        }
                                        else if (message == "онлайн локации" || message == "онлайн локи" || message == "онлайн местоположения" || message == "онлайн места")
                                        {
                                            int count = 0;
                                            List<Player> players = GetOnline(player.Id, users);
                                            string answer = $"";
                                            for (int i = 0; i < players.Count; i++)
                                            {
                                                if (players[i].LocationId == player.LocationId)
                                                {
                                                    count++;
                                                    answer += $"\n🌐 {players[i].Name} | 🔱 Уровень: {players[i].Level} @id{players[i].Id} (👁‍🗨)";
                                                }
                                            }
                                            Message($"✅ Онлайн игроки на локации ({count}):\n{answer}", player.Id, true);
                                        }
                                        else if (message == "кладбище" || message == "возрождение")
                                        {
                                            string type = "";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Read();
                                                type = reader.GetString("type");
                                            }
                                            reader.Close();

                                            if (type == "cemetry")
                                            {
                                                command.CommandText = $"UPDATE `players` SET `cemetry`={player.LocationId} WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();

                                                Message("⚰ Точка возрождения установлена", player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Здесь нет кладбища", player.Id, true);
                                            }
                                        }
                                        else if (message == "вызов" || message == "позвать" || message == "пвп" || message == "pvp" || message == "вызвать всех на бой")
                                        {
                                            if (player.NextCall == 0)
                                            {
                                                Location location = GetLocation(player.LocationId);
                                                if (location.Pvp == true)
                                                {
                                                    Message($"⚔ PVP Информатор\n\n👤 {player.Name} @id{player.Id} (👁‍🗨)\n🔱 Уровень: {player.Level}\n❤ Здоровье: {player.Health}/{player.MaxHealth}\n🗣 Зовёт всех в бой на локации\n💢 {location.Id} | {location.Name}", 2000000001, false);
                                                    command.CommandText = $"UPDATE `players` SET `nextcall`=20 WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                    Message($"🗣 Вызов брошен", player.Id, true);
                                                }
                                                else
                                                {
                                                    Message($"🗣 В этой локации отсутствует PVP", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🗣 Новое объявление можно сделать через {player.NextCall} ходов", player.Id, true);
                                            }
                                        }
                                        else if (message == "рейд")
                                        {
                                            string answer = "";
                                            string type = "";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Read();
                                                type = reader.GetString("type");
                                            }
                                            reader.Close();

                                            if (type == "boss")
                                            {
                                                command.CommandText = $"SELECT * FROM `bosses` WHERE `locationid`={player.LocationId}";
                                                reader = command.ExecuteReader();
                                                reader.Read();

                                                int count = 0;
                                                reader.Read();
                                                int mobid = reader.GetInt32("mobid");
                                                int emerg = reader.GetInt32("emerge");
                                                int health = reader.GetInt32("healthleft");
                                                if (reader.GetString("players") != "")
                                                {
                                                    count = reader.GetString("players").Split('_').Length;
                                                }

                                                reader.Close();

                                                Mob mob = GetMob(mobid, player.LocationId);

                                                answer += $"👹 Босс: {mob.Name}";

                                                if (emerg > 0)
                                                {
                                                    answer += $"\n⏲ Ходов до появления {emerg}\n👥 Игроков в рейде: {count}\n🚪 Ожидать рейд командой - Войти";
                                                }
                                                else
                                                {
                                                    answer += $"\n⚔ Идёт рейд\n👥 Игроков в рейде: {count}\n❤ Здоровье босса: {health}/{mob.MaxHealth}\n🚪 Присоединиться к рейду командой - Войти";
                                                }
                                                Message($"{answer}", player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Здесь нет босса", player.Id, true);
                                            }
                                        }
                                        else if (message == "перехват")
                                        {
                                            string answer = "";
                                            string type = "";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Read();
                                                type = reader.GetString("type");
                                            }
                                            reader.Close();

                                            if (type == "caravan")
                                            {
                                                command.CommandText = $"SELECT * FROM `bosses` WHERE `locationid`={player.LocationId}";
                                                reader = command.ExecuteReader();
                                                reader.Read();

                                                int count = 0;
                                                reader.Read();
                                                int mobid = reader.GetInt32("mobid");
                                                int emerg = reader.GetInt32("emerge");
                                                int health = reader.GetInt32("healthleft");
                                                if (reader.GetString("players") != "")
                                                {
                                                    count = reader.GetString("players").Split('_').Length - 1;
                                                }

                                                reader.Close();

                                                Mob mob = GetMob(mobid, player.LocationId);

                                                answer += $"🐪 Караван: {mob.Name}";

                                                if (emerg > 0)
                                                {
                                                    answer += $"\n⏲ Ходов до появления {emerg}\n👥 Игроков в перехвате: {count}\n🚪 Ожидать перехват командой - Войти";
                                                }
                                                else
                                                {
                                                    answer += $"\n⚔ Идёт перехват\n👥 Игроков в перехвате: {count}\n❤ Здоровье каравана: {health}/{mob.MaxHealth}\n🚪 Присоединиться к перехвату командой - Войти";
                                                }
                                                Message($"{answer}", player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Здесь нет карава", player.Id, true);
                                            }
                                        }
                                        else if (message == "войти" || message == "зайти")
                                        {
                                            string type = "";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Read();
                                                type = reader.GetString("type");
                                            }
                                            reader.Close();

                                            if (type == "boss")
                                            {
                                                command.CommandText = $"SELECT * FROM `bosses` WHERE `locationid`={player.LocationId}";
                                                reader = command.ExecuteReader();
                                                int mobid = 0;
                                                string players = "";
                                                while (reader.Read())
                                                {
                                                    mobid = reader.GetInt32("mobid");
                                                    players = reader.GetString("players");
                                                }
                                                if (players == "")
                                                {
                                                    players = $"{player.Id}";
                                                }
                                                else
                                                {
                                                    players += $"_{player.Id}";
                                                }
                                                reader.Close();

                                                command.CommandText = $"UPDATE `bosses` SET `players` = '{players}' WHERE `locationid`={player.LocationId};UPDATE `players` SET `fight` = 1 WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                                Message("🚪 Вы присоединились к рейду\n🚪 Выйти командой - Выйти", player.Id, false);
                                            }
                                            else if (type == "caravan")
                                            {
                                                command.CommandText = $"SELECT * FROM `bosses` WHERE `locationid`={player.LocationId}";
                                                reader = command.ExecuteReader();
                                                int mobid = 0;
                                                string players = "";
                                                while (reader.Read())
                                                {
                                                    mobid = reader.GetInt32("mobid");
                                                    players = reader.GetString("players");
                                                }
                                                if (players == "")
                                                {
                                                    players = $"{player.Id}";
                                                }
                                                else
                                                {
                                                    players += $"_{player.Id}";
                                                }
                                                reader.Close();

                                                command.CommandText = $"UPDATE `bosses` SET `players` = '{players}' WHERE `locationid`={player.LocationId};UPDATE `players` SET `fight` = 1 WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                                Message("🚪 Вы присоединились к перехвату\n🚪 Выйти командой - Выйти", player.Id, false);
                                            }
                                            else
                                            {
                                                Message("🚫 Неправильная команда", player.Id, true);
                                            }
                                        }
                                        else if (message == "место" || message == "локация" || message == "местоположение" || message == "лока")
                                        {
                                            Location location = GetLocation(player.LocationId);
                                            string type = "";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Read();
                                                type = reader.GetString("type");
                                            }
                                            reader.Close();

                                            string entityLevel = "Мобы: Отсутствуют";
                                            if (location.EntityLevel != 0)
                                            {
                                                entityLevel = "Уровень мобов: " + location.EntityLevel;
                                            }
                                            string pvp = "Включено";
                                            if (location.Pvp == false)
                                            {
                                                pvp = "Выключено";
                                            }

                                            List<string> locationsIds = GetAvailableLocations(player.LocationId);
                                            string locationsList = "";
                                            for (int i = 0; i < locationsIds.Count; i++)
                                            {
                                                locationsList += $"\n💢 {locationsIds[i].Split('_')[0]} | {GetLocation(Int16.Parse(locationsIds[i].Split('_')[0])).Name} | Шагов: {locationsIds[i].Split('_')[1]}";
                                            }
                                            string typeText = "\n\n";
                                            if (type == "portal")
                                            {
                                                typeText = "\n\n🌀 Здесь есть портал, просмотреть список порталов командой - Портал\n\n";
                                            }
                                            else if (type == "shop")
                                            {
                                                typeText = "\n\n💳 Здесь есть магазин, просмотреть список товаров командой - Магазин\n\n";
                                            }
                                            else if (type == "cemetry")
                                            {
                                                typeText = "\n\n⚰ Здесь есть кладбище, установить точку возрождения - Кладбище\n\n";
                                            }
                                            else if (type == "caravan")
                                            {
                                                typeText = "\n\n🐪 Здесь есть караван, команда для помощи - Перехват\n\n";
                                            }
                                            else if (type == "potionshop")
                                            {
                                                typeText = "\n\n🌿 Здесь есть зельеварня, просмотреть список товаров командой - Зельеварня\n\n";
                                            }
                                            else if (type == "chest")
                                            {
                                                typeText = "\n\n🗃 Здесь есть хранилище, просмотреть список предметов в хранилище - Хранилище\n\n";
                                            }
                                            else if (type == "sump")
                                            {
                                                typeText = "\n\n🗑 Здесь есть колодец, просмотреть информацию - Колодец\n\n";
                                            }
                                            if (type == "boss")
                                            {
                                                Message($"🗺 Локация: {location.Name}\n\n👹 Здесь есть босс, команда для помощи - Рейд\n\n🗺 Доступные локации:\n{locationsList}\n\n👣 Пойти в локацию - Команда Идти + Номер локации", player.Id, true);
                                            }

                                            else
                                            {
                                                Message($"🗺 Локация: {location.Name}\n\n🐜 {entityLevel}\n⚔ PVP: {pvp}{typeText}🗺 Доступные локации:\n{locationsList}\n\n👣 Пойти в локацию - Команда Идти + Номер локации", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "идти" || message.Split(' ')[0] == "пойти" || message.Split(' ')[0] == "путешествовать")
                                        {
                                            if (player.Stats.Tournament == false)
                                            {
                                                List<string> checkLocations = GetAvailableLocations(player.LocationId);
                                                bool check = false;
                                                int steps = 0;
                                                for (int i = 0; i < checkLocations.Count; i++)
                                                {
                                                    if (message.Split(' ')[1] == checkLocations[i].Split('_')[0])
                                                    {
                                                        steps = int.Parse(checkLocations[i].Split('_')[1]);
                                                        check = true;
                                                    }
                                                }
                                                if (check == false)
                                                {
                                                    Message("🚫 Неправильный номер локации", player.Id, true);
                                                }
                                                else if (message.Split(' ')[1] == player.LocationId.ToString())
                                                {
                                                    Message("🚫 Вы уже в этой локации", player.Id, true);
                                                }
                                                else
                                                {
                                                    command.CommandText = $"UPDATE `players` SET `movingto`={int.Parse(message.Split(' ')[1])}, `stepsleft`={steps}, `grind`=0 WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                    player.MovingTo = int.Parse(message.Split(' ')[1]);
                                                    player.StepsLeft = steps;
                                                    Message("👣 Вы в пути!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы участвуете в турнире", player.Id, true);
                                            }
                                        }
                                        else if (message == "магазин" || message == "магаз" || message == "купить")
                                        {
                                            string answer = "💳 Список предметов в магазине:";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId} AND `type`='shop'";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Close();
                                                List<Item> items = GetShop(player.Id, player.Level);
                                                for (int i = 0; i < items.Count; i++)
                                                {
                                                    answer += $"\n\n";
                                                    string item = GetItemInfo(items[i], items[i].Level, "shop");
                                                    if (items[i].Type == "helmet")
                                                    {
                                                        answer += $"🧢 ";
                                                    }
                                                    else if (items[i].Type == "plate")
                                                    {
                                                        answer += $"👕 ";
                                                    }
                                                    else if (items[i].Type == "pants")
                                                    {
                                                        answer += $"👖 ";
                                                    }
                                                    else if (items[i].Type == "boots")
                                                    {
                                                        answer += $"👟 ";
                                                    }
                                                    else if (items[i].Type == "weapon")
                                                    {
                                                        answer += $"🔪 ";
                                                    }
                                                    else if (items[i].Type == "shield")
                                                    {
                                                        answer += $"🛡 ";
                                                    }
                                                    else if (items[i].Type == "rune")
                                                    {
                                                        answer += $"🀄 ";
                                                    }
                                                    else if (items[i].Type == "book")
                                                    {
                                                        answer += $"📖 ";
                                                    }
                                                    answer += $"{i + 1} | {items[i].Name} {item}";
                                                }
                                                Message($"{answer}\n\n💰 Купить предмет команда - Купить + номер продмета в магазине", player.Id, true);
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Вы не в магазине", player.Id, true);
                                            }
                                        }
                                        else if (message == "зельеварня" || message == "зелья")
                                        {
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId} AND `type`='potionshop'";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Close();
                                                Message($"🌿 Список зелий:\n\n💉 1 | Зелье здоровья\n💰 {player.Level * 1000}\n\n⚗ 2 | Зелье маны\n💰 {player.Level * 2500}\n\n💰 Купить зелье команда - Купить зелье + номер зелья + количество", player.Id, true);
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Вы не в зельеварне", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "продать")
                                        {
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId} AND `type`='shop'";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Close();
                                                List<Item> items = new List<Item>();
                                                command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    items.Add(new Item()
                                                    {
                                                        Id = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level"),
                                                        MinLevel = reader.GetInt32("id")
                                                    });
                                                }
                                                reader.Close();

                                                int id = Convert.ToInt32(message.Split(' ')[1]);
                                                if (id <= items.Count)
                                                {
                                                    int idInv = items[id - 1].MinLevel;
                                                    Item item = GetInventoryItem(items[id - 1].Id);
                                                    item.Level = items[id - 1].Level;

                                                    for (int i = 0; i < item.Bonuses.Count; i++)
                                                    {
                                                        item.Cost += Math.Abs(item.Bonuses[i].Value + item.Level) * 251;
                                                    }

                                                    item.Cost += Convert.ToInt64(item.Level * (item.Level * (item.Level / 4) * 2.5));

                                                    item.Cost = Convert.ToInt64(item.Cost / 3);

                                                    command.CommandText = $"UPDATE `players` SET `money`=`money`+{item.Cost} WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();

                                                    command.CommandText = $"DELETE FROM `inventory` WHERE `id`={idInv}";
                                                    command.ExecuteNonQuery();
                                                    Message($"💰 Вы продали {item.Name} за {item.Cost} золота", player.Id, true);
                                                }
                                                else
                                                {
                                                    Message("🚫 Неправильный номер предмета", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Вы не в магазине", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "купить" && message.Split(' ')[1] == "зелье")
                                        {
                                            int number = Convert.ToInt32(message.Split(' ')[2]);
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId} AND `type`='potionshop'";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Close();

                                                int count;

                                                try
                                                {
                                                    count = Convert.ToInt32(message.Split(' ')[3]);
                                                }
                                                catch
                                                {
                                                    count = 1;
                                                }

                                                if (number == 1)
                                                {
                                                    if (player.Money >= player.Level * 1000 * count)
                                                    {
                                                        command.CommandText = $"UPDATE `potions` SET `healpotion`=`healpotion`+1 WHERE `playerid`={player.Id};UPDATE `players` SET `money`=`money`-{player.Level * 1000 * count} WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();

                                                        Message($"💰 Вы купили зелье здоровья в количестве {count} за {player.Level * 1000 * count} золота", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message($"🚫 Для покупки необходимо еще {player.Level * 1000 * count - player.Money} золота", player.Id, true);
                                                    }
                                                }
                                                else if (number == 2)
                                                {
                                                    if (player.Money >= player.Level * 2500 * count)
                                                    {
                                                        command.CommandText = $"UPDATE `potions` SET `manapotion`=`manapotion`+1 WHERE `playerid`={player.Id};UPDATE `players` SET `money`=`money`-{player.Level * 2500 * count} WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();

                                                        Message($"💰 Вы купили зелье маны в количестве {count} за {player.Level * 2500 * count} золота", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message($"🚫 Для покупки необходимо еще {player.Level * 2500 * count - player.Money} золота", player.Id, true);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Вы не в зельеварне", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "купить")
                                        {
                                            int number = Convert.ToInt32(message.Split(' ')[1]) - 1;
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `locationid`={player.LocationId} AND `type`='shop'";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                reader.Close();
                                                int rawcount = 0;
                                                command.CommandText = $"SELECT COUNT(*) as count FROM `inventory` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    rawcount = reader.GetInt32("count");
                                                }
                                                reader.Close();

                                                if (rawcount < 10)
                                                {
                                                    List<Item> items = GetShop(player.Id, player.Level);
                                                    if (number < items.Count && number >= 0)
                                                    {
                                                        Item item = items[number];
                                                        if (player.Money >= items[number].Cost)
                                                        {
                                                            string newIds = "";
                                                            string newLevels = "";
                                                            for (int i = 0; i < items.Count; i++)
                                                            {
                                                                if (i != number)
                                                                {
                                                                    if (newIds == "")
                                                                    {
                                                                        newIds += $"{items[i].Id}";
                                                                        newLevels += $"{items[i].Level}";
                                                                    }
                                                                    else
                                                                    {
                                                                        newIds += $"_{items[i].Id}";
                                                                        newLevels += $"_{items[i].Level}";
                                                                    }
                                                                }
                                                            }
                                                            command.CommandText = $"UPDATE `shops` SET `itemsids`='{newIds}', `itemslevels`='{newLevels}' WHERE `playerid`={player.Id};UPDATE `players` SET `money`=`money`-{item.Cost} WHERE `id`={player.Id};INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{item.Id},{item.Level})";
                                                            command.ExecuteNonQuery();

                                                            Message($"💰 Вы купили {item.Name} за {item.Cost} золота", player.Id, true);
                                                        }
                                                        else
                                                        {
                                                            Message($"🚫 Для покупки необходимо еще {items[number].Cost - player.Money} золота", player.Id, true);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неправильный номер предмета", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Нет места в инвентаре", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                reader.Close();
                                                Message("🚫 Вы не в магазине", player.Id, true);
                                            }
                                        }
                                        else if (message == "портал" || message == "порталы" || message == "телепорт" || message == "телепорты" || message == "телепортироваться" || message == "тп")
                                        {
                                            List<int> portalsIds = new List<int>();
                                            string answer = "🌀 Список порталов:";

                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='portal'";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                int id = reader.GetInt32("locationid");
                                                portalsIds.Add(id);
                                            }
                                            reader.Close();

                                            bool inPortal = false;
                                            for (int i = 0; i < portalsIds.Count; i++)
                                            {
                                                if (portalsIds[i] == player.LocationId)
                                                {
                                                    inPortal = true;
                                                }
                                            }

                                            if (inPortal)
                                            {
                                                int count = 0;
                                                for (int i = 0; i < portalsIds.Count; i++)
                                                {
                                                    for (int j = 0; j < player.Portals.Count; j++)
                                                    {
                                                        if (portalsIds[i] == player.Portals[j] && player.LocationId != portalsIds[i])
                                                        {
                                                            count++;
                                                            answer += $"\n 🌀 {count} | {GetLocation(portalsIds[i]).Name}";
                                                        }
                                                    }
                                                }
                                                if (count == 0)
                                                {
                                                    answer += " Доступных порталов нет";
                                                }
                                                answer += "\n\n🌀 Для перемещения используйте команду - Портал + Номер портала";
                                                Message(answer, player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не в портале", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "портал" || message.Split(' ')[0] == "телепорт" || message.Split(' ')[0] == "порталы" || message.Split(' ')[0] == "телепорты" || message.Split(' ')[0] == "телепортироваться" || message.Split(' ')[0] == "тп")
                                        {
                                            int portalId = Convert.ToInt32(message.Split(' ')[1]) - 1;

                                            if (player.LocationId != portalId)
                                            {
                                                List<int> portalsIds = new List<int>();

                                                command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='portal'";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    int id = reader.GetInt32("locationid");
                                                    portalsIds.Add(id);
                                                }
                                                reader.Close();

                                                bool inPortal = false;
                                                for (int i = 0; i < portalsIds.Count; i++)
                                                {
                                                    if (portalsIds[i] == player.LocationId)
                                                    {
                                                        inPortal = true;
                                                    }
                                                }

                                                if (inPortal)
                                                {
                                                    List<int> availableIds = new List<int>();
                                                    for (int i = 0; i < portalsIds.Count; i++)
                                                    {
                                                        for (int j = 0; j < player.Portals.Count; j++)
                                                        {
                                                            if (portalsIds[i] == player.Portals[j] && player.LocationId != portalsIds[i])
                                                            {
                                                                availableIds.Add(portalsIds[i]);
                                                            }
                                                        }
                                                    }
                                                    if (portalId <= availableIds.Count - 1 && portalId >= 0)
                                                    {
                                                        command.CommandText = $"UPDATE `players` SET `locationid` = {availableIds[portalId]} WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player = GetPlayer(player.Id, users[u]);
                                                        Message($"🌀 Вы переместились в {GetLocation(availableIds[portalId]).Name}", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неверный номер портала", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Вы не в портале", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы и так находитесь в этой локации", player.Id, true);
                                            }
                                        }
                                        else if (message == "гринд" || message == "гриндить" || message == "фармить" || message == "фарм")
                                        {
                                            if (player.Stats.Tournament == false)
                                            {
                                                Location location = GetLocation(player.LocationId);
                                                if (player.Grind == true)
                                                {
                                                    Message("🏹 Гринд остановлен", player.Id, true);
                                                    command.CommandText = $"UPDATE `players` SET `grind`=0 WHERE `id`={player.Id}";
                                                    command.ExecuteNonQuery();
                                                    player = GetPlayer(player.Id, users[u]);
                                                }
                                                else
                                                {
                                                    if ((location.EntityLevel == 0 && location.Pvp == false) || location.Battle == 0)
                                                    {
                                                        Message("🚫 Здесь нельзя гриндить", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🏹 Гринд запущен", player.Id, true);
                                                        command.CommandText = $"UPDATE `players` SET `grind`=1 WHERE `id`={player.Id}";
                                                        command.ExecuteNonQuery();
                                                        player = GetPlayer(player.Id, users[u]);
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы участвуете в турнире", player.Id, true);
                                            }
                                        }
                                        else if (message == "инвентарь" || message == "инв")
                                        {
                                            List<Item> items = new List<Item>();
                                            command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                items.Add(new Item()
                                                {
                                                    Id = reader.GetInt32("itemid"),
                                                    Level = reader.GetInt16("level")
                                                });
                                            }
                                            reader.Close();

                                            string answer = $"💼 Инвентарь {items.Count}/10";

                                            if (items.Count > 0)
                                            {
                                                answer += ": ";
                                                for (int i = 0; i < items.Count; i++)
                                                {
                                                    answer += "\n\n";
                                                    Item item = GetInventoryItem(items[i].Id);
                                                    items[i].Bonuses = item.Bonuses;
                                                    items[i].MinLevel = item.MinLevel;
                                                    items[i].Name = item.Name;
                                                    items[i].Requests = item.Requests;
                                                    items[i].Type = item.Type;

                                                    if (item.Type == "helmet")
                                                    {
                                                        answer += $"🧢 ";
                                                    }
                                                    else if (item.Type == "plate")
                                                    {
                                                        answer += $"👕 ";
                                                    }
                                                    else if (item.Type == "pants")
                                                    {
                                                        answer += $"👖 ";
                                                    }
                                                    else if (item.Type == "boots")
                                                    {
                                                        answer += $"👟 ";
                                                    }
                                                    else if (item.Type == "weapon")
                                                    {
                                                        answer += $"🔪 ";
                                                    }
                                                    else if (item.Type == "shield")
                                                    {
                                                        answer += $"🛡 ";
                                                    }
                                                    else if (item.Type == "rune")
                                                    {
                                                        answer += $"🀄 ";
                                                    }
                                                    else if (item.Type == "book")
                                                    {
                                                        answer += $"📖 ";
                                                    }

                                                    answer += $"{i + 1} | {items[i].Name} ";
                                                    answer += GetItemInfo(item, items[i].Level, "inventory");
                                                }
                                                answer += "\n\n⚙ Чтобы надеть предмет - Команда Надеть + Номер предмета";
                                            }
                                            answer += $"\n\n🌿 Зелья:\n\n💉 Зелья здоровья: {player.Pot.Heal}/10\n⚗ Зелья маны: {player.Pot.Mana}/10";

                                            Message(answer, player.Id, true);
                                        }
                                        else if (message == "хранилище" || message == "сундук")
                                        {
                                            bool chest = false;
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='chest' AND `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                chest = true;
                                            }
                                            reader.Close();

                                            if (chest)
                                            {
                                                List<Item> items = new List<Item>();
                                                command.CommandText = $"SELECT * FROM `chest` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    items.Add(new Item()
                                                    {
                                                        Id = reader.GetInt32("itemid"),
                                                        Level = reader.GetInt16("level")
                                                    });
                                                }
                                                reader.Close();

                                                string answer = $"🗃 Хранилище {items.Count}/25: ";

                                                if (items.Count > 0)
                                                {
                                                    for (int i = 0; i < items.Count; i++)
                                                    {
                                                        answer += "\n\n";
                                                        Item item = GetInventoryItem(items[i].Id);
                                                        items[i].Bonuses = item.Bonuses;
                                                        items[i].MinLevel = item.MinLevel;
                                                        items[i].Name = item.Name;
                                                        items[i].Requests = item.Requests;
                                                        items[i].Type = item.Type;

                                                        if (item.Type == "helmet")
                                                        {
                                                            answer += $"🧢 ";
                                                        }
                                                        else if (item.Type == "plate")
                                                        {
                                                            answer += $"👕 ";
                                                        }
                                                        else if (item.Type == "pants")
                                                        {
                                                            answer += $"👖 ";
                                                        }
                                                        else if (item.Type == "boots")
                                                        {
                                                            answer += $"👟 ";
                                                        }
                                                        else if (item.Type == "weapon")
                                                        {
                                                            answer += $"🔪 ";
                                                        }
                                                        else if (item.Type == "shield")
                                                        {
                                                            answer += $"🛡 ";
                                                        }
                                                        else if (item.Type == "rune")
                                                        {
                                                            answer += $"🀄 ";
                                                        }
                                                        else if (item.Type == "book")
                                                        {
                                                            answer += $"📖 ";
                                                        }

                                                        answer += $"{i + 1} | {items[i].Name} ";
                                                        answer += GetItemInfo(item, items[i].Level, "inventory");
                                                    }
                                                    answer += "\n\n⚙ Чтобы вытащить предмет из хранилища - Команда Вытащить + Номер предмета в хранилище\n⚙ Чтобы положить предмет в хранилище - Команда Положить + Номер предмета в инвентаре";
                                                }
                                                else
                                                {
                                                    answer += "пусто\n\n⚙ Чтобы положить предмет в хранилище - Команда Положить + Номер предмета в инвентаре";
                                                }

                                                Message(answer, player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не в хранилище", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "положить")
                                        {
                                            bool chest = false;
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='chest' AND `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                chest = true;
                                            }
                                            reader.Close();

                                            if (chest)
                                            {
                                                int count = 0;
                                                command.CommandText = $"SELECT COUNT(*) as `count` FROM `chest` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    count = reader.GetInt32("count");
                                                }
                                                reader.Close();

                                                if (count < 25)
                                                {
                                                    List<Item> items = new List<Item>();
                                                    command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                                    reader = command.ExecuteReader();
                                                    while (reader.Read())
                                                    {
                                                        items.Add(new Item()
                                                        {
                                                            Id = reader.GetInt32("itemid"),
                                                            Level = reader.GetInt16("level"),
                                                            MinLevel = reader.GetInt32("id")
                                                        });
                                                    }
                                                    reader.Close();

                                                    int id = Convert.ToInt32(message.Split(' ')[1]);
                                                    if (id <= items.Count)
                                                    {
                                                        int idInv = items[id - 1].MinLevel;
                                                        Item item = GetInventoryItem(items[id - 1].Id);
                                                        item.Level = items[id - 1].Level;
                                                        command.CommandText = $"DELETE FROM `inventory` WHERE `id`={idInv};INSERT INTO `chest`(`playerid`,`itemid`,`level`) VALUES({player.Id},{items[id - 1].Id},{item.Level})";
                                                        command.ExecuteNonQuery();
                                                        Message($"⚙ Вы положили {item.Name} в хранилище", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неправильный номер предмета", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Нет места в хранилище", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не в хранилище", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "вытащить")
                                        {
                                            bool chest = false;
                                            command.CommandText = $"SELECT * FROM `speciallocations` WHERE `type`='chest' AND `locationid`={player.LocationId}";
                                            reader = command.ExecuteReader();
                                            if (reader.HasRows)
                                            {
                                                chest = true;
                                            }
                                            reader.Close();

                                            if (chest)
                                            {
                                                int count = 0;
                                                command.CommandText = $"SELECT COUNT(*) as `count` FROM `inventory` WHERE `playerid`={player.Id}";
                                                reader = command.ExecuteReader();
                                                while (reader.Read())
                                                {
                                                    count = reader.GetInt32("count");
                                                }
                                                reader.Close();

                                                if (count < 10)
                                                {
                                                    List<Item> items = new List<Item>();
                                                    command.CommandText = $"SELECT * FROM `chest` WHERE `playerid`={player.Id}";
                                                    reader = command.ExecuteReader();
                                                    while (reader.Read())
                                                    {
                                                        items.Add(new Item()
                                                        {
                                                            Id = reader.GetInt32("itemid"),
                                                            Level = reader.GetInt16("level"),
                                                            MinLevel = reader.GetInt32("id")
                                                        });
                                                    }
                                                    reader.Close();

                                                    int id = Convert.ToInt32(message.Split(' ')[1]);
                                                    if (id <= items.Count)
                                                    {
                                                        int idInv = items[id - 1].MinLevel;
                                                        Item item = GetInventoryItem(items[id - 1].Id);
                                                        item.Level = items[id - 1].Level;
                                                        command.CommandText = $"DELETE FROM `chest` WHERE `id`={idInv};INSERT INTO `inventory`(`playerid`,`itemid`,`level`) VALUES({player.Id},{items[id - 1].Id},{item.Level})";
                                                        command.ExecuteNonQuery();
                                                        Message($"⚙ Вы вытащили {item.Name} из хранилища", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Неправильный номер предмета", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Нет места в инвентаре", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Вы не в хранилище", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "выбросить" || message.Split(' ')[0] == "выкинуть")
                                        {
                                            List<Item> items = new List<Item>();
                                            command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                items.Add(new Item()
                                                {
                                                    Id = reader.GetInt32("itemid"),
                                                    Level = reader.GetInt16("level"),
                                                    MinLevel = reader.GetInt32("id")
                                                });
                                            }
                                            reader.Close();

                                            int id = Convert.ToInt32(message.Split(' ')[1]);
                                            if (id <= items.Count)
                                            {
                                                int idInv = items[id - 1].MinLevel;
                                                Item item = GetInventoryItem(items[id - 1].Id);
                                                item.Level = items[id - 1].Level;
                                                command.CommandText = $"DELETE FROM `inventory` WHERE `id`={idInv}";
                                                command.ExecuteNonQuery();
                                                Message($"⚙ Вы выбросили {item.Name}", player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Неправильный номер предмета", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "надеть")
                                        {
                                            List<Item> items = new List<Item>();
                                            command.CommandText = $"SELECT * FROM `inventory` WHERE `playerid`={player.Id}";
                                            reader = command.ExecuteReader();
                                            while (reader.Read())
                                            {
                                                items.Add(new Item()
                                                {
                                                    Id = reader.GetInt32("itemid"),
                                                    Level = reader.GetInt16("level"),
                                                    MinLevel = reader.GetInt32("id")
                                                });
                                            }
                                            reader.Close();

                                            int id = Convert.ToInt32(message.Split(' ')[1]);
                                            if (id <= items.Count)
                                            {
                                                int idInv = items[id - 1].MinLevel;
                                                Item item = GetInventoryItem(items[id - 1].Id);
                                                item.Level = items[id - 1].Level;
                                                item.Id = items[id - 1].Id;

                                                if (item.MinLevel + (item.Level - item.MinLevel) <= player.Level)
                                                {
                                                    bool requestOK = true;
                                                    for (int i = 0; i < item.Requests.Count; i++)
                                                    {
                                                        Request request = item.Requests[i];

                                                        if (request.Name == "damagerate" && request.Value + item.Level - 1 > Convert.ToInt32(player.Stats.DamageRate + player.Stats.DamageRate * (player.Level - 1) * 0.25))
                                                        {
                                                            requestOK = false;
                                                        }
                                                        else if (request.Name == "attackrate" && request.Value + item.Level - 1 > player.Stats.AttackRate)
                                                        {
                                                            requestOK = false;
                                                        }
                                                        else if (request.Name == "defenserate" && request.Value + item.Level - 1 > player.Stats.DefenseRate)
                                                        {
                                                            requestOK = false;
                                                        }
                                                        else if (request.Name == "criticalrate" && request.Value + item.Level - 1 > player.Stats.CriticalRate)
                                                        {
                                                            requestOK = false;
                                                        }
                                                        else if (request.Name == "healrate" && request.Value + item.Level - 1 > player.Stats.HealRate)
                                                        {
                                                            requestOK = false;
                                                        }
                                                        else if (request.Name == "magicrate" && request.Value + item.Level - 1 > player.Stats.MagicRate)
                                                        {
                                                            requestOK = false;
                                                        }
                                                    }
                                                    if (requestOK)
                                                    {
                                                        if (item.Type == "helmet")
                                                        {
                                                            if (player.Equipt.HelmetId > 0)
                                                            {
                                                                Item helmet = GetEquipedItem(player.Equipt.HelmetId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `helmetid`={item.Id}, `helmetlevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={helmet.Id}, `level`={helmet.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `helmetid`={item.Id}, `helmetlevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "plate")
                                                        {
                                                            if (player.Equipt.PlateId > 0)
                                                            {
                                                                Item plate = GetEquipedItem(player.Equipt.PlateId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `plateid`={item.Id}, `platelevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={plate.Id}, `level`={plate.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `plateid`={item.Id}, `platelevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "pants")
                                                        {
                                                            if (player.Equipt.PantsId > 0)
                                                            {
                                                                Item pants = GetEquipedItem(player.Equipt.PantsId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `pantsid`={item.Id}, `pantslevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={pants.Id}, `level`={pants.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `pantsid`={item.Id}, `pantslevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "boots")
                                                        {
                                                            if (player.Equipt.BootsId > 0)
                                                            {
                                                                Item boots = GetEquipedItem(player.Equipt.BootsId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `bootsid`={item.Id}, `bootslevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={boots.Id}, `level`={boots.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `bootsid`={item.Id}, `bootslevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "weapon")
                                                        {
                                                            if (player.Equipt.WeaponId > 0)
                                                            {
                                                                Item weapon = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `weaponid`={item.Id}, `weaponlevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={weapon.Id}, `level`={weapon.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `weaponid`={item.Id}, `weaponlevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "shield")
                                                        {
                                                            if (player.Equipt.ShieldId > 0)
                                                            {
                                                                Item shield = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `shieldid`={item.Id}, `shieldlevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={shield.Id}, `level`={shield.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `shieldid`={item.Id}, `shieldlevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "rune")
                                                        {
                                                            if (player.Equipt.RuneId > 0)
                                                            {
                                                                Item rune = GetEquipedItem(player.Equipt.RuneId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `runeid`={item.Id}, `runelevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={rune.Id}, `level`={rune.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `runeid`={item.Id}, `runelevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        else if (item.Type == "book")
                                                        {
                                                            if (player.Equipt.BookId > 0)
                                                            {
                                                                Item book = GetEquipedItem(player.Equipt.BookId, player.Id);
                                                                command.CommandText = $"UPDATE `equipment` SET `bookid`={item.Id}, `booklevel`={item.Level} WHERE `playerid`={player.Id};UPDATE `inventory` SET `itemid`={book.Id}, `level`={book.Level} WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                            else
                                                            {
                                                                command.CommandText = $"UPDATE `equipment` SET `bookid`={item.Id}, `booklevel`={item.Level} WHERE `playerid`={player.Id};DELETE FROM `inventory` WHERE `id`={idInv}";
                                                                command.ExecuteNonQuery();
                                                            }
                                                        }
                                                        Message($"⚙ Вы надели {item.Name}", player.Id, true);
                                                    }
                                                    else
                                                    {
                                                        Message("🚫 Недостаточный уровень умений", player.Id, true);
                                                    }
                                                }
                                                else
                                                {
                                                    Message("🚫 Слишком низкий уровень", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message("🚫 Неправильный номер предмета", player.Id, true);
                                            }
                                        }
                                        else if (message == "снаряга" || message == "снаряжение" || message == "экипировка" || message == "кипа")
                                        {
                                            string equipment = "🥋 Снаряжение:\n";
                                            equipment += "\n🧢 Голова: ";
                                            if (player.Equipt.HelmetId > 0)
                                            {
                                                Item helmet = GetEquipedItem(player.Equipt.HelmetId, player.Id);
                                                equipment += helmet.Name;
                                                equipment += GetItemInfo(helmet, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n👕 Броня: ";
                                            if (player.Equipt.PlateId > 0)
                                            {
                                                Item plate = GetEquipedItem(player.Equipt.PlateId, player.Id);
                                                equipment += plate.Name;
                                                equipment += GetItemInfo(plate, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n👖 Ноги: ";
                                            if (player.Equipt.PantsId > 0)
                                            {
                                                Item pants = GetEquipedItem(player.Equipt.PantsId, player.Id);
                                                equipment += pants.Name;
                                                equipment += GetItemInfo(pants, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n👟 Ботинки: ";
                                            if (player.Equipt.BootsId > 0)
                                            {
                                                Item boots = GetEquipedItem(player.Equipt.BootsId, player.Id);
                                                equipment += boots.Name;
                                                equipment += GetItemInfo(boots, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n🔪 Оружие: ";
                                            if (player.Equipt.WeaponId > 0)
                                            {
                                                Item weapon = GetEquipedItem(player.Equipt.WeaponId, player.Id);
                                                equipment += weapon.Name;
                                                equipment += GetItemInfo(weapon, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n🛡 Щит: ";
                                            if (player.Equipt.ShieldId > 0)
                                            {
                                                Item shield = GetEquipedItem(player.Equipt.ShieldId, player.Id);
                                                equipment += shield.Name;
                                                equipment += GetItemInfo(shield, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n🀄 Руна: ";
                                            if (player.Equipt.RuneId > 0)
                                            {
                                                Item rune = GetEquipedItem(player.Equipt.RuneId, player.Id);
                                                equipment += rune.Name;
                                                equipment += GetItemInfo(rune, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n📖 Книга: ";
                                            if (player.Equipt.BookId > 0)
                                            {
                                                Item book = GetEquipedItem(player.Equipt.BookId, player.Id);
                                                equipment += book.Name;
                                                equipment += GetItemInfo(book, 0, "equipment");
                                            }
                                            else
                                            {
                                                equipment += "Пусто";
                                            }

                                            equipment += "\n\n⚙ Меню снятия снаряжения команда - Снять";

                                            Message(equipment, player.Id, true);
                                        }
                                        else if (message == "улучшить персонажа" || message == "улучшить" || message == "апнуть")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                UpdateMessage($"💫 Выберите улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                            }
                                            else
                                            {
                                                Message("🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень урона")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `damagerate`=`damagerate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"🔪 Уровень урона увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"🔪 Уровень урона увеличен до {player.Stats.DamageRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень силы")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `attackrate`=`attackrate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"👊🏻 Уровень силы увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"👊🏻 Уровень силы увеличен до {player.Stats.AttackRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень защиты")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `defenserate`=`defenserate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"🛡 Уровень защиты увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"🛡 Уровень защиты увеличен до {player.Stats.DefenseRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень боя")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `criticalrate`=`criticalrate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"💥 Уровень боя увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"💥 Уровень боя увеличен до {player.Stats.CriticalRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень лечения")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `healrate`=`healrate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"💚 Уровень лечения увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"💚 Уровень лечения увеличен до {player.Stats.HealRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message == "увеличить уровень волшебства")
                                        {
                                            if (player.Stats.SkillPoints > 0)
                                            {
                                                command.CommandText = $"UPDATE `stats` SET `magicrate`=`magicrate`+1, `skillpoints`=`skillpoints`-1 WHERE `playerid`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                if (player.Stats.SkillPoints > 0)
                                                {
                                                    string answer = $"\n🔪 Уровень урона: {player.Stats.DamageRate}\n👊🏻 Уровень силы: {player.Stats.AttackRate}\n🛡 Уровень защиты: {player.Stats.DefenseRate}\n💥 Уровень боя: {player.Stats.CriticalRate}\n💚 Уровень лечения: {player.Stats.HealRate}\n✨ Уровень волшебства: {player.Stats.MagicRate}";
                                                    UpdateMessage($"✨ Уровень волшебства увеличен!\n\n💫 Выберите следующее улучшение (Доступно очков: {player.Stats.SkillPoints})\n{answer}", player.Id);
                                                }
                                                else
                                                {
                                                    Message($"✨ Уровень волшебства увеличен до {player.Stats.MagicRate}!", player.Id, true);
                                                }
                                            }
                                            else
                                            {
                                                Message($"🚫 У вас отсутствуют очки умений", player.Id, true);
                                            }
                                        }
                                        else if (message.Split(' ')[0] == "имя" || message.Split(' ')[0] == "ник")
                                        {
                                            string name = source;
                                            string newName = "";
                                            for (int i = 0; i < name.Split(' ').Length - 1; i++)
                                            {
                                                newName += name.Split(' ')[i + 1];
                                                if (name.Split(' ').Length - 2 != i)
                                                {
                                                    newName += ' ';
                                                }
                                            }

                                            if (newName == player.Name)
                                            {
                                                Message("🚫 Старое и новое имя - одинаковые", player.Id, false);
                                            }
                                            else if (player.Diamonds < 1)
                                            {
                                                Message("🚫 Недостаточно алмазов (💎 стоимость: 1)", player.Id, false);
                                            }
                                            else if (newName.Length < 5)
                                            {
                                                Message("🚫 Слишком короткое имя (минимум 5 символов)", player.Id, false);
                                            }
                                            else if (newName.Length <= 30)
                                            {
                                                command.CommandText = $"UPDATE `players` SET `diamonds`=`diamonds`-1,`name`='{newName}' WHERE `id`={player.Id}";
                                                command.ExecuteNonQuery();
                                                player = GetPlayer(player.Id, users[u]);
                                                Message($"♻ Успешная смена имени! 💎 Оставшийся баланс: {player.Diamonds}", player.Id, true);
                                            }
                                            else
                                            {
                                                Message("🚫 Слишком длинное имя (максимум 30 символов)", player.Id, false);
                                            }
                                        }
                                        else
                                        {
                                            Message("🚫 Неправильная команда", player.Id, true);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Console.Write(e.ToString());
                                        Message("🚫 Неправильная команда", player.Id, true);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.Write(e.ToString());
                        }
                    }

                    #region Поиск врага
                    if (player.Grind == true && player.Fight == false)
                    {
                        bool attack = false;
                        int chance = rnd.Next(0, 100);
                        int chancePvp = rnd.Next(0, 100);
                        Location location = GetLocation(player.LocationId);
                        List<Player> players = GetPlayersOnLocation(player.LocationId, player.Id, users);
                        if (100 - location.Battle <= chance)
                        {
                            if (location.Pvp == true && chancePvp <= 20 && players.Count > 0)
                            {
                                for (int i = 0; i < players.Count; i++)
                                {
                                    if (attack == false)
                                    {
                                        Message($"⚔ PVP Информатор\n\n👤 {player.Name} @id{player.Id} (👁‍🗨)\n🔱 Уровень: {player.Level}\n❤ Здоровье: {player.Health}/{player.MaxHealth}\n\nVS\n\n👤 {players[i].Name} @id{players[i].Id} (👁‍🗨)\n🔱 Уровень: {players[i].Level}\n❤ Здоровье: {players[i].Health}/{players[i].MaxHealth}", 2000000001, false);
                                        Message($"⚔ Вы начали бой с игроком: {players[i].Name}\n🔱 Уровень врага: {players[i].Level}\n❤ Здоровье врага: {players[i].Health}/{players[i].MaxHealth}\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}", player.Id, false);
                                        Message($"⚔ С вами начал бой игрок: {player.Name}\n🔱 Уровень врага: {player.Level}\n❤ Здоровье врага: {player.Health}/{player.MaxHealth}\n❤ Ваше здоровье: {players[i].Health}/{players[i].MaxHealth}", players[i].Id, false);
                                        command.CommandText = $"UPDATE `players` SET `fight`=1, `attacksteps`=5,`attackerid`={players[i].Id},`attackertype`=1 WHERE `id`={player.Id};UPDATE `players` SET `fight`=1, `attacksteps`=10,`attackerid`={player.Id},`attackertype`=1 WHERE `id`={players[i].Id}";
                                        command.ExecuteNonQuery();
                                        attack = true;
                                        player = GetPlayer(player.Id, users[u]);
                                    }
                                }
                            }
                            else if (location.EntityLevel > 0)
                            {
                                List<int> mobsIds = new List<int>();
                                command.CommandText = $"SELECT * FROM `mobs_locations_relations` WHERE `locationid` = {player.LocationId}";
                                reader = command.ExecuteReader();
                                while (reader.Read())
                                {
                                    mobsIds.Add(reader.GetInt32("mobid"));
                                }
                                reader.Close();
                                Mob mob = GetMob(mobsIds[rnd.Next(0, mobsIds.Count)], player.LocationId);

                                Message($"⚔ Вы начали бой с мобом: {mob.Name}\n🔱 Уровень моба: {mob.Level}\n❤ Здоровье моба: {mob.Health}/{mob.MaxHealth}\n❤ Ваше здоровье: {player.Health}/{player.MaxHealth}", player.Id, false);

                                command.CommandText = $"UPDATE `players` SET `fight`=1, `attacksteps`=10,`attackerid`={mob.Id + 1000 * mob.MaxHealth},`attackertype`=0 WHERE `id`={player.Id};";
                                command.ExecuteNonQuery();
                                GetPlayer(player.Id, users[u]);
                            }
                        }
                    }
                    #endregion
                }
            }
        }
    }
}
