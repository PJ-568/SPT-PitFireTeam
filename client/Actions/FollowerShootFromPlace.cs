using EFT;

using StandardBrain2 = GClass28;

namespace friendlySAIN.Actions
{
    internal class FollowerShootFromPlace : GClass269
    {
        public FollowerShootFromPlace(BotOwner bot) : base(bot)
        {
        }

        public override void UpdateNodeByBrain(StandardBrain2 data)
        {
            method_1(data);

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if (botOwner_0.Mover.TargetPose == 0f && botOwner_0.ShootFromPlace.CanShootSit && goalEnemy != null && goalEnemy.IsVisible && goalEnemy.Distance < 40f)
            {
                botOwner_0.Mover.SetPose(0.5f);
            }

            method_0();
        }
    }
}
