using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using SAIN.Components;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerSearchCurrentEnemyLookPatch
    {
        private const float SearchEndpointDistance = 2f;

        public static void Apply(Harmony harmony)
        {
            Type? searchActionType = typeof(SAINLayer).Assembly.GetType("SAIN.Layers.Combat.Solo.SearchAction", false);
            MethodInfo? target = AccessTools.Method(searchActionType, "OnSteeringTicked");
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN SearchAction.OnSteeringTicked for follower search look patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerSearchCurrentEnemyLookPatch), nameof(Postfix_OnSteeringTicked)));
            Modules.Logger.LogInfo("[Init] SAIN follower search current-enemy look patch applied.");
        }

        private static void Postfix_OnSteeringTicked(object __instance)
        {
            try
            {
                BotComponent? bot = Traverse.Create(__instance).Property("Bot").GetValue<BotComponent>();
                Enemy? searchTarget = Traverse.Create(__instance).Property("_searchTarget").GetValue<Enemy>();
                BotOwner? botOwner = bot?.BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner) || searchTarget == null)
                {
                    return;
                }

                if (searchTarget.IsVisible)
                {
                    return;
                }

                if (searchTarget.KnownPlaces == null || searchTarget.KnownPlaces.BotDistanceFromLastKnown > SearchEndpointDistance)
                {
                    return;
                }

                if (!searchTarget.CheckValid())
                {
                    return;
                }

                bot.Steering?.LookToEnemy(searchTarget);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower search current-enemy look patch.");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
