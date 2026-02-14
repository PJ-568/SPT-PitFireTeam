using Comfort.Common;
using EFT;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;


using EventInfo = BotEventHandler.GClass692;
using GestusInfo = GClass532;

namespace friendlySAIN.Components
{
    public class pitAIBossPlayer : AIBossPlayer
    {
        private AIBossPlayerLogic aBossLogic;

        private BotsGroup _group = null;

        private BotsController _botsController;

        public BotsGroup bossGroup
        {
            get { return _group; }
            set
            {
                if (_group != null)
                {
                    _group.OnReportEnemy -= OnReportEnemy;
                }
                _group = value;
                _group.OnReportEnemy += OnReportEnemy;
            }
        }

        public readonly Player realPlayer;

        private List<BotOwner> bossEnemies = new List<BotOwner>();
        private const float TeamStatusGestureDistance = 15f;
        private const float ContactLookDistance = 45f;

        public pitAIBossPlayer(Player player, BotsController botsController) : base(player)
        {
            realPlayer = player;

            aBossLogic = new AIBossPlayerLogic(player, this);
            _botsController = botsController;

            player.HealthController.DiedEvent += OnDead;

            Singleton<BotEventHandler>.Instance.OnPhraseSay += PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnGestusShow += GestusShown;

            //_botsController.Bots.OnBotAdd += OnBotAdd;

        }

        // disabled
        public new void Dispose()
        {
            return;
        }
        // disabled, no auto add
        public new void OfferBot(BotOwner bot)
        {
            return;
        }

        /**
         * This it to help fix 0.16 bug where followers are not attacking same side players despite badguy flag being set
         * to be removed
         * @deprecated
         */
        public void OnBotAdd(BotOwner bot)
        {
            if (BossPlayers.IsFollower(bot)) return;

            WildSpawnType role = bot.Profile.Info.Settings.Role;

            if (realPlayer.Side == EPlayerSide.Savage)
            {
                if (Utils.Props.ZombieTypes.Contains(role) && bossGroup != null)
                {
                    AddEnemyToGroup(bot);
                }
                return;
            }

            List<WildSpawnType> _rougeTypes = Utils.Props.BossFollowersType.ToList();
            _rougeTypes.Add(WildSpawnType.exUsec);

            if (Utils.Utils.FlagGet("isBadGuy") && (!_rougeTypes.Contains(role) || !Utils.Utils.PlayerHasKnightQuest(realPlayer.Profile)))
            {
                if (bossGroup != null)
                {
                    AddEnemyToGroup(bot);
                }
            }
        }
        /**
         * to be remove if we remove OnBotAdd
         */
        public void AddEnemyToGroup(BotOwner bot)
        {
            if (!bossGroup.AddEnemy(bot, EBotEnemyCause.addPlayerToBoss))
            {
                bool isInEnemyList = false;
                bool isInNeutralList = false;

                if (bossGroup.Enemies.TryGetValue(bot, out var enemy))
                {
                    isInEnemyList = true;
                }

                if (bossGroup.Neutrals.TryGetValue(bot, out var neutral))
                {
                    isInNeutralList = true;
                }

                if (isInEnemyList) return;

                var botSettingsClass = new BotSettingsClass(bot.GetPlayer, bossGroup, EBotEnemyCause.addPlayerToBoss);

                bossGroup.Enemies.Add(bot, botSettingsClass);
                if (isInNeutralList)
                {
                    bossGroup.Neutrals.Remove(bot);
                }

                for (int i = 0; i < bossGroup.MembersCount; i++)
                {
                    var member = bossGroup.Member(i);

                    member.Memory.AddEnemy(bot, botSettingsClass, false);
                }
            }
        }

        private void OnDead(EDamageType _damageType)
        {
            InteractableObjects.BossIsDead();
            NpcMessage.PlayerDied();

            BossPlayers.KillPlayerBoss(realPlayer.ProfileId);
        }

