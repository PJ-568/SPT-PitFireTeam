using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;

namespace friendlySAIN.BigBrain
{
    internal abstract class FollowerCombatLogicBase
    {
        // High-level combat intent. Each objective owns its own decision stack and end logic.
        protected enum CombatObjectiveKind
        {
            Default,
            Regroup
        }

        protected readonly BotOwner BotOwner;
        protected readonly BotFollower BotFollower;
        protected readonly FollowerCombatCommon combatCommon;
        protected bool errorLogged;
        protected readonly FollowerCombatObjectiveBase defaultObjective;
        protected readonly FollowerCombatObjectiveBase sniperObjective;
        protected readonly FollowerCombatObjectiveBase regroupObjective;
        protected CombatObjectiveKind currentObjective = CombatObjectiveKind.Default;

        protected FollowerCombatLogicBase(BotOwner botOwner)
        {
            BotOwner = botOwner;
            BotFollower = botOwner.BotFollower;
            combatCommon = new FollowerCombatCommon(botOwner);
            defaultObjective = CreateDefaultObjective(botOwner, combatCommon);
            sniperObjective = CreateSniperObjective(botOwner, combatCommon);
            regroupObjective = CreateRegroupObjective(botOwner, combatCommon);
        }

        public bool ShallUseNow() => combatCommon.HasActiveCombatEnemy();

        public virtual void Reset()
        {
            combatCommon.Reset();
            defaultObjective.Reset();
            sniperObjective.Reset();
            regroupObjective.Reset();
            currentObjective = CombatObjectiveKind.Default;
        }

        public virtual AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision()
        {
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

        public virtual void StartDecision()
        {
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

        protected virtual bool ShouldConsumeRegroupCommand(BotFollowerPlayer? followerData)
        {
            // RegroupNearBoss is only a trigger for combat objective selection.
            // Once consumed, combat runs from objective state rather than command polling.
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.RegroupNearBoss;
        }

        protected virtual bool ShouldReturnToPrimaryObjective(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            // Regroup is temporary combat intent: finish it when it completes, combat ends, or
            // an explicit push order arrives and should hand control back to the tactic's primary stack.
            return HasActivePushOrder(followerData) ||
                   regroupObjective.IsComplete ||
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

            if (currentObjective == CombatObjectiveKind.Regroup && ShouldReturnToPrimaryObjective(followerData, goalEnemy))
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
        }

        protected void ActivatePrimaryObjectiveForStart()
        {
            currentObjective = CombatObjectiveKind.Default;
        }

        private void ActivatePrimaryObjective()
        {
            if (currentObjective == CombatObjectiveKind.Default)
            {
                return;
            }

            regroupObjective.Deactivate();
            // Re-enter tactic combat with clean local primary-objective state, but do not call
            // StartDecision() here or the bot would incorrectly get a fresh combat opener.
            GetObjective().Activate();
            currentObjective = CombatObjectiveKind.Default;
        }

        protected FollowerCombatObjectiveBase GetCurrentObjective()
        {
            return currentObjective == CombatObjectiveKind.Regroup
                ? regroupObjective
                : GetObjective();
        }

        protected abstract FollowerCombatObjectiveBase GetObjective();

        private static bool HasActivePushOrder(BotFollowerPlayer? followerData)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.PushEnemy;
        }
    }
}
