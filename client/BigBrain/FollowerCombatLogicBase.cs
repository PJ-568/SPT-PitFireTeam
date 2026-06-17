using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;
using Vector3 = UnityEngine.Vector3;

namespace pitTeam.BigBrain
{
    internal abstract class FollowerCombatLogicBase
    {
        // High-level combat intent. Each objective owns its own decision stack and end logic.
        protected enum CombatObjectiveKind
        {
            Default,
            Regroup,
            OrderedPush,
            Suppression,
            NeedSniper,
            Grenadier,
        }

        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;
        protected readonly FollowerCombatCommon combatCommon;
        protected bool errorLogged;
        protected readonly FollowerCombatObjectiveBase defaultObjective;
        protected readonly FollowerCombatObjectiveBase sniperObjective;
        protected readonly FollowerCombatObjectiveBase regroupObjective;
        protected readonly FollowerCombatOrderedPushObjective orderedPushObjective;
        protected readonly FollowerCombatSuppressionObjective suppressionObjective;
        protected readonly FollowerCombatObjectiveBase needSniperObjective;
        protected readonly FollowerCombatGrenadierObjective grenadierObjective;
        protected CombatObjectiveKind currentObjective = CombatObjectiveKind.Default;

        protected FollowerCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
            combatCommon = new FollowerCombatCommon(botOwner);
            defaultObjective = CreateDefaultObjective(botOwner, combatCommon);
            sniperObjective = CreateSniperObjective(botOwner, combatCommon);
            regroupObjective = CreateRegroupObjective(botOwner, combatCommon);
            orderedPushObjective = CreateOrderedPushObjective(botOwner, combatCommon);
            suppressionObjective = CreateSuppressionObjective(botOwner, combatCommon);
            needSniperObjective = CreateNeedSniperObjective(botOwner, combatCommon);
            grenadierObjective = CreateGrenadierObjective(botOwner, combatCommon);
        }

        public bool ShallUseNow() => combatCommon.HasActiveCombatEnemy();

        public bool HasActiveOrPendingHealWork() => combatCommon.HasActiveOrPendingHealWork();

        public bool HasImmediateExplosiveDanger() => combatCommon.HasImmediateExplosiveDanger();

