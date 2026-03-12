using friendlySAIN.Modules;
using friendlySAIN.Components;
using HarmonyLib;
using EFT;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal static class FollowerContactPhraseGate
    {
        private sealed class ContactState
        {
            public string LastEnemyProfileId;
        }

        private static readonly Dictionary<string, ContactState> StateByFollower = new Dictionary<string, ContactState>(StringComparer.Ordinal);

        public static bool ShouldAllow(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (owner.Memory?.HaveEnemy != true || owner.Memory.GoalEnemy == null)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            string enemyId = owner.Memory.GoalEnemy.ProfileId;
            if (string.IsNullOrEmpty(enemyId))
            {
                enemyId = "<unknown>";
            }

            if (!StateByFollower.TryGetValue(owner.ProfileId, out ContactState state))
            {
                StateByFollower[owner.ProfileId] = new ContactState { LastEnemyProfileId = enemyId };
                return true;
            }

            if (string.Equals(state.LastEnemyProfileId, enemyId, StringComparison.Ordinal))
            {
                return false;
            }

            state.LastEnemyProfileId = enemyId;
            return true;
        }
    }

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

            if (type == EPhraseTrigger.OnRepeatedContact && BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                if (!FollowerContactPhraseGate.ShouldAllow(__instance.BotOwner_0))
                {
                    return false;
                }
            }

            if (
                (
                    type == EPhraseTrigger.OnFight || type == EPhraseTrigger.OnRepeatedContact ||
                    (__instance.BotOwner_0.Memory.HaveEnemy && (type == EPhraseTrigger.MumblePhrase || type == EPhraseTrigger.OnMutter))
                ) &&
                    BossPlayers.IsFollower(__instance.BotOwner_0)
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

            if (type == EPhraseTrigger.OnRepeatedContact && BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                if (!FollowerContactPhraseGate.ShouldAllow(__instance.BotOwner_0))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
