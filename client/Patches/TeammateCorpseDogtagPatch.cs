using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using pitTeam.Modules;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class TeammateCorpseDogtagGuard
    {
        private static readonly FieldInfo ContainersPanelDogtagSlotViewField = AccessTools.Field(typeof(ContainersPanel), "slotView_0");

        public static bool IsTeammateCorpseEquipment(InventoryEquipment equipment)
        {
            try
            {
                return equipment?.Owner is GClass3385 corpseOwner
                    && BossPlayers.IsFollowerProfileId(corpseOwner.KilledProfileID);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse equipment owner.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }
        }

        public static bool IsTeammateCorpseDogtag(Item item)
        {
            try
            {
                if (item == null || item.GetItemComponent<DogtagComponent>() == null)
                {
                    return false;
                }

                return item.Owner is GClass3385 corpseOwner
                    && BossPlayers.IsFollowerProfileId(corpseOwner.KilledProfileID);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to check teammate corpse dogtag.");
                pitFireTeam.Log?.LogError(ex);
                return false;
            }
        }

        public static void HideContainersPanelDogtag(ContainersPanel panel)
        {
            try
            {
                if (panel == null)
                {
                    return;
                }

                SlotView dogtagSlotView = ContainersPanelDogtagSlotViewField?.GetValue(panel) as SlotView;
                if (dogtagSlotView == null)
                {
                    return;
                }

                dogtagSlotView.Close();
                UnityEngine.Object.Destroy(dogtagSlotView.gameObject);
                ContainersPanelDogtagSlotViewField?.SetValue(panel, null);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to hide teammate corpse dogtag container slot.");
                pitFireTeam.Log?.LogError(ex);
            }
        }

        public static void HideEquipmentTabDogtag(EquipmentTab tab)
        {
            try
            {
                SlotView dogtagSlotView = tab?.GetSlotView(EquipmentSlot.Dogtag);
                if (dogtagSlotView == null)
                {
                    return;
                }

                dogtagSlotView.Close();
                dogtagSlotView.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                pitFireTeam.Log?.LogError("[Loot] Failed to hide teammate corpse dogtag equipment slot.");
                pitFireTeam.Log?.LogError(ex);
            }
        }
    }

    internal sealed class TeammateCorpseContainersPanelDogtagPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContainersPanel), nameof(ContainersPanel.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(ContainersPanel __instance, InventoryEquipment equipment)
        {
            if (TeammateCorpseDogtagGuard.IsTeammateCorpseEquipment(equipment))
            {
                TeammateCorpseDogtagGuard.HideContainersPanelDogtag(__instance);
            }
        }
    }

    internal sealed class TeammateCorpseEquipmentTabDogtagPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EquipmentTab), nameof(EquipmentTab.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(EquipmentTab __instance, InventoryEquipment equipment)
        {
            if (TeammateCorpseDogtagGuard.IsTeammateCorpseEquipment(equipment))
            {
                TeammateCorpseDogtagGuard.HideEquipmentTabDogtag(__instance);
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
