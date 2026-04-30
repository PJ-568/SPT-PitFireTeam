using EFT;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using EventInfo = BotEventHandler.GClass692;

namespace pitTeam.Patches
{
    internal class FollowerBotRequestTakePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotRequest), nameof(BotRequest.Take));
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotRequest __instance, BotOwner executor)
        {
            if (executor == null || !BossPlayers.IsFollower(executor))
            {
                return true;
            }

            return FollowerBotRequestGate.TryConsume(executor, __instance.BotRequestType);
        }
    }

    internal class FollowerBotReceiverHardAimIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_3");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, IPlayer player, bool status)
        {
            BotOwner botOwner = __instance?.BotOwner_0;
            return botOwner == null || !BossPlayers.IsFollower(botOwner);
        }
    }

    internal class FollowerBotReceiverTiltIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_4");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, IPlayer player)
        {
            BotOwner botOwner = __instance?.BotOwner_0;
            return botOwner == null || !BossPlayers.IsFollower(botOwner);
        }
    }

    internal class FollowerBotReceiverPhraseIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_0");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            BotOwner botOwner = __instance?.BotOwner_0;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (info?.PlayerRequester == null || !BossPlayers.IsPlayerBoss(info.PlayerRequester.ProfileId))
            {
                return false;
            }

            return info.phrase == EPhraseTrigger.Stop;
        }
    }

    internal class FollowerBotReceiverGestureIgnorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_6");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, GClass532 data)
        {
            BotOwner botOwner = __instance?.BotOwner_0;
            if (botOwner == null || !BossPlayers.IsFollower(botOwner))
            {
                return true;
            }

            if (data?.Player == null || !BossPlayers.IsPlayerBoss(data.Player.ProfileId))
            {
                return false;
            }

            return data.Gesture == EInteraction.ComeWithMeGesture ||
                   data.Gesture == EInteraction.HoldGesture ||
                   data.Gesture == EInteraction.ThereGesture;
        }
    }
}
