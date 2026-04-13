using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatRegroupRunAction : FollowerCombatActionBase
    {
        private readonly GClass219 baseLogic;

        public CombatRegroupRunAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass219(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            // Combat regroup is an urgent converge objective. Keep the vanilla go-to-point
            // movement brain, but force running so regroup does not degrade into a walk.
            BotOwner.SetTargetMoveSpeed(1f);
            BotOwner.GoToSomePointData.UpdateToGo(true, 1, 1f);
            if (!BotOwner.Mover.Sprinting)
            {
                SetCombatSprint(true);
            }

            baseLogic.UpdateNodeByBrain(GetData<GClass30>(data));
        }
    }
}
