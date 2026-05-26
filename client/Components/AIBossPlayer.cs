using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.BigBrain;
using pitTeam.Modules;
using pitTeam.Utils;
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

namespace pitTeam.Components
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
        public CombatEvents CombatEvents { get; } = new CombatEvents();

        private List<BotOwner> bossEnemies = new List<BotOwner>();
        private const float TeamStatusGestureDistance = 15f;
        private const float ContactLookDistance = 45f;
        private const float ContactConeMinDot = 0.45f;
        private const float GestureCommandDistance = 15f;
        private const float HoldGestureDistance = 25f;
        private const float PhraseCommandDistance = 30f;
        private const float StopPhraseDistance = 35f;
        private const float CommandLookOverrideMinSeconds = 1.5f;
        private const float CommandLookOverrideMaxSeconds = 3.5f;
        private const float ComeWithMeMaxDistance = 30f;
        private const float DefaultGoToDistance = 50f;
        private const float CombatThereMaxDistance = 30f;
        private const float LookAtFollowerDistance = 30f;
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
        private float _lastCombatSupportCueAt = -999f;
        private float _lastBossShotAt = -999f;
        private const float CombatSupportCueWindow = 4f;
        private const float BossShotRecentWindow = 2.5f;

        public pitAIBossPlayer(Player player, BotsController botsController) : base(player)
        {
            realPlayer = player;

            aBossLogic = new AIBossPlayerLogic(player, this);
            _botsController = botsController;

            player.HealthController.DiedEvent += OnDead;

            Singleton<BotEventHandler>.Instance.OnPhraseSay += PhraseSaid;
            Singleton<BotEventHandler>.Instance.OnGestusShow += GestusShown;

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

        // Boss command map:
        // TeamStatus: status ping UI + nearby friendly gesture.
        // NeedSniper/NeedHelp/Contact/DirectionalLook/Look: attention and target-acquisition cues.
        // Stop/Hold/GoGoGo/GoForward/Suppress/Regroup: request or combat-objective commands.
        // OnYourOwn/CoverMe: broadcast patrol or combat-independent mode toggles, not movement requests.
        // OpenDoor/Loot: situational request-layer work.
        // FollowMe/Cooperation: recruit or clear follower commands back to normal follow.
        public void PhraseSaid(EventInfo info)
        {
            if (info.PlayerRequester != null && info.PlayerRequester.ProfileId == realPlayer.ProfileId)
            {
                // Phrase command path:
                // player voice line -> this boss-side router -> BotFollowerPlayer command state.
                // FollowerRequestLayer later consumes that state and starts the matching BigBrain action.
                if (info.phrase == (EPhraseTrigger)CustomPhrases.TeamStatus)
                {
                    // Status Report: show follower status and have nearby idle followers answer visually.
                    float now = Time.time;
                    if (now - _lastTeamStatusCommandAt < TeamStatusDebounceSeconds)
                    {
                        return;
                    }

                    _lastTeamStatusCommandAt = now;
                    PingTeamates.Instance.Ping(this);
                    SignalTeamStatusFollowers();
                }
                else if (info.phrase == EPhraseTrigger.NeedSniper)
                {
                    // Need Sniper: ask marksman followers for a firing-position support action.
                    ApplyNeedSniperPhrase(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.NeedHelp)
                {
                    // Need Help: mark the boss-local threat for follower support.
                    ApplyNeedHelpPhrase(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.OnRepeatedContact)
                {
                    // Contact: point followers toward a seen or aimed-at threat and seed enemy memory.
                    ProcessContactCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.InTheFront ||
                         info.phrase == EPhraseTrigger.LeftFlank ||
                         info.phrase == EPhraseTrigger.RightFlank ||
                         info.phrase == EPhraseTrigger.OnSix)
                {
                    // Directional calls: look-only callouts relative to the boss view direction.
                    ApplyDirectionalLookPhrase(info.PlayerRequester, info.phrase);
                }
                else if (info.phrase == EPhraseTrigger.Look)
                {
                    // Attention: clear look pressure and refocus followers on the boss/direction.
                    HandleAttentionCommand();
                }
                else if (info.phrase == EPhraseTrigger.Stop)
                {
                    // Stop: out-of-combat hold without crouch.
                    ApplyStopPhrase(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.HoldPosition)
                {
                    // Hold Position: combat 0% temporary aggression override.
                    ApplyHoldPositionCombatAggression(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.Gogogo)
                {
                    // Go Go Go: clear the temporary combat aggression override.
                    ApplyGoGoGoCombatAggression(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.GoForward)
                {
                    // Go Forward: push current enemy in combat, point movement out of combat.
                    ApplyGoForwardPhrase(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.Suppress)
                {
                    // Suppress: non-marksman suppress-capable followers suppress current contact.
                    ApplySuppressPhrase(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.Regroup)
                {
                    // Regroup: out of combat exits patrol; in combat orders a literal boss-position regroup.
                    ApplyRegroupCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.OnYourOwn)
                {
                    // On Your Own: out-of-combat patrol-radius mode; in combat independent anchor mode.
                    ApplyOnYourOwnCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.CoverMe)
                {
                    // Cover Me: out-of-combat drops patrol; in combat drops independent anchor mode only.
                    ApplyCoverMeCommandDrop(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.OpenDoor)
                {
                    // Open Door: closest eligible follower opens the target door.
                    ApplyOpenDoorCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.LootGeneric ||
                         info.phrase == EPhraseTrigger.LootWeapon ||
                         info.phrase == EPhraseTrigger.LootKey ||
                         info.phrase == EPhraseTrigger.LootMoney)
                {
                    // Loot: closest eligible follower takes the target item.
                    ApplyTakeLootCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.CheckHim ||
                         info.phrase == EPhraseTrigger.LootBody)
                {
                    // Body check: closest eligible follower moves to the corpse and recovers gear as cargo.
                    ApplyTakeBodyGearCommand(info.PlayerRequester);
                    return;
                }
                else if (info.phrase == EPhraseTrigger.FollowMe || info.phrase == EPhraseTrigger.Cooperation)
                {
                    // Follow Me / Cooperation: normal follow mode and command cleanup.
                    ClearFollowerCommands(info.PlayerRequester);
                    return;
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
                // Gesture command path mirrors phrases, but gesture visibility/range gates are stricter.
                // Commands handled here are consumed before the gesture is forwarded to vanilla receivers.
                if (info.Gesture == (EInteraction)CustomGestures.OverThere)
                {
                    // Over There: gesture-based Contact. Do not forward the contact phrase to
                    // receivers; commanded acquisition should be silent and go straight to fighting.
                    _ignoreNextThereGestureUntil = Time.time + 0.75f;
                    ProcessContactCommand(info.Player, true);

                    return;
                }

                if (info.Gesture == EInteraction.HoldGesture)
                {
                    // Hold gesture: visible nearby followers hold position and crouch.
                    ApplyHoldGesture(info.Player);
                    return;
                }

                if (info.Gesture == EInteraction.ComeWithMeGesture)
                {
                    // Come With Me: close movement out of combat, boss-cover movement in combat.
                    ApplyComeWithMeGesture(info.Player);
                    return;
                }

                if (info.Gesture == EInteraction.ThereGesture)
                {
                    // There: point movement out of combat, tactical point movement in combat.
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

            _lastCombatSupportCueAt = Time.time;

            List<Player> seenEnemies = new List<Player>();
            try
            {
                InteractableObjects.CheckSeenEnemies(Player());
                seenEnemies = InteractableObjects.GetSeenEnemies();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"Contact command legacy seen-enemy scan failed; falling back to direct visibility scan. {ex}");
            }
            if (seenEnemies == null || seenEnemies.Count == 0)
            {
                seenEnemies = GetBossVisibleEnemiesForContact(requester);
            }
            if (pitFireTeam.IsSAINInstalled && (seenEnemies == null || seenEnemies.Count == 0))
            {
                seenEnemies = GetSainContactFallbackEnemies(requester);
            }
            List<Player> directedVisibleEnemies = GetBossDirectedVisibleEnemyCandidates(requester);
            if (directedVisibleEnemies.Count > 0)
            {
                seenEnemies ??= new List<Player>();
                foreach (Player directedEnemy in directedVisibleEnemies)
                {
                    if (directedEnemy != null &&
                        !seenEnemies.Any(enemy => enemy != null && enemy.ProfileId == directedEnemy.ProfileId))
                    {
                        seenEnemies.Add(directedEnemy);
                    }
                }
            }
            seenEnemies = FilterContactEnemyCandidates(requester, seenEnemies);
            seenEnemies = PrioritizeContactEnemies(requester, seenEnemies);
            Vector3 lookTarget = GetLookTargetFromDirection(requester, requester.LookDirection);
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

                bool hasOwnVisibleGoal = TryGetLiveVisibleEnemy(follower, out string ownVisibleGoalId);
                if (!hasOwnVisibleGoal)
                {
                    ApplyFollowerLookOverride(follower, lookTarget);
                }
                followersProcessed++;

                if (seenEnemies == null || seenEnemies.Count == 0) continue;

                Player? prioritizedGoalEnemy = null;
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

                    if (hasOwnVisibleGoal)
                    {
                        if (string.Equals(enemy.ProfileId, ownVisibleGoalId, StringComparison.Ordinal))
                        {
                            prioritizedGoalEnemy = enemy;
                            break;
                        }

                        continue;
                    }

                    if (prioritizedGoalEnemy == null)
                    {
                        prioritizedGoalEnemy = enemy;
                    }

                    if (CanFollowerSeeEnemyForContact(follower, enemy))
                    {
                        prioritizedGoalEnemy = enemy;
                        break;
                    }
                }

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

                    if (hasOwnVisibleGoal &&
                        !string.Equals(enemy.ProfileId, ownVisibleGoalId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    bool prioritizeAsGoal = prioritizedGoalEnemy != null &&
                                            enemy.ProfileId == prioritizedGoalEnemy.ProfileId;
                    pitTeam.Patches.FollowerContactPhraseGate.SuppressCommandedContact(follower, enemy.ProfileId, 4f);
                    RegisterContactEnemyForFollower(follower, enemy, prioritizeAsGoal, true);
                    enemiesInjected++;
                }
            }

        }

        public void MarkBossShot()
        {
            _lastBossShotAt = Time.time;
        }

        public bool HasRecentCombatSupportCue()
        {
            return Time.time - _lastCombatSupportCueAt <= CombatSupportCueWindow;
        }

        public bool WasShootingRecentlyForSupport()
        {
            return Time.time - _lastBossShotAt <= BossShotRecentWindow;
        }

        public bool TryGetSupportEnemy(out string enemyProfileId, out Vector3 enemyPosition)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;

            List<Player> visibleEnemies = GetBossVisibleEnemiesForContact(realPlayer);
            if (pitFireTeam.IsSAINInstalled && (visibleEnemies == null || visibleEnemies.Count == 0))
            {
                visibleEnemies = GetSainContactFallbackEnemies(realPlayer);
            }

            visibleEnemies = PrioritizeContactEnemies(realPlayer, visibleEnemies);
            Player enemy = visibleEnemies != null ? visibleEnemies.FirstOrDefault() : null;
            if (enemy == null)
            {
                return false;
            }

            enemyProfileId = enemy.ProfileId ?? string.Empty;
            enemyPosition = enemy.Position;
            return !string.IsNullOrEmpty(enemyProfileId);
        }

        public bool IsPlayerEngaging(out string enemyProfileId, out Vector3 enemyPosition)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;

            if (TryGetSupportEnemy(out enemyProfileId, out enemyPosition))
            {
                return true;
            }

            return HasRecentCombatSupportCue() || WasShootingRecentlyForSupport();
        }

        private void RegisterContactEnemyForFollower(BotOwner follower, Player enemy, bool prioritizeAsGoal, bool allowGoalPromotion)
        {
            EEnemyPartVisibleType visibleType = CanFollowerSeeEnemyForContact(follower, enemy)
                ? EEnemyPartVisibleType.Visible
                : EEnemyPartVisibleType.Sence;
            bool visibleForContact = visibleType == EEnemyPartVisibleType.Visible;
            string beforeGoalId = follower.Memory?.GoalEnemy?.ProfileId ?? "<null>";
            bool beforeHaveEnemy = follower.Memory?.HaveEnemy == true;

            try
            {
                follower.BotsGroup?.AddEnemy(enemy, EBotEnemyCause.checkAddTODO);

                follower.BotsGroup?.ReportAboutEnemy(enemy, visibleType, follower);
            }
            catch (Exception ex)
            {
                // Keep memory injection path even if group propagation fails.
                Modules.Logger.LogError(ex);
            }

            // Contact is an explicit combat cue from boss; force an EnemyInfo to exist now
            // instead of waiting for later controller reconciliation.
            follower.Memory.IsPeace = false;
            EnemyInfo? trackedEnemy = Enemy.MakeEnemy(follower, enemy, EBotEnemyCause.checkAddTODO);
            if (trackedEnemy != null)
            {
                BotSettingsClass botSettings = GetOrCreateContactEnemyGroupInfo(follower, enemy, trackedEnemy);
                botSettings.EnemyLastPosition = enemy.Position;
                botSettings.IsHaveSeen = visibleForContact;
                botSettings.EnemyLastSeenTimeSense = Time.time;
                if (visibleForContact)
                {
                    botSettings.EnemyLastVisiblePosition = enemy.Position;
                }

                botSettings.EnemyWeaponRootLastPos = enemy.PlayerBones?.WeaponRoot?.position ?? (enemy.Position + Vector3.up * 1.2f);

                follower.Memory.AddEnemy(enemy, botSettings, false);

                trackedEnemy.SetVisible(visibleForContact);
                trackedEnemy.PersonalLastPos = enemy.Position;
                trackedEnemy.GroupInfo = botSettings;
                Enemy.RepairPersonalMemory(trackedEnemy, enemy.Position, visibleForContact || prioritizeAsGoal);
            }

            if (allowGoalPromotion && prioritizeAsGoal)
            {
                PromoteEnemyAsGoal(follower, enemy.ProfileId);
            }
            else if (allowGoalPromotion && (!follower.Memory.HaveEnemy || follower.Memory.GoalEnemy == null))
            {
                PromoteEnemyAsGoal(follower, enemy.ProfileId);
            }

            if (trackedEnemy != null &&
                allowGoalPromotion &&
                (prioritizeAsGoal || follower.Memory.GoalEnemy == null || follower.Memory.GoalEnemy.ProfileId == enemy.ProfileId))
            {
                trackedEnemy.PriorityIndex = 0;
                trackedEnemy.SetVisible(visibleForContact);
                Enemy.RepairPersonalMemory(trackedEnemy, enemy.Position, visibleForContact || prioritizeAsGoal);
                follower.Memory.GoalEnemy = trackedEnemy;
            }

            if (allowGoalPromotion)
            {
                FollowerContactEnemyRetention.Register(follower, enemy, visibleForContact, prioritizeAsGoal);
            }

            BotOwner? enemyBot = enemy.AIData?.BotOwner;

            if (allowGoalPromotion && enemyBot != null)
            {
                PrioritizeEnemy(follower, enemyBot);
            }


            bool sainSynced = TrySyncSainEnemyState(follower, enemy, prioritizeAsGoal);


            // Entering combat should break request commands
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType activeCommand, out _) &&
                activeCommand != FollowerCommandType.PushEnemy &&
                activeCommand != FollowerCommandType.SuppressEnemy &&
                follower.Memory.HaveEnemy)
            {
                followerData.ClearCommand($"ContactEnemy:RegisterContactEnemyForFollower active={activeCommand}");
            }
        }

        private static bool TryGetLiveVisibleEnemy(BotOwner follower, out string enemyProfileId)
        {
            enemyProfileId = string.Empty;
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (IsLiveVisibleEnemy(goalEnemy))
            {
                enemyProfileId = goalEnemy.ProfileId ?? string.Empty;
                if (!string.IsNullOrEmpty(enemyProfileId))
                {
                    return true;
                }
            }

            if (follower?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in follower.EnemiesController.EnemyInfos)
            {
                EnemyInfo? enemyInfo = item.Value;
                if (!IsLiveVisibleEnemy(enemyInfo))
                {
                    continue;
                }

                enemyProfileId = enemyInfo.ProfileId ?? item.Key?.ProfileId ?? string.Empty;
                return !string.IsNullOrEmpty(enemyProfileId);
            }

            return false;
        }

        private static bool IsLiveVisibleEnemy(EnemyInfo? enemyInfo)
        {
            return enemyInfo?.Person?.HealthController?.IsAlive == true &&
                   (enemyInfo.IsVisible || enemyInfo.CanShoot);
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
            if (!pitFireTeam.UseSainFollowerCombat) return false;
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
            if (!pitFireTeam.UseSainFollowerCombat) return false;
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
                    Modules.Logger.LogError("[SAIN] Decision reset bridge is unavailable. Ensure pitFireTeam SAIN addon is present and loaded.");
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
                Enemy.RepairPersonalMemory(promoted, promoted.CurrPosition, promoted.IsVisible || promoted.CanShoot || promoted.HaveSeen);
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

        private List<Player> FilterContactEnemyCandidates(IPlayer requester, List<Player> candidates)
        {
            if (requester == null || candidates == null || candidates.Count == 0)
            {
                return new List<Player>();
            }

            Vector3 requesterPosition = requester.Position;
            Vector3 lookDirection = requester.LookDirection.sqrMagnitude > 0.001f
                ? requester.LookDirection.normalized
                : requester.Transform.forward;
            float scanDistance = pitFireTeam.scanDistance?.Value ?? ContactLookDistance;
            float scanDistanceSqr = scanDistance * scanDistance;

            List<Player> filtered = new List<Player>();
            foreach (Player candidate in candidates)
            {
                if (candidate == null ||
                    !IsEligibleBossDirectedContactTarget(requester, candidate))
                {
                    continue;
                }

                Vector3 toCandidate = candidate.Position - requesterPosition;
                float distanceSqr = toCandidate.sqrMagnitude;
                if (distanceSqr < 0.01f || distanceSqr > scanDistanceSqr)
                {
                    continue;
                }

                if (Vector3.Dot(lookDirection, toCandidate.normalized) < ContactConeMinDot)
                {
                    continue;
                }

                if (!filtered.Any(enemy => enemy != null && enemy.ProfileId == candidate.ProfileId))
                {
                    filtered.Add(candidate);
                }
            }

            return filtered;
        }

        private static BotSettingsClass GetOrCreateContactEnemyGroupInfo(BotOwner follower, Player enemy, EnemyInfo trackedEnemy)
        {
            if (follower?.BotsGroup?.Enemies != null &&
                follower.BotsGroup.Enemies.TryGetValue(enemy, out BotSettingsClass groupInfo) &&
                groupInfo != null)
            {
                return groupInfo;
            }

            if (trackedEnemy?.GroupInfo != null)
            {
                return trackedEnemy.GroupInfo;
            }

            return new BotSettingsClass(enemy, follower.BotsGroup, EBotEnemyCause.checkAddTODO);
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

        private List<Player> GetBossDirectedVisibleEnemyCandidates(IPlayer requester)
        {
            List<Player> result = new List<Player>();
            if (requester == null || realPlayer == null)
            {
                return result;
            }

            IEnumerable<IPlayer> allPlayers = _botsController?.BotSpawner?.AllPlayers;
            if (allPlayers == null)
            {
                return result;
            }

            Vector3 requesterPosition = requester.Position;
            Vector3 lookDirection = requester.LookDirection.sqrMagnitude > 0.001f
                ? requester.LookDirection.normalized
                : requester.Transform.forward;
            Vector3 firePos = requester.PlayerBones?.WeaponRoot?.position ?? (requesterPosition + Vector3.up * 1.2f);
            float scanDistance = pitFireTeam.scanDistance?.Value ?? ContactLookDistance;
            float scanDistanceSqr = scanDistance * scanDistance;

            foreach (IPlayer candidateRef in allPlayers)
            {
                Player candidate = candidateRef as Player;
                if (candidate == null || candidate.HealthController?.IsAlive != true)
                {
                    continue;
                }

                if (!IsEligibleBossDirectedContactTarget(requester, candidate))
                {
                    continue;
                }

                Vector3 toCandidate = candidate.Position - requesterPosition;
                float distanceSqr = toCandidate.sqrMagnitude;
                if (distanceSqr < 0.01f || distanceSqr > scanDistanceSqr)
                {
                    continue;
                }

                if (Vector3.Dot(lookDirection, toCandidate.normalized) < ContactConeMinDot)
                {
                    continue;
                }

                if (!CanEnemyBeSeenForContact(candidate, firePos))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        private bool IsEligibleBossDirectedContactTarget(IPlayer requester, Player candidate)
        {
            if (requester == null || candidate == null)
            {
                return false;
            }

            if (candidate.ProfileId == requester.ProfileId || candidate.ProfileId == realPlayer?.ProfileId)
            {
                return false;
            }

            if (Followers.Any(follower => follower != null && follower.ProfileId == candidate.ProfileId))
            {
                return false;
            }

            if (!candidate.IsAI)
            {
                return true;
            }

            BotOwner candidateBot = candidate.AIData?.BotOwner;
            if (candidateBot == null || candidateBot.IsDead || candidateBot.BotState != EBotState.Active)
            {
                return false;
            }

            if (BossPlayers.IsFollower(candidateBot))
            {
                return false;
            }

            WildSpawnType? role = candidate.Profile?.Info?.Settings?.Role;
            if (role.HasValue && Props.friendlyBotTypes.Contains(role.Value))
            {
                return false;
            }

            if (bossGroup?.Contains(candidateBot) == true)
            {
                return false;
            }

            bool alreadyHostile = bossGroup?.IsEnemy(candidate) == true ||
                                   bossGroup?.IsPlayerEnemy(candidate) == true ||
                                   candidateBot.BotsGroup?.IsEnemy(requester) == true ||
                                   candidateBot.BotsGroup?.IsPlayerEnemy(requester) == true ||
                                   candidateBot.Memory?.GoalEnemy?.ProfileId == requester.ProfileId;

            bool samePmcSide = candidate.Side == requester.Side &&
                               (candidate.Side == EPlayerSide.Bear || candidate.Side == EPlayerSide.Usec);
            if (samePmcSide && Utils.Utils.FlagGet("pitFireTeam") && !Utils.Utils.FlagGet("isBadGuy") && !alreadyHostile)
            {
                return false;
            }

            if (role.HasValue)
            {
                List<WildSpawnType> protectedBossAllies = Props.BossFollowersType.ToList();
                protectedBossAllies.Add(WildSpawnType.exUsec);
                if (protectedBossAllies.Contains(role.Value) &&
                    !alreadyHostile &&
                    Utils.Utils.PlayerHasKnightQuest(realPlayer.Profile))
                {
                    return false;
                }
            }

            return true;
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

        private static bool CanFollowerSeeEnemyForContact(BotOwner follower, Player enemy)
        {
            if (follower == null || enemy == null)
            {
                return false;
            }

            Vector3 firePos = follower.PlayerBones?.WeaponRoot?.position ?? (follower.Position + Vector3.up * 1.2f);
            return CanEnemyBeSeenForContact(enemy, firePos);
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

        private static bool CanReactToBossPhrase(BotOwner follower, IPlayer requester, float maxDistance)
        {
            if (follower == null || requester == null) return false;
            if (follower.IsDead || follower.BotState != EBotState.Active) return false;

            return (follower.Position - requester.Position).sqrMagnitude <= maxDistance * maxDistance;
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
                if (!IsCombatRegroupContext())
                {
                    followerData?.SetCanPatrol(false);
                }
                followerData?.ClearCommand("Attention:Look");
                followerData?.ClearTemporaryCombatAggressionOverride();

                FollowerEnemyEnforceSuppression.Suppress(follower, enforceBlockSeconds);

                ClearEnemyStateForAttention(follower, clearedGroupIds);

                if (pitFireTeam.UseSainFollowerCombat)
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

                if (follower.Mover?.TargetPose < 0.85f)
                {
                    follower.SetPose(1f);
                }

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

            FollowerContactEnemyRetention.ClearAndAllowNextGoalClear(follower);
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
                // Focused hold path: if the boss is looking at one valid follower, command only that follower.
                BotOwner lookedFollower = FindLookedAtFollower(requesterPlayer, HoldGestureDistance);
                if (lookedFollower != null)
                {
                    bool applied = false;
                    if (!lookedFollower.IsDead &&
                        lookedFollower.BotState == EBotState.Active &&
                        CanReactToBossGesture(lookedFollower, requesterPlayer, HoldGestureDistance))
                    {
                        BotFollowerPlayer lookedFollowerData = BossPlayers.Instance?.GetFollower(lookedFollower);
                        if (lookedFollowerData != null)
                        {
                            applied = ApplyHoldIntent(lookedFollower, lookedFollowerData, crouch: true, source: "HoldGesture");
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
                if ((follower.Position - requester.Position).sqrMagnitude > HoldGestureDistance * HoldGestureDistance) continue;
                if (!CanReactToBossGesture(follower, requester, HoldGestureDistance)) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                ApplyHoldIntent(follower, followerData, crouch: true, source: "HoldGesture");
            }
        }

        private void ApplyStopPhrase(IPlayer requester)
        {
            if (requester == null) return;

            if (requester is Player requesterPlayer)
            {
                // Stop phrase uses the same HoldPosition command as hold gesture, but leaves crouch unchanged.
                BotOwner lookedFollower = FindLookedAtFollower(requesterPlayer, StopPhraseDistance);
                if (lookedFollower != null)
                {
                    bool applied = false;
                    if (!lookedFollower.IsDead &&
                        lookedFollower.BotState == EBotState.Active &&
                        CanReactToBossPhrase(lookedFollower, requesterPlayer, StopPhraseDistance))
                    {
                        BotFollowerPlayer lookedFollowerData = BossPlayers.Instance?.GetFollower(lookedFollower);
                        if (lookedFollowerData != null)
                        {
                            applied = ApplyHoldIntent(lookedFollower, lookedFollowerData, crouch: false, source: "StopPhrase");
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
                if (!CanReactToBossPhrase(follower, requester, StopPhraseDistance)) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                ApplyHoldIntent(follower, followerData, crouch: false, source: "StopPhrase");
            }
        }

        private static bool ApplyHoldIntent(BotOwner follower, BotFollowerPlayer followerData, bool crouch, string source)
        {
            if (follower == null || followerData == null)
            {
                return false;
            }

            bool combatContext =
                follower.Memory?.HaveEnemy == true ||
                HasActiveCombatEnemy(follower) ||
                followerData.HasCombatHandoffSignal();
            if (combatContext)
            {
                if (followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                    command == FollowerCommandType.PushEnemy)
                {
                    followerData.ClearCommand($"{source}:combatHoldReplacePush");
                }

                // Request-layer HoldPosition is intentionally blocked while combat/handoff signals
                // are still active. Route gesture/Stop hold intent through the same temporary
                // low-aggression path as the Hold Position phrase so combat remains the owner.
                followerData.SetTemporaryCombatAggressionOverride(0f);
            }
            else
            {
                followerData.ClearTemporaryCombatAggressionOverride();
                followerData.SetHoldPosition(float.PositiveInfinity, crouch);
            }

            follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            return true;
        }

        private void ApplyRegroupCommand(IPlayer requester)
        {
            if (requester == null) return;

            bool combatRegroupContext = IsCombatRegroupContext();
            Vector3 bossPos = requester.Position;
            bool useSainRegroupRoute = pitFireTeam.ShouldUseSainRegroupRoute(combatRegroupContext);
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;
                if (!combatRegroupContext)
                {
                    followerData.SetCanPatrol(false);
                }
                else
                {
                    followerData.SetCombatRegroupBossAnchor(true);
                }

                if (useSainRegroupRoute)
                {
                    // SAIN combat regroup path: clear/prepare SAIN decision state, then mark a regroup command
                    // for the addon combat layer to pick up instead of the core request movement action.
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

                // Core regroup path: FollowerRequestLayer consumes SetRegroup and runs the vanilla/core regroup action.
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

        private void ApplyOnYourOwnCommand(IPlayer requester)
        {
            if (requester == null) return;

            bool combatContext = IsCombatRegroupContext();
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                if (combatContext)
                {
                    followerData.SetCombatIndependent(true);
                }
                else
                {
                    followerData.ClearCommand("OnYourOwn:Patrol");
                    followerData.ClearTemporaryCombatAggressionOverride();
                    followerData.SetCanPatrol(true);
                }
                follower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            }
        }

        private void ApplyCoverMeCommandDrop(IPlayer requester)
        {
            if (requester == null)
            {
                return;
            }

            bool combatContext = IsCombatRegroupContext();
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                if (combatContext)
                {
                    followerData.SetCombatIndependent(false);
                }
                else
                {
                    followerData.SetCanPatrol(false);
                }
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

            BotOwner closestFollower = FindClosestEligibleInteractionFollower(lootItem.transform.position);

            if (closestFollower == null)
            {
                return;
            }

            if (!InteractableObjects.SetTaker(closestFollower, lootItem))
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

            // Loot command path: reserve the world loot target, then store a timed TakeLootItem command
            // so GestureCommandAction can move to the item and transfer it.
            closestFollowerData.SetTakeLootItem(35f);
            closestFollower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ApplyTakeBodyGearCommand(IPlayer requester)
        {
            if (requester == null)
            {
                return;
            }

            Corpse corpse = InteractableObjects.GetCurBodyLootTarget();
            if (corpse == null)
            {
                return;
            }

            BotOwner closestFollower = FindClosestEligibleInteractionFollower(corpse.transform.position);
            if (closestFollower == null)
            {
                return;
            }

            if (!InteractableObjects.SetBodyLootTaker(closestFollower, corpse))
            {
                closestFollower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                return;
            }

            BotFollowerPlayer closestFollowerData = BossPlayers.Instance?.GetFollower(closestFollower);
            if (closestFollowerData == null)
            {
                InteractableObjects.RemoveBodyLootTaker(closestFollower);
                return;
            }

            // Body looting can take multiple inventory transactions, so reserve the corpse and
            // let the request action own the approach/interruption/cleanup lifecycle.
            closestFollowerData.SetTakeBodyGear(75f);
            closestFollower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private BotOwner FindClosestEligibleInteractionFollower(Vector3 targetPosition)
        {
            BotOwner closestFollower = null;
            float bestSqrDistance = float.MaxValue;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                if (follower.Memory?.HaveEnemy == true || HasActiveCombatEnemy(follower))
                {
                    continue;
                }

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null)
                {
                    continue;
                }

                float sqrDistance = (follower.Position - targetPosition).sqrMagnitude;
                if (sqrDistance >= bestSqrDistance)
                {
                    continue;
                }

                closestFollower = follower;
                bestSqrDistance = sqrDistance;
            }

            return closestFollower;
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

                if (follower.Memory?.HaveEnemy == true || HasActiveCombatEnemy(follower))
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

            // Door command path: reserve the door opener, then store a timed OpenDoor command for request action execution.
            closestFollowerData.SetOpenDoor(12f);
            closestFollower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ClearFollowerCommands(IPlayer requester = null)
        {
            Player requesterPlayer = requester as Player;
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                followerData.ClearVanillaRequestState(requesterPlayer, "ClearFollowerCommands");
                followerData.ClearTemporaryCombatAggressionOverride();
                followerData.ClearCommand("ClearFollowerCommands");
                followerData.SetCanPatrol(false);
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

            if (HasActiveCombatEnemy(lookedFollower))
            {
                followerData.SetCombatComeToBossCover(8f);
                lookedFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
                return;
            }

            // Come-with-me is a targeted short command; after arrival the follower returns to normal patrol.
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

                closestFollower = follower;
                bestDist = sqrDist;
            }

            if (closestFollower == null)
            {
                return;
            }

            BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(closestFollower);
            if (followerData == null)
            {
                return;
            }

            bool combatCommand = HasActiveCombatEnemy(closestFollower);
            if (combatCommand && IsPickedUpFollower(followerData))
            {
                closestFollower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                closestFollower.Gesture.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            Vector3 commandTarget;
            bool hasTarget = combatCommand
                ? TryGetGoToCommandTarget(requester, CombatThereMaxDistance, out commandTarget)
                : TryGetGoToCommandTarget(requester, out commandTarget);
            if (!hasTarget)
            {
                return;
            }

            if (combatCommand &&
                (commandTarget - requester.Position).sqrMagnitude > CombatThereMaxDistance * CombatThereMaxDistance)
            {
                closestFollower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                closestFollower.Gesture.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            ApplyFollowerLookOverride(closestFollower, commandTarget);

            if (combatCommand)
            {
                followerData.SetCombatMoveToPointTactical(commandTarget, 8f);
                closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
                return;
            }

            if (CanAcceptThereCommand(closestFollower))
            {
                // There gesture path: sampled world point -> MoveToPoint command -> GestureCommandAction movement.
                followerData.SetMoveToPoint(commandTarget, 0f);
            }

            closestFollower.Gesture.TryGestus(EInteraction.OkGesture, false);
        }

        private void ApplyDirectionalLookPhrase(IPlayer requester, EPhraseTrigger phrase)
        {
            if (!TryGetDirectionalLookTarget(requester, phrase, out Vector3 lookTarget))
            {
                return;
            }

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (!CanReactToBossPhrase(follower, requester, PhraseCommandDistance)) continue;

                ApplyFollowerLookOverride(follower, lookTarget, 1f, 2f);
            }
        }

        private static bool TryGetDirectionalLookTarget(IPlayer requester, EPhraseTrigger phrase, out Vector3 lookTarget)
        {
            Vector3 direction = GetPlanarLookDirection(requester);
            if (direction.sqrMagnitude <= 0.001f)
            {
                lookTarget = Vector3.zero;
                return false;
            }

            switch (phrase)
            {
                case EPhraseTrigger.InTheFront:
                    break;

                case EPhraseTrigger.LeftFlank:
                    direction = Quaternion.Euler(0f, -90f, 0f) * direction;
                    break;

                case EPhraseTrigger.RightFlank:
                    direction = Quaternion.Euler(0f, 90f, 0f) * direction;
                    break;

                case EPhraseTrigger.OnSix:
                    direction = -direction;
                    break;

                default:
                    lookTarget = Vector3.zero;
                    return false;
            }

            lookTarget = GetLookTargetFromDirection(requester, direction);
            return true;
        }

        private static Vector3 GetLookTargetFromDirection(IPlayer requester, Vector3 direction)
        {
            Vector3 planarDirection = direction;
            planarDirection.y = 0f;
            if (planarDirection.sqrMagnitude <= 0.001f)
            {
                planarDirection = requester?.Transform?.forward ?? Vector3.forward;
                planarDirection.y = 0f;
            }

            if (planarDirection.sqrMagnitude <= 0.001f)
            {
                planarDirection = Vector3.forward;
            }

            return requester.Transform.position + planarDirection.normalized * ContactLookDistance;
        }

        private static Vector3 GetPlanarLookDirection(IPlayer requester)
        {
            if (requester == null)
            {
                return Vector3.zero;
            }

            Vector3 direction = requester.LookDirection;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                return direction.normalized;
            }

            var transform = requester.Transform;
            if (transform == null)
            {
                return Vector3.zero;
            }

            direction = transform.forward;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.zero;
        }

        private static bool ApplyFollowerLookOverride(BotOwner follower, Vector3 lookTarget, float minDuration = CommandLookOverrideMinSeconds, float maxDuration = CommandLookOverrideMaxSeconds)
        {
            if (follower == null)
            {
                return false;
            }

            float duration = Utils.Utils.Random(minDuration, maxDuration);
            return BotFollowerPlayer.TrySetCommandLookOverride(follower, lookTarget, duration);
        }

        private void ApplyHoldPositionCombatAggression(IPlayer requester)
        {
            ApplyTemporaryCombatAggressionCommand(requester, 0f, EPhraseTrigger.Roger);
        }

        private void ApplyGoGoGoCombatAggression(IPlayer requester)
        {
            ApplyTemporaryCombatAggressionCommand(requester, null, EPhraseTrigger.Roger);
        }

        private void ApplyTemporaryCombatAggressionCommand(IPlayer requester, float? overrideAggression, EPhraseTrigger response)
        {
            if (requester == null) return;

            BotOwner focusedFollower = null;
            if (requester is Player requesterPlayer)
            {
                focusedFollower = FindLookedAtFollower(requesterPlayer, PhraseCommandDistance, 0.15f, 4f);
            }

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (focusedFollower != null && follower != focusedFollower) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                if (overrideAggression.HasValue)
                {
                    if (followerData.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                        command == FollowerCommandType.PushEnemy)
                    {
                        followerData.ClearCommand("HoldPositionAggression");
                    }

                    // Combat hold phrase is stored as a temporary aggression override, not as a movement command.
                    followerData.SetTemporaryCombatAggressionOverride(overrideAggression.Value);
                }
                else
                {
                    // Gogogo clears the temporary override so combat logic reads the persisted tactic aggression again.
                    followerData.ClearTemporaryCombatAggressionOverride();
                }

                follower.BotTalk.TrySay(response, false);
                follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            }
        }

        private void ApplyGoForwardPhrase(IPlayer requester)
        {
            if (requester == null) return;
            if (Time.time < _nextThereGestureAt) return;
            _nextThereGestureAt = Time.time + 0.6f;

            BotOwner focusedFollower = null;
            if (requester is Player requesterPlayer)
            {
                focusedFollower = FindLookedAtFollower(requesterPlayer, PhraseCommandDistance);
            }

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (focusedFollower != null && follower != focusedFollower) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                EnemyInfo? goalEnemy = follower.Memory?.GoalEnemy;
                bool hasCombatEnemy =
                    follower.Memory?.HaveEnemy == true ||
                    (goalEnemy != null && goalEnemy.Person?.HealthController?.IsAlive == true);

                if (hasCombatEnemy)
                {
                    if (IsPickedUpFollower(followerData))
                    {
                        if (followerData.IsTemporaryCombatAggressionOverrideActive)
                        {
                            followerData.ClearTemporaryCombatAggressionOverride();
                            follower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                            follower.Gesture.TryGestus(EInteraction.OkGesture, false);
                        }
                        else
                        {
                            follower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                            follower.Gesture.TryGestus(EInteraction.NoGesture, false);
                        }

                        continue;
                    }

                    // GoForward in combat is not a movement ping; it becomes PushEnemy for combat objective routing.
                    followerData.ClearTemporaryCombatAggressionOverride();
                    followerData.SetPushEnemy(12f);
                    if (followerData.CombatTactic != FollowerCombatTactic.Marksman)
                    {
                        follower.BotTalk.TrySay(EPhraseTrigger.Going, false);
                    }
                    follower.Gesture.TryGestus(EInteraction.OkGesture, false);
                    continue;
                }

                if (!CanAcceptThereCommand(follower)) continue;
                if (!TryGetGoToCommandTarget(requester, out Vector3 commandTarget))
                {
                    return;
                }

                // Out of combat, GoForward falls back to the same MoveToPoint command as the There gesture.
                followerData.SetMoveToPoint(commandTarget, 0f);
                follower.Gesture.TryGestus(EInteraction.OkGesture, false);
            }
        }

        private void ApplySuppressPhrase(IPlayer requester)
        {
            if (requester == null) return;
            if (Time.time < _nextThereGestureAt) return;
            _nextThereGestureAt = Time.time + 0.6f;

            BotOwner focusedFollower = null;
            if (requester is Player requesterPlayer)
            {
                focusedFollower = FindLookedAtFollower(requesterPlayer, PhraseCommandDistance);
            }

            List<Player> bossVisibleEnemies = GetBossVisibleEnemiesForContact(requester);
            if (pitFireTeam.IsSAINInstalled && (bossVisibleEnemies == null || bossVisibleEnemies.Count == 0))
            {
                bossVisibleEnemies = GetSainContactFallbackEnemies(requester);
            }
            bossVisibleEnemies = PrioritizeContactEnemies(requester, bossVisibleEnemies);

            BotOwner? noSupressFollower = null;
            bool willSupress = false;

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                if (focusedFollower != null && follower != focusedFollower) continue;

                BotFollowerPlayer followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null) continue;

                if (followerData.CombatTactic == FollowerCombatTactic.Marksman)
                {
                    continue;
                }

                if (!FollowerCombatCommon.IsSuppressCapableWeapon(follower.WeaponManager?.ShootController?.Item))
                {
                    if (!willSupress) noSupressFollower = follower;
                    continue;
                }

                if (!TryEnsureSuppressEnemy(follower, bossVisibleEnemies))
                {
                    continue;
                }

                followerData.SetSuppressEnemy(6f);
                follower.BotTalk.TrySay(EPhraseTrigger.Covering, true);
                noSupressFollower = null;
                willSupress = true;
            }

            if (noSupressFollower != null)
            {
                noSupressFollower.BotTalk.TrySay(EPhraseTrigger.Negative, true);
            }
        }

        private void ApplyNeedSniperPhrase(IPlayer requester)
        {
            if (requester == null) return;

            // Seed fresh contact first, then let marksman combat convert it into a firing-position move.
            ProcessContactCommand(requester);

            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(follower);
                if (followerData == null ||
                    IsPickedUpFollower(followerData) ||
                    followerData.CombatTactic != FollowerCombatTactic.Marksman)
                {
                    continue;
                }

                if (IsMarksmanUnavailableForNeedSniper(follower))
                {
                    follower.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    follower.Gesture.TryGestus(EInteraction.NoGesture, false);
                    continue;
                }

                followerData.SetNeedSniper(10f);
                followerData.ClearTemporaryCombatAggressionOverride();
                follower.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            }
        }

        private void ApplyNeedHelpPhrase(IPlayer requester)
        {
            if (requester == null) return;

            if (!TryGetClosestNeedHelpEnemy(out BotOwner enemyBot))
            {
                return;
            }

            aBossLogic.MarkManualUnderAttack(enemyBot);
            foreach (BotOwner follower in Followers)
            {
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active) continue;
                PrioritizeEnemy(follower, enemyBot);
            }
        }

        private bool TryGetClosestNeedHelpEnemy(out BotOwner enemyBot)
        {
            enemyBot = null;
            float bestDistanceSqr = Mathf.Infinity;

            ConsiderNeedHelpEnemies(bossEnemies, ref enemyBot, ref bestDistanceSqr);

            if (bossGroup?.Enemies != null)
            {
                foreach (IPlayer enemyPlayerRef in bossGroup.Enemies.Keys)
                {
                    ConsiderNeedHelpEnemy(enemyPlayerRef?.AIData?.BotOwner, ref enemyBot, ref bestDistanceSqr);
                }
            }

            List<Player> visibleEnemies = GetBossVisibleEnemiesForContact(realPlayer);
            if (pitFireTeam.IsSAINInstalled && (visibleEnemies == null || visibleEnemies.Count == 0))
            {
                visibleEnemies = GetSainContactFallbackEnemies(realPlayer);
            }

            if (visibleEnemies != null)
            {
                foreach (Player enemy in visibleEnemies)
                {
                    ConsiderNeedHelpEnemy(enemy?.AIData?.BotOwner, ref enemyBot, ref bestDistanceSqr);
                }
            }

            return enemyBot != null;
        }

        private void ConsiderNeedHelpEnemies(
            IEnumerable<BotOwner> enemies,
            ref BotOwner closestEnemy,
            ref float bestDistanceSqr)
        {
            if (enemies == null)
            {
                return;
            }

            foreach (BotOwner enemy in enemies)
            {
                ConsiderNeedHelpEnemy(enemy, ref closestEnemy, ref bestDistanceSqr);
            }
        }

        private void ConsiderNeedHelpEnemy(
            BotOwner candidate,
            ref BotOwner closestEnemy,
            ref float bestDistanceSqr)
        {
            if (!IsValidNeedHelpEnemy(candidate))
            {
                return;
            }

            float distanceSqr = (realPlayer.Position - candidate.Position).sqrMagnitude;
            if (distanceSqr >= bestDistanceSqr)
            {
                return;
            }

            closestEnemy = candidate;
            bestDistanceSqr = distanceSqr;
        }

        private bool IsValidNeedHelpEnemy(BotOwner candidate)
        {
            if (candidate == null ||
                candidate.IsDead ||
                candidate.BotState != EBotState.Active ||
                candidate.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (candidate.ProfileId == realPlayer.ProfileId ||
                Followers.Any(follower => follower != null && follower.ProfileId == candidate.ProfileId))
            {
                return false;
            }

            if (candidate.GetPlayer?.Profile?.Info?.Settings?.Role is WildSpawnType role &&
                Props.friendlyBotTypes.Contains(role))
            {
                return false;
            }

            return true;
        }

        private static bool IsMarksmanUnavailableForNeedSniper(BotOwner follower)
        {
            return IsMarksmanBusyWithOwnFight(follower) ||
                   IsFollowerHealingOrNeedsHeal(follower);
        }

        private static bool IsFollowerHealingOrNeedsHeal(BotOwner follower)
        {
            if (follower?.Medecine == null)
            {
                return false;
            }

            ETagStatus? healthStatus = follower.GetPlayer?.HealthStatus;
            return follower.Medecine.FirstAid?.Have2Do == true ||
                   follower.Medecine.SurgicalKit?.HaveWork == true ||
                   follower.Medecine.FirstAid?.Using == true ||
                   follower.Medecine.SurgicalKit?.Using == true ||
                   follower.Medecine.Stimulators?.Using == true ||
                   healthStatus == ETagStatus.BadlyInjured ||
                   healthStatus == ETagStatus.Dying;
        }

        private static bool IsMarksmanBusyWithOwnFight(BotOwner follower)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            if (follower.DogFight?.DogFightState > BotDogFightStatus.none)
            {
                return true;
            }

            if (goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance() &&
                follower.WeaponManager?.Selector?.LastEquipmentSlot == EquipmentSlot.SecondPrimaryWeapon)
            {
                return true;
            }

            string? reason = follower.Brain?.Agent?.LastResult().Reason;
            return reason != null && Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose &&
                   (reason.StartsWith("sniper.closeSearch", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.closeAuto", StringComparison.Ordinal) ||
                    reason.StartsWith("sniper.startClose", StringComparison.Ordinal));
        }

        private bool TryEnsureSuppressEnemy(BotOwner follower, List<Player> bossVisibleEnemies)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            if (goalEnemy?.Person?.HealthController?.IsAlive == true)
            {
                return true;
            }

            if (bossVisibleEnemies == null || bossVisibleEnemies.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < bossVisibleEnemies.Count; i++)
            {
                Player enemy = bossVisibleEnemies[i];
                if (enemy == null ||
                    enemy.HealthController?.IsAlive != true ||
                    enemy.ProfileId == follower.ProfileId ||
                    enemy.ProfileId == realPlayer.ProfileId)
                {
                    continue;
                }

                if (enemy.IsAI)
                {
                    WildSpawnType? role = enemy.Profile?.Info?.Settings?.Role;
                    if (role.HasValue && Props.friendlyBotTypes.Contains(role.Value))
                    {
                        continue;
                    }
                }

                RegisterContactEnemyForFollower(follower, enemy, prioritizeAsGoal: true, allowGoalPromotion: true);
                return follower.Memory?.GoalEnemy?.Person?.HealthController?.IsAlive == true;
            }

            return false;
        }

        private static bool TryGetGoToCommandTarget(IPlayer requester, out Vector3 commandTarget)
        {
            float maxGoToDistance = pitFireTeam.goToDistance?.Value ?? DefaultGoToDistance;
            return TryGetGoToCommandTarget(requester, maxGoToDistance, out commandTarget);
        }

        private static bool TryGetGoToCommandTarget(IPlayer requester, float maxDistance, out Vector3 commandTarget)
        {
            commandTarget = Vector3.zero;
            if (requester == null)
            {
                return false;
            }

            Vector3 requesterPos = requester.Position;
            Vector3 lookDir = requester.LookDirection.sqrMagnitude > 0.001f
                ? requester.LookDirection.normalized
                : requester.Transform.forward;
            Ray interactionRay = requester is Player player ? player.InteractionRay : new Ray(requesterPos + Vector3.up * 1.5f, lookDir);
            Vector3 rayDirection = interactionRay.direction.sqrMagnitude > 0.001f
                ? interactionRay.direction.normalized
                : lookDir;
            Vector3 planarDirection = Vector3.ProjectOnPlane(rayDirection, Vector3.up);
            if (planarDirection.sqrMagnitude <= 0.001f)
            {
                planarDirection = Vector3.ProjectOnPlane(requester.Transform.forward, Vector3.up);
            }
            if (planarDirection.sqrMagnitude <= 0.001f)
            {
                planarDirection = Vector3.forward;
            }
            planarDirection.Normalize();

            maxDistance = Mathf.Max(1f, maxDistance);
            Vector3 rawTarget = requesterPos + planarDirection * maxDistance;
            bool hasSurfaceHit = TryGetCommandSurfaceHit(interactionRay, rayDirection, maxDistance, out RaycastHit lookHit);
            if (hasSurfaceHit)
            {
                rawTarget = lookHit.point;
            }

            float preferredY = hasSurfaceHit ? rawTarget.y : requesterPos.y;
            if (!TrySampleCommandNavPoint(rawTarget, preferredY, out commandTarget))
            {
                return false;
            }

            return true;
        }

        private static bool TryGetCommandSurfaceHit(Ray interactionRay, Vector3 rayDirection, float maxDistance, out RaycastHit lookHit)
        {
            if (Physics.Raycast(interactionRay.origin, rayDirection, out lookHit, maxDistance, LayerMaskClass.HighPolyWithTerrainMask))
            {
                return true;
            }

            return Physics.SphereCast(interactionRay.origin, 0.22f, rayDirection, out lookHit, maxDistance, LayerMaskClass.HighPolyWithTerrainMask);
        }

        private static bool TrySampleCommandNavPoint(Vector3 rawTarget, float preferredY, out Vector3 commandTarget)
        {
            float[] sampleRadii = { 1.5f, 3f, 5f };
            const float sameLevelTolerance = 1.9f;

            foreach (float radius in sampleRadii)
            {
                if (!NavMesh.SamplePosition(rawTarget, out NavMeshHit navHit, radius, NavMesh.AllAreas))
                {
                    continue;
                }

                if (Mathf.Abs(navHit.position.y - preferredY) > sameLevelTolerance)
                {
                    continue;
                }

                commandTarget = navHit.position;
                return true;
            }

            commandTarget = Vector3.zero;
            return false;
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
                   action != (BotLogicDecision)CustomBotDecisions.attackRetreat &&
                   action != BotLogicDecision.attackMovingFlank;
        }

        private static bool HasActiveCombatEnemy(BotOwner follower)
        {
            EnemyInfo? goalEnemy = follower?.Memory?.GoalEnemy;
            return goalEnemy?.Person?.HealthController?.IsAlive == true;
        }

        private static bool IsPickedUpFollower(BotFollowerPlayer? followerData)
        {
            return followerData != null && !followerData.IsSquadMate;
        }

        private BotOwner FindLookedAtFollower(Player requester, float distance)
        {
            return FindLookedAtFollower(requester, distance, 0.4f, 10f);
        }

        private BotOwner FindLookedAtFollower(Player requester, float distance, float sphereRadius, float maxAngleDegrees)
        {
            if (requester == null) return null;

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

                if (IsLookAlignedWithFollower(requester, bot, maxAngleDegrees) &&
                    CanBossSeeFollowerGestureTarget(bot, requester))
                {
                    return bot;
                }
            }

            return null;
        }

        private static bool IsLookAlignedWithFollower(Player requester, BotOwner follower, float maxAngleDegrees)
        {
            if (requester == null || follower == null)
            {
                return false;
            }

            Ray ray = requester.InteractionRay;
            Vector3 target = follower.Position + Vector3.up * 1.1f;
            if (follower.GetPlayer?.MainParts != null &&
                follower.GetPlayer.MainParts.TryGetValue(BodyPartType.body, out var bodyPart))
            {
                target = bodyPart.Position;
            }

            Vector3 toFollower = target - ray.origin;
            if (toFollower.sqrMagnitude < 0.01f)
            {
                return false;
            }

            return Vector3.Angle(ray.direction, toFollower.normalized) <= maxAngleDegrees;
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
                    Enemy.RepairPersonalMemory(info, enemy.Position, info.IsVisible || info.CanShoot || info.HaveSeen);
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
                        if (info != null)
                        {
                            Enemy.RepairPersonalMemory(info, enemy.Position, info.IsVisible || info.CanShoot || info.HaveSeen);
                            follower.Memory.GoalEnemy = info;
                        }
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

                for (int i = bossEnemies.Count - 1; i >= 0; i--)
                {
                    BotOwner item = bossEnemies[i];
                    if (!IsValidNeedHelpEnemy(item))
                    {
                        bossEnemies.RemoveAt(i);
                        continue;
                    }

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
            CombatEvents.Clear();
            StopFriendlyDownTimerIfIdle();
            aBossLogic.Dispose();

            Modules.Logger.LogInfo("Player Boss Disposed");
        }

        private void SubscribeBossGroupStaticUpdate()
        {
            if (_bossGroupStaticUpdateSubscribed || _group == null || StaticManager.Instance == null)
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
            if (pitFireTeam.UseSainFollowerCombat)
            {
                SainAddonBridge.RaiseBossGroupStaticUpdate(this);
            }
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
                if (!ShouldSyncFollowerWithReportedEnemy(follower, bestReporter, enemyPlayer))
                {
                    continue;
                }

                // Only refill idle followers with no current goal. Do not replace or refresh an
                // existing GoalEnemy here; the follower's own combat state owns that.
                RegisterContactEnemyForFollower(follower, enemyPlayer, false, true);
            }
        }

        private static bool ShouldSyncFollowerWithReportedEnemy(BotOwner follower, BotOwner reporter, Player enemyPlayer)
        {
            if (!IsFollowerEligibleForGroupEnemySync(follower) ||
                FollowerEnemyEnforceSuppression.IsSuppressed(follower) ||
                follower == reporter ||
                enemyPlayer == null ||
                string.IsNullOrEmpty(enemyPlayer.ProfileId))
            {
                return false;
            }

            return follower.Memory?.GoalEnemy == null;
        }

        private bool TryGetStableEnemyReporter(BotOwner follower, out Player enemyPlayer, out EEnemyPartVisibleType visibleType)
        {
            enemyPlayer = null;
            visibleType = EEnemyPartVisibleType.Sence;

            if (!IsFollowerEligibleForGroupEnemySync(follower) ||
                FollowerEnemyEnforceSuppression.IsSuppressed(follower))
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

        public void MarkManualUnderAttack(BotOwner enemyBot)
        {
            if (enemyBot == null ||
                enemyBot.IsDead ||
                enemyBot.BotState != EBotState.Active ||
                _aiplayer == null)
            {
                return;
            }

            _lastTimeHit = Time.time;
            try
            {
                if (_aiplayer.bossGroup == null)
                {
                    _aiplayer.AddEnemy(enemyBot);
                    return;
                }

                _aiplayer.AddEnemy(enemyBot);
                BotOwner followerBotOwner = _aiplayer.Followers.FirstOrDefault();
                _aiplayer.bossGroup.AddEnemy(enemyBot, EBotEnemyCause.addPlayerToBoss);
                if (followerBotOwner != null)
                {
                    _aiplayer.bossGroup.ReportAboutEnemy(enemyBot, EEnemyPartVisibleType.Sence, followerBotOwner);
                }
            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Failed to mark manual boss under attack");
                Modules.Logger.LogError(e);
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
