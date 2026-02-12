using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;

using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

using friendlySAIN.Modules;
using DrakiaXYZ.BigBrain.Brains;
using friendlySAIN.Patches;

namespace friendlySAIN.Components
{
    public class BotFollowerPlayer
    {
        protected BotOwner _bot;
        protected pitAIBossPlayer _player;

        protected BotLastBlindEffectModifierClass settingModif;

        protected bool _IsSquadMate = false;

        protected string _grouId = "";
        protected string _teamId = "";

        protected List<AICoreLayerClass<BotLogicDecision>> vanillaLayers;

        public bool IsSquadMate
        {
            get
            {
                return _IsSquadMate;
            }
        }


        protected WildSpawnType _botRole;
        protected bool _canPatrol = false;
        private bool _peaceChangeHooked = false;

        public bool CanPatrol
        {
            get
            {
                return _canPatrol;
            }
        }

        public BotFollowerPlayer(BotOwner bot, pitAIBossPlayer player, bool isSquad = false, WildSpawnType botRole = WildSpawnType.assault)
        {
            _bot = bot;
            _player = player;
            _botRole = botRole == WildSpawnType.assault ? _bot.Profile.Info.Settings.Role : botRole;

            _IsSquadMate = isSquad;

            settingModif = new BotLastBlindEffectModifierClass(1f, 1.4f, 1f, 0.9f, 1f, 1f, 1f, 1f, 1f);

            if (player.realPlayer.Side != EPlayerSide.Savage) NpcMessage.AddNpc(bot, isSquad);

        }

