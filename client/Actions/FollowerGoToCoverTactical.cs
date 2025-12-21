using EFT;
using UnityEngine;

namespace friendlySAIN.Actions
{
    internal class FollowerGoToCoverTactical : GClass231
    {
        public FollowerGoToCoverTactical(BotOwner bot) : base(bot)
        {
        }

        public override void LookSimple()
        {
            CustomNavigationPoint dest = botOwner_0.Memory.CurCustomCoverPoint;
            if (dest != null)
            {
                Vector3 botPos = botOwner_0.GetPlayer.Transform.position;
                Vector3 corner = botOwner_0.Mover.CurrentCornerPoint;

                if (Utils.Covers.IsPointBetween(corner, botPos, dest.Position))
                {
                    gclass486_0.SetVectorToLook(corner - botOwner_0.Position);
                }
                else
                {
                    gclass486_0.SetVectorToLook(dest.Position - botPos);
                }
            }
            gclass486_0.Update();
        }

    }
}
