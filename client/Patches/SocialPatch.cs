using ChatShared;
using Comfort.Common;

using EFT;
using EFT.UI.Chat;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using pitTeam.Components;
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
using UI.Matchmaker.Group;
using UnityEngine;

namespace pitTeam.Patches
{
    internal class SocialNetworkClassPatch : ModulePatch
    {
        private static SocialNetworkClass? socialNetworkClass;
        private static IChatInteractions? iChatInteractions;
        private static readonly MethodInfo RefreshFriendsCallbackMethod = AccessTools.Method(typeof(SocialNetworkClass), "method_13");
        private static readonly MethodInfo RefreshInputRequestsCallbackMethod = AccessTools.Method(typeof(SocialNetworkClass), "method_14");
        private static readonly MethodInfo RefreshOutputRequestsCallbackMethod = AccessTools.Method(typeof(SocialNetworkClass), "method_15");
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

        public static void RefreshFriendsList(bool force = false)
        {
            if (socialNetworkClass != null && iChatInteractions != null && (force || delay < Time.time))
            {
                delay = Time.time + (force ? 0.25f : 2f);
                iChatInteractions.GetFriendsList(new Callback<GClass1055>(result =>
                {
                    RefreshFriendsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));

                iChatInteractions.GetInputFriendsRequests(new Callback<GClass1056[]>(result =>
                {
                    RefreshInputRequestsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));

                iChatInteractions.GetOutputFriendsRequests(new Callback<GClass1056[]>(result =>
                {
                    RefreshOutputRequestsCallbackMethod?.Invoke(socialNetworkClass, new object[] { result });
                }));
            }
        }

        public static void RefreshFriendsListAndSquadRoster()
        {
            SquadControlMenuUi.RequestRosterRefreshNowOrNextInject();
            RefreshFriendsList(true);
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

    internal class ChatFriendsRequestsPanelRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ChatFriendsRequestsPanel), "Show");
        }

        [PatchPrefix]
        private static void PatchPrefix()
        {
            SocialNetworkClassPatch.RefreshFriendsList();
        }
    }

