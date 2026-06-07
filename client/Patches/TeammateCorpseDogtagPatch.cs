using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal static class TeammateCorpseDogtagGuard
    {
        public static bool IsTeammateCorpseDogtag(Item item)
        {
            try
            {
                if (item == null || item.GetItemComponent<DogtagComponent>() == null)
                {
                    return false;
                }

                return TeammateCorpseIdentity.IsTeammateCorpseOwner(item.Owner);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse dogtag.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }
        }
    }

    internal sealed class TeammateCorpseDogtagMovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TraderControllerClass), nameof(TraderControllerClass.CanMoveDogtag));
        }

        [PatchPrefix]
        private static bool PatchPrefix(Item item, ref bool __result)
        {
            if (!TeammateCorpseDogtagGuard.IsTeammateCorpseDogtag(item))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }
}
