using EFT;

using System;
using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Brains;

namespace friendlySAIN.Actions
{
    /**
     * Action to check the direction boss reported seeing an enemy
     */
    public class FollowerEnemyCheck
    {
        public static void CheckBossReport(BotOwner bot)
        {

            Player closest = InteractableObjects.GetClosestSeenEnemy();

            if (bot.BotState == EBotState.Active && !bot.IsDead)
            {
                //look in the direction he was looking
                Vector3 bossPosition = bot.BotFollower.BossToFollow.Player().Transform.position;
                Vector3 bossLookDirection = bot.BotFollower.BossToFollow.Player().LookDirection;

                FollowerBrain brain = bot.Brain.BaseBrain as FollowerBrain;
                if (brain != null && bot.BotFollower.HaveBoss && !bot.Memory.HaveEnemy)
                {
                    brain.FakeShot(bossPosition + bossLookDirection.normalized * 50f);
                }

                // make the closest reported enemy an active enemy
                if (closest != null)
                {
                    Modules.Logger.LogInfo("Player has seen " + closest.Profile.Nickname);
                    try
                    {
                        if (bot.Memory.HaveEnemy && bot.Memory.GoalEnemy.ProfileId == closest.Profile.ProfileId) return;

                        EnemyInfo info = Utils.Enemy.MakeEnemy(bot, closest);

                        if (info != null && !bot.Memory.HaveEnemy && !bot.Medecine.FirstAid.Using && !bot.Medecine.SurgicalKit.Using)
                        {
                            info.PriorityIndex = 0;
                            info.SetVisible(true);
                            bot.Memory.GoalEnemy = info;

                            Modules.Logger.LogInfo("Made " + closest.Profile.Nickname + " an active enemy to " + bot.Profile.Nickname);
                        }
                        else if (info == null)
                        {
                            Modules.Logger.LogInfo("Cannot make " + bot.Profile.Nickname + " an active enemy");
                        }
                    }
                    catch (Exception e)
                    {
                        Modules.Logger.LogInfo("Failed to accquire reported enemy:");
                        Modules.Logger.LogInfo(e.StackTrace);
                    }
                }
            }

            bot.CalcGoal();
        }
    }
}