        private void OnReportEnemy(IPlayer enemy, Vector3 enemypos, Vector3 weaponrootlast, EEnemyPartVisibleType isvisibleonlybysense, BotOwner reporter)
        {
            if (enemy.ProfileId == realPlayer.ProfileId)
            {
                return;
            }
            _group.CheckAndAddEnemy(enemy);
        }

        public void PhraseSaid(EventInfo info)
        {
            if (info.PlayerRequester != null && info.PlayerRequester.ProfileId == realPlayer.ProfileId)
            {
                if (info.phrase == (EPhraseTrigger)CustomPhrases.TeamStatus)
                {
                    PingTeamates.Instance.Ping(this);
                    SignalTeamStatusFollowers();
                }
                else if (info.phrase == (EPhraseTrigger)CustomPhrases.OverThere)
                {
                    ProcessContactCommand(info.PlayerRequester, true);
                }
                else if (info.phrase == EPhraseTrigger.OnRepeatedContact)
                {
                    ProcessContactCommand(info.PlayerRequester);
                }
                else if (info.phrase == EPhraseTrigger.Look)
                {
                    HandleAttentionCommand();
                }
            }

            foreach (var item in Followers)
            {
                item?.Receiver?.method_0(info);
            }
        }
        public void GestusShown(GestusInfo info)
        {
            if (info == null) return;

            if (info.Player != null && info.Player.ProfileId == realPlayer.ProfileId)
            {
                if (info.Gesture == (EInteraction)CustomGestures.OverThere)
                {
                    ProcessContactCommand(info.Player, true);

                    EventInfo overThereInfo = new EventInfo
                    {
                        phrase = EPhraseTrigger.OnRepeatedContact,
                        PlayerRequester = info.Player
                    };

                    foreach (var item in Followers)
                    {
                        if (!CanReactToBossGesture(item, info.Player)) continue;
                        item?.Receiver?.method_0(overThereInfo);
                    }
                    return;
                }
            }

            foreach (var item in Followers)
            {
                item?.Receiver?.method_6(info);
            }
        }

        private void SignalTeamStatusFollowers()
        {
            Vector3 bossPos = realPlayer.Transform.position;

            foreach (var follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (follower.Memory.HaveEnemy) continue;

                float distSqr = (follower.Position - bossPos).sqrMagnitude;
                if (distSqr > TeamStatusGestureDistance * TeamStatusGestureDistance) continue;

                follower.Gesture.TryGestus(EInteraction.FriendlyGesture, false);
            }
        }

        private void ProcessContactCommand(IPlayer requester, bool requireGestureVisibility = false)
        {
            if (requester == null) return;

            InteractableObjects.CheckSeenEnemies(Player());
            List<Player> seenEnemies = InteractableObjects.GetSeenEnemies();
            Vector3 lookTarget = requester.Transform.position + requester.LookDirection.normalized * ContactLookDistance;

            foreach (var follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (requireGestureVisibility && !CanReactToBossGesture(follower, requester)) continue;

                // Make followers orient toward boss reported direction.
                follower.Steering.LookToPoint(lookTarget);

                if (seenEnemies == null || seenEnemies.Count == 0) continue;

                foreach (Player enemy in seenEnemies)
                {
                    if (enemy == null || enemy.ProfileId == follower.ProfileId || enemy.ProfileId == realPlayer.ProfileId) continue;

                    BotSettingsClass botSettings = new BotSettingsClass(enemy, follower.BotsGroup, EBotEnemyCause.addPlayerToBoss)
                    {
                        EnemyLastPosition = enemy.Position
                    };

                    follower.Memory.AddEnemy(enemy, botSettings, false);
                }
            }
        }

