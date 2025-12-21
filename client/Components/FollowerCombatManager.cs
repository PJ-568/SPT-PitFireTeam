using EFT;
using friendlySAIN.Modules;
using System.Collections.Generic;
using System.Linq;

namespace friendlySAIN.Components
{
    internal class FollowerCombatManager
    {

        private pitAIBossPlayer boss;

        public FollowerCombatManager(pitAIBossPlayer boss)
        {
            this.boss = boss;
        }

        public void FollowerAdded()
        {

        }

        public void FollowerRemoved()
        {
        }

        public void ManualUpdate()
        {
            bool inCombat = false;

            Dictionary<EnemyInfo, List<BotFollowerPlayer>> squads = new Dictionary<EnemyInfo, List<BotFollowerPlayer>>();

            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowersByBoss(boss.realPlayer.ProfileId))
            {
                BotOwner owner = follower.GetBot();
                if (owner.Memory.HaveEnemy)
                {
                    inCombat = true;
                    EnemyInfo enemy = owner.Memory.GoalEnemy;
                    EnemyInfo key = squads.Keys.FirstOrDefault(x => x.ProfileId == enemy.ProfileId);
                    if (key == null)
                    {
                        key = enemy;
                        squads[key] = new List<BotFollowerPlayer>();
                    }
                    squads[key].Add(follower);
                }
            }

            if (!inCombat) return;

        }
    }
}
