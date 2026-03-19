using Arena.UI;
using EFT.UI;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using TMPro;

namespace friendlySAIN.Patches
{
    internal static class AddTeammateNicknameFieldUi
    {
        private static readonly FieldInfo StatusLabelField = AccessTools.Field(typeof(NicknameField), "_statusLabel");

        public static void SetStatusLabelText(NicknameField nicknameField, string text)
        {
            TMP_Text statusLabel = StatusLabelField?.GetValue(nicknameField) as TMP_Text;
            if (statusLabel != null)
            {
                statusLabel.text = text;
            }
        }
    }

    internal class AddTeammateNicknameFieldEndEditPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(NicknameField), "method_2");
        }

        [PatchPrefix]
        private static bool PatchPrefix(NicknameField __instance, string nickname)
        {
            bool isTeammateCreationFlow = AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController);
            bool isRenameOverlayField = ReferenceEquals(__instance, OtherPlayerProfileScreenPatch.RenameOverlayField);
            if (!isTeammateCreationFlow && !isRenameOverlayField)
            {
                return true;
            }

            __instance.method_3(nickname);
            if (isTeammateCreationFlow)
            {
                AddTeammateCreationFlow.RefreshSubmitButton();
            }

            return false;
        }
    }

    internal class AddTeammateNicknameValueChangedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HeadSelectionState), "method_10");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            if (!AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController))
            {
                return;
            }

            AddTeammateCreationFlow.RefreshSubmitButton();
        }
    }

    internal class AddTeammateFinishPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EftAccountSideSelectionScreen).BaseType, "method_7");
        }

        [PatchPrefix]
        private static bool PatchPrefix(object __instance)
        {
            if (!AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController))
            {
                return true;
            }

            AddTeammateCreationFlow.TryCompleteFromCurrentScreen();
            return false;
        }
    }

    internal class AddTeammateNicknameFieldInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(NicknameField), nameof(NicknameField.Init));
        }

        [PatchPostfix]
        private static void PatchPostfix(NicknameField __instance)
        {
            if (!AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController))
            {
                return;
            }

            AddTeammateNicknameFieldUi.SetStatusLabelText(
                __instance,
                AddTeammateCreationFlow.GetLocalizedSocialUi("EnterNickname", "enter player nickname"));
        }
    }

    internal class AddTeammateNicknameFieldStatusPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(NicknameField), "method_6");
        }

        [PatchPostfix]
        private static void PatchPostfix(NicknameField __instance, ENicknameError error, bool isFromBackend = false)
        {
            bool isTeammateCreationFlow = AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController);
            bool isRenameOverlayField = ReferenceEquals(__instance, OtherPlayerProfileScreenPatch.RenameOverlayField);
            if ((!isTeammateCreationFlow && !isRenameOverlayField) || isFromBackend)
            {
                return;
            }

            if (error == ENicknameError.TooShort)
            {
                AddTeammateNicknameFieldUi.SetStatusLabelText(
                    __instance,
                    AddTeammateCreationFlow.GetLocalizedSocialUi("NicknameTooShort", "Nickname too short"));
                return;
            }

            if (error == ENicknameError.ValidNickname)
            {
                AddTeammateNicknameFieldUi.SetStatusLabelText(
                    __instance,
                    AddTeammateCreationFlow.GetLocalizedSocialUi("EnterNickname", "Enter player nickname"));
            }
        }
    }
}
