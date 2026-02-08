using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class BotReceiverInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "Init");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance)
        {
            BotOwner botOwner = (BotOwner)AccessTools.Field(typeof(BotReceiver), "BotOwner_0").GetValue(__instance);
            if (botOwner != null)
            {

                FollowerReceiver receiver = Receivers.GetReceiver(botOwner.ProfileId);
                if (receiver != null)
                {
                    receiver.Initiate();
                    return false;
                }
            }

            return true;
        }
    }

    internal class BotReceiverDisposePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "Dispose");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance)
        {
            BotOwner botOwner = (BotOwner)AccessTools.Field(typeof(BotReceiver), "BotOwner_0").GetValue(__instance);
            if (botOwner != null)
            {

                FollowerReceiver receiver = Receivers.GetReceiver(botOwner.ProfileId);
                if (receiver != null)
                {
                    receiver.Destroy();
                    return false;
                }
            }

            return true;
        }
    }

    internal class BotReceiverPhrasePatch : ModulePatch
    {

        public static List<WildSpawnType> goonRole = new List<WildSpawnType> { WildSpawnType.followerBigPipe, WildSpawnType.followerBirdEye, WildSpawnType.bossKnight };
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_0");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, BotEventHandler.GClass692 info)
        {

            BotOwner botOwner = (BotOwner)AccessTools.Field(typeof(BotReceiver), "BotOwner_0").GetValue(__instance);
            if (botOwner != null)
            {
                if (
                    !BossPlayers.IsFollower(botOwner) &&
                    (info.phrase == (EPhraseTrigger)CustomPhrases.TeamStatus || info.phrase == (EPhraseTrigger)CustomPhrases.OverThere)
                )
                {
                    return false;
                }

                // on cooperation, starting following the boss player
                if (info.phrase == EPhraseTrigger.Cooperation || info.phrase == EPhraseTrigger.FollowMe)
                {
                    IPlayer requester = info.PlayerRequester;

                    if (requester != null && BossPlayers.IsPlayerBoss(requester.ProfileId) && (botOwner.GetPlayer.Transform.position - requester.Transform.position).magnitude < 10f)
                    {
                        if (!BossPlayers.IsFollower(botOwner))
                        {
                            // - if goons are friendly, refuse to follow
                            if (requester != null && botOwner.BotsGroup.IsAlly(requester))
                            {
                                foreach (var role in goonRole)
                                {
                                    if (botOwner.IsRole(role))
                                    {
                                        botOwner.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                                        return false;
                                    }
                                }
                            }

                            // - this will switch the BotReceiver to our own, so the rest can be altered there
                            if (botOwner.BotsGroup.RequestsController.TryAskFollowMeRequest(requester, botOwner))
                            {
                                Utils.Utils.SetTimeout(() =>
                                {
                                    botOwner.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                                }, 1000);
                            }
                            else
                            {
                                botOwner.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                            }

                            return false;
                        }
                    }

                }

            }

            return true;
        }
    }
    // WIP
    internal class BotReceiverGestusPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_6");
        }
        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, GClass532 data)
        {
            if (data == null)
            {
                return true;
            }
            BotOwner botOwner = (BotOwner)AccessTools.Field(typeof(BotReceiver), "BotOwner_0").GetValue(__instance);

            if (botOwner != null)
            {
                if (BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                if (data.Player != null && BossPlayers.IsPlayerBoss(data.Player.ProfileId) && BotReceiverPhrasePatch.goonRole.Contains(botOwner.Profile.Info.Settings.Role))
                {

                }
            }
            return true;
        }
    }
}
