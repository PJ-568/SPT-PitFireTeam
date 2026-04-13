using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatAttackMovingActionBackup : FollowerCombatActionBase
    {
        private readonly FollowerAttackMovingLogic baseLogic;

        public CombatAttackMovingActionBackup(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new FollowerAttackMovingLogic(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.ForceThreatFacing = ShouldForceThreatFacing(GetReason(data));
            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }

        private sealed class FollowerAttackMovingLogic : GClass205
        {
            public bool ForceThreatFacing { get; set; }

            public FollowerAttackMovingLogic(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void AimingAndShoot(GClass26 data)
            {
                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
                {
                    if (BotOwner_0.WeaponManager.UnderbarrelLauncherController.CanSwitchInFight(BotOwner_0))
                    {
                        BotOwner_0.WeaponManager.UnderbarrelLauncherController.TryEnable(null);
                    }

                    Gclass178_0.UpdateNodeByBrain(data as GClass27);
                    return;
                }

                CombatAttackMoveLook.TryLookThreatFacing(BotOwner_0, goalEnemy, ForceThreatFacing);
            }
        }

        private static bool ShouldForceThreatFacing(string? reason)
        {
            return reason != null &&
                   (reason.StartsWith("sniper.", StringComparison.Ordinal) ||
                    reason.StartsWith("regroup.withdraw", StringComparison.Ordinal));
        }
    }
}
