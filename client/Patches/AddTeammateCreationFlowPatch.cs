using EFT.UI;
using EFT.UI.Screens;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class AddTeammateBackButtonPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type targetType = typeof(EftAccountSideSelectionScreen).BaseType;
            return AccessTools.Method(targetType, "method_9");
        }

        [PatchPrefix]
        private static bool PatchPrefix(object __instance)
        {
            if (!AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController))
            {
                return true;
            }

            AddTeammateCreationFlow.ReturnToMainScreen();
            return false;
        }
    }
}
