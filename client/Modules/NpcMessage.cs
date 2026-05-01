using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace pitTeam.Modules
{
    internal class NpcMessage
    {
        private static NpcMessage? Instance;

        private Dictionary<string, object> _npcs = new Dictionary<string, object>();
        private List<string> _matesLost = new List<string>();

        private bool _playerDied = false;

        public NpcMessage()
        {
            if (Instance == null)
            {
                Instance = this;
                _npcs = new Dictionary<string, object>();
                _matesLost = new List<string>();
            }
        }

        public static void AddNpc(BotOwner npc, bool isSquadMate = true, bool isBoss = false)
        {
            if (Instance == null)
            {
                return;
            }

            if (!Instance._npcs.ContainsKey(npc.ProfileId))
            {
                Instance._npcs.Add(npc.ProfileId, new Dictionary<string, object> {
                    { "_id" , npc.ProfileId  },
                    { "aid" , npc.Profile.AccountId },
                    {
                        "Info" , new Dictionary<string, object>{
                            { "Level", npc.Profile.Info.Level },
                            { "MemberCategory", npc.Profile.Info.MemberCategory },
                            { "Nickname",  npc.Profile.Info.Nickname },
                            { "Side",  npc.Profile.Info.Side },
                        }
                    },
                    {
                        "SquadInfo", new Dictionary<string, object>
                        {
                            { "Mate", isSquadMate  },
                            { "AllyBoss", isBoss ? npc.Profile.Info.Settings.Role.ToString() : string.Empty }
                        }
                    }
                });
            }
        }

        public static void RemoveNpc(string id)
        {
            if (Instance == null || Instance._playerDied) return;

            if (Instance._npcs.ContainsKey(id))
            {
                if (((Dictionary<string, object>)Instance._npcs[id])["SquadInfo"] is Dictionary<string, object> squadInfo)
                {
                    if ((bool)squadInfo["Mate"])
                    {
                        if (((Dictionary<string, object>)Instance._npcs[id])["Info"] is Dictionary<string, object> memberInfo)
                        {
                            Instance._matesLost.Add((string)memberInfo["Nickname"]);
                        }
                    }
                }

                Instance._npcs.Remove(id);
            }
        }

        public static string? GetNpcType(string type)
        {
            if (Instance == null)
            {
                return null;
            }

            List<string> mates = new List<string>();
            List<string> allies = new List<string>();
            List<string> bosses = new List<string>();

            foreach (var item in Instance._npcs)
            {
                if (((Dictionary<string, object>)item.Value)["SquadInfo"] is Dictionary<string, object> squadInfo)
                {
                    if ((string)squadInfo["AllyBoss"] != null)
                        bosses.Add(item.Key);
                    else if (!(bool)squadInfo["Mate"])
                        allies.Add(item.Key);
                    else
                        mates.Add(item.Key);
                }
            }

            if (type == "ally" && allies.Count > 0) return allies.Random();
            else if (type == "boss" && bosses.Count > 0) return bosses.Random();
            else if (mates.Count > 0) return mates.Random();

            return null;
        }

        public static void NpcSendThankYou(string? id = null)
        {
            if (Instance == null || Instance._playerDied || !pitFireTeam.npcSendMessage.Value) { return; }

            List<object> mates = new List<object>();
            List<object> allies = new List<object>();
            List<object> bosses = new List<object>();

            object? info = null;

            if (id == null)
            {
                foreach (var item in Instance._npcs)
                {
                    if (((Dictionary<string, object>)item.Value)["SquadInfo"] is Dictionary<string, object> squadInfo)
                    {
                        if ((string)squadInfo["AllyBoss"] != null)
                            bosses.Add(item.Value);
                        else if (!(bool)squadInfo["Mate"])
                            allies.Add(item.Value);
                        else
                            mates.Add(item.Value);
                    }
                }

                if (allies.Count > 0) info = allies.Random();
                else
                {
                    info = mates.Count > 0 ? mates.Random() : null;
                    if (info != null && Instance._matesLost.Count > 0)
                    {
                        ((Dictionary<string, object>)((Dictionary<string, object>)info)["SquadInfo"]).Add("Partial", true);
                        ((Dictionary<string, object>)((Dictionary<string, object>)info)["SquadInfo"]).Add("Lost", Instance._matesLost);
                    }
                }

            }
            else if (Instance._npcs.ContainsKey(id))
            {
                info = Instance._npcs[id];
            }

            if (info == null) return;

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            string escapedJson = new
            {
                member = info,
            }.ToJson(_defaultJsonConverters);

            Task.Run(() =>
            {
                try
                {
                    RequestHandler.PostJson("/singleplayer/teamescaped", escapedJson);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("Failed to send post-raid teammate escaped message");
                    Modules.Logger.LogError(ex);
                }
            });
        }

        public static void Flush()
        {
            if (Instance == null) return;
            Instance._npcs.Clear();
            Instance._matesLost.Clear();
        }

        public static void PlayerDied()
        {
            if (Instance == null) return;
            Instance._playerDied = true;
        }

        public static void Dispose()
        {
            if (Instance == null) return;
            Instance._npcs.Clear();
            Instance = null;
        }
    }
}
