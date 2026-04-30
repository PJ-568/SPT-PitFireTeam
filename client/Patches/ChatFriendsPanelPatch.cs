using EFT.Communications;
using EFT.UI;
using EFT.UI.Chat;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace pitTeam.Patches
{
    internal class ChatFriendsPanelAddTeammateButtonPatch : ModulePatch
    {
        private const string AddTeammateButtonName = "pitFireTeam_AddTeammateButton";
        private const string DefaultAddTeammateLabel = "+ Add teammate";

        private static readonly FieldInfo FriendsButtonField = AccessTools.Field(typeof(ChatFriendsPanel), "_friendsButton");
        private static readonly FieldInfo ButtonLabelField = AccessTools.Field(typeof(FriendsListContentButton), "_buttonLabel");
        private static readonly FieldInfo FriendsInputField = AccessTools.Field(typeof(ChatFriendsPanel), "_friendsInputField");
        private static readonly FieldInfo FriendsListPanelField = AccessTools.Field(typeof(ChatFriendsPanel), "_chatFriendsListPanel");
        private static readonly FieldInfo FriendsLabelField = AccessTools.Field(typeof(ChatFriendsListPanel), "_friendsLabel");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatFriendsPanel), "Show");
        }

        [PatchPostfix]
        private static void PatchPostfix(ChatFriendsPanel __instance)
        {
            try
            {
                EnsureAddTeammateButton(__instance);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("Failed to add Add Teammate button to ChatFriendsPanel.");
                Modules.Logger.LogError(ex);
            }
        }

        private static void EnsureAddTeammateButton(ChatFriendsPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            FriendsListContentButton templateButton = FriendsButtonField?.GetValue(panel) as FriendsListContentButton;
            if (templateButton == null)
            {
                return;
            }

            RectTransform parent = (FriendsInputField?.GetValue(panel) as Component)?.transform.parent as RectTransform
                ?? panel.transform as RectTransform;
            if (parent == null)
            {
                return;
            }

            RemoveExistingButtons(parent);
            TMP_Text templateLabel = ResolveRowLabelTemplate(panel, templateButton);

            GameObject buttonObject = CreateButtonObject(templateButton, templateLabel, parent);
            buttonObject.name = AddTeammateButtonName;

            Transform inputTransform = (FriendsInputField?.GetValue(panel) as Component)?.transform;
            if (inputTransform != null)
            {
                buttonObject.transform.SetSiblingIndex(inputTransform.GetSiblingIndex() + 1);
            }
        }

        private static TMP_Text ResolveRowLabelTemplate(ChatFriendsPanel panel, FriendsListContentButton templateButton)
        {
            ChatFriendsListPanel listPanel = FriendsListPanelField?.GetValue(panel) as ChatFriendsListPanel;
            TMP_Text rowLabel = FriendsLabelField?.GetValue(listPanel) as TMP_Text;
            if (rowLabel != null)
            {
                return rowLabel;
            }

            return ButtonLabelField?.GetValue(templateButton) as TMP_Text;
        }

        private static void RemoveExistingButtons(RectTransform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child != null && string.Equals(child.name, AddTeammateButtonName, StringComparison.Ordinal))
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static GameObject CreateButtonObject(FriendsListContentButton templateButton, TMP_Text templateLabel, RectTransform parent)
        {
            GameObject buttonObject = new GameObject(
                AddTeammateButtonName,
                typeof(RectTransform),
                typeof(LayoutElement),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);

            RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0f, 1f);
                rectTransform.anchorMax = new Vector2(1f, 1f);
                rectTransform.pivot = new Vector2(0.5f, 1f);
                rectTransform.sizeDelta = new Vector2(0f, 28f);
            }

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.preferredHeight = 28f;
                layout.minHeight = 28f;
                layout.flexibleWidth = 1f;
            }

            Image image = buttonObject.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color32(58, 58, 58, 255);
                image.type = Image.Type.Sliced;
            }

            Button button = buttonObject.GetComponent<Button>();
            if (button != null)
            {
                ColorBlock colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color32(220, 220, 220, 255);
                colors.pressedColor = new Color32(180, 180, 180, 255);
                colors.selectedColor = Color.white;
                button.colors = colors;
                button.onClick.AddListener(new UnityAction(OnAddTeammateClicked));
            }

            CreateButtonLabel(buttonObject.transform, templateLabel);

            return buttonObject;
        }

        private static void CreateButtonLabel(Transform parent, TMP_Text templateLabel)
        {
            GameObject labelObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            if (labelRect != null)
            {
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
            }

            TextMeshProUGUI createdLabel = labelObject.GetComponent<TextMeshProUGUI>();
            if (createdLabel != null)
            {
                createdLabel.text = GetLocalizedSocialUi("AddTeammate", DefaultAddTeammateLabel);
                createdLabel.enableWordWrapping = false;
                createdLabel.overflowMode = TextOverflowModes.Ellipsis;
                createdLabel.alignment = TextAlignmentOptions.MidlineLeft;
                createdLabel.fontSize = templateLabel != null ? templateLabel.fontSize : 18f;
                createdLabel.fontSizeMin = 12f;
                createdLabel.fontSizeMax = createdLabel.fontSize;
                createdLabel.enableAutoSizing = false;
                if (templateLabel != null)
                {
                    createdLabel.font = templateLabel.font;
                    createdLabel.fontSharedMaterial = templateLabel.fontSharedMaterial;
                    createdLabel.color = templateLabel.color;
                    createdLabel.characterSpacing = templateLabel.characterSpacing;
                    createdLabel.wordSpacing = templateLabel.wordSpacing;
                    createdLabel.lineSpacing = templateLabel.lineSpacing;
                }
            }
        }

        private static void OnAddTeammateClicked()
        {
            Modules.Logger.LogInfo("[UI] Add teammate clicked from friends list.");
            AddTeammateCreationFlow.Start();
        }

        private static string GetLocalizedSocialUi(string key, string fallback)
        {
            try
            {
                if (pitFireTeam.optionsLang?.socialUi != null &&
                    pitFireTeam.optionsLang.socialUi.TryGetValue(key, out string value) &&
                    !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
            catch
            {
                // Fall back to the local default if language data is not ready.
            }

            return fallback;
        }
    }
}
