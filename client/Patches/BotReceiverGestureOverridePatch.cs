using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    // Route follower gesture commands through pitAIBossPlayer command handling, not vanilla BotReceiver.method_6 logic.
    internal class BotReceiverGestureOverridePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_6");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, GClass532 data)
        {
            if (data?.Player == null)
            {
                return true;
            }

            BotOwner botOwner = __instance.BotOwner_0;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (!BossPlayers.IsPlayerBoss(data.Player.ProfileId))
            {
                return true;
            }

            switch (data.Gesture)
            {
                case EInteraction.ComeWithMeGesture:
                case EInteraction.HoldGesture:
                case EInteraction.ThereGesture:
                    return false;
                default:
                    return true;
            }
        }
    }
}