        public AICoreActionResultStruct<BotLogicDecision, GClass26> GetMedicalDecision()
        {
            combatCommon.RepairGoalEnemyMemory();
            AICoreActionResultStruct<BotLogicDecision, GClass26>? healDecision = combatCommon.TryGetNeedHealDecision();
            if (healDecision != null)
            {
                return healDecision.Value;
            }

            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "medicalHold");
        }

        public virtual void Reset()
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            followerData?.SetCombatRegroupBossAnchor(false);
            followerData?.ClearOrderedPushTargetLock("CombatLogic:Reset");
            combatCommon.Reset();
            defaultObjective.Reset();
            sniperObjective.Reset();
            regroupObjective.Reset();
            orderedPushObjective.Reset();
            suppressionObjective.Reset();
            needSniperObjective.Reset();
            grenadierObjective.Reset();
            currentObjective = CombatObjectiveKind.Default;
        }

        public virtual AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision()
        {
            combatCommon.RepairGoalEnemyMemory();
            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (goalEnemy == null)
            {
                if (combatCommon.TryGetNoEnemyThreatCoverDecision(
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> noEnemyThreatDecision))
                {
                    return noEnemyThreatDecision;
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "nullEnemy");
            }

            FollowerEnemyInfoCorrection.CorrectDistanceOnly(BotOwner, goalEnemy);

            try
            {
                BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
                if (TryConsumeCombatGestureCommand(followerData, goalEnemy, out AICoreActionResultStruct<BotLogicDecision, GClass26> commandDecision))
                {
                    return commandDecision;
                }

                RefreshObjective(goalEnemy);
                if (currentObjective != CombatObjectiveKind.Grenadier &&
                    combatCommon.TryCreatePendingLauncherPrimaryFallbackDecision(
                        out AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackDecision))
                {
                    return fallbackDecision;
                }

                AICoreActionResultStruct<BotLogicDecision, GClass26> decision = GetCurrentObjective().GetDecision(goalEnemy);
                // Default combat can request an objective switch without leaking a fake action to the layer.
                // When that happens, activate regroup immediately and return regroup's first real decision.
                if (currentObjective != CombatObjectiveKind.Regroup &&
                    FollowerCombatRegroupObjective.IsRegroupActivationReason(decision.Reason))
                {
                    ActivateRegroupObjective();
                    return regroupObjective.GetDecision(goalEnemy);
                }

                if (currentObjective != CombatObjectiveKind.Grenadier &&
                    FollowerCombatGrenadierObjective.IsAutonomousActivationReason(decision.Reason))
                {
                    ActivateGrenadierObjective(ordered: false, "activateGrenadierAuto");
                    return grenadierObjective.GetDecision(goalEnemy);
                }

                return decision;
            }
            catch (Exception ex)
            {
                if (!errorLogged)
                {
                    Logger.LogError(ex);
                    errorLogged = true;
                    return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "errorLogged");
                }

                return new AICoreActionResultStruct<BotLogicDecision, GClass26>(BotLogicDecision.holdPosition, "errorLogged2");
            }
        }

        public virtual AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            combatCommon.TryApplyPendingLauncherPrimaryFallback(currentDecision);

            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            FollowerEnemyInfoCorrection.CorrectDistanceOnly(BotOwner, goalEnemy);

            if (currentObjective == CombatObjectiveKind.OrderedPush &&
                followerData?.HasOrderedPushCancelRequest == true)
            {
                return new AICoreActionEndStruct("orderedPushCancelRequested", true);
            }

            if (goalEnemy != null &&
                HasActiveCombatGestureOrder(followerData) &&
                CanInterruptForCombatGestureOrder(currentDecision))
            {
                return new AICoreActionEndStruct("combatGestureBreakMovement", true);
            }

            if (currentObjective != CombatObjectiveKind.Suppression &&
                goalEnemy != null &&
                ShouldConsumeSuppressCommand(followerData, goalEnemy))
            {
                if (!CanSatisfySuppressionOrder(followerData))
                {
                    followerData?.ClearCommand("CombatObjective:RejectSuppressionWeapon");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                if (!CanInterruptForSuppressionOrder(currentDecision))
                {
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
                {
                    followerData?.ClearCommand("CombatObjective:RejectSuppression");
                    return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
                }

                return new AICoreActionEndStruct("objectiveSuppressionOrder", true);
            }

            if (currentObjective != CombatObjectiveKind.OrderedPush &&
                goalEnemy != null &&
                ShouldConsumePushCommand(followerData, goalEnemy) &&
                CanInterruptForOrderedPushOrder(currentDecision))
            {
                return new AICoreActionEndStruct("objectivePushOrder", true);
            }

            if (currentObjective != CombatObjectiveKind.NeedSniper &&
                goalEnemy != null &&
                ShouldConsumeNeedSniperCommand(followerData, goalEnemy))
            {
                if (ShouldRejectNeedSniperObjective(goalEnemy))
                {
                    followerData?.ClearCommand("CombatObjective:RejectNeedSniper");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return FollowerCombatCommon.Continue();
                }

                return new AICoreActionEndStruct("objectiveNeedSniperOrder", true);
            }

            if (ShouldConsumeRegroupCommand(followerData) &&
                CanInterruptForRegroupOrder(currentDecision))
            {
                return new AICoreActionEndStruct("objectiveRegroupOrder", true);
            }

            // Objective ownership is stateful, not encoded in the action reason. Regroup may emit
            // shared interrupt actions such as heal/dogFight, and those still need to return to regroup.
            return GetCurrentObjective().ShallEndCurrentDecision(currentDecision);
        }

        public virtual void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            // Same ownership rule as end logic: the active objective owns even shared-reason actions.
            GetCurrentObjective().DecisionChanged(prevDecision, nextDecision);
        }

        public string GetCurrentObjectiveName()
        {
            return GetCurrentObjective().GetType().Name;
        }

        public virtual void StartDecision()
        {
            combatCommon.RepairGoalEnemyMemory();
            ActivatePrimaryObjectiveForStart();
            GetCurrentObjective().StartDecision();
        }

        protected virtual FollowerCombatObjectiveBase CreateDefaultObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatDefaultObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateSniperObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatSniperObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateRegroupObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatRegroupObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatOrderedPushObjective CreateOrderedPushObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatOrderedPushObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatSuppressionObjective CreateSuppressionObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatSuppressionObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatObjectiveBase CreateNeedSniperObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatNeedSniperObjective(botOwner, combatCommon);
        }

        protected virtual FollowerCombatGrenadierObjective CreateGrenadierObjective(
            BotOwner botOwner,
            FollowerCombatCommon combatCommon)
        {
            return new FollowerCombatGrenadierObjective(botOwner, combatCommon);
        }

        protected virtual bool ShouldConsumeRegroupCommand(BotFollowerPlayer? followerData)
        {
            // RegroupNearBoss is only a trigger for combat objective selection.
            // Once consumed, combat runs from objective state rather than command polling.
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        protected virtual bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool ShouldConsumePushCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool ShouldConsumeSuppressCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return false;
        }

        protected virtual bool CanInterruptForSuppressionOrder(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return !combatCommon.IsInFight(currentDecision.Action) &&
                   currentDecision.Action != BotLogicDecision.heal &&
                   currentDecision.Action != BotLogicDecision.healStimulators;
        }

        protected virtual bool CanInterruptForRegroupOrder(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (currentDecision.Action == BotLogicDecision.heal ||
                currentDecision.Action == BotLogicDecision.healStimulators ||
                currentDecision.Action == BotLogicDecision.dogFight)
            {
                return false;
            }

            return FollowerCombatCommon.IsMovementDecision(currentDecision) ||
                   !combatCommon.IsInFight(currentDecision.Action);
        }

        protected virtual bool CanInterruptForOrderedPushOrder(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (IsActiveGrenadierLauncherSuppress(currentDecision))
            {
                return false;
            }

            return currentDecision.Action != BotLogicDecision.heal &&
                   currentDecision.Action != BotLogicDecision.healStimulators &&
                   currentDecision.Action != BotLogicDecision.dogFight;
        }

        private bool IsActiveGrenadierLauncherSuppress(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            return currentObjective == CombatObjectiveKind.Grenadier &&
                   currentDecision.Action == BotLogicDecision.suppressFire &&
                   FollowerCombatCommon.IsGrenadeLauncherSuppressReason(currentDecision.Reason);
        }

        protected virtual bool ShouldReturnToPrimaryObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            // Regroup is temporary combat intent: finish it when it completes, combat ends, or
            // an explicit combat order arrives and should hand control back to the tactic's primary stack.
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   regroupObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromSuppressionObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   suppressionObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromOrderedPushObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   ShouldConsumeRegroupCommand(followerData) ||
                   orderedPushObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromNeedSniperObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   needSniperObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromGrenadierObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveCombatGestureOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   ShouldConsumeRegroupCommand(followerData) ||
                   grenadierObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        private bool TryConsumeCombatGestureCommand(
            BotFollowerPlayer? followerData,
            EnemyInfo goalEnemy,
            out AICoreActionResultStruct<BotLogicDecision, GClass26> decision)
        {
            decision = default;
            if (followerData == null ||
                !followerData.TryGetActiveCommand(out FollowerCommandType command, out Vector3 target))
            {
                return false;
            }

            if (command == FollowerCommandType.CombatComeToBossCover)
            {
                if (!combatCommon.TryCreateBossCoverAttackMovingDecision(
                        goalEnemy,
                        CombatDistanceConfiguration.Instance.GetBossCoverSearchRadius(),
                        "command.comeWithMeBossCover",
                        out decision))
                {
                    followerData.ClearCommand("CombatCommand:NoBossCover");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return false;
                }

                followerData.ClearCommand("CombatCommand:ConsumeComeWithMeCover");
                ActivatePrimaryObjective();
                return true;
            }

            if (command == FollowerCommandType.CombatMoveToPointTactical)
            {
                if (!combatCommon.TryCreateBossCommandTacticalPointDecision(
                        target,
                        "command.thereTactical",
                        out decision))
                {
                    followerData.ClearCommand("CombatCommand:InvalidTacticalPoint");
                    BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                    BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                    return false;
                }

                followerData.ClearCommand("CombatCommand:ConsumeThereTactical");
                ActivatePrimaryObjective();
                return true;
            }

            return false;
        }

        private bool CanInterruptForCombatGestureOrder(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!FollowerCombatCommon.IsMovementDecision(currentDecision))
            {
                return false;
            }

            string? reason = currentDecision.Reason;
            if (!string.IsNullOrEmpty(reason) &&
                reason.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return !IsActiveGrenadierLauncherSuppress(currentDecision);
        }

        private void RefreshObjective(EnemyInfo goalEnemy)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData?.TryConsumeOrderedPushCancelRequest(out string cancelReason) == true)
            {
                if (currentObjective == CombatObjectiveKind.OrderedPush)
                {
                    ActivatePrimaryObjective($"orderedPushCancel:{cancelReason}");
                }

                return;
            }

            if (ShouldConsumePushCommand(followerData, goalEnemy))
            {
                ActivateOrderedPushObjective(followerData!, goalEnemy);
                return;
            }

            if (ShouldConsumeRegroupCommand(followerData))
            {
                ActivateRegroupObjective(followerData!);
                return;
            }

            if (ShouldConsumeSuppressCommand(followerData, goalEnemy))
            {
                ActivateSuppressionObjective(followerData!, goalEnemy);
                return;
            }

            if (ShouldConsumeNeedSniperCommand(followerData, goalEnemy))
            {
                ActivateNeedSniperObjective(followerData!, goalEnemy);
                return;
            }

            if (currentObjective == CombatObjectiveKind.Regroup && ShouldReturnToPrimaryObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.Suppression && ShouldReturnFromSuppressionObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.OrderedPush && ShouldReturnFromOrderedPushObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.NeedSniper && ShouldReturnFromNeedSniperObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }

            if (currentObjective == CombatObjectiveKind.Grenadier && ShouldReturnFromGrenadierObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }
        }

        private void ActivateRegroupObjective(BotFollowerPlayer followerData)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:Regroup");
            followerData.ClearCommand("CombatObjective:ConsumeRegroup");
            ActivateRegroupObjective(forceReset: true, "activateRegroupOrder");
            followerData.SetCombatRegroupBossAnchor(true);
        }

        private void ActivateOrderedPushObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectPush");
                return;
            }

            followerData.ClearCommand("CombatObjective:ConsumePush");
            DeactivateGrenadierForObjectiveSwitch("switch.orderedPush");
            followerData.ActivateOrderedPushTargetLock(goalEnemy);
            orderedPushObjective.Activate(goalEnemy);
            currentObjective = CombatObjectiveKind.OrderedPush;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateOrderedPush");
        }

        private void ActivateRegroupObjective(bool forceReset = false, string reason = "activateRegroup")
        {
            if (currentObjective == CombatObjectiveKind.Regroup && !forceReset)
            {
                return;
            }

            // Activate resets regroup-local state so every new regroup order starts fresh from the
            // follower's current combat geometry instead of reusing stale bossward targets.
            DeactivateGrenadierForObjectiveSwitch(reason);
            regroupObjective.Activate();
            currentObjective = CombatObjectiveKind.Regroup;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        private void ActivateSuppressionObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:Suppression");
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectSuppression");
                return;
            }

            if (!CanSatisfySuppressionOrder(followerData))
            {
                followerData.ClearCommand("CombatObjective:RejectSuppressionWeapon");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            Vector3 suppressTarget = Vector3.zero;
            bool suppressRequiresLauncher = false;
            bool suppressForceWeapon = false;
            bool suppressUseAutomaticSecondary = false;
            if (followerData.TryPeekActiveCommand(out FollowerCommandType command, out Vector3 target, out _) &&
                command == FollowerCommandType.SuppressEnemy)
            {
                suppressTarget = target;
                suppressRequiresLauncher = followerData.SuppressEnemyRequiresLauncher;
                suppressForceWeapon = followerData.SuppressEnemyForceWeapon;
                suppressUseAutomaticSecondary = followerData.SuppressEnemyUseAutomaticSecondary;
            }

            followerData.ClearCommand("CombatObjective:ConsumeSuppression");
            DeactivateGrenadierForObjectiveSwitch("switch.suppressionOrder");
            bool launcherSuppressCooldownActive =
                combatCommon.IsGrenadeLauncherSuppressCooldownActive(ordered: true, out _);
            if (launcherSuppressCooldownActive)
            {
                combatCommon.RecordGrenadeLauncherSuppressCooldownSkip(
                    ordered: true,
                    suppressRequiresLauncher ? "orderedSuppressRequiresLauncher" : "orderedSuppress");
                suppressRequiresLauncher = false;
                suppressForceWeapon = true;
            }

            if (!launcherSuppressCooldownActive &&
                (suppressRequiresLauncher ||
                 (!suppressForceWeapon &&
                  !suppressUseAutomaticSecondary &&
                  combatCommon.HasUsableSecondPrimaryGrenadeLauncher())))
            {
                combatCommon.SetOrderedSuppressTarget(suppressTarget);
                ActivateGrenadierObjective(ordered: true, "activateGrenadierSuppression");
                return;
            }

            if (currentObjective != CombatObjectiveKind.Suppression)
            {
                suppressionObjective.Activate(suppressRequiresLauncher, suppressForceWeapon, suppressUseAutomaticSecondary);
                currentObjective = CombatObjectiveKind.Suppression;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateSuppression");
            }
            else
            {
                suppressionObjective.Activate(suppressRequiresLauncher, suppressForceWeapon, suppressUseAutomaticSecondary);
            }

            combatCommon.SetOrderedSuppressTarget(suppressTarget);
        }

        private bool CanSatisfySuppressionOrder(BotFollowerPlayer? followerData)
        {
            if (followerData?.SuppressEnemyRequiresLauncher == true)
            {
                return combatCommon.HasUsableSecondPrimaryGrenadeLauncher();
            }

            if (combatCommon.CanCurrentWeaponSuppressOrUseGrenadeLauncher())
            {
                return true;
            }

            return followerData?.SuppressEnemyUseAutomaticSecondary == true &&
                   combatCommon.HasLoadedAutomaticSecondaryForPush();
        }

        private void ActivateNeedSniperObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            followerData.ClearOrderedPushTargetLock("CombatObjective:NeedSniper");
            if (ShouldRejectNeedSniperObjective(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectNeedSniper");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            followerData.ClearCommand("CombatObjective:ConsumeNeedSniper");
            DeactivateGrenadierForObjectiveSwitch("switch.needSniper");
            if (currentObjective != CombatObjectiveKind.NeedSniper)
            {
                needSniperObjective.Activate();
                currentObjective = CombatObjectiveKind.NeedSniper;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateNeedSniper");
            }
        }

        private void ActivateGrenadierObjective(bool ordered, string reason)
        {
            grenadierObjective.Activate(ordered);
            currentObjective = CombatObjectiveKind.Grenadier;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        private void DeactivateGrenadierForObjectiveSwitch(string reason)
        {
            if (currentObjective == CombatObjectiveKind.Grenadier)
            {
                grenadierObjective.DeactivateForObjectiveSwitch(reason);
            }
        }

        private bool ShouldRejectNeedSniperObjective(EnemyInfo goalEnemy)
        {
            return combatCommon.HasActiveOrPendingHealWork() ||
                   BotOwner.Memory.IsUnderFire ||
                   FollowerCombatCommon.WasHitRecently(BotOwner, 0.75f) ||
                   (goalEnemy.IsVisible &&
                    goalEnemy.CanShoot &&
                    goalEnemy.Distance <= CombatDistanceConfiguration.Instance.GetCloseQuarterDistance());
        }

        protected void ActivatePrimaryObjectiveForStart()
        {
            currentObjective = CombatObjectiveKind.Default;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "combatStart");
        }

        private void ActivatePrimaryObjective(string reason = "returnPrimary")
        {
            if (currentObjective == CombatObjectiveKind.Default)
            {
                return;
            }

            if (currentObjective == CombatObjectiveKind.OrderedPush)
            {
                BossPlayers.Instance?.GetFollower(BotOwner)?.ClearOrderedPushTargetLock($"CombatObjective:{reason}");
            }

            regroupObjective.Deactivate();
            orderedPushObjective.Deactivate();
            suppressionObjective.Deactivate();
            needSniperObjective.Deactivate();
            grenadierObjective.Deactivate();
            BossPlayers.Instance?.GetFollower(BotOwner)?.SetCombatRegroupBossAnchor(false);
            // Re-enter tactic combat with clean local primary-objective state, but do not call
            // StartDecision() here or the bot would incorrectly get a fresh combat opener.
            GetObjective().Activate();
            currentObjective = CombatObjectiveKind.Default;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), reason);
        }

        protected FollowerCombatObjectiveBase GetCurrentObjective()
        {
            return currentObjective switch
            {
                CombatObjectiveKind.Regroup => regroupObjective,
                CombatObjectiveKind.OrderedPush => orderedPushObjective,
                CombatObjectiveKind.Suppression => suppressionObjective,
                CombatObjectiveKind.NeedSniper => needSniperObjective,
                CombatObjectiveKind.Grenadier => grenadierObjective,
                _ => GetObjective(),
            };
        }

        protected abstract FollowerCombatObjectiveBase GetObjective();

        private static bool HasActivePushOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.PushEnemy;
        }

        private static bool HasActiveSuppressOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }

        private static bool HasActiveNeedSniperOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.NeedSniper;
        }

        private static bool HasActiveCombatGestureOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   (command == FollowerCommandType.CombatComeToBossCover ||
                    command == FollowerCommandType.CombatMoveToPointTactical);
        }
    }
}
