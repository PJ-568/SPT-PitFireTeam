using ChatShared;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Matchmaker;
using EFT.UI.Settings;
using friendlySAIN.Modules;
using friendlySAIN.Patches;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayerIcons;
using SPT.Common.Http;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace friendlySAIN.Components
{
    internal partial class SquadControlMenuUi
    {
        private void BuildSettingsPanel()
        {
            if (settingsPanel == null)
            {
                return;
            }

            RectTransform settingsRect = settingsPanel.GetComponent<RectTransform>();
            if (settingsRect == null)
            {
                return;
            }

            float settingsShellHeight = currentRosterShellHeight > 1f
                ? currentRosterShellHeight
                : CalculateRosterShellHeight();

            settingsRect.anchorMin = new Vector2(0.5f, 0.5f);
            settingsRect.anchorMax = new Vector2(0.5f, 0.5f);
            settingsRect.pivot = new Vector2(0.5f, 0.5f);
            settingsRect.sizeDelta = new Vector2(1180f, settingsShellHeight);
            settingsRect.anchoredPosition = new Vector2(0f, -12f);

            for (int index = settingsRect.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsRect.GetChild(index).gameObject);
            }

            settingsScrollRect = null;
            settingsViewport = null;
            settingsContentRoot = null;
            settingsLayoutGroup = null;
            settingsScrollbar = null;

            CreateScrollableSettingsArea(settingsRect);
            RebuildSettingsEntries();
        }

        private void CreateScrollableSettingsArea(RectTransform panelRect)
        {
            if (TryCreateStockSettingsArea(panelRect))
            {
                return;
            }

            GameObject scrollRootObject = new GameObject("friendlySAIN_SquadControlSettingsScroll", typeof(RectTransform), typeof(ScrollRectNoDrag));
            scrollRootObject.transform.SetParent(panelRect, false);
            RectTransform scrollRoot = scrollRootObject.GetComponent<RectTransform>();
            Stretch(scrollRoot);
            scrollRoot.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
            scrollRoot.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollRoot, false);
            settingsViewport = viewportObject.GetComponent<RectTransform>();
            settingsViewport.anchorMin = Vector2.zero;
            settingsViewport.anchorMax = Vector2.one;
            settingsViewport.offsetMin = Vector2.zero;
            settingsViewport.offsetMax = new Vector2(-22f, 0f);

            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
            viewportImage.raycastTarget = true;

            settingsScrollbar = CreateRosterScrollbar(scrollRoot);
            settingsScrollbar.name = "friendlySAIN_SquadControlSettingsScrollbar";

            GameObject contentObject = new GameObject("friendlySAIN_SquadControlSettingsContent", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(settingsViewport, false);
            settingsContentRoot = contentObject.GetComponent<RectTransform>();
            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = new Vector2(0f, 0f);

            settingsLayoutGroup = contentObject.GetComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = contentObject.GetComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsScrollRect = scrollRootObject.GetComponent<ScrollRectNoDrag>();
            settingsScrollRect.horizontal = false;
            settingsScrollRect.vertical = true;
            settingsScrollRect.movementType = ScrollRect.MovementType.Clamped;
            settingsScrollRect.scrollSensitivity = 30f;
            settingsScrollRect.inertia = true;
            settingsScrollRect.viewport = settingsViewport;
            settingsScrollRect.content = settingsContentRoot;
            settingsScrollRect.verticalScrollbar = settingsScrollbar;
            settingsScrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            settingsScrollRect.verticalScrollbarSpacing = 6f;
            settingsScrollRect.AutoZeroing = true;
            settingsScrollRect.Alignment = TextAnchor.UpperLeft;
        }

        private void RebuildSettingsEntries()
        {
            if (settingsContentRoot == null)
            {
                return;
            }

            CancelShortcutCapture(false);

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsContentRoot.GetChild(index).gameObject);
            }

            string currentSection = null;
            foreach (SquadSettingEntry setting in BuildSquadSettingsEntries())
            {
                if (!string.Equals(currentSection, setting.SectionTitle, StringComparison.Ordinal))
                {
                    currentSection = setting.SectionTitle;
                    CreateSettingsSectionHeader(currentSection);
                }

                CreateSettingsEntryRow(setting.Entry);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(settingsContentRoot);
            if (settingsScrollRect != null)
            {
                settingsScrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private IEnumerable<SquadSettingEntry> BuildSquadSettingsEntries()
        {
            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.baseSettings ?? "Base Settings",
                friendlySAIN.spawnPoint,
                friendlySAIN.englishBear,
                friendlySAIN.pingRadioVolume,
                friendlySAIN.pingTime))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.followSettings ?? "Follow Settings",
                friendlySAIN.patrolRadius,
                friendlySAIN.goToDistance))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.combatSettings ?? "Combat Settings",
                friendlySAIN.botGrenades,
                friendlySAIN.enemyMarker,
                friendlySAIN.statusSound,
                friendlySAIN.enemyRemember,
                friendlySAIN.scanDistance,
                friendlySAIN.botTalk))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.inputSettings ?? "Input Settings",
                friendlySAIN.pingKey,
                friendlySAIN.contactKey,
                friendlySAIN.overThereKey))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.raidSettings ?? "Raid Settings",
                friendlySAIN.pickupEnabled,
                friendlySAIN.tieredPickup,
                friendlySAIN.maximumPickup,
                friendlySAIN.recruitPickup,
                friendlySAIN.npcSendMessage,
                friendlySAIN.friendlySAINFLAG,
                friendlySAIN.badGuy))
            {
                yield return setting;
            }

            foreach (SquadSettingEntry setting in BuildSettingsSection(
                friendlySAIN.optionsLang?.miscSettings ?? "Miscellaneous",
                friendlySAIN.teleportKey,
                friendlySAIN.healKey,
                friendlySAIN.heatlhMultiplier,
                friendlySAIN.botPrefetch))
            {
                yield return setting;
            }
        }

        private static IEnumerable<SquadSettingEntry> BuildSettingsSection(string title, params ConfigEntryBase[] entries)
        {
            return GetSettingsEntries(entries)
                .Select(entry => new SquadSettingEntry
                {
                    SectionTitle = title,
                    Entry = entry
                });
        }

        private static IEnumerable<ConfigEntryBase> GetSettingsEntries(params ConfigEntryBase[] entries)
        {
            return entries.Where(entry => entry != null);
        }

        private void CreateSettingsSectionHeader(string title)
        {
            GameObject headerObject = new GameObject($"friendlySAIN_SettingsHeader_{title}", typeof(RectTransform), typeof(LayoutElement));
            headerObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = headerObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsHeaderHeight;
            layout.flexibleWidth = 1f;

            GameObject labelObject = CreateText("Label", title.ToUpperInvariant(), 22f, TextAlignmentOptions.MidlineLeft);
            labelObject.transform.SetParent(headerObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(6f, 0f);
            labelRect.offsetMax = new Vector2(0f, -6f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.color = new Color(0.92f, 0.82f, 0.63f, 1f);
            label.fontWeight = FontWeight.Bold;
            label.fontSize = 20f;

            GameObject dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            dividerObject.transform.SetParent(headerObject.transform, false);
            RectTransform dividerRect = dividerObject.GetComponent<RectTransform>();
            dividerRect.anchorMin = new Vector2(0f, 0f);
            dividerRect.anchorMax = new Vector2(1f, 0f);
            dividerRect.pivot = new Vector2(0.5f, 0f);
            dividerRect.offsetMin = new Vector2(6f, 0f);
            dividerRect.offsetMax = new Vector2(-6f, 1f);

            Image divider = dividerObject.GetComponent<Image>();
            divider.color = new Color(0.54f, 0.45f, 0.29f, 0.45f);
            divider.raycastTarget = false;
        }

        private void CreateSettingsEntryRow(ConfigEntryBase entry)
        {
            if (entry == null)
            {
                return;
            }

            GameObject rowObject = new GameObject(
                $"friendlySAIN_Setting_{SanitizeName(entry.Definition.Key)}",
                typeof(RectTransform),
                typeof(Image),
                typeof(LayoutElement));
            rowObject.transform.SetParent(settingsContentRoot, false);

            LayoutElement layout = rowObject.GetComponent<LayoutElement>();
            layout.preferredHeight = SettingsRowHeight;
            layout.flexibleWidth = 1f;

            Image background = rowObject.GetComponent<Image>();
            background.color = new Color(0.07f, 0.07f, 0.07f, 0.84f);
            background.raycastTarget = true;

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0f, SettingsRowHeight);

            CreateSettingsRowChrome(rowObject.transform);

            GameObject nameObject = CreateText("Name", GetSettingDisplayName(entry), 22f, TextAlignmentOptions.MidlineLeft);
            nameObject.transform.SetParent(rowObject.transform, false);
            RectTransform nameRect = nameObject.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.offsetMin = new Vector2(22f, -34f);
            nameRect.offsetMax = new Vector2(-418f, -8f);

            TextMeshProUGUI nameLabel = nameObject.GetComponent<TextMeshProUGUI>();
            nameLabel.fontWeight = FontWeight.SemiBold;
            nameLabel.fontSize = 20f;

            GameObject descriptionObject = CreateText("Description", entry.Description?.Description ?? string.Empty, 16f, TextAlignmentOptions.TopLeft);
            descriptionObject.transform.SetParent(rowObject.transform, false);
            RectTransform descriptionRect = descriptionObject.GetComponent<RectTransform>();
            descriptionRect.anchorMin = new Vector2(0f, 0f);
            descriptionRect.anchorMax = new Vector2(1f, 1f);
            descriptionRect.pivot = new Vector2(0f, 1f);
            descriptionRect.offsetMin = new Vector2(22f, 16f);
            descriptionRect.offsetMax = new Vector2(-418f, -38f);

            TextMeshProUGUI descriptionLabel = descriptionObject.GetComponent<TextMeshProUGUI>();
            descriptionLabel.fontSize = 14f;
            descriptionLabel.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            descriptionLabel.enableWordWrapping = true;
            descriptionLabel.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform controlRect = new GameObject("Control", typeof(RectTransform)).GetComponent<RectTransform>();
            controlRect.SetParent(rowObject.transform, false);
            controlRect.sizeDelta = new Vector2(360f, 72f);
            controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset + controlRect.sizeDelta.x), 0f);

            if (entry.SettingType == typeof(bool))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.sizeDelta = new Vector2(120f, 72f);
                controlRect.anchoredPosition = new Vector2(-1 * (SettingsControlRightInset - 20f), 0f);
                CreateBoolSettingControl(controlRect, entry);
                return;
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                controlRect.anchoredPosition = new Vector2(-SettingsControlRightInset, SettingsSliderVerticalOffset);
                CreateIntSliderSettingControl(controlRect, entry, acceptableRange);
                return;
            }

            if (entry.SettingType == typeof(KeyboardShortcut))
            {
                controlRect.anchorMin = new Vector2(1f, 0.5f);
                controlRect.anchorMax = new Vector2(1f, 0.5f);
                controlRect.pivot = new Vector2(1f, 0.5f);
                controlRect.anchoredPosition = new Vector2(-SettingsShortcutRightInset, 0f);
                CreateShortcutSettingControl(controlRect, entry as ConfigEntry<KeyboardShortcut>);
                return;
            }

            CreateReadOnlySettingControl(controlRect, entry.BoxedValue?.ToString() ?? string.Empty);
        }

        private static void CreateSettingsRowChrome(Transform rowTransform)
        {
            GameObject topLineObject = new GameObject("TopLine", typeof(RectTransform), typeof(Image));
            topLineObject.transform.SetParent(rowTransform, false);

            RectTransform topLineRect = topLineObject.GetComponent<RectTransform>();
            topLineRect.anchorMin = new Vector2(0f, 1f);
            topLineRect.anchorMax = new Vector2(1f, 1f);
            topLineRect.pivot = new Vector2(0.5f, 1f);
            topLineRect.offsetMin = new Vector2(12f, -1f);
            topLineRect.offsetMax = new Vector2(-12f, 0f);

            Image topLine = topLineObject.GetComponent<Image>();
            topLine.color = new Color(0.68f, 0.58f, 0.38f, 0.14f);
            topLine.raycastTarget = false;

            GameObject bottomLineObject = new GameObject("BottomLine", typeof(RectTransform), typeof(Image));
            bottomLineObject.transform.SetParent(rowTransform, false);

            RectTransform bottomLineRect = bottomLineObject.GetComponent<RectTransform>();
            bottomLineRect.anchorMin = new Vector2(0f, 0f);
            bottomLineRect.anchorMax = new Vector2(1f, 0f);
            bottomLineRect.pivot = new Vector2(0.5f, 0f);
            bottomLineRect.offsetMin = new Vector2(12f, 0f);
            bottomLineRect.offsetMax = new Vector2(-12f, 1f);

            Image bottomLine = bottomLineObject.GetComponent<Image>();
            bottomLine.color = new Color(0f, 0f, 0f, 0.42f);
            bottomLine.raycastTarget = false;
        }

        private void CreateBoolSettingControl(RectTransform parent, ConfigEntryBase entry)
        {
            UpdatableToggle toggle = CloneStockToggle(parent);
            if (toggle == null)
            {
                Toggle fallbackToggle = CreateBasicToggle(parent);
                bool fallbackValue = entry.BoxedValue is bool fallbackBool && fallbackBool;
                fallbackToggle.isOn = fallbackValue;
                fallbackToggle.onValueChanged.AddListener(isOn =>
                {
                    entry.BoxedValue = isOn;
                    friendlySAIN.Instance?.Config.Save();
                });
                return;
            }

            bool currentValue = entry.BoxedValue is bool boolValue && boolValue;
            toggle.UpdateValue(currentValue, false, null, null);
            toggle.Bind(isOn =>
            {
                entry.BoxedValue = isOn;
                friendlySAIN.Instance?.Config.Save();
            });
        }

        private void CreateIntSliderSettingControl(RectTransform parent, ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange)
        {
            NumberSlider slider = CloneStockNumberSlider(parent);
            if (slider == null)
            {
                RectTransform sliderRoot = new GameObject("SliderRoot", typeof(RectTransform)).GetComponent<RectTransform>();
                sliderRoot.SetParent(parent, false);
                Stretch(sliderRoot);

                Slider fallbackSlider = CreateBasicSlider(sliderRoot, out TextMeshProUGUI valueLabel);
                fallbackSlider.minValue = acceptableRange.MinValue;
                fallbackSlider.maxValue = acceptableRange.MaxValue;
                fallbackSlider.wholeNumbers = true;
                fallbackSlider.value = Convert.ToSingle(entry.BoxedValue);
                valueLabel.text = entry.BoxedValue?.ToString() ?? "0";

                fallbackSlider.onValueChanged.AddListener(value =>
                {
                    int boxed = Mathf.RoundToInt(value);
                    if (Equals(entry.BoxedValue, boxed))
                    {
                        return;
                    }

                    entry.BoxedValue = boxed;
                    valueLabel.text = boxed.ToString();
                    friendlySAIN.Instance?.Config.Save();
                });
                return;
            }

            slider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            slider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            slider.Bind(value =>
            {
                int boxed = Mathf.RoundToInt(value);
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                friendlySAIN.Instance?.Config.Save();
            });
        }

        private void CreateShortcutSettingControl(RectTransform parent, ConfigEntry<KeyboardShortcut> entry)
        {
            if (entry == null)
            {
                CreateReadOnlySettingControl(parent, string.Empty);
                return;
            }

            Button button = CreateActionButton(parent, out TextMeshProUGUI label);
            label.text = FormatShortcut(entry.Value);
            button.onClick.AddListener(() =>
            {
                if (ReferenceEquals(activeShortcutCaptureEntry, entry))
                {
                    CancelShortcutCapture(true);
                    return;
                }

                BeginShortcutCapture(entry, button, label);
            });
        }

        private void CreateReadOnlySettingControl(RectTransform parent, string value)
        {
            GameObject valueObject = CreateText("Value", value, 18f, TextAlignmentOptions.MidlineRight);
            valueObject.transform.SetParent(parent, false);
            RectTransform valueRect = valueObject.GetComponent<RectTransform>();
            Stretch(valueRect);
        }

        private bool TryCreateStockSettingsArea(RectTransform panelRect)
        {
            ScrollRectNoDrag template = ResolveSettingsScrollRectTemplate();
            if (template == null || template.content == null || template.viewport == null)
            {
                return false;
            }

            settingsScrollRect = Instantiate(template, panelRect, false);
            settingsScrollRect.name = "friendlySAIN_SquadControlSettingsScroll";

            RectTransform scrollRect = settingsScrollRect.transform as RectTransform;
            if (scrollRect != null)
            {
                Stretch(scrollRect);
                scrollRect.offsetMin = new Vector2(SettingsViewportSideInset, SettingsViewportBottomInset);
                scrollRect.offsetMax = new Vector2(-SettingsViewportSideInset, -SettingsViewportTopInset);
                scrollRect.localScale = Vector3.one;
            }

            settingsViewport = settingsScrollRect.viewport;
            settingsContentRoot = settingsScrollRect.content;
            settingsScrollbar = settingsScrollRect.verticalScrollbar;

            if (settingsContentRoot == null)
            {
                return false;
            }

            for (int index = settingsContentRoot.childCount - 1; index >= 0; index--)
            {
                Destroy(settingsContentRoot.GetChild(index).gameObject);
            }

            settingsLayoutGroup = settingsContentRoot.GetComponent<VerticalLayoutGroup>() ?? settingsContentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            settingsLayoutGroup.spacing = SettingsSpacing;
            settingsLayoutGroup.padding = new RectOffset(0, 0, 0, 0);
            settingsLayoutGroup.childAlignment = TextAnchor.UpperLeft;
            settingsLayoutGroup.childControlWidth = true;
            settingsLayoutGroup.childControlHeight = false;
            settingsLayoutGroup.childForceExpandWidth = true;
            settingsLayoutGroup.childForceExpandHeight = false;

            ContentSizeFitter sizeFitter = settingsContentRoot.GetComponent<ContentSizeFitter>() ?? settingsContentRoot.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            settingsContentRoot.anchorMin = new Vector2(0f, 1f);
            settingsContentRoot.anchorMax = new Vector2(1f, 1f);
            settingsContentRoot.pivot = new Vector2(0.5f, 1f);
            settingsContentRoot.anchoredPosition = Vector2.zero;
            settingsContentRoot.sizeDelta = Vector2.zero;
            settingsContentRoot.localScale = Vector3.one;

            return true;
        }

        private bool CreateStockSettingsEntryRow(ConfigEntryBase entry)
        {
            if (settingsContentRoot == null)
            {
                return false;
            }

            if (entry.SettingType == typeof(bool))
            {
                return CreateStockToggleRow(entry);
            }

            if (entry.SettingType == typeof(int) && entry.Description?.AcceptableValues is AcceptableValueRange<int> acceptableRange)
            {
                return CreateStockSliderRow(entry, acceptableRange);
            }

            return false;
        }

        private bool CreateStockToggleRow(ConfigEntryBase entry)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"friendlySAIN_StockToggleRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            UpdatableToggle clonedToggle = FindComponentByPath<UpdatableToggle>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedToggle == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            clonedToggle.group = null;
            clonedToggle.UpdateValue(entry.BoxedValue is bool boolValue && boolValue, false, null, null);
            clonedToggle.Bind(isOn =>
            {
                entry.BoxedValue = isOn;
                friendlySAIN.Instance?.Config.Save();
            });

            return true;
        }

        private bool CreateStockSliderRow(ConfigEntryBase entry, AcceptableValueRange<int> acceptableRange)
        {
            NumberSlider template = ResolveSettingsSliderTemplate();
            if (template == null)
            {
                return false;
            }

            Transform rowTemplate = ResolveSettingsRowTemplateRoot(template);
            if (rowTemplate == null)
            {
                return false;
            }

            GameObject clonedRow = Instantiate(rowTemplate.gameObject, settingsContentRoot, false);
            clonedRow.name = $"friendlySAIN_StockSliderRow_{SanitizeName(entry.Definition.Key)}";
            ConfigureStockSettingsRow(clonedRow.transform as RectTransform, rowTemplate as RectTransform);

            NumberSlider clonedSlider = FindComponentByPath<NumberSlider>(rowTemplate, clonedRow.transform, template.transform);
            if (clonedSlider == null)
            {
                Destroy(clonedRow);
                return false;
            }

            ApplyStockSettingsLabelText(rowTemplate, clonedRow.transform, template.transform, GetSettingDisplayName(entry));
            clonedSlider.Show(acceptableRange.MinValue, acceptableRange.MaxValue, "0");
            clonedSlider.UpdateValue(Convert.ToSingle(entry.BoxedValue), false, acceptableRange.MinValue, acceptableRange.MaxValue);
            clonedSlider.Bind(value =>
            {
                int boxed = Mathf.RoundToInt(value);
                if (Equals(entry.BoxedValue, boxed))
                {
                    return;
                }

                entry.BoxedValue = boxed;
                friendlySAIN.Instance?.Config.Save();
            });

            return true;
        }

        private void ConfigureStockSettingsRow(RectTransform rowRect, RectTransform templateRect)
        {
            if (rowRect == null)
            {
                return;
            }

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.localScale = Vector3.one;

            LayoutElement layout = rowRect.GetComponent<LayoutElement>() ?? rowRect.gameObject.AddComponent<LayoutElement>();
            float preferredHeight = templateRect != null && templateRect.rect.height > 1f ? templateRect.rect.height : rowRect.rect.height;
            if (preferredHeight < 36f)
            {
                preferredHeight = 48f;
            }

            layout.preferredHeight = preferredHeight;
            layout.flexibleWidth = 1f;
        }

        private Transform ResolveSettingsRowTemplateRoot(Component control)
        {
            if (control == null)
            {
                return null;
            }

            Transform controlTransform = control.transform;
            Type controlType = control.GetType();
            Transform candidate = controlTransform;

            for (Transform current = controlTransform.parent; current != null; current = current.parent)
            {
                if (current.GetComponentsInChildren(controlType, true).Length != 1)
                {
                    break;
                }

                candidate = current;
            }

            return candidate;
        }

        private void ApplyStockSettingsLabelText(Transform rowTemplate, Transform clonedRow, Transform controlTransform, string text)
        {
            if (rowTemplate == null || clonedRow == null || controlTransform == null)
            {
                return;
            }

            TextMeshProUGUI[] templateLabels = rowTemplate
                .GetComponentsInChildren<TextMeshProUGUI>(true)
                .Where(label =>
                    label != null &&
                    !string.IsNullOrWhiteSpace(label.text) &&
                    !IsAncestorOf(controlTransform, label.transform))
                .ToArray();

            foreach (TextMeshProUGUI templateLabel in templateLabels)
            {
                TextMeshProUGUI clonedLabel = FindComponentByPath<TextMeshProUGUI>(rowTemplate, clonedRow, templateLabel.transform);
                if (clonedLabel != null)
                {
                    clonedLabel.text = text;
                }
            }
        }

        private UpdatableToggle CloneStockToggle(RectTransform parent)
        {
            UpdatableToggle template = ResolveSettingsToggleTemplate();
            if (template == null)
            {
                return null;
            }

            UpdatableToggle toggle = Instantiate(template, parent, false);
            toggle.name = "friendlySAIN_StockToggle";
            RectTransform rect = toggle.transform as RectTransform;
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0.5f);
                rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            toggle.gameObject.SetActive(true);
            toggle.group = null;
            HideStockLabelContainers(toggle.transform, null);
            return toggle;
        }

        private NumberSlider CloneStockNumberSlider(RectTransform parent)
        {
            RectTransform templateRoot = ResolveSettingsSliderContainerTemplate();
            if (templateRoot == null)
            {
                return null;
            }

            RectTransform sliderRoot = Instantiate(templateRoot, parent, false);
            NumberSlider slider = sliderRoot.GetComponent<NumberSlider>() ?? sliderRoot.GetComponentInChildren<NumberSlider>(true);
            if (slider == null)
            {
                Destroy(sliderRoot.gameObject);
                return null;
            }

            slider.name = "friendlySAIN_StockNumberSlider";
            if (sliderRoot != null)
            {
                Stretch(sliderRoot);
                sliderRoot.localScale = Vector3.one;
            }

            TMP_InputField valueInput = NumberSliderValueInputField?.GetValue(slider) as TMP_InputField;
            HideStockLabelContainers(sliderRoot, valueInput?.transform);

            slider.gameObject.SetActive(true);
            return slider;
        }

        private void HideStockLabelContainers(Transform root, Transform exemptRoot)
        {
            if (root == null)
            {
                return;
            }

            HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }

            foreach (Text label in root.GetComponentsInChildren<Text>(true))
            {
                HideStockLabelContainer(root, exemptRoot, label?.transform, hiddenObjects);
            }
        }

        private void HideStockLabelContainer(Transform root, Transform exemptRoot, Transform labelTransform, HashSet<GameObject> hiddenObjects)
        {
            if (root == null || labelTransform == null)
            {
                return;
            }

            if (exemptRoot != null && IsAncestorOf(exemptRoot, labelTransform))
            {
                return;
            }

            Transform directChild = ResolveDirectChildContainer(root, labelTransform);
            if (directChild == null || directChild == root || directChild == exemptRoot || (exemptRoot != null && IsAncestorOf(directChild, exemptRoot)))
            {
                return;
            }

            if (hiddenObjects.Add(directChild.gameObject))
            {
                directChild.gameObject.SetActive(false);
            }
        }

        private static Transform ResolveDirectChildContainer(Transform root, Transform descendant)
        {
            if (root == null || descendant == null)
            {
                return null;
            }

            if (ReferenceEquals(root, descendant))
            {
                return root;
            }

            Transform current = descendant;
            while (current != null && current.parent != root)
            {
                current = current.parent;
            }

            return current == null ? descendant : current;
        }

        private Toggle CreateBasicToggle(RectTransform parent)
        {
            GameObject root = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            root.transform.SetParent(parent, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(1f, 0.5f);
            rootRect.anchorMax = new Vector2(1f, 0.5f);
            rootRect.pivot = new Vector2(1f, 0.5f);
            rootRect.sizeDelta = new Vector2(92f, 38f);
            rootRect.anchoredPosition = Vector2.zero;

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(root.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            Stretch(backgroundRect);
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.18f, 0.18f, 0.18f, 1f);

            GameObject checkmarkObject = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkmarkObject.transform.SetParent(backgroundObject.transform, false);
            RectTransform checkRect = checkmarkObject.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.5f, 0.5f);
            checkRect.anchorMax = new Vector2(0.5f, 0.5f);
            checkRect.pivot = new Vector2(0.5f, 0.5f);
            checkRect.sizeDelta = new Vector2(76f, 24f);
            checkRect.anchoredPosition = Vector2.zero;
            Image checkmark = checkmarkObject.GetComponent<Image>();
            checkmark.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            Toggle toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = checkmark;
            toggle.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            toggle.colors = colors;

            return toggle;
        }

        private Slider CreateBasicSlider(RectTransform parent, out TextMeshProUGUI valueLabel)
        {
            valueLabel = CreateText("Value", "0", 18f, TextAlignmentOptions.MidlineRight).GetComponent<TextMeshProUGUI>();
            valueLabel.transform.SetParent(parent, false);
            RectTransform valueRect = valueLabel.GetComponent<RectTransform>();
            valueRect.anchorMin = new Vector2(1f, 0.5f);
            valueRect.anchorMax = new Vector2(1f, 0.5f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(52f, 26f);
            valueRect.anchoredPosition = new Vector2(0f, -20f);

            GameObject sliderRoot = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderRoot.transform.SetParent(parent, false);
            RectTransform sliderRect = sliderRoot.GetComponent<RectTransform>();
            sliderRect.anchorMin = new Vector2(0f, 0.5f);
            sliderRect.anchorMax = new Vector2(1f, 0.5f);
            sliderRect.offsetMin = new Vector2(0f, -10f);
            sliderRect.offsetMax = new Vector2(-64f, 10f);

            GameObject backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = new Vector2(0f, 0.5f);
            backgroundRect.anchorMax = new Vector2(1f, 0.5f);
            backgroundRect.sizeDelta = new Vector2(0f, 8f);
            backgroundRect.anchoredPosition = Vector2.zero;
            Image background = backgroundObject.GetComponent<Image>();
            background.color = new Color(0.16f, 0.16f, 0.16f, 1f);

            GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
            fillAreaObject.transform.SetParent(sliderRoot.transform, false);
            RectTransform fillAreaRect = fillAreaObject.GetComponent<RectTransform>();
            Stretch(fillAreaRect);
            fillAreaRect.offsetMin = new Vector2(0f, 0f);
            fillAreaRect.offsetMax = new Vector2(-18f, 0f);

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(fillAreaObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.5f);
            fillRect.anchorMax = new Vector2(1f, 0.5f);
            fillRect.sizeDelta = new Vector2(0f, 8f);
            fillRect.anchoredPosition = Vector2.zero;
            Image fillImage = fillObject.GetComponent<Image>();
            fillImage.color = new Color(0.82f, 0.59f, 0.16f, 1f);

            GameObject handleSlideArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleSlideArea.transform.SetParent(sliderRoot.transform, false);
            RectTransform slideAreaRect = handleSlideArea.GetComponent<RectTransform>();
            Stretch(slideAreaRect);
            slideAreaRect.offsetMin = new Vector2(8f, -6f);
            slideAreaRect.offsetMax = new Vector2(-8f, 6f);

            GameObject handleObject = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(handleSlideArea.transform, false);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(18f, 18f);
            Image handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.96f, 0.89f, 0.74f, 1f);

            Slider slider = sliderRoot.GetComponent<Slider>();
            slider.targetGraphic = handleImage;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.transition = Selectable.Transition.ColorTint;

            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.96f, 0.9f, 1f);
            colors.pressedColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            slider.colors = colors;

            return slider;
        }

        private Button CreateActionButton(RectTransform parent, out TextMeshProUGUI label)
        {
            GameObject buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(1f, 0.5f);
            buttonRect.anchorMax = new Vector2(1f, 0.5f);
            buttonRect.pivot = new Vector2(1f, 0.5f);
            buttonRect.sizeDelta = new Vector2(232f, 36f);
            buttonRect.anchoredPosition = Vector2.zero;

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.52f, 0.43f, 0.27f, 0.88f);
            background.raycastTarget = true;

            GameObject fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(buttonObject.transform, false);
            RectTransform fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(1f, 1f);
            fillRect.offsetMax = new Vector2(-1f, -1f);

            Image fill = fillObject.GetComponent<Image>();
            fill.color = new Color(0.12f, 0.12f, 0.12f, 0.96f);
            fill.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = fill;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.97f, 0.92f, 1f);
            colors.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.55f);
            button.colors = colors;

            GameObject labelObject = CreateText("Label", string.Empty, 18f, TextAlignmentOptions.Center);
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(12f, 0f);
            labelRect.offsetMax = new Vector2(-12f, 0f);
            label = labelObject.GetComponent<TextMeshProUGUI>();
            label.fontSize = 17f;
            label.fontWeight = FontWeight.SemiBold;
            label.color = new Color(0.93f, 0.88f, 0.77f, 1f);
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
            return button;
        }

        private void BeginShortcutCapture(ConfigEntry<KeyboardShortcut> entry, Button button, TextMeshProUGUI label)
        {
            CancelShortcutCapture(false);
            activeShortcutCaptureEntry = entry;
            activeShortcutCaptureButton = button;
            activeShortcutCaptureLabel = label;
            if (activeShortcutCaptureLabel != null)
            {
                activeShortcutCaptureLabel.text = GetSocialUiText("SettingsPressKey", "Press key...");
            }
        }

        private void CancelShortcutCapture(bool refreshLabel)
        {
            ConfigEntry<KeyboardShortcut> capturedEntry = activeShortcutCaptureEntry;
            TextMeshProUGUI capturedLabel = activeShortcutCaptureLabel;

            activeShortcutCaptureEntry = null;
            activeShortcutCaptureButton = null;
            activeShortcutCaptureLabel = null;

            if (refreshLabel && capturedEntry != null && capturedLabel != null)
            {
                capturedLabel.text = FormatShortcut(capturedEntry.Value);
            }
        }

        private void ApplyShortcutCapture(KeyboardShortcut shortcut)
        {
            if (activeShortcutCaptureEntry == null)
            {
                return;
            }

            activeShortcutCaptureEntry.Value = shortcut;
            friendlySAIN.Instance?.Config.Save();
            CancelShortcutCapture(true);
        }

        private KeyCode FindPressedMainKey()
        {
            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (IsModifierKey(keyCode))
                {
                    continue;
                }

                if (UnityEngine.Input.GetKeyDown(keyCode))
                {
                    return keyCode;
                }
            }

            return KeyCode.None;
        }

        private static bool IsModifierKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return true;
                default:
                    return false;
            }
        }

        private static KeyCode[] GetPressedModifiers()
        {
            List<KeyCode> modifiers = new List<KeyCode>(3);
            if (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl))
            {
                modifiers.Add(KeyCode.LeftControl);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift))
            {
                modifiers.Add(KeyCode.LeftShift);
            }

            if (UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt))
            {
                modifiers.Add(KeyCode.LeftAlt);
            }

            return modifiers.ToArray();
        }

        private string FormatShortcut(KeyboardShortcut shortcut)
        {
            bool hasModifiers = shortcut.Modifiers != null && shortcut.Modifiers.Any();
            if (shortcut.MainKey == KeyCode.None && !hasModifiers)
            {
                return GetSocialUiText("SettingsNotBound", "Not Bound");
            }

            shortcutBuilder.Clear();
            if (hasModifiers)
            {
                foreach (KeyCode modifier in shortcut.Modifiers)
                {
                    if (shortcutBuilder.Length > 0)
                    {
                        shortcutBuilder.Append(" + ");
                    }

                    shortcutBuilder.Append(FormatKeyCode(modifier));
                }
            }

            if (shortcut.MainKey != KeyCode.None)
            {
                if (shortcutBuilder.Length > 0)
                {
                    shortcutBuilder.Append(" + ");
                }

                shortcutBuilder.Append(FormatKeyCode(shortcut.MainKey));
            }

            return shortcutBuilder.ToString();
        }

        private static string FormatKeyCode(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.None:
                    return "None";
                default:
                    return keyCode.ToString();
            }
        }

        private static string GetSettingDisplayName(ConfigEntryBase entry)
        {
            if (entry == friendlySAIN.goToDistance)
            {
                return friendlySAIN.optionsLang?.goToDistance?["Name"] ?? "Maximum 'Go To' Distance";
            }

            string key = entry.Definition.Key ?? string.Empty;
            int index = 0;
            while (index < key.Length && (char.IsDigit(key[index]) || char.IsWhiteSpace(key[index])))
            {
                index++;
            }

            string trimmed = key.Substring(index).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? key : trimmed;
        }

        private static string SanitizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "Setting";
            }

            char[] buffer = value.ToCharArray();
            for (int index = 0; index < buffer.Length; index++)
            {
                if (!char.IsLetterOrDigit(buffer[index]))
                {
                    buffer[index] = '_';
                }
            }

            return new string(buffer);
        }

    }
}
