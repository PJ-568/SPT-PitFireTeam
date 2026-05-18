using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Dedicated regroup run toward the boss or a bossward sampled point. It keeps a cached target,
    /// refreshes it at controlled intervals, and uses sprint/path checks so regroup does not become
    /// repeated cover hopping.
    /// </summary>
    internal sealed class CombatRegroupRunAction : FollowerCombatActionBase
    {
        private const float RegroupRunSpreadMinRadius = 1f;
        private const float RegroupRunSpreadMaxRadius = 6f;
        private const float RegroupRunSameLevelTolerance = 1.75f;
        private const float RegroupRunSpacing = 2f;
        private const float RegroupRunClaimTtl = 2f;
        private readonly GClass219 baseLogic;
        private GClass30? cachedData;
        private Vector3 cachedPoint;
        private Vector3 bossAnchor;
        private bool hasCachedPoint;
        private bool hasBossAnchor;

        public CombatRegroupRunAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass219(botOwner);
        }

        public override void Start()
        {
            base.Start();

            Vector3 bossPosition = GetBossPosition();
            SetCachedPoint(GetBossRunTarget(bossPosition), bossPosition);
        }

        public override void Stop()
        {
            ReleaseDestinationClaim();
            cachedData = null;
            cachedPoint = Vector3.zero;
            bossAnchor = Vector3.zero;
            hasCachedPoint = false;
            hasBossAnchor = false;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            Vector3 bossPosition = GetBossPosition();
            if (!hasCachedPoint || ShouldRefreshCachedPoint(bossPosition))
            {
                SetCachedPoint(GetBossRunTarget(bossPosition), bossPosition);
            }
            else
            {
                UpsertDestinationClaim(cachedPoint);
            }

            baseLogic.UpdateNodeByBrain(cachedData);

            // Combat regroup is an urgent converge objective. Keep the vanilla go-to-point
            // movement brain, but force running so regroup does not degrade into a walk.
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.GoToSomePointData.UpdateToGo(true, 1, 1f);
            if (!BotOwner.Mover.Sprinting)
            {
                SetCombatSprint(true);
            }
        }

        private Vector3 GetBossPosition()
        {
            return FollowerCombatAnchor.GetAnchorPosition(BotOwner);
        }

        private void SetCachedPoint(Vector3 point, Vector3 bossPosition)
        {
            cachedPoint = point;
            bossAnchor = bossPosition;
            hasCachedPoint = true;
            hasBossAnchor = true;
            cachedData = new GClass30(cachedPoint)
            {
                Used = true
            };

            BotOwner.GoToSomePointData.SetPoint(cachedPoint);
        }

        private bool ShouldRefreshCachedPoint(Vector3 bossPosition)
        {
            if (!hasBossAnchor)
            {
                return true;
            }

            float refreshDistance = CombatDistanceConfiguration.Instance.GetRegroupBossMoveRefreshDistance();
            return (bossPosition - bossAnchor).sqrMagnitude > refreshDistance * refreshDistance;
        }

        private Vector3 GetBossRunTarget(Vector3 bossPosition)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents) &&
                combatEvents.TryFindBossSpreadDestination(
                    BotOwner,
                    bossPosition,
                    RegroupRunSpreadMinRadius,
                    RegroupRunSpreadMaxRadius,
                    RegroupRunSameLevelTolerance,
                    RegroupRunSpacing,
                    out Vector3 spreadTarget))
            {
                UpsertDestinationClaim(spreadTarget);
                return spreadTarget;
            }

            if (NavMesh.SamplePosition(bossPosition, out NavMeshHit bossHit, 2f, -1))
            {
                Vector3 target = bossHit.position;
                UpsertDestinationClaim(target);
                return target;
            }

            UpsertDestinationClaim(bossPosition);
            return bossPosition;
        }

        private void UpsertDestinationClaim(Vector3 target)
        {
            if (TryGetBossCombatEvents(out CombatEvents? combatEvents))
            {
                combatEvents.UpsertDestinationClaim(BotOwner, target, RegroupRunClaimTtl);
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

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) &&
                   !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) &&
                   !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) &&
                   !float.IsInfinity(value.z);
        }
    }
}
