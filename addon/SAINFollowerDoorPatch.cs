using EFT;
using EFT.Interactive;
using friendlySAIN.Modules;
using HarmonyLib;
using SAIN.SAINComponent.Classes.Mover;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerDoorPatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(
                typeof(DoorOpener),
                nameof(DoorOpener.SelectDoor),
                new[] { typeof(EInteractionType).MakeByRefType(), typeof(DoorDataStruct).MakeByRefType(), typeof(IBotPathData) });

            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find SAIN DoorOpener.SelectDoor for follower door patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINFollowerDoorPatch), nameof(Postfix_SelectDoor)));
            Modules.Logger.LogInfo("[Init] SAIN follower door auto-close suppression patch applied.");
        }

        private static void Postfix_SelectDoor(DoorOpener __instance, ref bool __result, ref EInteractionType interactionType, ref DoorDataStruct currentDoor)
        {
            try
            {
                if (!__result || interactionType != EInteractionType.Close)
                {
                    return;
                }

                BotOwner? owner = __instance?.BotOwner;
                if (owner == null || !BossPlayers.IsFollower(owner))
                {
                    return;
                }

                __result = false;
                interactionType = EInteractionType.Open;
                currentDoor = default;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN] Failed in follower door auto-close patch.");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
