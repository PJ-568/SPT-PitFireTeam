using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatShootFromPlaceAction : FollowerCombatActionBase
    {
        private const float MinEnemyDistanceForProne = 35f;
        private readonly GClass276 baseLogic;
        private float aimAlignStartedAt;
        private string? aimAlignEnemyId;

        public CombatShootFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass276(botOwner);
        }

        public override void Stop()
        {
            StopCombatShooting();
            aimAlignStartedAt = 0f;
            aimAlignEnemyId = null;
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

            string? reason = BotOwner.Brain?.Agent?.LastResult().Reason;
            if (string.Equals(reason, "visibleImmediateShoot", System.StringComparison.Ordinal) &&
                (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true || BotOwner.Mover.TargetPose < 0.85f))
            {
                BotOwner.SetPose(1f);
            }

            if (WaitForEnemyAimAlignment(ref aimAlignStartedAt, ref aimAlignEnemyId))
            {
                return;
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }
}
