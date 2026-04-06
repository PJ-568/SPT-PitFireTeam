using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.Components
{
    internal enum CombatManagerEngagerType
    {
        None = 0,
        Player = 1,
        Follower = 2,
    }

    internal enum CombatManagerMode
    {
        None = 0,
        Support = 1,
        Search = 2,
    }

    internal sealed class FollowerCombatManagerComponent : MonoBehaviour
    {
        private sealed class CombatManagerState
        {
            public CombatManagerMode Mode { get; set; }
            public CombatManagerEngagerType CurrentEngagerType { get; set; }
            public string? CurrentEngagerProfileId { get; set; }
            public Vector3 CurrentEngagerPosition { get; set; }
            public string? TargetEnemyProfileId { get; set; }
            public Vector3 TargetEnemyPosition { get; set; }
            public string? SearchLeaderProfileId { get; set; }
            public float CommitUntilTime { get; set; }
            public float NextEvaluateAt { get; set; }
        }

        private sealed class SupportContext
        {
            public pitAIBossPlayer Boss { get; set; } = null!;
            public List<BotFollowerPlayer> Followers { get; set; } = null!;
            public string EnemyProfileId { get; set; } = string.Empty;
            public Vector3 EnemyPosition { get; set; }
            public CombatManagerEngagerType EngagerType { get; set; }
            public string? EngagerProfileId { get; set; }
            public Vector3 EngagerPosition { get; set; }
        }

        private sealed class SearchContext
        {
            public pitAIBossPlayer Boss { get; set; } = null!;
            public List<BotFollowerPlayer> Followers { get; set; } = null!;
            public string EnemyProfileId { get; set; } = string.Empty;
            public Vector3 EnemyPosition { get; set; }
            public BotFollowerPlayer Leader { get; set; } = null!;
            public Vector3 LeaderPoint { get; set; }
        }

        public static FollowerCombatManagerComponent? Instance { get; private set; }

        public GameWorld? GameWorld { get; private set; }

        private readonly Dictionary<string, CombatManagerState> _stateByBossProfileId =
            new Dictionary<string, CombatManagerState>(StringComparer.Ordinal);

        private bool _disposing;

        private const float EvaluationInterval = 0.35f;
        private const float SupportCommitWindow = 2.5f;
        private const float SupportGatherDistance = 50f;
        private const float SlowSupportGatherDistance = 32f;
        private const float SearchGatherDistance = 50f;
        private const float SearchFollowerJoinDistance = 25f;
        private const float AttackMovingRunThreshold = 30f;
        private const float NearBossThreatDistance = 18f;
        private const float CloseAllySupportDistance = 22f;
        private const float CloseAllyThreatDistance = 20f;
        private const float RecentOwnEnemyLockout = 2f;
        private const float ProtectorCoverRadius = 16f;
        private const float SupportCoverRadius = 26f;
        private const float MarksmanCoverRadius = 35f;
        private const float SearchLeaderRadius = 30f;
        private const float EnemyDangerDistance = 12f;
        private const float SupportSideMaxDot = 0.55f;
        private const float SupportFrontMaxDot = 0.72f;
        private const float SupportBackMaxDot = -0.55f;
        private const float EnemyGroupDangerCount = 2f;
        private const float SearchFollowerSpacing = 2.5f;
        private const float FloorRunDelta = 2.5f;

        public void Activate(GameWorld gameWorld)
        {
            if (_disposing)
            {
                return;
            }

            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Instance.Dispose();
            }

            Instance = this;
            GameWorld = gameWorld;
            if (GameWorld == null)
            {
                Dispose();
                return;
            }

            GameWorld.OnDispose -= Dispose;
            GameWorld.OnDispose += Dispose;
        }

        public void WorldTick(float deltaTime)
        {
            if (_disposing)
            {
                return;
            }

            try
            {
                ManualUpdate(Time.time, deltaTime);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[FollowerCombatManager] WorldTick failed: {ex}");
            }
        }

        public void ManualUpdate(float currentTime, float deltaTime)
        {
            if (_disposing)
            {
                return;
            }

            UpdateBossManagers(currentTime);
        }

        public List<BotFollowerPlayer> GetFollowersByTactic(FollowerCombatTactic tactic)
        {
            List<BotFollowerPlayer> followers = BossPlayers.GetFollowers();
            if (followers.Count == 0)
            {
                return followers;
            }

            List<BotFollowerPlayer> result = new List<BotFollowerPlayer>();
            for (int i = 0; i < followers.Count; i++)
            {
                BotFollowerPlayer follower = followers[i];
                if (follower != null && follower.CombatTactic == tactic)
                {
                    result.Add(follower);
                }
            }

            return result;
        }

        private void UpdateBossManagers(float currentTime)
        {
            Dictionary<string, pitAIBossPlayer> bosses = BossPlayers.GetBosses();
            HashSet<string> activeBossIds = new HashSet<string>(StringComparer.Ordinal);

            foreach ((string bossProfileId, pitAIBossPlayer boss) in bosses)
            {
                if (string.IsNullOrEmpty(bossProfileId) || boss?.realPlayer == null)
                {
                    continue;
                }

                activeBossIds.Add(bossProfileId);
                List<BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(bossProfileId)
                    .Where(IsEligibleFollower)
                    .OrderBy(f => f.GetBot()?.ProfileId, StringComparer.Ordinal)
                    .ToList();

                if (followers.Count == 0)
                {
                    ClearBossManagerState(bossProfileId, "noFollowers");
                    continue;
                }

                CombatManagerState state = GetOrCreateState(bossProfileId);
                if (state.NextEvaluateAt > currentTime)
                {
                    continue;
                }

                state.NextEvaluateAt = currentTime + EvaluationInterval;

                if (TryBuildSupportContext(boss, followers, state, out SupportContext? supportContext))
                {
                    ApplySupportOrders(state, supportContext);
                    continue;
                }

                if (TryBuildSearchContext(boss, followers, state, out SearchContext? searchContext))
                {
                    ApplySearchOrders(state, searchContext);
                    continue;
                }

                ClearFollowerManagerOrders(followers, "managerIdle");
                _stateByBossProfileId.Remove(bossProfileId);
            }

            foreach (string staleBossId in _stateByBossProfileId.Keys.Where(id => !activeBossIds.Contains(id)).ToList())
            {
                ClearBossManagerState(staleBossId, "bossMissing");
            }
        }

        private CombatManagerState GetOrCreateState(string bossProfileId)
        {
            if (_stateByBossProfileId.TryGetValue(bossProfileId, out CombatManagerState? state))
            {
                return state;
            }

            state = new CombatManagerState();
            _stateByBossProfileId[bossProfileId] = state;
            return state;
        }

        private bool TryBuildSupportContext(
            pitAIBossPlayer boss,
            List<BotFollowerPlayer> followers,
            CombatManagerState state,
            out SupportContext? context)
        {
            context = null;
            if (boss?.realPlayer == null)
            {
                return false;
            }

            Vector3 bossPosition = boss.Position;
            if (boss.IsPlayerEngaging(out string playerEnemyProfileId, out Vector3 playerEnemyPosition))
            {
                state.Mode = CombatManagerMode.Support;
                state.CurrentEngagerType = CombatManagerEngagerType.Player;
                state.CurrentEngagerProfileId = boss.realPlayer.ProfileId;
                state.CurrentEngagerPosition = bossPosition;
                state.TargetEnemyProfileId = playerEnemyProfileId;
                state.TargetEnemyPosition = playerEnemyPosition;
                state.CommitUntilTime = Time.time + SupportCommitWindow;
                state.SearchLeaderProfileId = null;

                context = new SupportContext
                {
                    Boss = boss,
                    Followers = followers,
                    EnemyProfileId = playerEnemyProfileId,
                    EnemyPosition = playerEnemyPosition,
                    EngagerType = CombatManagerEngagerType.Player,
                    EngagerProfileId = boss.realPlayer.ProfileId,
                    EngagerPosition = bossPosition,
                };
                return true;
            }

            BotOwner? selectedFollower = null;
            string selectedEnemyProfileId = string.Empty;
            Vector3 selectedEnemyPosition = Vector3.zero;
            float bestPriority = float.MinValue;
            float bestBossDistanceSqr = float.MaxValue;

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner bot = followers[i].GetBot();
                if (bot == null ||
                    !TryGetFollowerSupportEnemy(bot, bossPosition, out string enemyProfileId, out Vector3 enemyPosition, out float supportPriority))
                {
                    continue;
                }

                float bossDistanceSqr = (bot.Position - bossPosition).sqrMagnitude;
                if (selectedFollower == null ||
                    supportPriority > bestPriority + 0.01f ||
                    (Mathf.Abs(supportPriority - bestPriority) <= 0.01f && bossDistanceSqr < bestBossDistanceSqr))
                {
                    selectedFollower = bot;
                    selectedEnemyProfileId = enemyProfileId;
                    selectedEnemyPosition = enemyPosition;
                    bestPriority = supportPriority;
                    bestBossDistanceSqr = bossDistanceSqr;
                }
            }

            if (selectedFollower != null)
            {
                state.Mode = CombatManagerMode.Support;
                state.CurrentEngagerType = CombatManagerEngagerType.Follower;
                state.CurrentEngagerProfileId = selectedFollower.ProfileId;
                state.CurrentEngagerPosition = selectedFollower.Position;
                state.TargetEnemyProfileId = selectedEnemyProfileId;
                state.TargetEnemyPosition = selectedEnemyPosition;
                state.CommitUntilTime = Time.time + SupportCommitWindow;
                state.SearchLeaderProfileId = null;

                context = new SupportContext
                {
                    Boss = boss,
                    Followers = followers,
                    EnemyProfileId = selectedEnemyProfileId,
                    EnemyPosition = selectedEnemyPosition,
                    EngagerType = CombatManagerEngagerType.Follower,
                    EngagerProfileId = selectedFollower.ProfileId,
                    EngagerPosition = selectedFollower.Position,
                };
                return true;
            }

            if (Time.time <= state.CommitUntilTime &&
                state.Mode == CombatManagerMode.Support &&
                !string.IsNullOrEmpty(state.TargetEnemyProfileId) &&
                IsFinite(state.TargetEnemyPosition))
            {
                context = new SupportContext
                {
                    Boss = boss,
                    Followers = followers,
                    EnemyProfileId = state.TargetEnemyProfileId,
                    EnemyPosition = state.TargetEnemyPosition,
                    EngagerType = state.CurrentEngagerType,
                    EngagerProfileId = state.CurrentEngagerProfileId,
                    EngagerPosition = state.CurrentEngagerPosition,
                };
                return true;
            }

            return false;
        }

        private bool TryBuildSearchContext(
            pitAIBossPlayer boss,
            List<BotFollowerPlayer> followers,
            CombatManagerState state,
            out SearchContext? context)
        {
            context = null;
            if (boss?.realPlayer == null)
            {
                return false;
            }

            string enemyProfileId = string.Empty;
            Vector3 enemyPosition = Vector3.zero;
            float bestDistance = float.MaxValue;
            bool anySearchRequest = false;
            List<BotFollowerPlayer> requestingFollowers = new List<BotFollowerPlayer>();

            for (int i = 0; i < followers.Count; i++)
            {
                BotFollowerPlayer follower = followers[i];
                BotOwner bot = follower.GetBot();
                if (!follower.TryGetPendingManagerSearchRequest(out string? requestedEnemyProfileId))
                {
                    continue;
                }

                anySearchRequest = true;
                requestingFollowers.Add(follower);
                EnemyInfo? goalEnemy = bot?.Memory?.GoalEnemy;
                if (!IsValidManagerSearchEnemy(bot, goalEnemy))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(requestedEnemyProfileId) &&
                    !string.Equals(requestedEnemyProfileId, goalEnemy.ProfileId, StringComparison.Ordinal))
                {
                    continue;
                }

                float navDistance = Utils.Utils.GetNavDistance(boss.Position, goalEnemy.CurrPosition);
                if (navDistance > SearchGatherDistance)
                {
                    continue;
                }

                if (navDistance < bestDistance)
                {
                    bestDistance = navDistance;
                    enemyProfileId = goalEnemy.ProfileId;
                    enemyPosition = goalEnemy.CurrPosition;
                }
            }

            if (!anySearchRequest || string.IsNullOrEmpty(enemyProfileId))
            {
                return false;
            }

            BotFollowerPlayer? leader = SelectSearchLeader(requestingFollowers, enemyPosition, state.SearchLeaderProfileId);
            if (leader == null || !TryFindSearchLeaderPoint(leader.GetBot(), boss.Position, enemyPosition, out Vector3 leaderPoint))
            {
                return false;
            }

            state.Mode = CombatManagerMode.Search;
            state.CurrentEngagerType = CombatManagerEngagerType.None;
            state.CurrentEngagerProfileId = null;
            state.CurrentEngagerPosition = boss.Position;
            state.TargetEnemyProfileId = enemyProfileId;
            state.TargetEnemyPosition = enemyPosition;
            state.SearchLeaderProfileId = leader.GetBot()?.ProfileId;
            state.CommitUntilTime = Time.time + SupportCommitWindow;

            context = new SearchContext
            {
                Boss = boss,
                Followers = followers,
                EnemyProfileId = enemyProfileId,
                EnemyPosition = enemyPosition,
                Leader = leader,
                LeaderPoint = leaderPoint,
            };
            return true;
        }

        private void ApplySupportOrders(CombatManagerState state, SupportContext context)
        {
            Vector3 bossPosition = context.Boss.Position;
            bool bossIsEngager = context.EngagerType == CombatManagerEngagerType.Player;
            bool engagementNearBoss = (context.EnemyPosition - bossPosition).sqrMagnitude <= NearBossThreatDistance * NearBossThreatDistance;
            HashSet<string> assignedFollowers = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < context.Followers.Count; i++)
            {
                BotFollowerPlayer follower = context.Followers[i];
                BotOwner bot = follower.GetBot();
                if (bot == null)
                {
                    continue;
                }

                string profileId = bot.ProfileId ?? string.Empty;
                if (string.IsNullOrEmpty(profileId))
                {
                    continue;
                }

                if (follower.IsBotActivelyEngaging(context.EnemyProfileId))
                {
                    follower.ClearManagerCombatDecision("supportOwnFight");
                    continue;
                }

                if (!CanFollowerReceiveSupportOrder(bot, context.EngagerProfileId, context.EngagerPosition) ||
                    ShouldInterruptSupportOrder(bot, context.EnemyProfileId))
                {
                    follower.ClearManagerCombatDecision("supportInterrupted");
                    continue;
                }

                FollowerCombatTactic followerType = follower.CombatTactic;
                if (TryAssignSupportFireFromCurrentPosition(
                    follower,
                    bot,
                    followerType,
                    context.EnemyProfileId,
                    context.EnemyPosition,
                    engagementNearBoss,
                    bossIsEngager))
                {
                    assignedFollowers.Add(profileId);
                    continue;
                }

                if (!TryFindSupportPosition(bot, followerType, bossPosition, context.EngagerPosition, context.EnemyProfileId, context.EnemyPosition, out CustomNavigationPoint? supportCover, out Vector3 supportPoint))
                {
                    follower.ClearManagerCombatDecision("supportNoPoint");
                    continue;
                }

                float navDistance = Utils.Utils.GetNavDistance(bot.Position, supportPoint);
                bool needsRunToCover = navDistance > AttackMovingRunThreshold || Mathf.Abs(bot.Position.y - supportPoint.y) > FloorRunDelta;
                bool canSuppress = CanUseAutomaticSupportWeapon(bot, followerType, engagementNearBoss, bossIsEngager);

                bot.Memory.SetCoverPoints(supportCover);
                follower.SetManagerCombatDecision(
                    needsRunToCover ? BotLogicDecision.runToCover : (canSuppress ? BotLogicDecision.attackMovingWithSuppress : BotLogicDecision.attackMoving),
                    needsRunToCover ? "support:runToCover" : (canSuppress ? "support:attackMovingSuppress" : "support:attackMoving"),
                    context.EnemyProfileId,
                    supportPoint);
                assignedFollowers.Add(profileId);
            }

            ClearUnassignedFollowers(context.Followers, assignedFollowers, "supportNotSelected");
        }

        private void ApplySearchOrders(CombatManagerState state, SearchContext context)
        {
            HashSet<string> assignedFollowers = new HashSet<string>(StringComparer.Ordinal);
            BotOwner? leaderBot = context.Leader.GetBot();
            if (leaderBot == null)
            {
                ClearFollowerManagerOrders(context.Followers, "searchNoLeader");
                return;
            }

            context.Leader.SetManagerCombatDecision(
                BotLogicDecision.search,
                "search:leader",
                context.EnemyProfileId,
                context.LeaderPoint);
            context.Leader.ClearManagerSearchRequest("searchLeaderAssigned");
            assignedFollowers.Add(leaderBot.ProfileId);

            for (int i = 0; i < context.Followers.Count; i++)
            {
                BotFollowerPlayer follower = context.Followers[i];
                BotOwner bot = follower.GetBot();
                if (bot == null || bot == leaderBot)
                {
                    continue;
                }

                if (!CanFollowerReceiveSearchOrder(bot, context.EnemyPosition, leaderBot.Position))
                {
                    follower.ClearManagerCombatDecision("searchUnavailable");
                    continue;
                }

                if (!TryGetSearchFollowerPoint(leaderBot.Position, bot.Position, out Vector3 followPoint))
                {
                    follower.ClearManagerCombatDecision("searchNoFollowPoint");
                    continue;
                }

                follower.SetManagerCombatDecision(
                    BotLogicDecision.goToPointTactical,
                    "search:follower",
                    context.EnemyProfileId,
                    followPoint);
                follower.ClearManagerSearchRequest("searchFollowerAssigned");
                assignedFollowers.Add(bot.ProfileId);
            }

            ClearUnassignedFollowers(context.Followers, assignedFollowers, "searchNotSelected");
        }

        private static void ClearUnassignedFollowers(IEnumerable<BotFollowerPlayer> followers, HashSet<string> assignedFollowers, string reason)
        {
            foreach (BotFollowerPlayer follower in followers)
            {
                BotOwner bot = follower?.GetBot();
                if (bot == null || assignedFollowers.Contains(bot.ProfileId))
                {
                    continue;
                }

                follower.ClearManagerCombatDecision(reason);
                if (reason.StartsWith("search", StringComparison.Ordinal) ||
                    string.Equals(reason, "managerIdle", StringComparison.Ordinal))
                {
                    follower.ClearManagerSearchRequest(reason);
                }
            }
        }

        private static bool TryAssignSupportFireFromCurrentPosition(
            BotFollowerPlayer follower,
            BotOwner bot,
            FollowerCombatTactic followerType,
            string enemyProfileId,
            Vector3 enemyPosition,
            bool engagementNearBoss,
            bool bossIsEngager)
        {
            if (!CanSupportFromCurrentPosition(bot, enemyProfileId))
            {
                return false;
            }

            if (FollowerShotSafety.IsFriendlyInShotLane(bot, enemyPosition))
            {
                return false;
            }

            if (followerType == FollowerCombatTactic.Marksman && !CanUseAutomaticSupportWeapon(bot, followerType, engagementNearBoss, bossIsEngager))
            {
                follower.SetManagerCombatDecision(
                    BotLogicDecision.shootFromPlace,
                    "support:marksmanShot",
                    enemyProfileId);
                return true;
            }

            follower.SetManagerCombatDecision(
                BotLogicDecision.suppressFire,
                "support:suppressFire",
                enemyProfileId);
            return true;
        }

        private static bool TryGetFollowerSupportEnemy(BotOwner bot, Vector3 bossPosition, out string enemyProfileId, out Vector3 enemyPosition, out float supportPriority)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;
            supportPriority = float.MinValue;

            EnemyInfo? goalEnemy = bot?.Memory?.GoalEnemy;
            if (goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return false;
            }

            bool visibleFight = goalEnemy.IsVisible && goalEnemy.CanShoot;
            bool underHeavyThreat = bot.Memory.IsUnderFire && Time.time - bot.Memory.LastTimeHit <= 2f;
            bool inDogFight = bot.DogFight?.DogFightState > BotDogFightStatus.none;
            if (!visibleFight && !underHeavyThreat && !inDogFight)
            {
                return false;
            }

            float bossDistance = Vector3.Distance(bot.Position, bossPosition);
            float enemyDistanceToBoss = Vector3.Distance(goalEnemy.CurrPosition, bossPosition);
            bool closeAlly = bossDistance <= CloseAllySupportDistance;
            bool threatNearBoss = enemyDistanceToBoss <= CloseAllyThreatDistance;
            if (!closeAlly && !threatNearBoss)
            {
                return false;
            }

            supportPriority = (closeAlly ? 3f : 1f) +
                              (underHeavyThreat ? 3f : 0f) +
                              (inDogFight ? 2f : 0f) +
                              (visibleFight ? 1f : 0f) -
                              bossDistance * 0.05f;
            enemyProfileId = goalEnemy.ProfileId;
            enemyPosition = goalEnemy.CurrPosition;
            return IsFinite(enemyPosition);
        }

        private static bool CanSupportFromCurrentPosition(BotOwner bot, string enemyProfileId)
        {
            EnemyInfo? goalEnemy = bot?.Memory?.GoalEnemy;
            return goalEnemy != null &&
                   string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal) &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   bot.LookSensor.EnoughDistToShoot(out _);
        }

        private static bool IsFollowerUnavailable(BotOwner bot)
        {
            if (bot == null)
            {
                return true;
            }

            if (bot.Medecine?.Using == true ||
                bot.Medecine?.FirstAid?.Using == true ||
                bot.Medecine?.SurgicalKit?.Using == true)
            {
                return true;
            }

            if (bot.DogFight?.DogFightState > BotDogFightStatus.none)
            {
                return true;
            }

            BotLogicDecision action = bot.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return action == BotLogicDecision.heal ||
                   action == BotLogicDecision.healStimulators ||
                   action == BotLogicDecision.throwGrenadeFromPlace;
        }

        private static bool CanFollowerReceiveSupportOrder(BotOwner bot, string? engagerProfileId, Vector3 engagerPosition)
        {
            if (bot == null || IsFollowerUnavailable(bot))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(engagerProfileId) &&
                string.Equals(bot.ProfileId, engagerProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            EnemyInfo? currentEnemy = bot.Memory?.GoalEnemy;
            if (currentEnemy != null && Time.time - currentEnemy.PersonalSeenTime < RecentOwnEnemyLockout)
            {
                return false;
            }

            return Utils.Utils.GetNavDistance(bot.Position, engagerPosition) <= GetAllowedGatherDistance(bot);
        }

        private static bool ShouldInterruptSupportOrder(BotOwner bot, string enemyProfileId)
        {
            if (bot == null)
            {
                return true;
            }

            EnemyInfo? currentEnemy = bot.Memory?.GoalEnemy;
            if (currentEnemy != null &&
                !string.IsNullOrEmpty(currentEnemy.ProfileId) &&
                !string.Equals(currentEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal) &&
                Time.time - currentEnemy.PersonalSeenTime <= 1.5f)
            {
                return true;
            }

            if (bot.Memory.IsUnderFire && Time.time - bot.Memory.LastTimeHit <= 2f)
            {
                return true;
            }

            return GetTotalHealthPercent(bot) <= 30f;
        }

        private static bool CanFollowerReceiveSearchOrder(BotOwner bot, Vector3 enemyPosition, Vector3 leaderPosition)
        {
            if (bot == null || IsFollowerUnavailable(bot))
            {
                return false;
            }

            EnemyInfo? currentEnemy = bot.Memory?.GoalEnemy;
            if (currentEnemy != null && Time.time - currentEnemy.PersonalSeenTime < RecentOwnEnemyLockout)
            {
                return false;
            }

            return Utils.Utils.GetNavDistance(bot.Position, enemyPosition) <= GetAllowedGatherDistance(bot) &&
                   Utils.Utils.GetNavDistance(bot.Position, leaderPosition) <= SearchFollowerJoinDistance;
        }

        private static float GetAllowedGatherDistance(BotOwner bot)
        {
            if (bot == null || !bot.CanSprintPlayer || HasLegInjury(bot))
            {
                return SlowSupportGatherDistance;
            }

            return SupportGatherDistance;
        }

        private static bool HasLegInjury(BotOwner bot)
        {
            if (bot?.GetPlayer?.ActiveHealthController == null)
            {
                return false;
            }

            float leftLeg = GetBodyPartPercent(bot, EBodyPart.LeftLeg);
            float rightLeg = GetBodyPartPercent(bot, EBodyPart.RightLeg);
            return Mathf.Min(leftLeg, rightLeg) < 60f;
        }

        private static float GetBodyPartPercent(BotOwner bot, EBodyPart bodyPart)
        {
            ValueStruct partHealth = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(bodyPart, false);
            if (partHealth.Maximum <= 0.01f)
            {
                return 100f;
            }

            return partHealth.Current / partHealth.Maximum * 100f;
        }

        private static float GetTotalHealthPercent(BotOwner bot)
        {
            if (bot?.GetPlayer?.ActiveHealthController == null)
            {
                return 100f;
            }

            float currentHealth = 0f;
            float maxHealth = 0f;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                ValueStruct partHealth = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                currentHealth += partHealth.Current;
                maxHealth += partHealth.Maximum;
            }

            return maxHealth <= 0.01f ? 100f : currentHealth / maxHealth * 100f;
        }

        private static bool CanUseAutomaticSupportWeapon(BotOwner bot, FollowerCombatTactic followerType, bool engagementNearBoss, bool bossIsEngager)
        {
            if (bot?.WeaponManager?.ShootController?.Item is Weapon currentWeapon &&
                currentWeapon.WeapFireType.Contains(Weapon.EFireMode.fullauto))
            {
                bot.WeaponManager.ShootController.ChangeFireMode(Weapon.EFireMode.fullauto);
                return true;
            }

            if (followerType != FollowerCombatTactic.Marksman || (!engagementNearBoss && !bossIsEngager))
            {
                return false;
            }

            BotWeaponSelector? selector = bot.WeaponManager?.Selector;
            if (selector?.CanChangeToSecondWeapons != true ||
                selector.SecondPrimaryWeaponItem is not Weapon secondWeapon ||
                !secondWeapon.WeapFireType.Contains(Weapon.EFireMode.fullauto))
            {
                return false;
            }

            selector.TryChangeWeapon(true);
            bot.WeaponManager.ShootController.ChangeFireMode(Weapon.EFireMode.fullauto);
            return true;
        }

        private static bool TryFindSupportPosition(
            BotOwner bot,
            FollowerCombatTactic followerType,
            Vector3 bossPosition,
            Vector3 engagerPosition,
            string enemyProfileId,
            Vector3 enemyPosition,
            out CustomNavigationPoint? coverPoint,
            out Vector3 supportPoint)
        {
            coverPoint = null;
            supportPoint = default;

            float radius = followerType switch
            {
                FollowerCombatTactic.Protector => ProtectorCoverRadius,
                FollowerCombatTactic.Marksman => MarksmanCoverRadius,
                _ => SupportCoverRadius,
            };

            List<CustomNavigationPoint> coverPoints = Covers.GetCoverPoints(
                bot,
                bossPosition,
                radius,
                point => point != null && !point.IsSpotted && point.IsFreeById(bot.Id),
                24);

            float bestScore = float.MinValue;
            for (int i = 0; i < coverPoints.Count; i++)
            {
                CustomNavigationPoint point = coverPoints[i];
                if (!IsSafeSupportPosition(bot, enemyProfileId, point.Position, bossPosition, engagerPosition, enemyPosition))
                {
                    continue;
                }

                bool clearShot = CanShootFromPoint(bot, point.FirePosition, enemyPosition);
                float score = ScoreSupportPosition(point.Position, bossPosition, engagerPosition, enemyPosition, clearShot, followerType);
                if (score <= bestScore)
                {
                    continue;
                }

                bestScore = score;
                coverPoint = point;
                supportPoint = point.Position;
            }

            if (coverPoint != null)
            {
                return true;
            }

            Vector3 fallback = BuildSupportFallbackPoint(followerType, bossPosition, engagerPosition, enemyPosition);
            if (fallback != Vector3.zero && NavMesh.SamplePosition(fallback, out NavMeshHit navHit, 3f, NavMesh.AllAreas))
            {
                if (IsSafeSupportPosition(bot, enemyProfileId, navHit.position, bossPosition, engagerPosition, enemyPosition))
                {
                    supportPoint = navHit.position;
                    return true;
                }
            }

            return false;
        }

        private static float ScoreSupportPosition(
            Vector3 point,
            Vector3 bossPosition,
            Vector3 engagerPosition,
            Vector3 enemyPosition,
            bool clearShot,
            FollowerCombatTactic followerType)
        {
            Vector3 toEnemy = enemyPosition - engagerPosition;
            toEnemy.y = 0f;
            Vector3 toPoint = point - engagerPosition;
            toPoint.y = 0f;

            float sideScore = 0f;
            if (toEnemy.sqrMagnitude > 0.01f && toPoint.sqrMagnitude > 0.01f)
            {
                sideScore = 1f - Mathf.Abs(Vector3.Dot(toEnemy.normalized, toPoint.normalized));
            }

            float enemyDistance = Vector3.Distance(point, enemyPosition);
            float bossDistance = Vector3.Distance(point, bossPosition);
            float rangeBias = followerType == FollowerCombatTactic.Marksman
                ? enemyDistance * 0.08f
                : -bossDistance * 0.05f;

            return sideScore * 3f + (clearShot ? 3f : 0f) + rangeBias;
        }

        private static Vector3 BuildSupportFallbackPoint(
            FollowerCombatTactic followerType,
            Vector3 bossPosition,
            Vector3 engagerPosition,
            Vector3 enemyPosition)
        {
            Vector3 engagerToEnemy = enemyPosition - engagerPosition;
            engagerToEnemy.y = 0f;
            if (engagerToEnemy.sqrMagnitude <= 0.01f)
            {
                return Vector3.zero;
            }

            Vector3 forward = engagerToEnemy.normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float forwardDistance = followerType == FollowerCombatTactic.Protector ? 3f : followerType == FollowerCombatTactic.Marksman ? 10f : 6f;
            float sideDistance = followerType == FollowerCombatTactic.Protector ? 4f : followerType == FollowerCombatTactic.Marksman ? 12f : 8f;

            return bossPosition + forward * forwardDistance + right * sideDistance;
        }

        private static bool IsSafeSupportPosition(BotOwner bot, string enemyProfileId, Vector3 point, Vector3 bossPosition, Vector3 engagerPosition, Vector3 enemyPosition)
        {
            Vector3 engagerToEnemy = enemyPosition - engagerPosition;
            engagerToEnemy.y = 0f;
            Vector3 engagerToPoint = point - engagerPosition;
            engagerToPoint.y = 0f;
            if (engagerToEnemy.sqrMagnitude > 0.01f && engagerToPoint.sqrMagnitude > 0.01f)
            {
                float align = Vector3.Dot(engagerToEnemy.normalized, engagerToPoint.normalized);
                if (Mathf.Abs(align) > SupportSideMaxDot ||
                    align >= SupportFrontMaxDot ||
                    align <= SupportBackMaxDot)
                {
                    return false;
                }
            }

            if ((point - enemyPosition).sqrMagnitude <= EnemyDangerDistance * EnemyDangerDistance)
            {
                return false;
            }

            if ((point - bossPosition).sqrMagnitude > SupportGatherDistance * SupportGatherDistance)
            {
                return false;
            }

            if (IsEnemyGroupDangerous(bot, enemyProfileId, point))
            {
                return false;
            }

            return true;
        }

        private static bool IsEnemyGroupDangerous(BotOwner bot, string enemyProfileId, Vector3 point)
        {
            EnemyInfo? goalEnemy = bot?.Memory?.GoalEnemy;
            if (goalEnemy == null ||
                string.IsNullOrEmpty(goalEnemy.ProfileId) ||
                !string.Equals(goalEnemy.ProfileId, enemyProfileId, StringComparison.Ordinal))
            {
                return false;
            }

            return Enemy.GetEnemiesAtLocation(bot, goalEnemy, point) > EnemyGroupDangerCount;
        }

        private static bool CanShootFromPoint(BotOwner bot, Vector3 firePosition, Vector3 enemyPosition)
        {
            ShootPointClass shootPoint = bot.CurrentEnemyTargetPosition(true);
            if (shootPoint != null)
            {
                return Utils.Utils.CanShootToTarget(shootPoint, firePosition, bot.LookSensor.Mask, false);
            }

            return !Physics.Linecast(firePosition, enemyPosition + Vector3.up, bot.LookSensor.Mask);
        }

        private static bool IsValidManagerSearchEnemy(BotOwner bot, EnemyInfo? goalEnemy)
        {
            return bot != null &&
                   goalEnemy != null &&
                   !string.IsNullOrEmpty(goalEnemy.ProfileId) &&
                   !goalEnemy.IsVisible &&
                   !goalEnemy.CanShoot &&
                   goalEnemy.CanISearch;
        }

        private static BotFollowerPlayer? SelectSearchLeader(
            List<BotFollowerPlayer> followers,
            Vector3 enemyPosition,
            string? preferredLeaderProfileId)
        {
            if (!string.IsNullOrEmpty(preferredLeaderProfileId))
            {
                BotFollowerPlayer? preferredLeader = followers.FirstOrDefault(f =>
                    string.Equals(f.GetBot()?.ProfileId, preferredLeaderProfileId, StringComparison.Ordinal));
                if (preferredLeader != null && CanFollowerReceiveSearchOrder(preferredLeader.GetBot(), enemyPosition, preferredLeader.GetBot().Position))
                {
                    return preferredLeader;
                }
            }

            BotFollowerPlayer? selected = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner bot = followers[i].GetBot();
                if (!CanFollowerReceiveSearchOrder(bot, enemyPosition, bot.Position))
                {
                    continue;
                }

                float navDistance = Utils.Utils.GetNavDistance(bot.Position, enemyPosition);
                if (selected == null || navDistance < bestDistance)
                {
                    selected = followers[i];
                    bestDistance = navDistance;
                }
            }

            return selected;
        }

        private static bool TryFindSearchLeaderPoint(BotOwner? bot, Vector3 bossPosition, Vector3 enemyPosition, out Vector3 point)
        {
            point = default;
            if (bot == null)
            {
                return false;
            }

            CustomNavigationPoint? coverPoint = Covers.GetClosestCoverPointTowardPoint(
                bot,
                bossPosition,
                enemyPosition,
                SearchLeaderRadius,
                cover => cover != null && !cover.IsSpotted && cover.IsFreeById(bot.Id));
            if (coverPoint != null)
            {
                point = coverPoint.Position;
                bot.Memory.SetCoverPoints(coverPoint);
                return true;
            }

            if (NavMesh.SamplePosition(enemyPosition, out NavMeshHit navHit, 4f, NavMesh.AllAreas))
            {
                point = navHit.position;
                return true;
            }

            return false;
        }

        private static bool TryGetSearchFollowerPoint(Vector3 leaderPosition, Vector3 followerPosition, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(leaderPosition, out NavMeshHit leaderHit, 3f, NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 leaderDirection = followerPosition - leaderHit.position;
            leaderDirection.y = 0f;
            if (leaderDirection.sqrMagnitude <= 0.01f)
            {
                leaderDirection = Vector3.back;
            }

            leaderDirection = leaderDirection.normalized * SearchFollowerSpacing;
            if (NavMesh.Raycast(leaderHit.position, leaderHit.position + leaderDirection, out NavMeshHit rayHit, NavMesh.AllAreas))
            {
                point = rayHit.position;
                return true;
            }

            point = leaderHit.position + leaderDirection;
            return true;
        }

        private void ClearBossManagerState(string bossProfileId, string reason)
        {
            List<BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(bossProfileId);
            ClearFollowerManagerOrders(followers, $"managerClear:{reason}");
            _stateByBossProfileId.Remove(bossProfileId);
        }

        private static void ClearFollowerManagerOrders(IEnumerable<BotFollowerPlayer> followers, string reason)
        {
            foreach (BotFollowerPlayer follower in followers)
            {
                follower?.ClearManagerCombatDecision(reason);
            }
        }

        private static bool IsEligibleFollower(BotFollowerPlayer follower)
        {
            BotOwner bot = follower?.GetBot();
            return follower != null &&
                   bot != null &&
                   !bot.IsDead &&
                   bot.BotState == EBotState.Active &&
                   bot.GetPlayer?.HealthController?.IsAlive == true &&
                   bot.BotFollower?.HaveBoss == true;
        }

        private static bool IsFinite(Vector3 vector)
        {
            return !float.IsNaN(vector.x) &&
                   !float.IsNaN(vector.y) &&
                   !float.IsNaN(vector.z) &&
                   !float.IsInfinity(vector.x) &&
                   !float.IsInfinity(vector.y) &&
                   !float.IsInfinity(vector.z);
        }

        public void Dispose()
        {
            if (_disposing)
            {
                return;
            }

            _disposing = true;
            if (GameWorld != null)
            {
                GameWorld.OnDispose -= Dispose;
            }

            foreach (string bossProfileId in _stateByBossProfileId.Keys.ToList())
            {
                ClearFollowerManagerOrders(BossPlayers.GetFollowersByBoss(bossProfileId), "managerDispose");
            }

            _stateByBossProfileId.Clear();
            GameWorld = null;
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
