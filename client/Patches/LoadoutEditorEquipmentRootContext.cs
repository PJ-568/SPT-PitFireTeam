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
                return true;
            }

            if (OtherPlayerProfileScreenPatch.ShouldRequireLoadoutEditorSaveBeforeRepair(itemToRepair))
            {
                __result = Task.FromResult(OtherPlayerProfileScreenPatch.ShowLoadoutEditorSaveBeforeRepairPrompt());
                return false;
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
                || repairItem == null
                || !OtherPlayerProfileScreenPatch.TryGetLoadoutEditorEquipmentItem(repairItem.Id, out Item itemToRepair))
            {
                return true;
            }

            if (OtherPlayerProfileScreenPatch.ShouldRequireLoadoutEditorSaveBeforeRepair(itemToRepair))
            {
                __result = Task.FromResult(OtherPlayerProfileScreenPatch.ShowLoadoutEditorSaveBeforeRepairPrompt());
                return false;
            }

            if (!OtherPlayerProfileScreenPatch.CanRepairLoadoutEditorEquipmentItem(itemToRepair))
            {
                return true;
            }

            __result = OtherPlayerProfileScreenPatch.RepairLoadoutEditorEquipmentWithTraderAsync(__instance.Id, repairItem, itemToRepair);
            return false;
        }
    }

    internal sealed class LoadoutEditorRepairExecuteInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsAbstractClass), nameof(ContextInteractionsAbstractClass.ExecuteInteractionInternal));
        }

        [PatchPrefix]
        private static bool PatchPrefix(ContextInteractionsAbstractClass __instance, EItemInfoButton interaction)
        {
            if (interaction != EItemInfoButton.Repair)
            {
                return true;
            }

            Item item = __instance?.Item_0;
            if (item == null || !OtherPlayerProfileScreenPatch.ShouldRequireLoadoutEditorSaveBeforeRepair(item))
            {
                return true;
            }

            __instance.Action_6?.Invoke();
            OtherPlayerProfileScreenPatch.ShowLoadoutEditorSaveBeforeRepairPrompt();
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
}
