using EFT;
using EFT.Counters;

using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using friendlySAIN.Components;
using friendlySAIN.Utils;

namespace friendlySAIN.Modules
{
    internal class RecruitPickupCandidateRequest
    {
        public string ProfileId { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public int Level { get; set; }
        public string Side { get; set; } = string.Empty;
        public string Voice { get; set; } = string.Empty;
        public string Head { get; set; } = string.Empty;
    }

    internal class RecruitPickupRequest
    {
        public List<RecruitPickupCandidateRequest> Candidates { get; set; } = new List<RecruitPickupCandidateRequest>();
    }

    /**
     *  Helper class to manage Boss Players and their followers
     */
    public class BossPlayers
    {
        public static BossPlayers Instance { get; private set; }

        private Dictionary<string, pitAIBossPlayer> _bosses;
        private List<BotFollowerPlayer> _followers;
        private Dictionary<string, BotFollowerPlayer> _followersByProfileId;
        private List<string> _shallBeFollower;
        private List<int> _botsGroup;


        private List<string> _removedBosses;

        public bool IsDisposed = false;

        public BossPlayers()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            _bosses = new Dictionary<string, pitAIBossPlayer>();
            _followers = new List<BotFollowerPlayer> { };
            _followersByProfileId = new Dictionary<string, BotFollowerPlayer>(StringComparer.Ordinal);
            _shallBeFollower = new List<string> { };
            _removedBosses = new List<string> { };
            _botsGroup = new List<int> { };
        }

        public static void Dispose()
        {
            if (Instance != null)
            {
                Instance.Destroy();
                Instance = null;
            }
        }


        private pitAIBossPlayer AddBossPlayer(Player player, BotsController botsController)
        {
            if (_bosses.ContainsKey(player.ProfileId)) return _bosses[player.ProfileId];


            WildSpawnType roleType = player.Profile.Info.Settings.Role;
            player.Profile.Info.Settings.Role = WildSpawnType.bossKnight; // temp switch to boss role
            pitAIBossPlayer playerBoss = new pitAIBossPlayer(player, botsController);
            player.Profile.Info.Settings.Role = roleType; // revert role back to original

            // force bosses to not warn the player (these will be exUsec and the Goons)
            if (player.Side != EPlayerSide.Savage && Utils.Utils.PlayerHasKnightQuest(player.Profile))
            {
                var field = typeof(FenceLoyaltyLevel).GetField("HostileBosses", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    FieldInfo attrField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
                    attrField?.SetValue(field, field.Attributes & ~FieldAttributes.InitOnly);

                    field.SetValue(player.Profile.FenceInfo.FenceLoyalty, false);
                }
            }

            if (!playerBoss.IAmBoos)
            {
                Logger.LogInfo($"Could not make player {player.Profile.Nickname} as BOSS");
                return null;
            }
            else
            {
                Logger.LogInfo($"Made player {player.Profile.Nickname} a BOSS");
            }

            string name = player.ProfileId;

            if (string.IsNullOrEmpty(player.Profile.Info.GroupId))
            {
                player.Profile.Info.GroupId = "bossGroup_" + name;
            }

            if (_removedBosses.Contains(name))
            {
                _removedBosses.Remove(name);
            }

            _bosses[name] = playerBoss;

            return playerBoss;

        }

        private bool RemoveBossPlayer(string name, bool died = false)
        {
            if (_bosses.ContainsKey(name))
            {
                pitAIBossPlayer boss = _bosses[name];

                List<BotOwner> ownersToRemove = new List<BotOwner>();
                List<BotFollowerPlayer> followersToRemove = new List<BotFollowerPlayer>();

                boss.Followers.ForEach(fl =>
                {
                    ownersToRemove.Add(fl);
                    _followers.ForEach(follower =>
                    {
                        if (follower.IsBot(fl))
                        {
                            followersToRemove.Add(follower);
                        }
                    });
                });
                if (followersToRemove.Count > 0)
                {
                    SaveFollowersProgress(died ? followersToRemove.FindAll(fl => fl.IsSquadMate) : followersToRemove);
                }

                ownersToRemove.ForEach(follower =>
                {
                    boss.RemoveFollower(follower);
                });

                followersToRemove.ForEach(_follower =>
                {
                    _followers.Remove(_follower);
                    BotOwner followerBot = _follower.GetBot();
                    if (followerBot?.ProfileId != null)
                    {
                        _followersByProfileId.Remove(followerBot.ProfileId);
                    }
                    _follower.Dismiss();
                });

                boss.Followers.Clear();

                boss.DisposeBoss();

                _bosses.Remove(name);
                _removedBosses.Add(name);
                return true;
            }
            else if (_removedBosses.Contains(name))
            {
                return true;
            }

            return false;
        }

