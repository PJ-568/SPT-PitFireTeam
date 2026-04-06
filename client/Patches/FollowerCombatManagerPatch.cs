using EFT;
using friendlySAIN.Components;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal sealed class FollowerCombatManagerAddGameWorldPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorldUnityTickListener), nameof(GameWorldUnityTickListener.Create));
        }

        [PatchPostfix]
        private static void PatchPostfix(GameObject gameObject, GameWorld gameWorld)
        {
            if (gameWorld is HideoutGameWorld)
            {
                return;
            }

            try
            {
                FollowerCombatManagerComponent manager =
                    gameWorld.gameObject.GetComponent<FollowerCombatManagerComponent>() ??
                    gameWorld.gameObject.AddComponent<FollowerCombatManagerComponent>();

                manager.Activate(gameWorld);
            }
            catch (System.Exception ex)
            {
                Modules.Logger.LogError($"[Init] Failed to create core follower combat manager shell: {ex}");
            }
        }
    }

    internal sealed class FollowerCombatManagerWorldTickPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GameWorld), nameof(GameWorld.DoWorldTick));
        }

        [PatchPostfix]
        private static void PatchPostfix(float dt)
        {
            FollowerCombatManagerComponent.Instance?.WorldTick(dt);
        }
    }
}
