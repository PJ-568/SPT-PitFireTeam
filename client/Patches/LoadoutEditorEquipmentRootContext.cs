using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using System.Threading.Tasks;

namespace pitTeam.Patches
{
    internal class LoadoutEditorEquipmentRootContext : GClass3450
    {
        public LoadoutEditorEquipmentRootContext(EItemViewType viewType)
            : base(viewType)
        {
        }

        public override ItemContextAbstractClass CreateChild(Item item)
        {
            if (item == null)
            {
                return this;
            }

            return new GClass3453(item, this);
        }
    }

    internal sealed class LoadoutEditorRepairByKitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Class308), nameof(Class308.RepairItemsByRepairKit));
        }

        [PatchPrefix]
        private static bool PatchPrefix(RepairItem[] repairKitsInfo, string targetItemId, ref Task<IResult> __result)
        {
            if (!OtherPlayerProfileScreenPatch.TryGetLoadoutEditorEquipmentItem(targetItemId, out Item itemToRepair))
            {
                if (OtherPlayerProfileScreenPatch.TryGetLoadoutEditorItem(targetItemId, out _))
                {
                    __result = Task.FromResult<IResult>(new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0));
                    return false;
                }

                return true;
            }

            if (!OtherPlayerProfileScreenPatch.CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return true;
            }

            __result = OtherPlayerProfileScreenPatch.RepairLoadoutEditorEquipmentWithKitAsync(repairKitsInfo, itemToRepair);
            return false;
        }
    }

    internal sealed class LoadoutEditorRepairByTraderPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(TraderClass), nameof(TraderClass.RepairItems));
        }

        [PatchPrefix]
        private static bool PatchPrefix(TraderClass __instance, RepairItem repairItem, ref Task<IResult> __result)
        {
            if (__instance == null
                || repairItem == null)
            {
                return true;
            }

            if (!OtherPlayerProfileScreenPatch.TryGetLoadoutEditorEquipmentItem(repairItem.Id, out Item itemToRepair))
            {
                if (OtherPlayerProfileScreenPatch.TryGetLoadoutEditorItem(repairItem.Id, out _))
                {
                    __result = Task.FromResult<IResult>(new FailedResult("This teammate loadout item must remain on teammate equipment to be repaired", 0));
                    return false;
                }

                return true;
            }

            if (!OtherPlayerProfileScreenPatch.CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return true;
            }

            __result = OtherPlayerProfileScreenPatch.RepairLoadoutEditorEquipmentWithTraderAsync(__instance.Id, repairItem, itemToRepair);
            return false;
        }
    }

    internal sealed class LoadoutEditorRepairContextInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsAbstractClass), nameof(ContextInteractionsAbstractClass.IsActive));
        }

        [PatchPostfix]
        private static void PatchPostfix(ContextInteractionsAbstractClass __instance, EItemInfoButton button, ref bool __result)
        {
            Item item = __instance?.Item_0;
            if (item == null
                || OtherPlayerProfileScreenPatch.LoadoutEditorOverlayRoot == null
                || !OtherPlayerProfileScreenPatch.IsLoadoutEditorEquipmentItem(item))
            {
                return;
            }

            // Keep this at the context menu layer instead of patching ContextInteractionSwitcherClass.IsActive:
            // the switcher is used broadly during profile initialization, while this only changes the final
            // visible action state for teammate editor equipment.
            if (button == EItemInfoButton.Insure)
            {
                __result = false;
                return;
            }

            if (button == EItemInfoButton.Repair && OtherPlayerProfileScreenPatch.CanRepairLoadoutEditorEquipmentItem(item))
            {
                __result = true;
            }
        }
    }

    internal sealed class LoadoutEditorLockContextInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsAbstractClass), nameof(ContextInteractionsAbstractClass.IsActive));
        }

        [PatchPostfix]
        private static void PatchPostfix(ContextInteractionsAbstractClass __instance, EItemInfoButton button, ref bool __result)
        {
            Item item = __instance?.Item_0;
            if (item == null || OtherPlayerProfileScreenPatch.LoadoutEditorOverlayRoot == null)
            {
                return;
            }

            if (button == EItemInfoButton.Open && OtherPlayerProfileScreenPatch.ShouldBlockLoadoutEditorContainerOpen(item))
            {
                __result = false;
                return;
            }

            if (OtherPlayerProfileScreenPatch.IsLoadoutEditorPinLockInteraction(button)
                && OtherPlayerProfileScreenPatch.IsLoadoutEditorStashItem(item))
            {
                __result = false;
            }
        }
    }

    internal sealed class LoadoutEditorLockExecuteInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsAbstractClass), nameof(ContextInteractionsAbstractClass.ExecuteInteractionInternal));
        }

        [PatchPrefix]
        private static bool PatchPrefix(ContextInteractionsAbstractClass __instance, EItemInfoButton interaction)
        {
            Item item = __instance?.Item_0;
            if (item == null || OtherPlayerProfileScreenPatch.LoadoutEditorOverlayRoot == null)
            {
                return true;
            }

            if (interaction == EItemInfoButton.Open && OtherPlayerProfileScreenPatch.ShouldBlockLoadoutEditorContainerOpen(item))
            {
                __instance.Action_6?.Invoke();
                LoadoutEditorLockUi.ShowLockedContainerNotification();
                return false;
            }

            if (OtherPlayerProfileScreenPatch.IsLoadoutEditorPinLockInteraction(interaction)
                && OtherPlayerProfileScreenPatch.IsLoadoutEditorStashItem(item))
            {
                __instance.Action_6?.Invoke();
                return false;
            }

            return true;
        }
    }

    internal sealed class LoadoutEditorLockedContainerOpenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.OpenItem));
        }

        [PatchPrefix]
        private static bool PatchPrefix(CompoundItem item)
        {
            if (!OtherPlayerProfileScreenPatch.ShouldBlockLoadoutEditorContainerOpen(item))
            {
                return true;
            }

            LoadoutEditorLockUi.ShowLockedContainerNotification();
            return false;
        }
    }

    internal sealed class LoadoutEditorLockedItemMovePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionsHandlerClass), nameof(InteractionsHandlerClass.CanModifyItem));
        }

        [PatchPrefix]
        private static bool PatchPrefix(Item item, ref Error error, ref bool __result)
        {
            if (!OtherPlayerProfileScreenPatch.TryFindLoadoutEditorLockedItemInPath(item, out Item lockedItem))
            {
                return true;
            }

            error = new InteractionsHandlerClass.GClass1606(lockedItem ?? item);
            __result = false;
            return false;
        }
    }

    internal sealed class LoadoutEditorLockedDestinationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(InteractionsHandlerClass), "smethod_24");
        }

        [PatchPrefix]
        private static bool PatchPrefix(ItemAddress to, ref Error error, ref bool __result)
        {
            if (!OtherPlayerProfileScreenPatch.TryFindLoadoutEditorLockedItemInAddress(to, out Item lockedItem))
            {
                return true;
            }

            error = new InteractionsHandlerClass.GClass1606(lockedItem);
            __result = false;
            return false;
        }
    }

    internal static class LoadoutEditorLockUi
    {
        public static void ShowLockedContainerNotification()
        {
            NotificationManagerClass.DisplayWarningNotification("Container is locked".Localized(null), ENotificationDurationType.Default);
        }
    }
}
