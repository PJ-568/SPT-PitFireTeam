using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.Common.Utils;
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
    internal partial class OtherPlayerProfileScreenPatch
    {
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

        private static void EnsureBodySuccess(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return;
            }

            FriendlyTeammateBodyResponse<object> body = null;
            try
            {
                body = JsonConvert.DeserializeObject<FriendlyTeammateBodyResponse<object>>(responseJson);
            }
            catch
            {
                return;
            }

            if (body != null && body.err != 0)
            {
                throw new InvalidOperationException(body.errmsg ?? "Unknown teammate backend error");
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
            ReportPanel reportPanel = ReportPanelField?.GetValue(screen) as ReportPanel;
            if (reportPanel != null)
            {
                reportPanel.Close();
                reportPanel.gameObject.SetActive(false);
            }
        }

        private static void ClearProfileRightSideContent(OtherPlayerProfileScreen screen)
        {
            HideProfileRightSideRoot(screen, screen?.transform.Find("Overall")?.gameObject);
            Transform rightSide = screen?.transform.Find("RightSide")
                ?? FindChildRecursive(screen?.transform, "RightSide");
            if (rightSide != null)
            {
                foreach (Transform child in rightSide)
                {
                    HideProfileRightSideRoot(screen, child.gameObject);
                }
            }
            HideProfileRightSideRoot(screen, OverallStatsPanelField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, AchievementsProgressBlockField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, AchievementsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, WeaponsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, NonWeaponItemsBlockPlaceholderField?.GetValue(screen) as GameObject);
            HideProfileRightSideRoot(screen, WeaponsGridLayoutGroupField?.GetValue(screen) as Component);
            HideProfileRightSideRoot(screen, NonWeaponItemsGridLayoutGroupField?.GetValue(screen) as Component);
        }

        private static void HideProfileRightSideRoot(OtherPlayerProfileScreen screen, Component component)
        {
            HideProfileRightSideRoot(screen, component?.gameObject);
        }

        private static void HideProfileRightSideRoot(OtherPlayerProfileScreen screen, GameObject target)
        {
            if (screen == null || target == null)
            {
                return;
            }

            GameObject root = ResolveProfileSectionRoot(screen.transform, target.transform);
            if (root == null || HiddenRightSideRoots.ContainsKey(root))
            {
                return;
            }

            HiddenRightSideRoots[root] = root.activeSelf;
            root.SetActive(false);
        }

        private static GameObject ResolveProfileSectionRoot(Transform screenRoot, Transform target)
        {
            if (screenRoot == null || target == null)
            {
                return null;
            }

            Transform rightSideRoot = screenRoot.Find("RightSide")
                ?? FindChildRecursive(screenRoot, "RightSide");
            if (rightSideRoot != null && target.IsChildOf(rightSideRoot))
            {
                Transform currentRightSide = target;
                while (currentRightSide.parent != null && currentRightSide.parent != rightSideRoot)
                {
                    currentRightSide = currentRightSide.parent;
                }

                return currentRightSide == rightSideRoot ? null : currentRightSide.gameObject;
            }

            Transform current = target;
            while (current.parent != null && current.parent != screenRoot)
            {
                current = current.parent;
            }

            return current == screenRoot ? null : current.gameObject;
        }

        private static void ConfigureLoadoutPanel(InventoryClothingSelectionPanel panel, InventoryClothingSelectionPanel sourcePanel)
        {
            DropDownBox upperDropdown = UpperDropdownField?.GetValue(panel) as DropDownBox;
            DropDownBox lowerDropdown = LowerDropdownField?.GetValue(panel) as DropDownBox;

            if (upperDropdown != null && lowerDropdown != null)
            {
                ReplaceDropdownIcon(panel.transform, "Upper/Icon", GearIconPath);
                ReplaceDropdownIcon(panel.transform, "Lower/Icon", BrainIconPath);
                upperDropdown.gameObject.SetActive(true);
                lowerDropdown.gameObject.SetActive(true);
            }
        }

        private static void ApplyLoadoutPanelLayout(InventoryClothingSelectionPanel panel, InventoryClothingSelectionPanel sourcePanel, bool useLowerSource = false)
        {
            ApplyDropdownLayout(
                UpperDropdownField?.GetValue(panel) as DropDownBox,
                UpperDropdownField?.GetValue(sourcePanel) as DropDownBox);
            ApplyDropdownLayout(
                LowerDropdownField?.GetValue(panel) as DropDownBox,
                LowerDropdownField?.GetValue(sourcePanel) as DropDownBox);

            ApplyIconLayout(panel.transform, "Upper/Icon", sourcePanel.transform, "Upper/Icon");
            ApplyIconLayout(panel.transform, "Lower/Icon", sourcePanel.transform, "Lower/Icon");
        }

        private static void ApplyDropdownLayout(DropDownBox targetDropdown, DropDownBox sourceDropdown)
        {
            if (targetDropdown?.transform is not RectTransform targetRect || sourceDropdown?.transform is not RectTransform sourceRect)
            {
                return;
            }

            targetRect.anchorMin = sourceRect.anchorMin;
            targetRect.anchorMax = sourceRect.anchorMax;
            targetRect.pivot = sourceRect.pivot;
            targetRect.anchoredPosition = sourceRect.anchoredPosition;
            targetRect.sizeDelta = sourceRect.sizeDelta;
            targetRect.offsetMin = sourceRect.offsetMin;
            targetRect.offsetMax = sourceRect.offsetMax;
            targetRect.localScale = sourceRect.localScale;
        }

        private static void ApplyIconLayout(Transform targetParent, string targetPath, Transform sourceParent, string sourcePath)
        {
            RectTransform targetRect = targetParent?.Find(targetPath) as RectTransform;
            RectTransform sourceRect = sourceParent?.Find(sourcePath) as RectTransform;
            if (targetRect == null || sourceRect == null)
            {
                return;
            }

            targetRect.anchorMin = sourceRect.anchorMin;
            targetRect.anchorMax = sourceRect.anchorMax;
            targetRect.pivot = sourceRect.pivot;
            targetRect.anchoredPosition = sourceRect.anchoredPosition;
            targetRect.sizeDelta = sourceRect.sizeDelta;
            targetRect.offsetMin = sourceRect.offsetMin;
            targetRect.offsetMax = sourceRect.offsetMax;
            targetRect.localScale = sourceRect.localScale;
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
            image.preserveAspect = true;
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
            HashSet<string> loadoutIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> tacticIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> tacticValueByDropdownId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            dropDownItem currentLoadout = null;
            foreach (FriendlyTeammateLoadoutOption option in options.Loadouts)
            {
                FriendlyProfileDropdownItem item = new FriendlyProfileDropdownItem
                {
                    Id = option.Id,
                    Name = option.Name
                };

                CustomDropdownIds.Add(item.Id);
                loadoutIds.Add(item.Id);
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

            ActiveTeammateLoadoutId = currentLoadout.Id;
            ActiveTeammateLoadoutName = currentLoadout.Name;

            List<dropDownItem> tacticItems = [];
            dropDownItem currentTactic = null;
            IEnumerable<FriendlyTeammateTacticOption> availableTactics =
                options.Tactics != null && options.Tactics.Count > 0
                    ? options.Tactics
                    : new[]
                    {
                        new FriendlyTeammateTacticOption { Id = "Rifleman", Name = "Rifleman" },
                        new FriendlyTeammateTacticOption { Id = "Marksman", Name = "Marksman" },
                        new FriendlyTeammateTacticOption { Id = "Protector", Name = "Protector" },
                    };

            int tacticIdSeed = 0;
            string currentTacticValue = options.CurrentTactic?.Trim() ?? string.Empty;

            foreach (FriendlyTeammateTacticOption tactic in availableTactics)
            {
                if (string.IsNullOrWhiteSpace(tactic?.Id))
                {
                    continue;
                }

                string tacticValue = tactic.Id;

                string dropdownId = $"11111111111111111111111{(tacticIdSeed++ % 16):x1}";

                FriendlyProfileDropdownItem tacticItem = new FriendlyProfileDropdownItem
                {
                    Id = dropdownId,
                    Name = GetTacticDisplayName(tacticValue)
                };

                CustomDropdownIds.Add(tacticItem.Id);
                tacticIds.Add(tacticItem.Id);
                tacticValueByDropdownId[tacticItem.Id] = tacticValue;
                tacticItems.Add(tacticItem);

                if (string.Equals(tactic.Id, currentTacticValue, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(tacticValue, currentTacticValue, StringComparison.OrdinalIgnoreCase))
                {
                    currentTactic = tacticItem;
                }
            }

            currentTactic ??= tacticItems.FirstOrDefault();
            if (currentTactic == null)
            {
                return;
            }

            panel.Show(loadoutItems, currentLoadout, tacticItems, currentTactic, false, selected =>
            {
                if (selected == null)
                {
                    return;
                }

                if (loadoutIds.Contains(selected.Id))
                {
                    try
                    {
                        string responseJson = RequestHandler.PostJson(LoadoutRoute, SerializeBody(new FriendlyTeammateLoadoutRequest
                        {
                            aid = profile.AccountId,
                            loadoutId = selected.Id
                        }));
                        EnsureBodySuccess(responseJson);

                        ActiveTeammateLoadoutId = selected.Id;
                        ActiveTeammateLoadoutName = selected.Name;
                        MarkSquadRosterDirty(profile?.AccountId);
                        RefreshPlayerVisualization(profile, inventoryController, session, window);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError("[UI] Failed to persist teammate loadout change.");
                        Modules.Logger.LogError(ex);
                    }

                    return;
                }

                if (tacticIds.Contains(selected.Id))
                {
                    try
                    {
                        if (!tacticValueByDropdownId.TryGetValue(selected.Id, out string tacticValue) || string.IsNullOrWhiteSpace(tacticValue))
                        {
                            tacticValue = "Rifleman";
                        }

                        string responseJson = RequestHandler.PostJson(TacticRoute, SerializeBody(new FriendlyTeammateTacticRequest
                        {
                            aid = profile.AccountId,
                            tactic = tacticValue
                        }));
                        EnsureBodySuccess(responseJson);
                        Modules.Logger.LogInfo($"[UI] Persisted teammate tactic '{tacticValue}' for '{profile.AccountId}'.");

                        SetAggressionRowMarksmanState(IsMarksmanTactic(tacticValue));
                        MarkSquadRosterDirty(profile?.AccountId);
                        RefreshPlayerVisualization(profile, inventoryController, session, window);
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError("[UI] Failed to persist teammate tactic change.");
                        Modules.Logger.LogError(ex);
                    }
                }
            });
        }

        private static void RefreshPlayerVisualization(ResultProfile profile, InventoryController inventoryController, ISession session, InventoryPlayerModelWithStatsWindow window)
        {
            try
            {
                Task.Run(async () =>
                {
                    Result<OtherProfileResult> result = await session.GetOtherPlayerProfile(profile.AccountId);
                    return result;
                }).ContinueWith(task =>
                {
                    Result<OtherProfileResult> result = task.Result;
                    if (result.Failed)
                    {
                        Modules.Logger.LogError(result.Error);
                        return;
                    }

                    profile.PlayerVisualRepresentation.Info.Nickname = result.Value.Info?.Nickname ?? profile.PlayerVisualRepresentation.Info.Nickname;
                    profile.PlayerVisualRepresentation.Info.Side = result.Value.Info?.Side ?? profile.PlayerVisualRepresentation.Info.Side;
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Head] = result.Value.Customization[EBodyModelPart.Head];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Body] = result.Value.Customization[EBodyModelPart.Body];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Feet] = result.Value.Customization[EBodyModelPart.Feet];
                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Hands] = result.Value.Customization[EBodyModelPart.Hands];

                    InventoryEquipment refreshedEquipment = result.Value.Equipment.ToEquipment();

                    FieldInfo equipmentField = profile.PlayerVisualRepresentation.GetType()
                        .GetField("Equipment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    equipmentField?.SetValue(profile.PlayerVisualRepresentation, refreshedEquipment);

                    PropertyInfo profileEquipmentProperty = profile.GetType()
                        .GetProperty("Equipment", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (profileEquipmentProperty?.CanWrite == true)
                    {
                        profileEquipmentProperty.SetValue(profile, refreshedEquipment);
                    }
                    else
                    {
                        FieldInfo profileEquipmentField = profile.GetType()
                            .GetField("Equipment", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        profileEquipmentField?.SetValue(profile, refreshedEquipment);
                    }

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
}