        private void Destroy()
        {
            if (IsDisposed) return;

            if (_bosses.Count > 0)
            {
                List<string> keys = new List<string>(_bosses.Keys);
                foreach (string key in keys)
                {
                    RemoveBossPlayer(key);
                }
            }
            _bosses.Clear();
            _removedBosses.Clear();
            _followers.Clear();
            _followersByProfileId.Clear();
            _shallBeFollower.Clear();
            FollowerGrenadeCooldowns.ClearAll();

            IsDisposed = true;
            Instance = null;
        }

        private BotFollowerPlayer AddBotFollower(BotOwner bot, pitAIBossPlayer player, bool squadMate = false, WildSpawnType role = WildSpawnType.assault, string tactic = "Default", float aggression = 50f)
        {

            BotFollowerPlayer? _follower = null;

            _followers.ForEach(follower =>
            {
                if (follower.IsBot(bot))
                {
                    _follower = follower;
                }
            });

            if (_follower != null)
            {
                return _follower;
            }

            if (_shallBeFollower.Contains(bot.ProfileId)) _shallBeFollower.Remove(bot.ProfileId);

            bool isAIBoss = false;

            foreach (var item in Utils.Props.BossFollowersType)
            {
                if (role == item)
                {
                    isAIBoss = true;
                    break;
                }
            }

            if (!isAIBoss)
            {
                _follower = new BotFollowerPlayer(bot, player, squadMate);
            }
            else
            {
                _follower = new BossFollowerPlayer(bot, player, role);

            }

            _follower.CombatTactic = BotFollowerPlayer.ParseCombatTactic(tactic);
            _follower.CombatAggression = aggression;

            try
            {
                _follower.Init();

            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Failed to init follower");
                Modules.Logger.LogError(e);
                return null;
            }

            _followers.Add(_follower);
            if (bot?.ProfileId != null)
            {
                _followersByProfileId[bot.ProfileId] = _follower;
            }

            // Fire lifecycle event for addon integration (cache registration, etc).
            SainAddonBridge.RaiseFollowerLifecycleEvent(bot, FollowerLifecycleEvent.OnRecruited);

            return _follower;
        }

        private static double GetLevelFactor(int playerLevel, int botLevel)
        {
            const double minValue = 1;
            const double maxValue = 4;
            const int threshold = 7; // Point where the decrease starts

            if (botLevel <= playerLevel - threshold)
                return maxValue;
            if (botLevel >= playerLevel + threshold)
                return minValue;

            // Linear interpolation 
            double factor = maxValue - ((botLevel - (playerLevel + threshold)) / threshold) * (maxValue - minValue);

            return Math.Max(minValue, Math.Min(maxValue, factor)); // Ensure it's within range
        }


