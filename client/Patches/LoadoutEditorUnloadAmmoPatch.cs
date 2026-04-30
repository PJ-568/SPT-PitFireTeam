using EFT.InventoryLogic;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace pitTeam.Patches
{
    internal sealed class LoadoutEditorUnloadAmmoPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GClass3372).GetMethod(nameof(GClass3372.GetPrioritizedGridsForUnloadedObject));
        }

        [PatchPostfix]
        private static void PatchPostfix(InventoryEquipment equipment, ref IEnumerable<StashGridClass> __result)
        {
            if (OtherPlayerProfileScreenPatch.LoadoutEditorOverlayRoot == null
                || equipment == null
                || !ReferenceEquals(equipment, OtherPlayerProfileScreenPatch.LoadoutEditorProfile?.Inventory?.Equipment))
            {
                return;
            }

            List<StashGridClass> prioritizedGrids = __result?.ToList() ?? [];
            HashSet<string> seenGridIds = prioritizedGrids
                .Where(grid => grid != null)
                .Select(grid => grid.ID)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet();

            AppendUniqueGrids(prioritizedGrids, seenGridIds, GetSlotGrids(equipment, EquipmentSlot.Backpack));
            AppendUniqueGrids(prioritizedGrids, seenGridIds, OtherPlayerProfileScreenPatch.LoadoutEditorProfile?.Inventory?.Stash?.Grids);
            __result = prioritizedGrids;
        }

        private static IEnumerable<StashGridClass> GetSlotGrids(InventoryEquipment equipment, EquipmentSlot slot)
        {
            return (equipment.GetSlot(slot)?.ContainedItem as CompoundItem)?.Grids ?? [];
        }

        private static void AppendUniqueGrids(
            ICollection<StashGridClass> destination,
            ISet<string> seenGridIds,
            IEnumerable<StashGridClass> grids)
        {
            if (grids == null)
            {
                return;
            }

            foreach (StashGridClass grid in grids)
            {
                if (grid == null)
                {
                    continue;
                }

                string gridId = grid.ID;
                if (!string.IsNullOrWhiteSpace(gridId) && !seenGridIds.Add(gridId))
                {
                    continue;
                }

                destination.Add(grid);
            }
        }
    }
}
