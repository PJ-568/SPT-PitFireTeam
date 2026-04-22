using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private const float MinEnemyDistanceForProne = 35f;
        private const float SameSpotMaxDistanceSqr = 0.75f * 0.75f;
        private readonly GClass276 baseLogic;
        private float aimAlignStartedAt;
        private Vector3 startPosition;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Start()
        {
            base.Start();
            startPosition = BotOwner.Position;
        }

        public override void Stop()
        {
            StopCombatShooting();
            aimAlignStartedAt = 0f;
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo? goalEnemy = BotOwner.Memory?.GoalEnemy;
            bool allowProne = goalEnemy == null || goalEnemy.Distance >= MinEnemyDistanceForProne;
            baseLogic.CanLay = allowProne;

            if (!allowProne && BotOwner.BotLay.IsLay)
            {
                BotOwner.BotLay.GetUp(false);
            }

            string? reason = GetReason(data) ?? BotOwner.Brain?.Agent?.LastResult().Reason;
            if (string.Equals(reason, "visibleImmediateShoot", System.StringComparison.Ordinal) &&
                (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true || BotOwner.Mover.TargetPose < 0.85f))
            {
                BotOwner.SetPose(1f);
            }

            if (TryUpdateImmediateLostVisualSuppress(reason, goalEnemy))
            {
                return;
            }

            if (WaitForEnemyAimAlignment(ref aimAlignStartedAt))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }

        private bool TryUpdateImmediateLostVisualSuppress(string? reason, EnemyInfo? goalEnemy)
        {
            if (!FollowerImmediateFirePolicy.IsImmediateShootReason(reason) ||
                goalEnemy == null ||
                !FollowerImmediateFirePolicy.CanUseLostVisualSuppress(goalEnemy))
            {
                return false;
            }

            if ((BotOwner.Position - startPosition).sqrMagnitude > SameSpotMaxDistanceSqr)
            {
                StopCombatShooting();
                return true;
            }

            Vector3 target = FollowerImmediateFirePolicy.GetLostVisualSuppressTarget(goalEnemy);
            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            if (FollowerShotSafety.IsFriendlyInShotLane(BotOwner, fireOrigin, target))
            {
                BotOwner.Steering.LookToPoint(target);
                StopCombatShooting();
                return true;
            }

            BotOwner.StopMove();
            BotOwner.SetPose(1f);
            BotOwner.Steering.LookToPoint(target);
            BotOwner.ShootData.Shoot();
            return true;
        }

    }
}
