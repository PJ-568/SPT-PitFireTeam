using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace friendlySAIN.Patches
{
    // patch for preventing bots from talking if silenced command is active
    internal class BotTalkTrySayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type[] parameterTypes = new Type[] { typeof(EPhraseTrigger), typeof(ETagStatus?), typeof(bool) };
            return AccessTools.Method(typeof(BotTalk), "TrySay", parameterTypes);
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotTalk __instance, EPhraseTrigger type, ETagStatus? additionaMask, bool withGroupDelay)
        {
            if (__instance.IsSilenced) return false;

            if (
                (
                    type == EPhraseTrigger.OnFight || type == EPhraseTrigger.OnRepeatedContact ||
                    (__instance.botOwner_0.Memory.HaveEnemy && (type == EPhraseTrigger.MumblePhrase || type == EPhraseTrigger.OnMutter))
                ) &&
                    BossPlayers.IsFollower(__instance.botOwner_0)
                )
            {
                int freq = friendlySAIN.botTalk.Value;

                if (freq == 0) return false;

                if (freq < 100)
                {
                    if (new Random().Next(1, 101) > freq)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
    // patch for preventing bots from talking if silenced command is active
    internal class BotTalkSayPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotTalk), "Say");
        }

        [PatchPrefix]
        private static bool PatchPrefix(BotTalk __instance, EPhraseTrigger type, bool sayImmediately = false, ETagStatus? additionalMask = null)
        {
            if (__instance.IsSilenced) return false;

            return true;
        }
    }
}
