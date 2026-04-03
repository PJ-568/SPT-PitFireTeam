using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
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
                case BotLogicDecision.goToPointTactical:
                    return new Action(typeof(CombatGoToPointTacticalAction), decision.Reason, actionData);
                case BotLogicDecision.heal:
                    return new Action(typeof(HealAction), decision.Reason, actionData);
                case BotLogicDecision.search:
                    return new Action(typeof(CombatGoToPointTacticalAction), decision.Reason, actionData);
                case BotLogicDecision.throwGrenadeFromPlace:
                    return new Action(typeof(CombatThrowGrenadeFromPlaceAction), decision.Reason, actionData);
                case BotLogicDecision.shootToSmoke:
                    return new Action(typeof(CombatShootToSmokeAction), decision.Reason, actionData);
                case BotLogicDecision.goToCoverPoint:
                    return new Action(typeof(GoToCoverPointAction), decision.Reason, actionData);
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

        protected enum EscortTargetType
        {
            None,
            Cover,
            Point,
        }

        protected enum GroupSearchRole
        {
            None,
            Leader,
            Follower,
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
        protected CustomNavigationPoint? escortCoverPoint;
        protected Vector3 escortPoint;
        protected EscortTargetType escortTargetType;
        protected string? escortEnemyId;
        protected Vector3 escortBossAnchor;
        protected Vector3 escortEnemyAnchor;
        protected bool escortWantedShootLane;
        protected GroupSearchRole groupSearchRole;
        protected string? groupSearchEnemyId;
        protected string? groupSearchLeaderProfileId;
        protected Vector3 groupSearchBossAnchor;
        protected Vector3 groupSearchEnemyAnchor;
        protected Vector3 groupSearchLeaderAnchor;
        protected Vector3 groupSearchPoint;
        private bool scheduleNextFrameEnd;
        private bool holdActive;
        private float holdEndTime;
        private const float BalancedPushEnemyMaxDistance = 41f;
        private const float CombatAreaExitDistance = 12f;
        private const float CombatAreaArrivalDistance = 8f;
        private const float PostCombatHealDelay = 1f;
        private const float SurgeryHealDelayMultiplier = 1.5f;
        private const float CriticalHealthThreshold = 30f;  // Below 30%: retreat, no aggressive pushes
        private const float InjuredHealthThreshold = 60f;   // Below 60% + recent hit: avoid aggressive advances
        private const float RecentHitWindow = 2.5f;         // Track hits within 2.5s window
        private const float EscortEnemyMoveReevalDistance = 9f;
        private const float EscortEnemyAngleReeval = 30f;
        private const float GroupSearchJoinDistanceMin = 12f;
        private const float GroupSearchJoinDistanceMax = 30f;
        private const float GroupSearchSectorAngleTolerance = 55f;

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
            ClearEscortCommit();
            ClearGroupSearchCommit();
        }

        public bool ShallUseNow()
        {
            return BotOwner.Memory.HaveEnemy;
        }

        public void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (prevDecision.HasValue &&
                (prevDecision.Value.Action == BotLogicDecision.search ||
                 prevDecision.Value.Action == BotLogicDecision.goToPointTactical) &&
                nextDecision.Action != BotLogicDecision.search &&
                nextDecision.Action != BotLogicDecision.goToPointTactical)
            {
                ClearGroupSearchCommit();
            }

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
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "!haveEnemy");
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
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "shootNow");
                }

                return DecideCombat(goalEnemy);
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    Modules.Logger.LogError(ex);
                    errorLogged = true;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "erorrLoged");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "erorrLoged2");
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
                BotLogicDecision.holdPosition => EndHoldPosition(currentDecision.Reason),
                BotLogicDecision.runToCover => EndRunToCover(),
                BotLogicDecision.attackMoving => EndAttackMoving(),
                BotLogicDecision.shootFromPlace => EndShootFromPlace(),
                BotLogicDecision.goToEnemy => EndGoToEnemy(),
                BotLogicDecision.runToEnemy => EndRunToEnemy(),
                BotLogicDecision.heal => EndHeal(),
                BotLogicDecision.shootFromCover => EndShootFromCover(),
                BotLogicDecision.goToPoint => EndGoToPoint(),
                BotLogicDecision.search => EndGroupSearchLeader(),
                BotLogicDecision.goToPointTactical => EndGroupSearchFollower(),
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

        protected virtual AICoreActionEndStruct EndGroupSearchLeader()
        {
            return EndGroupSearchMove(GroupSearchRole.Leader);
        }

        protected virtual AICoreActionEndStruct EndGroupSearchFollower()
        {
            return EndGroupSearchMove(GroupSearchRole.Follower);
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

        protected virtual AICoreActionEndStruct EndHoldPosition(string reason)
        {
            if (string.Equals(reason, "distToBoss", StringComparison.Ordinal))
            {
                return EndDistToBossHoldPosition();
            }

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

        protected virtual AICoreActionEndStruct EndDistToBossHoldPosition()
        {
            if (!BotOwner.Memory.IsInCover)
            {
                return new AICoreActionEndStruct("IsInCover", true);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy != null &&
                HasEscortTarget() &&
                !IsEscortCommitStillValid(goalEnemy, GetBossPosition()))
            {
                return new AICoreActionEndStruct("escortInvalid", true);
            }

            if (goalEnemy != null)
            {
                if (goalEnemy.IsVisible &&
                    goalEnemy.Distance < BotOwner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
                {
                    return new AICoreActionEndStruct("CLOSEANDVIS", true);
                }

                if (goalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
                {
                    return new AICoreActionEndStruct("enemy.canSh", true);
                }

                if (CanShootFromCurrentCover(out _))
                {
                    return new AICoreActionEndStruct("canShootCover", true);
                }
            }

            return Continue();
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

            if (IsPushCommitted(goalEnemy))
            {
                return Continue();
            }

            return new AICoreActionEndStruct("pushEnded", true);
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
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy != null &&
                HasEscortTarget() &&
                !IsEscortCommitStillValid(goalEnemy, GetBossPosition()))
            {
                return new AICoreActionEndStruct("escortInvalid", true);
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

        protected virtual AICoreActionEndStruct EndGroupSearchMove(GroupSearchRole expectedRole)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                ClearGroupSearchCommit();
                return new AICoreActionEndStruct("groupSearchNoEnemy", true);
            }

            if (HasReliablePersonalEnemyLocation(goalEnemy) || (goalEnemy.IsVisible && goalEnemy.CanShoot))
            {
                ClearGroupSearchCommit();
                return new AICoreActionEndStruct("groupSearchAcquire", true);
            }

            if (!IsGroupSearchCommitStillValid(goalEnemy, GetBossPosition(), expectedRole))
            {
                return new AICoreActionEndStruct("groupSearchInvalid", true);
            }

            if (BotOwner.GoToSomePointData.IsCome() ||
                (BotOwner.Position - groupSearchPoint).sqrMagnitude <= 2.25f)
            {
                return new AICoreActionEndStruct("groupSearchArrived", true);
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
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.throwGrenadeFromPlace, "FollowerGrenade");
            }

            if (!goalEnemy.IsVisible && BotOwner.SmokeGrenade.ShallShoot())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootToSmoke, "SmokeGrenad");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? groupSearchDecision = TryGetGroupSearchDecision(goalEnemy);
            if (groupSearchDecision != null)
            {
                return groupSearchDecision.Value;
            }

            if (careKill &&
                !IsEnemyVisibleAndShootable() &&
                Time.time - goalEnemy.PersonalSeenTime < 3f)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "goalEnemy.P");
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? wantKillPushDecision =
                TryGetWantKillPushDecision(goalEnemy, careKill, wantKill);
            if (wantKillPushDecision != null)
            {
                return wantKillPushDecision.Value;
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
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "canShootLas");
                }

                if (!seesShootableEnemy && goalEnemy.Distance > 10f)
                {
                    BotOwner.Memory.BotCurrentCoverInfo.SetCover(PointToShoot, true);
                    return BotOwner.CanSprintPlayer
                        ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "goalEnemy.D")
                        : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "goal.D");
                }

                if (BotOwner.Memory.IsInCover && BotOwner.Memory.CurCustomCoverPoint?.Id == PointToShoot?.Id)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, ".Memor");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, ".Memor");
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
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "goalEnemy.V");
                }

                if (!WasHitRecently(BotOwner.Settings.FileSettings.Boss.IF_I_HITTED_GO_AWAY_SEC_HIT) && !BotOwner.Memory.IsUnderFire)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "deltaLastHi");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "deltaLastHi");
            }

            Vector3 bossPosition = GetBossPosition();
            if ((BotOwner.Position - bossPosition).sqrMagnitude > CloseBossSqr)
            {
                return DecideBossPositionAction(bossPosition);
            }

            if (BotOwner.Memory.IsInCover)
            {
                if (ShouldStartCoverHeal(goalEnemy))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.heal, "CoverHealDelay");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "distToBoss");
            }

            if (HaveCoverToShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(HoldOrCover(BotOwner), "HaveCoverSh");
            }

            return DecideBossPositionAction(GetBossPosition());
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? InFightLogic()
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (ShouldShootImmediately())
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "ShootImmediately");
            }

            if (CanShootFromCurrentCover(out string cause))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromCover, cause);
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
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
            }

            if (dogFightState == BotDogFightStatus.shootFromPlace)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.shootFromPlace, "cdgfp");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.Distance < 18f &&
                goalEnemy.Distance > BotOwner.Settings.FileSettings.Mind.DOG_FIGHT_IN)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "cdg");
            }

            if (goalEnemy.IsVisible &&
                goalEnemy.CanShoot &&
                Enemy.Distance(BotOwner) <= Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "enemyVeryClose");
            }

            return null;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetWantKillPushDecision(
            EnemyInfo goalEnemy,
            bool careKill,
            bool wantKill)
        {
            if (!careKill || !wantKill)
            {
                return null;
            }

            if (!HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            if (!IsPushEnabled())
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            if (!IsEnemyLowThreat(goalEnemy, 2f))
            {
                return GetNoPushSafePositionDecision(goalEnemy);
            }

            // Block aggressive advance if critically wounded; retreat to cover instead
            if (IsFollowerCriticallyWounded())
            {
                if (TryAssignRetreatAttackCover(goalEnemy, true))
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "critAtkRetreat");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "critHealth");
            }

            // Block aggressive advance if injured and recently hit; prefer cover
            if (IsFollowerInjured() && !HaveCoverToShoot)
            {
                TryAssignRetreatAttackCover(goalEnemy, false);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "injuredProc");
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToEnemy, "wantKill");
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetProtectorEngageDecision(EnemyInfo goalEnemy)
        {
            // Block aggressive push when critically wounded; only allows if enemy is very close
            if (IsFollowerCriticallyWounded() && goalEnemy.Distance > 15f)
            {
                return null;
            }

            if (!IsPushEnabled() ||
                !HasReliablePersonalEnemyLocation(goalEnemy) ||
                !ShouldAttackImmediately(goalEnemy) ||
                !IsEnemyLowThreat(goalEnemy, 2f) ||
                !CanLeaveBossForPush() ||
                goalEnemy.Distance > GetPushEnemyMaxDistance(goalEnemy))
            {
                return null;
            }

            Enemy.EnemyDistance distanceToEnemy = Enemy.Distance(BotOwner);
            if (distanceToEnemy <= Enemy.EnemyDistance.VeryClose && goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.dogFight, "pushDogFight");
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
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushDecision, "pushEnemy");
            }

            if (distanceToEnemy == Enemy.EnemyDistance.Mid)
            {
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    CommitPush(goalEnemy, BotLogicDecision.attackMoving);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "getInCloseSlow");
                }

                CommitPush(goalEnemy, BotLogicDecision.runToEnemy);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToEnemy, "getInCloseFast");
            }

            return null;
        }

        protected void CommitPush(EnemyInfo goalEnemy, BotLogicDecision decision)
        {
            ClearEscortCommit();
            ClearGroupSearchCommit();
            pushCommitEnemyId = goalEnemy.ProfileId;
            pushCommitDecision = decision;
        }

        protected void CommitEscortCover(EnemyInfo goalEnemy, Vector3 bossPosition, CustomNavigationPoint coverPoint, bool wantedShootLane)
        {
            ClearGroupSearchCommit();
            escortTargetType = EscortTargetType.Cover;
            escortCoverPoint = coverPoint;
            escortPoint = coverPoint.Position;
            escortEnemyId = goalEnemy.ProfileId;
            escortBossAnchor = bossPosition;
            escortEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            escortWantedShootLane = wantedShootLane;
        }

        protected void CommitEscortPoint(EnemyInfo goalEnemy, Vector3 bossPosition, Vector3 point)
        {
            ClearGroupSearchCommit();
            escortTargetType = EscortTargetType.Point;
            escortCoverPoint = null;
            escortPoint = point;
            escortEnemyId = goalEnemy.ProfileId;
            escortBossAnchor = bossPosition;
            escortEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            escortWantedShootLane = false;
        }

        protected void ClearEscortCommit()
        {
            escortTargetType = EscortTargetType.None;
            escortCoverPoint = null;
            escortPoint = Vector3.zero;
            escortEnemyId = null;
            escortBossAnchor = Vector3.zero;
            escortEnemyAnchor = Vector3.zero;
            escortWantedShootLane = false;
        }

        protected void CommitGroupSearchLeader(EnemyInfo goalEnemy, Vector3 bossPosition, Vector3 targetPoint)
        {
            ClearPushCommit();
            ClearEscortCommit();
            groupSearchRole = GroupSearchRole.Leader;
            groupSearchEnemyId = goalEnemy.ProfileId;
            groupSearchLeaderProfileId = BotOwner.ProfileId;
            groupSearchBossAnchor = bossPosition;
            groupSearchEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            groupSearchLeaderAnchor = BotOwner.Position;
            groupSearchPoint = targetPoint;
        }

        protected void CommitGroupSearchFollower(EnemyInfo goalEnemy, Vector3 bossPosition, BotOwner leader, Vector3 targetPoint)
        {
            ClearPushCommit();
            ClearEscortCommit();
            groupSearchRole = GroupSearchRole.Follower;
            groupSearchEnemyId = goalEnemy.ProfileId;
            groupSearchLeaderProfileId = leader.ProfileId;
            groupSearchBossAnchor = bossPosition;
            groupSearchEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            groupSearchLeaderAnchor = leader.Position;
            groupSearchPoint = targetPoint;
        }

        protected void ClearGroupSearchCommit()
        {
            groupSearchRole = GroupSearchRole.None;
            groupSearchEnemyId = null;
            groupSearchLeaderProfileId = null;
            groupSearchBossAnchor = Vector3.zero;
            groupSearchEnemyAnchor = Vector3.zero;
            groupSearchLeaderAnchor = Vector3.zero;
            groupSearchPoint = Vector3.zero;
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

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(pushCommitDecision.Value, "pushCommit");
        }

        protected void ClearPushCommit()
        {
            pushCommitEnemyId = null;
            pushCommitDecision = null;
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

            if (!FollowerGrenadeCooldowns.TryReserveThrow(BotOwner))
            {
                return false;
            }

            if (IsDogFightActive() ||
                BotOwner.Memory.IsUnderFire ||
                WasHitRecently(2f) ||
                Time.time - goalEnemy.FirstTimeSeen < 1.5f)
            {
                FollowerGrenadeCooldowns.CancelPending(BotOwner);
                return false;
            }

            if (goalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
            {
                FollowerGrenadeCooldowns.CancelPending(BotOwner);
                return false;
            }

            Vector3 targetPosition = goalEnemy.CurrPosition + Vector3.up;
            if (IsFriendlyTooCloseToGrenadeTarget(targetPosition, 8f))
            {
                FollowerGrenadeCooldowns.CancelPending(BotOwner);
                return false;
            }

            bool activated = BotOwner.BotRequestController.TryActivateThrowGrenadeRequest(targetPosition, null, out _);
            if (!activated)
            {
                FollowerGrenadeCooldowns.CancelPending(BotOwner);
            }

            return activated;
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

        protected bool ShouldStartCoverHeal(EnemyInfo? goalEnemy)
        {
            if (!HasPendingCoverHealWork())
            {
                return false;
            }

            if (ShouldHealImmediatelyInCover())
            {
                return true;
            }

            if (goalEnemy != null && (goalEnemy.IsVisible || goalEnemy.CanShoot))
            {
                return false;
            }

            float latestThreatSeenTime = GetLatestThreatSeenTime(goalEnemy);
            if (latestThreatSeenTime <= 0f)
            {
                return true;
            }

            float requiredQuietTime = BotOwner.Settings.FileSettings.Mind.PROTECT_DELTA_HEAL_SEC + GetAdditionalHealDelay();
            return Time.time - latestThreatSeenTime > requiredQuietTime;
        }

        protected bool HasPendingCoverHealWork()
        {
            return BotOwner.Medecine.FirstAid.Have2Do || BotOwner.Medecine.SurgicalKit.HaveWork;
        }

        protected bool ShouldHealImmediatelyInCover()
        {
            if (BotOwner.Medecine.FirstAid.IsBleeding)
            {
                return true;
            }

            if (GetTotalRealBodyHealthPercent() <= CriticalHealthThreshold)
            {
                return true;
            }

            return IsFollowerCriticallyWounded();
        }

        protected float GetAdditionalHealDelay()
        {
            return BotOwner.Medecine.SurgicalKit.HaveWork
                ? PostCombatHealDelay * SurgeryHealDelayMultiplier
                : PostCombatHealDelay;
        }

        protected float GetLatestThreatSeenTime(EnemyInfo? goalEnemy)
        {
            float latestThreatSeenTime = Mathf.Max(BotOwner.Memory.LastEnemyTimeSeen, BotOwner.BotsGroup.EnemyLastSeenTimeReal);
            if (goalEnemy != null)
            {
                latestThreatSeenTime = Mathf.Max(latestThreatSeenTime, goalEnemy.PersonalSeenTime);
            }

            return latestThreatSeenTime;
        }

        /// <summary>
        /// Check if follower is critically wounded based on recent damage and hit frequency.
        /// Blocks aggressive pushes (goToEnemy, runToEnemy) when critically injured.
        /// </summary>
        protected bool IsFollowerCriticallyWounded()
        {
            // Critically wounded if hit multiple times recently, or if they're currently under fire and recently hit
            bool multipleRecentHits = WasHitRecently(1.5f) && Time.time - BotOwner.Memory.LastTimeHit - 0.5f > 0f; // Approximate hit count
            bool heavyFire = BotOwner.Memory.IsUnderFire && WasHitRecently(3f);

            return multipleRecentHits || heavyFire;
        }

        /// <summary>
        /// Check if follower is injured and should avoid aggressive advances.
        /// Prefers cover-holding or cautious movement when injured and under recent fire.
        /// </summary>
        protected bool IsFollowerInjured()
        {
            // Injured if recently hit and still at risk (under fire or enemy still visible)
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            bool underThreat = BotOwner.Memory.IsUnderFire || (goalEnemy != null && goalEnemy.IsVisible);
            return WasHitRecently(RecentHitWindow) && underThreat;
        }

        protected float GetTotalRealBodyHealthPercent()
        {
            if (BotOwner?.GetPlayer?.ActiveHealthController == null)
            {
                return 100f;
            }

            float currentHealth = 0f;
            float maxHealth = 0f;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                ValueStruct partHealth = BotOwner.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                currentHealth += partHealth.Current;
                maxHealth += partHealth.Maximum;
            }

            if (maxHealth <= 0.01f)
            {
                return 100f;
            }

            return currentHealth / maxHealth * 100f;
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
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            Vector3 enemyAnchor = goalEnemy != null ? GetEscortEnemyAnchor(goalEnemy) : Vector3.zero;
            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = BotOwner.LookSensor.Mask;

            if (goalEnemy != null)
            {
                if (shootPoint != null)
                {
                    CustomNavigationPoint? directionalShootCover = Covers.GetClosestCoverPointTowardPoint(
                        BotOwner,
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
                    BotOwner,
                    bossPosition,
                    enemyAnchor,
                    22f);
                if (directionalCover != null)
                {
                    return directionalCover;
                }
            }

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

            // primary: vanilla boss-anchored search with shoot/hide eligibility
            CustomNavigationPoint? point = BotOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
            if (point != null) return point;

            // fallback 1: custom cover near boss with verified shoot LOS and teammate spacing
            if (shootPoint != null)
            {
                point = Covers.GetClosestCoverPoint(BotOwner, bossPosition, 25f,
                    cover => Utils.Utils.CanShootToTarget(shootPoint, cover, mask, false));
                if (point != null) return point;
            }

            // fallback 2: any cover near boss (teammate spacing still applied)
            return Covers.GetClosestCoverPoint(BotOwner, bossPosition, 20f);
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

        protected bool HasReliablePersonalEnemyLocation(EnemyInfo goalEnemy)
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
                   (personalLastPos - BotOwner.Position).sqrMagnitude > 0.01f;
        }

        protected bool CanGroupSearch(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null ||
                goalEnemy.IsVisible ||
                goalEnemy.CanShoot ||
                !goalEnemy.CanISearch ||
                HaveCoverToShoot ||
                CanShootLastKnownPosition(goalEnemy) ||
                !BotOwner.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) ||
                WasHitRecently(10f))
            {
                return false;
            }

            if (HasReliablePersonalEnemyLocation(goalEnemy))
            {
                return false;
            }

            return BotOwner.Memory.LastEnemyVisionOld(LocalBotSettingsProviderClass.Core.COVER_SECONDS_AFTER_LOSE_VISION);
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetGroupSearchDecision(EnemyInfo goalEnemy)
        {
            if (!CanGroupSearch(goalEnemy) ||
                BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss ||
                boss.realPlayer == null ||
                string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return null;
            }

            BotOwner? leader = FollowerGroupSearchRuntime.GetOrAssignLeader(boss, goalEnemy, IsValidGroupSearchCandidate);
            if (leader == null)
            {
                return null;
            }

            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            if (leader.ProfileId == BotOwner.ProfileId)
            {
                if (!TryGetGroupSearchLeaderPoint(goalEnemy, out Vector3 searchPoint))
                {
                    return null;
                }

                CommitGroupSearchLeader(goalEnemy, bossPosition, searchPoint);
                BotOwner.GoToSomePointData.SetPoint(searchPoint);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.search, "groupSearchLeader");
            }

            if (!CanJoinGroupSearchLeader(leader, bossPosition, enemyAnchor))
            {
                return null;
            }

            if (!TryGetGroupSearchFollowerPoint(leader.Position, out Vector3 followPoint))
            {
                return null;
            }

            CommitGroupSearchFollower(goalEnemy, bossPosition, leader, followPoint);
            BotOwner.GoToSomePointData.SetPoint(followPoint);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPointTactical, "groupSearchFollow");
        }

        protected bool IsValidGroupSearchCandidate(BotOwner owner, EnemyInfo goalEnemy)
        {
            if (owner == null ||
                owner.IsDead ||
                owner.BotState != EBotState.Active ||
                owner.GetPlayer?.HealthController?.IsAlive != true ||
                owner.BotFollower?.HaveBoss != true ||
                owner.Memory?.GoalEnemy == null ||
                owner.Memory.GoalEnemy.ProfileId != goalEnemy.ProfileId)
            {
                return false;
            }

            return !owner.Memory.GoalEnemy.IsVisible &&
                   !owner.Memory.GoalEnemy.CanShoot &&
                   owner.Memory.GoalEnemy.CanISearch;
        }

        protected bool TryGetGroupSearchLeaderPoint(EnemyInfo goalEnemy, out Vector3 point)
        {
            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            CustomNavigationPoint? coverPoint = Covers.GetClosestCoverPointTowardPoint(
                BotOwner,
                BotOwner.Position,
                enemyAnchor,
                25f,
                cover => !cover.IsSpotted && cover.IsFreeById(BotOwner.Id));
            if (coverPoint != null)
            {
                point = coverPoint.Position;
                return true;
            }

            if (NavMesh.SamplePosition(enemyAnchor, out NavMeshHit navMeshHit, 4f, -1))
            {
                point = navMeshHit.position;
                return true;
            }

            point = enemyAnchor;
            return point != Vector3.zero;
        }

        protected bool TryGetGroupSearchFollowerPoint(Vector3 leaderPosition, out Vector3 point)
        {
            point = default;
            if (!NavMesh.SamplePosition(leaderPosition, out NavMeshHit leaderHit, 3f, -1))
            {
                return false;
            }

            Vector3 leaderDirection = BotOwner.Position - leaderHit.position;
            leaderDirection.y = 0f;
            if (leaderDirection.sqrMagnitude <= 0.01f)
            {
                leaderDirection = -BotOwner.LookDirection;
                leaderDirection.y = 0f;
            }

            if (leaderDirection.sqrMagnitude <= 0.01f)
            {
                leaderDirection = Vector3.back;
            }

            leaderDirection = leaderDirection.normalized * 2f;
            if (NavMesh.Raycast(leaderHit.position, leaderHit.position + leaderDirection, out NavMeshHit rayHit, -1))
            {
                point = rayHit.position;
                return true;
            }

            point = leaderHit.position + leaderDirection;
            return true;
        }

        protected bool CanJoinGroupSearchLeader(BotOwner leader, Vector3 bossPosition, Vector3 enemyAnchor)
        {
            if (leader == null || leader.IsDead)
            {
                return false;
            }

            Vector3 leaderOffset = leader.Position - BotOwner.Position;
            leaderOffset.y = 0f;
            float joinDistance = GetGroupSearchJoinDistance(bossPosition, enemyAnchor);
            if (leaderOffset.sqrMagnitude > joinDistance * joinDistance)
            {
                return false;
            }

            Vector3 enemyDirection = enemyAnchor - bossPosition;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.01f)
            {
                return true;
            }

            Vector3 myBossOffset = BotOwner.Position - bossPosition;
            myBossOffset.y = 0f;
            Vector3 leaderBossOffset = leader.Position - bossPosition;
            leaderBossOffset.y = 0f;
            if (myBossOffset.sqrMagnitude <= 4f || leaderBossOffset.sqrMagnitude <= 4f)
            {
                return true;
            }

            float myAngle = Vector3.Angle(enemyDirection, myBossOffset);
            float leaderAngle = Vector3.Angle(enemyDirection, leaderBossOffset);
            return Mathf.Abs(myAngle - leaderAngle) <= GroupSearchSectorAngleTolerance;
        }

        protected float GetGroupSearchJoinDistance(Vector3 bossPosition, Vector3 enemyAnchor)
        {
            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            float scaledDistance = bossToEnemy.magnitude * 0.35f;
            return Mathf.Clamp(scaledDistance, GroupSearchJoinDistanceMin, GroupSearchJoinDistanceMax);
        }

        protected bool IsGroupSearchCommitStillValid(EnemyInfo goalEnemy, Vector3 bossPosition, GroupSearchRole expectedRole)
        {
            if (groupSearchRole != expectedRole ||
                expectedRole == GroupSearchRole.None ||
                string.IsNullOrEmpty(groupSearchEnemyId) ||
                !string.Equals(groupSearchEnemyId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                ClearGroupSearchCommit();
                return false;
            }

            if ((bossPosition - groupSearchBossAnchor).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
            {
                ClearGroupSearchCommit();
                return false;
            }

            Vector3 currentEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            Vector3 previousEnemyDirection = groupSearchEnemyAnchor - groupSearchBossAnchor;
            previousEnemyDirection.y = 0f;
            Vector3 currentEnemyDirection = currentEnemyAnchor - bossPosition;
            currentEnemyDirection.y = 0f;
            if ((currentEnemyAnchor - groupSearchEnemyAnchor).sqrMagnitude > EscortEnemyMoveReevalDistance ||
                (previousEnemyDirection.sqrMagnitude > 0.01f &&
                 currentEnemyDirection.sqrMagnitude > 0.01f &&
                 Vector3.Angle(previousEnemyDirection, currentEnemyDirection) > EscortEnemyAngleReeval))
            {
                ClearGroupSearchCommit();
                return false;
            }

            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                ClearGroupSearchCommit();
                return false;
            }

            BotOwner? currentLeader = FollowerGroupSearchRuntime.GetCurrentLeader(boss, goalEnemy.ProfileId);
            if (expectedRole == GroupSearchRole.Leader)
            {
                if (currentLeader == null || currentLeader.ProfileId != BotOwner.ProfileId)
                {
                    ClearGroupSearchCommit();
                    return false;
                }
            }
            else
            {
                if (currentLeader == null ||
                    currentLeader.IsDead ||
                    string.IsNullOrEmpty(groupSearchLeaderProfileId) ||
                    currentLeader.ProfileId != groupSearchLeaderProfileId ||
                    (currentLeader.Position - groupSearchLeaderAnchor).sqrMagnitude > 16f * 16f ||
                    !CanJoinGroupSearchLeader(currentLeader, bossPosition, currentEnemyAnchor))
                {
                    ClearGroupSearchCommit();
                    return false;
                }
            }

            return true;
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
            float aggression = GetFollowerAggression01();
            float allowedDistance = Mathf.Sqrt(BotOwner.Settings.FileSettings.Boss.MAX_DIST_COVER_BOSS_SQRT);
            if (aggression > 0.001f && BotOwner.Memory.GoalEnemy?.Distance <= 100f)
            {
                allowedDistance = Mathf.Lerp(allowedDistance, 100f, aggression * aggression);
            }

            return (BotOwner.Position - bossPosition).sqrMagnitude <= allowedDistance * allowedDistance;
        }

        protected bool CanShootLastKnownPosition(EnemyInfo enemyInfo)
        {
            Vector3 target = enemyInfo.EnemyLastPositionReal + Vector3.up * 1.6f;
            return !Physics.Linecast(BotOwner.WeaponRoot.position, target, out _, LayerMaskClass.HighPolyWithTerrainMask);
        }

        protected bool TryAssignRetreatAttackCover(EnemyInfo goalEnemy, bool requireShootLane)
        {
            Vector3 bossPosition = GetBossPosition();
            Vector3 enemyPosition = goalEnemy.CurrPosition;
            Vector3 awayFromEnemy = bossPosition - enemyPosition;

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = BotOwner.Position - enemyPosition;
            }

            if (awayFromEnemy.sqrMagnitude < 0.25f)
            {
                awayFromEnemy = Vector3.back;
            }

            Vector3 retreatAnchor = bossPosition + awayFromEnemy.normalized * 6f;
            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = BotOwner.LookSensor.Mask;

            CustomNavigationPoint retreatCover = Covers.GetClosestCoverPoint(
                BotOwner,
                retreatAnchor,
                18f,
                point =>
                {
                    if (point == null || point.IsSpotted || !point.IsFreeById(BotOwner.Id))
                    {
                        return false;
                    }

                    if ((point.Position - bossPosition).sqrMagnitude > CloseBossSqr)
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

            BotOwner.Memory.BotCurrentCoverInfo.Spotted();
            BotOwner.Memory.BotCurrentCoverInfo.SetCover(retreatCover, true);
            return true;
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

        protected float GetFollowerAggression()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData?.CombatAggression ?? 50f;
        }

        protected float GetFollowerAggression01()
        {
            return Mathf.Clamp01(GetFollowerAggression() / 100f);
        }

        protected float GetPushEnemyMaxDistance(EnemyInfo? goalEnemy = null)
        {
            float aggression = GetFollowerAggression01();
            if (aggression <= 0.001f)
            {
                return 0f;
            }

            float maxDistance = Mathf.Lerp(25f, 100f, aggression * aggression);
            if (goalEnemy != null && IsEnemyLowThreat(goalEnemy, 2f))
            {
                float lowThreatMaxDistance = Mathf.Lerp(60f, 100f, aggression);
                if (combatStyle == FollowerCombatStyle.MoveForward)
                {
                    lowThreatMaxDistance = 100f;
                }

                maxDistance = Mathf.Max(maxDistance, lowThreatMaxDistance);
            }

            return maxDistance;
        }

        protected bool IsPushEnabled()
        {
            return GetFollowerAggression() > 0.01f;
        }

        protected Vector3 GetEscortEnemyAnchor(EnemyInfo goalEnemy)
        {
            Vector3 enemyAnchor = goalEnemy.EnemyLastPositionReal;
            if ((enemyAnchor - BotOwner.Position).sqrMagnitude > 0.01f)
            {
                return enemyAnchor;
            }

            return goalEnemy.CurrPosition;
        }

        protected bool HasEscortTarget()
        {
            return escortTargetType != EscortTargetType.None &&
                   !string.IsNullOrEmpty(escortEnemyId);
        }

        protected bool IsEscortCommitStillValid(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            if (!HasEscortTarget() ||
                !string.Equals(escortEnemyId, goalEnemy.ProfileId, StringComparison.Ordinal))
            {
                ClearEscortCommit();
                return false;
            }

            if ((bossPosition - escortBossAnchor).sqrMagnitude > CombatAreaExitDistance * CombatAreaExitDistance)
            {
                ClearEscortCommit();
                return false;
            }

            Vector3 currentEnemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            Vector3 previousEnemyDirection = escortEnemyAnchor - escortBossAnchor;
            previousEnemyDirection.y = 0f;
            Vector3 currentEnemyDirection = currentEnemyAnchor - bossPosition;
            currentEnemyDirection.y = 0f;
            if ((currentEnemyAnchor - escortEnemyAnchor).sqrMagnitude > EscortEnemyMoveReevalDistance ||
                (previousEnemyDirection.sqrMagnitude > 0.01f &&
                 currentEnemyDirection.sqrMagnitude > 0.01f &&
                 Vector3.Angle(previousEnemyDirection, currentEnemyDirection) > EscortEnemyAngleReeval))
            {
                ClearEscortCommit();
                return false;
            }

            if (escortTargetType == EscortTargetType.Cover)
            {
                if (escortCoverPoint == null ||
                    !escortCoverPoint.IsFreeById(BotOwner.Id) ||
                    escortCoverPoint.IsSpotted ||
                    (escortCoverPoint.Position - bossPosition).sqrMagnitude > CloseBossSqr)
                {
                    ClearEscortCommit();
                    return false;
                }

                if (escortWantedShootLane)
                {
                    ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
                    if (shootPoint != null && !Utils.Utils.CanShootToTarget(shootPoint, escortCoverPoint, BotOwner.LookSensor.Mask, false))
                    {
                        ClearEscortCommit();
                        return false;
                    }
                }
            }
            else if (escortTargetType == EscortTargetType.Point)
            {
                if ((escortPoint - bossPosition).sqrMagnitude > CloseBossSqr)
                {
                    ClearEscortCommit();
                    return false;
                }
            }

            return true;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26>? TryGetCommittedEscortDecision(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            if (!IsEscortCommitStillValid(goalEnemy, bossPosition))
            {
                return null;
            }

            if (escortTargetType == EscortTargetType.Cover && escortCoverPoint != null)
            {
                BotOwner.Memory.BotCurrentCoverInfo.SetCover(escortCoverPoint, true);
                if (BotOwner.Memory.IsInCover && BotOwner.Memory.CurCustomCoverPoint?.Id == escortCoverPoint.Id)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "escortHold");
                }

                return BotOwner.CanSprintPlayer
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "escortCover")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "escortCover");
            }

            if (escortTargetType == EscortTargetType.Point)
            {
                BotOwner.GoToSomePointData.SetPoint(escortPoint);
                if (BotOwner.GoToSomePointData.IsCome())
                {
                    HoldFor(2f);
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "escortPointHold");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "escortPoint");
            }

            ClearEscortCommit();
            return null;
        }

        protected bool TryFindEscortCover(EnemyInfo goalEnemy, Vector3 bossPosition, out CustomNavigationPoint? bestPoint, out bool bestPointHasShootLane)
        {
            bestPoint = null;
            bestPointHasShootLane = false;

            Vector3 enemyAnchor = GetEscortEnemyAnchor(goalEnemy);
            Vector3 bossToEnemy = enemyAnchor - bossPosition;
            bossToEnemy.y = 0f;
            if (bossToEnemy.sqrMagnitude <= 0.01f)
            {
                return false;
            }

            ShootPointClass shootPoint = BotOwner.CurrentEnemyTargetPosition(true);
            LayerMask mask = BotOwner.LookSensor.Mask;
            Func<CustomNavigationPoint, bool> escortEligibility = point =>
            {
                if (point == null || point.IsSpotted || !point.IsFreeById(BotOwner.Id))
                {
                    return false;
                }

                return (point.Position - bossPosition).sqrMagnitude <= CloseBossSqr;
            };

            if (shootPoint != null)
            {
                bestPoint = Covers.GetClosestCoverPointTowardPoint(
                    BotOwner,
                    bossPosition,
                    enemyAnchor,
                    22f,
                    point => escortEligibility(point) && Utils.Utils.CanShootToTarget(shootPoint, point, mask, false));

                if (bestPoint != null)
                {
                    bestPointHasShootLane = true;
                    return true;
                }
            }

            bestPoint = Covers.GetClosestCoverPointTowardPoint(
                BotOwner,
                bossPosition,
                enemyAnchor,
                22f,
                escortEligibility);

            if (bestPoint != null)
            {
                return true;
            }

            bestPoint = shootPoint != null
                ? Covers.GetClosestCoverPoint(BotOwner, bossPosition, 20f, point => Utils.Utils.CanShootToTarget(shootPoint, point, mask, false))
                : null;

            if (bestPoint != null)
            {
                bestPointHasShootLane = shootPoint != null && Utils.Utils.CanShootToTarget(shootPoint, bestPoint, mask, false);
                return true;
            }

            bestPoint = Covers.GetClosestCoverPoint(BotOwner, bossPosition, 15f);
            bestPointHasShootLane = false;
            return bestPoint != null;
        }

        protected AICoreActionResultStruct<BotLogicDecision, GClass26> GetNoPushSafePositionDecision(EnemyInfo goalEnemy)
        {
            if (TryAssignRetreatAttackCover(goalEnemy, true))
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "unsafePushCover");
            }

            if (HaveCoverToShoot)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(HoldOrCover(BotOwner), "unsafePushHold");
            }

            Vector3 bossPosition = GetBossPosition();
            if ((BotOwner.Position - bossPosition).sqrMagnitude <= CloseBossSqr && BotOwner.Memory.IsInCover)
            {
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "unsafePushBossHold");
            }

            return DecideBossPositionAction(bossPosition);
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
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy != null)
            {
                AICoreActionResultStruct<BotLogicDecision, GClass26>? committedEscortDecision = TryGetCommittedEscortDecision(goalEnemy, bossPosition);
                if (committedEscortDecision != null)
                {
                    return committedEscortDecision.Value;
                }
            }

            if (goalEnemy != null && TryFindEscortCover(goalEnemy, bossPosition, out CustomNavigationPoint? preferredEscortCover, out bool escortHasShootLane))
            {
                CommitEscortCover(goalEnemy, bossPosition, preferredEscortCover, escortHasShootLane);
                BotOwner.Memory.BotCurrentCoverInfo.SetCover(preferredEscortCover, true);
                return BotOwner.CanSprintPlayer
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "escortCover")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "escortCover");
            }

            RefreshBossCover();
            if (haveNearBossCover)
            {
                if (goalEnemy != null)
                {
                    CommitEscortCover(goalEnemy, bossPosition, nearBossCoverPoint, false);
                }

                BotOwner.Memory.BotCurrentCoverInfo.SetCover(nearBossCoverPoint, true);
                return BotOwner.CanSprintPlayer
                    ? new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.runToCover, "sDistCloseB")
                    : new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.attackMoving, "sDistCloseB");
            }

            if (Time.time - lastGoToPointEndTime > 10f && NavMesh.SamplePosition(bossPosition, out NavMeshHit navMeshHit, BossPointRadius, -1))
            {
                lastBossPointSample = navMeshHit.position;
                if (goalEnemy != null)
                {
                    CommitEscortPoint(goalEnemy, bossPosition, lastBossPointSample);
                }

                BotOwner.GoToSomePointData.SetPoint(lastBossPointSample);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "HaveCoverSh");
            }

            if (BotOwner.Memory.IsInCover)
            {
                HoldFor(4f);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "HaveCoverSh");
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossNavMeshHit, 2f, -1))
            {
                if (goalEnemy != null)
                {
                    CommitEscortPoint(goalEnemy, bossPosition, bossNavMeshHit.position);
                }

                BotOwner.GoToSomePointData.SetPoint(bossNavMeshHit.position);
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss");
            }

            if (goalEnemy != null)
            {
                CommitEscortPoint(goalEnemy, bossPosition, bossPosition);
            }

            BotOwner.GoToSomePointData.SetPoint(bossPosition);
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "NoCoverMoveBoss");
        }

    }

    internal static class FollowerGroupSearchRuntime
    {
        private static readonly Dictionary<string, Dictionary<string, string>> LeaderProfileIdByBossAndEnemy =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        public static BotOwner? GetCurrentLeader(pitAIBossPlayer boss, string? enemyProfileId)
        {
            if (boss?.realPlayer == null || string.IsNullOrEmpty(enemyProfileId))
            {
                return null;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            if (!LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? leadersByEnemy) ||
                leadersByEnemy == null ||
                !leadersByEnemy.TryGetValue(enemyProfileId, out string? leaderProfileId) ||
                string.IsNullOrEmpty(leaderProfileId))
            {
                return null;
            }

            BotOwner? leader = BossPlayers.GetFollowerByProfileId(leaderProfileId)?.GetBot();
            if (leader == null || leader.IsDead)
            {
                leadersByEnemy.Remove(enemyProfileId);
                if (leadersByEnemy.Count == 0)
                {
                    LeaderProfileIdByBossAndEnemy.Remove(bossProfileId);
                }
                return null;
            }

            return leader;
        }

        public static BotOwner? GetOrAssignLeader(pitAIBossPlayer boss, EnemyInfo goalEnemy, Func<BotOwner, EnemyInfo, bool> isValidCandidate)
        {
            if (boss?.realPlayer == null || goalEnemy == null || string.IsNullOrEmpty(goalEnemy.ProfileId))
            {
                return null;
            }

            string bossProfileId = boss.realPlayer.ProfileId;
            string enemyProfileId = goalEnemy.ProfileId;
            BotOwner? currentLeader = GetCurrentLeader(boss, enemyProfileId);
            if (currentLeader != null &&
                currentLeader.Memory?.GoalEnemy != null &&
                currentLeader.Memory.GoalEnemy.ProfileId == enemyProfileId &&
                CanOwnerLeadSearch(currentLeader) &&
                isValidCandidate(currentLeader, currentLeader.Memory.GoalEnemy))
            {
                return currentLeader;
            }

            BotOwner? bestLeader = null;
            bool bestHadPersonal = false;
            float bestAggression = float.MinValue;
            float bestPersonalSeenTime = float.MinValue;
            float bestDistanceSqr = float.MaxValue;

            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowersByBoss(bossProfileId))
            {
                BotOwner owner = follower?.GetBot();
                EnemyInfo? ownerEnemy = owner?.Memory?.GoalEnemy;
                if (owner == null ||
                    ownerEnemy == null ||
                    ownerEnemy.ProfileId != enemyProfileId ||
                    !CanOwnerLeadSearch(owner) ||
                    !isValidCandidate(owner, ownerEnemy))
                {
                    continue;
                }

                bool hadPersonal = Time.time - ownerEnemy.PersonalLastSeenTime <= 12f;
                float aggression = GetOwnerAggression(owner);
                float personalSeenTime = ownerEnemy.PersonalLastSeenTime;
                float distanceSqr = (owner.Position - ownerEnemy.CurrPosition).sqrMagnitude;

                if (bestLeader == null ||
                    (hadPersonal && !bestHadPersonal) ||
                    (hadPersonal == bestHadPersonal && aggression > bestAggression + 0.01f) ||
                    (hadPersonal == bestHadPersonal &&
                     Mathf.Abs(aggression - bestAggression) <= 0.01f &&
                     personalSeenTime > bestPersonalSeenTime + 0.01f) ||
                    (hadPersonal == bestHadPersonal &&
                     Mathf.Abs(aggression - bestAggression) <= 0.01f &&
                     Mathf.Abs(personalSeenTime - bestPersonalSeenTime) <= 0.01f &&
                     distanceSqr < bestDistanceSqr))
                {
                    bestLeader = owner;
                    bestHadPersonal = hadPersonal;
                    bestAggression = aggression;
                    bestPersonalSeenTime = personalSeenTime;
                    bestDistanceSqr = distanceSqr;
                }
            }

            if (bestLeader == null)
            {
                if (LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? leadersByEnemy) &&
                    leadersByEnemy != null)
                {
                    leadersByEnemy.Remove(enemyProfileId);
                    if (leadersByEnemy.Count == 0)
                    {
                        LeaderProfileIdByBossAndEnemy.Remove(bossProfileId);
                    }
                }

                return null;
            }

            if (!LeaderProfileIdByBossAndEnemy.TryGetValue(bossProfileId, out Dictionary<string, string>? bossLeaders) ||
                bossLeaders == null)
            {
                bossLeaders = new Dictionary<string, string>(StringComparer.Ordinal);
                LeaderProfileIdByBossAndEnemy[bossProfileId] = bossLeaders;
            }

            bossLeaders[enemyProfileId] = bestLeader.ProfileId;
            return bestLeader;
        }

        private static bool CanOwnerLeadSearch(BotOwner owner)
        {
            return GetOwnerAggression(owner) > 0.01f;
        }

        private static float GetOwnerAggression(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return 50f;
            }

            return BossPlayers.GetFollowerByProfileId(owner.ProfileId)?.CombatAggression ?? 50f;
        }
    }

}
