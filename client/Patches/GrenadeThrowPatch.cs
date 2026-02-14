using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Patches
{
    
    // Guard vanilla straight-contact grenade logic from null refs (seen in GClass274.UpdateTryThrow).
    internal class GrenadeThrowPatch : ModulePatch
    {
        private static readonly FieldInfo BotOwnerField = AccessTools.Field(typeof(GClass274), "BotOwner_0");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass274), "UpdateTryThrow");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass274 __instance, ref bool __result)
        {
            try
            {
                BotOwner bot = BotOwnerField?.GetValue(__instance) as BotOwner;
                if (bot == null)
                {
                    return true;
                }

                BotGrenadeController grenades = bot.WeaponManager?.Grenades;
                if (grenades == null || !grenades.HaveGrenade)
                {
                    __result = false;
                    return false;
                }

                if (!bot.Settings.FileSettings.Grenade.CAN_THROW_STRAIGHT_CONTACT)
                {
                    __result = false;
                    return false;
                }

                bool hasGoalEnemy = bot.Memory?.GoalEnemy != null;
                if (hasGoalEnemy && Time.time - bot.Memory.EnemySetTime < bot.Settings.FileSettings.Grenade.STRAIGHT_CONTACT_DELTA_SEC)
                {
                    __result = false;
                    return false;
                }

                if (grenades.ReadyToThrow)
                {
                    if (grenades.AIGreanageThrowData == null || !grenades.AIGreanageThrowData.IsUpToDate())
                    {
                        __result = false;
                        return false;
                    }

                    if (bot.Settings.FileSettings.Grenade.STOP_WHEN_THROW_GRENADE)
                    {
                        bot.StopMove();
                    }
                    bot.WeaponManager.Grenades.DoThrow();
                    __result = true;
                    return false;
                }

                if (hasGoalEnemy)
                {
                    IPlayer enemyPlayer = bot.Memory.GoalEnemy?.Person;
                    if (enemyPlayer == null)
                    {
                        __result = false;
                        return false;
                    }

                    Player liveEnemy = Singleton<GameWorld>.Instance?.GetAlivePlayerByProfileID(enemyPlayer.ProfileId);
                    if (liveEnemy == null)
                    {
                        __result = false;
                        return false;
                    }

                    if (liveEnemy.IsSprintEnabled)
                    {
                        __result = false;
                        return false;
                    }

                    if (Time.time - bot.Memory.GoalEnemy.FirstTimeSeen > bot.Settings.FileSettings.Grenade.FIRST_TIME_SEEN_DELTA_CAN_THROW)
                    {
                        grenades.CanThrowGrenade(bot.Memory.GoalEnemy.CurrPosition + Vector3.up);
                    }
                }

                __result = false;
                return false;
            }
            catch (Exception)
            {
                __result = false;
                return false;
            }
        }
    }
}