    internal class FriendRequestAcceptRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            Type acceptCallbackType = typeof(SocialNetworkClass).GetNestedType("Class1613", BindingFlags.Public | BindingFlags.NonPublic);
            return AccessTools.Method(acceptCallbackType, "method_1");
        }

        [PatchPostfix]
        private static void PatchPostfix()
        {
            SocialNetworkClassPatch.RefreshFriendsListAndSquadRoster();
        }
    }

    internal class FriendRequestAcceptAllRefreshPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(SocialNetworkClass), nameof(SocialNetworkClass.AcceptAllFriendRequests));
        }

        [PatchPostfix]
        private static async void PatchPostfix(Task __result)
        {
            try
            {
                if (__result != null)
                {
                    await __result;
                }

                SocialNetworkClassPatch.RefreshFriendsListAndSquadRoster();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh squad roster after accepting all friend requests.");
                Modules.Logger.LogError(ex);
            }
        }
    }

    internal class TeammateContextMenuButtonsPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3785), nameof(GClass3785.IsActive));
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3785 __instance, EFriendInteractionButton button, ref bool __result)
        {
            if (button == EFriendInteractionButton.WatchProfile)
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

    internal class FriendRequestProfileViewPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GClass3785), "method_15");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GClass3785 __instance)
        {
            UpdatableChatMember? profileMember = __instance?.Gclass1070_0?.Profile;
            if (profileMember == null)
            {
                return true;
            }

            string profileAccountId = profileMember.AccountId;
            if (string.IsNullOrWhiteSpace(profileAccountId) || profileAccountId == "0")
            {
                return true;
            }

            string currentAccountId = __instance.UpdatableChatMember_0?.AccountId;
            if (!string.IsNullOrWhiteSpace(currentAccountId) && currentAccountId != "0")
            {
                return true;
            }

            ItemUiContext.Instance.ShowPlayerProfileScreen(profileAccountId, EItemViewType.OtherPlayerProfile).HandleExceptions();
            return false;
        }
    }

    internal class FriendListInvitePlayerPanelPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";

        private sealed class TeammateInviteEntry
        {
            public string AccountId;
            public string Id;
            public string Nickname;
            public int Level;
            public EChatMemberSide Side;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FriendListInvitePlayerPanel), nameof(FriendListInvitePlayerPanel.Show));
        }

        [PatchPrefix]
        private static void PatchPrefix(ref GClass1628<UpdatableChatMember> friendsList)
        {
            friendsList = BuildInviteableFriendsList(friendsList);
        }

        private static GClass1628<UpdatableChatMember> BuildInviteableFriendsList(GClass1628<UpdatableChatMember> source)
        {
            List<UpdatableChatMember> filtered = new List<UpdatableChatMember>();
            HashSet<string> seenAccountIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, TeammateInviteEntry> teammatesByAccountId = LoadTeammateInviteEntries();

            if (source != null)
            {
                foreach (UpdatableChatMember member in source)
                {
                    if (!ShouldIncludeInviteMember(member))
                    {
                        continue;
                    }

                    string accountId = GetStableAccountId(member);
                    if (teammatesByAccountId.ContainsKey(accountId))
                    {
                        NormalizeInviteTeammateMember(member);
                    }

                    if (!seenAccountIds.Add(accountId))
                    {
                        continue;
                    }

                    filtered.Add(member);
                }
            }

            foreach (TeammateInviteEntry teammate in teammatesByAccountId.Values)
            {
                if (!seenAccountIds.Add(teammate.AccountId))
                {
                    continue;
                }

                UpdatableChatMember teammateMember = UpdatableChatMember.FindOrCreate(teammate.Id, static memberId => new UpdatableChatMember(memberId));
                teammateMember.AccountId = teammate.AccountId;
                teammateMember.Info.Nickname = teammate.Nickname;
                teammateMember.Info.Level = teammate.Level;
                teammateMember.Info.Side = teammate.Side;
                NormalizeInviteTeammateMember(teammateMember);
                filtered.Add(teammateMember);
            }

            return new GClass1628<UpdatableChatMember>(filtered);
        }

        private static Dictionary<string, TeammateInviteEntry> LoadTeammateInviteEntries()
        {
            Dictionary<string, TeammateInviteEntry> teammatesByAccountId = new Dictionary<string, TeammateInviteEntry>(StringComparer.Ordinal);

            try
            {
                string response = RequestHandler.GetJson(TeammatesRoute);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return teammatesByAccountId;
                }

                JToken root = JToken.Parse(response);
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];
                if (dataToken is not JArray teammates)
                {
                    return teammatesByAccountId;
                }

                foreach (JToken teammate in teammates)
                {
                    string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    string id = teammate?["Id"]?.ToString() ?? teammate?["id"]?.ToString();
                    string nickname = teammate?["Info"]?["Nickname"]?.ToString() ?? teammate?["info"]?["nickname"]?.ToString();

                    if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(nickname))
                    {
                        continue;
                    }

                    teammatesByAccountId[accountId] = new TeammateInviteEntry
                    {
                        AccountId = accountId,
                        Id = id,
                        Nickname = nickname,
                        Level = ParseInt(teammate?["Info"]?["Level"]?.ToString() ?? teammate?["info"]?["level"]?.ToString()),
                        Side = ParseSide(teammate?["Info"]?["Side"]?.ToString() ?? teammate?["info"]?["side"]?.ToString())
                    };
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to build teammate invite list.");
                Modules.Logger.LogError(ex);
            }

            return teammatesByAccountId;
        }

        private static void NormalizeInviteTeammateMember(UpdatableChatMember member)
        {
            if (member?.Info == null)
            {
                return;
            }

            member.Info.MemberCategory = EMemberCategory.Unheard;
            member.Info.SelectedMemberCategory = EMemberCategory.Unheard;
        }

        private static bool ShouldIncludeInviteMember(UpdatableChatMember member)
        {
            if (member == null)
            {
                return false;
            }

            if (member.Info == null)
            {
                return false;
            }

            if (member.Info.MemberCategory == EMemberCategory.Developer)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(GetStableAccountId(member));
        }

        private static string GetStableAccountId(UpdatableChatMember member)
        {
            return !string.IsNullOrWhiteSpace(member?.AccountId) ? member.AccountId : member?.Id ?? string.Empty;
        }

        private static int ParseInt(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : 1;
        }

        private static EChatMemberSide ParseSide(string side)
        {
            if (string.Equals(side, "Bear", StringComparison.OrdinalIgnoreCase))
            {
                return EChatMemberSide.Bear;
            }

            if (string.Equals(side, "Savage", StringComparison.OrdinalIgnoreCase))
            {
                return EChatMemberSide.Savage;
            }

            return EChatMemberSide.Usec;
        }
    }

    internal class TeammateGroupContextMenuButtonsPatch : ModulePatch
    {
        private const string TeammatesRoute = "/singleplayer/pitfireteam/teammates";
        private static readonly HashSet<string> TeammateAccountIds = new HashSet<string>(StringComparer.Ordinal);
        private static float _nextRefreshTime;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(ContextInteractionsClass), nameof(ContextInteractionsClass.IsActive));
        }

        [PatchPrefix]
        private static bool PatchPrefix(ContextInteractionsClass __instance, ERaidPlayerButton button, ref bool __result)
        {
            if (button == ERaidPlayerButton.RemovePlayer)
            {
                return true;
            }

            GroupPlayerDataClass groupMember = __instance?.GroupPlayerDataClass;
            IMatchmakerPlayersController controller = __instance?.IMatchmakerPlayersController;
            if (groupMember == null || controller == null || string.IsNullOrWhiteSpace(groupMember.AccountId))
            {
                return true;
            }

            if (!controller.IsInGroup(groupMember.AccountId))
            {
                return true;
            }

            if (!IsTeammateAccountId(groupMember.AccountId))
            {
                return true;
            }

            __result = false;
            return false;
        }

        private static bool IsTeammateAccountId(string accountId)
        {
            RefreshTeammateCacheIfNeeded();
            return TeammateAccountIds.Contains(accountId);
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
                JToken dataToken = root.Type == JTokenType.Array ? root : root["data"];

                if (dataToken is not JArray teammates)
                {
                    return;
                }

                TeammateAccountIds.Clear();
                foreach (JToken teammate in teammates)
                {
                    string accountId = teammate?["Aid"]?.ToString() ?? teammate?["aid"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(accountId))
                    {
                        TeammateAccountIds.Add(accountId);
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("[UI] Failed to refresh teammate cache for group context actions.");
                Modules.Logger.LogError(ex);
            }
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
