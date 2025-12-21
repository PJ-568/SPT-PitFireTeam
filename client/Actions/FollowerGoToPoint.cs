using EFT;
using UnityEngine;

namespace friendlySAIN.Actions
{
    /**
     * Overwrite of goToPoint decision to include sprinting
     */
    public class FollowerGoToPoint : GClass212
    {
        private bool _shouldSprint = true;

        public FollowerGoToPoint(BotOwner bot) : base(bot)
        {

            Vector3 point = botOwner_0.GoToSomePointData.Point;
            if (point != null)
                _shouldSprint = !Utils.Utils.IsWithinDistance(point, botOwner_0.GetPlayer.Transform.position, 15f);
        }

        public override void UpdateNodeByBrain(GClass30 data)
        {
            base.UpdateNodeByBrain(data);
            botOwner_0.GoToSomePointData.UpdateToGo(_shouldSprint);
        }
    }
}