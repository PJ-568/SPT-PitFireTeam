using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

using EventInfo = BotEventHandler.GClass692;

namespace friendlySAIN.Patches
{
    // Minimal recruit trigger for 4.x:
    // Intercept FollowMe/Cooperation phrase receipt and forward it to the existing follow-request flow.
    internal class BotReceiverFollowMeRecruitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotReceiver), "method_0");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotReceiver __instance, EventInfo info)
        {
            if (__instance == null || info == null) return true;

            BotOwner? botOwner = AccessTools.Field(typeof(BotReceiver), "BotOwner_0")?.GetValue(__instance) as BotOwner;
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
            }

            IPlayer requester = ReadRequester(info);
            if (requester == null) return true;
            if (!BossPlayers.IsPlayerBoss(requester.ProfileId)) return true;

            // Keep vanilla behavior at longer range.
            if ((botOwner.Position - requester.Position).sqrMagnitude > 10f * 10f) return true;

            bool accepted = botOwner.BotsGroup?.RequestsController?.TryAskFollowMeRequest(requester, botOwner) == true;
            if (!accepted)
            {
                botOwner.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
            }

            // Request was handled by the mod flow, suppress vanilla duplicate processing.
            return false;
        }

        private static EPhraseTrigger? ReadPhrase(EventInfo info)
        {
            object? value =
                AccessTools.Field(info.GetType(), "phrase")?.GetValue(info) ??
                AccessTools.Field(info.GetType(), "Phrase")?.GetValue(info);

            if (value is EPhraseTrigger phrase) return phrase;
            return null;
        }

        private static IPlayer ReadRequester(EventInfo info)
        {
            return
                AccessTools.Field(info.GetType(), "PlayerRequester")?.GetValue(info) as IPlayer ??
                AccessTools.Field(info.GetType(), "playerRequester")?.GetValue(info) as IPlayer ??
                AccessTools.Field(info.GetType(), "Player")?.GetValue(info) as IPlayer;
        }
    }
}
