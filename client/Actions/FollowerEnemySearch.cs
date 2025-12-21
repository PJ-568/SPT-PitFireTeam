using EFT;
using UnityEngine;

namespace friendlySAIN.Actions
{
    internal class FollowerEnemySearch : GClass228
    {
        public FollowerEnemySearch(BotOwner bot) : base(bot)
        {
        }
        public override void UpdateNodeByBrain(GClass26 data)
        {
            base.UpdateNodeByBrain(data);
            LookSimple();
        }

        public void LookSimple()
        {
            Vector3 dest = botOwner_0.Memory.HaveEnemy ? botOwner_0.Memory.GoalEnemy.CurrPosition : botOwner_0.Position;
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
