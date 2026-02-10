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
using SPT.Reflection.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using dropDownItem = GClass3682;
using OtherProfileController = EFT.UI.OtherPlayerProfileScreen.GClass3883;
using OtherProfileResult = GClass2213;
using ResultProfile = GClass1416;

namespace friendlySAIN.Patches
{

    internal class DropDownItem : dropDownItem
    {
        public string Type;
    }

    /** Patch how items name are displayed in the dropdown as we cannot rely on the game's built-in localization for all */
    [HarmonyPatch(typeof(GClass3672), "NameLocalizationKey", MethodType.Getter)]
    public static class NameLocalizationKeyPatch
    {
        static bool Prefix(GClass3672 __instance, ref string __result)
        {
            if (OtherPlayerProfileScreenPatch.EquipIds.Contains(__instance.Id))
            {
                __result = __instance.Name;
                return false;
            }

            return true;
        }
    }

    internal class OtherPlayerProfileScreenPatch : ModulePatch
    {
        public static ResultProfile viewedProfile = null;

        public static Transform equipSelector = null;

        public static Transform charsSelector = null;

        public static List<MongoID> EquipIds = new List<MongoID>();
        /**
         * Helper method to change the icon of a dropdown
         */
        private static void ReplaceIcon(Transform parent, string childPath, string filePath)
        {
            Transform imgTransform = parent.Find(childPath);
            if (imgTransform != null)
            {
                UnityEngine.UI.Image imgComponent = imgTransform.GetComponent<UnityEngine.UI.Image>();
                if (imgComponent != null)
                {
                    if (File.Exists(filePath))
                    {
                        byte[] fileData = File.ReadAllBytes(filePath);
                        Texture2D tex = new Texture2D(2, 2);
                        if (tex.LoadImage(fileData))
                        {
                            Sprite newSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 200f);
                            imgComponent.sprite = newSprite;
                            imgComponent.enabled = true;
                            imgComponent.rectTransform.sizeDelta = new Vector2(25, 30);
                        }
                        else
                        {
                            imgComponent.enabled = false;
                        }
                    }
                    else
                    {
                        imgComponent.enabled = false;
                    }
                }
            }
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(OtherPlayerProfileScreen), "Show", new System.Type[] { typeof(OtherProfileController) });
        }
        /**
         * Patch the OtherPlayerProfileScreen to show the customization dropdowns for the bot
         */
        [PatchPostfix]
        private static void PatchPostfix(OtherPlayerProfileScreen __instance, OtherProfileController controller)
        {


            var fieldInfo = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
            if (fieldInfo == null) return;

            var playerModelWithStatsWindow = (InventoryPlayerModelWithStatsWindow)fieldInfo.GetValue(__instance);
            if (playerModelWithStatsWindow == null) return;

            playerModelWithStatsWindow.OnCustomizationChanged += PlayerModelWithStatsWindow_OnCustomizationChanged;

            Transform parent = GameObject.Find("Menu UI/UI/InventoryOtherPlayerProfile/PlayerModelWithStats").transform;
            Transform clothingPanel = parent?.Find("ClothingPanel");

            if (clothingPanel != null) clothingPanel.gameObject.SetActive(false);

            if (PatchConstants.BackEndSession.Profile != null && PatchConstants.BackEndSession.Profile.AccountId == controller.Profile.AccountId)
            {
                return;
            }

            bool isFriend = false;
            foreach (var friend in PatchConstants.BackEndSession.SocialNetwork.FriendsList)
            {
                if (friend.AccountId == controller.Profile.AccountId)
                {
                    isFriend = true;
                    break;
                }
            }

            if (!isFriend) return;

            var _hideoutButton = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_hideoutButton");
            if (_hideoutButton != null)
            {
                ((DefaultUIButton)_hideoutButton.GetValue(__instance))?.gameObject.SetActive(false);
            }

            if (Utils.Props.BossFriendsIds.Contains(controller.Profile.AccountId)) return; // bosses cannot be customized

            if (clothingPanel != null)
            {   // show the clothing dropdowns for the bot
                clothingPanel.gameObject.SetActive(true);
                InventoryClothingSelectionPanel inventoryClothingSelectionPanel = clothingPanel.GetComponent<InventoryClothingSelectionPanel>();
                if (inventoryClothingSelectionPanel != null)
                {

                    viewedProfile = controller.Profile;
                }


                var UIInfo = AccessTools.Field(typeof(OtherPlayerProfileScreen), "UI");
                if (UIInfo != null)
                {
                    var UI = UIInfo.GetValue(__instance) as AddViewListClass;
                    if (UI != null)
                    {
                        // duplicate the clothing panel so we can add the tactic and equipment dropdowns
                        var clone = GameObject.Instantiate(clothingPanel, parent, true);
                        clone.name = clothingPanel.name + "_Clone";
                        clone.transform.localPosition += new Vector3(1, 0, 0);

                        InventoryClothingSelectionPanel panel = clone.GetComponent<InventoryClothingSelectionPanel>();
                        UI.AddDisposable(panel);
                        equipSelector = clone;

                        string dllPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

                        string letIconFile = Path.Combine(dllPath, "brain.png");
                        string rightIconFile = Path.Combine(dllPath, "gear.png");

                        ReplaceIcon(clone, "Upper/Icon", letIconFile);
                        ReplaceIcon(clone, "Lower/Icon", rightIconFile);

                        // duplicate again for voice and head options
                        var clone2 = GameObject.Instantiate(clothingPanel, parent, true);
                        clone2.name = clothingPanel.name + "_Clone2";
                        clone2.transform.localPosition += new Vector3(2, 0, 0);

                        InventoryClothingSelectionPanel panel2 = clone2.GetComponent<InventoryClothingSelectionPanel>();
                        UI.AddDisposable(panel2);
                        charsSelector = clone2;

                        letIconFile = Path.Combine(dllPath, "voice.png");
                        rightIconFile = Path.Combine(dllPath, "head.png");

                        ReplaceIcon(clone2, "Upper/Icon", letIconFile);
                        ReplaceIcon(clone2, "Lower/Icon", rightIconFile);


                    }
                }
                // populate available clothing options
                if (inventoryClothingSelectionPanel != null)
                {
                    DisplayClothingOptions(controller.Profile.PlayerVisualRepresentation, playerModelWithStatsWindow, controller.InventoryController, inventoryClothingSelectionPanel);
                }

                // populate available tactics, equipment, voice and head options
                if (equipSelector != null || charsSelector != null)
                {
                    EquipIds.Clear();
                    try
                    {
                        string detailsBE = RequestHandler.GetJson("/client/game/bot/followerdetails");
                        List<BotDetails> BEDetails = Json.Deserialize<List<BotDetails>>(detailsBE);

                        BotDetails currentDetails = BEDetails.FirstOrDefault(x => x.Aid == controller.Profile.AccountId);

                        var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                            .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

                        var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                        string charsBE = RequestHandler.PostJson("/client/game/bot/followercustoms", new
                        {
                            Aid = controller.Profile.AccountId

                        }.ToJson(_defaultJsonConverters));

                        BotCharacteristics BEChars = Json.Deserialize<BotCharacteristics>(charsBE);


                        if (equipSelector != null)
                            DisplayEquipmentOptions(controller, equipSelector.GetComponent<InventoryClothingSelectionPanel>(), playerModelWithStatsWindow, BEChars, currentDetails);

                        if (charsSelector != null)
                        {
                            DisplayCharacteristicsOptions(controller, charsSelector.GetComponent<InventoryClothingSelectionPanel>(), playerModelWithStatsWindow, BEChars, currentDetails);
                        }
                    }
                    catch (Exception e)
                    {
                        Modules.Logger.LogError(e);
                    }
                }
            }
        }
        /**
         * Update Bot's profile with the new customization
         */
        public static void PlayerModelWithStatsWindow_OnCustomizationChanged(dropDownItem suit)
        {
            if (viewedProfile == null) return;

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            RequestHandler.PostJson("/client/game/bot/followersuit", new
            {
                aid = viewedProfile.AccountId,
                suit = new String[] { viewedProfile.Customization[EBodyModelPart.Body], viewedProfile.Customization[EBodyModelPart.Feet] }

            }.ToJson(_defaultJsonConverters));
        }

        private static void RefershPlayerVisualization(OtherProfileController controller, InventoryPlayerModelWithStatsWindow window)
        {

            ResultProfile profile = controller.Profile;
            try
            {
                Task.Run(async () =>
                {
                    Result<OtherProfileResult> result = await PatchConstants.BackEndSession.GetOtherPlayerProfile(viewedProfile.AccountId);
                    return result;
                }).ContinueWith(r =>
                {
                    var result = r.Result;

                    FieldInfo equipmentField = typeof(LastPlayerStateClass).GetField("Equipment", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (result.Failed)
                    {
                        Modules.Logger.LogError(result.Error);
                        return;
                    }

                    profile.PlayerVisualRepresentation.Customization[EBodyModelPart.Head] = result.Value.Customization[EBodyModelPart.Head];

                    equipmentField?.SetValue(profile.PlayerVisualRepresentation, result.Value.Equipment.ToEquipment());

                    FieldInfo playerModelInfo = AccessTools.Field(typeof(InventoryPlayerModelWithStatsWindow), "_playerModelView");

                    if (playerModelInfo != null)
                    {
                        var playerModelView = playerModelInfo.GetValue(window) as PlayerModelView;

                        if (playerModelView != null)
                        {
                            if (playerModelView.gameObject.activeSelf)
                            {
                                playerModelView.Close();
                            }
                        }
                        window.method_3(profile.PlayerVisualRepresentation, controller.InventoryController);
                    }

                }, TaskScheduler.FromCurrentSynchronizationContext()).HandleExceptions();

            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }

        /*
        * Replicated and adapted from method_2 of InventoryPlayerModelWithStatsWindow
        */
        private static void DisplayClothingOptions(LastPlayerStateClass playerVisualRepresentation, InventoryPlayerModelWithStatsWindow window, InventoryController inventoryController, InventoryClothingSelectionPanel inventoryClothingSelectionPanel)
        {
            InventoryPlayerModelWithStatsWindow.Class3160 @class = new InventoryPlayerModelWithStatsWindow.Class3160();
            @class.playerVisualRepresentation = playerVisualRepresentation;
            @class.inventoryPlayerModelWithStatsWindow_0 = window;
            @class.inventoryController = inventoryController;

            IEnumerable<dropDownItem> availableSuites;

            IEnumerable<dropDownItem> availableSuitesBear = Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Bear);
            IEnumerable<dropDownItem> availableSuitesUsec = Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Usec);
            IEnumerable<dropDownItem> availableSuitesSavage = Singleton<CustomizationSolverClass>.Instance.GetAvailableSuites(EPlayerSide.Savage);
            // make all suites available, regardless of faction
            availableSuites = availableSuitesBear.Concat(availableSuitesUsec).Concat(availableSuitesSavage);

            List<dropDownItem> list = new List<dropDownItem>();
            List<dropDownItem> list2 = new List<dropDownItem>();
            MongoID mongoID = @class.playerVisualRepresentation.Customization[EBodyModelPart.Body];
            MongoID mongoID2 = @class.playerVisualRepresentation.Customization[EBodyModelPart.Feet];
            dropDownItem gclass = null;
            dropDownItem gclass2 = null;
            foreach (dropDownItem gclass3 in availableSuites)
            {
                dropDownItem gclass4 = gclass3;
                if (gclass4 != null)
                {
                    GClass3683 gclass5;
                    if ((gclass5 = gclass4 as GClass3683) == null)
                    {
                        GClass3684 gclass6;
                        if ((gclass6 = gclass4 as GClass3684) != null)
                        {
                            GClass3684 gclass7 = gclass6;
                            list2.Add(gclass7);
                        }
                    }
                    else
                    {
                        GClass3683 gclass8 = gclass5;
                        list.Add(gclass8);
                    }
                }
                if (mongoID == gclass3.MainBodyPartItem)
                {
                    gclass = gclass3;
                }
                else if (mongoID2 == gclass3.MainBodyPartItem)
                {
                    gclass2 = gclass3;
                }
            }
            inventoryClothingSelectionPanel.Show(list, gclass, list2, gclass2, false, new Action<dropDownItem>(@class.method_0));
        }

        private static void DisplayEquipmentOptions(OtherProfileController controller, InventoryClothingSelectionPanel inventoryClothingSelectionPanel, InventoryPlayerModelWithStatsWindow window, BotCharacteristics BEChars, BotDetails BEDetails)
        {
            ResultProfile profile = controller.Profile;


            dropDownItem currentTacticItem = null;
            dropDownItem currentEquipmentItem = null;

            List<dropDownItem> tactics = new List<dropDownItem>();
            List<dropDownItem> equipment = new List<dropDownItem>();

            try
            {

                foreach (var x in BEChars.Tactics)
                {
                    dropDownItem item = new dropDownItem
                    {
                        Name = x.Name,
                        Id = x.Id
                    };

                    EquipIds.Add(x.Id);
                    tactics.Add(item);

                    if (BEDetails != null && x.Tactic == BEDetails.Tactic)
                    {
                        currentTacticItem = item;
                    }
                }

                if (currentTacticItem == null)
                {
                    currentTacticItem = tactics[0];
                }

                foreach (var eq in BEChars.Equipment)
                {
                    dropDownItem item = new dropDownItem
                    {
                        Name = eq.Name,
                        Id = eq.Id
                    };

                    EquipIds.Add(eq.Id);
                    equipment.Add(item);

                    if (eq.Name == BEDetails.Equipment)
                    {
                        currentEquipmentItem = item;
                    }
                }

                if (currentEquipmentItem == null)
                {
                    currentEquipmentItem = equipment[0];
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
                return;
            }


            inventoryClothingSelectionPanel.Show(tactics, currentTacticItem, equipment, currentEquipmentItem, false,
            new Action<dropDownItem>((dropDownItem suit) =>
            {
                if (viewedProfile == null) return;

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

                var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                if (tactics.Contains(suit))
                {
                    BotTactic tacticItem = BEChars.Tactics.FirstOrDefault(x => x.Id == suit.Id);
                    string tactic = "default";
                    if (tacticItem != null)
                    {
                        tactic = tacticItem.Tactic.ToLower();
                    }
                    try
                    {
                        RequestHandler.PostJson("/client/game/bot/followertactic", new
                        {
                            aid = viewedProfile.AccountId,
                            tactic
                        }.ToJson(_defaultJsonConverters));
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError(ex);
                    }
                }
                else
                {

                    RequestHandler.PostJson("/client/game/bot/followerequip", new
                    {
                        aid = viewedProfile.AccountId,
                        equipment = suit.NameLocalizationKey
                    }.ToJson(_defaultJsonConverters));

                    RefershPlayerVisualization(controller, window);
                }
            }));
        }


        private static void DisplayCharacteristicsOptions(OtherProfileController controller, InventoryClothingSelectionPanel inventoryClothingSelectionPanel, InventoryPlayerModelWithStatsWindow window, BotCharacteristics BEChars, BotDetails currentDetails)
        {
            ResultProfile profile = controller.Profile;

            string currentVoiceId = currentDetails.Voice ?? new MongoID();
            string currentHeadId = currentDetails.Head ?? new MongoID();

            string currentVoice = "Default";
            string currentHead = "Default";
            foreach (var item in BEChars.Voices)
            {
                if (item.Id == currentVoiceId)
                {
                    currentVoice = item.Name;
                    break;
                }
            }

            foreach (var item in BEChars.Heads)
            {
                if (item.Id == currentHeadId)
                {
                    currentHead = item.Name;
                    break;
                }
            }


            List<dropDownItem> voices = new List<dropDownItem>();
            List<dropDownItem> heads = new List<dropDownItem>();

            DropDownItem currentVoiceItem = new DropDownItem
            {
                Name = currentVoice,
                Id = currentVoiceId,
                Type = "Voice"
            };
            voices.Add(currentVoiceItem);

            DropDownItem currentHeadItem = new DropDownItem
            {
                Name = currentHead,
                Id = currentHeadId,
                Type = "Head"
            };
            heads.Add(currentHeadItem);

            EquipIds.Add(currentVoiceItem.Id);
            EquipIds.Add(currentHeadItem.Id);

            foreach (var item in BEChars.Voices)
            {
                if (item.Id != currentVoiceId)
                {
                    MongoID id = item.Id;
                    EquipIds.Add(id);
                    DropDownItem voice = new DropDownItem
                    {
                        Name = item.Name,
                        Id = item.Id,
                        Type = "Voice"
                    };
                    voices.Add(voice);
                }
            }

            foreach (var item in BEChars.Heads)
            {
                if (item.Id != currentHeadId)
                {
                    MongoID id = item.Id;
                    EquipIds.Add(id);
                    DropDownItem head = new DropDownItem
                    {
                        Name = item.Name,
                        Id = item.Id,
                        Type = "Head"
                    };
                    heads.Add(head);
                }
            }


            inventoryClothingSelectionPanel.Show(voices, currentVoiceItem, heads, currentHeadItem, false, (dropDownItem suit) =>
            {
                DropDownItem dropDownItem = suit as DropDownItem;

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

                var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                RequestHandler.PostJson("/client/game/bot/followerchars", new
                {
                    Aid = viewedProfile.AccountId,
                    dropDownItem.Type,
                    dropDownItem.Id

                }.ToJson(_defaultJsonConverters));

                if (dropDownItem.Type == "Head")
                {
                    RefershPlayerVisualization(controller, window);
                }
                else
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            BotVoice vc = BEChars.Voices.Find(x => x.Id == suit.Id);

                            TagBank voice = await Singleton<GClass899>.Instance.TakeVoice(vc.Voice, EPhraseTrigger.OnMutter);
                            Modules.Logger.LogInfo("Clips length " + voice.Clips.Length);

                            int num = global::UnityEngine.Random.Range(0, voice.Clips.Length);
                            TaggedClip taggedClip = voice.Clips[num];

                            await Singleton<GUISounds>.Instance.ForcePlaySound(taggedClip.Clip);
                        }
                        catch (Exception ex)
                        {
                            Modules.Logger.LogError(ex);
                        }
                    }).HandleExceptions();
                }
            });

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
            var fieldInfo = AccessTools.Field(typeof(OtherPlayerProfileScreen), "_playerModelWithStatsWindow");
            if (fieldInfo != null)
            {
                var playerModelWithStatsWindow = (InventoryPlayerModelWithStatsWindow)fieldInfo.GetValue(__instance);

                if (playerModelWithStatsWindow != null)
                {
                    playerModelWithStatsWindow.OnCustomizationChanged -= OtherPlayerProfileScreenPatch.PlayerModelWithStatsWindow_OnCustomizationChanged;
                }
            }

            OtherPlayerProfileScreenPatch.viewedProfile = null;

            if (OtherPlayerProfileScreenPatch.equipSelector != null) GameObject.Destroy(OtherPlayerProfileScreenPatch.equipSelector.gameObject);
            OtherPlayerProfileScreenPatch.equipSelector = null;

            if (OtherPlayerProfileScreenPatch.charsSelector != null) GameObject.Destroy(OtherPlayerProfileScreenPatch.charsSelector.gameObject);
            OtherPlayerProfileScreenPatch.charsSelector = null;
        }
    }
}
