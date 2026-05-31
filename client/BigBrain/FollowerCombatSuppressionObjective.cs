using EFT;
using System;
using UnityEngine;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerCombatSuppressionObjective : FollowerCombatObjectiveBase
    {
        internal const string ReasonPrefix = "objectiveSuppress";
        private const string WeaponSwitchToPrimaryReason = "objectiveSuppress.weaponSwitchToPrimary";
        private const float WeaponSwitchRetrySeconds = 0.25f;

        private bool complete;
        private bool launcherFallbackToWeapon;
        private float weaponSwitchRetryUntil;

        public FollowerCombatSuppressionObjective(BotOwner botOwner, FollowerCombatCommon combatCommon)
            : base(botOwner, combatCommon)
        {
        }

        public override bool IsComplete => complete;

        public override void Reset()
        {
            complete = false;
            launcherFallbackToWeapon = false;
            weaponSwitchRetryUntil = 0f;
        }

        public override void Activate()
        {
            Reset();
            ClearObjectiveCommitments();
        }

        public override void Deactivate()
        {
            ClearObjectiveCommitments();
            complete = false;
        }

        public override void DecisionChanged(
            AICoreActionResultStruct<BotLogicDecision, GClass26>? prevDecision,
            AICoreActionResultStruct<BotLogicDecision, GClass26> nextDecision)
        {
            CombatCommon.HandleSharedDecisionChanged(nextDecision);
            CombatCommon.HandleFollowerSuppressDecisionChanged(nextDecision);
        }

        public override void StartDecision()
        {
        }

        public override AICoreActionResultStruct<BotLogicDecision, GClass26> GetDecision(EnemyInfo goalEnemy)
        {
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                return Hold("noEnemy");
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

            if (!launcherFallbackToWeapon &&
                CombatCommon.TryCreateGrenadeLauncherSuppressDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> launcherDecision,
                    ordered: true))
            {
                return launcherDecision;
            }

            if (CombatCommon.TryCreateOrderedSuppressWeaponFallbackDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> fallbackDecision))
            {
                if (string.Equals(fallbackDecision.Reason, WeaponSwitchToPrimaryReason, StringComparison.Ordinal))
                {
                    weaponSwitchRetryUntil = Time.time + WeaponSwitchRetrySeconds;
                }

                return fallbackDecision;
            }

            if (CombatCommon.TryCreateSuppressDecision(
                    goalEnemy,
                    ReasonPrefix,
                    out AICoreActionResultStruct<BotLogicDecision, GClass26> decision,
                    allowObstructedSuppression: true))
            {
                return decision;
            }

            complete = true;
            return Hold("noSuppressionDecision");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(
            AICoreActionResultStruct<BotLogicDecision, GClass26> currentDecision)
        {
            if (!IsSuppressionObjectiveReason(currentDecision.Reason))
            {
                return CombatCommon.ShallEndCurrentDecision(currentDecision);
            }

            EnemyInfo? goalEnemy = BotOwner.Memory.GoalEnemy;
            if (!CombatCommon.HasActiveCombatEnemy(goalEnemy))
            {
                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionEnemyMissing", true);
            }

            if (currentDecision.Action == BotLogicDecision.suppressFire)
            {
                AICoreActionEndStruct end = CombatCommon.EndSuppressFire(currentDecision.Reason);
                if (end.Value)
                {
                    if (FollowerCombatCommon.IsGrenadeLauncherSuppressReason(currentDecision.Reason) &&
                        ShouldFallbackLauncherSuppressToWeapon(end.Reason))
                    {
                        launcherFallbackToWeapon = true;
                        CombatCommon.PrepareLauncherSuppressWeaponFallback();
                        return end;
                    }

                    complete = true;
                    ClearObjectiveCommitments();
                }

                return end;
            }

            if (currentDecision.Action == BotLogicDecision.holdPosition)
            {
                if (string.Equals(currentDecision.Reason, WeaponSwitchToPrimaryReason, StringComparison.Ordinal))
                {
                    if (Time.time >= weaponSwitchRetryUntil)
                    {
                        return new AICoreActionEndStruct("suppressionWeaponSwitchRetry", true);
                    }

                    CombatCommon.HoldFor(Mathf.Max(0.05f, weaponSwitchRetryUntil - Time.time));
                    return default;
                }

                complete = true;
                ClearObjectiveCommitments();
                return new AICoreActionEndStruct("suppressionNoAction", true);
            }

            return CombatCommon.ShallEndCurrentDecision(currentDecision);
        }

        internal static bool IsSuppressionObjectiveReason(string? reason)
        {
            return reason != null && reason.StartsWith(ReasonPrefix, StringComparison.Ordinal);
        }

        private static bool ShouldFallbackLauncherSuppressToWeapon(string? endReason)
        {
            return string.Equals(endReason, "followerSuppressHardBlockedLane", StringComparison.Ordinal) ||
                   string.Equals(endReason, "followerSuppressBlockedLane", StringComparison.Ordinal) ||
                   string.Equals(endReason, "launcherImpactUnsafe", StringComparison.Ordinal);
        }

        private void ClearObjectiveCommitments()
        {
            CombatCommon.ClearFollowerSuppressState();
            CombatCommon.ClearCommittedMovement();
            CombatCommon.ClearCommittedPosition();
            CombatCommon.ClearInitialDecision();
        }

        private static AICoreActionResultStruct<BotLogicDecision, GClass26> Hold(string suffix)
        {
            return new AICoreActionResultStruct<BotLogicDecision, GClass26>(
                BotLogicDecision.holdPosition,
                $"{ReasonPrefix}.{suffix}");
        }
    }
}