        private bool CanReactToBossGesture(BotOwner follower, IPlayer requester)
        {
            if (follower == null || requester == null) return false;
            if (follower.IsDead || follower.BotState != EBotState.Active) return false;

            float distSqr = (follower.Position - requester.Position).sqrMagnitude;
            if (distSqr > TeamStatusGestureDistance * TeamStatusGestureDistance) return false;

            if (realPlayer?.MainParts == null || !realPlayer.MainParts.ContainsKey(BodyPartType.head)) return false;

            Vector3 bossHead = realPlayer.MainParts[BodyPartType.head].Position;
            Vector3 followerFirePos = follower.WeaponRoot.position;

            return Utils.Utils.CanShootToTarget(
                new ShootPointClass(bossHead, 1),
                followerFirePos,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
        }

        private void HandleAttentionCommand()
        {
            foreach (var follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (!BossPlayers.IsFollower(follower)) continue;

                // Old FollowerReceiver "Look"/Attention behavior.
                InteractableObjects.RemoveTaker(follower);
                InteractableObjects.RemoveOpener(follower);

                ClearEnemyStateForAttention(follower);

                FollowerRecovery.SoftReset(follower);

                follower.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
            }
        }

        private static void ClearEnemyStateForAttention(BotOwner follower)
        {
            if (follower == null || follower.Memory == null)
            {
                return;
            }

            // Clear current goal enemy flags first.
            if (follower.Memory.GoalEnemy != null && follower.Memory.GoalEnemy.GroupInfo != null)
            {
                follower.Memory.GoalEnemy.GroupInfo.EnemyLastSeenTimeSense = 0f;
                follower.Memory.GoalEnemy.GroupInfo.IsHaveSeen = false;
            }

            // Remove all known enemies from bot memory + group cache so they are not immediately reacquired as "visible".
            if (follower.EnemiesController?.EnemyInfos != null)
            {
                List<IPlayer> knownEnemies = follower.EnemiesController.EnemyInfos.Keys.ToList();
                foreach (IPlayer enemy in knownEnemies)
                {
                    if (enemy == null) continue;
                    follower.Memory.DeleteInfoAboutEnemy(enemy);
                    follower.BotsGroup?.RemoveEnemy(enemy);
                }
            }

            follower.Memory.GoalEnemy = null;
            follower.Memory.LastEnemy = null;
        }

        public new AIBossPlayerLogic GetBossLogic()
        {
            return aBossLogic;
        }

        public bool AddEnemy(BotOwner bot)
        {
            if (!bossEnemies.Contains(bot) && !bot.IsDead && bot.BotState == EBotState.Active)
            {
                bossEnemies.Add(bot);

                if (bot.HealthController != null) bot.HealthController.DiedEvent += (EDamageType type) =>
                {
                    RemoveEnemy(bot);
                };
                if (bot.LeaveData != null) bot.LeaveData.OnLeave += (BotOwner _bot) =>
                {
                    RemoveEnemy(_bot);
                };

                return true;
            }

            return false;
        }
        public void RemoveEnemy(BotOwner bot)
        {
            if (bossEnemies.Contains(bot))
            {
                bossEnemies.Remove(bot);
            }
        }

        public List<BotOwner> GetEnemies()
        {
            return bossEnemies;
        }

        public void PrioritizeEnemy(BotOwner follower, BotOwner enemy)
        {

            // make the closest enemy of boss, the enemy
            if (enemy != null)
            {

                EnemyInfo info = null;

                foreach (var item in follower.EnemiesController.EnemyInfos)
                {
                    if (item.Key.ProfileId == enemy.ProfileId)
                    {
                        info = item.Value;
                        break;
                    }
                }

                if (info != null)
                {
                    info.PriorityIndex = 0;
                    if (!follower.Memory.HaveEnemy) follower.Memory.GoalEnemy = info;
                }
                else
                {
                    BotSettingsClass botSettingsClass = new BotSettingsClass(Singleton<GameWorld>.Instance.GetAlivePlayerByProfileID(enemy.ProfileId), bossGroup, EBotEnemyCause.addPlayerToBoss);
                    botSettingsClass.EnemyLastPosition = enemy.Position;
                    follower.Memory.AddEnemy(enemy, botSettingsClass, false);

                    if (!follower.Memory.HaveEnemy)
                    {
                        foreach (var item in follower.EnemiesController.EnemyInfos)
                        {
                            if (item.Key.ProfileId == enemy.ProfileId)
                            {
                                info = item.Value;
                                break;
                            }
                        }
                        if (info != null) follower.Memory.GoalEnemy = info;
                    }
                }


            }
        }

        public BotOwner ClosestEnemy()
        {
            BotOwner enemy = null;

            if (bossEnemies.Count > 0)
            {
                float dist = Mathf.Infinity;

                foreach (var item in bossEnemies)
                {
                    float range = (this.Position - item.Position).sqrMagnitude;
                    if (range < dist)
                    {
                        enemy = item;
                        dist = range;
                    }
                }
            }

            return enemy;
        }

        public void DisposeBoss()
        {
            realPlayer.HealthController.DiedEvent -= OnDead;

            Singleton<BotEventHandler>.Instance.OnPhraseSay -= PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnGestusShow -= GestusShown;

            //_botsController.Bots.OnBotAdd -= OnBotAdd;

            if (bossGroup != null)
            {
                bossGroup.RemoveInfo(Player());
            }
            aBossLogic.Dispose();

            Modules.Logger.LogInfo("Player Boss Disposed");
        }
        public void AddFollower(BotOwner bot)
        {
            Followers.Add(bot);
            // dispose of the original patrol mode
            bot.BotFollower.PatrolDataFollower.InitPlayer(realPlayer);

            bot.BotFollower.SetToFollow(this,Followers.Count - 1);
            bot.PatrollingData.Pause();
            bot.PatrollingData.Disable();
        }
    }
    public class AIBossPlayerLogic : GClass430
    {
        private Player _player;
        private pitAIBossPlayer _aiplayer;
        public AIBossPlayerLogic(Player player, pitAIBossPlayer aiplayer) : base(null, null)
        {
            player.BeingHitAction += OnHit;
            _player = player;
            _aiplayer = aiplayer;
        }

