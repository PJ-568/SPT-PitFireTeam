using EFT;
using friendlySAIN.Components;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerSniperCombatLogic : FollowerCombatLogicBase
    {
        public FollowerSniperCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void StartDecision()
        {
            currentObjective = CombatObjectiveKind.Default;
            sniperObjective.StartDecision();
        }

        protected override FollowerCombatObjectiveBase GetObjective()
        {
            return sniperObjective;
        }

        protected override bool ShouldConsumeNeedSniperCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.NeedSniper;
        }
    }
}
