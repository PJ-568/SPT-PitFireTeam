using Arena.UI;
using Comfort.Common;
using EFT;
using EFT.UI;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
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

    internal class AddTeammateHeadSelectionOptionsPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HeadSelectionState), "method_1");
        }

        [PatchPrefix]
        private static bool PatchPrefix(HeadSelectionState __instance)
        {
            if (!AddTeammateCreationFlow.IsActiveForController(CurrentScreenSingletonClass.Instance.CurrentScreenController))
            {
                return true;
            }

            CustomizationSolverClass solver = Singleton<CustomizationSolverClass>.Instance;
            if (solver == null || __instance == null)
            {
                return true;
            }

            __instance.HeadTemplates = CollectAllHeads(solver);
            __instance.VoiceTemplates = CollectAllVoices(solver);
            __instance.Voices.Clear();
            __instance.method_3();
            __instance.method_4();
            return false;
        }

        private static List<KeyValuePair<MongoID, GClass3678>> CollectAllHeads(CustomizationSolverClass solver)
        {
            Dictionary<MongoID, GClass3678> heads = new Dictionary<MongoID, GClass3678>();
            foreach (EPlayerSide side in new[] { EPlayerSide.Bear, EPlayerSide.Usec, EPlayerSide.Savage })
            {
                foreach (GClass3678 head in solver.GetAvailableHeads(side))
                {
                    if (head != null && !heads.ContainsKey(head.Id))
                    {
                        heads[head.Id] = head;
                    }
                }
            }

            return heads
                .OrderBy(entry => entry.Value?.NameLocalizationKey.Localized(null) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new KeyValuePair<MongoID, GClass3678>(entry.Key, entry.Value))
                .ToList();
        }

        private static List<KeyValuePair<MongoID, GClass3681>> CollectAllVoices(CustomizationSolverClass solver)
        {
            Dictionary<MongoID, GClass3681> voices = new Dictionary<MongoID, GClass3681>();
            foreach (EPlayerSide side in new[] { EPlayerSide.Bear, EPlayerSide.Usec, EPlayerSide.Savage })
            {
                foreach (GClass3681 voice in solver.GetAvailableVoices(side))
                {
                    if (voice != null && !voices.ContainsKey(voice.Id))
                    {
                        voices[voice.Id] = voice;
                    }
                }
            }

            return voices
                .OrderBy(entry => entry.Value?.NameLocalizationKey.Localized(null) ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new KeyValuePair<MongoID, GClass3681>(entry.Key, entry.Value))
                .ToList();
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
