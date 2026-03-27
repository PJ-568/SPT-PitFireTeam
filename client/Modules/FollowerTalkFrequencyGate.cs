using EFT;
using friendlySAIN.Components;
using System;

namespace friendlySAIN.Modules
{
    public static class FollowerTalkFrequencyGate
    {
        private static readonly Random Random = new Random();

        public static bool ShouldBlockCombatTalk(BotOwner owner, EPhraseTrigger phrase)
        {
            if (owner == null || !BossPlayers.IsFollower(owner))
            {
                return false;
            }

            if (!IsCombatTalkPhrase(owner, phrase))
            {
                return false;
            }

            return ShouldBlockForConfiguredFrequency();
        }

        private static bool IsCombatTalkPhrase(BotOwner owner, EPhraseTrigger phrase)
        {
            return phrase == EPhraseTrigger.OnFight
                || phrase == EPhraseTrigger.OnRepeatedContact
                || (owner.Memory?.HaveEnemy == true && (phrase == EPhraseTrigger.MumblePhrase || phrase == EPhraseTrigger.OnMutter));
        }

        private static bool ShouldBlockForConfiguredFrequency()
        {
            int freq = friendlySAIN.botTalk.Value;
            if (freq <= 0)
            {
                return true;
            }

            if (freq >= 100)
            {
                return false;
            }

            lock (Random)
            {
                return Random.Next(1, 101) > freq;
            }
        }
    }
}
