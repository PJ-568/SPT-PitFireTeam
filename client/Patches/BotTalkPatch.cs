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
    internal static class FollowerForcedPhraseGate
    {
        private sealed class ForcedPhraseState
        {
            public EPhraseTrigger Phrase;
            public float UntilTime;
        }

        private static readonly Dictionary<string, ForcedPhraseState> StateByFollower = new Dictionary<string, ForcedPhraseState>(StringComparer.Ordinal);

        public static void Arm(BotOwner owner, EPhraseTrigger phrase, float durationSeconds)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            float safeDuration = Math.Max(0.1f, durationSeconds);
            StateByFollower[owner.ProfileId] = new ForcedPhraseState
            {
                Phrase = phrase,
                UntilTime = UnityEngine.Time.time + safeDuration
            };
        }

        public static void Clear(BotOwner owner)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return;
            }

            StateByFollower.Remove(owner.ProfileId);
        }

        public static bool ShouldBlock(BotOwner owner, EPhraseTrigger phrase)
        {
            if (owner == null || string.IsNullOrEmpty(owner.ProfileId))
            {
                return false;
            }

            if (!StateByFollower.TryGetValue(owner.ProfileId, out ForcedPhraseState state))
            {
                return false;
            }

            if (UnityEngine.Time.time > state.UntilTime)
            {
                StateByFollower.Remove(owner.ProfileId);
                return false;
            }

            if (phrase == state.Phrase)
            {
                return false;
            }

            return true;
        }
    }

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

    internal static class FollowerMutedCombatPhraseGate
    {
        private static readonly HashSet<EPhraseTrigger> MutedFollowerTriggers = new HashSet<EPhraseTrigger>
        {
            EPhraseTrigger.Clear,
            EPhraseTrigger.LostVisual,
            EPhraseTrigger.OnLostVisual
        };

        public static bool ShouldBlock(BotOwner owner, EPhraseTrigger phrase)
        {
            return owner != null && BossPlayers.IsFollower(owner) && MutedFollowerTriggers.Contains(phrase);
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
            if (FollowerForcedPhraseGate.ShouldBlock(__instance.BotOwner_0, type))
            {
                return false;
            }

            if (FollowerMutedCombatPhraseGate.ShouldBlock(__instance.BotOwner_0, type))
            {
                return false;
            }

            if (__instance.IsSilenced) return false;

            if (type == EPhraseTrigger.OnRepeatedContact && BossPlayers.IsFollower(__instance.BotOwner_0))
            {
                if (!FollowerContactPhraseGate.ShouldAllow(__instance.BotOwner_0))
                {
                    return false;
                }
            }

            if (FollowerTalkFrequencyGate.ShouldBlockCombatTalk(__instance.BotOwner_0, type))
            {
                return false;
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
            if (FollowerForcedPhraseGate.ShouldBlock(__instance.BotOwner_0, type))
            {
                return false;
            }

            if (FollowerMutedCombatPhraseGate.ShouldBlock(__instance.BotOwner_0, type))
            {
                return false;
            }

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
