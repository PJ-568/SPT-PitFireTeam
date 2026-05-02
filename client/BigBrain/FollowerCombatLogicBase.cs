using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;

namespace pitTeam.BigBrain
{
    internal abstract class FollowerCombatLogicBase
    {
        // High-level combat intent. Each objective owns its own decision stack and end logic.
        protected enum CombatObjectiveKind
        {
            Default,
            Regroup,
            Suppression,
            NeedSniper
        }

        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;
        protected readonly FollowerCombatCommon combatCommon;
        protected bool errorLogged;
        protected readonly FollowerCombatObjectiveBase defaultObjective;
        protected readonly FollowerCombatObjectiveBase sniperObjective;
        protected readonly FollowerCombatObjectiveBase regroupObjective;
        protected readonly FollowerCombatObjectiveBase suppressionObjective;
        protected readonly FollowerCombatObjectiveBase needSniperObjective;
        protected CombatObjectiveKind currentObjective = CombatObjectiveKind.Default;

        protected FollowerCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
            combatCommon = new FollowerCombatCommon(botOwner);
            defaultObjective = CreateDefaultObjective(botOwner, combatCommon);
            sniperObjective = CreateSniperObjective(botOwner, combatCommon);
            regroupObjective = CreateRegroupObjective(botOwner, combatCommon);
            suppressionObjective = CreateSuppressionObjective(botOwner, combatCommon);
            needSniperObjective = CreateNeedSniperObjective(botOwner, combatCommon);
        }

        public bool ShallUseNow() => combatCommon.HasActiveCombatEnemy();

        public bool HasActiveOrPendingHealWork() => combatCommon.HasActiveOrPendingHealWork();

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
            combatCommon.Reset();
            defaultObjective.Reset();
            sniperObjective.Reset();
            regroupObjective.Reset();
            suppressionObjective.Reset();
            needSniperObjective.Reset();
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

            try
            {
                RefreshObjective(goalEnemy);
                AICoreActionResultStruct<BotLogicDecision, GClass26> decision = GetCurrentObjective().GetDecision(goalEnemy);
                // Default combat can request an objective switch without leaking a fake action to the layer.
                // When that happens, activate regroup immediately and return regroup's first real decision.
                if (currentObjective != CombatObjectiveKind.Regroup &&
                    FollowerCombatRegroupObjective.IsRegroupActivationReason(decision.Reason))
                {
                    ActivateRegroupObjective();
                    return regroupObjective.GetDecision(goalEnemy);
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
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            if (currentObjective != CombatObjectiveKind.Suppression &&
                goalEnemy != null &&
                ShouldConsumeSuppressCommand(followerData, goalEnemy))
            {
                if (!combatCommon.CanCurrentWeaponSuppress())
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

        protected virtual FollowerCombatObjectiveBase CreateSuppressionObjective(
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

        protected virtual bool ShouldReturnToPrimaryObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            // Regroup is temporary combat intent: finish it when it completes, combat ends, or
            // an explicit combat order arrives and should hand control back to the tactic's primary stack.
            return HasActivePushOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   regroupObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromSuppressionObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveNeedSniperOrder(followerData) ||
                   suppressionObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        protected virtual bool ShouldReturnFromNeedSniperObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return HasActivePushOrder(followerData) ||
                   HasActiveSuppressOrder(followerData) ||
                   needSniperObjective.IsComplete ||
                   !combatCommon.HasActiveCombatEnemy(goalEnemy);
        }

        private void RefreshObjective(EnemyInfo goalEnemy)
        {
            BotFollowerPlayer? followerData = BossPlayers.Instance?.GetFollower(BotOwner);
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

            if (currentObjective == CombatObjectiveKind.NeedSniper && ShouldReturnFromNeedSniperObjective(followerData, goalEnemy))
            {
                ActivatePrimaryObjective();
            }
        }

        private void ActivateRegroupObjective(BotFollowerPlayer followerData)
        {
            followerData.ClearCommand("CombatObjective:ConsumeRegroup");
            ActivateRegroupObjective();
        }

        private void ActivateRegroupObjective()
        {
            if (currentObjective == CombatObjectiveKind.Regroup)
            {
                return;
            }

            // Activate resets regroup-local state so every new regroup order starts fresh from the
            // follower's current combat geometry instead of reusing stale bossward targets.
            regroupObjective.Activate();
            currentObjective = CombatObjectiveKind.Regroup;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateRegroup");
        }

        private void ActivateSuppressionObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            if (!combatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectSuppression");
                return;
            }

            if (!combatCommon.CanCurrentWeaponSuppress())
            {
                followerData.ClearCommand("CombatObjective:RejectSuppressionWeapon");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            followerData.ClearCommand("CombatObjective:ConsumeSuppression");
            if (currentObjective != CombatObjectiveKind.Suppression)
            {
                suppressionObjective.Activate();
                currentObjective = CombatObjectiveKind.Suppression;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateSuppression");
            }
        }

        private void ActivateNeedSniperObjective(BotFollowerPlayer followerData, EnemyInfo goalEnemy)
        {
            if (ShouldRejectNeedSniperObjective(goalEnemy))
            {
                followerData.ClearCommand("CombatObjective:RejectNeedSniper");
                BotOwner.BotTalk?.TrySay(EPhraseTrigger.Negative, false);
                BotOwner.Gesture?.TryGestus(EInteraction.NoGesture, false);
                return;
            }

            followerData.ClearCommand("CombatObjective:ConsumeNeedSniper");
            if (currentObjective != CombatObjectiveKind.NeedSniper)
            {
                needSniperObjective.Activate();
                currentObjective = CombatObjectiveKind.NeedSniper;
                BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "activateNeedSniper");
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

        private void ActivatePrimaryObjective()
        {
            if (currentObjective == CombatObjectiveKind.Default)
            {
                return;
            }

            regroupObjective.Deactivate();
            suppressionObjective.Deactivate();
            needSniperObjective.Deactivate();
            // Re-enter tactic combat with clean local primary-objective state, but do not call
            // StartDecision() here or the bot would incorrectly get a fresh combat opener.
            GetObjective().Activate();
            currentObjective = CombatObjectiveKind.Default;
            BattleRecorder.RecordObjectiveSwitch(BotOwner, GetCurrentObjectiveName(), "returnPrimary");
        }

        protected FollowerCombatObjectiveBase GetCurrentObjective()
        {
            return currentObjective switch
            {
                CombatObjectiveKind.Regroup => regroupObjective,
                CombatObjectiveKind.Suppression => suppressionObjective,
                CombatObjectiveKind.NeedSniper => needSniperObjective,
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
    }
}
