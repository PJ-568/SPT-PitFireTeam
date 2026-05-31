using EFT.UI.Gestures;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UI.BattleUI.Gestures;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GestureAction = EFT.UI.Gestures.GestureBaseItem.GStruct449;
using GestureMenuItem = EFT.UI.Gestures.GesturesMenu.Class3396;

namespace pitTeam.Patches
{
    internal static class CustomGestureText
    {
        public static string ViewBackpackTextUpper()
        {
            return pitFireTeam.GetGestureText("ViewBackpack").ToUpperInvariant();
        }

        public static void FitViewBackpackQuickText(CustomTextMeshProUGUI textField, GameObject? quickCommandObject = null)
        {
            if (textField == null)
            {
                return;
            }

            textField.enableWordWrapping = false;
            textField.overflowMode = TextOverflowModes.Overflow;

            float textWidth = Mathf.Max(84f, textField.GetPreferredValues(textField.text, 170f, 40f).x + 5f);
            EnsureMinWidth(textField.rectTransform, textWidth);

            LayoutElement textLayout = textField.GetComponent<LayoutElement>() ?? textField.gameObject.AddComponent<LayoutElement>();
            textLayout.minWidth = Mathf.Max(textLayout.minWidth, textWidth);
            textLayout.preferredWidth = Mathf.Max(textLayout.preferredWidth, textWidth);

            if (quickCommandObject?.transform is RectTransform quickRect)
            {
                EnsureMinWidth(quickRect, textWidth + 35f);
            }

            if (textField.transform.parent is RectTransform parentRect)
            {
                EnsureMinWidth(parentRect, textWidth);
            }
        }

        private static void EnsureMinWidth(RectTransform rectTransform, float width)
        {
            if (rectTransform == null || width <= 0f)
            {
                return;
            }

            if (rectTransform.rect.width + 0.5f < width)
            {
                rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }
        }
    }

