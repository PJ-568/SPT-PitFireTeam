using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using friendlySAIN.Utils;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using dropDownItem = GClass3682;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace friendlySAIN.Patches
{
    internal class FriendlyProfileDropdownItem : dropDownItem
    {
    }

    internal class FriendlyTeammateBodyResponse<T>
    {
        public int err { get; set; }
        public string errmsg { get; set; }
        public T data { get; set; }
    }

    internal class FriendlyTeammateLoadoutOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    internal class FriendlyTeammateProfileOptions
    {
        public string CurrentLoadoutId { get; set; }
        public List<FriendlyTeammateLoadoutOption> Loadouts { get; set; }
    }

    internal class FriendlyTeammateSuitRequest
    {
        public string aid { get; set; }
        public string[] suit { get; set; }
    }

    internal class FriendlyTeammateLoadoutRequest
    {
        public string aid { get; set; }
        public string loadoutId { get; set; }
    }

    internal class FriendlyDropdownNamePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(GClass3672), "NameLocalizationKey");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3672 __instance, ref string __result)
        {
            if (OtherPlayerProfileScreenPatch.CustomDropdownIds.Contains(__instance.Id))
            {
                __result = __instance.Name;
                return false;
            }

            return true;
        }
    }

    internal class OtherPlayerProfileScreenPatch : ModulePatch
    {
        private const string OptionsRoute = "/singleplayer/friendlysain/teammate/profile/options";
        private const string SuitRoute = "/singleplayer/friendlysain/teammate/profile/suit";
        private const string LoadoutRoute = "/singleplayer/friendlysain/teammate/profile/loadout";

        private static readonly FieldInfo PlayerModelWindowField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
        private static readonly FieldInfo ClothingPanelField = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_clothingPanel");
        private static readonly FieldInfo HideoutButtonField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_hideoutButton");
        private static readonly FieldInfo ReportPanelField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_reportPanel");
        private static readonly FieldInfo UiField = AccessTools.Field(typeof(OtherPlayerProfileScreen), "UI");
        private static readonly FieldInfo UpperDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_upperButtonDropDown");
        private static readonly FieldInfo LowerDropdownField = AccessTools.Field(typeof(InventoryClothingSelectionPanel), "_lowerButtonDropDown");
        private static readonly string PluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        private static readonly string GearIconPath = Path.Combine(PluginDirectory, "gear.png");

        public static ResultProfile ViewedProfile { get; set; }
        public static Transform LoadoutSelector { get; set; }
        public static List<MongoID> CustomDropdownIds { get; } = new List<MongoID>();

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(OtherPlayerProfileScreen),
                "Show",
                new Type[] { typeof(ResultProfile), typeof(InventoryController), typeof(EItemViewType), typeof(ISession) });
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance, ResultProfile profile, InventoryController inventoryController, EItemViewType viewType, ISession session)
        {
            InventoryPlayerModelWithStatsWindow playerModelWindow =
                PlayerModelWindowField?.GetValue(__instance) as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow == null)
            {
                return;
            }

            if (session?.Profile != null
                && session.Profile.AccountId == profile.AccountId)
            {
                return;
            }

            FriendlyTeammateProfileOptions options = TryLoadProfileOptions(profile.AccountId);
            if (options == null || options.Loadouts == null || options.Loadouts.Count == 0)
            {
                friendlySAIN.Log.LogWarning($"[UI] Teammate profile patch aborted: no profile options for accountId '{profile.AccountId}'.");
                return;
            }

            if (!TryGetClothingPanel(__instance, playerModelWindow, out RectTransform clothingPanel, out InventoryClothingSelectionPanel clothingSelectionPanel, out Transform parent))
            {
                friendlySAIN.Log.LogWarning("[UI] Teammate profile patch aborted: clothing panel not found on profile screen.");
                return;
            }

            friendlySAIN.Log.LogInfo($"[UI] Applying teammate profile customization UI for '{profile.AccountId}'.");
            ViewedProfile = profile;
            playerModelWindow.OnCustomizationChanged -= PlayerModelWithStatsWindow_OnCustomizationChanged;
            playerModelWindow.OnCustomizationChanged += PlayerModelWithStatsWindow_OnCustomizationChanged;

            HideProfileActions(__instance);

            clothingPanel.gameObject.SetActive(true);
            DisplayClothingOptions(profile.PlayerVisualRepresentation, playerModelWindow, inventoryController, clothingSelectionPanel);

            AddViewListClass ui = UiField?.GetValue(__instance) as AddViewListClass;
            if (ui == null)
            {
                return;
            }

            if (LoadoutSelector != null)
            {
                GameObject.Destroy(LoadoutSelector.gameObject);
                LoadoutSelector = null;
            }

            RectTransform clone = GameObject.Instantiate(clothingPanel, parent, true);
            clone.name = "friendlySAIN_LoadoutSelector";
            clone.anchoredPosition = clothingPanel.anchoredPosition + new Vector2(0f, -72f);

            InventoryClothingSelectionPanel loadoutPanel = clone.GetComponent<InventoryClothingSelectionPanel>();
            if (loadoutPanel == null)
            {
                GameObject.Destroy(clone.gameObject);
                return;
            }

            ui.AddDisposable(loadoutPanel);
            LoadoutSelector = clone;
            ConfigureLoadoutPanel(loadoutPanel);
            DisplayLoadoutOptions(profile, inventoryController, session, loadoutPanel, playerModelWindow, options);
        }

        private static bool TryGetClothingPanel(
            OtherPlayerProfileScreen screen,
            InventoryPlayerModelWithStatsWindow playerModelWindow,
            out RectTransform clothingPanel,
            out InventoryClothingSelectionPanel clothingSelectionPanel,
            out Transform parent)
        {
            clothingPanel = null;
            clothingSelectionPanel = null;
            parent = null;

            clothingSelectionPanel = ClothingPanelField?.GetValue(playerModelWindow) as InventoryClothingSelectionPanel;
            if (clothingSelectionPanel == null)
            {
                Transform playerModelTransform = playerModelWindow.transform;
                Transform hierarchyPanel = playerModelTransform.Find("ClothingPanel")
                    ?? playerModelTransform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen.transform.Find("PlayerModelWithStats/ClothingPanel")
                    ?? screen.transform.Find("ClothingPanel");

                if (hierarchyPanel == null)
                {
                    hierarchyPanel = FindChildRecursive(playerModelTransform, "ClothingPanel")
                        ?? FindChildRecursive(screen.transform, "ClothingPanel");
                }

                clothingSelectionPanel = hierarchyPanel?.GetComponent<InventoryClothingSelectionPanel>();
            }

            clothingPanel = clothingSelectionPanel?.transform as RectTransform;
            parent = clothingPanel?.parent;
            return clothingPanel != null && clothingSelectionPanel != null && parent != null;
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
            {
                return null;
            }

            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }

                Transform nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        public static void PlayerModelWithStatsWindow_OnCustomizationChanged(dropDownItem suit)
        {
            if (ViewedProfile == null)
            {
                return;
            }

            try
            {
                string body = ViewedProfile.Customization[EBodyModelPart.Body];
                string feet = ViewedProfile.Customization[EBodyModelPart.Feet];
                RequestHandler.PostJson(SuitRoute, SerializeBody(new FriendlyTeammateSuitRequest
                {
                    aid = ViewedProfile.AccountId,
                    suit = new string[] { body, feet }
                }));
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[UI] Failed to persist teammate suit change.");
                Modules.Logger.LogError(ex);
            }
        }

        private static FriendlyTeammateProfileOptions TryLoadProfileOptions(string accountId)
        {
            try
            {
                string responseJson = RequestHandler.PostJson(OptionsRoute, SerializeBody(new { aid = accountId }));
                FriendlyTeammateBodyResponse<FriendlyTeammateProfileOptions> body =
                    JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<FriendlyTeammateProfileOptions>>(responseJson);

                if (body?.data != null)
                {
                    return body.data;
                }

                return JsonConvert.DeserializeObject<FriendlyTeammateProfileOptions>(responseJson);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to load teammate profile options for '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
                return null;
            }
        }

        private static string SerializeBody(object body)
        {
            return body.ToJson(GetDefaultJsonConverters());
        }

        private static JsonConverter[] GetDefaultJsonConverters()
        {
            Type converterClass = typeof(AbstractGame).Assembly
                .GetTypes()
                .First(type => type.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            return Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;
        }

        private static void HideProfileActions(OtherPlayerProfileScreen screen)
        {
            DefaultUIButton hideoutButton = HideoutButtonField?.GetValue(screen) as DefaultUIButton;
            hideoutButton?.gameObject.SetActive(false);

            ReportPanel reportPanel = ReportPanelField?.GetValue(screen) as ReportPanel;
            if (reportPanel != null)
            {
                reportPanel.Close();
                reportPanel.gameObject.SetActive(false);
            }
        }

        private static void ConfigureLoadoutPanel(InventoryClothingSelectionPanel panel)
        {
            DropDownBox upperDropdown = UpperDropdownField?.GetValue(panel) as DropDownBox;
            DropDownBox lowerDropdown = LowerDropdownField?.GetValue(panel) as DropDownBox;

            if (upperDropdown != null && lowerDropdown != null)
            {
                ReplaceDropdownIcon(panel.transform, "Upper/Icon", GearIconPath);
                HideDropdownIcon(panel.transform, "Lower/Icon");

                RectTransform upperRect = upperDropdown.transform as RectTransform;
                if (upperRect != null)
                {
                    upperRect.anchorMin = new Vector2(0f, upperRect.anchorMin.y);
                    upperRect.anchorMax = new Vector2(1f, upperRect.anchorMax.y);
                    upperRect.pivot = new Vector2(0.5f, upperRect.pivot.y);
                    upperRect.anchoredPosition = new Vector2(0f, upperRect.anchoredPosition.y);
                    upperRect.offsetMin = new Vector2(60f, upperRect.offsetMin.y);
                    upperRect.offsetMax = new Vector2(-14f, upperRect.offsetMax.y);
                }

                lowerDropdown.gameObject.SetActive(false);
            }
        }

        private static void ReplaceDropdownIcon(Transform parent, string childPath, string filePath)
        {
            Transform iconTransform = parent.Find(childPath);
            if (iconTransform == null)
            {
                return;
            }

            Image image = iconTransform.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                image.enabled = false;
                return;
            }

            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                image.enabled = false;
                UnityEngine.Object.Destroy(texture);
                return;
            }

            image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            image.enabled = true;
            image.rectTransform.sizeDelta = new Vector2(25f, 30f);
        }

        private static void HideDropdownIcon(Transform parent, string childPath)
        {
            Transform iconTransform = parent.Find(childPath);
            if (iconTransform == null)
            {
                return;
            }

            Image image = iconTransform.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
            }
        }

        private static void DisplayClothingOptions(
            LastPlayerStateClass playerVisualRepresentation,
            InventoryPlayerModelWithStatsWindow window,
            InventoryController inventoryController,
            InventoryClothingSelectionPanel panel
        )
        {
            InventoryPlayerModelWithStatsWindow.Class3160 state = new InventoryPlayerModelWithStatsWindow.Class3160
            {
                playerVisualRepresentation = playerVisualRepresentation,
                inventoryPlayerModelWithStatsWindow_0 = window,
                inventoryController = inventoryController
            };

            IEnumerable<dropDownItem> availableSuites =
                Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Bear)
                .Concat(Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Usec))
                .Concat(Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Savage));

            List<dropDownItem> upper = new List<dropDownItem>();
            List<dropDownItem> lower = new List<dropDownItem>();
            MongoID selectedBody = state.playerVisualRepresentation.Customization[EBodyModelPart.Body];
            MongoID selectedFeet = state.playerVisualRepresentation.Customization[EBodyModelPart.Feet];
            dropDownItem currentUpper = null;
            dropDownItem currentLower = null;

            foreach (dropDownItem suite in availableSuites)
            {
                if (suite is GClass3683 upperSuite)
                {
                    upper.Add(upperSuite);
                }
                else if (suite is GClass3684 lowerSuite)
                {
                    lower.Add(lowerSuite);
                }

                if (selectedBody == suite.MainBodyPartItem)
                {
                    currentUpper = suite;
                }
                else if (selectedFeet == suite.MainBodyPartItem)
                {
                    currentLower = suite;
                }
            }

            panel.Show(upper, currentUpper, lower, currentLower, false, state.method_0);
        }

        private static void DisplayLoadoutOptions(
            ResultProfile profile,
            InventoryController inventoryController,
            ISession session,
            InventoryClothingSelectionPanel panel,
            InventoryPlayerModelWithStatsWindow window,
            FriendlyTeammateProfileOptions options
        )
        {
            CustomDropdownIds.Clear();

            List<dropDownItem> loadoutItems = [];
            dropDownItem currentLoadout = null;
            foreach (FriendlyTeammateLoadoutOption option in options.Loadouts)
            {
                FriendlyProfileDropdownItem item = new FriendlyProfileDropdownItem
                {
                    Id = option.Id,
                    Name = option.Name
                };

                CustomDropdownIds.Add(item.Id);
                loadoutItems.Add(item);

                if (string.Equals(option.Id, options.CurrentLoadoutId, StringComparison.OrdinalIgnoreCase))
                {
                    currentLoadout = item;
                }
            }

            currentLoadout ??= loadoutItems.FirstOrDefault();
            if (currentLoadout == null)
            {
                return;
            }

            panel.Show(loadoutItems, currentLoadout, new List<dropDownItem> { currentLoadout }, currentLoadout, false, selected =>
            {
                try
                {
                    RequestHandler.PostJson(LoadoutRoute, SerializeBody(new FriendlyTeammateLoadoutRequest
                    {
                        aid = profile.AccountId,
                        loadoutId = selected.Id
                    }));

                    RefreshPlayerVisualization(profile, inventoryController, session, window);
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("[UI] Failed to persist teammate loadout change.");
                    Modules.Logger.LogError(ex);
                }
            });
        }

        private static void RefreshPlayerVisualization(ResultProfile profile, InventoryController inventoryController, ISession session, InventoryPlayerModelWithStatsWindow window)
        {
            try
            {
                Task.Run(async () =>
                {
                    Result<OtherProfileResult> result = await session.GetOtherPlayerProfile(ViewedProfile.AccountId);
                    return result;
                }).ContinueWith(task =>
                {
                    Result<OtherProfileResult> result = task.Result;
                    if (result.Failed)
                    {
                        Modules.Logger.LogError(result.Error);
                        return;
                    }

                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Head] = result.Value.Customization[EBodyModelPart.Head];

                    FieldInfo equipmentField = profile.PlayerVisualRepresentation.GetType()
                        .GetField("Equipment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    equipmentField?.SetValue(profile.PlayerVisualRepresentation, result.Value.Equipment.ToEquipment());

                    FieldInfo playerModelInfo = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_playerModelView");
                    PlayerModelView playerModelView = playerModelInfo?.GetValue(window) as PlayerModelView;
                    if (playerModelView?.gameObject.activeSelf == true)
                    {
                        playerModelView.Close();
                    }

                    window.method_3(profile.PlayerVisualRepresentation, inventoryController);
                }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal class OtherPlayerProfileScreenClosePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(OtherPlayerProfileScreen), "Close");
        }

        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance)
        {
            InventoryPlayerModelWithStatsWindow playerModelWindow =
                AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow")?.GetValue(__instance)
                as InventoryPlayerModelWithStatsWindow;
            if (playerModelWindow != null)
            {
                playerModelWindow.OnCustomizationChanged -= OtherPlayerProfileScreenPatch.PlayerModelWithStatsWindow_OnCustomizationChanged;
            }

            OtherPlayerProfileScreenPatch.ViewedProfile = null;
            OtherPlayerProfileScreenPatch.CustomDropdownIds.Clear();

            if (OtherPlayerProfileScreenPatch.LoadoutSelector != null)
            {
                GameObject.Destroy(OtherPlayerProfileScreenPatch.LoadoutSelector.gameObject);
                OtherPlayerProfileScreenPatch.LoadoutSelector = null;
            }
        }
    }
}
