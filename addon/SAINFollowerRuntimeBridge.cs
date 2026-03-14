using System.Reflection;
using System.Collections.Generic;
using EFT;
using friendlySAIN.Modules;
using SAIN;
using SAIN.Components;
using SAIN.Models.Enums;
using SAIN.SAINComponent.Classes.EnemyClasses;
using UnityEngine;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerRuntimeBridge
    {
        private const float SoloCombatReleaseGraceSeconds = 1.5f;
        private const float StaleSearchReleaseSeconds = 3f;
        private const float StaleSeekCoverReleaseSeconds = 3f;
        private const float StaleRetreatReleaseSeconds = 4f;
        private const float StaleShiftCoverReleaseSeconds = 3.5f;
        private const float StaleSoloLayerNoDecisionReleaseSeconds = 2.5f;
        private const float CooldownCrouchPose = 0.1f;
        private const float CooldownCrouchSetIntervalSeconds = 0.3f;
        private static readonly Dictionary<string, float> LastSoloCombatSeenAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> StaleDecisionStartedAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, ECombatDecision> StaleDecisionTypeByBot = new Dictionary<string, ECombatDecision>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> SoloLayerNoDecisionStartedAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly Dictionary<string, float> NextCooldownCrouchSetAtByBot = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private static readonly FieldInfo TimeLastKnownUpdatedField = typeof(SAIN.SAINComponent.Classes.EnemyClasses.EnemyKnownPlaces)
            .GetField("<TimeLastKnownUpdated>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

        // Core plugin calls this bridge when SAIN is installed so SAIN-specific runtime gating stays addon-owned.
        public static bool IsReadyForPatrolAfterCombat(BotOwner owner)
        {
            if (owner == null || owner.IsDead || owner.BotState != EBotState.Active)
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            string profileId = owner.ProfileId;
            bool botInCombat = bot.BotActivation.BotInCombat;
            bool soloCombatLayerActive = bot.ActiveLayer == ESAINLayer.Combat;
            var decisions = bot.Decision;
            int knownEnemyCount = bot.EnemyController?.KnownEnemies?.Count ?? 0;
            ECombatDecision combatDecision = decisions?.CurrentCombatDecision ?? ECombatDecision.None;
            ESelfActionType selfDecision = decisions?.CurrentSelfDecision ?? ESelfActionType.None;
            bool soloSelfActionSeekCover = decisions != null
                && combatDecision == ECombatDecision.SeekCover
                && selfDecision != ESelfActionType.None;

            bool hasNoEnemyContext = owner.Memory?.HaveEnemy != true && knownEnemyCount == 0;
            bool staleDecisionCandidate = TryGetStaleReleaseTimeout(combatDecision, selfDecision, hasNoEnemyContext, out float staleTimeoutSeconds);
            bool staleSoloLayerNoDecisionCandidate =
                soloCombatLayerActive &&
                combatDecision == ECombatDecision.None &&
                selfDecision == ESelfActionType.None &&
                hasNoEnemyContext;

            if (staleDecisionCandidate)
            {
                bool hasTimer = StaleDecisionStartedAtByBot.TryGetValue(profileId, out float startedAt);
                bool sameDecision = StaleDecisionTypeByBot.TryGetValue(profileId, out ECombatDecision trackedDecision) && trackedDecision == combatDecision;
                if (!hasTimer || !sameDecision)
                {
                    StaleDecisionStartedAtByBot[profileId] = Time.time;
                    StaleDecisionTypeByBot[profileId] = combatDecision;
                }
                else if (Time.time - startedAt >= staleTimeoutSeconds)
                {
                    Modules.Logger.LogInfo(
                        $"[SAIN] Stale {combatDecision} release for follower={owner.Profile?.Nickname ?? owner.name}[{profileId}]");
                    ForceReleaseFollowerCombatState(owner);
                    StaleDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionTypeByBot.Remove(profileId);
                    SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
                    LastSoloCombatSeenAtByBot.Remove(profileId);
                    NextCooldownCrouchSetAtByBot.Remove(profileId);
                    return true;
                }
            }
            else
            {
                StaleDecisionStartedAtByBot.Remove(profileId);
                StaleDecisionTypeByBot.Remove(profileId);
            }

            if (staleSoloLayerNoDecisionCandidate)
            {
                if (!SoloLayerNoDecisionStartedAtByBot.TryGetValue(profileId, out float startedAt))
                {
                    SoloLayerNoDecisionStartedAtByBot[profileId] = Time.time;
                }
                else if (Time.time - startedAt >= StaleSoloLayerNoDecisionReleaseSeconds)
                {
                    Modules.Logger.LogInfo(
                        $"[SAIN] Stale solo-layer release for follower={owner.Profile?.Nickname ?? owner.name}[{profileId}] " +
                        $"combat={combatDecision} self={selfDecision} knownEnemies={knownEnemyCount}");
                    ForceReleaseFollowerCombatState(owner);
                    SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionStartedAtByBot.Remove(profileId);
                    StaleDecisionTypeByBot.Remove(profileId);
                    LastSoloCombatSeenAtByBot.Remove(profileId);
                    NextCooldownCrouchSetAtByBot.Remove(profileId);
                    return true;
                }
            }
            else
            {
                SoloLayerNoDecisionStartedAtByBot.Remove(profileId);
            }

            if (botInCombat || soloCombatLayerActive || soloSelfActionSeekCover)
            {
                LastSoloCombatSeenAtByBot[profileId] = Time.time;
            }

            if (soloCombatLayerActive || soloSelfActionSeekCover || staleDecisionCandidate)
            {
                TryApplyCooldownCrouch(owner, bot, profileId);
                return false;
            }

            if (LastSoloCombatSeenAtByBot.TryGetValue(profileId, out float lastSeenAt))
            {
                if (Time.time - lastSeenAt < SoloCombatReleaseGraceSeconds)
                {
                    TryApplyCooldownCrouch(owner, bot, profileId);
                    return false;
                }

                LastSoloCombatSeenAtByBot.Remove(profileId);
            }

            return true;
        }

        private static bool TryGetStaleReleaseTimeout(
            ECombatDecision combatDecision,
            ESelfActionType selfDecision,
            bool hasNoEnemyContext,
            out float timeoutSeconds)
        {
            timeoutSeconds = 0f;
            if (!hasNoEnemyContext)
            {
                return false;
            }

            switch (combatDecision)
            {
                case ECombatDecision.Search:
                    timeoutSeconds = StaleSearchReleaseSeconds;
                    return true;
                case ECombatDecision.SeekCover:
                    if (selfDecision != ESelfActionType.None)
                    {
                        return false;
                    }
                    timeoutSeconds = StaleSeekCoverReleaseSeconds;
                    return true;
                case ECombatDecision.Retreat:
                    timeoutSeconds = StaleRetreatReleaseSeconds;
                    return true;
                case ECombatDecision.ShiftCover:
                    timeoutSeconds = StaleShiftCoverReleaseSeconds;
                    return true;
                default:
                    return false;
            }
        }

        private static void TryApplyCooldownCrouch(BotOwner owner, BotComponent bot, string profileId)
        {
            if (owner == null || bot == null || string.IsNullOrEmpty(profileId))
            {
                return;
            }

            if (NextCooldownCrouchSetAtByBot.TryGetValue(profileId, out float nextAt) && Time.time < nextAt)
            {
                return;
            }

            NextCooldownCrouchSetAtByBot[profileId] = Time.time + CooldownCrouchSetIntervalSeconds;

            float currentPose = owner.GetPlayer?.MovementContext?.PoseLevel ?? 1f;
            if (currentPose <= 0.2f)
            {
                return;
            }

            try
            {
                bot.Mover?.SetTargetPose(CooldownCrouchPose);
                owner.Mover?.SetPose(CooldownCrouchPose);
            }
            catch
            {
                // Keep readiness checks resilient; crouch nudge is best-effort only.
            }
        }

        public static void ForceReleaseFollowerCombatState(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            try
            {
                if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
                {
                    return;
                }

                ClearFollowerSearchState(bot);
                ExpireKnownEnemyTimers(bot);

                var enemyController = bot.EnemyController;
                if (enemyController != null)
                {
                    enemyController.ClearEnemy();
                }

                var decisions = bot.Decision;
                if (decisions != null)
                {
                    decisions.ResetDecisions(false);
                }

                // Hard-release SAIN layer ownership so attention/reset cannot leave a bot in
                // combat-layer control with no active combat decision.
                bot.ActiveLayer = ESAINLayer.None;
                bot.BotActivation?.SetCurrentAction(null);

                if (owner.BotRequestController?.CurRequest != null)
                {
                    owner.BotRequestController.CurRequest.Complete();
                    owner.BotRequestController.CurRequest = null;
                }

                owner.StopMove();
                owner.GoToSomePointData?.SetPoint(owner.Position);
                owner.GoToSomePointData?.UpdateToGo(false);

                if (owner.Mover != null)
                {
                    owner.Mover.Pause = false;
                    if (owner.Mover.Sprinting)
                    {
                        owner.Mover.Sprint(false, false);
                    }
                    owner.Mover.Stop();
                }
            }
            catch
            {
                // Keep release resilient in raid even if movement internals throw.
            }
        }

        private static void ExpireKnownEnemyTimers(BotComponent bot)
        {
            if (bot?.EnemyController?.EnemiesArray == null || TimeLastKnownUpdatedField == null)
            {
                return;
            }

            float forgetEnemyTime = Mathf.Max(bot.Info?.ForgetEnemyTime ?? 0f, 0.1f);
            float expiredAt = Time.time - forgetEnemyTime - 0.01f;

            foreach (var enemy in bot.EnemyController.EnemiesArray)
            {
                var knownPlaces = enemy?.KnownPlaces;
                if (knownPlaces?.LastKnownPlace == null)
                {
                    continue;
                }

                TimeLastKnownUpdatedField.SetValue(knownPlaces, expiredAt);
            }
        }

        public static bool TrySyncFollowerEnemyState(BotOwner owner, Player enemyPlayer)
        {
            if (owner == null || enemyPlayer == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            var enemyController = bot.EnemyController;
            if (enemyController == null)
            {
                return false;
            }

            var sainEnemy = enemyController.CheckAddEnemy(enemyPlayer);
            if (sainEnemy != null)
            {
                sainEnemy.UpdateLastSeenPosition(enemyPlayer.Position, Time.time);
            }

            enemyController.ChooseEnemy();
            return true;
        }

        public static bool TryResetFollowerDecisionState(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!SAINEnableClass.GetSAIN(owner.ProfileId, out BotComponent bot) || bot == null)
            {
                return false;
            }

            ClearFollowerSearchState(bot);
            var decisions = bot.Decision;
            if (decisions == null)
            {
                return false;
            }

            decisions.ResetDecisions(false);
            return true;
        }

        private static void ClearFollowerSearchState(BotComponent bot)
        {
            if (bot == null)
            {
                return;
            }

            try
            {
                var search = bot.Search;
                if (search != null)
                {
                    search.ToggleSearch(false, null);
                    search.Reset();
                }
            }
            catch
            {
                // Keep release resilient if SAIN search internals change.
            }

            try
            {
                var enemyController = bot.EnemyController;
                if (enemyController?.EnemiesArray == null)
                {
                    return;
                }

                foreach (var enemy in enemyController.EnemiesArray)
                {
                    enemy?.KnownPlaces?.OnEnemyKnownChanged(false, enemy);
                }
            }
            catch
            {
                // Best-effort known-place cleanup only.
            }
        }

        public static string GetFollowerDebugState(BotOwner owner)
        {
            if (owner == null)
            {
                return "owner=null";
            }

            string profileId = owner.ProfileId ?? "<null>";
            bool layerActive = SAINFollowerCombatLayer.IsFollowerCombatLayerActive(owner);
            bool sainFound = SAINEnableClass.GetSAIN(profileId, out BotComponent bot) && bot != null;
            if (!sainFound)
            {
                return $"sainBot=missing layerActive={layerActive}";
            }

            var decision = bot.Decision;
            var enemyController = bot.EnemyController;
            Enemy goalEnemy = bot.GoalEnemy;
            string combatDecision = decision?.CurrentCombatDecision.ToString() ?? "<null>";
            string squadDecision = decision?.CurrentSquadDecision.ToString() ?? "<null>";
            string selfDecision = decision?.CurrentSelfDecision.ToString() ?? "<null>";
            int knownEnemies = enemyController?.KnownEnemies?.Count ?? -1;
            int enemiesArray = enemyController?.EnemiesArray?.Count ?? -1;
            bool hasGoalEnemy = goalEnemy != null;
            bool goalVisible = goalEnemy?.IsVisible == true;
            float goalSeenAgo = hasGoalEnemy ? goalEnemy.TimeSinceSeen : -1f;

            return
                $"layerActive={layerActive} combat={combatDecision} squad={squadDecision} self={selfDecision} " +
                $"knownEnemies={knownEnemies} enemiesArray={enemiesArray} " +
                $"goalEnemy={hasGoalEnemy} goalVisible={goalVisible} goalSeenAgo={goalSeenAgo:0.00}";
        }
    }
}
