using EFT;
using UnityEngine;

using friendlySAIN.Brains;

namespace friendlySAIN.Actions
{
    /**
     * Overwrite the holdPosition action for the follower to make him look to the direction of the enemy if he has one.
     */
    public class FollowerHoldPosition : GClass271
    {
        private float timer = 0f;
        public FollowerHoldPosition(BotOwner bot) : base(bot)
        {
        }

        public override void Look()
        {
            if (botOwner_0.Memory.HaveEnemy && !botOwner_0.Memory.IsInCover && botOwner_0.Memory.GoalEnemy.CurrPosition != null)
            {
                botOwner_0.Steering.LookToDirection(botOwner_0.Memory.GoalEnemy.CurrPosition - botOwner_0.GetPlayer.Transform.position);
            }

            FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            if (!botOwner_0.Memory.HaveEnemy && timer < Time.time && (brain == null || (!brain.UnderFire && !brain.WasHit)))
            {
                timer = Time.time + Utils.Utils.Random(3f, 6f);
                botOwner_0.LookData.SetLookPointByHearing(null);
            }
        }
    }
}
