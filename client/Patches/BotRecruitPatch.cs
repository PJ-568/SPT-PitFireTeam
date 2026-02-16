using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

using EventInfo = BotEventHandler.GClass692;

namespace friendlySAIN.Patches
{
    // Minimal recruit trigger for 4.x:
    // Intercept FollowMe/Cooperation phrase receipt and forward it to the existing follow-request flow.
    internal class BotReceiverFollowMeRecruitPatch : ModulePatch
    {
        private const float RecruitPhraseDistance = 15f;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_0");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            if (__instance == null || info == null) return true;

            BotOwner? botOwner = __instance.BotOwner_0;
            if (botOwner == null) return true;

            EPhraseTrigger? phrase = ReadPhrase(info);
            if (!phrase.HasValue) return true;

            if (
                    !BossPlayers.IsFollower(botOwner) &&
                    (
                        phrase == (EPhraseTrigger)CustomPhrases.TeamStatus ||
                        phrase == (EPhraseTrigger)CustomPhrases.OverThere ||
                        phrase == EPhraseTrigger.OnRepeatedContact
                    )
                )
            {
                return false;
            }

            if (phrase != EPhraseTrigger.Cooperation && phrase != EPhraseTrigger.FollowMe)
            {
                return true;
            } else if (BossPlayers.IsFollower(botOwner))
            {
                return false;
            }

            IPlayer requester = ReadRequester(info);
            if (requester == null) return true;
            if (!BossPlayers.IsPlayerBoss(requester.ProfileId)) return true;

            // Keep vanilla behavior at longer range.
            if ((botOwner.Position - requester.Position).sqrMagnitude > RecruitPhraseDistance * RecruitPhraseDistance) return true;

            bool accepted = botOwner.BotsGroup?.RequestsController?.TryAskFollowMeRequest(requester, botOwner) == true;
            if (!accepted)
            {
                botOwner.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                botOwner.Gesture.TryGestus(EInteraction.NoGesture, true);
            }
            else
            {
                int responseDelayMs = Random.Range(300, 700);
                Utils.Utils.SetTimeout(() =>
                {
                    if (botOwner.IsDead || botOwner.BotState != EBotState.Active) return;
                    botOwner.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                }, responseDelayMs);

                botOwner.Gesture.TryGestus(EInteraction.OkGesture, true);
            }

            // Request was handled by the mod flow, suppress vanilla duplicate processing.
            return false;
        }

        private static EPhraseTrigger ReadPhrase(EventInfo info)
        {
            return info.phrase;
        }

        private static IPlayer ReadRequester(EventInfo info)
        {
            return info.PlayerRequester;
        }
    }
}
