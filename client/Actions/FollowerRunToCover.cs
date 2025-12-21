using EFT;

namespace friendlySAIN.Actions
{
    public class FollowerRunToCover : GClass206
    {
        public FollowerRunToCover(BotOwner bot) : base(bot)
        {

        }

        /*public override void Update()
        {
            bool notOpeningDoor = base.method_0();
            this.botOwner_0.BotLight.TurnOff(true, false);
            ShootPointClass shootPointClass = null;
            bool flag;
            if (flag = (this.botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Attack) || this.botOwner_0.Tactic.IsCurTactic(BotsGroup.BotCurrentTactic.Protect)))
            {
                shootPointClass = this.botOwner_0.CurrentEnemyTargetPosition(true);
            }
            if (shootPointClass == null)
            {
                flag = false;
            }
            if (this.botOwner_0.Memory.GoalEnemy != null && this.botOwner_0.Memory.GoalEnemy.CanShoot && this.botOwner_0.Memory.GoalEnemy.Distance < this.botOwner_0.Settings.FileSettings.Mind.DIST_TO_STOP_RUN_ENEMY)
            {
                this.botOwner_0.DangerPointsData.SetAllDangerNull();
            }
            CoverShootType shootType = flag ? CoverShootType.shoot : CoverShootType.hide;

            if (botOwner_0.Memory.CurCustomCoverPoint.IsFreeById(this.botOwner_0.Id))
            {

            }

            this.botOwner_0.BotRun.Run(shootType, shootPointClass, this.botOwner_0.Settings.FileSettings.Cover.CHANGE_RUN_TO_COVER_SEC, notOpeningDoor, this.CanChangeDecision, this.UseZigZag);
        }*/
    }
}