        public static void SaveFollowersProgress(List<BotFollowerPlayer> followers = null)
        {
            if (Instance._followers.Count == 0 && (followers == null || followers.Count < 1)) return;

            List<RecruitPickupCandidateRequest> recruitCandidates = new List<RecruitPickupCandidateRequest>();

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                   .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            try
            {
                List<BotFollowerPlayer> _followers = followers ?? Instance._followers;
                List<object> data = new List<object>();

                foreach (var item in _followers)
                {
                    Profile pr = item.GetBot().Profile;
                    WildSpawnType role = pr.Info.Settings.Role;

                    if (Utils.Props.BossFollowersType.Contains(role) || item.GetBot().Side == EPlayerSide.Savage)
                    {
                        continue;
                    }

                    if (!item.IsSquadMate)
                    {
                        if (friendlySAIN.recruitPickup.Value && item.GetBoss()?.realPlayer?.Side != EPlayerSide.Savage)
                        {
                            string voiceId = pr.Customization != null && pr.Customization.TryGetValue(EBodyModelPart.Voice, out MongoID voice) ? voice.ToString() : string.Empty;
                            string headId = pr.Customization != null && pr.Customization.TryGetValue(EBodyModelPart.Head, out MongoID head) ? head.ToString() : string.Empty;

                            if (!string.IsNullOrWhiteSpace(pr.Id) &&
                                !string.IsNullOrWhiteSpace(pr.Nickname) &&
                                !string.IsNullOrWhiteSpace(voiceId) &&
                                !string.IsNullOrWhiteSpace(headId))
                            {
                                recruitCandidates.Add(new RecruitPickupCandidateRequest
                                {
                                    ProfileId = pr.Id,
                                    Nickname = pr.Nickname,
                                    Level = pr.Info?.Level ?? 1,
                                    Side = pr.Info?.Side.ToString() ?? string.Empty,
                                    Voice = voiceId,
                                    Head = headId,
                                });
                            }
                        }

                        continue;
                    }

                    List<object> skills = new List<object>();

                    pr.Skills.DisplayList.ExecuteForEach(skill =>
                    {
                        skills.Add(new
                        {
                            Id = skill.Id,
                            Current = skill.Current,
                            Progress = skill.ProgressValue,
                            PointsEarnedDuringSession = skill.PointsEarned,
                        });
                    });

                    if (skills.Find(skill => (ESkillId)skill.GetType().GetProperty("Id").GetValue(skill) == ESkillId.BotReload) == null)
                    {
                        skills.Add(new
                        {
                            Id = ESkillId.BotReload,
                            Current = pr.Skills.BotReload.Current,
                            Progress = pr.Skills.BotReload.ProgressValue,
                            PointsEarnedDuringSession = 0,
                        });
                    }

                    var boss = item.GetBoss();

                    int bossLevel = boss.realPlayer.Profile.Info.Level;
                    int botLevel = pr.Info.Level;
                    data.Add(new
                    {
                        Aid = pr.AccountId,
                        BotExperienceSession = Math.Round(pr.EftStats.SessionCounters.GetAllInt(new object[] { CounterTag.ExpKill }) + (boss.realPlayer.Profile.EftStats.SessionCounters.GetAllInt(new object[] { CounterTag.Exp }) * GetLevelFactor(bossLevel, botLevel))),
                        Skills = skills
                    });
                }

                RequestHandler.PostJson("/client/game/bot/followerprogress", new
                {
                    Entries = data
                }.ToJson(_defaultJsonConverters));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to save followers progress");
                Modules.Logger.LogError(ex);
            }

            if (recruitCandidates.Count > 0)
            {
                RequestHandler.PostJson("/singleplayer/friendlysain/recruitpickup", new RecruitPickupRequest
                {
                    Candidates = recruitCandidates
                }.ToJson(_defaultJsonConverters));
            }
        }
        private bool IsBotFollower(BotOwner bot, AIBossPlayer boss = null)
        {
            if (bot == null || bot.BotFollower == null || !bot.BotFollower.HaveBoss) return false;

            if (!string.IsNullOrEmpty(bot.ProfileId) && _followersByProfileId.TryGetValue(bot.ProfileId, out BotFollowerPlayer cachedFollower))
            {
                if (boss != null)
                {
                    return bot.BotFollower.HaveBoss &&
                           bot.BotFollower.BossToFollow != null &&
                           bot.BotFollower.BossToFollow.Player().ProfileId == boss.Player().ProfileId;
                }

                return cachedFollower != null;
            }

            BotFollowerPlayer _follower = null;

            foreach (var item in _followers)
            {
                if (item != null && item.IsBot(bot))
                {
                    _follower = item;
                    break;
                }
            }

            if (_follower != null && boss != null && bot != null && bot.BotFollower.HaveBoss)
            {
                return bot.BotFollower.BossToFollow.Player().ProfileId == boss.Player().ProfileId;
            }

            return _follower != null;
        }

        private void RemoveBotFollower(BotOwner bot, pitAIBossPlayer player)
        {

            BotFollowerPlayer _follower = null;

            _followers.ForEach(follower =>
            {
                if (follower.IsBot(bot))
                {
                    _follower = follower;
                }
            });

            if (_follower != null)
            {
                _followers.Remove(_follower);
                if (bot?.ProfileId != null)
                {
                    _followersByProfileId.Remove(bot.ProfileId);
                }
                if (player.bossGroup != null)
                {
                    player.bossGroup.RemoveAlly(bot);

                }

                player.RemoveFollower(bot);
                bot.BotFollower.BossToFollow = null;
            }
        }

        public BotFollowerPlayer GetFollower(BotOwner bot)
        {
            if (bot == null) return null;

            if (!string.IsNullOrEmpty(bot.ProfileId) && _followersByProfileId.TryGetValue(bot.ProfileId, out BotFollowerPlayer cachedFollower))
            {
                return cachedFollower;
            }

            BotFollowerPlayer _follower = null;

            foreach (var item in _followers)
            {
                if (item != null && item.IsBot(bot))
                {
                    _follower = item;
                    break;
                }
            }
            return _follower;

        }

        private bool IsBoss(string id)
        {
            if (_bosses == null) return false;

            return _bosses.ContainsKey(id);
        }

        public pitAIBossPlayer GetBossPlayer(string name)
        {
            if (_bosses == null) return null;

            if (!_bosses.ContainsKey(name))
            {
                return null;
            }
            return _bosses[name];
        }

        public Dictionary<string, pitAIBossPlayer> GetBossPlayers()
        {
            return _bosses;
        }

