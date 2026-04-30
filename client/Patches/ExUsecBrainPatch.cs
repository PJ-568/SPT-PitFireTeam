using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace pitTeam.Patches
{
    /**
     * Patch for exUsec brain to not add player as an enemy because he just killed an Usec (Goons dependent)
     */
    internal class ExUsecBrainHitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ExUsecBrainClass), "method_17");

        }
        [PatchPrefix]
        private static bool PatchPrefix(ExUsecBrainClass __instance, DamageInfoStruct damageinfo, Player victim)
        {
            IPlayerOwner player = damageinfo.Player;
            // this is original condition + does player have knight quest
            if (
                player != null && victim != null && victim.Profile.Side == EPlayerSide.Usec &&
                (
                    (player.iPlayer.Profile.Side != EPlayerSide.Usec && Utils.Utils.PlayerHasKnightQuest(player.iPlayer.Profile)) ||
                    (player.iPlayer.Profile.Side == EPlayerSide.Usec && Utils.Utils.FlagGet("pitFireTeam"))
                )
            )
            {
                return false;
            }

            return true;
        }
    }
}