    // Add new prhases to the menu
    internal class GestureMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "InitPhraseGroups");
        }
        [PatchPostfix]
        private static void PatchPostfix(GesturesMenu __instance)
        {
            var list_0 = (List<GesturesAudioItem>)AccessTools.Field(typeof(GesturesMenu), "list_1").GetValue(__instance);
            var list_1 = (List<GestureBaseItem>)AccessTools.Field(typeof(GesturesMenu), "list_2").GetValue(__instance);

            if (!pitFireTeam.hideUnsupportedCommands.Value) list_0.ForEach(item =>
            {
                if (item.gameObject.name == "ENEMY")
                {
                    List<EPhraseTrigger> enemyPhrases = new List<EPhraseTrigger>
                    {
                        EPhraseTrigger.OnRepeatedContact
                    };

                    enemyPhrases.ForEach(phrase =>
                    {
                        GestureMenuItem @class = new GestureMenuItem();
                        @class.gesturesMenu_0 = __instance;
                        @class.isSituational = false;
                        GestureBaseItem gestureBaseItem = item.CreateNewPhrase(phrase, false);
                        gestureBaseItem.OnPointerClicked.Subscribe(new Action<GestureAction>(@class.method_0));
                        list_1.Add(gestureBaseItem);
                    });
                }

                else if (item.gameObject.name == "TEAM STATUS")
                {
                    List<CustomPhrases> statusPhrases = new List<CustomPhrases> { CustomPhrases.TeamStatus };

                    statusPhrases.ForEach(phrase =>
                    {
                        GestureMenuItem @class = new GestureMenuItem();
                        @class.gesturesMenu_0 = __instance;
                        @class.isSituational = false;
                        GestureBaseItem gestureBaseItem = item.CreateNewPhrase((EPhraseTrigger)phrase, false);
                        gestureBaseItem.OnPointerClicked.Subscribe(new Action<GestureAction>(@class.method_0));
                        list_1.Add(gestureBaseItem);
                    });


                }
            });
        }
    }
    // Hide Unsupported Gesture Commands
    internal class CreatePhraseGroupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "CreatePhraseGroup");
        }
        [PatchPrefix]
        private static bool PatchPrefix(GesturesMenu __instance, string localizationKey, GesturesAudioItem groupTemplate, ref EPhraseTrigger[] phrases)
        {
            if (pitFireTeam.hideUnsupportedCommands.Value)
            {
                // modify phrases here
                if (localizationKey == "COMMAND")
                {
                    phrases =
                    [
                        EPhraseTrigger.Suppress,
                        EPhraseTrigger.Regroup,
                        EPhraseTrigger.HoldPosition,
                        EPhraseTrigger.Look,
                        EPhraseTrigger.Gogogo,
                        EPhraseTrigger.GoForward,
                        EPhraseTrigger.FollowMe,
                        EPhraseTrigger.CoverMe,
                        EPhraseTrigger.Stop,
                        //EPhraseTrigger.Silence,
                        EPhraseTrigger.OnYourOwn
                    ];
                }
                else if (localizationKey == "HELP")
                {
                    phrases =
                    [
                        EPhraseTrigger.NeedHelp,
                        EPhraseTrigger.NeedSniper
                    ];
                }
                else if (localizationKey == "CONTACT")
                {
                    phrases = [
                        EPhraseTrigger.RightFlank,
                        EPhraseTrigger.InTheFront,
                        EPhraseTrigger.OnSix,
                        EPhraseTrigger.LeftFlank
                    ];
                }
                else if (localizationKey == "ENEMY")
                {
                    phrases = [
                        EPhraseTrigger.OnRepeatedContact
                    ];
                }
                else if (localizationKey == "TEAM STATUS")
                {
                    phrases = [
                        (EPhraseTrigger)CustomPhrases.TeamStatus
                    ];
                }
                else if (localizationKey == "SITUATIONAL")
                {
                    phrases = [
                        EPhraseTrigger.LootKey,
                        EPhraseTrigger.LootMoney,
                        EPhraseTrigger.LootWeapon,
                        EPhraseTrigger.LootGeneric,
                        EPhraseTrigger.OpenDoor
                    ];
                }
                else if (localizationKey == "HEALTH STATUS" || localizationKey == "REACTION")
                {
                    return false;
                }

            }

            return true;
        }
    }

    internal class CreateGesturesPatch : ModulePatch
    {
        private const string OverThereSpriteFileName = "gesture_over_there.png";
        private static Sprite overThereSprite;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "method_5");
        }

        [PatchPostfix]
        private static void PatchPostfix(GesturesMenu __instance)
        {
            EInteraction gesture = (EInteraction)CustomGestures.OverThere;
            GesturesMenuItem gestureItemTemplate = (GesturesMenuItem)AccessTools.Field(typeof(GesturesMenu), "_gestureItemTemplate").GetValue(__instance);
            BaseTransformSolver gestureContainer = (BaseTransformSolver)AccessTools.Field(typeof(GesturesMenu), "_gestureContainer").GetValue(__instance);
            List<GesturesMenuItem> gestureItems = (List<GesturesMenuItem>)AccessTools.Field(typeof(GesturesMenu), "_gestureItems").GetValue(__instance);
            List<GestureBaseItem> list_2 = (List<GestureBaseItem>)AccessTools.Field(typeof(GesturesMenu), "list_2").GetValue(__instance);

            GesturesMenuItem gesturesMenuItem = global::UnityEngine.Object.Instantiate<GesturesMenuItem>(gestureItemTemplate, gestureContainer.transform);
            gestureContainer.SetChild(gesturesMenuItem.transform);
            gesturesMenuItem.gameObject.name = gesture.ToString();
            gesturesMenuItem.Gesture = gesture;

            Sprite sprite = LoadOverThereSprite();
            if (sprite != null)
            {
                gesturesMenuItem.Icon = sprite;
            }

            gesturesMenuItem.gameObject.SetActive(true);
            gestureItems.Add(gesturesMenuItem);
            list_2.Add(gesturesMenuItem);
            gesturesMenuItem.OnPointerClicked.Subscribe(new Action<GestureBaseItem.GStruct449>(__instance.method_7));
        }

        private static Sprite LoadOverThereSprite()
        {
            if (overThereSprite != null)
            {
                return overThereSprite;
            }

            string pluginDirectory = Path.GetDirectoryName(typeof(pitFireTeam).Assembly.Location) ?? string.Empty;
            string[] candidates =
            {
                Path.Combine(pluginDirectory, OverThereSpriteFileName),
                Path.Combine(pluginDirectory, "resources", OverThereSpriteFileName),
                Path.Combine(Directory.GetParent(pluginDirectory)?.FullName ?? pluginDirectory, "resources", OverThereSpriteFileName),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", OverThereSpriteFileName),
                Path.Combine(Environment.CurrentDirectory, "BepInEx", "plugins", "pitFireTeam", "resources", OverThereSpriteFileName)
            };

            string iconPath = candidates.FirstOrDefault(File.Exists);
            if (string.IsNullOrEmpty(iconPath))
            {
                pitFireTeam.Log?.LogWarning($"[UI] Over There gesture icon could not be found: {OverThereSpriteFileName}");
                return null;
            }

            byte[] fileData = File.ReadAllBytes(iconPath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(fileData))
            {
                UnityEngine.Object.Destroy(texture);
                pitFireTeam.Log?.LogWarning($"[UI] Failed to decode Over There gesture icon '{iconPath}'.");
                return null;
            }

            texture.name = "pitFireTeam_OverThereGestureIcon";
            overThereSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 200f);
            overThereSprite.name = "pitFireTeam_OverThereGestureIcon";
            return overThereSprite;
        }
    }

    internal class GestureMenuAvailablePhrasesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "Init");
        }
        [PatchPostfix]
        private static void PatchPostfix(GesturesMenu __instance)
        {
            var hashSet_1 = (HashSet<EPhraseTrigger>)AccessTools.Field(typeof(GesturesMenu), "hashSet_1").GetValue(__instance);
            hashSet_1.Add((EPhraseTrigger)CustomPhrases.TeamStatus);
            hashSet_1.Add((EPhraseTrigger)CustomPhrases.ViewBackpack);
        }
    }

    internal class ViewBackpackQuickPanelTextPatch : ModulePatch
    {
        private static readonly FieldInfo TextField = AccessTools.Field(typeof(GesturesQuickPanel), "_textField");
        private static readonly FieldInfo QuickCommandObjectField = AccessTools.Field(typeof(GesturesQuickPanel), "_quickCommandObject");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), "method_8");
        }

        [PatchPostfix]
        private static void PatchPostfix(GesturesQuickPanel __instance)
        {
            if (__instance.EPhraseTrigger_0 != (EPhraseTrigger)CustomPhrases.ViewBackpack)
            {
                return;
            }

            if (TextField.GetValue(__instance) is CustomTextMeshProUGUI textField)
            {
                textField.text = CustomGestureText.ViewBackpackTextUpper();
                CustomGestureText.FitViewBackpackQuickText(textField, QuickCommandObjectField.GetValue(__instance) as GameObject);
            }
        }
    }

    internal class ViewBackpackQuickPanelItemTextPatch : ModulePatch
    {
        private static readonly FieldInfo LabelField = AccessTools.Field(typeof(GesturesQuickPanelItem), "_label");

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanelItem), nameof(GesturesQuickPanelItem.Show));
        }

        [PatchPostfix]
        private static void PatchPostfix(GesturesQuickPanelItem __instance, EPhraseTrigger trigger)
        {
            if (trigger != (EPhraseTrigger)CustomPhrases.ViewBackpack)
            {
                return;
            }

            if (LabelField.GetValue(__instance) is CustomTextMeshProUGUI label)
            {
                label.text = CustomGestureText.ViewBackpackTextUpper();
                CustomGestureText.FitViewBackpackQuickText(label, __instance.gameObject);
            }
        }
    }

    internal class CustomPlayerGestureIntPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3937), nameof(GClass3937.IsPlayerGesture), new[] { typeof(int) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(int index, ref bool __result)
        {
            if (index != (int)(EInteraction)CustomGestures.OverThere)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    internal class CustomPlayerGestureInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3937), nameof(GClass3937.IsPlayerGesture), new[] { typeof(EInteraction) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(EInteraction gesture, ref bool __result)
        {
            if (gesture != (EInteraction)CustomGestures.OverThere)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    internal class GestureCommandNamePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3937), nameof(GClass3937.GetCommandName), new[] { typeof(int) });
        }

        [PatchPrefix]
        private static bool PatchPrefix(int index, ref string __result)
        {
            if (index == (int)EPhraseTrigger.OnRepeatedContact)
            {
                __result = GetGestureText("OnRepeatedContact");
                return false;
            }

            if (index == (int)(EPhraseTrigger)CustomPhrases.TeamStatus)
            {
                __result = GetGestureText("TeamStatus");
                return false;
            }

            if (index == (int)(EPhraseTrigger)CustomPhrases.ViewBackpack)
            {
                __result = GetGestureText("ViewBackpack");
                return false;
            }

            if (index == (int)(EInteraction)CustomGestures.OverThere)
            {
                __result = GetGestureText("OverThere");
                return false;
            }

            return true;
        }

        private static string GetGestureText(string key)
        {
            return pitFireTeam.GetGestureText(key);
        }
    }

    // patch to return friendly name for the new phrases
    internal class EPhraseTriggerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Enum), "ToString", new Type[] { });
        }
        [PatchPrefix]
        private static bool PatchPrefix(Enum __instance, ref string __result)
        {
            if (__instance is EPhraseTrigger trigger)
            {
                if (trigger == EPhraseTrigger.OnRepeatedContact)
                {
                    __result = pitFireTeam.GetGestureText("OnRepeatedContact");
                    return false;
                }
                else if (trigger == (EPhraseTrigger)CustomPhrases.TeamStatus)
                {
                    __result = pitFireTeam.GetGestureText("TeamStatus");
                    return false;
                }
                else if (trigger == (EPhraseTrigger)CustomPhrases.ViewBackpack)
                {
                    __result = pitFireTeam.GetGestureText("ViewBackpack");
                    return false;
                }
            }
            else if (__instance is EInteraction trigger2)
            {
                if (trigger2 == (EInteraction)CustomGestures.OverThere)
                {
                    __result = pitFireTeam.GetGestureText("OverThere");
                    return false;
                }
            }

            return true;
        }
    }
}
