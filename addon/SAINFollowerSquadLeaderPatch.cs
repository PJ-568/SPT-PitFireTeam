using EFT;
using HarmonyLib;
using SAIN.SAINComponent.Classes.Info;
using pitTeam.Components;
using pitTeam.Modules;
using System.Reflection;

namespace pitTeam.SAINAddon
{
    internal static class SAINFollowerSquadLeaderPatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.PropertyGetter(typeof(BotSquadContainer), nameof(BotSquadContainer.IAmLeader));
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find BotSquadContainer.IAmLeader for follower squad-leader patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerSquadLeaderPatch), nameof(Postfix_IAmLeader)));
            Modules.Logger.LogInfo("[Init] SAIN follower squad-leader override patch applied.");
        }

        private static void Postfix_IAmLeader(BotSquadContainer __instance, ref bool __result)
        {
            BotOwner? owner = __instance?.BotOwner;
            if (owner != null && BossPlayers.IsFollower(owner))
            {
                __result = false;
            }
        }
    }
}
