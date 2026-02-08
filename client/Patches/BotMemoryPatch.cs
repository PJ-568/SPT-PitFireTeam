using EFT;

using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using friendlySAIN.Modules;

namespace friendlySAIN.Patches
{
    /**
     * Patch to yell friendly fire from teamates
     */
    internal class BotMemoryDamagePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotMemoryClass), "method_8");
        }
        [PatchPrefix]
        private static void PatchPrefix(BotMemoryClass __instance, DamageInfoStruct damageInfo)
        {
            try
            {
                var botOwner_0 = AccessTools.Field(typeof(BotMemoryClass), "BotOwner_0").GetValue(__instance) as BotOwner;

                if (damageInfo.Player == null) return;

                bool isfollower = BossPlayers.IsFollower(botOwner_0);
                if (!isfollower) return;

                bool isBossEnemy = BossPlayers.IsPlayerBoss(damageInfo.Player.iPlayer.ProfileId);

                bool isTeamate = false;

                if (botOwner_0.BotFollower.BossToFollow == null) return;

                botOwner_0.BotFollower.BossToFollow.Followers.ForEach(bt =>
                {
                    if (bt.ProfileId == damageInfo.Player.iPlayer.ProfileId) isTeamate = true;
                });

                if (!(isBossEnemy || isTeamate)) return;

                botOwner_0.BotTalk.TrySay(EPhraseTrigger.FriendlyFire, false);
            }
            catch (System.Exception e)
            {
                Modules.Logger.LogError(e);
            }
        }
    }
}
