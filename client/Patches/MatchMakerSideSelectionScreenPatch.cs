using EFT.UI.Matchmaker;
using EFT.UI;
using EFT.UI.Ragfair;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace friendlySAIN.Patches
{
    /// <summary>
    /// When opened in squad mode, hides all native MatchMakerSideSelectionScreen elements
    /// except the background and back button, and resets the title to "My Squad".
    /// </summary>
    internal class MatchMakerSideSelectionScreenShowPatch : ModulePatch
    {
        private static GameObject tabsOverlayRoot;
        private static Coroutine tabsOverlayCoroutine;
        private static UIAnimatedToggleSpawner tabsRosterInstance;
        private static UIAnimatedToggleSpawner tabsSettingsInstance;
        private static ToggleGroup overlayToggleGroup;

        private static readonly FieldInfo RagfairAllOffersToggleField = AccessTools.Field(typeof(RagfairScreen), "_allOffersToggle");
        private static readonly FieldInfo RagfairWishListToggleField = AccessTools.Field(typeof(RagfairScreen), "_wishListToggle");
        private static readonly FieldInfo SpawnableToggleHeaderLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_headerLabel");
        private static readonly FieldInfo SpawnableToggleSizeLabelField = AccessTools.Field(typeof(UISpawnableToggle), "_sizeLabel");
        private static readonly FieldInfo BackButtonField = AccessTools.Field(typeof(MatchMakerSideSelectionScreen), "_backButton");
        private static readonly UnityAction BackExitAction = () =>
        {
            SquadSideSelectionFlow.Deactivate("side-selection-back");
            CurrentScreenSingletonClass.Instance.TryReturnToRootScreen().HandleExceptions();
        };

        // Serialized fields to hide via reflection (elements not covered by named containers below)
        private static readonly string[] NativeHideFields =
        {
            "_nextButton",
            "_savageBlockMessage",
            "_sideDescriptionText",
            "_savageBlocker",
            "_lowHealthWarning",
            "_notFullHealthWarning",
            "_randomButton",
            "_healthPanel",
            "_randomIcon",
            "_subCaption",
            "_pmcModelView",
            "_savageModelView",
        };

        // Direct named children of the screen root to hide entirely
        private static readonly string[] NativeHideByName =
        {
            "PMCs",
            "Savage",
            "Description",
        };

        // Hierarchical child paths (relative to screen root) to hide
        private static readonly string[] NativeHideByPath =
        {
        };

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(MatchMakerSideSelectionScreen),
                "Show",
                new System.Type[] { typeof(MatchMakerSideSelectionScreen.GClass3919) });
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            if (SquadSideSelectionFlow.SquadModeActive)
            {
                SquadSideSelectionFlow.BeginPlayerModelViewSuppression();
            }
        }

        [PatchPostfix]
        private static void PatchPostfix(MatchMakerSideSelectionScreen __instance)
        {
            if (!SquadSideSelectionFlow.SquadModeActive)
            {
                return;
            }

            // Hide via reflected serialized fields
            foreach (string fieldName in NativeHideFields)
            {
                FieldInfo field = AccessTools.Field(typeof(MatchMakerSideSelectionScreen), fieldName);
                Component comp = field?.GetValue(__instance) as Component;
                if (comp != null)
                {
                    comp.gameObject.SetActive(false);
                }
            }

            // Hide whole named containers
            foreach (string childName in NativeHideByName)
            {
                Transform child = __instance.transform.Find(childName);
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                }
            }

            // Hide by hierarchical path
            foreach (string path in NativeHideByPath)
            {
                Transform child = __instance.transform.Find(path);
                if (child != null)
                {
                    child.gameObject.SetActive(false);
                }
            }

            // CaptionsHolder/MainCaption: relabel to squad title
            Transform mainCaption = __instance.transform.Find("CaptionsHolder/MainCaption");
            if (mainCaption != null)
            {
                TMP_Text label = mainCaption.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    string title = "My Squad";
                    if (friendlySAIN.optionsLang?.socialUi != null
                        && friendlySAIN.optionsLang.socialUi.TryGetValue("SquadControlButton", out string localized)
                        && !string.IsNullOrWhiteSpace(localized))
                    {
                        title = localized;
                    }
                    label.text = title;
                }
            }

            ScheduleTabsOverlay(__instance);
            WireBackButtonExit(__instance);

            SquadSideSelectionFlow.EndPlayerModelViewSuppression();
        }

        private static void ScheduleTabsOverlay(MatchMakerSideSelectionScreen screen)
        {
            if (friendlySAIN.Instance == null || screen == null)
            {
                return;
            }

            if (tabsOverlayCoroutine != null)
            {
                friendlySAIN.Instance.StopCoroutine(tabsOverlayCoroutine);
                tabsOverlayCoroutine = null;
            }

            tabsOverlayCoroutine = friendlySAIN.Instance.StartCoroutine(ShowTabsOverlayDeferred(screen));
        }

        private static IEnumerator ShowTabsOverlayDeferred(MatchMakerSideSelectionScreen screen)
        {
            // Defer one frame so side screen can finish its own Show path first.
            yield return null;

            if (screen == null || screen.gameObject == null)
            {
                tabsOverlayCoroutine = null;
                yield break;
            }

            CleanupTabsOverlay();

            UIAnimatedToggleSpawner rosterTemplate = ResolveRagfairToggle(primary: true);
            UIAnimatedToggleSpawner settingsTemplate = ResolveRagfairToggle(primary: false);

            if (rosterTemplate == null || settingsTemplate == null)
            {
                tabsOverlayCoroutine = null;
                yield break;
            }

            // Invisible root — holds the ToggleGroup so Unity can enforce mutual exclusion.
            tabsOverlayRoot = new GameObject("friendlySAIN_SideSelectionTabsOnly", typeof(RectTransform));
            tabsOverlayRoot.GetComponent<RectTransform>().SetParent(screen.transform, false);

            overlayToggleGroup = tabsOverlayRoot.AddComponent<ToggleGroup>();
            overlayToggleGroup.allowSwitchOff = false;

            tabsRosterInstance = UnityEngine.Object.Instantiate(rosterTemplate, screen.transform, false);
            tabsRosterInstance.name = "friendlySAIN_SideSelTab_Roster";
            ConfigureTabForOverlay(tabsRosterInstance, new Vector2(-135f, -112f), GetSocialUiText("SquadControlRosterTab", "Roaster"), selected: true,
                onSelected: () =>
                {
                    tabsRosterInstance?.ToggleSilently(true);
                    tabsSettingsInstance?.ToggleSilently(false);
                    Components.SquadControlMenuUi.FindInstance()?.ShowTab(true);
                });

            tabsSettingsInstance = UnityEngine.Object.Instantiate(settingsTemplate, screen.transform, false);
            tabsSettingsInstance.name = "friendlySAIN_SideSelTab_Settings";
            ConfigureTabForOverlay(tabsSettingsInstance, new Vector2(135f, -112f), GetSocialUiText("SquadControlSettingsTab", "Settings"), selected: false,
                onSelected: () =>
                {
                    tabsRosterInstance?.ToggleSilently(false);
                    tabsSettingsInstance?.ToggleSilently(true);
                    Components.SquadControlMenuUi.FindInstance()?.ShowTab(false);
                });

            Components.SquadControlMenuUi.FindInstance()?.InjectPanelsIntoScreen(screen.transform);

            tabsOverlayCoroutine = null;
        }

        private static UIAnimatedToggleSpawner ResolveRagfairToggle(bool primary)
        {
            return Resources.FindObjectsOfTypeAll<RagfairScreen>()
                .Select(s => primary
                    ? RagfairAllOffersToggleField?.GetValue(s) as UIAnimatedToggleSpawner
                    : RagfairWishListToggleField?.GetValue(s) as UIAnimatedToggleSpawner)
                .FirstOrDefault(t => t != null);
        }

        // Mirrors SquadControlMenuUi.ConfigureAnimatedTab exactly.
        private static void ConfigureTabForOverlay(UIAnimatedToggleSpawner tab, Vector2 anchoredPosition, string label, bool selected, Action onSelected)
        {
            RectTransform rect = tab.transform as RectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.localScale = Vector3.one;

            UISpawnableToggle spawnableToggle = tab.SpawnableToggle;
            spawnableToggle.method_1(overlayToggleGroup);

            if (SpawnableToggleHeaderLabelField?.GetValue(spawnableToggle) is TextMeshProUGUI headerLabel)
            {
                headerLabel.text = label.ToUpperInvariant();
            }

            if (SpawnableToggleSizeLabelField?.GetValue(spawnableToggle) is TextMeshProUGUI sizeLabel)
            {
                sizeLabel.text = label.ToUpperInvariant();
            }

            foreach (TextMeshProUGUI text in tab.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                text.text = label.ToUpperInvariant();
            }

            tab.SetActive(true);
            spawnableToggle.Interactable = true;

            if (tab.SpawnedObject != null)
            {
                tab.SpawnedObject.group = overlayToggleGroup;
                tab.SpawnedObject.interactable = true;
                tab.SpawnedObject.onValueChanged.RemoveAllListeners();
                tab.SpawnedObject.onValueChanged.AddListener(isOn =>
                {
                    if (isOn) onSelected();
                });
            }

            Components.SquadControlMenuUi.TabHoverController hoverController = tab.gameObject.GetComponent<Components.SquadControlMenuUi.TabHoverController>();
            if (hoverController == null)
            {
                hoverController = tab.gameObject.AddComponent<Components.SquadControlMenuUi.TabHoverController>();
            }

            hoverController.Configure(rect);

            tab.ToggleSilently(selected);
        }

        private static string GetSocialUiText(string key, string fallback)
        {
            if (friendlySAIN.optionsLang?.socialUi != null
                && friendlySAIN.optionsLang.socialUi.TryGetValue(key, out string value)
                && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return fallback;
        }

        private static void WireBackButtonExit(MatchMakerSideSelectionScreen screen)
        {
            DefaultUIButton backButton = BackButtonField?.GetValue(screen) as DefaultUIButton;
            if (backButton == null)
            {
                return;
            }

            // Override stock back history behavior so squad-mode back always returns to main menu.
            backButton.OnClick.RemoveAllListeners();
            backButton.OnClick.AddListener(BackExitAction);
        }

        internal static void CleanupTabsOverlay()
        {
            if (friendlySAIN.Instance != null && tabsOverlayCoroutine != null)
            {
                friendlySAIN.Instance.StopCoroutine(tabsOverlayCoroutine);
            }

            tabsOverlayCoroutine = null;
            overlayToggleGroup = null;

            Components.SquadControlMenuUi.FindInstance()?.RetractPanels();

            if (tabsOverlayRoot != null)
            {
                UnityEngine.Object.Destroy(tabsOverlayRoot);
                tabsOverlayRoot = null;
            }

            if (tabsRosterInstance != null)
            {
                UnityEngine.Object.Destroy(tabsRosterInstance.gameObject);
                tabsRosterInstance = null;
            }

            if (tabsSettingsInstance != null)
            {
                UnityEngine.Object.Destroy(tabsSettingsInstance.gameObject);
                tabsSettingsInstance = null;
            }
        }
    }

    /// <summary>
    /// Restores all hidden elements when MatchMakerSideSelectionScreen closes in squad mode.
    /// </summary>
    internal class MatchMakerSideSelectionScreenClosePatch : ModulePatch
    {
        // Fields that were hidden
        private static readonly string[] NativeHideFields =
        {
            "_nextButton",
            "_savageBlockMessage",
            "_sideDescriptionText",
            "_savageBlocker",
            "_lowHealthWarning",
            "_notFullHealthWarning",
            "_randomButton",
            "_healthPanel",
            "_randomIcon",
            "_subCaption",
            "_pmcModelView",
            "_savageModelView",
        };

        // Named children that were hidden
        private static readonly string[] NativeHideByName =
        {
            "PMCs",
            "Savage",
            "Description",
        };

        // Hierarchical paths that were hidden
        private static readonly string[] NativeHideByPath =
        {
        };

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerSideSelectionScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix(MatchMakerSideSelectionScreen __instance)
        {
            // Restore hidden fields
            foreach (string fieldName in NativeHideFields)
            {
                FieldInfo field = AccessTools.Field(typeof(MatchMakerSideSelectionScreen), fieldName);
                Component comp = field?.GetValue(__instance) as Component;
                if (comp != null)
                {
                    comp.gameObject.SetActive(true);
                }
            }

            // Restore hidden named children
            foreach (string childName in NativeHideByName)
            {
                Transform child = __instance.transform.Find(childName);
                if (child != null)
                {
                    child.gameObject.SetActive(true);
                }
            }

            // Restore hidden hierarchical children
            foreach (string path in NativeHideByPath)
            {
                Transform child = __instance.transform.Find(path);
                if (child != null)
                {
                    child.gameObject.SetActive(true);
                }
            }

            MatchMakerSideSelectionScreenShowPatch.CleanupTabsOverlay();
            SquadSideSelectionFlow.EndPlayerModelViewSuppression();
            SquadSideSelectionFlow.OnScreenClosed();
        }
    }

    internal class MainMenuControllerOpenSideSelectionGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MainMenuControllerClass), "method_44");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            // If stock flow opens side-selection while squad mode is still set,
            // clear squad mode so Play opens the normal screen, not My Squad UI.
            if (SquadSideSelectionFlow.SquadModeActive && !SquadSideSelectionFlow.IsOpeningSquadModeScreen)
            {
                SquadSideSelectionFlow.Deactivate("stock-side-selection-open");
            }
        }
    }

    internal class PlayerModelViewShowProfilePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(PlayerModelView)))
            {
                if (!string.Equals(method.Name, "Show"))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 6
                    && parameters[0].ParameterType.Name == "Profile"
                    && parameters[1].ParameterType.Name == "InventoryController")
                {
                    return method;
                }
            }

            return null;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref Task __result)
        {
            if (!SquadSideSelectionFlow.SuppressPlayerModelViewShow)
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    internal class PlayerModelViewShowLastStatePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(PlayerModelView)))
            {
                if (!string.Equals(method.Name, "Show"))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 6
                    && parameters[0].ParameterType.Name == "LastPlayerStateClass"
                    && parameters[1].ParameterType.Name == "InventoryController")
                {
                    return method;
                }
            }

            return null;
        }

        [PatchPrefix]
        private static bool PatchPrefix(ref Task __result)
        {
            if (!SquadSideSelectionFlow.SuppressPlayerModelViewShow)
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    internal class CurrentScreenTryReturnToRootScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(CurrentScreenSingletonClass), "TryReturnToRootScreen");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            if (SquadSideSelectionFlow.SquadModeActive)
            {
                SquadSideSelectionFlow.Deactivate("main-menu-root-return");
            }
        }
    }
}
