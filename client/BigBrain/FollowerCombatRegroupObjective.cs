using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerCombatRegroupObjective : FollowerCombatObjectiveBase
    {
        // Internal trigger reason used by the default objective to request an immediate switch into
        // regroup. This should never survive long enough to become a real layer action.
        internal const string ActivateRegroupReason = "objective.regroup";
        private const float CombatRegroupCompleteDistance = 8f;
        private const float CombatRegroupCompleteDistanceMarksman = 12f;
        private const float CombatRegroupSameLevelTolerance = 1.75f;
        private const float CombatRegroupCoverRadius = 18f;
        private const float CombatRegroupCoverRadiusMarksman = 24f;
        private const float BossSectorRadius = 20f;
        private const float RegroupHotContactSeconds = 2.5f;
        private const float RegroupFallbackSpreadMinRadius = 1f;
        private const float RegroupFallbackSpreadMaxRadius = 6f;
        private const float RegroupFallbackSpacing = 2f;
        private const float RegroupClaimTtlSeconds = 2f;
        internal const string RegroupReasonPrefix = "regroup.";
        private const string RegroupWithdrawBackwardReason = "regroup.withdraw.backward";
        private const string RegroupWithdrawForwardReason = "regroup.withdraw.forward";
        private const string RegroupWithdrawSideReason = "regroup.withdraw.side";
        private const float RegroupSideDotThreshold = 0.35f;

        private Vector3 currentTarget;
        private Vector3 bossSectorAnchor;
        private bool hasTarget;
        private bool hasBossSectorAnchor;
        private bool complete;

        private enum RegroupBossDirection
        {
            Front,
            Back,
            Side
        }

        public FollowerCombatRegroupObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            bossSectorAnchor = Vector3.zero;
            hasTarget = false;
            hasBossSectorAnchor = false;
            complete = false;
        }

        public override void Activate()
        {
            // Regroup is command-triggered but objective-owned, so each activation should discard
            // previous bossward targets and recompute from current combat geometry.
            Reset();
        }

        public override void Deactivate()
        {
            ReleaseDestinationClaim();
            complete = false;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            if (IsRegroupReason(nextDecision.Reason))
            {
                // Regroup should not inherit a low combat pose from prior cover/shoot logic.
                // Bring the bot upright so backward withdraw and bossward movement read clearly.
                if (BotOwner.Mover.TargetPose < 0.85f)
                {
                    BotOwner.SetPose(1f);
                }
            }
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            // Regroup owns the movement objective, but survival work still preempts the current regroup
            // phase. The objective itself remains regroup until it completes or is explicitly replaced.
            if (TryGetMedicalInterrupt(goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> medicalDecision))
            {
                return medicalDecision;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? dogFight = CombatCommon.TryGetDogFightDecision();
            if (dogFight != null)
            {
                return dogFight.Value;
            }

            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }


            Vector3 bossPosition = CombatCommon.GetBossPosition();
            if (HasReachedBoss(bossPosition))
            {
                CombatCommon.HoldCoverForMaxDuration();
                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "regroup.arrived");
            }

            // The regroup movement style depends on where the boss is from this bot's perspective.
            // Back means withdraw while staying oriented to the threat; front/side can use the normal
            // attack-moving action because our attack-moving look helper keeps aim on the threat lane.
            RegroupBossDirection bossDirection = GetBossDirectionFromBot(goalEnemy, bossPosition);
            bool hotContact = IsHotEnemyContact(goalEnemy);

            // While contact is still hot, regroup stays combat-active and moves bossward without
            // dropping threat orientation.
            if (hotContact)
            {
                if (TryGetRegroupCombatMove(goalEnemy, bossPosition, bossDirection, out AICoreActionResultStruct<BotLogicDecision, GClass26> combatMove))
                {
                    return combatMove;
                }
            }

            return GetRegroupRunDecision(bossPosition);
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (IsHealingDecision(currentDecision.Action))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                return new AICoreActionEndStruct("regroupEnemyMissing", true);
            }

            if (HasReachedBoss(CombatCommon.GetBossPosition()))
            {
                complete = true;
                return new AICoreActionEndStruct("regroupReachedBoss", true);
            }

            if (HasActivePushOrder())
            {
                complete = true;
                return new AICoreActionEndStruct("regroupPushOverride", true);
            }

            if (currentDecision.Reason != null && currentDecision.Reason.StartsWith("regroup.withdraw", System.StringComparison.Ordinal))
            {
                if (CombatCommon.TryGetNeedHealDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupNeedHeal", true);
                }

                if (CombatCommon.TryGetDogFightDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupDogFight", true);
                }

                if (!IsHotEnemyContact(goalEnemy))
                {
                    return new AICoreActionEndStruct("regroupColdContact", true);
                }

                if (HasReachedCurrentTarget())
                {
                    ClearCurrentTarget();
                    return new AICoreActionEndStruct("regroupReachedWithdrawTarget", true);
                }

                if (HasBossSectorChanged(CombatCommon.GetBossPosition()))
                {
                    return new AICoreActionEndStruct("regroupBossSectorChanged", true);
                }

                return default;
            }

            if (currentDecision.Reason != null && currentDecision.Reason.StartsWith("regroup.run", System.StringComparison.Ordinal))
            {
                if (CombatCommon.TryGetNeedHealDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupNeedHeal", true);
                }

                if (CombatCommon.TryGetDogFightDecision() != null)
                {
                    return new AICoreActionEndStruct("regroupDogFight", true);
                }

                if (IsHotEnemyContact(goalEnemy))
                {
                    return new AICoreActionEndStruct("regroupThreatReturned", true);
                }

                if (HasReachedCurrentTarget())
                {
                    ClearCurrentTarget();
                    return new AICoreActionEndStruct("regroupReachedRunTarget", true);
                }

                if (HasBossSectorChanged(CombatCommon.GetBossPosition()))
                {
                    return new AICoreActionEndStruct("regroupBossSectorChanged", true);
                }

                return default;
            }

            if (string.Equals(currentDecision.Reason, "regroup.arrived", System.StringComparison.Ordinal))
            {
                AICoreActionEndStruct arrivedEnd = CombatCommon.EndBaseHoldPosition("regroup.arrived");
                if (arrivedEnd.Value)
                {
                    complete = true;
                }

                return arrivedEnd;
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        private bool TryGetMedicalInterrupt(
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            // Reuse the shared heal decision tree, but let regroup remain the owning objective.
            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = CombatCommon.TryGetNeedHealDecision();
            if (healDecision == null)
            {
                return false;
            }

            decision = healDecision.Value;
            return true;
        }

        private bool TryGetRegroupCombatMove(
            EnemyInfo goalEnemy,
            Vector3 bossPosition,
            RegroupBossDirection bossDirection,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;

            if (TryAssignRegroupCover(goalEnemy, bossPosition, bossDirection, out Vector3 targetPosition))
            {
                // Keep a single bossward target for the current regroup phase so the action can keep
                // moving continuously instead of re-picking a slightly different point every update.
                currentTarget = targetPosition;
                bossSectorAnchor = bossPosition;
                hasTarget = true;
                hasBossSectorAnchor = true;
                UpsertDestinationClaim(targetPosition);
                BotOwner.GoToSomePointData.SetPoint(targetPosition);
                decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                    BotLogicDecision.attackMoving,
                    GetWithdrawReason(bossDirection));
                return true;
            }

            currentTarget = GetFallbackBossDestination(bossPosition);
            bossSectorAnchor = bossPosition;
            hasTarget = true;
            hasBossSectorAnchor = true;
            UpsertDestinationClaim(currentTarget);
            BotOwner.GoToSomePointData.SetPoint(currentTarget);
            decision = new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.attackMoving,
                GetWithdrawReason(bossDirection));
            return true;
        }

        private AICoreActionResultStruct<BotLogicDecision, GClass26> GetRegroupRunDecision(Vector3 bossPosition)
        {
            // Once contact cools off, regroup becomes a pure "close back to boss" objective.
            // Do not select the destination here. CombatRegroupRunAction owns one cached run
            // target from Start() until the action reaches it, so repeated decisions cannot
            // keep nudging the point while the bot is already running.
            if (!hasBossSectorAnchor || HasBossSectorChanged(bossPosition))
            {
                bossSectorAnchor = bossPosition;
                hasBossSectorAnchor = true;
            }

            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            hasTarget = false;
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.goToPoint, "regroup.run");
        }

        private bool TryAssignRegroupCover(EnemyInfo goalEnemy, Vector3 bossPosition, RegroupBossDirection bossDirection, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            float regroupCoverRadius = GetRegroupCoverRadius();

            if (bossDirection == RegroupBossDirection.Back)
            {
                // True withdrawal: prefer bossward cover that hides from the enemy over cover that only
                // provides a shooting lane.
                if (CombatCommon.TryFindCoverTowardBoss(
                    goalEnemy,
                    bossPosition,
                    regroupCoverRadius,
                    requireShootLane: false,
                    requireHideFromEnemy: true,
                    out CustomNavigationPoint? withdrawCover) &&
                    TryAcceptRegroupCover(withdrawCover, bossPosition, out targetPosition))
                {
                    CombatCommon.AssignCover(withdrawCover);
                    return true;
                }
            }
            else
            {
                // Boss is ahead or lateral, so regroup is a rejoin under contact. Prefer bossward cover
                // that still preserves a shot while moving up or across.
                if (CombatCommon.TryFindCoverTowardBoss(
                    goalEnemy,
                    bossPosition,
                    regroupCoverRadius,
                    requireShootLane: true,
                    requireHideFromEnemy: false,
                    out CustomNavigationPoint? supportCover) &&
                    TryAcceptRegroupCover(supportCover, bossPosition, out targetPosition))
                {
                    CombatCommon.AssignCover(supportCover);
                    return true;
                }
            }

            if (CombatCommon.TryFindBossCover(goalEnemy, bossPosition, regroupCoverRadius, out CustomNavigationPoint? bossCover) &&
                TryAcceptRegroupCover(bossCover, bossPosition, out targetPosition))
            {
                CombatCommon.AssignCover(bossCover);
                return true;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                targetPosition = bossHit.position;
                return true;
            }

            targetPosition = bossPosition;
            return true;
        }

        private bool IsHotEnemyContact(EnemyInfo goalEnemy)
        {
            if (goalEnemy == null)
            {
                return false;
            }

            if (goalEnemy.IsVisible)
            {
                return true;
            }

            // Recently seen contact keeps regroup in its combat-withdraw phase for a short window before
            // collapsing into a pure run-back-to-boss phase.
            return Time.time - goalEnemy.PersonalLastSeenTime < RegroupHotContactSeconds;
        }

        private RegroupBossDirection GetBossDirectionFromBot(EnemyInfo goalEnemy, Vector3 bossPosition)
        {
            Vector3 enemyAnchor = FollowerCombatCommon.GetEnemyAnchor(goalEnemy);
            Vector3 toEnemy = enemyAnchor - BotOwner.Position;
            Vector3 toBoss = bossPosition - BotOwner.Position;
            toEnemy.y = 0f;
            toBoss.y = 0f;
            if (toEnemy.sqrMagnitude <= 0.01f || toBoss.sqrMagnitude <= 0.01f)
            {
                return RegroupBossDirection.Front;
            }

            float dot = Vector3.Dot(toEnemy.normalized, toBoss.normalized);
            if (dot <= -RegroupSideDotThreshold)
            {
                return RegroupBossDirection.Back;
            }

            if (dot >= RegroupSideDotThreshold)
            {
                return RegroupBossDirection.Front;
            }

            return RegroupBossDirection.Side;
        }

        private static string GetWithdrawReason(RegroupBossDirection bossDirection)
        {
            return bossDirection switch
            {
                RegroupBossDirection.Back => RegroupWithdrawBackwardReason,
                RegroupBossDirection.Side => RegroupWithdrawSideReason,
                _ => RegroupWithdrawForwardReason,
            };
        }

        private bool HasReachedBoss(Vector3 bossPosition)
        {
            float verticalDiff = Mathf.Abs(BotOwner.Position.y - bossPosition.y);
            if (verticalDiff > CombatRegroupSameLevelTolerance)
            {
                return false;
            }

            float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPosition);
            return navDistanceToBoss <= GetRegroupCompleteDistance();
        }

        private float GetRegroupCompleteDistance()
        {
            return CombatCommon.GetFollowerTactic() == FollowerCombatTactic.Marksman
                ? CombatRegroupCompleteDistanceMarksman
                : CombatRegroupCompleteDistance;
        }

        private float GetRegroupCoverRadius()
        {
            return CombatCommon.GetFollowerTactic() == FollowerCombatTactic.Marksman
                ? CombatRegroupCoverRadiusMarksman
                : CombatRegroupCoverRadius;
        }

        private bool HasReachedCurrentTarget()
        {
            if (BotOwner.GoToSomePointData.IsCome())
            {
                return true;
            }

            if (!hasTarget)
            {
                return false;
            }

            return (BotOwner.Position - currentTarget).sqrMagnitude <= 2f * 2f;
        }

        private bool TryAcceptRegroupCover(CustomNavigationPoint? cover, Vector3 bossPosition, out Vector3 targetPosition)
        {
            targetPosition = Vector3.zero;
            if (cover == null)
            {
                return false;
            }

            // A bossward cover is only an intermediate step. Once that step is reached, do not pick
            // the same nearby owned cover again unless it also completes regroup.
            if (!HasReachedBoss(bossPosition) && HasReachedPosition(cover.Position))
            {
                return false;
            }

            targetPosition = cover.Position;
            return true;
        }

        private bool HasReachedPosition(Vector3 position)
        {
            return (BotOwner.Position - position).sqrMagnitude <= 2f * 2f;
        }

        private void ClearCurrentTarget()
        {
            ReleaseDestinationClaim();
            currentTarget = Vector3.zero;
            hasTarget = false;
        }

        private Vector3 GetFallbackBossDestination(Vector3 bossPosition)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents) &&
                combatEvents.TryFindBossSpreadDestination(
                    BotOwner,
                    bossPosition,
                    RegroupFallbackSpreadMinRadius,
                    RegroupFallbackSpreadMaxRadius,
                    CombatRegroupSameLevelTolerance,
                    RegroupFallbackSpacing,
                    out Vector3 spreadTarget))
            {
                return spreadTarget;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                return bossHit.position;
            }

            return bossPosition;
        }

        private void UpsertDestinationClaim(Vector3 target)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.UpsertDestinationClaim(BotOwner, target, RegroupClaimTtlSeconds);
            }
        }

        private void ReleaseDestinationClaim()
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.ReleaseDestinationClaim(BotOwner);
            }
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

        private bool HasBossSectorChanged(Vector3 bossPosition)
        {
            if (!hasBossSectorAnchor)
            {
                return false;
            }

            return (bossPosition - bossSectorAnchor).sqrMagnitude > BossSectorRadius * BossSectorRadius;
        }

        private static bool IsHealingDecision(BotLogicDecision decision)
        {
            return decision == BotLogicDecision.heal || decision == BotLogicDecision.healStimulators;
        }

        private bool HasActivePushOrder()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.PushEnemy;
        }

        public static bool IsRegroupReason(string? reason)
        {
            return !string.IsNullOrEmpty(reason) &&
                reason != null &&
                reason.StartsWith(RegroupReasonPrefix, System.StringComparison.Ordinal);
        }

        public static bool IsRunReason(string? reason)
        {
            return string.Equals(reason, "regroup.run", System.StringComparison.Ordinal);
        }

        public static bool IsRegroupActivationReason(string? reason)
        {
            return string.Equals(reason, ActivateRegroupReason, System.StringComparison.Ordinal);
        }

        public static bool IsBackwardWithdrawReason(string? reason)
        {
            return string.Equals(reason, RegroupWithdrawBackwardReason, System.StringComparison.Ordinal);
        }
    }
}
