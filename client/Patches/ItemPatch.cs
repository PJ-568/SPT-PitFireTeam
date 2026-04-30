using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

using ModSlotViewTP = EFT.UI.DragAndDrop.ModSlotView.GStruct448;

namespace pitTeam.Patches
{
    // Make all followers items unlootable
    internal class UnlootableComponentPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(UnlootableComponent), "IsUnlootableFrom");
        }

        [PatchPrefix]
        public static bool PatchPrefix(UnlootableComponent __instance, ref bool __result, IContainer container)
        {
            bool isBotEquiptment = false;

            Modules.InteractableObjects.GetStoredEquipment().ExecuteForEach((id, items) =>
            {
                foreach (var itemId in items)
                {
                    if (itemId == __instance.Item.Id)
                    {
                        isBotEquiptment = true;
                        break;
                    }
                }
            });

            if (isBotEquiptment)
            {
                __result = true;
                return false;
            }

            return true;
        }
    }
    // Make all followers items unremovable
    internal class ModRaidModdablePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(Mod), "RaidModdable");
        }

        [PatchPrefix]
        public static bool PatchPrefix(Mod __instance, ref bool __result)
        {
            bool isBotEquiptment = false;

            foreach (var stack in Modules.InteractableObjects.GetStoredEquipment())
            {
                foreach (var itemId in stack.Value)
                {
                    if (itemId == __instance.Id)
                    {
                        isBotEquiptment = true;
                        break;
                    }
                }

                if (isBotEquiptment) break;
            }

            if (isBotEquiptment)
            {
                __result = false;
                return false;
            }
            // let original run
            return true;
        }
    }

    internal class ItemSpecificationPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemSpecificationPanel), "method_21");
        }

        [PatchPrefix]
        public static bool PatchPrefix(ItemSpecificationPanel __instance, ref KeyValuePair<EModLockedState, ModSlotViewTP> __result, Slot slot)
        {
            string itemName = (slot.ContainedItem != null) ? slot.ContainedItem.Name.Localized(null) : string.Empty;
            string id = (slot.ContainedItem != null) ? slot.ContainedItem.Id : null;

            if (slot.Locked)
            {
                bool isBotEquiptment = false;

                foreach (var stack in Modules.InteractableObjects.GetStoredEquipment())
                {
                    foreach (var itemId in stack.Value)
                    {
                        if (itemId == id)
                        {
                            isBotEquiptment = true;
                            break;
                        }
                    }

                    if (isBotEquiptment) break;
                }

                if (!isBotEquiptment) return true;

                __result = new KeyValuePair<EModLockedState, ModSlotViewTP>(EModLockedState.RaidLock, new ModSlotViewTP
                {
                    Error = "<color=red>" + "Raid lock".Localized(null) + "</color>",
                    ItemName = itemName
                });

                return false;
            }

            return true;
        }
    }
}