        public void OnHit(DamageInfoStruct arg1, EBodyPart arg2, float arg3)
        {
            if (
                arg1.Player != null && arg1.Player.IsAI &&
                arg1.Player.AIData != null &&
                arg1.Player.AIData.BotOwner != null &&
                _aiplayer != null &&
                !BossPlayers.IsFollower(arg1.Player.AIData.BotOwner, _aiplayer)
            )
            {
                _lastTimeHit = Time.time;
                BotOwner enemyBot = arg1.Player.AIData.BotOwner;
                try
                {
                    if (_aiplayer.bossGroup != null && _aiplayer.AddEnemy(enemyBot))
                    {
                        BotOwner followerBotOwner = _aiplayer.Followers.FirstOrDefault();
                        _aiplayer.bossGroup.AddEnemy(enemyBot, EBotEnemyCause.addPlayerToBoss);
                        if(followerBotOwner != null)
                        _aiplayer.bossGroup.ReportAboutEnemy(enemyBot, EEnemyPartVisibleType.Sence, followerBotOwner);
                    }
                }
                catch (Exception e)
                {
                    Modules.Logger.LogError("Failed to add Enemy to group");
                    Modules.Logger.LogError(e);
                }
            }
        }


        public override void Activate()
        {
            if (_aiplayer.Followers.Count > 0)
            {
                foreach (var item in _aiplayer.Followers)
                {
                    if (item.IsRole(WildSpawnType.bossKnight))
                    {
                        item.Boss.BossLogic.Activate();
                        break;
                    }
                }
            }
        }

        public override void BossLogicUpdate()
        {
            if (_aiplayer.Followers.Count > 0)
            {
                foreach (var item in _aiplayer.Followers)
                {
                    if (item.IsRole(WildSpawnType.bossKnight))
                    {
                        item.Boss.BossLogic.BossLogicUpdate();
                        break;
                    }
                }
            }
        }

        public override void Dispose()
        {
            _player.BeingHitAction -= OnHit;
        }


    }
}
