using EFT;
using UnityEngine;

namespace friendlySAIN.Actions
{
    internal class FollowerGoToPointTactical : GClass232
    {
        public FollowerGoToPointTactical(BotOwner bot) : base(bot)
        {
        }

        public override void LookSimple()
        {
            Vector3 dest = MainTargetPosition;
            if (dest != null)
            {
                Vector3 botPos = botOwner_0.GetPlayer.Transform.position;
                Vector3 corner = botOwner_0.Mover.CurrentCornerPoint;

                if (Utils.Covers.IsPointBetween(corner, botPos, dest))
                {
                    gclass486_0.SetVectorToLook(corner - botOwner_0.Position);
                }
                else
                {
                    gclass486_0.SetVectorToLook(dest - botPos);
                }
            }
            gclass486_0.Update();
        }
    }
}