        public virtual void Init()
        {
            _canPatrol = false;
            BaseBrain baseBrain = _bot.Brain.BaseBrain;
            if(baseBrain == null)
            {
                Modules.Logger.LogError("BaseBrain is null for " + _bot.Profile.Nickname);
                return;
            }

            // force current layer to trigger end decision
            try
            {
                ResetBrainForFollower(baseBrain);
            }
            catch(Exception ex)
            {
                Modules.Logger.LogError("Error while trying to deactivate vanilla layers");
                Modules.Logger.LogError(ex);
            }

           
            if (baseBrain != null)
            {
                // deactive current layer to prevent conflicts with our layers, if any
                if (baseBrain.CurLayerInfo != null && baseBrain.CurLayerInfo.IsActive)
                {
                    string name = baseBrain.CurLayerInfo.Name();
                    _bot.Brain.Agent.Deactivate(name);
                    baseBrain.CurLayerInfo.IsActive = false;
                }
            }

            // remove layers that might conflict with our logic (like vanilla follow or loot patrol layers)
            RemoveConflictingLayers(_bot.Brain.BaseBrain, _bot.Brain.Agent);

            ForceEndCurrentDecision(_bot);
            HookPeaceChange();
            //LogBrainState("after init");
            


            // bot might be following someone, reset that
            if (_bot.BotFollower.HaveBoss)
            {
                _bot.BotFollower.BossToFollow.RemoveFollower(_bot);
                _bot.BotFollower.BossToFollow = null;
            }
            // bot might have request going on, dispose it
            if (_bot.BotRequestController.CurRequest != null)
            {
                _bot.BotRequestController.CurRequest.Complete();
                _bot.BotRequestController.CurRequest = null;
            }
            // bot might have an enemy in his mind, clear it
            if (_bot.Memory.HaveEnemy)
            {
                _bot.Memory.DeleteInfoAboutEnemy(_bot.Memory.GoalEnemy.Person);
            }

            // remove looting brain, if present
            /*if (Chainloader.PluginInfos.ContainsKey("me.skwizzy.lootingbots"))
            {
                Type lootingBrain = Type.GetType("LootingBots.Patch.Components.LootingBrain, skwizzy.LootingBots");

                if (lootingBrain != null)
                {
                    if (_bot.GetPlayer.gameObject.TryGetComponent(lootingBrain, out Component component))
                    {
                        Modules.Logger.LogInfo("Looting brain found, removing it");
                        UnityEngine.Object.Destroy(component);
                    }
                }
                else
                {
                    Modules.Logger.LogInfo("Looting brain not found");
                }
            }*/

            // followers should not be questing, so stop it (if questing mod is present)
           /* Type questingBrain = Type.GetType("SPTQuestingBots.Components.BotObjectiveManager, SPTQuestingBots");
            if (questingBrain != null && _bot.GetPlayer.gameObject.TryGetComponent(questingBrain, out var questingComponent))
            {
                try
                {
                    MethodInfo stopMethod = questingBrain.GetMethod("StopQuesting", BindingFlags.Public | BindingFlags.Instance);
                    stopMethod?.Invoke(questingComponent, null);
                    Modules.Logger.LogInfo("Questing stopped for " + _bot.Profile.Nickname);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("Failed to stop questing");
                    Modules.Logger.LogError(ex);
                }
            }*/

            /// reset bot animation stances
            _bot.GetPlayer.MovementContext.SetPatrol(false);
            _bot.Tilt.Stop();

            // add special follower settings
            SetFollowerSettings(_bot);
            // let the bot talk
            _bot.BotTalk.SetSilence(0f);
            // force bot to turn off light
            if (_bot.BotLight != null && _bot.BotLight.IsEnable) _bot.BotLight.TurnOff(false, true);
            // make bot follower of player
            _player.AddFollower(_bot);
            

            bool isPickedUp = !_IsSquadMate && (_player.bossGroup == null || _player.bossGroup.Id != _bot.BotsGroup.Id);

            // make bot join the player's group
            if (_player.bossGroup != null)
            {
                // clear the player's followers from being enemies to the bot
                _player.Followers.ForEach(bt =>
                {
                    if (_bot.EnemiesController.EnemyInfos.TryGetValue(bt, out var fl))
                    {
                        _bot.EnemiesController.EnemyInfos.Remove(bt);
                    }
                });
                // clear the player from being an enemy to the bot
                if (_bot.EnemiesController.EnemyInfos.TryGetValue(_player.realPlayer, out var info))
                {
                    _bot.EnemiesController.EnemyInfos.Remove(_player.realPlayer);
                }
                // add the bot to the player's group, if not already (PickUp case here with spawn)
                if (_bot.BotsGroup.Id != _player.bossGroup.Id)
                {
                    // - bot is some kind of boss of a group, we have to change that
                    if (_bot.Boss.HaveFollowers() && (_bot.BotsGroup.BossGroup != null) && _bot.Boss.Followers.Count >= 1)
                    {
                        foreach (BotOwner follower in _bot.Boss.Followers)
                        {
                            follower.BotFollower.BossToFollow = null;
                        }
                    }

                    _bot.BotsGroup.RemoveAlly(_bot);
                    _bot.BotsGroup = _player.bossGroup;

                    // - ensure the bot is not marked as enemy already by the others
                    _player.bossGroup.RemoveEnemy(_bot.GetPlayer);

                    var botEnemies = _bot.EnemiesController.EnemyInfos.ToList();
                    foreach (var item in botEnemies)
                    {
                        _bot.Memory.DeleteInfoAboutEnemy(item.Key);
                    }

                    isPickedUp = true;

                    _player.bossGroup.AddMember(_bot, false);
                    foreach (var en in _player.bossGroup.Enemies)
                    {
                        _bot.Memory.AddEnemy(en.Key, en.Value, false);
                    }
                }
            }
            // if there is no group yet, make one and group the player with the bot (PickUp case here without spawn)
            else
            {
                isPickedUp = true;

                _bot.BotsGroup.RemoveAlly(_bot);

                _bot.Settings.GetEnemyBotTypes().RemoveAll(x => Utils.Props.friendlyBotTypes.Contains(x));
                _bot.Settings.GetFriendlyBotTypes().AddRange(Utils.Props.friendlyBotTypes);

                BotZone zone = _bot.BotsController.BotSpawner.GetClosestZone(_bot.GetPlayer.Transform.position, out var zoneDist);

                List<BotOwner> activeEnemies = new List<BotOwner>();
                foreach (BotOwner item2 in _bot.BotsController.BotSpawner.method_5(_bot))
                {
                    if (!Utils.Props.friendlyBotTypes.Contains(item2.Profile.Info.Settings.Role))
                        activeEnemies.Add(item2);
                }

                BotsGroup group = new BotsGroupPlayer(zone, _bot.BotsController.BotGame, _bot, activeEnemies, _bot.BotsController.BotSpawner.DeadBodiesController, _bot.BotsController.BotSpawner.AllPlayers, _player);

                _bot.BotsGroup = group;

                // - go through the enemy filtering process
                var groupEnemies = _bot.BotsGroup.Enemies;
                var botEnemies = _bot.EnemiesController.EnemyInfos.ToList();
                foreach (var item in botEnemies)
                {
                    _bot.Memory.DeleteInfoAboutEnemy(item.Key);
                }

                group.AddMember(_bot, false);
                BossPlayers.AddGroupToBoss(_player, group);

                _bot.Memory.GoalEnemy = null;
            }

            if (isPickedUp)
            {
                // ensure bot sees the player's group as his group
                var _groupRequestController = _bot.BotRequestController.GroupRequestController_1;
                if (_groupRequestController != null)
                {
                    _groupRequestController.OnAddRequest -= _bot.BotRequestController.method_0;
                }

                _bot.Memory.BotsGroup_0 = _bot.BotsGroup;
                _bot.BotRequestController.GroupRequestController_1 = null;
            }

            var _bots = _bot.BotsController.BotSpawner.Bots;
            var _rougeTypes = Utils.Props.BossFollowersType.ToList();
            _rougeTypes.Add(WildSpawnType.exUsec);

            bool friendsWithRogue = !Utils.Props.BossFollowersType.Contains(_botRole) && Utils.Utils.PlayerHasKnightQuest(_player.realPlayer.Profile);

            if (friendsWithRogue)
            {

                _bot.Settings.GetEnemyBotTypes().RemoveAll(x => _rougeTypes.Contains(x));
                _bot.Settings.GetFriendlyBotTypes().AddRange(_rougeTypes);
            }

            foreach (var item in _bots.BotOwners)
            {
                if (BossPlayers.IsFollower(item) || item.IsDead) continue;

                var itemRole = item.Profile.Info.Settings.Role;
                // ensure the new member sees zombies as enemies
                if (Utils.Props.ZombieTypes.Contains(itemRole))
                {
                    _bot.BotsGroup.AddEnemy(item, EBotEnemyCause.addPlayerToBoss);
                }
                // ensure Rogues see the new member as ally and vice versa
                else if (friendsWithRogue)
                {
                    if (!_rougeTypes.Contains(itemRole)) continue;

                    _bot.BotsGroup.RemoveEnemy(item);
                    _bot.BotsGroup.AddNeutral(item);
                    _bot.BotsGroup.AddAlly(item.GetPlayer);

                    _bot.Memory.DeleteInfoAboutEnemy(item);

                    item.BotsGroup.RemoveEnemy(_bot);
                    item.BotsGroup.AddNeutral(_bot);
                    item.BotsGroup.AddAlly(_bot.GetPlayer);

                    item.Memory.DeleteInfoAboutEnemy(_bot);
                }
            }

            // apply the settings modifier
            _bot.Settings.Current.Apply(settingModif);

            Utils.Utils.SetTimeout(() =>
            {
                // TURN OFF THE FLASHLIGHT!
                if (_bot.BotLight != null && _bot.BotLight.IsEnable)
                {
                    _bot.BotLight.TurnOff(false, false);
                }
            }, 300);

            // ensure bot has enough ammo
            AddExtraAmmo();

            // - ensure weapon is in auto mode
            if (_bot.WeaponManager.ShootController.Item != null && _bot.WeaponManager.ShootController.Item.WeapFireType.Contains(Weapon.EFireMode.fullauto))
                _bot.WeaponManager.ShootController.ChangeFireMode(Weapon.EFireMode.fullauto);

            Modules.Logger.LogInfo($"Bot {_bot.Profile.Nickname} is now a follower of {_player.Player().Profile.Nickname}");
        }

