using Comfort.Common;
using EFT;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Utils;
using System;
using System.Reflection;
using UnityEngine;

namespace friendlySAIN.Modules
{
    internal static class SquadSideSelectionFlow
    {
        private static readonly FieldInfo AppMenuControllerField =
            AccessTools.Field(typeof(TarkovApplication), "mainMenuControllerClass");

        private static readonly MethodInfo OpenSideSelectionMethod =
            AccessTools.Method(typeof(MainMenuControllerClass), "method_44");

        private const string AlphaLabelPath = "Preloader UI/BottomPanel/Content/UpperPart/AlphaLabel";

        public static bool SquadModeActive { get; private set; }
        public static bool SuppressPlayerModelViewShow { get; private set; }
        public static bool IsOpeningSquadModeScreen { get; private set; }

        private static GameObject alphaLabelObject;
        private static bool? alphaLabelWasActive;

        public static void Open()
        {
            if (SquadModeActive)
            {
                return;
            }

            if (AppMenuControllerField == null || OpenSideSelectionMethod == null)
            {
                friendlySAIN.Log.LogWarning("[SquadFlow] MainMenuControllerClass reflection not available — cannot open side selection screen.");
                return;
            }

            TarkovApplication app = ResolveApp();
            if (app == null)
            {
                friendlySAIN.Log.LogWarning("[SquadFlow] TarkovApplication not available.");
                return;
            }

            object menuController = AppMenuControllerField.GetValue(app);
            if (menuController == null)
            {
                friendlySAIN.Log.LogWarning("[SquadFlow] MainMenuControllerClass instance is null.");
                return;
            }

            SquadModeActive = true;
            IsOpeningSquadModeScreen = true;
            HideSquadScreenAlphaLabel();

            try
            {
                OpenSideSelectionMethod.Invoke(menuController, null);
            }
            catch (Exception ex)
            {
                ShowSquadScreenAlphaLabel();
                SquadModeActive = false;
                friendlySAIN.Log.LogError("[SquadFlow] Failed to open MatchMakerSideSelectionScreen.");
                friendlySAIN.Log.LogError(ex);
            }
            finally
            {
                IsOpeningSquadModeScreen = false;
            }
        }

        public static void OnScreenClosed()
        {
            SuppressPlayerModelViewShow = false;
            ShowSquadScreenAlphaLabel();
        }

        public static void Deactivate(string reason = null)
        {
            SuppressPlayerModelViewShow = false;
            SquadModeActive = false;
            IsOpeningSquadModeScreen = false;
            ShowSquadScreenAlphaLabel();

            if (!string.IsNullOrWhiteSpace(reason))
            {
                friendlySAIN.Log.LogInfo($"[SquadFlow] Squad side-selection mode disabled: {reason}");
            }
        }

        public static void BeginPlayerModelViewSuppression()
        {
            SuppressPlayerModelViewShow = true;
        }

        public static void EndPlayerModelViewSuppression()
        {
            SuppressPlayerModelViewShow = false;
        }

        private static void HideSquadScreenAlphaLabel()
        {
            GameObject target = ResolveAlphaLabelObject();
            if (target == null)
            {
                return;
            }

            alphaLabelWasActive ??= target.activeSelf;
            target.SetActive(false);
        }

        private static void ShowSquadScreenAlphaLabel()
        {
            if (alphaLabelObject == null || !alphaLabelWasActive.HasValue)
            {
                return;
            }

            alphaLabelObject.SetActive(alphaLabelWasActive.Value);
            alphaLabelWasActive = null;
        }

        private static GameObject ResolveAlphaLabelObject()
        {
            if (alphaLabelObject != null)
            {
                return alphaLabelObject;
            }

            if (!MonoBehaviourSingleton<PreloaderUI>.Instantiated)
            {
                return null;
            }

            Transform transform = MonoBehaviourSingleton<PreloaderUI>.Instance.transform.Find(AlphaLabelPath);
            if (transform == null)
            {
                return null;
            }

            alphaLabelObject = transform.gameObject;
            return alphaLabelObject;
        }

        private static TarkovApplication ResolveApp()
        {
            if (friendlySAIN.application != null)
            {
                return friendlySAIN.application;
            }

            try
            {
                friendlySAIN.application = ClientAppUtils.GetMainApp();
            }
            catch { }

            return friendlySAIN.application;
        }
    }
}
