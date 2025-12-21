using ChatShared;
using Comfort.Common;

using EFT.InventoryLogic;
using EFT.Quests;

using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal class SocialNetworkClassPatch : ModulePatch
    {

        private static SocialNetworkClass socialNetworkClass = null;
        private static IChatInteractions iChatInteractions;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SocialNetworkClass), "method_1");
        }

        [PatchPostfix]
        private static void PatchPostfix(SocialNetworkClass __instance, IChatInteractions session, InventoryController inventoryController, string matchingVersion)
        {
            socialNetworkClass = __instance;
            iChatInteractions = session;
        }

        private static float delay = 0;

        public static void RefreshFriendsList()
        {
            if (socialNetworkClass != null && delay < Time.time)
            {
                delay = Time.time + 2;
                iChatInteractions.GetFriendsList(new Callback<GClass1023>(socialNetworkClass.method_13));
            }
        }
    }
    /** Refresh friends list whenever we send a message to the SquadManager **/
    internal class SocialNetworkClassSendPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SocialNetworkClass), "SendMessage");
        }

        [PatchPostfix]
        private static void PatchPostfix(SocialNetworkClass __instance, DialogueClass dialogue, EMessageType messageType, ChatMessageClass message, Action callback)
        {

            if (dialogue.Profile.Id == "67b0f29e151899410b04aacb")
            {
                string txt = message.Text;
                if (txt.StartsWith("/rename ") || txt.StartsWith("/delete "))
                {
                    RefreshFriendsList();
                }

                if (txt.StartsWith("/autojoin "))
                {

                    string[] parts = txt.Split(' ');
                    if (parts.Length < 3)
                    {
                        return;
                    }

                    string playerName = parts[1];
                    string state = parts[2].ToLower();

                    if (playerName == null || playerName == string.Empty)
                    {
                        return;
                    }


                    UpdatableChatMember playerToInvite = null;

                    foreach (var friend in __instance.FriendsList)
                    {
                        if (friend._info.Nickname == playerName)
                        {
                            playerToInvite = friend;
                            break;
                        }
                    }

                    if (playerToInvite == null)
                    {
                        return;
                    }

                    if (friendlySAIN.application == null)
                    {
                        try
                        {
                            friendlySAIN.application = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                        }
                        catch { }
                    }

                    if (friendlySAIN.application != null && state == "on")
                    {
                        SendInvite(playerToInvite);
                    }
                }
            }
        }

        private static async void SendInvite(UpdatableChatMember playerToInvite)
        {
            await Task.Delay(1000);

            MatchmakerPlayerControllerClass matchPlayer = friendlySAIN.application.MatchmakerPlayerControllerClass;

            bool found = false;
            foreach (var player in matchPlayer.GroupPlayers)
            {
                if (player.AccountId == playerToInvite.AccountId)
                {
                    found = true;
                    break;
                }
            }

            if (found)
            {
                return;
            }

            string json = RequestHandler.GetJson("/singleplayer/pendingauto");

            var ids = Json.Deserialize<List<string>>(json);

            if (ids.Contains(playerToInvite.AccountId))
            {
                matchPlayer.SendInvite(playerToInvite.AccountId, true, null);
            }
        }


        public static async void RefreshFriendsList()
        {
            await Task.Delay(2000);
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    /** Refresh friends list whenever we complete a quest **/
    internal class QuestClassPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(QuestClass), "SetStatus");
        }

        [PatchPostfix]
        private static void PatchPostfix(QuestClass __instance)
        {
            if (__instance.QuestStatus == EQuestStatus.Success)
            {
                SocialNetworkClassPatch.RefreshFriendsList();
            }
        }

    }
}
