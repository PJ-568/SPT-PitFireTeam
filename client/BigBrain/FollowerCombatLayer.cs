using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerPmcCombatLayer : CustomLayer
    {
        private static readonly HashSet<BotLogicDecision> LoggedUnsupportedDecisions = new HashSet<BotLogicDecision>();

        private readonly FollowerPmcCombatLogicBase? combatLogic;
        private readonly string brainShortName;

        private AICoreActionResultStruct<BotLogicDecision, GClass26>? currentDecision;

        public FollowerPmcCombatLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            brainShortName = botOwner?.Brain?.BaseBrain?.ShortName() ?? string.Empty;
            combatLogic = CreateCombatLogic(botOwner, brainShortName);
        }

        public override string GetName()
        {
            return "friendlySAIN.FollowerPmcCombat";
        }

        public override bool IsActive()
        {
            if (friendlySAIN.UseSainFollowerCombat || BotOwner == null || combatLogic == null)
            {
                return false;
            }

            if (BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer?.HealthController?.IsAlive != true)
            {
                return false;
            }

            if (!BossPlayers.IsFollower(BotOwner))
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss || BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer)
            {
                return false;
            }

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData != null &&
                followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                command == FollowerCommandType.RegroupNearBoss)
            {
                return false;
            }

            return combatLogic.ShallUseNow();
        }

        public override void Start()
        {
            base.Start();
            currentDecision = null;
            combatLogic?.Reset();
        }

        public override void Stop()
        {
            currentDecision = null;
            combatLogic?.Reset();
            base.Stop();
        }

        public override Action GetNextAction()
        {
            if (combatLogic == null)
            {
                return new Action(typeof(CombatHoldPositionAction), "MissingCombatLogic", new FollowerCombatActionData(null));
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision = combatLogic.GetDecision();
            combatLogic.DecisionChanged(currentDecision, nextDecision);
            currentDecision = nextDecision;

            return CreateBigBrainAction(nextDecision);
        }

        public override bool IsCurrentActionEnding()
        {
            if (!IsActive() || combatLogic == null || currentDecision == null)
            {
                return true;
            }

            AICoreActionEndStruct endResult = combatLogic.ShallEndCurrentDecision(currentDecision.Value);
            if (endResult.Value &&
                (currentDecision.Value.Action == BotLogicDecision.runToCover ||
                 currentDecision.Value.Action == BotLogicDecision.runToEnemy))
            {
                BotOwner.BotRun.EndMove();
            }

            return endResult.Value;
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.Append(" brain=");
            stringBuilder.Append(brainShortName);
            stringBuilder.Append(" decision=");
            stringBuilder.Append(currentDecision?.Action.ToString() ?? "<none>");
        }

        private static FollowerPmcCombatLogicBase? CreateCombatLogic(BotOwner botOwner, string shortName)
        {
            if (botOwner == null)
            {
                return null;
            }

            return shortName switch
            {
                "PmcBear" => new StandardFollowerPmcCombatLogic(botOwner),
                "PmcUsec" => new StandardFollowerPmcCombatLogic(botOwner),
                "PMC" => new StandardFollowerPmcCombatLogic(botOwner),
                "ExUsec" => new StandardFollowerPmcCombatLogic(botOwner),
                _ => CreateCombatLogicByRole(botOwner),
            };
        }

        private static FollowerPmcCombatLogicBase? CreateCombatLogicByRole(BotOwner botOwner)
        {
            WildSpawnType role = botOwner.Profile?.Info?.Settings?.Role ?? WildSpawnType.assault;

            return role switch
            {
                WildSpawnType.pmcBEAR => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.pmcUSEC => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.pmcBot => new StandardFollowerPmcCombatLogic(botOwner),
                WildSpawnType.exUsec => new StandardFollowerPmcCombatLogic(botOwner),
                _ => null,
            };
        }

        private static Action CreateBigBrainAction(AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            FollowerCombatActionData actionData = new FollowerCombatActionData(decision.Data);

            switch (decision.Action)
            {
                case BotLogicDecision.holdPosition:
                    return new Action(typeof(CombatHoldPositionAction), decision.Reason, actionData);
                case BotLogicDecision.runToCover:
                    return new Action(typeof(CombatRunToCoverAction), decision.Reason, actionData);
                case BotLogicDecision.attackMoving:
                    return new Action(typeof(CombatAttackMovingAction), decision.Reason, actionData);
                case BotLogicDecision.dogFight:
                    return new Action(typeof(CombatDogFightAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromPlace:
                    return new Action(typeof(CombatShootFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.shootFromCover:
                    return new Action(typeof(CombatShootFromCoverAction), decision.Reason, actionData);
                case BotLogicDecision.goToEnemy:
                    return new Action(typeof(CombatGoToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.runToEnemy:
                    return new Action(typeof(CombatRunToEnemyAction), decision.Reason, actionData);
                case BotLogicDecision.goToPoint:
                    return new Action(typeof(CombatGoToPointAction), decision.Reason, actionData);
                case BotLogicDecision.heal:
                    return new Action(typeof(HealAction), decision.Reason, actionData);
                case BotLogicDecision.throwGrenadeFromPlace:
                    return new Action(typeof(CombatThrowGrenadeFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.shootToSmoke:
                    return new Action(typeof(CombatShootToSmokeAction), decision.Reason, actionData);
                default:
                    if (LoggedUnsupportedDecisions.Add(decision.Action))
                    {
                        Modules.Logger.LogError($"[FollowerPmcCombat] Unsupported decision '{decision.Action}', falling back to hold.");
                    }

                    return new Action(typeof(CombatHoldPositionAction), $"Unsupported:{decision.Action}", actionData);
            }
        }
    }

    internal abstract class FollowerPmcCombatLogicBase
    {
        protected enum FollowerCombatStyle
        {
            HangBack,
            MoveForward,
        }

        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;

        protected bool HaveCoverToShoot;
        protected CustomNavigationPoint? PointToShoot;
        protected float CloseBossSqr = 100f;
        protected float BossPointRadius = 5f;
        protected float nextBossCoverCheckTime;
        protected bool haveNearBossCover;
        protected bool errorLogged;
        protected Vector3 lastBossPointSample;
        protected CustomNavigationPoint? nearBossCoverPoint;
        protected float nextShootCoverCheckTime;
        protected float lastGoToPointEndTime;
        protected string? pushCommitEnemyId;
        protected BotLogicDecision? pushCommitDecision;
        protected Vector3 combatAreaCenter;
        protected FollowerCombatStyle combatStyle;
        protected bool combatAreaInitialized;
        private BotLogicDecision? trackedAdvanceDecision;
        private Vector3 lastAdvanceProgressPosition;
        private float nextAdvanceProgressCheckTime;
        private int stalledAdvanceChecks;
        private bool scheduleNextFrameEnd;
        private bool holdActive;
        private float holdEndTime;
        private const float PushEnemyMaxDistance = 41f;
        private const float CombatAreaExitDistance = 12f;
        private const float CombatAreaArrivalDistance = 8f;
        private const float AdvanceProgressCheckInterval = 1.25f;
        private const float AdvanceMinProgressDistance = 0.75f;
        private const int AdvanceStallEndThreshold = 3;

        protected FollowerPmcCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
        }

        public virtual void Reset()
        {
            scheduleNextFrameEnd = false;
            holdActive = false;
            holdEndTime = 0f;
            pushCommitEnemyId = null;
            pushCommitDecision = null;
            combatAreaCenter = Vector3.zero;
            combatStyle = FollowerCombatStyle.HangBack;
            combatAreaInitialized = false;
            trackedAdvanceDecision = null;
            lastAdvanceProgressPosition = Vector3.zero;
            nextAdvanceProgressCheckTime = 0f;
            stalledAdvanceChecks = 0;
        }

        public bool ShallUseNow()
        {
            return BotOwner.Memory.HaveEnemy;
        }

        public void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            BotLogicDecision action = nextDecision.Action;
            if (action != BotLogicDecision.shootFromStationary &&
                action != BotLogicDecision.debugStationary &&
                action != BotLogicDecision.debugStationaryInstantTake &&
                BotOwner.WeaponManager.Stationary.Taken)
            {
                BotOwner.WeaponManager.Stationary.DropCurWeapon(false, true);
            }
        }

        public virtual AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "!haveEnemy", null);
            }

            try
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFightDecision = TryGetDogFightDecision(goalEnemy);
                if (dogFightDecision != null)
                {
                    return dogFightDecision.Value;
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26>? inFightDecision = InFightLogic();
                if (inFightDecision != null)
                {
                    return inFightDecision.Value;
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26>? committedPushDecision = TryGetCommittedPushDecision(goalEnemy);
                if (committedPushDecision != null)
                {
                    return committedPushDecision.Value;
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26>? protectorEngageDecision = TryGetProtectorEngageDecision(goalEnemy);
                if (protectorEngageDecision != null)
                {
                    return protectorEngageDecision.Value;
                }

                if (ShouldShootImmediately())
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "shootNow", null);
                }

                return DecideCombat(goalEnemy);
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    Modules.Logger.LogError(ex);
                    errorLogged = true;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "erorrLoged", null);
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "erorrLoged2", null);
            }
        }

        public virtual AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (scheduleNextFrameEnd)
            {
                scheduleNextFrameEnd = false;
                return new AICoreActionEndStruct("nextFrameDr", true);
            }

            return currentDecision.Action switch
            {
                BotLogicDecision.dogFight => EndDogFight(),
                BotLogicDecision.shootToSmoke => EndImmediately(),
                BotLogicDecision.holdPosition => EndHoldPosition(),
                BotLogicDecision.runToCover => EndRunToCover(),
                BotLogicDecision.attackMoving => EndAttackMoving(),
                BotLogicDecision.shootFromPlace => EndShootFromPlace(),
                BotLogicDecision.goToEnemy => EndGoToEnemy(),
                BotLogicDecision.runToEnemy => EndRunToEnemy(),
                BotLogicDecision.heal => EndHeal(),
                BotLogicDecision.shootFromCover => EndShootFromCover(),
                BotLogicDecision.goToPoint => EndGoToPoint(),
                BotLogicDecision.throwGrenadeFromPlace => EndThrowGrenadeFromPlace(),
                _ => EndImmediately(),
            };
        }

        protected abstract AICoreActionResultStruct<BotLogicDecision, GClass26> DecideBossPositionAction(Vector3 bossPosition);

        protected virtual AICoreActionEndStruct EndShootFromCover()
        {
            if (CanShootFromCurrentCover(out string cause))
            {
                return Continue();
            }

            return new AICoreActionEndStruct(cause, true);
        }

        protected virtual AICoreActionEndStruct EndGoToPoint()
        {
            AICoreActionEndStruct result = EndBossPointMove();
            if (result.Value)
            {
                lastGoToPointEndTime = Time.time;
            }

            return result;
        }

        protected virtual AICoreActionEndStruct EndThrowGrenadeFromPlace()
        {
            BotRequest currentRequest = BotOwner.BotRequestController?.CurRequest;
            if (currentRequest?.BotRequestType == BotRequestType.throwGrenade)
            {
                return Continue();
            }

            return new AICoreActionEndStruct("throwGrenade", true);
        }

        protected virtual AICoreActionEndStruct EndHoldPosition()
        {
            RefreshShootCover();
            Vector3 bossPosition = GetBossPosition();
            if ((BotOwner.Position - bossPosition).sqrMagnitude > CloseBossSqr)
            {
                return new AICoreActionEndStruct(">CloseBoss", true);
            }

            if (HaveCoverToShoot && ProtectWantKill() && ProtectCareKill())
            {
                return new AICoreActionEndStruct("havecoverto", true);
            }

            return EndBaseHoldPosition();
        }

        protected virtual AICoreActionEndStruct EndDogFight()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if ((goalEnemy == null || goalEnemy.Distance > BotOwner.Settings.FileSettings.Mind.DOG_FIGHT_OUT) &&
                !BotOwner.WeaponManager.Reload.Reloading &&
                !BotOwner.Memory.BotCurrentCoverInfo.UseDogFight(BotOwner.Settings.FileSettings.Cover.DOG_FIGHT_AFTER_LEAVE))
            {
                return new AICoreActionEndStruct("DogFightOut", true);
            }

            if (goalEnemy == null || !goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                return new AICoreActionEndStruct("DogFightNoEnemy", true);
            }

            return Continue();
        }

        protected virtual AICoreActionEndStruct EndRunToCover()
        {
            if (ShouldShootImmediately())
            {
                return new AICoreActionEndStruct("ShootImmediately", true);
            }

            RefreshShootCover();
            if (BotOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("InCover", true);
            }

            if (!BotOwner.CanSprintPlayer)
            {
                return new AICoreActionEndStruct("CanSprintPl", true);
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("StartD", true);
            }

            if (BotOwner.Memory.CurCustomCoverPoint != null && BotOwner.Memory.CurCustomCoverPoint.IsSpotted)
            {
                return new AICoreActionEndStruct("IsSpotted", true);
            }

            return Continue();
        }

        protected virtual AICoreActionEndStruct EndRunToEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (ShouldEndAdvanceForStall(BotLogicDecision.runToEnemy))
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("advanceStuck", true);
            }

            if (IsPushCommitted(goalEnemy))
            {
                return Continue();
            }

            return Continue();
        }

        protected virtual AICoreActionEndStruct EndShootFromPlace()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                return new AICoreActionEndStruct("enemynull", true);
            }

            if (BotOwner.DogFight.ShallStartCauseHavePlace())
            {
                return new AICoreActionEndStruct("StartH", true);
            }

            if (!goalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("!enemy.CanS", true);
            }

            if (ShouldShootImmediately())
            {
                return Continue();
            }

            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("StartD", true);
            }

            if (goalEnemy.Distance < 1f)
            {
                return new AICoreActionEndStruct("enemy.Dista", true);
            }

            if (BotOwner.WeaponManager.Reload.Reloading)
            {
                return new AICoreActionEndStruct(".Reloa", true);
            }

            return Continue();
        }

        protected virtual AICoreActionEndStruct EndGoToEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (goalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (ShouldEndAdvanceForStall(BotLogicDecision.goToEnemy))
            {
                ClearPushCommit();
                return new AICoreActionEndStruct("advanceStuck", true);
            }

            if (IsPushCommitted(goalEnemy))
            {
                return Continue();
            }

            return EndBaseGoToEnemy();
        }

        protected virtual AICoreActionEndStruct EndAttackMoving()
        {
            RefreshShootCover();
            if (HaveCoverToShoot && BotOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("pmcFindCove", true);
            }

            return EndBaseAttackMoving();
        }

        protected virtual AICoreActionEndStruct EndBossPointMove()
        {
            RefreshShootCover();
            if (haveNearBossCover)
            {
                return new AICoreActionEndStruct("haveCoverN", true);
            }

            if ((BotOwner.GoToSomePointData.Point - GetBossPosition()).sqrMagnitude > BossPointRadius * BossPointRadius)
            {
                return new AICoreActionEndStruct(">CloseBoss", true);
            }

            if (BotOwner.GoToSomePointData.IsCome())
            {
                return new AICoreActionEndStruct("AtPoint", true);
            }

            return EndBaseGoToPoint();
        }

        protected virtual AICoreActionEndStruct EndHeal()
        {
            if (!BotOwner.Medecine.Using)
            {
                return new AICoreActionEndStruct("1", true);
            }

            return Continue();
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26> DecideCombat(EnemyInfo goalEnemy)
        {
            bool canShoot = goalEnemy.CanShoot;
            bool wantKill = ProtectWantKill();
            bool careKill = ProtectCareKill();
            UpdateCombatAreaStyle();
            RefreshShootCover();

            if (TryActivateFollowerGrenade(goalEnemy))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.throwGrenadeFromPlace, "FollowerGrenade", null);
            }

            if (!goalEnemy.IsVisible && BotOwner.SmokeGrenade.ShallShoot())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootToSmoke, "SmokeGrenad", null);
            }

            if (combatStyle == FollowerCombatStyle.MoveForward)
            {
                return DecideBossPositionAction(GetBossPosition());
            }

            if (HaveCoverToShoot && wantKill)
            {
                bool canShootLastPosition = CanShootLastKnownPosition(goalEnemy);
                bool seesShootableEnemy = IsEnemyVisibleAndShootable();
                if (canShootLastPosition && !seesShootableEnemy && Time.time - goalEnemy.PersonalSeenTime < 3f)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "canShootLas", null);
                }

                if (!seesShootableEnemy && goalEnemy.Distance > 10f)
                {
                    BotOwner.Memory.BotCurrentCoverInfo.SetCover(PointToShoot, true);
                    return BotOwner.CanSprintPlayer
                        ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "goalEnemy.D", null)
                        : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "goal.D", null);
                }

                if (BotOwner.Memory.IsInCover && BotOwner.Memory.CurCustomCoverPoint?.Id == PointToShoot?.Id)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, ".Memor", null);
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, ".Memor", null);
            }

            bool canStandAndShoot = false;
            if (canShoot)
            {
                BotOwner closestFriend = BotOwner.Covers.GetClosestFriend(out float friendDist);
                canStandAndShoot = friendDist >= LocalBotSettingsProviderClass.Core.MIN_DIST_CLOSE_DEF ||
                    closestFriend == null ||
                    closestFriend.Id > BotOwner.Id;
            }

            if (canStandAndShoot)
            {
                if (goalEnemy.IsVisible)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "goalEnemy.V", null);
                }

                if (!WasHitRecently(BotOwner.Settings.FileSettings.Boss.IF_I_HITTED_GO_AWAY_SEC_HIT) && !BotOwner.Memory.IsUnderFire)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "deltaLastHi", null);
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "deltaLastHi", null);
            }

            if (careKill)
            {
                if (!IsEnemyVisibleAndShootable() && Time.time - goalEnemy.PersonalSeenTime < 3f)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "goalEnemy.P", null);
                }

                if (wantKill)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToEnemy, "wantKill", null);
                }
            }

            Vector3 bossPosition = GetBossPosition();
            if ((BotOwner.Position - bossPosition).sqrMagnitude > CloseBossSqr)
            {
                return DecideBossPositionAction(bossPosition);
            }

            if (BotOwner.Memory.IsInCover)
            {
                if (BotOwner.Medecine.FirstAid.Have2Do &&
                    (BotOwner.Memory.LastEnemy == null ||
                     Time.time - BotOwner.Memory.LastEnemyTimeSeen > BotOwner.Settings.FileSettings.Mind.PROTECT_DELTA_HEAL_SEC))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "PROTECTDELT", null);
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "distToBoss", null);
            }

            if (HaveCoverToShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(HoldOrCover(BotOwner), "HaveCoverSh", null);
            }

            return DecideBossPositionAction(GetBossPosition());
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? InFightLogic()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (ShouldShootImmediately())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "ShootImmediately", null);
            }

            if (CanShootFromCurrentCover(out string cause))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, cause, null);
            }

            if (BotOwner.NearDoorData.RecentlyClosedDoorCheckTime + 0.3f < Time.time &&
                BotOwner.BotsGroup.EnemyLastSeenTimeReal + 7f >= Time.time &&
                goalEnemy != null &&
                EnemyPathCrossesRecentDoor(goalEnemy))
            {
                BotOwner.Memory.Spotted(false, null, null);
            }

            return null;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetDogFightDecision(EnemyInfo goalEnemy)
        {
            BotDogFightStatus dogFightState = BotOwner.DogFight?.DogFightState ?? BotDogFightStatus.none;
            if (dogFightState == BotDogFightStatus.dogFight)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg", null);
            }

            if (dogFightState == BotDogFightStatus.shootFromPlace)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp", null);
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < 18f &&
                goalEnemy.Distance > BotOwner.Settings.FileSettings.Mind.DOG_FIGHT_IN)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg", null);
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Enemy.Distance(BotOwner) <= Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "enemyVeryClose", null);
            }

            return null;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetProtectorEngageDecision(EnemyInfo goalEnemy)
        {
            if (!ShouldAttackImmediately(goalEnemy) ||
                !IsEnemyLowThreat(goalEnemy, 2f) ||
                !CanLeaveBossForPush() ||
                goalEnemy.Distance > PushEnemyMaxDistance)
            {
                return null;
            }

            Enemy.EnemyDistance distanceToEnemy = Enemy.Distance(BotOwner);
            if (distanceToEnemy <= Enemy.EnemyDistance.VeryClose && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight", null);
            }

            if (distanceToEnemy <= Enemy.EnemyDistance.Close)
            {
                BotLogicDecision pushDecision = goalEnemy.IsVisible || !BotOwner.CanSprintPlayer
                    ? BotLogicDecision.goToEnemy
                    : BotLogicDecision.runToEnemy;

                if (!Enemy.IsClosestEnemy(BotOwner) && distanceToEnemy <= Enemy.EnemyDistance.Mid)
                {
                    pushDecision = BotLogicDecision.goToEnemy;
                }

                CommitPush(goalEnemy, pushDecision);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushDecision, "pushEnemy", null);
            }

            if (distanceToEnemy == Enemy.EnemyDistance.Mid)
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    CommitPush(goalEnemy, BotLogicDecision.attackMoving);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "getInCloseSlow", null);
                }

                CommitPush(goalEnemy, BotLogicDecision.runToEnemy);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToEnemy, "getInCloseFast", null);
            }

            return null;
        }

        protected void CommitPush(EnemyInfo goalEnemy, BotLogicDecision decision)
        {
            pushCommitEnemyId = goalEnemy.ProfileId;
            pushCommitDecision = decision;
        }

        protected bool IsPushCommitted(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null ||
                string.IsNullOrEmpty(pushCommitEnemyId) ||
                !string.Equals(pushCommitEnemyId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                ClearPushCommit();
                return false;
            }

            if (!CanLeaveBossForPush())
            {
                ClearPushCommit();
                return false;
            }

            return true;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedPushDecision(EnemyInfo goalEnemy)
        {
            if (!IsPushCommitted(goalEnemy) || !pushCommitDecision.HasValue)
            {
                return null;
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushCommitDecision.Value, "pushCommit", null);
        }

        protected void ClearPushCommit()
        {
            pushCommitEnemyId = null;
            pushCommitDecision = null;
        }

        private bool ShouldEndAdvanceForStall(BotLogicDecision decision)
        {
            if (trackedAdvanceDecision != decision)
            {
                trackedAdvanceDecision = decision;
                stalledAdvanceChecks = 0;
                lastAdvanceProgressPosition = BotOwner.Position;
                nextAdvanceProgressCheckTime = Time.time + AdvanceProgressCheckInterval;
                return false;
            }

            if (!BotOwner.Mover.HasPathAndNoComplete)
            {
                stalledAdvanceChecks = 0;
                lastAdvanceProgressPosition = BotOwner.Position;
                nextAdvanceProgressCheckTime = Time.time + AdvanceProgressCheckInterval;
                return false;
            }

            if (nextAdvanceProgressCheckTime > Time.time)
            {
                return false;
            }

            float minProgressSqr = AdvanceMinProgressDistance * AdvanceMinProgressDistance;
            Vector3 currentPosition = BotOwner.Position;
            if ((currentPosition - lastAdvanceProgressPosition).sqrMagnitude >= minProgressSqr)
            {
                stalledAdvanceChecks = 0;
            }
            else
            {
                stalledAdvanceChecks++;
            }

            lastAdvanceProgressPosition = currentPosition;
            nextAdvanceProgressCheckTime = Time.time + AdvanceProgressCheckInterval;

            if (stalledAdvanceChecks < AdvanceStallEndThreshold)
            {
                return false;
            }

            trackedAdvanceDecision = null;
            stalledAdvanceChecks = 0;
            return true;
        }

        protected bool TryActivateFollowerGrenade(EnemyInfo goalEnemy)
        {
            if (!friendlySAIN.botGrenades.Value ||
                goalEnemy == null ||
                !goalEnemy.IsVisible ||
                goalEnemy.Person == null ||
                goalEnemy.Distance < 15f ||
                goalEnemy.Distance > 28f ||
                BotOwner.WeaponManager == null ||
                BotOwner.WeaponManager.IsMelee ||
                BotOwner.WeaponManager.Grenades == null ||
                !BotOwner.WeaponManager.Grenades.HaveGrenade ||
                BotOwner.BotRequestController == null ||
                BotOwner.BotRequestController.HaveActivatedRequests() ||
                BotOwner.Medecine.Using)
            {
                return false;
            }

            if (IsDogFightActive() ||
                BotOwner.Memory.IsUnderFire ||
                WasHitRecently(2f) ||
                Time.time - goalEnemy.FirstTimeSeen < 1.5f)
            {
                return false;
            }

            if (goalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                return false;
            }

            Vector3 targetPosition = goalEnemy.CurrPosition + Vector3.up;
            if (IsFriendlyTooCloseToGrenadeTarget(targetPosition, 8f))
            {
                return false;
            }

            return BotOwner.BotRequestController.TryActivateThrowGrenadeRequest(targetPosition, null, out _);
        }

        protected bool IsFriendlyTooCloseToGrenadeTarget(Vector3 targetPosition, float unsafeRadius)
        {
            float unsafeRadiusSqr = unsafeRadius * unsafeRadius;

            if (BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            Player bossPlayer = boss.realPlayer;
            if (bossPlayer != null && (bossPlayer.Position - targetPosition).sqrMagnitude <= unsafeRadiusSqr)
            {
                return true;
            }

            List<BotOwner> followers = boss.Followers;
            if (followers == null)
            {
                return false;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == BotOwner || follower.IsDead)
                {
                    continue;
                }

                if ((follower.Position - targetPosition).sqrMagnitude <= unsafeRadiusSqr)
                {
                    return true;
                }
            }

            return false;
        }

        protected void HoldFor(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            holdEndTime = Time.time + seconds;
            holdActive = true;
        }

        protected bool ProtectWantKill()
        {
            return Time.time - BotOwner.BotsGroup.EnemyLastSeenTimeReal <
                   BotOwner.Settings.FileSettings.Mind.ATTACK_ENEMY_IF_PROTECT_DELTA_LAST_TIME_SEEN;
        }

        protected bool ProtectCareKill()
        {
            return Time.time - GetProtectSeenTime() <
                   BotOwner.Settings.FileSettings.Mind.HOLD_IF_PROTECT_DELTA_LAST_TIME_SEEN;
        }

        protected Vector3 GetBossPosition()
        {
            return BotOwner.BotFollower.BossToFollow != null ? BotOwner.BotFollower.BossToFollow.Position : BotOwner.Position;
        }

        protected void UpdateCombatAreaStyle()
        {
            Vector3 bossPosition = GetBossPosition();
            if (!combatAreaInitialized)
            {
                combatAreaCenter = bossPosition;
                combatStyle = FollowerCombatStyle.HangBack;
                combatAreaInitialized = true;
                return;
            }

            if (combatStyle == FollowerCombatStyle.HangBack)
            {
                if ((bossPosition - combatAreaCenter).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
                {
                    combatStyle = FollowerCombatStyle.MoveForward;
                    combatAreaCenter = bossPosition;
                }
                return;
            }

            if ((BotOwner.Position - combatAreaCenter).sqrMagnitude <= CombatAreaArrivalDistance * CombatAreaArrivalDistance)
            {
                combatStyle = FollowerCombatStyle.HangBack;
            }
        }

        protected void RefreshBossCover()
        {
            if (nextBossCoverCheckTime >= Time.time)
            {
                return;
            }

            Vector3 bossPosition = GetBossPosition();
            nextBossCoverCheckTime = Time.time + 1f;
            CoverSearchData searchData = new CoverSearchData(
                bossPosition,
                BotOwner.CoverSearchInfo,
                CoverShootType.hide,
                CloseBossSqr,
                0f,
                CoverSearchType.closerToSelectedPoint,
                null,
                null,
                bossPosition,
                ECheckSHootHide.shootAndHide,
                new CoverSearchDefenceDataClass(0f),
                PointsArrayType.byShootType,
                true,
                null,
                null,
                "Default");

            nearBossCoverPoint = BotOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
            haveNearBossCover = nearBossCoverPoint != null &&
                                (bossPosition - nearBossCoverPoint.Position).sqrMagnitude < CloseBossSqr &&
                                !nearBossCoverPoint.IsSpotted;
        }

        protected void RefreshShootCover()
        {
            if (nextShootCoverCheckTime >= Time.time)
            {
                return;
            }

            nextShootCoverCheckTime = Time.time + 1f;
            Vector3 bossPosition = GetBossPosition();
            PointToShoot = FindFollowerShootCover();
            if (PointToShoot == null || !PointToShoot.IsFreeById(BotOwner.Id) || PointToShoot.IsSpotted)
            {
                HaveCoverToShoot = false;
                return;
            }

            if ((bossPosition - PointToShoot.Position).sqrMagnitude >= BotOwner.Settings.FileSettings.Boss.MAX_DIST_COVER_BOSS_SQRT)
            {
                HaveCoverToShoot = false;
                return;
            }

            HaveCoverToShoot = !ProtectCareKill() || PointToShoot.CanIShootToEnemy;
            if (HaveCoverToShoot && (BotOwner.Memory.CurCustomCoverPoint == null || BotOwner.Memory.CurCustomCoverPoint.Id != PointToShoot.Id))
            {
                BotOwner.Memory.BotCurrentCoverInfo.Spotted();
                BotOwner.Memory.BotCurrentCoverInfo.SetCover(PointToShoot, true);
            }
        }

        protected CustomNavigationPoint? FindFollowerShootCover()
        {
            Vector3 bossPosition = GetBossPosition();
            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            CoverShootType shootType = shootPoint != null ? CoverShootType.shoot : CoverShootType.hide;
            CoverSearchData searchData = new CoverSearchData(
                bossPosition,
                BotOwner.CoverSearchInfo,
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

            return BotOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
        }

        protected bool IsEnemyVisibleAndShootable()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            return goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible;
        }

        protected bool IsEnemyLowThreat(EnemyInfo goalEnemy, float maximumEnemies)
        {
            if (goalEnemy.ProfileId == null)
            {
                return false;
            }

            return Enemy.GetEnemiesAtLocation(BotOwner, goalEnemy, goalEnemy.CurrPosition) <= maximumEnemies;
        }

        protected bool IsDogFightActive()
        {
            return BotOwner.DogFight.DogFightState > BotDogFightStatus.none;
        }

        protected bool ShouldAttackImmediately(EnemyInfo goalEnemy)
        {
            return BotOwner.Memory.AttackImmediately ||
                   (goalEnemy.IsVisible && goalEnemy.CanShoot && Time.time - goalEnemy.PersonalSeenTime < 2f);
        }

        protected bool CanLeaveBossForPush()
        {
            if (BotOwner.Medecine?.Using == true || BotOwner.Medecine?.FirstAid?.Have2Do == true)
            {
                return false;
            }

            if (BotOwner.Memory.IsUnderFire || WasHitRecently(1.5f))
            {
                return false;
            }

            Vector3 bossPosition = GetBossPosition();
            return (BotOwner.Position - bossPosition).sqrMagnitude <= BotOwner.Settings.FileSettings.Boss.MAX_DIST_COVER_BOSS_SQRT;
        }

        protected bool CanShootLastKnownPosition(EnemyInfo enemyInfo)
        {
            Vector3 target = enemyInfo.EnemyLastPositionReal + Vector3.up * 1.6f;
            return !Physics.Linecast(BotOwner.WeaponRoot.position, target, out _, LayerMaskClass.HighPolyWithTerrainMask);
        }

        protected bool CanShootFromCurrentCover(out string cause)
        {
            if (!BotOwner.Memory.IsInCover)
            {
                cause = "IsInCover";
                return false;
            }

            if (!BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                cause = "EnoughDistToShoot";
                return false;
            }

            if (!BotOwner.Memory.CurCustomCoverPoint.CanShootToTargetCast(
                    BotOwner,
                    BotOwner.Settings.FileSettings.Cover.DELTA_SEEN_FROM_COVE_LAST_POS))
            {
                cause = "CanShootToTargetCast";
                return false;
            }

            if (BotOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                cause = "EndSho";
                return false;
            }

            cause = "allFine";
            return true;
        }

        protected bool ShouldShootImmediately()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            bool shootNow = ((goalEnemy != null && goalEnemy.Distance < BotOwner.Settings.FileSettings.Shoot.SHOOT_IMMEDIATELY_DIST) ||
                             BotOwner.BotsGroup.AnyBodyShootImmediately) &&
                            goalEnemy != null &&
                            goalEnemy.CanShoot &&
                            Time.time - goalEnemy.AddTime < 5f;

            bool launcherActive = BotOwner.WeaponManager.UnderbarrelLauncherController.IsActive;
            BotOwner.BotsGroup.AnyBodyShootImmediately = shootNow || launcherActive;
            return BotOwner.BotsGroup.AnyBodyShootImmediately;
        }

        protected bool EnemyPathCrossesRecentDoor(EnemyInfo enemy)
        {
            NavMeshDoorLink nearestDoor = BotOwner.NearDoorData.GetNearestDoor();
            if (nearestDoor == null)
            {
                return false;
            }

            Vector3 from = BotOwner.Transform.position;
            Vector3 to = enemy.CurrPosition;
            GClass365 segment = new GClass365(from, to);
            Vector3 delta = nearestDoor.SegmentOpen.b - nearestDoor.SegmentOpen.a;
            Vector3 a = nearestDoor.SegmentOpen.a - delta * 0.1f;
            Vector3 b = nearestDoor.SegmentOpen.b + delta * 0.1f;
            return GClass369.GetCrossPoint(segment.a, segment.b, a, b) != null;
        }

        protected bool WasHitRecently(float seconds)
        {
            return Time.time - BotOwner.Memory.LastTimeHit < seconds;
        }

        protected bool CanSearchEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            return goalEnemy == null ||
                   (!WasHitRecently(10f) &&
                    !goalEnemy.IsVisible &&
                    !goalEnemy.CanShoot &&
                    goalEnemy.CanISearch &&
                    BotOwner.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) &&
                    BotOwner.Memory.LastEnemyVisionOld(LocalBotSettingsProviderClass.Core.COVER_SECONDS_AFTER_LOSE_VISION));
        }

        protected float GetProtectSeenTime()
        {
            return BotOwner.Settings.FileSettings.Mind.PROTECT_TIME_REAL
                ? BotOwner.BotsGroup.EnemyLastSeenTimeReal
                : BotOwner.BotsGroup.EnemyLastSeenTimeSence;
        }

        protected BotLogicDecision HoldOrCover(BotOwner owner)
        {
            return owner.Memory.IsInCover ? BotLogicDecision.holdPosition : BotLogicDecision.goToCoverPoint;
        }

        protected AICoreActionEndStruct EndBaseGoToEnemy()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!IsDogFightActive() && goalEnemy != null && (!goalEnemy.IsVisible || !goalEnemy.CanShoot))
            {
                return Continue();
            }

            return new AICoreActionEndStruct("DogFightCan", true);
        }

        protected AICoreActionEndStruct EndBaseAttackMoving()
        {
            if (IsDogFightActive())
            {
                return new AICoreActionEndStruct("dog", true);
            }

            if (BotOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("inCvr", true);
            }

            if (BotOwner.WeaponManager.Stationary.ShallEndShootFromCurrent())
            {
                return new AICoreActionEndStruct("stationary", true);
            }

            return Continue();
        }

        protected AICoreActionEndStruct EndBaseGoToPoint()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null || (goalEnemy.IsVisible && goalEnemy.CanShoot))
            {
                return new AICoreActionEndStruct("Enemy", true);
            }

            if (BotOwner.GoToSomePointData.IsCome())
            {
                return new AICoreActionEndStruct("Come", true);
            }

            return Continue();
        }

        protected AICoreActionEndStruct EndBaseHoldPosition()
        {
            if (holdActive && holdEndTime < Time.time)
            {
                holdActive = false;
                return new AICoreActionEndStruct("EndHol", true);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!BotOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("IsInCover", true);
            }

            if (goalEnemy == null)
            {
                if (CanSearchEnemy())
                {
                    return new AICoreActionEndStruct("CanSearchEn", true);
                }
            }
            else
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    return new AICoreActionEndStruct("CanShoot", true);
                }

                if (goalEnemy.IsVisible &&
                    goalEnemy.Distance < BotOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
                {
                    return new AICoreActionEndStruct("CLOSEANDVIS", true);
                }
            }

            return Continue();
        }

        protected AICoreActionEndStruct EndImmediately()
        {
            return new AICoreActionEndStruct("Base logic", true);
        }

        protected AICoreActionEndStruct Continue()
        {
            return default;
        }
    }

    internal sealed class StandardFollowerPmcCombatLogic : FollowerPmcCombatLogicBase
    {
        public StandardFollowerPmcCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        protected override AICoreActionResultStruct<BotLogicDecision, GClass26> DecideBossPositionAction(Vector3 bossPosition)
        {
            RefreshBossCover();
            if (haveNearBossCover)
            {
                BotOwner.Memory.BotCurrentCoverInfo.SetCover(nearBossCoverPoint, true);
                return BotOwner.CanSprintPlayer
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "sDistCloseB", null)
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "sDistCloseB", null);
            }

            Vector3 target = SampleBossOffsetPoint(bossPosition);
            CustomNavigationPoint closestPoint = BotOwner.Covers.GetClosestPoint(target, null, false, 1000);
            if (closestPoint != null)
            {
                BotOwner.Memory.BotCurrentCoverInfo.SetCover(closestPoint, true);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "HaveCoverSh", null);
            }

            if (Time.time - lastGoToPointEndTime > 10f && NavMesh.SamplePosition(target, out NavMeshHit navMeshHit, BossPointRadius, -1))
            {
                lastBossPointSample = navMeshHit.position;
                BotOwner.GoToSomePointData.SetPoint(lastBossPointSample);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "HaveCoverSh", null);
            }

            if (BotOwner.Memory.IsInCover)
            {
                HoldFor(4f);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "HaveCoverSh", null);
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossNavMeshHit, 2f, -1))
            {
                BotOwner.GoToSomePointData.SetPoint(bossNavMeshHit.position);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss", null);
            }

            BotOwner.GoToSomePointData.SetPoint(bossPosition);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss", null);
        }

        private static Vector3 SampleBossOffsetPoint(Vector3 bossPosition)
        {
            float min = 0.5f;
            float max = 2.5f;
            float x = GClass856.Random(min, max) * GClass856.RandomSing();
            float z = GClass856.Random(min, max) * GClass856.RandomSing();
            return bossPosition + new Vector3(x, 0f, z);
        }
    }

}
