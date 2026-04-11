using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;

using UnityEngine;
using UnityEngine.AI;

using Comfort.Common;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatCommon
    {
        private const float StartCloseCoverDistance = 25f;
        private const float StartSupportSuppressDistance = 30f;
        private const float ShootCoverSuperiorNavImprovementFactor = 0.7f;
        private const float StableShootCoverRefreshInterval = 1.2f;
        private const float UnstableShootCoverRefreshInterval = 0.6f;
        private const float HaveCoverToShootDebounceSeconds = 0.15f;
        private const float ShootLaneUpgradeHysteresisSeconds = 0.2f;
        private const float PointToShootUpdateMinDistance = 1.5f;
        private const float CombatCoverMaxDistance = 120f;
        private const float StableVisibleImmediateFireSeconds = 0.3f;
        private const float HealCoverRetreatDistance = 14f;
        private const float HealCoverSearchRadius = 30f;
        private const float HealCoverMaxNavDistance = 35f;
        private const float HealCoverMinNavDistance = 2f;
        private const float HealCoverMinEnemyDistanceGain = -2f;

        private readonly BotOwner botOwner;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? initialDecision;
        private float healBlockUntil;
        private float healStartedAt;
        private float stimStartedAt;
        private CustomNavigationPoint? committedHealCover;
        private BotLogicDecision committedHealMoveAction;
        private string? committedHealMoveReason;
        private bool holdActive;
        private float holdEndTime;

        private float dangerTimer = 0f;
        private float nextShootCoverCheckTime;
        private float nextClosestShootCoverCheckTime;
        private float nextApproachableCoverCheckTime;
        private float dangerIgnoreEquipTimer = 0f;
        private bool dangerResult = false;
        private bool dangerIgnoreEquipResult = false;
        private CustomNavigationPoint? cachedClosestShootCover;
        private float inCoverSince = 0f;
        private bool pendingHaveCoverToShoot;
        private float pendingHaveCoverToShootSince;
        private float shootLaneUpgradeSince;

        public FollowerCombatCommon(BotOwner botOwner)
        {
            this.botOwner = botOwner;
        }

        public bool HasInitialDecision => initialDecision.HasValue;

        public void ClearInitialDecision()
        {
            initialDecision = null;
        }

        public void Reset()
        {
            initialDecision = null;
            healBlockUntil = 0f;
            healStartedAt = 0f;
            stimStartedAt = 0f;
            committedHealCover = null;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
            holdActive = false;
            holdEndTime = 0f;
            HaveCoverToShoot = false;
            PointToShoot = null;
            cachedClosestShootCover = null;
            nextClosestShootCoverCheckTime = 0f;
            nextApproachableCoverCheckTime = 0f;
            pendingHaveCoverToShoot = false;
            pendingHaveCoverToShootSince = 0f;
            shootLaneUpgradeSince = 0f;
        }
        /// <summary>
        /// Returns the active tactic so combat branches can bias toward protection or ranged play.
        /// </summary>
        public FollowerCombatTactic GetFollowerTactic()
        {
            return BossPlayers.Instance?.GetFollower(botOwner)?.CombatTactic ?? FollowerCombatTactic.Balanced;
        }

        /// <summary>
        /// Reads the configured follower aggression as a normalized 0-1 value.
        /// </summary>
        public float GetAggression01()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(botOwner);
            float aggression = followerData?.CombatAggression ?? 50f;
            return Mathf.Clamp01(aggression / 100f);
        }

        public bool HaveCoverToShoot { get; private set; }
        public CustomNavigationPoint? PointToShoot { get; private set; }

        public bool IsEnemyVisibleAndShootable()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            return HasActiveCombatEnemy(goalEnemy) && goalEnemy.CanShoot && goalEnemy.IsVisible;
        }

        public bool HasActiveCombatEnemy()
        {
            return HasActiveCombatEnemy(botOwner.Memory.GoalEnemy);
        }

        public bool HasActiveCombatEnemy(EnemyInfo? goalEnemy)
        {
            if (!botOwner.Memory.HaveEnemy || goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.Person?.HealthController?.IsAlive == true)
            {
                return true;
            }

            // EnemyInfo.Person can temporarily be null/stale; resolve against alive players by profile id.
            if (!string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                Player? alivePlayer = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(goalEnemy.ProfileId);
                if (alivePlayer?.HealthController?.IsAlive == true)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Promotes an already-tracked enemy to the follower's current goal without forcing a new acquire path.
        /// </summary>
        public bool TryPromoteTrackedEnemyAsGoal(string enemyProfileId)
        {
            if (string.IsNullOrEmpty(enemyProfileId) || botOwner?.EnemiesController?.EnemyInfos == null)
            {
                return false;
            }

            foreach (var item in botOwner.EnemiesController.EnemyInfos)
            {
                if (item.Key?.ProfileId != enemyProfileId)
                {
                    continue;
                }

                item.Value.PriorityIndex = 0;
                botOwner.Memory.GoalEnemy = item.Value;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies the default follower aggression-to-threat mapping used by the core combat path.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, float aggression01)
        {
            bool ignoreEquip = aggression01 >= 0.4f;
            float maximumEnemies = aggression01 >= 0.7f ? 3f : aggression01 >= 0.4f ? 2f : 1f;
            return IsEnemyLowThreat(goalEnemy, ignoreEquip, maximumEnemies);
        }

        /// <summary>
        /// Decides whether a visible enemy should force the bot into cover before trading shots.
        /// </summary>
        public bool ShouldTakeVisibleCover(EnemyInfo goalEnemy, float? aggressionOverride01 = null)
        {
            if (botOwner.Memory.IsInCover)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() || botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 0.75f))
            {
                return true;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            float standAndTradeDistance = botOwner.LookSensor.MaxShootDist * 0.5f;
            return aggression < 0.45f && goalEnemy.Distance > standAndTradeDistance && PointToShoot != null;
        }

        /// <summary>
        /// Shared aggression gate for pushes so tactic variants can reuse the same advance logic
        /// while overriding aggression or distance policy where needed.
        /// </summary>
        public bool ShouldAdvance(
            EnemyInfo goalEnemy,
            float? aggressionOverride01 = null,
            FollowerCombatTactic? tacticOverride = null,
            Enemy.EnemyDistance? maxPushDistanceOverride = null)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (IsFollowerCriticallyWounded() ||
                botOwner.Memory.IsUnderFire ||
                WasHitRecently(botOwner, 1f))
            {
                return false;
            }

            float aggression = aggressionOverride01 ?? GetAggression01();
            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            float pushThreshold = goalEnemy.IsVisible ? 0.35f : 0.45f;

            if (tactic == FollowerCombatTactic.Protector)
            {
                pushThreshold += 0.15f;
            }
            else if (tactic == FollowerCombatTactic.Marksman)
            {
                pushThreshold += 0.3f;
            }

            Enemy.EnemyDistance maxPushDistance = maxPushDistanceOverride ?? GetMaxPushDistance(aggression, tactic);

            if (!IsEnemyLowThreat(goalEnemy, aggression))
            {
                return aggression >= 0.7f && Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.Close;
            }

            if (!goalEnemy.IsVisible && !HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            if (Enemy.Distance(goalEnemy) > maxPushDistance)
            {
                return false;
            }

            return aggression >= pushThreshold && ProtectWantKill(goalEnemy.Distance * 1.2f);
        }

        /// <summary>
        /// Chooses the movement mode used to reach a committed combat cover point.
        /// </summary>
        public BotLogicDecision SelectCommittedCoverMoveAction(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return BotLogicDecision.attackMoving;
            }

            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                return BotLogicDecision.attackMovingWithSuppress;
            }

            return botOwner.CanSprintPlayer
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;
        }

        /// <summary>
        /// Pushes the selected cover point into EFT cover memory so movement actions use it.
        /// </summary>
        public void AssignCover(CustomNavigationPoint? cover)
        {
            SetCover(cover);
            if (cover != null && cover.IsFreeById(botOwner.Id))
            {
                cover.SetOwner(botOwner);
            }
        }

        public void RefreshShootCover()
        {
            if (nextShootCoverCheckTime >= Time.time)
            {
                return;
            }

            Vector3 bossPosition = GetBossPosition();
            CustomNavigationPoint? candidate = FindFollowerShootCover();
            bool pointChangedMeaningfully = IsPointMeaningfullyDifferent(PointToShoot, candidate);
            if (ShouldUpdatePointToShoot(PointToShoot, candidate))
            {
                PointToShoot = candidate;
            }

            if (!IsCoverUsable(candidate))
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            if (candidate == null)
            {
                HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(false);
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            bool requireShootLane = ProtectCareKill();
            bool candidateCanShoot = candidate.CanIShootToEnemy;
            bool candidateShootLaneStable = !requireShootLane || IsShootLaneUpgradeStable(candidateCanShoot);
            bool rawHaveCoverToShoot = !requireShootLane || candidateShootLaneStable;
            HaveCoverToShoot = UpdateDebouncedHaveCoverToShoot(rawHaveCoverToShoot);
            if (!HaveCoverToShoot)
            {
                ScheduleShootCoverRefresh(stable: false);
                return;
            }

            CustomNavigationPoint? current = botOwner.Memory.CurCustomCoverPoint;
            if (!ShouldCommitRefreshedShootCover(current, candidate, bossPosition, requireShootLane, candidateShootLaneStable))
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            if (current != null && current.Id == candidate.Id)
            {
                bool stableSignal = !IsHaveCoverDebouncePending() && !pointChangedMeaningfully;
                ScheduleShootCoverRefresh(stableSignal);
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(candidate, true);
            ScheduleShootCoverRefresh(stable: false);
        }

        private bool ShouldCommitRefreshedShootCover(
            CustomNavigationPoint? current,
            CustomNavigationPoint candidate,
            Vector3 bossPosition,
            bool requireShootLane,
            bool candidateShootLaneStable)
        {
            // Rule 1: no current cover or current cover is invalid.
            if (IsCurrentCoverInvalid(current, bossPosition))
            {
                return true;
            }

            if (current == null)
            {
                return true;
            }

            bool currentCanShoot = current.CanIShootToEnemy;
            bool candidateCanShoot = candidate.CanIShootToEnemy;

            // Rule 2: current cannot shoot and candidate can.
            if (!currentCanShoot && candidateCanShoot && candidateShootLaneStable)
            {
                return true;
            }

            bool currentUsable = IsCoverUsable(current);
            bool candidateUsable = IsCoverUsable(candidate);

            // Rule 3: current violates boss-distance/usability and candidate does not.
            if (!currentUsable && candidateUsable)
            {
                return true;
            }

            // Rule 4: meaningful superiority only; avoid reshuffle from already-valid shoot-capable cover.
            if (currentUsable && currentCanShoot)
            {
                return false;
            }

            if (requireShootLane && !candidateShootLaneStable)
            {
                return false;
            }

            return HasMeaningfulNavImprovement(current, candidate);
        }

        private bool IsCurrentCoverInvalid(CustomNavigationPoint? cover, Vector3 bossPosition)
        {
            return cover == null ||
                   !cover.IsFreeById(botOwner.Id) ||
                   cover.IsSpotted;
        }

        /// <summary>
        /// Basic validity gate for a candidate cover point.
        /// </summary>
        public bool IsCoverUsable(CustomNavigationPoint? cover, bool ignoreSpotted = false)
        {
            return cover != null &&
                   cover.IsFreeById(botOwner.Id) &&
                   (ignoreSpotted || !cover.IsSpotted);
        }

        /// <summary>
        /// Returns the mod-owned maximum combat cover search distance.
        /// </summary>
        public float GetCombatCoverMaxDistanceSqr()
        {
            return CombatCoverMaxDistance * CombatCoverMaxDistance;
        }

        private bool HasMeaningfulNavImprovement(CustomNavigationPoint current, CustomNavigationPoint candidate)
        {
            float currentNavDistance = GetCoverNavDistance(current);
            float candidateNavDistance = GetCoverNavDistance(candidate);

            if (!IsFinite(currentNavDistance) || !IsFinite(candidateNavDistance))
            {
                return false;
            }

            return candidateNavDistance <= currentNavDistance * ShootCoverSuperiorNavImprovementFactor;
        }

        private bool ShouldUpdatePointToShoot(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (candidate == null)
            {
                return currentPoint == null;
            }

            if (currentPoint == null)
            {
                return true;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool IsPointMeaningfullyDifferent(CustomNavigationPoint? currentPoint, CustomNavigationPoint? candidate)
        {
            if (currentPoint == null || candidate == null)
            {
                return currentPoint != candidate;
            }

            if (currentPoint.Id == candidate.Id)
            {
                return false;
            }

            float minDeltaSqr = PointToShootUpdateMinDistance * PointToShootUpdateMinDistance;
            return (currentPoint.Position - candidate.Position).sqrMagnitude >= minDeltaSqr;
        }

        private bool UpdateDebouncedHaveCoverToShoot(bool rawValue)
        {
            if (rawValue == HaveCoverToShoot)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = 0f;
                return HaveCoverToShoot;
            }

            if (pendingHaveCoverToShootSince <= 0f || pendingHaveCoverToShoot != rawValue)
            {
                pendingHaveCoverToShoot = rawValue;
                pendingHaveCoverToShootSince = Time.time;
                return HaveCoverToShoot;
            }

            if (Time.time - pendingHaveCoverToShootSince < HaveCoverToShootDebounceSeconds)
            {
                return HaveCoverToShoot;
            }

            HaveCoverToShoot = rawValue;
            pendingHaveCoverToShootSince = 0f;
            return HaveCoverToShoot;
        }

        private bool IsHaveCoverDebouncePending()
        {
            return pendingHaveCoverToShootSince > 0f && pendingHaveCoverToShoot != HaveCoverToShoot;
        }

        private bool IsShootLaneUpgradeStable(bool candidateCanShoot)
        {
            if (!candidateCanShoot)
            {
                shootLaneUpgradeSince = 0f;
                return false;
            }

            if (shootLaneUpgradeSince <= 0f)
            {
                shootLaneUpgradeSince = Time.time;
            }

            return Time.time - shootLaneUpgradeSince >= ShootLaneUpgradeHysteresisSeconds;
        }

        private void ScheduleShootCoverRefresh(bool stable)
        {
            nextShootCoverCheckTime = Time.time + (stable ? StableShootCoverRefreshInterval : UnstableShootCoverRefreshInterval);
        }

        private float GetCoverNavDistance(CustomNavigationPoint cover)
        {
            float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (IsFinite(navDistance))
            {
                return navDistance;
            }

            return Vector3.Distance(botOwner.Position, cover.Position);
        }

        private CustomNavigationPoint? FindFollowerShootCover()
        {
            Vector3 bossPosition = GetBossPosition();
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            Vector3 enemyAnchor = goalEnemy != null ? GetEnemyAnchor(goalEnemy) : Vector3.zero;
            LayerMask mask = botOwner.LookSensor.Mask;
            if (goalEnemy != null)
            {
                if (shootPoint != null)
                {
                    CustomNavigationPoint? directionalShootCover = Covers.GetClosestCoverPointTowardPoint(
                        botOwner,
                        bossPosition,
                        enemyAnchor,
                        25f,
                        cover => Utils.Utils.CanShootToTarget(shootPoint, cover, mask, false));
                    if (directionalShootCover != null)
                    {
                        return directionalShootCover;
                    }
                }

                CustomNavigationPoint? directionalCover = Covers.GetClosestCoverPointTowardPoint(
                    botOwner,
                    bossPosition,
                    enemyAnchor,
                    22f);
                if (directionalCover != null)
                {
                    return directionalCover;
                }
            }

            return null;
        }

        /// <summary>
        /// Old-plugin equivalent of GetClosestAttackCoverPoint/GetClosestShootCover.
        /// Finds a nearby cover point with a clear shot to the enemy target point.
        /// </summary>
        public CustomNavigationPoint? GetClosestShootCover(Vector3 centerPosition, float maxDistance = 150f, bool inbetween = false)
        {
            if (nextClosestShootCoverCheckTime > Time.time)
            {
                return cachedClosestShootCover;
            }

            nextClosestShootCoverCheckTime = Time.time + 1f;

            ShootPointClass shootPointClass = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPointClass == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            float weaponShootDistMaxSqr = botOwner.LookSensor.MaxShootDist * botOwner.LookSensor.MaxShootDist;
            cachedClosestShootCover = Covers.GetClosestCoverPoint(
                botOwner,
                centerPosition,
                maxDistance,
                point =>
                {
                    if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                    {
                        return false;
                    }

                    if (inbetween && !Covers.IsPointBetween(point.Position, botOwner.Position, centerPosition))
                    {
                        return false;
                    }

                    if ((point.Position - shootPointClass.Point).sqrMagnitude >= weaponShootDistMaxSqr)
                    {
                        return false;
                    }

                    return Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner.LookSensor.Mask, false);
                });

            if (cachedClosestShootCover != null)
            {
                botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
            }

            botOwner.Memory.SetCoverPoints(cachedClosestShootCover);
            return cachedClosestShootCover;
        }

        /// <summary>
        /// Old-plugin equivalent of GetApproachablePoint/GetApproachableCover.
        /// Picks a shooting cover around the midpoint between bot and enemy.
        /// </summary>
        public CustomNavigationPoint? GetApproachableCover(bool inbetween = false)
        {
            if (nextApproachableCoverCheckTime > Time.time)
            {
                return cachedClosestShootCover;
            }

            nextApproachableCoverCheckTime = Time.time + 1f;
            nextClosestShootCoverCheckTime = 0f;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                cachedClosestShootCover = null;
                return null;
            }

            Vector3 enemyPosition = IsFinite(goalEnemy.EnemyLastPositionReal)
                ? goalEnemy.EnemyLastPositionReal
                : goalEnemy.CurrPosition;

            Vector3 midpoint = (botOwner.Position + enemyPosition) * 0.5f;
            return GetClosestShootCover(midpoint, 120f, inbetween);
        }

        public Vector3 GetBossPosition()
        {
            if (botOwner.BotFollower.BossToFollow is pitAIBossPlayer boss &&
                boss.realPlayer != null &&
                IsFinite(boss.realPlayer.Transform.position))
            {
                return boss.realPlayer.Transform.position;
            }

            Vector3? liveBossPos = botOwner.BotFollower.BossToFollow?.Position;
            if (liveBossPos.HasValue && IsFinite(liveBossPos.Value))
            {
                return liveBossPos.Value;
            }

            return botOwner.Position;
        }

        /// <summary>
        /// Returns boss distance using path distance first. Boss leash decisions should not use only
        /// straight-line distance because floors, doors, and building routes can make a nearby 3D
        /// position tactically far away.
        /// </summary>
        public float GetBossNavDistance(Vector3 bossPosition)
        {
            return Utils.Utils.GetNavDistance(botOwner.Position, bossPosition);
        }

        /// <summary>
        /// Shared boss/follower/enemy spacing snapshot used by combat objective logic.
        /// This lets the higher-level combat tree compare who currently owns the forward line:
        /// the boss or the follower.
        /// </summary>
        public bool TryGetBossRelativeCombatSpacing(
            EnemyInfo goalEnemy,
            out Vector3 bossPosition,
            out Vector3 enemyAnchor,
            out float followerBossDistance,
            out float followerEnemyDistance,
            out float bossEnemyDistance)
        {
            bossPosition = GetBossPosition();
            enemyAnchor = GetEnemyAnchor(goalEnemy);
            followerBossDistance = 0f;
            followerEnemyDistance = 0f;
            bossEnemyDistance = 0f;

            if (!IsFinite(bossPosition) || !IsFinite(enemyAnchor))
            {
                return false;
            }

            followerBossDistance = GetBossNavDistance(bossPosition);
            followerEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            bossEnemyDistance = Vector3.Distance(bossPosition, enemyAnchor);
            return true;
        }

        /// <summary>
        /// Finds a step cover that moves the follower toward the boss while optionally requiring
        /// either a shoot lane or a hide lane from the active enemy.
        /// Used by the boss-relative combat objective so rejoin/retreat movement is cover-to-cover
        /// instead of a blind run straight at the boss.
        /// </summary>
        public bool TryFindCoverTowardBoss(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            float searchRadius,
            bool requireShootLane,
            bool requireHideFromEnemy,
            out CustomNavigationPoint? cover)
        {
            cover = null;
            if (!IsFinite(bossPosition))
            {
                return false;
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;
            LayerMask mask = botOwner.LookSensor.Mask;

            // The selector still comes from our custom cover path. The forward direction is bossward,
            // while the extra checks decide whether this step is a fighting cover or a retreat cover.
            CustomNavigationPoint? candidate = Covers.GetClosestCoverPointTowardPoint(
                botOwner,
                botOwner.Position,
                bossPosition,
                searchRadius,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if (requireHideFromEnemy &&
                        IsFinite(enemyAnchor) &&
                        !point.CanIHideFromPos(0f, true, false, enemyAnchor))
                    {
                        return false;
                    }

                    if (shootPoint != null &&
                        !Utils.Utils.CanShootToTarget(shootPoint, point, mask, false))
                    {
                        return false;
                    }

                    return true;
                });

            if (candidate == null)
            {
                return false;
            }

            cover = candidate;
            return true;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> ConsumeInitialDecision()
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26> decision = initialDecision ??
                new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "missingInitialDecision");
            initialDecision = null;
            return decision;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? PreFightLogic()
        {
            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = TryGetDogFightDecision();
            if (dogFightDecision != null)
            {
                initialDecision = null;
                return dogFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = InFightLogic();
            if (inFightDecision != null)
            {
                initialDecision = null;
                return inFightDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = TryGetNeedHealDecision();
            if (healDecision != null)
            {
                initialDecision = null;
                return healDecision;
            }

            return null;
        }

        /// <summary>
        /// Standalone in-cover ally support check.
        /// Allows follower to switch targets and support an actively engaged allied enemy
        /// when:
        /// 1. Follower is in cover and stably held position (≥1s)
        /// 2. Current goal enemy is not visible or does not exist
        /// 3. Not under direct fire
        /// 4. An ally is clearly engaging an enemy (visible, shootable)
        /// 5. Support cover for that engagement exists within reasonable distance
        /// 
        /// Prevents flip-flopping by:
        /// - Requiring minimum cover duration
        /// - Checking recent enemy-seen time (don't abandon hot targets)
        /// - Requiring good support cover availability
        /// </summary>
        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetAllyEngagementSupportDecision()
        {
            // Gate 1: Must be in cover and have held it for stability
            if (!botOwner.Memory.IsInCover)
            {
                inCoverSince = 0f;
                return null;
            }

            if (inCoverSince <= 0f)
            {
                inCoverSince = Time.time;
            }

            if (Time.time - inCoverSince < 1f)
            {
                return null;
            }

            // Gate 2: Current enemy conditions allow switching
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;

            // If we can see current enemy, don't switch away
            if (goalEnemy != null && goalEnemy.IsVisible)
            {
                return null;
            }

            // If under active fire, need to stay focused on threat
            if (botOwner.Memory.IsUnderFire)
            {
                return null;
            }

            // If current enemy was recently seen, maintain focus (avoid flip-flopping)
            if (goalEnemy != null && Time.time - goalEnemy.PersonalLastSeenTime < 2.5f)
            {
                return null;
            }

            // Gate 3: An ally must be clearly engaging an enemy (visible + shootable = credible threat)
            if (!TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition))
            {
                return null;
            }

            TryPromoteTrackedEnemyAsGoal(supportEnemyProfileId);

            // Gate 4: We must have viable cover to support from
            if (!TryGetSupportCover(supportEnemyPosition, out CustomNavigationPoint? supportCover, out float supportCoverNavDistance))
            {
                return null;
            }

            // All conditions met: commit to support decision
            SetCover(supportCover);
            BotLogicDecision supportDecision = supportCoverNavDistance <= StartSupportSuppressDistance
                ? BotLogicDecision.attackMovingWithSuppress
                : BotLogicDecision.runToCover;
            string reason = supportDecision == BotLogicDecision.runToCover ? "allySupportRun" : "allySupportSuppress";
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(supportDecision, reason);
        }

        public void PrepareStartDecision(float aggression)
        {
            initialDecision = null;

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return;
            }

            bool haveCover = TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? startCover, out float startCoverNavDistance, out bool startCoverHasShootLane);
            bool closeCover = haveCover && startCoverNavDistance <= StartCloseCoverDistance;
            bool farCover = haveCover && !closeCover;

            // Decision 1: enemy visible + close shooting cover -> attack-moving into that cover.
            if (goalEnemy.IsVisible && closeCover && startCover != null && startCover.CanIShootToEnemy)
            {
                SetCover(startCover);
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "startVisCloseCover");
                return;
            }

            // Decision 2: enemy unseen + under fire.
            // If close cover exists -> move with suppressive fire.
            // Else if far cover exists -> run to cover.
            // Else -> hold lane with suppressive fire in place.
            if (!goalEnemy.IsVisible && botOwner.Memory.IsUnderFire)
            {
                if (closeCover)
                {
                    SetCover(startCover);
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMovingWithSuppress, "startSuppressionCover");
                    return;
                }

                if (farCover)
                {
                    SetCover(startCover);
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "startUnderFireRunCover");
                    return;
                }

                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.suppressFire, "startUnderFireSuppress");
                return;
            }

            // Decision 3: enemy unseen, not under fire, and allies are actively engaging -> support from shooting cover.
            if (!goalEnemy.IsVisible && !botOwner.Memory.IsUnderFire && TryGetAllyEngagementEnemy(out string supportEnemyProfileId, out Vector3 supportEnemyPosition))
            {
                TryPromoteTrackedEnemyAsGoal(supportEnemyProfileId);

                if (TryGetSupportCover(supportEnemyPosition, out CustomNavigationPoint? supportCover, out float supportCoverNavDistance))
                {
                    SetCover(supportCover);
                    BotLogicDecision supportDecision = supportCoverNavDistance <= StartSupportSuppressDistance
                        ? BotLogicDecision.attackMovingWithSuppress
                        : BotLogicDecision.runToCover;
                    initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(supportDecision, supportDecision == BotLogicDecision.runToCover ? "startAllySupportRun" : "startAllySupportSuppress");
                    return;
                }
            }

            // Decision 4: enemy unseen and low threat -> close pressure/push.
            if (!goalEnemy.IsVisible && IsEnemyLowThreat(goalEnemy, aggression > 0.6f, aggression >= 0.8f ? 2f : 1f))
            {

                initialDecision = EnemySearch("startWeakEnemyPush");
            }

            // Decision 5: any far cover opportunity at combat start -> run to cover.
            if (farCover)
            {
                SetCover(startCover);
                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, goalEnemy.IsVisible ? "startVisFarCover" : "startBlindFarCover");
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? InFightLogic()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            AICoreActionResultStruct<BotLogicDecision, GClass26>? shootNowDecision = TryGetImmediateShootDecision("ShootImmediately");
            if (shootNowDecision != null)
            {
                return shootNowDecision;
            }

            if (CanShootFromCurrentCover(out string cause))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, cause);
            }

            if (botOwner.NearDoorData.RecentlyClosedDoorCheckTime + 0.3f < Time.time &&
                botOwner.BotsGroup.EnemyLastSeenTimeReal + 7f >= Time.time &&
                goalEnemy != null &&
                EnemyPathCrossesRecentDoor(goalEnemy))
            {
                botOwner.Memory.Spotted(false, null, null);
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetDogFightDecision()
        {
            EnemyInfo goalEnemy = botOwner.Memory.GoalEnemy;

            BotDogFightStatus dogFightState = botOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            if (dogFightState == BotDogFightStatus.dogFight)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
            }

            if (dogFightState == BotDogFightStatus.shootFromPlace)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < 18f &&
                goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_IN)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Enemy.Distance(goalEnemy) <= Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "enemyVeryClose");
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetNeedHealDecision()
        {
            bool coverTried = false;

            if (botOwner.Medecine == null)
            {
                return null;
            }

            if (!botOwner.Memory.HaveEnemy)
            {
                healBlockUntil = 0f;
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do ||
                                botOwner.Medecine.SurgicalKit.HaveWork ||
                                botOwner.Medecine.FirstAid.Using ||
                                botOwner.Medecine.SurgicalKit.Using;
            var stims = botOwner.Medecine.Stimulators;
            bool shouldUseStim = stims?.HaveSmt == true &&
                                 Time.time - stims.LastEndUseTime > 3f &&
                                 stims.CanUseNow() &&
                                 botOwner.GetPlayer?.HealthStatus != ETagStatus.Healthy;

            if (botOwner.Medecine.Stimulators.Using)
            {
                if (stimStartedAt <= 0f)
                {
                    stimStartedAt = Time.time;
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
            }

            if (!haveHealWork)
            {
                ClearCommittedHealCover();

                if (shouldUseStim &&
                    goalEnemy != null &&
                    !goalEnemy.IsVisible &&
                    Time.time - goalEnemy.PersonalLastSeenTime > 1.5f)
                {
                    stimStartedAt = Time.time;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.healStimulators, "healQuick");
                }

                return null;
            }

            if (healBlockUntil >= Time.time)
            {
                return null;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? committedHealMove = TryGetCommittedHealMoveDecision(goalEnemy);
            if (committedHealMove != null)
            {
                return committedHealMove;
            }

            if (goalEnemy == null ||
                botOwner.Medecine.FirstAid.Using ||
                botOwner.Medecine.SurgicalKit.Using)
            {
                if (goalEnemy == null)
                {
                    healBlockUntil = Time.time;
                }

                if (healStartedAt <= 0f)
                {
                    healStartedAt = Time.time;
                }
                ClearCommittedHealCover();

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
            }

            float lastSeen = Time.time - goalEnemy.PersonalLastSeenTime;
            bool enemyVisible = goalEnemy.IsVisible;
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);

            if (!enemyVisible && lastSeen > 3f)
            {
                if (botOwner.Memory.IsInCover && enemyProxyDistance > Enemy.ProxyDistance.VeryClose)
                {
                    if (healStartedAt <= 0f)
                    {
                        healStartedAt = Time.time;
                    }
                    ClearCommittedHealCover();
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (!enemyVisible && lastSeen <= 3f)
            {
                if (enemyProxyDistance > Enemy.ProxyDistance.Close)
                {
                    if (botOwner.Memory.IsInCover)
                    {
                        if (healStartedAt <= 0f)
                        {
                            healStartedAt = Time.time;
                        }
                        ClearCommittedHealCover();
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                    }

                    if (TryAssignHealCover(goalEnemy, ref coverTried))
                    {
                        return CreateCommittedHealMoveDecision(goalEnemy);
                    }

                    healBlockUntil = Time.time + 3f;
                    return null;
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return CreateCommittedHealMoveDecision(goalEnemy);
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (TryAssignHealCover(goalEnemy, ref coverTried))
            {
                return CreateCommittedHealMoveDecision(goalEnemy);
            }

            healBlockUntil = Time.time + 3f;
            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetImmediateShootDecision(string reason)
        {
            if (!ShouldShootImmediately())
            {
                return null;
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, reason);
        }

        private bool TryAssignHealCover(EnemyInfo goalEnemy, ref bool coverTried)
        {
            if (coverTried)
            {
                return false;
            }

            coverTried = true;

            if (IsCommittedHealCoverValid(goalEnemy))
            {
                SetCover(committedHealCover);
                return true;
            }

            if (TryFindHealCover(goalEnemy, out CustomNavigationPoint? healCover))
            {
                SetCover(healCover);
                committedHealCover = healCover;
                CommitHealMove(goalEnemy);
                return true;
            }

            if (TryAssignRetreatAttackCover(goalEnemy, false, HealCoverMaxNavDistance * HealCoverMaxNavDistance))
            {
                committedHealCover = botOwner.Memory.CurCustomCoverPoint;
                CommitHealMove(goalEnemy);
                return true;
            }

            return false;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null || botOwner.Memory.IsInCover || !IsCommittedHealCoverValid(goalEnemy))
            {
                ClearCommittedHealCover();
                return null;
            }

            SetCover(committedHealCover);

            return CreateCommittedHealMoveDecision(goalEnemy);
        }

        private void CommitHealMove(EnemyInfo? goalEnemy)
        {
            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            committedHealMoveAction = botOwner.CanSprintPlayer && enemyProxyDistance > Enemy.ProxyDistance.VeryClose
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;
            committedHealMoveReason = committedHealMoveAction == BotLogicDecision.runToCover ? "runToHeal" : "moveToHeal";
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> CreateCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (committedHealMoveAction == default)
            {
                CommitHealMove(goalEnemy);
            }

            string reason = !string.IsNullOrEmpty(committedHealMoveReason)
                ? committedHealMoveReason
                : "runToHeal";
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(committedHealMoveAction, reason);
        }

        /// <summary>
        /// Expands push distance as aggression rises while still respecting follower tactics.
        /// </summary>
        public Enemy.EnemyDistance GetMaxPushDistance(float aggression, FollowerCombatTactic? tacticOverride = null)
        {
            Enemy.EnemyDistance defaultDistance;

            if (aggression <= 0.2f)
            {
                defaultDistance = Enemy.EnemyDistance.VeryClose;
            }

            else if (aggression <= 0.4f)
            {
                defaultDistance = Enemy.EnemyDistance.Close;
            }
            else if (aggression <= 0.65f)
            {
                defaultDistance = Enemy.EnemyDistance.Distant;
            }
            else
            {
                defaultDistance = Enemy.EnemyDistance.Far;
            }

            FollowerCombatTactic tactic = tacticOverride ?? GetFollowerTactic();
            return tactic switch
            {
                FollowerCombatTactic.Protector => Enemy.EnemyDistance.Close,
                FollowerCombatTactic.Marksman => Enemy.EnemyDistance.VeryClose,
                _ => defaultDistance,
            };
        }

        private bool TryFindHealCover(EnemyInfo goalEnemy, out CustomNavigationPoint? cover)
        {
            cover = null;
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            if (!IsFinite(enemyAnchor))
            {
                return false;
            }

            Vector3 awayFromEnemy = botOwner.Position - enemyAnchor;
            awayFromEnemy.y = 0f;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = GetBossPosition() - enemyAnchor;
                awayFromEnemy.y = 0f;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                return false;
            }

            Vector3 retreatAnchor = botOwner.Position + awayFromEnemy.normalized * HealCoverRetreatDistance;
            float currentEnemyDistance = Vector3.Distance(botOwner.Position, enemyAnchor);
            cover = Covers.GetClosestCoverPoint(
                botOwner,
                retreatAnchor,
                HealCoverSearchRadius,
                point =>
                {
                    if (!IsCoverUsable(point))
                    {
                        return false;
                    }

                    if (!point.CanIHideFromPos(0f, true, false, enemyAnchor))
                    {
                        return false;
                    }

                    float navDistance = Utils.Utils.GetNavDistance(botOwner.Position, point.Position);
                    if (!IsFinite(navDistance) ||
                        navDistance < HealCoverMinNavDistance ||
                        navDistance > HealCoverMaxNavDistance)
                    {
                        return false;
                    }

                    float candidateEnemyDistance = Vector3.Distance(point.Position, enemyAnchor);
                    return candidateEnemyDistance + HealCoverMinEnemyDistanceGain >= currentEnemyDistance;
                });

            return cover != null;
        }

        private bool IsCommittedHealCoverValid(EnemyInfo? goalEnemy = null)
        {
            if (committedHealCover == null)
            {
                return false;
            }

            if (!committedHealCover.IsFreeById(botOwner.Id) || committedHealCover.IsSpotted)
            {
                committedHealCover = null;
                return false;
            }

            if (goalEnemy != null)
            {
                Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
                if (IsFinite(enemyAnchor) && !committedHealCover.CanIHideFromPos(0f, true, false, enemyAnchor))
                {
                    committedHealCover = null;
                    return false;
                }
            }

            return true;
        }

        private void ClearCommittedHealCover()
        {
            committedHealCover = null;
            committedHealMoveAction = default;
            committedHealMoveReason = null;
        }

        /// <summary>
        /// Assign a retreat/attack cover point opposite the enemy relative to the boss anchor.
        /// Returns true when a valid cover was assigned to BotCurrentCoverInfo.
        /// </summary>
        public bool TryAssignRetreatAttackCover(
            EnemyInfo goalEnemy,
            bool requireShootLane,
            float maxBossDistanceSqr = 100f,
            bool allowSpotted = false)
        {
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyPosition = IsFinite(goalEnemy.CurrPosition) ? goalEnemy.CurrPosition : goalEnemy.EnemyLastPositionReal;
            Vector3 awayFromEnemy = bossPosition - enemyPosition;
            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = botOwner.Position - enemyPosition;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = Vector3.back;
            }

            Vector3 retreatAnchor = bossPosition + awayFromEnemy.normalized * 6f;
            ShootPointClass? shootPoint = requireShootLane ? botOwner.CurrentEnemyTargetPosition(true) : null;

            CustomNavigationPoint? retreatCover = Covers.GetClosestCoverPoint(
                botOwner,
                retreatAnchor,
                18f,
                point =>
                {
                    if (!IsCoverUsable(point, allowSpotted))
                    {
                        return false;
                    }

                    if ((point.Position - botOwner.Position).sqrMagnitude > maxBossDistanceSqr)
                    {
                        return false;
                    }

                    if (shootPoint != null && !Utils.Utils.CanShootToTarget(shootPoint, point, botOwner.LookSensor.Mask, false))
                    {
                        return false;
                    }

                    return true;
                });

            if (retreatCover == null)
            {
                return false;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(retreatCover, true);
            return true;
        }

        /// <summary>
        /// Finds a safe boss-local cover to use when the follower needs to reanchor or protect the boss.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, float searchRadius, out CustomNavigationPoint? cover)
        {
            return TryFindBossCover(goalEnemy, GetBossPosition(), searchRadius, out cover);
        }

        /// <summary>
        /// Finds a safe boss-local cover around the supplied boss anchor.
        /// </summary>
        public bool TryFindBossCover(EnemyInfo goalEnemy, Vector3 bossPosition, float searchRadius, out CustomNavigationPoint? cover)
        {
            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            CustomNavigationPoint? candidate = Covers.GetClosestCoverPoint(
                botOwner,
                bossPosition,
                searchRadius,
                point =>
                {
                    if (!IsCoverUsable(point, true))
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude < 2f * 2f)
                    {
                        return false;
                    }

                    return !IsFinite(enemyAnchor) || point.CanIHideFromPos(0f, true, false, enemyAnchor);
                });

            if (candidate == null)
            {
                cover = null;
                return false;
            }

            if ((candidate.Position - bossPosition).sqrMagnitude < 2f * 2f)
            {
                cover = null;
                return false;
            }

            if (IsFinite(enemyAnchor) && !candidate.CanIHideFromPos(0f, true, false, enemyAnchor))
            {
                cover = null;
                return false;
            }

            cover = candidate;
            return true;
        }

        private bool TryGetGeneralStartCover(EnemyInfo goalEnemy, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (goalEnemy == null)
            {
                return false;
            }

            Vector3 enemyPosition = goalEnemy.CurrPosition;
            if (!IsFinite(enemyPosition))
            {
                enemyPosition = goalEnemy.EnemyLastPositionReal;
            }

            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out hasShootLane);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance)
        {
            return TryGetSupportCover(enemyPosition, out cover, out navDistance, out _);
        }

        private bool TryGetSupportCover(Vector3 enemyPosition, out CustomNavigationPoint? cover, out float navDistance, out bool hasShootLane)
        {
            cover = null;
            navDistance = float.MaxValue;
            hasShootLane = false;

            if (!IsFinite(enemyPosition))
            {
                return false;
            }

            ShootPointClass shootPoint = new ShootPointClass(enemyPosition + Vector3.up * 1.1f, 1f);
            LayerMask mask = botOwner.LookSensor.Mask;

            cover = Covers.GetClosestCoverPoint(
                botOwner,
                botOwner.Position,
                35f,
                point => point != null &&
                         !point.IsSpotted &&
                         point.IsFreeById(botOwner.Id) &&
                         Utils.Utils.CanShootToTarget(shootPoint, point, mask, false));

            if (cover == null)
            {
                return false;
            }

            navDistance = Utils.Utils.GetNavDistance(botOwner.Position, cover.Position);
            if (!IsFinite(navDistance))
            {
                navDistance = Vector3.Distance(botOwner.Position, cover.Position);
            }

            hasShootLane = true;
            return true;
        }

        /// <summary>
        /// Picks the best available enemy anchor for blind pushes and cover searches.
        /// </summary>
        public static Vector3 GetEnemyAnchor(EnemyInfo goalEnemy)
        {
            if (IsFinite(goalEnemy.CurrPosition) && goalEnemy.CurrPosition.sqrMagnitude > 0.01f)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        public AICoreActionResultStruct<BotLogicDecision, GClass26> EnemySearch(string reason = "enemySearch")
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "enemySearchNoEnemy");
            }

            Vector3 enemyAnchor = GetEnemyAnchor(goalEnemy);
            Vector3 searchPoint = enemyAnchor;

            // Prefer an approach cover with a clear shot from a nearby tactical point.
            CustomNavigationPoint? approachCover = GetApproachableCover();
            if (approachCover != null)
            {
                searchPoint = approachCover.Position;
            }
            else if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit hit, 8f, -1))
            {
                ShootPointClass shootPoint = new ShootPointClass(enemyAnchor + Vector3.up * 1.1f, 1f);
                Vector3 firePos = hit.position + Vector3.up * 1.2f;
                if (Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    searchPoint = hit.position;
                }
            }

            botOwner.GoToSomePointData.SetPoint(searchPoint);
            botOwner.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, reason);
        }
        private void SetCover(CustomNavigationPoint? cover)
        {
            if (cover == null)
            {
                return;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(cover, true);
        }

        private bool TryGetAllyEngagementEnemy(out string enemyProfileId, out Vector3 enemyPosition)
        {
            enemyProfileId = string.Empty;
            enemyPosition = Vector3.zero;

            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            if (boss.IsPlayerEngaging(out string playerEnemyProfileId, out Vector3 playerEnemyPosition) &&
                !string.IsNullOrEmpty(playerEnemyProfileId) &&
                IsFinite(playerEnemyPosition))
            {
                enemyProfileId = playerEnemyProfileId;
                enemyPosition = playerEnemyPosition;
                return true;
            }

            foreach (BotOwner followerBot in boss.Followers)
            {
                if (followerBot == null || followerBot == botOwner || followerBot.IsDead || followerBot.Memory?.GoalEnemy == null)
                {
                    continue;
                }

                EnemyInfo followerEnemy = followerBot.Memory.GoalEnemy;
                if (!followerEnemy.IsVisible || !followerEnemy.CanShoot || string.IsNullOrEmpty(followerEnemy.ProfileId))
                {
                    continue;
                }

                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(followerBot);
                if (followerData == null || !followerData.IsBotActivelyEngaging(followerEnemy.ProfileId))
                {
                    continue;
                }

                enemyProfileId = followerEnemy.ProfileId;
                enemyPosition = followerEnemy.CurrPosition;
                return IsFinite(enemyPosition);
            }

            return false;
        }

        /// <summary>
        /// Treats very recent visible contacts as an immediate-fire window so followers do not hesitate
        /// before taking their first shot.
        /// </summary>
        public bool ShouldShootImmediately()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool recentVisibleShoot =
                goalEnemy != null &&
                goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Time.time - goalEnemy.PersonalSeenTime < 1.5f;
            bool shootNow = ((goalEnemy != null && goalEnemy.Distance < botOwner.Settings.FileSettings.Shoot.SHOOT_IMMEDIATELY_DIST) ||
                             botOwner.BotsGroup.AnyBodyShootImmediately) &&
                            goalEnemy != null &&
                            goalEnemy.CanShoot &&
                            Time.time - goalEnemy.AddTime < 5f;

            bool launcherActive = botOwner.WeaponManager.UnderbarrelLauncherController.IsActive;
            botOwner.BotsGroup.AnyBodyShootImmediately = shootNow || recentVisibleShoot || launcherActive;
            return botOwner.BotsGroup.AnyBodyShootImmediately;
        }

        /// <summary>
        /// A committed cover run should only break for immediate fire if the visible contact is stable
        /// enough to be real, not just a one-frame LOS flicker while crossing geometry.
        /// </summary>
        public bool ShouldBreakRunToCoverForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (WasHitRecently(botOwner, 0.5f))
            {
                return true;
            }

            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        /// <summary>
        /// Push movement should only end for a firing transition when the shot is stable enough to
        /// capitalize on immediately, not on a brief visible/shootable flicker while advancing.
        /// </summary>
        public bool ShouldBreakAdvanceForImmediateFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy) || !goalEnemy.IsVisible || !goalEnemy.CanShoot)
            {
                return false;
            }

            if (Enemy.Distance(goalEnemy) > Enemy.EnemyDistance.Close &&
                Time.time - goalEnemy.PersonalSeenTime < StableVisibleImmediateFireSeconds)
            {
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            if (shootPoint == null)
            {
                return false;
            }

            return Utils.Utils.CanShootToTarget(shootPoint, botOwner.WeaponRoot.position, botOwner.LookSensor.Mask, false);
        }

        /// <summary>
        /// Verifies that the follower can actually fire from the current cover, with a direct line-of-sight
        /// fallback when EFT's cover cast says no but the shot is still physically clear.
        /// </summary>
        public bool CanShootFromCurrentCover(out string cause)
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                cause = "noActiveEnemy";
                return false;
            }

            if (!goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                cause = "enemyNotShootable";
                return false;
            }

            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            if (botOwner.Memory.CurCustomCoverPoint == null)
            {
                cause = "noCurrentCoverPoint";
                return false;
            }

            if (!botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            if (!botOwner.Memory.CurCustomCoverPoint.CanShootToTargetCast(
                    botOwner,
                    botOwner.Settings.FileSettings.Cover.DELTA_SEEN_FROM_COVE_LAST_POS))
            {
                ShootPointClass? shootPoint = botOwner.CurrentEnemyTargetPosition(true);
                Vector3 firePos = botOwner.WeaponRoot.position;
                if (shootPoint == null || !Utils.Utils.CanShootToTarget(shootPoint, firePos, botOwner.LookSensor.Mask, false))
                {
                    cause = "CanShootToTargetCast";
                    return false;
                }
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                cause = "EndSho";
                return false;
            }

            cause = "allFine";
            return true;
        }

        private bool EnemyPathCrossesRecentDoor(EnemyInfo enemy)
        {
            NavMeshDoorLink nearestDoor = botOwner.NearDoorData.GetNearestDoor();
            if (nearestDoor == null)
            {
                return false;
            }

            Vector3 from = botOwner.Transform.position;
            Vector3 to = enemy.CurrPosition;
            GClass365 segment = new GClass365(from, to);
            Vector3 delta = nearestDoor.SegmentOpen.b - nearestDoor.SegmentOpen.a;
            Vector3 a = nearestDoor.SegmentOpen.a - delta * 0.1f;
            Vector3 b = nearestDoor.SegmentOpen.b + delta * 0.1f;
            return GClass369.GetCrossPoint(segment.a, segment.b, a, b) != null;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Check if the current enemy is low threat based on equipment, and number of nearby enemies.
        /// </summary>
        public bool IsEnemyLowThreat(EnemyInfo goalEnemy, bool ignoreEquip = false, float maximumEnemies = 2)
        {
            if (!ignoreEquip && dangerTimer > Time.time) return dangerResult;
            else if (ignoreEquip && dangerIgnoreEquipTimer > Time.time) return dangerIgnoreEquipResult;

            if (!ignoreEquip)
            {
                dangerTimer = Time.time + 1f;
                dangerResult = botOwner.Memory.AttackImmediately && Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) <= maximumEnemies;

                return dangerResult;
            }
            else
            {
                dangerIgnoreEquipTimer = Time.time + 1f;
                dangerIgnoreEquipResult = Utils.Enemy.GetEnemiesAtLocation(botOwner, goalEnemy, goalEnemy.CurrPosition) < 3;

                return dangerIgnoreEquipResult;
            }
        }

        /// <summary>
        /// Check if there is a reliable known position of the goal enemy (visible or recently seen with valid position).
        /// </summary>
        public bool HasReliablePersonalEnemyLocation(EnemyInfo goalEnemy)
        {
            if (goalEnemy.IsVisible)
            {
                return true;
            }

            Vector3 personalLastPos = goalEnemy.PersonalLastPos;
            return !float.IsNaN(personalLastPos.x) &&
                   !float.IsNaN(personalLastPos.y) &&
                   !float.IsNaN(personalLastPos.z) &&
                   !float.IsInfinity(personalLastPos.x) &&
                   !float.IsInfinity(personalLastPos.y) &&
                   !float.IsInfinity(personalLastPos.z) &&
                   (personalLastPos - botOwner.Position).sqrMagnitude > 0.01f;
        }

        /// <summary>
        /// Check if follower is critically wounded based on recent damage and hit frequency.
        /// Blocks aggressive pushes when critically injured.
        /// </summary>
        public bool IsFollowerCriticallyWounded()
        {
            bool multipleRecentHits = WasHitRecently(botOwner, 1.5f) && Time.time - botOwner.Memory.LastTimeHit - 0.5f > 0f;
            bool heavyFire = botOwner.Memory.IsUnderFire && WasHitRecently(botOwner, 3f);
            return multipleRecentHits || heavyFire;
        }

        /// <summary>
        /// Check if follower is injured and should avoid aggressive advances.
        /// Prefers cover-holding or cautious movement when injured and under recent fire.
        /// </summary>
        public bool IsFollowerInjured()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool underThreat = botOwner.Memory.IsUnderFire || (goalEnemy != null && goalEnemy.IsVisible);
            return WasHitRecently(botOwner, 2.5f) && underThreat;
        }

        /// <summary>
        /// Check if boss/player wants to kill the current enemy (not just protect).
        /// </summary>
        public bool ProtectWantKill(float maxEnemyDistance = 50f)
        {
            return Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal <
                   botOwner.Settings.FileSettings.Mind.ATTACK_ENEMY_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        /// <summary>
        /// Check if follower should care about protecting/holding boss position.
        /// </summary>
        public bool ProtectCareKill(float maxEnemyDistance = 50f)
        {
            float protectSeenTime = Time.time - botOwner.BotsGroup.EnemyLastSeenTimeReal;
            return protectSeenTime < botOwner.Settings.FileSettings.Mind.HOLD_IF_PROTECT_DELTA_LAST_TIME_SEEN &&
                   botOwner.Memory.GoalEnemy != null &&
                   botOwner.Memory.GoalEnemy.Distance <= maxEnemyDistance;
        }

        public static bool WasHitRecently(BotOwner bot, float seconds)
        {
            return Time.time - bot.Memory.LastTimeHit < seconds;
        }

        /// <summary>
        /// Shared dogfight-state probe used by both decision and end-condition logic.
        /// </summary>
        public bool IsDogFightActive() => botOwner.DogFight.DogFightState > BotDogFightStatus.none;

        // ──────────────────────────────────────────────────────────────────────────
        // End-condition dispatch
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Shared end-condition dispatcher.
        /// Keep this focused on decisions that are common across follower combat implementations,
        /// so specialized logic classes can override before/after this call without duplicating base behavior.
        /// </summary>
        public AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return currentDecision.Action switch
            {
                BotLogicDecision.dogFight => EndDogFight(),
                BotLogicDecision.shootToSmoke => EndImmediately(),
                BotLogicDecision.runToCover => EndRunToCover(currentDecision.Reason),
                BotLogicDecision.attackMoving => EndAttackMoving(),
                BotLogicDecision.attackMovingWithSuppress => EndAttackMovingWithSuppress(),
                BotLogicDecision.shootFromPlace => EndShootFromPlace(),
                BotLogicDecision.heal => EndHeal(),
                BotLogicDecision.healStimulators => EndStimulators(),
                BotLogicDecision.suppressFire => EndSuppressFire(),
                BotLogicDecision.shootFromCover => EndShootFromCover(),
                BotLogicDecision.throwGrenadeFromPlace => EndThrowGrenadeFromPlace(),
                _ => EndImmediately(),
            };
        }

        public AICoreActionEndStruct EndDogFight()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if ((goalEnemy == null || goalEnemy.Distance > botOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT) &&
                !botOwner.WeaponManager.Reload.Reloading &&
                !botOwner.Memory.BotCurrentCoverInfo.UseDogFight(botOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE))
            {
                return new AICoreActionEndStruct("dogFightOutOfRange", true);
            }

            if (goalEnemy == null || !goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                return new AICoreActionEndStruct("dogFightNoValidEnemy", true);
            }

            return Continue();
        }

        /// <summary>
        /// Common run-to-cover stop conditions.
        /// Specialized logic can short-circuit this in its own dispatcher when needed.
        /// </summary>
        public AICoreActionEndStruct EndRunToCover(string? reason = null)
        {
            if (ShouldBreakRunToCoverForImmediateFire())
            {
                return new AICoreActionEndStruct("stableImmediateFire", true);
            }

            bool isRunToHeal = string.Equals(reason, "runToHeal", StringComparison.Ordinal);
            if (!isRunToHeal)
            {
                RefreshShootCover();
            }

            if (botOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("alreadyInCover", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (!isRunToHeal &&
                botOwner.Memory.CurCustomCoverPoint != null &&
                botOwner.Memory.CurCustomCoverPoint.IsSpotted)
            {
                return new AICoreActionEndStruct("coverSpotted", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndAttackMoving()
        {
            RefreshShootCover();
            if (HaveCoverToShoot && botOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("foundCoverToShoot", true);
            }

            return EndBaseAttackMoving();
        }

        public AICoreActionEndStruct EndAttackMovingWithSuppress()
        {
            return EndAttackMoving();
        }

        public AICoreActionEndStruct EndShootFromPlace()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (botOwner.DogFight.ShallStartCauseHavePlace())
            {
                return new AICoreActionEndStruct("dogFightHavePlace", true);
            }

            if (!goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyCannotShoot", true);
            }

            if (ShouldShootImmediately())
            {
                return Continue();
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (goalEnemy.Distance < 1f)
            {
                return new AICoreActionEndStruct("enemyTooClose", true);
            }

            if (botOwner.WeaponManager.Reload.Reloading)
            {
                return new AICoreActionEndStruct("reloading", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndHeal()
        {
            bool haveHealWork = botOwner.Medecine.FirstAid.Have2Do || botOwner.Medecine.SurgicalKit.HaveWork;
            bool activelyHealing = botOwner.Medecine.FirstAid.Using || botOwner.Medecine.SurgicalKit.Using;
            if (!haveHealWork)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healCompleted", true);
            }

            // If the heal action never transitions into active first-aid/surgery use, do not let the
            // bot sit in healInCover forever waiting on a stuck vanilla node.
            if (!activelyHealing &&
                healStartedAt > 0f &&
                healStartedAt + 3f < Time.time)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healIdleTimedOut", true);
            }

            float timeout = botOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartedAt > 0f && healStartedAt + timeout < Time.time)
            {
                CompleteActiveHeal();
                return new AICoreActionEndStruct("healTimedOut", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndStimulators()
        {
            if (!botOwner.Medecine.Stimulators.Using)
            {
                stimStartedAt = 0f;
                return new AICoreActionEndStruct("stimsCompleted", true);
            }

            if (stimStartedAt > 0f && stimStartedAt + 5f < Time.time)
            {
                botOwner.Medecine.Stimulators.CancelCurrent();
                stimStartedAt = 0f;
                return new AICoreActionEndStruct("stimsTimedOut", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndSuppressFire()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (ShouldShootImmediately())
            {
                return new AICoreActionEndStruct("shootImmediately", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            // If enemy cannot be shot (not visible or can't shoot), suppress fire ends
            if (goalEnemy != null && (!goalEnemy.CanShoot || !goalEnemy.IsVisible))
            {
                return new AICoreActionEndStruct("enemyNotShootable", true);
            }

            return Continue();
        }

        private void CancelActiveHealIfNeeded()
        {
            ClearCommittedHealCover();
            FollowerMedical.CancelActiveMedical(botOwner);
        }

        private void CompleteActiveHeal()
        {
            ClearCommittedHealCover();
            FollowerMedical.CompleteHealing(botOwner);
            healBlockUntil = Time.time + 5f;
            healStartedAt = 0f;
        }

        public AICoreActionEndStruct EndShootFromCover()
        {
            if (CanShootFromCurrentCover(out string cause))
            {
                return Continue();
            }

            return new AICoreActionEndStruct(cause, true);
        }

        public AICoreActionEndStruct EndThrowGrenadeFromPlace()
        {
            BotRequest? currentRequest = botOwner.BotRequestController?.CurRequest;
            if (currentRequest?.BotRequestType == BotRequestType.throwGrenade)
            {
                return Continue();
            }

            FollowerGrenadeRuntimeGate.EnforceDisabled(botOwner);
            return new AICoreActionEndStruct("grenadeRequestFinished", true);
        }

        public AICoreActionEndStruct EndBaseGoToPoint()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (botOwner.GoToSomePointData.IsCome())
            {
                return new AICoreActionEndStruct("arrivedAtPoint", true);
            }

            return Continue();
        }

        public AICoreActionEndStruct EndBaseGoToEnemy()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!HasActiveCombatEnemy(goalEnemy))
            {
                return new AICoreActionEndStruct("enemyMissingOrDead", true);
            }

            if (botOwner.Memory.IsUnderFire)
            {
                return new AICoreActionEndStruct("underFire", true);
            }

            if (ShouldBreakAdvanceForImmediateFire())
            {
                return new AICoreActionEndStruct("stableAdvanceFire", true);
            }

            if (!IsDogFightActive() && (!goalEnemy.IsVisible || !goalEnemy.CanShoot))
            {
                return Continue();
            }

            return new AICoreActionEndStruct("dogFightConditionsMet", true);
        }

        public AICoreActionEndStruct EndBaseAttackMoving()
        {
            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightActive", true);
            }

            if (botOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("inCover", true);
            }

            if (botOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                return new AICoreActionEndStruct("stationary", true);
            }

            return Continue();
        }

        public void HoldFor(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            holdEndTime = Time.time + seconds;
            holdActive = true;
        }

        public static bool IsStableNoCoverHoldReason(string reason)
        {
            return string.Equals(reason, "goalEnemy.P", StringComparison.Ordinal) ||
                   string.Equals(reason, "canShootLas", StringComparison.Ordinal) ||
                   string.Equals(reason, "deltaLastHi", StringComparison.Ordinal) ||
                   string.Equals(reason, "unsafePushBossHold", StringComparison.Ordinal) ||
                   string.Equals(reason, "escortNoSafeCover", StringComparison.Ordinal);
        }

        public AICoreActionEndStruct EndBaseHoldPosition(string reason)
        {
            if (holdActive && holdEndTime < Time.time)
            {
                holdActive = false;
                return new AICoreActionEndStruct("holdExpired", true);
            }

            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            if (!botOwner.Memory.IsInCover)
            {
                if (!IsStableNoCoverHoldReason(reason))
                {
                    return new AICoreActionEndStruct("notInCover", true);
                }

                // No-cover hold reasons are allowed to crouch-wait, but not under active pressure.
                if (botOwner.Memory.IsUnderFire || WasHitRecently(botOwner, 0.5f))
                {
                    return new AICoreActionEndStruct("underFireNoCover", true);
                }
            }

            if (goalEnemy == null)
            {
                return new AICoreActionEndStruct("canSearchEnemy", true);
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemyVisibleAndShootable", true);
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < botOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
            {
                return new AICoreActionEndStruct("enemyCloseAndVisible", true);
            }

            return Continue();
        }

        /// <summary>
        /// Convenience terminal result for decisions that always end in one update.
        /// </summary>
        public static AICoreActionEndStruct EndImmediately() => new AICoreActionEndStruct(string.Empty, true);

        private static AICoreActionEndStruct Continue() => default;
    }
}
