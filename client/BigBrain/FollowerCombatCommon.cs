using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using UnityEngine;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatCommon
    {
        private const float StartCloseCoverDistance = 25f;
        private const float StartSupportSuppressDistance = 30f;

        private readonly BotOwner botOwner;
        private AICoreActionResultStruct<BotLogicDecision, GClass26>? initialDecision;
        private float healBlockUntil;
        private float healStartedAt;
        private float stimStartedAt;
        private CustomNavigationPoint? committedHealCover;
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

        public FollowerCombatCommon(BotOwner botOwner)
        {
            this.botOwner = botOwner;
        }

        public bool HasInitialDecision => initialDecision.HasValue;

        public void Reset()
        {
            initialDecision = null;
            healBlockUntil = 0f;
            healStartedAt = 0f;
            stimStartedAt = 0f;
            committedHealCover = null;
            holdActive = false;
            holdEndTime = 0f;
            HaveCoverToShoot = false;
            PointToShoot = null;
            cachedClosestShootCover = null;
            nextClosestShootCoverCheckTime = 0f;
            nextApproachableCoverCheckTime = 0f;
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

            return goalEnemy.Person?.HealthController?.IsAlive != false;
        }

        public void RefreshShootCover()
        {
            if (nextShootCoverCheckTime >= Time.time)
            {
                return;
            }

            nextShootCoverCheckTime = Time.time + 1f;
            Vector3 bossPosition = GetBossPosition();
            PointToShoot = FindFollowerShootCover();
            if (PointToShoot == null || !PointToShoot.IsFreeById(botOwner.Id) || PointToShoot.IsSpotted)
            {
                HaveCoverToShoot = false;
                return;
            }

            if ((bossPosition - PointToShoot.Position).sqrMagnitude >= botOwner.Settings.FileSettings.Boss.MAX_DIST_COVER_BOSS_SQRT)
            {
                HaveCoverToShoot = false;
                return;
            }

            HaveCoverToShoot = !ProtectCareKill() || PointToShoot.CanIShootToEnemy;
            if (HaveCoverToShoot && (botOwner.Memory.CurCustomCoverPoint == null || botOwner.Memory.CurCustomCoverPoint.Id != PointToShoot.Id))
            {
                botOwner.Memory.BotCurrentCoverInfo.Spotted();
                botOwner.Memory.BotCurrentCoverInfo.SetCover(PointToShoot, true);
            }
        }

        private CustomNavigationPoint? FindFollowerShootCover()
        {
            Vector3 bossPosition = GetBossPosition();
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            Vector3 enemyAnchor = goalEnemy?.CurrPosition ?? Vector3.zero;
            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = botOwner.LookSensor.Mask;

            if (goalEnemy != null)
            {
                if (shootPoint != null)
                {
                    CustomNavigationPoint? directionalShootCover = Covers.GetClosestCoverPointTowardPoint(
                        botOwner, bossPosition, enemyAnchor, 25f,
                        cover => Utils.Utils.CanShootToTarget(shootPoint, cover, mask, false));
                    if (directionalShootCover != null)
                    {
                        return directionalShootCover;
                    }
                }

                CustomNavigationPoint? directionalCover = Covers.GetClosestCoverPointTowardPoint(
                    botOwner, bossPosition, enemyAnchor, 22f);
                if (directionalCover != null)
                {
                    return directionalCover;
                }
            }

            CoverShootType shootType = shootPoint != null ? CoverShootType.shoot : CoverShootType.hide;
            CoverSearchData searchData = new CoverSearchData(
                bossPosition,
                botOwner.CoverSearchInfo,
                shootType,
                LocalBotSettingsProviderClass.Core.START_DIST_TO_COV,
                0f,
                CoverSearchType.closerToSelectedPoint,
                shootPoint,
                null,
                bossPosition,
                ECheckSHootHide.shootAndHide,
                new CoverSearchDefenceDataClass(0f),
                PointsArrayType.byShootType,
                true,
                null,
                null,
                "Default");

            CustomNavigationPoint? point = botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
            if (point != null) return point;

            if (shootPoint != null)
            {
                point = Covers.GetClosestCoverPoint(botOwner, bossPosition, 25f,
                    cover => Utils.Utils.CanShootToTarget(shootPoint, cover, mask, false));
                if (point != null) return point;
            }

            return Covers.GetClosestCoverPoint(botOwner, bossPosition, 20f);
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

        private Vector3 GetBossPosition() => botOwner.BotFollower.BossToFollow?.Position ?? botOwner.Position;

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

        public void PrepareStartDecision(float enemydist = 25f)
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
            if (!goalEnemy.IsVisible && IsEnemyLowThreat(false, 1f))
            {
                BotLogicDecision weakEnemyDecision = goalEnemy.Distance <= enemydist
                    ? BotLogicDecision.goToEnemy
                    : BotLogicDecision.runToEnemy;

                initialDecision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(weakEnemyDecision, "startWeakEnemyPush");
                return;
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
                Enemy.Distance(botOwner) <= Enemy.EnemyDistance.VeryClose)
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

                healStartedAt = Time.time;
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
                    healStartedAt = Time.time;
                    ClearCommittedHealCover();
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "runToHeal");
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
                        healStartedAt = Time.time;
                        ClearCommittedHealCover();
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "healInCover");
                    }

                    if (TryAssignHealCover(goalEnemy, ref coverTried))
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "runToHeal");
                    }

                    healBlockUntil = Time.time + 3f;
                    return null;
                }

                if (TryAssignHealCover(goalEnemy, ref coverTried))
                {
                    return botOwner.CanSprintPlayer
                        ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "runToHeal")
                        : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "moveToHeal");
                }

                healBlockUntil = Time.time + 3f;
                return null;
            }

            if (TryAssignHealCover(goalEnemy, ref coverTried))
            {
                return botOwner.CanSprintPlayer && enemyProxyDistance > Enemy.ProxyDistance.VeryClose
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "runToHeal")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "moveToHeal");
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

            if (IsCommittedHealCoverValid())
            {
                SetCover(committedHealCover);
                return true;
            }

            if (TryAssignRetreatAttackCover(goalEnemy, false))
            {
                committedHealCover = botOwner.Memory.CurCustomCoverPoint;
                return true;
            }

            if (TryGetGeneralStartCover(goalEnemy, out CustomNavigationPoint? cover, out _, out _))
            {
                SetCover(cover);
                committedHealCover = cover;
                return true;
            }

            return false;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedHealMoveDecision(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null || botOwner.Memory.IsInCover || !IsCommittedHealCoverValid())
            {
                ClearCommittedHealCover();
                return null;
            }

            SetCover(committedHealCover);

            Enemy.ProxyDistance enemyProxyDistance = Enemy.DistanceProxy(botOwner, botOwner.Position);
            BotLogicDecision moveDecision = botOwner.CanSprintPlayer && enemyProxyDistance > Enemy.ProxyDistance.VeryClose
                ? BotLogicDecision.runToCover
                : BotLogicDecision.attackMoving;

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                moveDecision,
                moveDecision == BotLogicDecision.runToCover ? "runToHeal" : "moveToHeal");
        }

        private bool IsCommittedHealCoverValid()
        {
            if (committedHealCover == null)
            {
                return false;
            }

            if (committedHealCover.IsSpotted || !committedHealCover.IsFreeById(botOwner.Id))
            {
                committedHealCover = null;
                return false;
            }

            return true;
        }

        private void ClearCommittedHealCover()
        {
            committedHealCover = null;
        }

        /// <summary>
        /// Assign a retreat/attack cover point opposite the enemy relative to the boss anchor.
        /// Returns true when a valid cover was assigned to BotCurrentCoverInfo.
        /// </summary>
        public bool TryAssignRetreatAttackCover(EnemyInfo goalEnemy, bool requireShootLane, float maxBossDistanceSqr = 100f)
        {
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyPosition = goalEnemy.CurrPosition;
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
            ShootPointClass shootPoint = botOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = botOwner.LookSensor.Mask;

            CustomNavigationPoint? retreatCover = Covers.GetClosestCoverPoint(
                botOwner,
                retreatAnchor,
                18f,
                point =>
                {
                    if (point == null || point.IsSpotted || !point.IsFreeById(botOwner.Id))
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude > maxBossDistanceSqr)
                    {
                        return false;
                    }

                    if (!requireShootLane || shootPoint == null)
                    {
                        return true;
                    }

                    return Utils.Utils.CanShootToTarget(shootPoint, point, mask, false);
                });

            if (retreatCover == null)
            {
                return false;
            }

            botOwner.Memory.BotCurrentCoverInfo.Spotted();
            botOwner.Memory.BotCurrentCoverInfo.SetCover(retreatCover, true);
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

        public bool ShouldShootImmediately()
        {
            EnemyInfo? goalEnemy = botOwner.Memory.GoalEnemy;
            bool shootNow = ((goalEnemy != null && goalEnemy.Distance < botOwner.Settings.FileSettings.Shoot.SHOOT_IMMEDIATELY_DIST) ||
                             botOwner.BotsGroup.AnyBodyShootImmediately) &&
                            goalEnemy != null &&
                            goalEnemy.CanShoot &&
                            Time.time - goalEnemy.AddTime < 5f;

            bool launcherActive = botOwner.WeaponManager.UnderbarrelLauncherController.IsActive;
            botOwner.BotsGroup.AnyBodyShootImmediately = shootNow || launcherActive;
            return botOwner.BotsGroup.AnyBodyShootImmediately;
        }

        public bool CanShootFromCurrentCover(out string cause)
        {
            if (!botOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
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
                cause = "CanShootToTargetCast";
                return false;
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
        public bool IsEnemyLowThreat(bool ignoreEquip = false, float maximumEnemies = 2)
        {
            if (!ignoreEquip && dangerTimer > Time.time) return dangerResult;
            else if (ignoreEquip && dangerIgnoreEquipTimer > Time.time) return dangerIgnoreEquipResult;

            if (!botOwner.Memory.HaveEnemy) return true;

            if (!ignoreEquip)
            {
                dangerTimer = Time.time + 1f;
                dangerResult = botOwner.Memory.AttackImmediately && Utils.Enemy.GetEnemiesAtLocation(botOwner, botOwner.Memory.GoalEnemy, botOwner.Memory.GoalEnemy.CurrPosition) <= maximumEnemies;

                return dangerResult;
            }
            else
            {
                dangerIgnoreEquipTimer = Time.time + 1f;
                dangerIgnoreEquipResult = Utils.Enemy.GetEnemiesAtLocation(botOwner, botOwner.Memory.GoalEnemy, botOwner.Memory.GoalEnemy.CurrPosition) < 3;

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

            if (Time.time - goalEnemy.PersonalLastSeenTime > 12f)
            {
                return false;
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
                BotLogicDecision.runToCover => EndRunToCover(),
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
        public AICoreActionEndStruct EndRunToCover()
        {
            if (ShouldShootImmediately())
            {
                return new AICoreActionEndStruct("shootImmediately", true);
            }

            RefreshShootCover();
            if (botOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("alreadyInCover", true);
            }

            if (!botOwner.CanSprintPlayer)
            {
                return new AICoreActionEndStruct("cannotSprint", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dogFightStarted", true);
            }

            if (botOwner.Memory.CurCustomCoverPoint != null && botOwner.Memory.CurCustomCoverPoint.IsSpotted)
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
            if (!haveHealWork)
            {
                CancelActiveHealIfNeeded();
                healBlockUntil = Time.time + 5f;
                healStartedAt = 0f;
                return new AICoreActionEndStruct("healCompleted", true);
            }

            float timeout = botOwner.Medecine.SurgicalKit.Using ? 20f : 7f;
            if (healStartedAt > 0f && healStartedAt + timeout < Time.time)
            {
                CancelActiveHealIfNeeded();
                healBlockUntil = Time.time + 5f;
                healStartedAt = 0f;
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
            if (botOwner.Medecine.FirstAid.Using)
            {
                botOwner.Medecine.FirstAid.CancelCurrent();
            }
            else if (botOwner.Medecine.SurgicalKit.Using)
            {
                botOwner.Medecine.SurgicalKit.CancelCurrent();
            }
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

            if (goalEnemy.CanShoot && botOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return new AICoreActionEndStruct("enemyCanShoot", true);
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
