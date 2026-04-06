using DrakiaXYZ.BigBrain.Brains;
using EFT;
using EFT.HealthSystem;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain
{
    public class FollowCombatUtils
    {


        private BotOwner botOwner;

        public BotOwner Bot => botOwner;

        public FollowCombatUtils(BotOwner owner)
        {
            botOwner = owner;
        }

        public bool TryGetBossPosition(out Vector3 position)
        {
            position = default;
            if (botOwner.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }


            position = boss.realPlayer.Transform.position;
            return true;
        }

        public bool TryGetEnemy(out EnemyInfo enemyInfo)
        {

            enemyInfo = null;

            if (botOwner.Memory.HaveEnemy)
            {
                enemyInfo = botOwner.Memory.GoalEnemy;
                return true;
            }

            return false;
        }

        public BotLogicDecision HoldOrCover(BotOwner owner)
        {
            return owner.Memory.IsInCover ? BotLogicDecision.holdPosition : BotLogicDecision.goToCoverPoint;
        }
    }
}