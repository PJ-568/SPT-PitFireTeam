using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class MatchMakerSelectionLocationScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), "method_5");
        }
        [PatchPostfix]
        private static void PatchPostfix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {
        }
    }
}
