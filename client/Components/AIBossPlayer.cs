using Comfort.Common;
using EFT;
using EFT.Interactive;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;


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
                    UnsubscribeBossGroupStaticUpdate();
                }
                _group = value;
                if (_group != null)
                {
                    _group.OnReportEnemy += OnReportEnemy;
                    SubscribeBossGroupStaticUpdate();
                }
            }
        }

        public readonly Player realPlayer;

        private List<BotOwner> bossEnemies = new List<BotOwner>();
        private const float TeamStatusGestureDistance = 15f;
        private const float ContactLookDistance = 45f;
        private const float GestureCommandDistance = 15f;
        private const float ComeWithMeMaxDistance = 25f;
        private const float GoThereMaxDistance = 50f;
        private const float GoThereCrouchMaxDistance = 25f;
        private const float LookAtFollowerDistance = 27f;
        private const float RegroupCloseNavDistance = 8f;
        private const float RegroupSameLevelTolerance = 1.75f;
        private const float FriendlyDownVisibilityDistance = 60f;
        private const float FriendlyDownObserveWindow = 60f;
        private const float FriendlyDownPollMs = 3000f;
        private const float TeamStatusDebounceSeconds = 0.08f;
        private const float AttentionCommandDebounceSeconds = 0.35f;
        private float _ignoreNextThereGestureUntil;
        private float _nextThereGestureAt;
        private float _lastTeamStatusCommandAt = -999f;
        private float _lastAttentionCommandAt = -999f;
        private readonly Dictionary<string, Action<EDamageType>> _followerDeathHandlers = new Dictionary<string, Action<EDamageType>>();
        private readonly Dictionary<string, FallenFollowerInfo> _pendingFriendlyDown = new Dictionary<string, FallenFollowerInfo>();
        private GClass641.IBotTimer _friendlyDownTimer;
        private static bool _sainAddonDecisionResetBridgeErrorLogged;
        private bool _bossGroupStaticUpdateSubscribed;
        private float _nextBossGroupStaticUpdateAt;
        private readonly Dictionary<string, StableEnemyReportState> _stableEnemyReportByFollower =
            new Dictionary<string, StableEnemyReportState>(StringComparer.Ordinal);
        private const float StableEnemyReportSeconds = 0.75f;

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
                    float now = Time.time;
                    if (now - _lastTeamStatusCommandAt < TeamStatusDebounceSeconds)
                    {
                        return;
                    }

                    _lastTeamStatusCommandAt = now;
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
                else if (info.phrase == EPhraseTrigger.Regroup)
                {
                    ApplyRegroupCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.OpenDoor)
                {
                    ApplyOpenDoorCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.LootGeneric || info.phrase == EPhraseTrigger.LootWeapon)
                {
                    ApplyTakeLootCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.FollowMe || info.phrase == EPhraseTrigger.Cooperation)
                {
                    ClearFollowerCommands();
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
                    _ignoreNextThereGestureUntil = Time.time + 0.75f;
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

                if (info.Gesture == EInteraction.HoldGesture)
                {
                    ApplyHoldGesture(info.Player);
                    return;
                }

                if (info.Gesture == EInteraction.ComeWithMeGesture)
                {
                    ApplyComeWithMeGesture(info.Player);
                    return;
                }

                if (info.Gesture == EInteraction.ThereGesture)
                {
                    if (Time.time < _ignoreNextThereGestureUntil)
                    {
                        return;
                    }
                    ApplyThereGesture(info.Player);
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
            if (seenEnemies == null || seenEnemies.Count == 0)
            {
                seenEnemies = GetBossVisibleEnemiesForContact(requester);
            }
            if (friendlySAIN.IsSAINInstalled && (seenEnemies == null || seenEnemies.Count == 0))
            {
                seenEnemies = GetSainContactFallbackEnemies(requester);
            }
            seenEnemies = PrioritizeContactEnemies(requester, seenEnemies);
            Vector3 lookTarget = requester.Transform.position + requester.LookDirection.normalized * ContactLookDistance;
            int followersProcessed = 0;
            int followersSkippedVisibility = 0;
            int enemiesInjected = 0;

            foreach (var follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null)
                {
                    continue;
                }

                if (requireGestureVisibility && !CanReactToBossGesture(follower, requester))
                {
                    followersSkippedVisibility++;
                    continue;
                }

                // Make followers orient toward boss reported direction.
                follower.Steering.LookToPoint(lookTarget);

                float lookOverrideDuration = Utils.Utils.Random(2f, 4f);
                followerData?.SetCommandLookOverride(lookTarget, lookOverrideDuration);
                followersProcessed++;

                if (seenEnemies == null || seenEnemies.Count == 0) continue;

                for (int i = 0; i < seenEnemies.Count; i++)
                {
                    Player enemy = seenEnemies[i];
                    if (enemy == null || enemy.ProfileId == follower.ProfileId || enemy.ProfileId == realPlayer.ProfileId) continue;
                    if (enemy.IsAI)
                    {
                        WildSpawnType? role = enemy.Profile?.Info?.Settings?.Role;
                        if (role.HasValue && Props.friendlyBotTypes.Contains(role.Value))
                        {
                            continue;
                        }
                    }

                    RegisterContactEnemyForFollower(follower, enemy, i == 0);
                    enemiesInjected++;
                }
            }

        }

        private void RegisterContactEnemyForFollower(BotOwner follower, Player enemy, bool prioritizeAsGoal)
        {

            try
            {
                follower.BotsGroup?.AddEnemy(enemy, EBotEnemyCause.addPlayerToBoss);
                follower.BotsGroup?.ReportAboutEnemy(enemy, EEnemyPartVisibleType.Visible, follower);
            }
            catch (Exception ex)
            {
                // Keep memory injection path even if group propagation fails.
                Modules.Logger.LogError(ex);
            }

            BotSettingsClass botSettings = new BotSettingsClass(enemy, follower.BotsGroup, EBotEnemyCause.addPlayerToBoss)
            {
                EnemyLastPosition = enemy.Position
            };
            botSettings.EnemyLastVisiblePosition = enemy.Position;
            botSettings.EnemyWeaponRootLastPos = enemy.PlayerBones?.WeaponRoot?.position ?? (enemy.Position + Vector3.up * 1.2f);

            // Contact is an explicit combat cue from boss; force an EnemyInfo to exist now
            // instead of waiting for later controller reconciliation.
            follower.Memory.IsPeace = false;
            EnemyInfo? trackedEnemy = Enemy.MakeEnemy(follower, enemy, EBotEnemyCause.addPlayerToBoss);
            if (trackedEnemy != null)
            {
                trackedEnemy.SetVisible(true);
                trackedEnemy.PersonalLastPos = enemy.Position;
                trackedEnemy.GroupInfo = botSettings;
            }

            if (prioritizeAsGoal)
            {
                PromoteEnemyAsGoal(follower, enemy.ProfileId);
            }
            else if (!follower.Memory.HaveEnemy || follower.Memory.GoalEnemy == null)
            {
                PromoteEnemyAsGoal(follower, enemy.ProfileId);
            }

            BotOwner? enemyBot = enemy.AIData?.BotOwner;

            if (enemyBot != null)
            {
                PrioritizeEnemy(follower, enemyBot);
            }


            TrySyncSainEnemyState(follower, enemy, prioritizeAsGoal);


            // Entering combat should break request commands
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) && follower.Memory.HaveEnemy)
            {
                followerData.ClearCommand($"ContactEnemy:RegisterContactEnemyForFollower active={activeCommand}");
            }
        }

        private static EnemyInfo? GetTrackedEnemyInfo(BotOwner follower, string enemyProfileId)
        {
            if (follower?.EnemiesController?.EnemyInfos == null || string.IsNullOrEmpty(enemyProfileId))
            {
                return null;
            }

            foreach (var item in follower.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId == enemyProfileId)
                {
                    return item.Value;
                }
            }

            return null;
        }

        private static bool TrySyncSainEnemyState(BotOwner follower, Player enemyPlayer, bool prioritizeAsGoal)
        {
            if (!friendlySAIN.UseSainFollowerCombat) return false;
            if (follower == null || enemyPlayer == null) return false;

            try
            {
                return SainAddonBridge.TrySyncEnemyState(follower, enemyPlayer, prioritizeAsGoal);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
                return false;
            }
        }

        private static bool TryResetSainDecisionState(BotOwner follower)
        {
            if (!friendlySAIN.UseSainFollowerCombat) return false;
            if (follower == null) return false;

            try
            {
                if (!SainAddonBridge.IsFollowerCombatEnabled)
                {
                    return false;
                }

                bool reset = SainAddonBridge.TryResetDecisionState(follower);
                if (!reset && !SainAddonBridge.HasRuntimeCallbacks && !_sainAddonDecisionResetBridgeErrorLogged)
                {
                    _sainAddonDecisionResetBridgeErrorLogged = true;
                    Modules.Logger.LogError("[SAIN] Decision reset bridge is unavailable. Ensure friendlySAIN SAIN addon is present and loaded.");
                }

                return reset;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo($"[Regroup] Failed to reset SAIN decision state for {follower?.Profile?.Nickname}");
                Modules.Logger.LogError(ex);
                return false;
            }
        }

        private static void PromoteEnemyAsGoal(BotOwner follower, string enemyProfileId)
        {
            EnemyInfo? promoted = GetTrackedEnemyInfo(follower, enemyProfileId);

            if (promoted != null)
            {
                promoted.PriorityIndex = 0;
                follower.Memory.GoalEnemy = promoted;
            }
        }

        private static List<Player> PrioritizeContactEnemies(IPlayer requester, List<Player> seenEnemies)
        {
            if (requester == null || seenEnemies == null || seenEnemies.Count <= 1)
            {
                return seenEnemies ?? new List<Player>();
            }

            Player? closestSeenEnemy = InteractableObjects.GetClosestSeenEnemy();
            Vector3 lookDirection = requester.LookDirection.sqrMagnitude > 0.001f
                ? requester.LookDirection.normalized
                : requester.Transform.forward;
            Vector3 requesterPosition = requester.Transform.position;

            return seenEnemies
                .Where(enemy => enemy != null)
                .Distinct()
                .OrderByDescending(enemy => closestSeenEnemy != null && enemy.ProfileId == closestSeenEnemy.ProfileId)
                .ThenByDescending(enemy =>
                {
                    Vector3 toEnemy = enemy.Position - requesterPosition;
                    if (toEnemy.sqrMagnitude < 0.001f)
                    {
                        return 1f;
                    }

                    return Vector3.Dot(lookDirection, toEnemy.normalized);
                })
                .ThenBy(enemy => (enemy.Position - requesterPosition).sqrMagnitude)
                .ToList();
        }

        private List<Player> GetBossVisibleEnemiesForContact(IPlayer requester)
        {
            List<Player> result = new List<Player>();
            if (requester == null || bossGroup?.Enemies == null) return result;

            Vector3 firePos = requester.PlayerBones?.WeaponRoot?.position ?? (requester.Position + Vector3.up * 1.2f);
            foreach (var kv in bossGroup.Enemies)
            {
                IPlayer enemyPlayerRef = kv.Key;
                BotOwner enemyBot = enemyPlayerRef?.AIData?.BotOwner;
                if (enemyBot == null || enemyBot.IsDead || enemyBot.BotState != EBotState.Active) continue;

                Player enemy = enemyBot.GetPlayer as Player;
                if (enemy == null) continue;
                if (enemy.ProfileId == realPlayer.ProfileId) continue;
                if (Followers.Any(f => f != null && f.ProfileId == enemy.ProfileId)) continue;

                if (enemy.MainParts == null) continue;
                if (!enemy.MainParts.TryGetValue(BodyPartType.head, out _) &&
                    !enemy.MainParts.TryGetValue(BodyPartType.body, out _))
                {
                    continue;
                }

                bool visible = false;
                if (enemy.MainParts.TryGetValue(BodyPartType.head, out var headPartVisible))
                {
                    visible = Utils.Utils.CanShootToTarget(
                        new ShootPointClass(headPartVisible.Position, 1),
                        firePos,
                        LayerMaskClass.HighPolyWithTerrainMask,
                        false
                    );
                }

                if (!visible && enemy.MainParts.TryGetValue(BodyPartType.body, out var bodyPart))
                {
                    visible = Utils.Utils.CanShootToTarget(
                        new ShootPointClass(bodyPart.Position, 1),
                        firePos,
                        LayerMaskClass.HighPolyWithTerrainMask,
                        false
                    );
                }

                if (!visible) continue;
                if (!result.Any(p => p != null && p.ProfileId == enemy.ProfileId))
                {
                    result.Add(enemy);
                }
            }

            return result;
        }

        private List<Player> GetSainContactFallbackEnemies(IPlayer requester)
        {
            List<Player> result = new List<Player>();
            if (requester == null) return result;

            IEnumerable<IPlayer> allPlayers = _botsController?.BotSpawner?.AllPlayers;
            if (allPlayers == null) return result;

            Vector3 firePos = requester.PlayerBones?.WeaponRoot?.position ?? (requester.Position + Vector3.up * 1.2f);
            foreach (IPlayer candidateRef in allPlayers)
            {
                Player enemy = candidateRef as Player;
                if (enemy == null) continue;
                if (enemy.HealthController?.IsAlive != true) continue;
                if (enemy.ProfileId == requester.ProfileId || enemy.ProfileId == realPlayer.ProfileId) continue;
                if (Followers.Any(f => f != null && f.ProfileId == enemy.ProfileId)) continue;

                BotOwner enemyBot = enemy.AIData?.BotOwner;
                if (enemyBot == null || enemyBot.IsDead || enemyBot.BotState != EBotState.Active) continue;

                bool hostile = false;
                if (enemyBot.BotsGroup != null)
                {
                    hostile = enemyBot.BotsGroup.IsEnemy(requester) || enemyBot.BotsGroup.IsPlayerEnemy(requester);
                }

                if (!hostile && enemyBot.Memory?.GoalEnemy?.ProfileId == requester.ProfileId)
                {
                    hostile = true;
                }

                if (!hostile) continue;
                if (!CanEnemyBeSeenForContact(enemy, firePos)) continue;

                result.Add(enemy);
            }

            return result;
        }

        private static bool CanEnemyBeSeenForContact(Player enemy, Vector3 firePos)
        {
            if (enemy?.MainParts == null) return false;

            if (enemy.MainParts.TryGetValue(BodyPartType.head, out var headPart))
            {
                if (Utils.Utils.CanShootToTarget(new ShootPointClass(headPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false))
                {
                    return true;
                }
            }

            if (enemy.MainParts.TryGetValue(BodyPartType.body, out var bodyPart))
            {
                if (Utils.Utils.CanShootToTarget(new ShootPointClass(bodyPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanReactToBossGesture(BotOwner follower, IPlayer requester)
        {
            return CanReactToBossGesture(follower, requester, TeamStatusGestureDistance);
        }

        private bool CanReactToBossGesture(BotOwner follower, IPlayer requester, float maxDistance)
        {
            if (follower == null || requester == null) return false;
            if (follower.IsDead || follower.BotState != EBotState.Active) return false;

            float distSqr = (follower.Position - requester.Position).sqrMagnitude;
            if (distSqr > maxDistance * maxDistance) return false;

            if (realPlayer?.MainParts == null) return false;
            bool hasHead = realPlayer.MainParts.TryGetValue(BodyPartType.head, out var headPart);
            bool hasBody = realPlayer.MainParts.TryGetValue(BodyPartType.body, out var bodyPart);
            if (!hasHead && !hasBody) return false;
            Vector3 followerFirePos = follower.WeaponRoot.position;

            bool seesHead = hasHead && Utils.Utils.CanShootToTarget(
                new ShootPointClass(headPart.Position, 1),
                followerFirePos,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
            if (seesHead) return true;

            bool seesBody = hasBody && Utils.Utils.CanShootToTarget(
                new ShootPointClass(bodyPart.Position, 1),
                followerFirePos,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
            return seesBody;
        }

        private void HandleAttentionCommand()
        {
            const float enforceBlockSeconds = 2f;
            float now = Time.time;
            if (now - _lastAttentionCommandAt < AttentionCommandDebounceSeconds)
            {
                return;
            }

            _lastAttentionCommandAt = now;
            var clearedGroupIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (!BossPlayers.IsFollower(follower)) continue;

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
                followerData?.ClearCommand("Attention:Look");

                FollowerEnemyEnforceSuppression.Suppress(follower, enforceBlockSeconds);

                ClearEnemyStateForAttention(follower, clearedGroupIds);

                if (friendlySAIN.UseSainFollowerCombat)
                {
                    try
                    {
                        SainAddonBridge.TryForceReleaseFollowerCombatState(follower);
                        TryResetSainDecisionState(follower);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError($"[SAIN] Force-release combat state failed for attention follower={follower?.Profile?.Nickname}");
                        Modules.Logger.LogError(ex);
                    }
                }

                FollowerRecovery.SoftReset(follower);

                follower?.BotTalk.TrySay(EPhraseTrigger.Roger, true);
            }
        }

        private static void ClearEnemyStateForAttention(BotOwner follower, HashSet<string> clearedGroupIds)
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

            // Remove known enemies from bot memory so command intent applies immediately.
            if (follower.EnemiesController?.EnemyInfos != null)
            {
                List<IPlayer> knownEnemies = follower.EnemiesController.EnemyInfos.Keys.ToList();
                foreach (IPlayer enemy in knownEnemies)
                {
                    if (enemy == null) continue;
                    follower.Memory.DeleteInfoAboutEnemy(enemy);
                }
            }

            // Followers in one boss group share the same enemy cache; clear it once per group per command.
            if (follower.BotsGroup?.Enemies != null)
            {
                string groupId = follower.BotsGroup.Id.ToString();
                bool shouldClearGroup = string.IsNullOrEmpty(groupId) || clearedGroupIds == null || clearedGroupIds.Add(groupId);
                if (shouldClearGroup)
                {
                    List<IPlayer> groupEnemies = follower.BotsGroup.Enemies.Keys.ToList();
                    foreach (IPlayer enemy in groupEnemies)
                    {
                        if (enemy == null) continue;
                        follower.BotsGroup.RemoveEnemy(enemy);
                    }
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

        private void ApplyHoldGesture(IPlayer requester)
        {
            if (requester == null) return;

            if (requester is Player requesterPlayer)
            {
                BotOwner lookedFollower = FindLookedAtFollower(requesterPlayer, GestureCommandDistance);
                if (lookedFollower != null)
                {
                    bool applied = false;
                    if (!lookedFollower.IsDead &&
                        lookedFollower.BotState == EBotState.Active &&
                        !lookedFollower.Memory.HaveEnemy &&
                        CanReactToBossGesture(lookedFollower, requesterPlayer))
                    {
                        BotFollowerPlayer lookedFollowerData = BossPlayers.Instance?.GetFollower(lookedFollower);
                        if (lookedFollowerData != null)
                        {
                            lookedFollowerData.SetHoldPosition(float.PositiveInfinity);
                            lookedFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
                            applied = true;
                        }
                    }

                    if (applied)
                    {
                        return;
                    }
                }
            }

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (follower.Memory.HaveEnemy) continue;
                if ((follower.Position - requester.Position).sqrMagnitude > GestureCommandDistance * GestureCommandDistance) continue;
                if (!CanReactToBossGesture(follower, requester)) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                followerData.SetHoldPosition(float.PositiveInfinity);
                follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            }
        }

        private void ApplyRegroupCommand(IPlayer requester)
        {
            if (requester == null) return;

            Vector3 bossPos = requester.Position;
            bool combatRegroupContext = IsCombatRegroupContext();
            bool useSainRegroupRoute = friendlySAIN.ShouldUseSainRegroupRoute(combatRegroupContext);
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                if (useSainRegroupRoute)
                {
                    bool ignore = combatRegroupContext
                        ? ShouldIgnoreCombatRegroup(follower, bossPos)
                        : ShouldIgnoreRegroup(follower, bossPos);
                    if (ignore)
                    {
                        followerData.ClearCommand("Regroup:ignoredCloseOrHealing");
                        float navDistance = Utils.Utils.GetNavDistance(follower.Position, bossPos);
                        Modules.Logger.LogInfo($"[Regroup] IGNORE SAIN regroup for {follower.Profile?.Nickname}: filter hit. haveEnemy={follower.Memory?.HaveEnemy}, goalVisible={follower.Memory?.GoalEnemy?.IsVisible}, navDist={navDistance:F1}");
                        continue;
                    }

                    TryResetSainDecisionState(follower);
                    followerData.SetRegroup(20f);
                    follower.Gesture.TryGestus(EInteraction.OkGesture, false);
                    continue;
                }

                if (ShouldIgnoreRegroup(follower, bossPos))
                {
                    followerData.ClearCommand("Regroup:ignoredCloseOrHealing");
                    float navDistance = Utils.Utils.GetNavDistance(follower.Position, bossPos);
                    Modules.Logger.LogInfo($"[Regroup] IGNORE vanilla regroup for {follower.Profile?.Nickname}: filter hit. haveEnemy={follower.Memory?.HaveEnemy}, goalVisible={follower.Memory?.GoalEnemy?.IsVisible}, navDist={navDistance:F1}");
                    continue;
                }

                followerData.SetRegroup(20f);
                follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            }
        }

        private void ApplyTakeLootCommand(IPlayer requester)
        {
            if (requester == null)
            {
                return;
            }

            LootItem lootItem = InteractableObjects.GetCurLootItem();
            if (lootItem == null)
            {
                return;
            }

            BotOwner closestFollower = null;
            float bestSqrDistance = float.MaxValue;
            Vector3 lootPos = lootItem.transform.position;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                if (follower.Memory?.HaveEnemy == true)
                {
                    continue;
                }

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null)
                {
                    continue;
                }

                float sqrDistance = (follower.Position - lootPos).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                {
                    continue;
                }

                closestFollower = follower;
                bestSqrDistance = sqrDistance;
            }

            if (closestFollower == null)
            {
                return;
            }

            if (!InteractableObjects.SetTaker(closestFollower))
            {
                closestFollower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                return;
            }

            BotFollowerPlayer closestFollowerData = BossPlayers.Instance?.GetFollower(closestFollower);
            if (closestFollowerData == null)
            {
                InteractableObjects.RemoveTaker(closestFollower);
                return;
            }

            closestFollowerData.SetTakeLootItem(35f);
            closestFollower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ApplyOpenDoorCommand(IPlayer requester)
        {
            if (requester == null)
            {
                return;
            }

            Door door = InteractableObjects.GetCurDoor();
            if (door == null)
            {
                return;
            }

            if (door.DoorState == EDoorState.Locked)
            {
                foreach (BotOwner follower in Followers)
                {
                    if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                    {
                        continue;
                    }

                    if ((follower.Position - requester.Position).sqrMagnitude > 15f * 15f)
                    {
                        continue;
                    }

                    follower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    follower.Gesture.TryGestus(EInteraction.NoGesture, false);
                    break;
                }
                return;
            }

            BotOwner closestFollower = null;
            float bestDistance = float.MaxValue;
            Vector3 doorPos = door.transform.position;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                if (follower.Memory?.HaveEnemy == true)
                {
                    continue;
                }

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null)
                {
                    continue;
                }

                float distance = (follower.Position - doorPos).magnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                closestFollower = follower;
                bestDistance = distance;
            }

            if (closestFollower == null)
            {
                return;
            }

            if (!InteractableObjects.SetOpener(closestFollower, door))
            {
                closestFollower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                return;
            }

            BotFollowerPlayer closestFollowerData = BossPlayers.Instance?.GetFollower(closestFollower);
            if (closestFollowerData == null)
            {
                InteractableObjects.RemoveOpener(closestFollower);
                return;
            }

            closestFollowerData.SetOpenDoor(12f);
            closestFollower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ClearFollowerCommands()
        {
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                followerData?.ClearCommand("ClearFollowerCommands");
            }
        }

        private bool IsCombatRegroupContext()
        {
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (follower.Memory?.HaveEnemy == true) return true;
            }

            return false;
        }

        private static bool ShouldIgnoreRegroup(BotOwner follower, Vector3 bossPos)
        {
            if (follower == null) return true;

            BotLogicDecision currentDecision = follower.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = follower.Medecine?.FirstAid?.Using == true ||
                           follower.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing) return true;

            float verticalDiff = Mathf.Abs(follower.Position.y - bossPos.y);
            if (verticalDiff > RegroupSameLevelTolerance)
            {
                return false;
            }

            float navDistance = Utils.Utils.GetNavDistance(follower.Position, bossPos);
            return navDistance <= RegroupCloseNavDistance;
        }

        private static bool ShouldIgnoreCombatRegroup(BotOwner follower, Vector3 bossPos)
        {
            if (follower == null) return true;

            BotLogicDecision currentDecision = follower.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = follower.Medecine?.FirstAid?.Using == true ||
                           follower.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing) return true;

            float verticalDiff = Mathf.Abs(follower.Position.y - bossPos.y);
            if (verticalDiff > RegroupSameLevelTolerance)
            {
                return false;
            }

            float navDistance = Utils.Utils.GetNavDistance(follower.Position, bossPos);
            return navDistance <= RegroupCloseNavDistance;
        }

        private void HookFollowerDeath(BotOwner follower)
        {
            if (follower == null || follower.HealthController == null) return;
            if (_followerDeathHandlers.ContainsKey(follower.ProfileId)) return;

            Action<EDamageType> handler = _ => HandleFollowerDeath(follower);
            _followerDeathHandlers[follower.ProfileId] = handler;
            follower.HealthController.DiedEvent += handler;
        }

        private void UnhookFollowerDeath(BotOwner follower)
        {
            if (follower == null || follower.HealthController == null) return;
            if (!_followerDeathHandlers.TryGetValue(follower.ProfileId, out Action<EDamageType> handler)) return;

            follower.HealthController.DiedEvent -= handler;
            _followerDeathHandlers.Remove(follower.ProfileId);
        }

        private void HandleFollowerDeath(BotOwner deadFollower)
        {
            if (deadFollower == null) return;

            FallenFollowerInfo info = new FallenFollowerInfo
            {
                DeadFollowerProfileId = deadFollower.ProfileId,
                Position = deadFollower.Position,
                ExpiresAt = Time.time + FriendlyDownObserveWindow
            };

            _pendingFriendlyDown[deadFollower.ProfileId] = info;
            TryAnnounceFriendlyDown(info);
            EnsureFriendlyDownTimer();
        }

        private void EnsureFriendlyDownTimer()
        {
            if (_friendlyDownTimer != null) return;

            _friendlyDownTimer = Utils.Utils.SetTimeout(() =>
            {
                try
                {
                    ProcessFriendlyDownPending();
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("FriendlyDown timer update failed");
                    Modules.Logger.LogError(ex);
                }
            }, (int)FriendlyDownPollMs, true);
        }

        private void StopFriendlyDownTimerIfIdle()
        {
            if (_pendingFriendlyDown.Count > 0) return;
            if (_friendlyDownTimer == null) return;

            _friendlyDownTimer.Stop();
            _friendlyDownTimer = null;
        }

        private void ProcessFriendlyDownPending()
        {
            if (_pendingFriendlyDown.Count == 0)
            {
                StopFriendlyDownTimerIfIdle();
                return;
            }

            List<string> keys = _pendingFriendlyDown.Keys.ToList();
            foreach (string key in keys)
            {
                if (!_pendingFriendlyDown.TryGetValue(key, out FallenFollowerInfo info)) continue;

                if (Time.time > info.ExpiresAt)
                {
                    _pendingFriendlyDown.Remove(key);
                    continue;
                }

                if (info.Announced) continue;
                TryAnnounceFriendlyDown(info);
            }

            StopFriendlyDownTimerIfIdle();
        }

        private void TryAnnounceFriendlyDown(FallenFollowerInfo info)
        {
            if (info == null || info.Announced) return;

            BotOwner witness = FindClosestWitness(info.DeadFollowerProfileId, info.Position);
            if (witness == null) return;

            witness.BotTalk.TrySay(EPhraseTrigger.OnFriendlyDown, false);
            info.Announced = true;
            _pendingFriendlyDown.Remove(info.DeadFollowerProfileId);
        }

        private BotOwner FindClosestWitness(string deadFollowerProfileId, Vector3 deathPos)
        {
            BotOwner best = null;
            float bestDist = float.MaxValue;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (follower.ProfileId == deadFollowerProfileId) continue;

                float sqrDist = (follower.Position - deathPos).sqrMagnitude;
                if (sqrDist > FriendlyDownVisibilityDistance * FriendlyDownVisibilityDistance) continue;
                if (sqrDist >= bestDist) continue;
                if (!CanFollowerSeePosition(follower, deathPos)) continue;

                best = follower;
                bestDist = sqrDist;
            }

            return best;
        }

        private static bool CanFollowerSeePosition(BotOwner follower, Vector3 position)
        {
            if (follower == null) return false;

            Vector3 from = follower.WeaponRoot != null ? follower.WeaponRoot.position : follower.Position + Vector3.up * 1.2f;
            Vector3 target = position + Vector3.up * 0.6f;

            return Utils.Utils.CanShootToTarget(
                new ShootPointClass(target, 1),
                from,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
        }

        private void ApplyComeWithMeGesture(IPlayer requester)
        {
            if (requester is not Player requesterPlayer) return;

            BotOwner lookedFollower = FindLookedAtFollower(requesterPlayer, LookAtFollowerDistance);
            if (lookedFollower == null)
            {
                return;
            }
            if ((lookedFollower.Position - requesterPlayer.Position).sqrMagnitude > ComeWithMeMaxDistance * ComeWithMeMaxDistance)
            {
                return;
            }
            if (!CanReactToBossGesture(lookedFollower, requesterPlayer, ComeWithMeMaxDistance))
            {
                return;
            }

            BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(lookedFollower);
            if (followerData == null)
            {
                return;
            }

            followerData.SetComeCloser(10f);
            lookedFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ApplyThereGesture(IPlayer requester)
        {
            if (requester == null) return;
            if (Time.time < _nextThereGestureAt) return;
            _nextThereGestureAt = Time.time + 0.6f;

            BotOwner closestFollower = null;
            float bestDist = float.MaxValue;
            Vector3 requesterPos = requester.Position;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                float sqrDist = (follower.Position - requesterPos).sqrMagnitude;
                if (sqrDist > GestureCommandDistance * GestureCommandDistance) continue;
                if (!CanReactToBossGesture(follower, requester)) continue;
                if (sqrDist >= bestDist) continue;
                if (!CanAcceptThereCommand(follower)) continue;

                closestFollower = follower;
                bestDist = sqrDist;
            }

            if (closestFollower == null)
            {
                return;
            }

            float maxThereDistance = GoThereMaxDistance;
            if (requester is Player requesterPlayer && (requesterPlayer.MovementContext?.PoseLevel ?? 1f) < 0.75f)
            {
                maxThereDistance = GoThereCrouchMaxDistance;
            }

            Vector3 lookDir = requester.LookDirection.sqrMagnitude > 0.001f
                ? requester.LookDirection.normalized
                : requester.Transform.forward;

            Vector3 rawTarget = requesterPos + lookDir * maxThereDistance;
            if (Physics.Raycast(requesterPos + Vector3.up * 1.5f, lookDir, out RaycastHit lookHit, maxThereDistance, LayerMaskClass.HighPolyWithTerrainMask))
            {
                rawTarget = lookHit.point;
            }

            if (!NavMesh.SamplePosition(rawTarget, out NavMeshHit navHit, 12f, NavMesh.AllAreas))
            {
                return;
            }

            BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(closestFollower);
            if (followerData == null)
            {
                return;
            }

            followerData.SetMoveToPoint(navHit.position, 14f);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private static bool CanAcceptThereCommand(BotOwner follower)
        {
            if (follower == null) return false;
            if (!follower.Memory.HaveEnemy || follower.Memory.GoalEnemy == null) return true;

            EnemyInfo enemy = follower.Memory.GoalEnemy;
            if (enemy.IsVisible) return false;

            float lastSeenAgo = Time.time - enemy.PersonalLastSeenTime;
            if (lastSeenAgo <= 3f) return false;

            BotLogicDecision action = follower.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return action != BotLogicDecision.goToEnemy &&
                   action != BotLogicDecision.runToEnemy &&
                   action != BotLogicDecision.runToEnemyZigZag &&
                   action != BotLogicDecision.attackMoving &&
                   action != BotLogicDecision.attackMovingWithSuppress &&
                   action != BotLogicDecision.attackMovingFlank;
        }

        private BotOwner FindLookedAtFollower(Player requester, float distance)
        {
            if (requester == null) return null;

            const float sphereRadius = 0.4f;
            RaycastHit[] hits = new RaycastHit[10];
            Ray ray = requester.InteractionRay;
            int hitCount = Physics.SphereCastNonAlloc(ray, sphereRadius, hits, distance, LayerMaskClass.PlayerMask);
            if (hitCount <= 0) return null;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = hits[i];
                if (hit.collider?.gameObject == null) continue;
                BotOwner bot = hit.collider.gameObject.GetComponentInParent<BotOwner>();
                if (bot == null) continue;
                if (!BossPlayers.IsFollower(bot, this)) continue;

                if (CanBossSeeFollowerGestureTarget(bot, requester))
                {
                    return bot;
                }
            }

            return null;
        }

        private static bool CanBossSeeFollowerGestureTarget(BotOwner follower, Player requester)
        {
            if (follower == null || requester == null) return false;
            if (follower.GetPlayer?.MainParts == null) return false;
            if (requester.PlayerBones?.WeaponRoot == null) return false;

            bool hasHead = follower.GetPlayer.MainParts.TryGetValue(BodyPartType.head, out var headPart);
            bool hasBody = follower.GetPlayer.MainParts.TryGetValue(BodyPartType.body, out var bodyPart);
            if (!hasHead && !hasBody) return false;

            Vector3 bossFirePos = requester.PlayerBones.WeaponRoot.position;

            bool seesHead = hasHead && Utils.Utils.CanShootToTarget(
                new ShootPointClass(headPart.Position, 1),
                bossFirePos,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
            if (seesHead) return true;

            bool seesBody = hasBody && Utils.Utils.CanShootToTarget(
                new ShootPointClass(bodyPart.Position, 1),
                bossFirePos,
                LayerMaskClass.HighPolyWithTerrainMask,
                false
            );
            return seesBody;
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
            UnsubscribeBossGroupStaticUpdate();
            _stableEnemyReportByFollower.Clear();
            realPlayer.HealthController.DiedEvent -= OnDead;

            Singleton<BotEventHandler>.Instance.OnPhraseSay -= PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnGestusShow -= GestusShown;

            //_botsController.Bots.OnBotAdd -= OnBotAdd;

            if (bossGroup != null)
            {
                bossGroup.RemoveInfo(Player());
            }

            foreach (BotOwner follower in Followers.ToList())
            {
                UnhookFollowerDeath(follower);
            }
            _pendingFriendlyDown.Clear();
            StopFriendlyDownTimerIfIdle();
            aBossLogic.Dispose();

            Modules.Logger.LogInfo("Player Boss Disposed");
        }

        private void SubscribeBossGroupStaticUpdate()
        {
            if (!friendlySAIN.UseSainFollowerCombat || _bossGroupStaticUpdateSubscribed || _group == null || StaticManager.Instance == null)
            {
                return;
            }

            StaticManager.Instance.StaticUpdate += OnBossGroupStaticUpdate;
            _bossGroupStaticUpdateSubscribed = true;
        }

        private void UnsubscribeBossGroupStaticUpdate()
        {
            if (!_bossGroupStaticUpdateSubscribed || StaticManager.Instance == null)
            {
                return;
            }

            StaticManager.Instance.StaticUpdate -= OnBossGroupStaticUpdate;
            _bossGroupStaticUpdateSubscribed = false;
        }

        private void OnBossGroupStaticUpdate()
        {
            if (realPlayer == null || !realPlayer.HealthController.IsAlive)
            {
                return;
            }

            if (Time.time < _nextBossGroupStaticUpdateAt)
            {
                return;
            }

            _nextBossGroupStaticUpdateAt = Time.time + 0.5f;
            ReportEnemyToIdleFollowers();
            SainAddonBridge.RaiseBossGroupStaticUpdate(this);
        }

        private void ReportEnemyToIdleFollowers()
        {
            if (bossGroup == null || Followers == null || Followers.Count < 2)
            {
                _stableEnemyReportByFollower.Clear();
                return;
            }

            HashSet<string> activeFollowerIds = new HashSet<string>(StringComparer.Ordinal);
            BotOwner bestReporter = null;
            Player enemyPlayer = null;
            EEnemyPartVisibleType visibleType = EEnemyPartVisibleType.Sence;
            bool foundVisibleReporter = false;

            for (int i = 0; i < Followers.Count; i++)
            {
                BotOwner follower = Followers[i];
                if (!IsFollowerEligibleForGroupEnemySync(follower))
                {
                    continue;
                }

                activeFollowerIds.Add(follower.ProfileId);

                if (!TryGetStableEnemyReporter(follower, out Player candidateEnemy, out EEnemyPartVisibleType candidateVisibleType))
                {
                    continue;
                }

                if (bestReporter == null || (!foundVisibleReporter && candidateVisibleType == EEnemyPartVisibleType.Visible))
                {
                    bestReporter = follower;
                    enemyPlayer = candidateEnemy;
                    visibleType = candidateVisibleType;
                    foundVisibleReporter = candidateVisibleType == EEnemyPartVisibleType.Visible;
                }
            }

            foreach (string followerId in _stableEnemyReportByFollower.Keys.ToList())
            {
                if (!activeFollowerIds.Contains(followerId))
                {
                    _stableEnemyReportByFollower.Remove(followerId);
                }
            }

            if (bestReporter == null || enemyPlayer == null)
            {
                return;
            }

            for (int i = 0; i < Followers.Count; i++)
            {
                BotOwner follower = Followers[i];
                if (!IsFollowerEligibleForGroupEnemySync(follower) || follower == bestReporter)
                {
                    continue;
                }

                if (follower.Memory?.HaveEnemy == true)
                {
                    continue;
                }

                bossGroup.ReportAboutEnemy(enemyPlayer, visibleType, bestReporter);
                break;
            }
        }

        private bool TryGetStableEnemyReporter(BotOwner follower, out Player enemyPlayer, out EEnemyPartVisibleType visibleType)
        {
            enemyPlayer = null;
            visibleType = EEnemyPartVisibleType.Sence;

            if (!IsFollowerEligibleForGroupEnemySync(follower))
            {
                ClearStableEnemyReporter(follower);
                return false;
            }

            EnemyInfo goalEnemy = follower.Memory?.GoalEnemy;
            if (goalEnemy == null || !follower.Memory.HaveEnemy || follower.Memory.IsPeace)
            {
                ClearStableEnemyReporter(follower);
                return false;
            }

            enemyPlayer = goalEnemy.Person as Player;
            if ((enemyPlayer == null || enemyPlayer.HealthController?.IsAlive != true) && !string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                enemyPlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
            }

            if (enemyPlayer == null || enemyPlayer.HealthController?.IsAlive != true)
            {
                ClearStableEnemyReporter(follower);
                return false;
            }

            if (BossPlayers.IsPlayerBoss(enemyPlayer.ProfileId) ||
                (enemyPlayer.IsAI && enemyPlayer.AIData?.BotOwner != null && BossPlayers.IsFollower(enemyPlayer.AIData.BotOwner)))
            {
                ClearStableEnemyReporter(follower);
                return false;
            }

            if (!_stableEnemyReportByFollower.TryGetValue(follower.ProfileId, out StableEnemyReportState state) ||
                state == null ||
                !string.Equals(state.EnemyProfileId, enemyPlayer.ProfileId, StringComparison.Ordinal))
            {
                _stableEnemyReportByFollower[follower.ProfileId] = new StableEnemyReportState
                {
                    EnemyProfileId = enemyPlayer.ProfileId,
                    Since = Time.time
                };
                return false;
            }

            if (Time.time - state.Since < StableEnemyReportSeconds)
            {
                return false;
            }

            visibleType = goalEnemy.IsVisible ? EEnemyPartVisibleType.Visible : EEnemyPartVisibleType.Sence;
            return true;
        }

        private void ClearStableEnemyReporter(BotOwner follower)
        {
            if (follower == null || string.IsNullOrEmpty(follower.ProfileId))
            {
                return;
            }

            _stableEnemyReportByFollower.Remove(follower.ProfileId);
        }

        private static bool IsFollowerEligibleForGroupEnemySync(BotOwner follower)
        {
            return follower != null &&
                   !follower.IsDead &&
                   follower.BotState == EBotState.Active &&
                   follower.Memory != null &&
                   follower.GetPlayer?.HealthController?.IsAlive == true;
        }

        public void AddFollower(BotOwner bot)
        {
            Followers.Add(bot);
            HookFollowerDeath(bot);
            bot.BotFollower.SetToFollow(this, Followers.Count - 1);
            // dispose of the original patrol mode
            bot.BotFollower.PatrolDataFollower.InitPlayer(realPlayer);
            bot.PatrollingData.Pause();
            bot.PatrollingData.Disable();
        }

        private sealed class FallenFollowerInfo
        {
            public string DeadFollowerProfileId;
            public Vector3 Position;
            public float ExpiresAt;
            public bool Announced;
        }

        private sealed class StableEnemyReportState
        {
            public string EnemyProfileId = string.Empty;
            public float Since;
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
                        if (followerBotOwner != null)
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
