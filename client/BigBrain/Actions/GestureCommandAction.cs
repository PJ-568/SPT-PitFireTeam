using Comfort.Common;
using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.Components;
using pitTeam.Modules;
using pitTeam.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Executes boss-issued follower commands outside the combat objective system. It owns command
    /// movement, hold/come/there/regroup behavior, loot and door interaction, and command cleanup
    /// when combat or another command interrupts the active task.
    /// </summary>
    internal class GestureCommandAction : CustomLogic
    {
        private BotFollowerPlayer? followerData;
        private float nextPathCheckAt;
        private bool moveCommandInitialized;
        private float nextHoldLookChangeAt;
        private Vector3 holdLookPoint;
        private float moveArrivalLookUntil;
        private float comeArrivalHoldUntil;
        private Vector3 activeMoveTarget;
        private bool comeTargetInitialized;
        private Vector3 comeTarget;
        private bool comePoseInitialized;
        private float comeMovePose = 1f;
        private bool regroupTargetInitialized;
        private Vector3 regroupTarget;
        private float nextRegroupRefreshAt;
        private bool regroupRunMode;
        private bool regroupReportedOnPosition;
        private bool regroupBossAnchorInitialized;
        private Vector3 regroupBossAnchorPosition;
        private float nextRegroupBossAnchorCheckAt;
        private bool lootPickupInProgress;
        private float lootPickupReadyAt;
        private float lootPickupAttemptStartedAt;
        private LootItem? activeLootItem;
        private Door? activeDoor;
        private bool doorMoveIssued;
        private bool doorInteractIssued;
        private float doorTimeoutAt;
        private FollowerCommandType lastCommand = FollowerCommandType.None;
        private const float RegroupArriveNavDistance = 4f;
        private const float RegroupRunDistance = 10f;
        private const float SameLevelTolerance = 1.75f;
        private const float RegroupCoverSearchRadius = 15f;
        private const float RegroupRandomRadius = 6f;
        private const float RegroupReservationSpacing = 1.5f;
        private const float RegroupReservationTtl = 2f;

        public GestureCommandAction(BotOwner botOwner) : base(botOwner) { }

        public override void Start()
        {
            followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            ReleaseRegroupReservation();
            nextPathCheckAt = 0f;
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            holdLookPoint = Vector3.zero;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
            comeTargetInitialized = false;
            comeTarget = Vector3.zero;
            comePoseInitialized = false;
            comeMovePose = 1f;
            regroupTargetInitialized = false;
            regroupTarget = Vector3.zero;
            nextRegroupRefreshAt = 0f;
            regroupRunMode = false;
            regroupReportedOnPosition = false;
            regroupBossAnchorInitialized = false;
            regroupBossAnchorPosition = Vector3.zero;
            nextRegroupBossAnchorCheckAt = 0f;
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;
            activeDoor = null;
            doorMoveIssued = false;
            doorInteractIssued = false;
            doorTimeoutAt = 0f;
            lastCommand = FollowerCommandType.None;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null || !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                ReleaseRegroupReservation();
                lastCommand = FollowerCommandType.None;
                return;
            }

            EnsureCommandControl();

            // Request-layer commands are lower priority than real combat contact. If the command can
            // no longer safely continue, clear interaction state and let combat/patrol take over.
            if (ShouldInterruptCommandForCombat(command))
            {
                ReleaseRegroupReservation();
                CleanupLootInteraction($"CommandInterrupt:{command}");
                CleanupDoorInteraction();
                followerData?.ClearCommand($"CommandInterrupt:{command}");
                BotOwner.StopMove();
                BotOwner.SetPose(1f);
                lastCommand = FollowerCommandType.None;
                return;
            }

            if (command != lastCommand)
            {
                // Command changes must release resources owned by the previous command. This avoids
                // stale regroup reservations, loot pickup state, or door interaction state carrying
                // into a different command.
                if (lastCommand == FollowerCommandType.RegroupNearBoss)
                {
                    ReleaseRegroupReservation();
                }

                if (lastCommand == FollowerCommandType.TakeLootItem)
                {
                    CleanupLootInteraction($"CommandChanged:{command}");
                }

                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                regroupTargetInitialized = false;
                regroupTarget = Vector3.zero;
                nextRegroupRefreshAt = 0f;
                regroupReportedOnPosition = false;
                regroupBossAnchorInitialized = false;
                regroupBossAnchorPosition = Vector3.zero;
                nextRegroupBossAnchorCheckAt = 0f;
                lootPickupInProgress = false;
                lootPickupReadyAt = 0f;
                lootPickupAttemptStartedAt = 0f;
                activeLootItem = null;
                CleanupDoorInteraction();
            }

            switch (command)
            {
                case FollowerCommandType.HoldPosition:
                    HandleHoldPosition();
                    break;

                case FollowerCommandType.ComeCloser:
                    HandleComeCloser();
                    break;

                case FollowerCommandType.MoveToPoint:
                    HandleMoveToPoint(target);
                    break;

                case FollowerCommandType.RegroupNearBoss:
                    HandleRegroupNearBoss();
                    break;

                case FollowerCommandType.TakeLootItem:
                    HandleTakeLootItem();
                    break;

                case FollowerCommandType.OpenDoor:
                    HandleOpenDoor();
                    break;
            }

            if (command != lastCommand)
            {
                lastCommand = command;
                if (
                    command == FollowerCommandType.MoveToPoint ||
                    command == FollowerCommandType.ComeCloser
                )
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
            }
        }

        private void EnsureCommandControl()
        {
            if (BotOwner?.Mover != null)
            {
                if (BotOwner.Mover.Pause)
                {
                    BotOwner.Mover.Pause = false;
                }
            }

            if (BotOwner?.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }
        }

        private void HandleRegroupNearBoss()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:missingBoss");
                return;
            }
            // intrerupt on enemy enagage
            if (ShouldInterruptRegroupForThreatOrState(clearForDanger: true))
            {
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:interrupt");
                BotOwner.StopMove();
                return;
            }

            // Regroup is an urgent converge order: force move-capable state each tick.
            if (BotOwner.Mover.Pause)
            {
                BotOwner.Mover.Pause = false;
            }

            if (BotOwner.Mover.TargetPose < 0.85f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            Vector3 bossPos = boss.realPlayer.Position;
            if (!regroupBossAnchorInitialized)
            {
                regroupBossAnchorInitialized = true;
                regroupBossAnchorPosition = bossPos;
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
            }

            if (Time.time >= nextRegroupBossAnchorCheckAt)
            {
                nextRegroupBossAnchorCheckAt = Time.time + 0.5f;
                if ((bossPos - regroupBossAnchorPosition).sqrMagnitude > 10f * 10f)
                {
                    regroupBossAnchorPosition = bossPos;
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                }
            }

            float verticalDiff = Mathf.Abs(BotOwner.Position.y - bossPos.y);
            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPos);

            if (verticalDiff <= SameLevelTolerance && navDistanceToBoss <= RegroupArriveNavDistance)
            {
                BotOwner.StopMove();
                if (!regroupReportedOnPosition)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.OnPosition, false);
                    regroupReportedOnPosition = true;
                }
                ReleaseRegroupReservation();
                followerData?.ClearCommand("Regroup:arrived");
                return;
            }

            if (!regroupTargetInitialized || Time.time >= nextRegroupRefreshAt)
            {
                if (!TryGetRegroupTarget(bossPos, out regroupTarget))
                {
                    regroupTarget = bossPos;
                }
                regroupTargetInitialized = true;
                nextRegroupRefreshAt = Time.time + 0.8f;
                UpsertRegroupReservation(regroupTarget);
                BotOwner.GoToSomePointData.SetPoint(regroupTarget);
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, regroupTarget, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    ReleaseRegroupReservation();
                    regroupTargetInitialized = false;
                    return;
                }
            }

            float regroupDistance = (regroupTarget - BotOwner.Position).magnitude;
            float regroupPressureDistance = Mathf.Max(regroupDistance, navDistanceToBoss);
            if (regroupRunMode)
            {
                regroupRunMode = regroupPressureDistance > 6f;
            }
            else if (regroupPressureDistance >= RegroupRunDistance)
            {
                regroupRunMode = true;
            }

            bool shouldRun = regroupRunMode;
            BotOwner.GoToSomePointData.UpdateToGo(shouldRun, 1, 1f);
            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            nextHoldLookChangeAt = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private bool ShouldInterruptRegroupForThreatOrState(bool clearForDanger)
        {
            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing)
            {
                return true;
            }

            bool dangerNow = currentDecision == BotLogicDecision.runAwayGrenade ||
                             currentDecision == BotLogicDecision.runAwayBTR ||
                             BotOwner.BewareGrenade?.ShallRunAway() == true ||
                             BotOwner.BewareBTR?.ShallRunAway() == true;

            if (dangerNow && clearForDanger)
            {
                return true;
            }

            return false;
        }

        private bool ShouldInterruptCommandForCombat(FollowerCommandType command)
        {
            if (command == FollowerCommandType.HoldPosition)
            {
                return false;
            }

            if (command == FollowerCommandType.RegroupNearBoss)
            {
                return ShouldInterruptRegroupForThreatOrState(clearForDanger: true);
            }

            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal ||
                           currentDecision == BotLogicDecision.healStimulators;
            if (healing)
            {
                return true;
            }

            if (BotOwner.BewareGrenade?.ShallRunAway() == true ||
                BotOwner.BewareBTR?.ShallRunAway() == true ||
                currentDecision == BotLogicDecision.runAwayGrenade ||
                currentDecision == BotLogicDecision.runAwayBTR)
            {
                return true;
            }

            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (goalEnemy == null)
            {
                return false;
            }

            bool visibleFightNow = goalEnemy.IsVisible &&
                                  goalEnemy.CanShoot &&
                                  BotOwner.LookSensor.EnoughDistToShoot(out _);
            bool closeVisibleThreat = goalEnemy.IsVisible && goalEnemy.Distance <= 18f;
            bool urgentCombatAction = currentDecision == BotLogicDecision.dogFight ||
                                      currentDecision == BotLogicDecision.shootFromPlace ||
                                      currentDecision == BotLogicDecision.shootFromCover ||
                                      currentDecision == BotLogicDecision.attackMoving ||
                                      currentDecision == (BotLogicDecision)CustomBotDecisions.attackRetreat ||
                                      currentDecision == BotLogicDecision.runToCover ||
                                      currentDecision == BotLogicDecision.goToEnemy ||
                                      currentDecision == BotLogicDecision.runToEnemy;

            return visibleFightNow || closeVisibleThreat || urgentCombatAction || BotOwner.Memory.IsUnderFire;
        }

        private bool TryGetRegroupTarget(Vector3 bossPos, out Vector3 target)
        {
            target = Vector3.zero;
            float bestDistance = float.MaxValue;
            List<CustomNavigationPoint> coverPoints = Covers.GetCoverPoints(
                BotOwner,
                bossPos,
                RegroupCoverSearchRadius,
                point => Mathf.Abs(point.Position.y - bossPos.y) <= SameLevelTolerance && !IsRegroupTargetCrowded(point.Position)
            );

            foreach (CustomNavigationPoint point in coverPoints)
            {
                if (point == null) continue;
                NavMeshPath coverPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, point.Position, NavMesh.AllAreas, coverPath) || coverPath.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float pathDistance = coverPath.CalculatePathLength();
                if (pathDistance < bestDistance)
                {
                    bestDistance = pathDistance;
                    target = point.Position;
                }
            }

            if (target == Vector3.zero)
            {
                if (TryGetBossCombatEvents(out CombatEvents? combatEvents) &&
                    combatEvents.TryFindBossSpreadDestination(
                        BotOwner,
                        bossPos,
                        1f,
                        RegroupRandomRadius,
                        SameLevelTolerance,
                        RegroupReservationSpacing,
                        out Vector3 spreadTarget))
                {
                    target = spreadTarget;
                }
            }

            return target != Vector3.zero;
        }

        private bool IsRegroupTargetCrowded(Vector3 candidate)
        {
            if (BotOwner.BotFollower.BossToFollow is pitAIBossPlayer boss)
            {
                if (boss.CombatEvents.HasDestinationClaimConflict(
                        BotOwner,
                        candidate,
                        RegroupReservationSpacing,
                        includeFollowerPositions: true))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpsertRegroupReservation(Vector3 target)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.UpsertDestinationClaim(BotOwner, target, RegroupReservationTtl);
            }
        }

        private void ReleaseRegroupReservation()
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.ReleaseDestinationClaim(BotOwner);
            }
        }

        private static void CleanupRegroupReservations()
        {
        }

        private bool TryGetBossCombatEvents(out CombatEvents? combatEvents)
        {
            combatEvents = null;
            if (BotOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss)
            {
                return false;
            }

            combatEvents = boss.CombatEvents;
            return combatEvents != null;
        }

        private void HandleComeCloser()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                followerData?.ClearCommand("ComeCloser:missingBoss");
                return;
            }

            if (!comeTargetInitialized)
            {
                comeTarget = boss.realPlayer.Transform.position;
                comeTargetInitialized = true;
            }
            if (!comePoseInitialized)
            {
                float bossPose = Mathf.Clamp01(boss.realPlayer.MovementContext?.PoseLevel ?? 1f);
                // Snapshot boss stance at command start.
                comeMovePose = bossPose < 0.75f ? 0.1f : 1f;
                comePoseInitialized = true;
            }

            float distance = (comeTarget - BotOwner.Position).magnitude;
            if (distance > 1.5f && comeArrivalHoldUntil > 0f)
            {
                comeArrivalHoldUntil = 0f;
            }
            if (distance <= 1.5f)
            {

                HandleComeArrivalPause();
                if (Time.time < comeArrivalHoldUntil)
                {
                    return;
                }
                comeArrivalHoldUntil = 0f;
                comeTargetInitialized = false;
                comeTarget = Vector3.zero;
                comePoseInitialized = false;
                comeMovePose = 1f;
                followerData?.CompleteComeCloser();
                BotOwner.StopMove();
                return;
            }

            BotOwner.GoToSomePointData.SetPoint(comeTarget);
            BotOwner.GoToSomePointData.UpdateToGo(distance > 16f, 1, comeMovePose);
            BotOwner.Steering.LookToPathDestPoint();
            moveCommandInitialized = false;
            nextHoldLookChangeAt = 0f;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleMoveToPoint(Vector3 target)
        {
            if (BotOwner.Mover.TargetPose != 1f) BotOwner.Mover.SetPose(1f);

            float distance = (target - BotOwner.Position).magnitude;
            if (distance > 1.5f && moveArrivalLookUntil > 0f)
            {
                moveArrivalLookUntil = 0f;
            }
            if (distance <= 1.5f)
            {
                HandleMovePointArrivalLookAround();
                if (Time.time < moveArrivalLookUntil)
                {
                    return;
                }
                moveArrivalLookUntil = 0f;
                BotOwner.StopMove();
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                moveCommandInitialized = false;
                followerData?.ClearCommand("MoveToPoint:arrived");
                return;
            }

            bool targetChanged = !moveCommandInitialized || (activeMoveTarget - target).sqrMagnitude > 0.25f;
            if (targetChanged)
            {
                BotOwner.GoToSomePointData.SetPoint(target);
                moveCommandInitialized = true;
                activeMoveTarget = target;
                moveArrivalLookUntil = 0f;
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextPathCheckAt)
            {
                nextPathCheckAt = Time.time + 0.5f;
                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    followerData?.ClearCommand("MoveToPoint:pathInvalid");
                    BotOwner.StopMove();
                    return;
                }
            }

            // "There" should always be a walk move.
            BotOwner.GoToSomePointData.UpdateToGo(false);

            if (followerData?.TryGetCommandLookOverride(out Vector3 lookOverridePoint) == true)
            {
                BotOwner.Steering.LookToPoint(lookOverridePoint);
            }
            else
            {
                BotOwner.Steering.LookToPathDestPoint();
            }

            nextHoldLookChangeAt = 0f;
        }

        private void HandleMovePointArrivalLookAround()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            // Always start the arrival hold window first so command is not cleared immediately
            // when random look is temporarily paused (e.g. recent contact command).
            if (moveArrivalLookUntil <= 0f)
            {
                moveArrivalLookUntil = Time.time + Utils.Utils.Random(2f, 4f);
                nextHoldLookChangeAt = 0f;
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;

                return;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.8f, 2f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private void HandleHoldPosition()
        {
            if (lootPickupInProgress)
            {
                return;
            }

            BotOwner.StopMove();
            if (followerData?.ShouldCrouchForHoldPosition() == true &&
                (BotOwner.Mover.TargetPose > 0.15f || BotOwner.Mover.TargetPose < 0.05f))
            {
                BotOwner.Mover.SetPose(0.1f);
            }
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {
                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;
                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                moveCommandInitialized = false;
                moveArrivalLookUntil = 0f;
                comeArrivalHoldUntil = 0f;
                activeMoveTarget = Vector3.zero;
                return;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(2f, 6f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }

            moveCommandInitialized = false;
            moveArrivalLookUntil = 0f;
            comeArrivalHoldUntil = 0f;
            activeMoveTarget = Vector3.zero;
        }

        private void HandleComeArrivalPause()
        {
            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }

            if (followerData?.TryGetCommandLookOverride(out Vector3 holdLookOverridePoint) == true)
            {

                BotOwner.Steering.LookToPoint(holdLookOverridePoint);

                holdLookPoint = Vector3.zero;
                nextHoldLookChangeAt = 0f;

                return;
            }

            if (comeArrivalHoldUntil <= 0f)
            {
                comeArrivalHoldUntil = Time.time + Utils.Utils.Random(1.25f, 2.5f);
                nextHoldLookChangeAt = 0f;
            }

            if (Time.time >= nextHoldLookChangeAt)
            {
                holdLookPoint = PickNextHoldLookPoint();
                nextHoldLookChangeAt = Time.time + Utils.Utils.Random(0.6f, 1.5f);
            }

            if (holdLookPoint != Vector3.zero)
            {
                BotOwner.Steering.LookToPoint(holdLookPoint);
            }
        }

        private Vector3 PickNextHoldLookPoint()
        {
            Vector3 baseForward = BotOwner.LookDirection;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
            }
            // Keep hold/look-around horizontal so we don't accumulate upward pitch.
            baseForward.y = 0f;
            if (baseForward.sqrMagnitude < 0.01f)
            {
                baseForward = BotOwner.GetPlayer.Transform.forward;
                baseForward.y = 0f;
            }

            float yawOffset = UnityEngine.Random.Range(-130f, 130f);
            Vector3 lookDir = Quaternion.Euler(0f, yawOffset, 0f) * baseForward.normalized;
            float lookDistance = UnityEngine.Random.Range(8f, 20f);
            Vector3 lookPoint = BotOwner.Position + lookDir * lookDistance;
            lookPoint.y = BotOwner.Position.y + 1.1f;
            return lookPoint;
        }

        public override void Stop()
        {
            if (followerData?.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) != true ||
                command != FollowerCommandType.TakeLootItem)
            {
                CleanupLootInteraction("TakeLoot:actionStop");
            }

            CleanupDoorInteraction();
            base.Stop();
        }

        private void HandleTakeLootItem()
        {
            if (followerData == null)
            {
                return;
            }

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                ClearTakeLootState(guardFailureReason ?? "TakeLoot:invalidState");
                return;
            }

            activeLootItem ??= InteractableObjects.GetCurLootItem();
            if (activeLootItem == null)
            {
                ClearTakeLootState("TakeLoot:itemMissing");
                return;
            }

            if (TryFinishTransferredLoot("TakeLoot:detectedInInventory"))
            {
                return;
            }

            // Keep the selected loot item pinned for this command so brief quick-panel target changes
            // don't drop execution ownership mid-pickup.
            if (InteractableObjects.GetCurLootItem() == null)
            {
                InteractableObjects.SetCurLootItem(activeLootItem);
            }

            Vector3 lootPosition;
            try
            {
                lootPosition = InteractableObjects.GetLootPosition();
            }
            catch
            {
                ClearTakeLootState("TakeLoot:missingLootPosition");
                return;
            }

            float distance = Vector3.Distance(BotOwner.Position, lootPosition);
            if (distance > 1.75f)
            {
                lootPickupReadyAt = 0f;
                BotOwner.GoToSomePointData.SetPoint(lootPosition);
                BotOwner.GoToSomePointData.UpdateToGo(false);
                BotOwner.Steering.LookToMovingDirection();
                return;
            }

            BotOwner.StopMove();
            if (BotOwner.Mover.Sprinting)
            {
                BotOwner.Mover.Sprint(false, false);
            }
            BotOwner.Steering.LookToPoint(activeLootItem.transform.position);

            if (lootPickupInProgress)
            {
                if (TryFinishTransferredLoot("TakeLoot:detectedInInventoryDuringPickup"))
                {
                    return;
                }

                if (lootPickupAttemptStartedAt > 0f && Time.time - lootPickupAttemptStartedAt > 3f)
                {
                    StopLootPickupState(BotOwner?.GetPlayer);
                    lootPickupInProgress = false;
                    lootPickupAttemptStartedAt = 0f;
                    lootPickupReadyAt = Time.time + 0.35f;
                }

                return;
            }

            if (lootPickupReadyAt <= 0f)
            {
                lootPickupReadyAt = Time.time + 0.35f;
                return;
            }

            if (Time.time < lootPickupReadyAt)
            {
                return;
            }

            StartLootPickup(activeLootItem);
        }

        private bool CanContinueLootCommand(out string? reason)
        {
            reason = null;

            if (BotOwner == null || BotOwner.IsDead || BotOwner.BotState != EBotState.Active)
            {
                reason = "TakeLoot:botInvalid";
                return false;
            }

            if (!InteractableObjects.IsTaker(BotOwner))
            {
                // Taker ownership can be cleared transiently; try to recover once before aborting.
                if (!InteractableObjects.SetTaker(BotOwner) || !InteractableObjects.IsTaker(BotOwner))
                {
                    reason = "TakeLoot:notTaker";
                    return false;
                }
            }

            if (BotOwner.Memory?.HaveEnemy == true)
            {
                reason = "TakeLoot:enemy";
                return false;
            }

            return true;
        }

        private bool TryGetLootExecutionContext(
            LootItem? lootItem,
            out Player? botPlayer,
            out InventoryController? inventory,
            out Item? rootItem,
            out string reason)
        {
            botPlayer = null;
            inventory = null;
            rootItem = null;

            if (!CanContinueLootCommand(out string? guardFailureReason))
            {
                reason = guardFailureReason ?? "TakeLoot:invalidState";
                return false;
            }

            if (lootItem == null || lootItem.gameObject == null)
            {
                reason = "TakeLoot:itemMissing";
                return false;
            }

            TraderControllerClass? itemOwner = lootItem.ItemOwner;
            rootItem = itemOwner?.RootItem;
            if (rootItem == null)
            {
                reason = "TakeLoot:itemNull";
                return false;
            }

            botPlayer = BotOwner.GetPlayer;
            inventory = botPlayer?.InventoryController;
            if (botPlayer == null || inventory == null)
            {
                reason = "TakeLoot:noInventory";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void StartLootPickup(LootItem lootItem)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? inventory, out Item? rootItem, out string reason))
                {
                    ClearTakeLootState(reason);
                    return;
                }

                var pickupResult = InteractionsHandlerClass.QuickFindAppropriatePlace(
                    rootItem,
                    inventory,
                    inventory.Inventory.Equipment.ToEnumerable<InventoryEquipment>(),
                    InteractionsHandlerClass.EMoveItemOrder.PickUp,
                    true);

                if (!pickupResult.Succeeded)
                {
                    BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                    ClearTakeLootState("TakeLoot:noSpace");
                    return;
                }

                if (!inventory.CanExecute(pickupResult.Value))
                {
                    ClearTakeLootState("TakeLoot:cannotExecute");
                    return;
                }

                lootPickupInProgress = true;
                lootPickupAttemptStartedAt = Time.time;
                botPlayer.SaveInteractionRayInfo();
                botPlayer.CurrentManagedState.Pickup(true, () => ExecuteLootPickupTransaction(lootItem, rootItem, inventory, pickupResult.Value));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot start failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:startException");
            }
        }

        private void ExecuteLootPickupTransaction(LootItem lootItem, Item rootItem, InventoryController inventory, GInterface424 pickupAction)
        {
            try
            {
                if (!TryGetLootExecutionContext(lootItem, out Player? botPlayer, out InventoryController? currentInventory, out Item? currentRootItem, out string reason))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState(reason);
                    return;
                }

                if (!ReferenceEquals(currentInventory, inventory) || !ReferenceEquals(currentRootItem, rootItem))
                {
                    StopLootPickupState(botPlayer);
                    ClearTakeLootState("TakeLoot:itemChanged");
                    return;
                }

                if (pickupAction is GInterface427 moveAction)
                {
                    ItemAddress currentAddress = rootItem.CurrentAddress;
                    if (currentAddress == null || !moveAction.From.Equals(currentAddress))
                    {
                        StopLootPickupState(botPlayer);
                        ClearTakeLootState("TakeLoot:itemMoved");
                        return;
                    }
                }

                inventory.RunNetworkTransaction(pickupAction, new Callback(result => CompleteLootPickup(result, botPlayer, rootItem)));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot transaction failed");
                Modules.Logger.LogError(ex);
                StopLootPickupState(BotOwner?.GetPlayer);
                ClearTakeLootState("TakeLoot:transactionException");
            }
        }

        private void CompleteLootPickup(IResult result, Player? botPlayer, Item rootItem)
        {
            try
            {
                if (result?.Succeed == true || IsLootNowInBotInventory(botPlayer, rootItem))
                {
                    FinishLootPickupSuccess(botPlayer, rootItem, "TakeLoot:done");
                    return;
                }

                BotOwner.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                ClearTakeLootState("TakeLoot:transactionFailed");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("TakeLoot completion failed");
                Modules.Logger.LogError(ex);
                ClearTakeLootState("TakeLoot:completionException");
            }
            finally
            {
                StopLootPickupState(botPlayer);
            }
        }

        private bool TryFinishTransferredLoot(string reason)
        {
            Item? rootItem = activeLootItem?.ItemOwner?.RootItem;
            Player? botPlayer = BotOwner?.GetPlayer;
            if (rootItem == null || !IsLootNowInBotInventory(botPlayer, rootItem))
            {
                return false;
            }

            FinishLootPickupSuccess(botPlayer, rootItem, reason);
            return true;
        }

        private void FinishLootPickupSuccess(Player? botPlayer, Item rootItem, string reason)
        {
            botPlayer?.UpdateInteractionCast();

            if (followerData?.IsSquadMate == true)
            {
                InteractableObjects.StoreItem(BotOwner, rootItem);
            }

            if (rootItem is Weapon && rootItem.GetItemComponent<KnifeComponent>() == null)
            {
                BotOwner.WeaponManager.UpdateWeaponsList();
            }

            BotOwner.BotTalk.TrySay(EPhraseTrigger.Roger, false);
            ClearTakeLootState(reason);
        }

        private bool IsLootNowInBotInventory(Player? botPlayer, Item rootItem)
        {
            InventoryController? inventory = botPlayer?.InventoryController ?? BotOwner?.GetPlayer?.InventoryController;
            if (inventory == null || rootItem == null || string.IsNullOrEmpty(rootItem.Id))
            {
                return false;
            }

            return inventory.TryFindItem(rootItem.Id, out Item foundItem) &&
                   ReferenceEquals(foundItem, rootItem);
        }

        private static void StopLootPickupState(Player? botPlayer)
        {
            try
            {
                if (botPlayer == null)
                {
                    return;
                }

                if (botPlayer.MovementContext != null)
                {
                    botPlayer.MovementContext.PickupAction = null;
                }

                if (botPlayer.CurrentManagedState is PickupStateClass pickupState)
                {
                    pickupState.Pickup(false, null);
                }
            }
            catch
            {
                // best-effort cleanup only
            }
        }

        private void CleanupLootInteraction(string reason)
        {
            if (!lootPickupInProgress &&
                lootPickupReadyAt <= 0f &&
                lootPickupAttemptStartedAt <= 0f &&
                activeLootItem == null &&
                BotOwner?.GetPlayer?.CurrentManagedState is not PickupStateClass)
            {
                return;
            }

            StopLootPickupState(BotOwner?.GetPlayer);
            lootPickupInProgress = false;
            lootPickupReadyAt = 0f;
            lootPickupAttemptStartedAt = 0f;
            activeLootItem = null;

            if (BotOwner != null)
            {
                InteractableObjects.RemoveTaker(BotOwner);
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }

                BotOwner.SetPose(1f);
            }

            InteractableObjects.ClearCurLootItem();
        }

        private void ClearTakeLootState(string reason)
        {
            if (!string.Equals(reason, "TakeLoot:done", StringComparison.Ordinal) &&
                !string.Equals(reason, "TakeLoot:actionStop", StringComparison.Ordinal))
            {
                Modules.Logger.LogInfo(
                    $"[LootCommand] Take loot ended for '{BotOwner?.Profile?.Nickname ?? BotOwner?.ProfileId ?? "unknown"}': {reason}");
            }

            CleanupLootInteraction(reason);
            followerData?.ClearCommand(reason);
        }

        private void HandleOpenDoor()
        {
            activeDoor ??= InteractableObjects.GetDoorToOpen(BotOwner);
            if (activeDoor == null)
            {
                ClearOpenDoorState("OpenDoor:missingDoor");
                return;
            }

            if (activeDoor.DoorState == EDoorState.Open)
            {
                ClearOpenDoorState("OpenDoor:alreadyOpen");
                return;
            }

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();

            if (!doorMoveIssued)
            {
                Vector3 position = activeDoor.transform.position;
                if (!NavMesh.SamplePosition(position, out NavMeshHit navMeshHit, 2f, NavMesh.AllAreas))
                {
                    ClearOpenDoorState("OpenDoor:noNavMesh");
                    return;
                }

                if (BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) != NavMeshPathStatus.PathComplete)
                {
                    ClearOpenDoorState("OpenDoor:pathInvalid");
                    return;
                }

                BotOwner.GoToSomePointData.SetPoint(navMeshHit.position);
                BotOwner.Steering.LookToMovingDirection();
                doorMoveIssued = true;
                doorTimeoutAt = Time.time + 7f;
                return;
            }

            if (doorTimeoutAt > 0f && Time.time > doorTimeoutAt)
            {
                ClearOpenDoorState("OpenDoor:timeout");
                return;
            }

            if (!doorInteractIssued)
            {
                BotOwner.GoToSomePointData.UpdateToGo(false);
            }

            if (!BotOwner.GoToSomePointData.IsCome())
            {
                return;
            }

            if (doorInteractIssued)
            {
                return;
            }

            BotOwner.StopMove();
            BotOwner.DoorOpener.OnEndInteract -= OnDoorInteractEnded;
            BotOwner.DoorOpener.OnEndInteract += OnDoorInteractEnded;
            BotOwner.DoorOpener.Interact(activeDoor, EInteractionType.Open);
            doorInteractIssued = true;
        }

        private void OnDoorInteractEnded()
        {
            ClearOpenDoorState("OpenDoor:done");
        }

        private void CleanupDoorInteraction()
        {
            BotOwner?.DoorOpener.OnEndInteract -= OnDoorInteractEnded;
            activeDoor = null;
            doorMoveIssued = false;
            doorInteractIssued = false;
            doorTimeoutAt = 0f;
        }

        private void ClearOpenDoorState(string reason)
        {
            CleanupDoorInteraction();
            InteractableObjects.RemoveOpener(BotOwner);
            InteractableObjects.SetCurDoor(null);
            followerData?.ClearCommand(reason);
        }
    }
}