        protected virtual void SetFollowerSettings(BotOwner bot)
        {

            bool isGoon = Utils.Props.BossFollowersType.Contains(_botRole);
            // increase bot's power
            BotDifficultySettingsClass settings = Singleton<GClass620>.Instance.GetSettings(BotDifficulty.hard, _botRole, _bot.BotsController.IsPvE);

            // - hardcode some settings to make the bot more efficient
            settings.FileSettings.Move.REACH_DIST = 1.5f;
            settings.FileSettings.Move.REACH_DIST_COVER = 2f;
            settings.FileSettings.Move.REACH_DIST_RUN = 1f;
            settings.FileSettings.Boss.BIG_PIPE_ARTILLERY_COUNT = 1;
            settings.FileSettings.Mind.PART_PERCENT_TO_HEAL = 0.8f;

            settings.FileSettings.Mind.DIST_TO_STOP_RUN_ENEMY = 15f;
            settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = friendlySAIN.enemyRemember.Value;
            settings.FileSettings.Mind.TIME_TO_FIND_ENEMY = 10f;
            settings.FileSettings.Mind.ATTACK_IMMEDIATLY_CHANCE_0_100 = 50f;
            settings.FileSettings.Mind.CHANCE_TO_RUN_CAUSE_DAMAGE_0_100 = 50f;


            settings.FileSettings.Mind.CAN_TALK = true;
            settings.FileSettings.Mind.TALK_WITH_QUERY = true;
            bot.BotTalk.CanSay = true;
            // - fix missing phrases (bug appeared in 0.16)
            if (!bot.BotTalk.Priority.Any(x => x.Key == EPhraseTrigger.Ready))
                bot.BotTalk.Priority.Add(EPhraseTrigger.Ready, 140f);
            if (!bot.BotTalk.Priority.Any(x => x.Key == EPhraseTrigger.Going))
                bot.BotTalk.Priority.Add(EPhraseTrigger.Going, 141f);
            if (!bot.BotTalk.Priority.Any(x => x.Key == EPhraseTrigger.DontKnow))
                bot.BotTalk.Priority.Add(EPhraseTrigger.DontKnow, 142f);

            settings.FileSettings.Mind.CAN_STAND_BY = false;
            settings.FileSettings.Mind.CAN_TAKE_ANY_ITEM = true;
            settings.FileSettings.Mind.CAN_TAKE_ITEMS = true;
            settings.FileSettings.Mind.CAN_THROW_REQUESTS = true;
            settings.FileSettings.Mind.CAN_DROP_ITEMS = false; // prevent bot from dropping items randomly
            settings.FileSettings.Mind.CAN_USE_MEDS = true;
            settings.FileSettings.Mind.MEDS_ONLY_SAFE_CONTAINER = false;
            settings.FileSettings.Mind.SURGE_KIT_ONLY_SAFE_CONTAINER = false;
            settings.FileSettings.Mind.GROUP_ANY_PHRASE_DELAY = 2f;
            settings.FileSettings.Mind.GROUP_EXACTLY_PHRASE_DELAY = 1f;
            settings.FileSettings.Mind.GROUP_EXACTLY_PHRASE_DELAY_MAX = 1f;

            if (_player.realPlayer.Side != EPlayerSide.Savage)
            {
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_PMC_PLAYERS = false;
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_SAVAGE_PLAYERS = true;
            }
            else
            {
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_SAVAGE_PLAYERS = false;
                settings.FileSettings.Mind.ENEMY_BY_GROUPS_PMC_PLAYERS = true;
            }

            settings.FileSettings.Mind.CHANCE_FUCK_YOU_ON_CONTACT_100 = 0;
            settings.FileSettings.Mind.REVENGE_TO_GROUP = true;

            EPlayerSide playerSide = _player.Player().Side;

            // force follower loyality
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_SAVAGE = playerSide == EPlayerSide.Savage;
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_BEAR = playerSide == EPlayerSide.Bear;
            settings.FileSettings.Mind.CAN_RECEIVE_PLAYER_REQUESTS_USEC = playerSide == EPlayerSide.Usec;
            settings.FileSettings.Mind.CAN_EXECUTE_REQUESTS = true;

            settings.FileSettings.Mind.FRIEND_AGR_KILL = 0.000001f;
            settings.FileSettings.Mind.FRIEND_DEAD_AGR_LOW = -0.000001f;
            settings.FileSettings.Mind.REVENGE_FOR_SAVAGE_PLAYERS = false;

            // follower can turn enemy to anyone and cares only for the boss
            settings.GetWarnBotTypes().Clear();
            settings.FileSettings.Mind.WARN_BOT_TYPES = new WildSpawnType[] { };
            settings.FileSettings.Mind.REVENGE_BOT_TYPES = new WildSpawnType[] { };
            List<WildSpawnType> friendList = settings.GetFriendlyBotTypes();
            friendList.AddRange(Utils.Props.friendlyBotTypes);
            settings.FileSettings.Mind.FRIENDLY_BOT_TYPES = Utils.Props.friendlyBotTypes.ToArray();

            // opposing sides are always enemies
            if (playerSide == EPlayerSide.Bear)
                settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

            else if (playerSide == EPlayerSide.Usec)
                settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

            if (playerSide != EPlayerSide.Savage)
            {
                settings.FileSettings.Mind.DEFAULT_SAVAGE_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                // - bad guy is hostile to everyone    
                if (Utils.Utils.FlagGet("isBadGuy"))
                {
                    settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                    settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;

                    List<WildSpawnType> enemyList = settings.GetEnemyBotTypes();

                    if (!enemyList.Contains(WildSpawnType.pmcUSEC))
                        enemyList.Add(WildSpawnType.pmcUSEC);

                    if (!enemyList.Contains(WildSpawnType.pmcBEAR))
                        enemyList.Add(WildSpawnType.pmcBEAR);
                }
            }
            // ensure scav followers are hostile to PMCs
            else
            {
                settings.FileSettings.Mind.DEFAULT_USEC_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
                settings.FileSettings.Mind.DEFAULT_BEAR_BEHAVIOUR = EWarnBehaviour.AlwaysEnemies;
            }

            settings.FileSettings.Mind.BULLET_FEEL_CLOSE_SDIST = 2f * 2f;
            settings.FileSettings.Mind.CHANCE_FUCK_YOU_ON_CONTACT_100 = 0;
            settings.FileSettings.Mind.DIST_TO_ENEMY_SPOTTED_ON_HIT = 20;
            settings.FileSettings.Mind.DOG_FIGHT_IN = 3f;
            settings.FileSettings.Mind.DOG_FIGHT_OUT = 6f;
            settings.FileSettings.Mind.SHOOT_INSTEAD_DOG_FIGHT = 1f;
            settings.FileSettings.Mind.MIN_DAMAGE_SCARE = 20f;

            settings.FileSettings.Patrol.PICKUP_ITEMS_TO_BACKPACK_OR_CONTAINER = true;
            settings.FileSettings.Patrol.CHANCE_TO_PLAY_VOICE_WHEN_CLOSE = 50;
            settings.FileSettings.Patrol.CHANCE_TO_PLAY_GESTURE_WHEN_CLOSE = 100;
            settings.FileSettings.Patrol.CAN_PEACEFUL_LOOK = true;
            settings.FileSettings.Patrol.FRIEND_SEARCH_SEC = 60;
            settings.FileSettings.Patrol.FOLLOWER_START_MOVE_DELAY = 0.5f;
            settings.FileSettings.Patrol.CAN_FRIENDLY_TILT = true;
            settings.FileSettings.Patrol.VISION_DIST_COEF_PEACE = 1f;

            settings.FileSettings.Boss.SHALL_WARN = false;
            settings.FileSettings.Patrol.MAX_YDIST_TO_START_WARN_REQUEST_TO_REQUESTER = 0f;

            settings.FileSettings.Look.MINIMUM_VISIBLE_DIST = 15f;

            settings.FileSettings.Core.CanGrenade = friendlySAIN.botGrenades.Value;
            settings.FileSettings.Core.CanRun = true;

            settings.FileSettings.Cover.CHECK_CLOSEST_FRIEND = true;
            settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE = 1;
            settings.FileSettings.Cover.HIDE_TO_COVER_TIME = 5;
            settings.FileSettings.Cover.HITS_TO_LEAVE_COVER = 2;
            settings.FileSettings.Cover.HITS_TO_LEAVE_COVER_UNKNOWN = 2;
            settings.FileSettings.Cover.TIME_TO_MOVE_TO_COVER = 15;
            settings.FileSettings.Cover.RETURN_TO_ATTACK_AFTER_AMBUSH_MIN = 20;
            settings.FileSettings.Cover.RETURN_TO_ATTACK_AFTER_AMBUSH_MAX = 50;
            settings.FileSettings.Cover.SPOTTED_GRENADE_RADIUS = 24f;
            settings.FileSettings.Cover.SPOTTED_GRENADE_TIME = 7;

            // - faster aiming sett
            settings.FileSettings.Aiming.COEF_IF_MOVE = 1f;
            settings.FileSettings.Aiming.BOTTOM_COEF = 0.05f;
            settings.FileSettings.Aiming.COEF_FROM_COVER = 0.3f;
            settings.FileSettings.Aiming.PANIC_COEF = 1f;
            if (!isGoon)
            {
                settings.FileSettings.Aiming.MAX_AIMING_UPGRADE_BY_TIME = 0.15f;
            }

            // - improved shooting settings
            settings.FileSettings.Aiming.SHPERE_FRIENDY_FIRE_SIZE = 0.5f;
            settings.FileSettings.Aiming.AIMING_TYPE = 6; // the head is a priority
            if (!isGoon)
            {
                settings.FileSettings.Aiming.ANY_PART_SHOOT_TIME = 15f;
                settings.FileSettings.Aiming.ANYTIME_LIGHT_WHEN_AIM_100 = 50;
                settings.FileSettings.Aiming.BAD_SHOOTS_MAX = 2;
                settings.FileSettings.Aiming.BAD_SHOOTS_MIN = 1;
                settings.FileSettings.Aiming.FIRST_CONTACT_ADD_CHANCE_100 = 20f;
            }
            // - hit disturbance settings
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_DELAY_SEC = 0.2f;
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_MAX_ANG = 10f;
            settings.FileSettings.Aiming.BASE_HIT_AFFECTION_MIN_ANG = 2f;
            settings.FileSettings.Aiming.DAMAGE_PANIC_TIME = 10f;
            if (!isGoon)
            {
                settings.FileSettings.Aiming.DAMAGE_TO_DISCARD_AIM_0_100 = 30f;
            }


            settings.FileSettings.Look.CAN_USE_LIGHT = true;
            settings.FileSettings.Look.NIGHT_VISION_ON = 100.0f;
            settings.FileSettings.Look.NIGHT_VISION_OFF = 110.0f;
            settings.FileSettings.Look.NIGHT_VISION_DIST = 160.0f;
            settings.FileSettings.Look.VISIBLE_ANG_NIGHTVISION = 120f;
            settings.FileSettings.Look.LOOK_THROUGH_PERIOD_BY_HIT = 5f;
            settings.FileSettings.Look.LightOnVisionDistance = 40.0f;
            settings.FileSettings.Look.LOOK_LAST_POSENEMY_IF_NO_DANGER_SEC = 25f;
            settings.FileSettings.Look.VISIBLE_ANG_LIGHT = 45.0f;
            settings.FileSettings.Look.VISIBLE_DISNACE_WITH_LIGHT = 65.0f;


            settings.FileSettings.Look.GOAL_TO_FULL_DISSAPEAR = 1.1f;
            settings.FileSettings.Look.GOAL_TO_FULL_DISSAPEAR_GREEN = 2f;
            //settings.FileSettings.Look.LOOK_THROUGH_GRASS = true;
            settings.FileSettings.Look.MAX_VISION_GRASS_METERS = 1.0f;
            settings.FileSettings.Look.NO_GREEN_DIST = 4.0f;
            settings.FileSettings.Look.NO_GRASS_DIST = 5.0f;

            settings.FileSettings.Look.CHECK_HEAD_ANY_DIST = true;
            settings.FileSettings.Look.MIDDLE_DIST_CAN_SHOOT_HEAD = true;

            settings.FileSettings.Hearing.DISPERSION_COEF = 1.6f;
            settings.FileSettings.Hearing.CLOSE_DIST = 7f;
            settings.FileSettings.Hearing.FAR_DIST = 35f;

            settings.FileSettings.Cover.SIT_DOWN_WHEN_HOLDING = true;

            settings.FileSettings.Shoot.WAIT_NEXT_SINGLE_SHOT = 0.1f;
            settings.FileSettings.Shoot.WAIT_NEXT_SINGLE_SHOT_LONG_MAX = 1.8f;
            settings.FileSettings.Shoot.NEXT_SINGLE_SHOT_PAUSE = 3f;

            settings.FileSettings.Boss.EFFECT_REGENERATION_PER_MIN = 40f;

            settings.FileSettings.Grenade.NO_RUN_FROM_AI_GRENADES = false;

            bot.Settings = settings;

            bot.ENEMY_LOOK_AT_ME = Mathf.Cos(settings.FileSettings.Mind.ENEMY_LOOK_AT_ME_ANG * 0.017453292f);
            bot.GetPlayer.ActiveHealthController.SetDamageCoeff(settings.FileSettings.Core.DamageCoeff);

            // counter SAIN
            bot.LookSensor.ShootFromEyes = true;
            bot.Settings.FileSettings.Look.SHOOT_FROM_EYES = true;

            // - friendly bot never gets tired
            bot.GetPlayer.Physical.Stamina.ForceMode = true;
            bot.GetPlayer.Physical.HandsStamina.ForceMode = true;
            // - need no food
            bot.GetPlayer.HealthController.DisableMetabolism();
            // - have followers share the same groupId as the player
            _grouId = bot.GetPlayer.Profile.Info.GroupId;
            _teamId = bot.GetPlayer.Profile.Info.TeamId;
            bot.GetPlayer.Profile.Info.GroupId = _player.realPlayer.GroupId;
            bot.GetPlayer.Profile.Info.TeamId = _player.realPlayer.Profile.Info.TeamId;

            bot.Tactic.AggressionChange(1f);

            // - take on the new vision values
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Core.VisibleAngle * 0.017453292f));
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE_LIGHT").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Look.VISIBLE_ANG_LIGHT * 0.017453292f));
            AccessTools.Field(typeof(LookSensor), "VISIBLE_ANGLE_NIGHTVISION").SetValue(bot.LookSensor, Mathf.Cos(settings.FileSettings.Look.VISIBLE_ANG_NIGHTVISION * 0.017453292f));

