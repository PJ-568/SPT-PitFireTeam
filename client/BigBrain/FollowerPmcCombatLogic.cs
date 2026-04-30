using EFT;
using pitTeam.Components;
using pitTeam.Modules;
using System;

namespace pitTeam.BigBrain
{
    internal sealed class FollowerPmcCombatLogic : FollowerCombatLogicBase
    {
        public FollowerPmcCombatLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void StartDecision()
        {
            currentObjective = CombatObjectiveKind.Default;
            defaultObjective.StartDecision();
        }

        protected override FollowerCombatObjectiveBase GetObjective()
        {
            return defaultObjective;
        }

        protected override bool ShouldConsumeSuppressCommand(BotFollowerPlayer? followerData, EnemyInfo goalEnemy)
        {
            return followerData != null &&
                   followerData.TryGetActiveCommand(out FollowerCommandType command, out _) &&
                   command == FollowerCommandType.SuppressEnemy;
        }
    }
}
