using ChatShared;
using Comfort.Common;

using EFT;
using EFT.UI.Chat;
using EFT.InventoryLogic;
using EFT.Quests;
using Newtonsoft.Json.Linq;

using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal class SocialNetworkClassPatch : ModulePatch
    {
        private static SocialNetworkClass? socialNetworkClass;
        private static IChatInteractions? iChatInteractions;
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
            if (socialNetworkClass != null && iChatInteractions != null && delay < Time.time)
            {
                delay = Time.time + 2;
                iChatInteractions.GetFriendsList(new Callback<GClass1055>(socialNetworkClass.method_13));
            }
        }
    }

    internal class ChatInvitePlayersPanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatInvitePlayersPanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class ChatCreateDialoguePanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatCreateDialoguePanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class SocialNetworkClassFriendsListDedupePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SocialNetworkClass), "method_13");
        }

        [PatchPostfix]
        private static void PatchPostfix(SocialNetworkClass __instance)
        {
            if (__instance?.FriendsList == null || __instance.FriendsList.Count <= 1)
            {
                return;
            }

            List<UpdatableChatMember> deduped = new List<UpdatableChatMember>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);

            foreach (UpdatableChatMember member in __instance.FriendsList)
            {
                if (member == null)
                {
                    continue;
                }

                string key = !string.IsNullOrEmpty(member.AccountId)
                    ? $"aid:{member.AccountId}"
                    : $"id:{member.Id}";

                if (!seenKeys.Add(key))
                {
                    continue;
                }

                deduped.Add(member);
            }

            if (deduped.Count == __instance.FriendsList.Count)
            {
                return;
            }

            friendlySAIN.Log.LogInfo($"[UI] Deduped social friends list from {__instance.FriendsList.Count} to {deduped.Count} entries.");
            __instance.FriendsList.UpdateItems(deduped.ToArray());
        }
    }

    internal class TeammateTransferLeadershipButtonPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/friendlysain/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3785), nameof(GClass3785.IsActive));
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3785 __instance, EFriendInteractionButton button, ref bool __result)
        {
            if (button != EFriendInteractionButton.TransferLeadership)
            {
                return true;
            }

            UpdatableChatMember? member = __instance?.UpdatableChatMember_0;
            if (member == null)
            {
                return true;
            }

            if (!IsTeammateMember(member))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool IsTeammateMember(UpdatableChatMember member)
        {
            if (member == null || string.IsNullOrWhiteSpace(member.AccountId))
            {
                return false;
            }

            RefreshTeammateCacheIfNeeded();
            return TeammateAccountIds.Contains(member.AccountId);
        }

        private static void RefreshTeammateCacheIfNeeded()
        {
            if (Time.time < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.time + 5f;

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return;
                }

                JToken root = JToken.Parse(response);
                JToken? dataToken = root.Type == JTokenType.Array ? root : root["data"];

                if (dataToken is not JArray teammates)
                {
                    return;
                }

                TeammateAccountIds.Clear();
                foreach (JToken teammate in teammates)
                {
                    string? accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(accountId))
                    {
                        TeammateAccountIds.Add(accountId!);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh teammate cache for social context actions.");
                Modules.Logger.LogError(ex);
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


                    UpdatableChatMember? playerToInvite = null;

                    foreach (var friend in __instance.FriendsList)
                    {
                        if (friend.Info.Nickname == playerName)
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

            List<string>? ids = Json.Deserialize<List<string>>(json);

            if (ids != null && ids.Contains(playerToInvite.AccountId))
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