            _bot.LookSensor.ManualUpdate();

            // force bosses to not warn the follower (these will be exUsec and the Goons)
            if (_player.realPlayer.Side != EPlayerSide.Savage && Utils.Utils.PlayerHasKnightQuest(_player.realPlayer.Profile))
            {
                var field = typeof(FenceLoyaltyLevel).GetField("HostileBosses", BindingFlags.Public | BindingFlags.Instance);
                if (field != null)
                {
                    FieldInfo attrField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
                    attrField?.SetValue(field, field.Attributes & ~FieldAttributes.InitOnly);

                    field.SetValue(_bot.Profile.FenceInfo.FenceLoyalty, false);
                }
            }
        }

        protected void AddExtraAmmo()
        {
            InventoryController? inventory = GetInventoryController();
            
            if(inventory == null)
            {
                Modules.Logger.LogError("Cannot access inventory of bot, extra ammo will not be added");
                return;
            }

            SearchableItemItemClass? secureContainer;

            try
            {
                secureContainer = (SearchableItemItemClass)inventory.Inventory.Equipment.GetSlot(EquipmentSlot.SecuredContainer).ContainedItem;
            }
            catch
            {
                Modules.Logger.LogError("Cannot access secure container of bot, extra ammo will not be added");
                return;
            }

            if (secureContainer == null)
            {
                Modules.Logger.LogError("Bot has no secure container, cannot add extra ammo");
                return;
            }

            StashGridClass stashGridClass = secureContainer.Grids.FirstOrDefault();
            if (stashGridClass == null) return;

            Weapon? weapon = _bot.AIData?.Player?.HandsController?.Item as Weapon;
            if (weapon == null)
            {
                Modules.Logger.LogError("Bot has no weapon to add ammo");
                return;
            }

            Item ammoToAdd = weapon.GetCurrentMagazine()?.FirstRealAmmo() ??
                Singleton<ItemFactoryClass>.Instance.CreateItem(MongoID.Generate(), weapon.CurrentAmmoTemplate._id, null);

            if (ammoToAdd == null)
            {
                Modules.Logger.LogError("Bot has no ammo template to add");
                return;
            }

            for (int i = 0; i < 10; i++)
            {
                Item ammo = ammoToAdd.CloneItem();
                ammo.StackObjectsCount = ammo.StackMaxSize;
                var location = stashGridClass.FindFreeSpace(ammo);
                if (location == null) break;

                var result = stashGridClass.AddItemWithoutRestrictions(ammo, location);
                if (!result.Succeeded) break;
            }
        }

        private InventoryController? GetInventoryController()
        {
            return _bot?.GetPlayer?.InventoryController;
        }

        public bool IsBot(BotOwner bot)
        {
            return _bot == null ? false : bot.ProfileId == _bot.ProfileId;
        }

        public BotOwner GetBot()
        {
            return _bot;
        }

        public pitAIBossPlayer GetBoss()
        {
            if (_bot == null) return null;
            return _player;
        }

        public virtual void Dismiss(bool warnPlayer = false)
        {
            if (_bot == null) return;
            try
            {
                if (_bot.IsDead || _bot.BotState != EBotState.Active) return;

                // - ensure bot no longer follows the player
                if (_bot.BotFollower.HaveBoss)
                {
                    _bot.BotFollower.BossToFollow.RemoveFollower(_bot);
                    _bot.BotFollower.BossToFollow = null;

                }
                // - bot might have request going on, dispose it
                if (_bot.BotRequestController.CurRequest != null)
                {
                    _bot.BotRequestController.CurRequest.Complete();
                }

                _bot.GetPlayer.Physical.Stamina.ForceMode = false;
                _bot.GetPlayer.Physical.HandsStamina.ForceMode = false;

                // -- bot needs a new group
                BotZone zone = _bot.BotsController.BotSpawner.GetClosestZone(_bot.GetPlayer.Transform.position, out var zoneDist);
                BotsGroup group = _bot.BotsController.BotSpawner.GetGroupAndSetEnemies(_bot, zone);

                _bot.BotsGroup = group;

                if (_bot.BotRequestController.GroupRequestController_1 != null)
                {
                    _bot.BotRequestController.GroupRequestController_1.OnAddRequest -= _bot.BotRequestController.method_0;
                }

                _bot.Memory.BotsGroup_0 = group;
                _bot.BotRequestController.GroupRequestController_1 = null;

                _bot.GetPlayer.Profile.Info.GroupId = _grouId;
                _bot.GetPlayer.Profile.Info.TeamId = _teamId;

                _bot.BotsGroup.AddMember(_bot, false);

                _bot.Memory.IsPeace = !warnPlayer;

                // make player and his followers enemies of the bot
                if (warnPlayer)
                {
                    _player.Followers.ForEach(fl =>
                    {
                        if (_bot.EnemiesController.EnemyInfos.TryGetValue(fl.GetPlayer, out var info))
                        {
                            _bot.Memory.DeleteInfoAboutEnemy(fl.GetPlayer);
                        }
                        _bot.BotsGroup.AddEnemy(fl.GetPlayer, EBotEnemyCause.addPlayer);
                    });

                    if (_bot.EnemiesController.EnemyInfos.TryGetValue(_player.Player(), out var plinfo))
                    {
                        _bot.Memory.DeleteInfoAboutEnemy(_player.Player());
                    }

                    _bot.BotsGroup.CheckAndAddEnemy(_player.Player());
                    _bot.BotsGroup.Enemies.ExecuteForEach((key, value) =>
                    {
                        value.IsHaveSeen = key.ProfileId == _player.Player().ProfileId || BossPlayers.GetFollowers().Find(fl => fl.GetBot().ProfileId == key.ProfileId) != null;
                        _bot.Memory.AddEnemy(key, value, false);

                        if (key.ProfileId == _player.Player().ProfileId && _bot.EnemiesController.EnemyInfos.TryGetValue(_player.Player(), out var eninfo))
                        {
                            _bot.Memory.GoalEnemy = eninfo;
                            Modules.Logger.LogInfo("Make player the enemy");
                        }
                    });
                }

            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("Error on dismiss for a follower: " + ex.Message);
                Modules.Logger.LogInfo(ex.StackTrace);
            }
            finally
            {
                UnhookPeaceChange();
            }
            // @TODO : see what else can be reverted
        }

        public void SetCanPatrol(bool value)
        {
            _canPatrol = value;
        }

        private void ResetBrainForFollower(BaseBrain baseBrain)
        {
            if (baseBrain == null) return;

            // Deactivate current layer if possible
            var currentLayer = baseBrain.CurLayerInfo;
            if (currentLayer != null && currentLayer.IsActive)
            {
                string name = currentLayer.Name();
                _bot.Brain.Agent.Deactivate(name);
                currentLayer.IsActive = false;
            }

            // Remove peaceful/patrol style layers
            RemoveBrainLayer(baseBrain, layer =>
            {
                if (layer == null) return false;
                string name = layer.Name();
                return name.Contains("Utility") || name.Contains("Patrol") || name == "PtrlBirdEye";
            });

            // Force decision to end so new layer can take over
            if (currentLayer is BaseLogicLayerSimpleAbstractClass simpleLayer)
            {
                simpleLayer.CalcActionNextFrame();
            }
            else if (currentLayer is BaseLogicLayerAbstractClass baseLayer)
            {
                baseLayer.Bool_1 = true;
            }
        }

        private void RemoveBrainLayer(BaseBrain baseBrain, Func<AICoreLayerClass<BotLogicDecision>, bool> shouldRemove)
        {
            try
            {
                if (baseBrain == null) return;

                var dictField = AccessTools.Field(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "Dictionary_0");
                var layers = dictField?.GetValue(baseBrain) as Dictionary<int, AICoreLayerClass<BotLogicDecision>>;
                if (layers == null) return;

                var toRemove = new List<int>();
                foreach (var kvp in layers)
                {
                    if (shouldRemove(kvp.Value))
                    {
                        toRemove.Add(kvp.Key);
                    }
                }

                var brainHelpersType = AccessTools.TypeByName("DrakiaXYZ.BigBrain.Internal.BrainHelpers");
                var removeLayerForBot = brainHelpersType != null
                    ? AccessTools.Method(brainHelpersType, "RemoveLayerForBot", new[] { typeof(BotOwner), typeof(string) })
                    : null;

                foreach (var index in toRemove)
                {
                    if (layers.TryGetValue(index, out var layer) && layer != null)
                    {
                        string name = layer.Name();
                        if (removeLayerForBot != null)
                        {
                            removeLayerForBot.Invoke(null, new object[] { _bot, name });
                        }
                        else
                        {
                            _bot.Brain.Agent.Deactivate(name);
                            layer.IsActive = false;
                            baseBrain.List_0.Remove(layer);
                            layers.Remove(index);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Error while removing brain layer");
                Modules.Logger.LogError(ex);
            }
        }
        /** Ensure vanilla patrolling is not conflicting with our layers */
        private void HookPeaceChange()
        {
            if (_peaceChangeHooked) return;
            if (_bot?.Memory == null) return;
            _bot.Memory.OnPeaceChange += OnPeaceChange;
            _peaceChangeHooked = true;
        }

        private void UnhookPeaceChange()
        {
            if (!_peaceChangeHooked) return;
            if (_bot?.Memory == null) return;
            _bot.Memory.OnPeaceChange -= OnPeaceChange;
            _peaceChangeHooked = false;
        }

        private void OnPeaceChange(bool isPeace)
        {
            _bot?.PatrollingData?.Pause();
        }

        private void ForceEndCurrentDecision(BotOwner bot)
        {
            if(bot == null) return;
            
            var brain = bot?.Brain?.BaseBrain;
            var agent = bot?.Brain?.Agent;
            if (brain == null || agent == null) return;

            string layer = brain.CurLayerInfo?.Name() ?? "<null>";
            string node = agent.GetActiveNodeName() ?? "<null>";

            EnsureFollowerLayerPresent(brain);

            if (bot.BotRequestController?.CurRequest != null)
            {
                bot.BotRequestController.CurRequest.Complete();
                bot.BotRequestController.CurRequest = null;
            }

            bot.PatrollingData?.LootData?.StopLootCluster();
            bot.PatrollingData?.LootData?.SetTargetLootCluster(null);
            bot.PatrollingData?.Pause();

            if (brain.CurLayerInfo is BaseLogicLayerSimpleAbstractClass simpleLayer)
            {
                simpleLayer.CalcActionNextFrame(BotLogicDecision.holdPosition);
            }
            else if (brain.CurLayerInfo is BaseLogicLayerAbstractClass baseLayer)
            {
                baseLayer.Bool_1 = true;
            }

            bot.StopMove();
            bot.GoToSomePointData?.SetPoint(bot.Position);

            ClearActiveLayerPointers(brain, agent);
            brain.CalcActionNextFrame();
        }

        private void RemoveConflictingLayers(BaseBrain brain, AICoreAgentClass<BotLogicDecision> agent)
        {
            if (brain == null || agent == null) return;

            string[] blockedExact =
            {
                "Exfiltration",
                "SAIN : Extract",
                "SAIN : Squad Layer",
                "Follow Player"
            };

            var toRemove = new List<int>();
            foreach (var kvp in brain.Dictionary_0)
            {
                var layer = kvp.Value;
                if (layer == null) continue;

                string name = layer.Name();
                if (string.IsNullOrEmpty(name)) continue;

                bool exactMatch = blockedExact.Contains(name);
                bool containsMatch =
                    name.IndexOf("Exfil", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Extract", StringComparison.OrdinalIgnoreCase) >= 0;

                if (exactMatch || containsMatch)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (int index in toRemove)
            {
                if (!brain.Dictionary_0.TryGetValue(index, out var layer) || layer == null) continue;

                agent.Deactivate(layer.Name());
                brain.method_3(index);
                layer.IsActive = false;

                brain.List_0.Remove(layer);
                //brain.Dictionary_0.Remove(index);
            }
        }

        private void EnsureFollowerLayerPresent(BaseBrain brain)
        {
            if (brain == null) return;

            foreach (var kvp in brain.Dictionary_0)
            {
                if (kvp.Value != null && kvp.Value.Name() == "friendlySAIN.FollowerPatrol")
                {
                    if (!brain.method_2(kvp.Value))
                    {
                        brain.method_1(kvp.Key);
                    }
                    return;
                }
            }

            var customEntry = BrainManager.CustomLayersReadOnly
                .FirstOrDefault(x => x.Value.customLayerType.FullName == "friendlySAIN.BigBrain.FollowerPatrolLayer");

            if (customEntry.Value == null) return;

            var wrapperType = AccessTools.TypeByName("DrakiaXYZ.BigBrain.Internal.CustomLayerWrapper");
            if (wrapperType == null) return;

            object wrapper = Activator.CreateInstance(
                wrapperType,
                new object[] { customEntry.Value.customLayerType, _bot, customEntry.Value.customLayerPriority }
            );
            if (wrapper == null) return;

            AccessTools
                .Method(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "method_0")
                ?.Invoke(brain, new object[] { customEntry.Key, wrapper, true });
        }

        private void ClearActiveLayerPointers(BaseBrain brain, AICoreAgentClass<BotLogicDecision> agent)
        {
            try
            {
                var activeLayerField = AccessTools.Field(typeof(AICoreStrategyAbstractClass<BotLogicDecision>), "Gclass35_0");
                activeLayerField?.SetValue(brain, null);

                var agentActiveLayerField = AccessTools.Field(typeof(AICoreAgentClass<BotLogicDecision>), "Gclass35_0");
                agentActiveLayerField?.SetValue(agent, null);

                agent.UsingLayer = string.Empty;

                var lastResultField = AccessTools.Field(typeof(AICoreAgentClass<BotLogicDecision>), "Gstruct8_0");
                if (lastResultField != null)
                {
                    var defaultResult = default(AICoreActionResultStruct<BotLogicDecision, GClass26>);
                    lastResultField.SetValue(agent, defaultResult);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to clear active layer pointers");
                Modules.Logger.LogError(ex);
            }
        }

        private void LogBrainState(string tag)
        {
            try
            {
                var brain = _bot?.Brain?.BaseBrain;
                var agent = _bot?.Brain?.Agent;
                if (brain == null || agent == null) return;

                string shortName = brain.ShortName();
                string curLayer = brain.CurLayerInfo?.Name() ?? "<null>";
                string agentLayer = agent.UsingLayer ?? "<null>";
                string agentNode = agent.GetActiveNodeName() ?? "<null>";

                List<string> listNames = new List<string>();
                foreach (var layer in brain.List_0)
                {
                    if (layer == null) continue;
                    listNames.Add($"{layer.Name()}:{layer.Priority}");
                }

                List<string> dictNames = new List<string>();
                foreach (var kvp in brain.Dictionary_0)
                {
                    if (kvp.Value == null) continue;
                    dictNames.Add($"{kvp.Key}:{kvp.Value.Name()}:{kvp.Value.Priority}");
                }

                Modules.Logger.LogInfo($"Follower brain dump [{tag}] bot={_bot.Profile.Nickname} brain={shortName} curLayer={curLayer} agentLayer={agentLayer} agentNode={agentNode}");
                Modules.Logger.LogInfo($"Follower brain dump [{tag}] list={string.Join(", ", listNames)}");
                Modules.Logger.LogInfo($"Follower brain dump [{tag}] dict={string.Join(", ", dictNames)}");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to log brain state");
                Modules.Logger.LogError(ex);
            }
        }

    }
}