        private List<BotFollowerPlayer> GetBossFollowers(string name)
        {
            List<BotFollowerPlayer> botFollowers = new List<BotFollowerPlayer>();

            if (_bosses == null) return botFollowers;

            if (!_bosses.ContainsKey(name) || _bosses[name] == null)
            {
                return botFollowers;
            }

            pitAIBossPlayer player = _bosses[name];

            foreach (var item in _followers)
            {
                if (item.GetBoss() == player)
                {
                    botFollowers.Add(item);
                }
            }

            return botFollowers;
        }

        public static pitAIBossPlayer? GetBoss(string name)
        {
            if (Instance == null) return null;
            return Instance.GetBossPlayer(name);
        }

        public static Dictionary<string, pitAIBossPlayer> GetBosses()
        {
            if (Instance == null) return new Dictionary<string, pitAIBossPlayer>();
            return Instance.GetBossPlayers();
        }

        public static bool IsPlayerBoss(string profileId)
        {
            if (Instance == null) return false;

            return Instance.IsBoss(profileId);
        }

        public static List<BotFollowerPlayer> GetFollowersByBoss(string bossName)
        {
            if (Instance == null) return new List<BotFollowerPlayer>();
            return Instance.GetBossFollowers(bossName);
        }

        public static List<BotFollowerPlayer> GetFollowers()
        {
            if (Instance == null) return new List<BotFollowerPlayer>();
            return Instance._followers;
        }

        public static bool IsFollowerProfileId(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId)) return false;
            return Instance._followersByProfileId.ContainsKey(profileId);
        }

        public static BotFollowerPlayer GetFollowerByProfileId(string profileId)
        {
            if (Instance == null || string.IsNullOrEmpty(profileId)) return null;
            Instance._followersByProfileId.TryGetValue(profileId, out BotFollowerPlayer follower);
            return follower;
        }

        public static bool IsFollower(BotOwner bot, AIBossPlayer boss = null)
        {
            if (Instance == null || bot == null) return false;
            return Instance.IsBotFollower(bot, boss) || Instance._shallBeFollower.Contains(bot.ProfileId);
        }

        public static void AddGroupToBoss(pitAIBossPlayer player, BotsGroup group)
        {
            player.bossGroup = group;
            player.bossGroup.Lock();
            // prevent group from ever making the player an enemy
            player.bossGroup.OnEnemyAdd += (IPlayer pl, EBotEnemyCause cause) =>
            {
                if (pl != null)
                {
                    if (player.Player().ProfileId == pl.ProfileId)
                    {
                        player.bossGroup.RemoveEnemy(player.Player());
                        player.bossGroup.AddAlly(player.realPlayer);
                    }
                    else if (pl.IsAI && player.bossGroup.Contains(pl.AIData.BotOwner))
                    {
                        player.bossGroup.RemoveEnemy(pl);
                        player.bossGroup.AddAlly(pl.AIData.Player);
                    }
                }
                Enemy.ForceIgnoreUntilAggressionOff(player.bossGroup);
            };
            if (!Instance._botsGroup.Contains(group.Id)) Instance._botsGroup.Add(group.Id);

            player.bossGroup.AddAlly(player.realPlayer);

            foreach (var enemy in player.GetEnemies())
            {
                player.bossGroup.AddEnemy(enemy, EBotEnemyCause.addPlayerToBoss);
            }

            Enemy.ForceIgnoreUntilAggressionOff(player.bossGroup);
        }

        public static bool IsBossGroup(int id)
        {
            if (Instance == null) return false;
            return Instance._botsGroup.Contains(id);
        }

        public static pitAIBossPlayer GetBossByGroup(int id)
        {
            if (Instance == null) return null;
            if (Instance._bosses.Count == 0) return null;
            if (Instance._botsGroup.Contains(id))
            {
                foreach (var item in Instance._bosses)
                {
                    if (item.Value.bossGroup != null && item.Value.bossGroup.Id == id)
                    {
                        return item.Value;
                    }
                }
            }

            return null;
        }

        public static void RemoveFollower(BotOwner bot, pitAIBossPlayer player)
        {
            if (Instance == null) return;
            Instance.RemoveBotFollower(bot, player);
        }

        public static pitAIBossPlayer AddPlayerAsBoss(Player player, BotsController botsController)
        {
            return Instance.AddBossPlayer(player, botsController);
        }

        public static void KillPlayerBoss(string profileId)
        {
            Instance.RemoveBossPlayer(profileId, true);
        }

        public static BotFollowerPlayer AddFollower(BotOwner bot, pitAIBossPlayer player, bool squadMate = false, WildSpawnType role = WildSpawnType.assault, string tactic = "Default", float aggression = 50f)
        {
            return Instance.AddBotFollower(bot, player, squadMate, role, tactic, aggression);
        }

        public static void ShallBeFollower(string profileId)
        {
            if (!Instance._shallBeFollower.Contains(profileId)) Instance._shallBeFollower.Add(profileId);
        }
    }
}
