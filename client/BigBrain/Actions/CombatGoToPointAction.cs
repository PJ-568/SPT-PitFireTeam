using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToPointAction : FollowerCombatActionBase
    {
        private readonly GClass219 baseLogic;

        public CombatGoToPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass219(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            if (ShouldRunToPoint(data))
            {
                BotOwner.SetTargetMoveSpeed(1f);
                SetCombatSprint(true);
                BotOwner.SetPose(1f);
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass30>(data));
        }

        private static bool ShouldRunToPoint(CustomLayer.ActionData data)
        {
            string? reason = GetReason(data);
            return reason != null &&
                   reason.IndexOf(".runToPoint", StringComparison.Ordinal) >= 0;
        }
    }
}
