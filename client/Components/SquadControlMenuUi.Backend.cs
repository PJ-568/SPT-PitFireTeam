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
        private void InviteTeammateToGroup(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
            {
                friendlySAIN.Log.LogWarning($"[UI] Could not invite teammate '{entry.AccountId}': matchmaker controller unavailable.");
                return;
            }

            if (!groupRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            if (controller.IsInGroup(entry.AccountId))
            {
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                return;
            }

            TeammateAutoJoinRuntime.ClearSuppression(entry.AccountId);
            SetPortraitLoading(entry.AccountId, true);

            try
            {
                controller.SendInvite(entry.AccountId, true, null);
            }
            catch (Exception ex)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                friendlySAIN.Log.LogError($"[UI] Failed to send group invite for teammate '{entry.AccountId}'.");
                friendlySAIN.Log.LogError(ex);
                return;
            }

            if (friendlySAIN.Instance == null)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                return;
            }

            friendlySAIN.Instance.StartCoroutine(WaitForInviteResolutionCoroutine(entry.AccountId, entry.Nickname));
        }

        private void RemoveTeammateFromGroup(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
            {
                friendlySAIN.Log.LogWarning($"[UI] Could not remove teammate '{entry.AccountId}' from the group: matchmaker controller unavailable.");
                return;
            }

            if (!groupRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            if (!controller.IsInGroup(entry.AccountId))
            {
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                return;
            }

            SetPortraitLoading(entry.AccountId, true);

            bool removeTriggered;
            try
            {
                removeTriggered = TryExecuteStockRemovePlayerInteraction(controller, entry.AccountId);
            }
            catch (Exception ex)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                friendlySAIN.Log.LogError($"[UI] Failed to remove teammate '{entry.AccountId}' from the group.");
                friendlySAIN.Log.LogError(ex);
                return;
            }

            if (!removeTriggered)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                SyncGroupBadgesFromKnownState();
                friendlySAIN.Log.LogWarning($"[UI] Stock remove-player interaction was not available for teammate '{entry.AccountId}'.");
                return;
            }

            if (friendlySAIN.Instance == null)
            {
                SetPortraitLoading(entry.AccountId, false);
                groupRequestsInFlight.Remove(entry.AccountId);
                return;
            }

            friendlySAIN.Instance.StartCoroutine(WaitForGroupRemovalCoroutine(entry.AccountId, entry.Nickname));
        }

        private IEnumerator WaitForInviteResolutionCoroutine(string accountId, string nickname)
        {
            const float timeoutSeconds = 15f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            bool inviteObserved = false;
            bool joinedGroup = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) && controller != null)
                {
                    if (controller.IsInGroup(accountId))
                    {
                        joinedGroup = true;
                        break;
                    }

                    bool invitePending = controller.IsInvitedByMe(accountId);
                    if (invitePending)
                    {
                        inviteObserved = true;
                    }
                    else if (inviteObserved)
                    {
                        break;
                    }
                }

                yield return null;
            }

            SetPortraitLoading(accountId, false);
            groupRequestsInFlight.Remove(accountId);
            SyncGroupBadgesFromKnownState();

            if (joinedGroup)
            {
                string successTemplate = GetSocialUiText("SquadControlInviteAcceptedToast", "{0} joined the group.");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            string failureTemplate = GetSocialUiText("SquadControlInvitePendingFailedToast", "Group invite for {0} was not accepted.");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private IEnumerator WaitForGroupRemovalCoroutine(string accountId, string nickname)
        {
            const float timeoutSeconds = 10f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            bool removedFromGroup = false;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) && controller != null)
                {
                    if (!controller.IsInGroup(accountId))
                    {
                        removedFromGroup = true;
                        break;
                    }
                }

                yield return null;
            }

            SetPortraitLoading(accountId, false);
            groupRequestsInFlight.Remove(accountId);
            SyncGroupBadgesFromKnownState();

            if (removedFromGroup)
            {
                string successTemplate = GetSocialUiText("SquadControlRemovedFromGroupToast", "Removed {0} from the group.");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            string failureTemplate = GetSocialUiText("SquadControlRemoveFromGroupFailedToast", "Failed to remove {0} from the group.");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private bool TryExecuteStockRemovePlayerInteraction(MatchmakerPlayerControllerClass controller, string accountId)
        {
            if (controller == null || string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            GroupPlayerDataClass groupPlayer = controller.GroupPlayers?
                .FirstOrDefault(player => player?.AccountId == accountId);
            if (groupPlayer == null)
            {
                friendlySAIN.Log.LogWarning($"[UI] Could not resolve live group player data for teammate '{accountId}'.");
                return false;
            }

            ContextInteractionsClass contextInteractions = controller.GetContextInteractions(groupPlayer, true, true);
            if (contextInteractions == null)
            {
                friendlySAIN.Log.LogWarning($"[UI] Could not build stock context interactions for teammate '{accountId}'.");
                return false;
            }

            return contextInteractions.ExecuteInteraction(ERaidPlayerButton.RemovePlayer);
        }

        private void ToggleTeammateAutoJoin(SquadRosterEntry entry)
        {
            ClosePortraitContextMenu();

            if (entry == null || string.IsNullOrWhiteSpace(entry.AccountId))
            {
                return;
            }

            bool nextState = !entry.AutoJoinEnabled;
            if (!autoJoinRequestsInFlight.Add(entry.AccountId))
            {
                return;
            }

            SetPortraitLoading(entry.AccountId, true);
            friendlySAIN.Instance.StartCoroutine(ToggleTeammateAutoJoinCoroutine(entry, nextState));
        }

        private IEnumerator ToggleTeammateAutoJoinCoroutine(SquadRosterEntry entry, bool nextState)
        {
            string accountId = entry?.AccountId ?? string.Empty;
            string nickname = entry?.Nickname ?? string.Empty;
            Task<string> requestTask = null;
            string requestBody = JsonConvert.SerializeObject(new
            {
                aid = accountId,
                enabled = nextState
            });

            try
            {
                requestTask = Task.Run(() => RequestHandler.PostJson("/singleplayer/friendlysain/teammate/autojoin", requestBody));
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError($"[UI] Failed to start auto join toggle request for teammate '{accountId}'.");
                friendlySAIN.Log.LogError(ex);
            }

            if (requestTask != null)
            {
                yield return new WaitUntil(() => requestTask.IsCompleted);
            }

            bool success = false;
            string backendError = null;

            if (requestTask == null)
            {
                backendError = "Request did not start.";
            }
            else if (requestTask.IsFaulted)
            {
                backendError = requestTask.Exception?.GetBaseException().Message;
                friendlySAIN.Log.LogError($"[UI] Failed to toggle auto join for teammate '{accountId}'.");
                friendlySAIN.Log.LogError(requestTask.Exception);
            }
            else
            {
                success = TryValidateBackendSuccess(requestTask.Result, out backendError);
                if (!success && !string.IsNullOrWhiteSpace(backendError))
                {
                    friendlySAIN.Log.LogWarning($"[UI] Auto join toggle rejected for teammate '{accountId}': {backendError}");
                }
            }

            SetPortraitLoading(accountId, false);
            autoJoinRequestsInFlight.Remove(accountId);

            if (success)
            {
                if (nextState)
                {
                    TeammateAutoJoinRuntime.ClearSuppression(accountId);
                }
                else
                {
                    TeammateAutoJoinRuntime.MarkSuppressed(accountId);
                    if (!IsAccountInCurrentGroup(accountId))
                    {
                        MainMenuControllerPatch.GroupPlayers.RemoveFirst(player => player?.AccountId == accountId);
                    }
                }

                entry.AutoJoinEnabled = nextState;
                UpdateAutoJoinBadge(accountId, nextState);
                string successTemplate = GetSocialUiText(
                    nextState ? "SquadControlAutoJoinEnabledToast" : "SquadControlAutoJoinDisabledToast",
                    nextState ? "Enabled PMC raid auto-join for {0}." : "Disabled PMC raid auto-join for {0}.");
                AddTeammateCreationFlow.ShowToast(string.Format(successTemplate, nickname ?? string.Empty));
                yield break;
            }

            string failureTemplate = GetSocialUiText(
                nextState ? "SquadControlAutoJoinEnableFailedToast" : "SquadControlAutoJoinDisableFailedToast",
                nextState ? "Failed to enable auto-join for {0}" : "Failed to disable auto-join for {0}");
            AddTeammateCreationFlow.ShowToast(string.Format(failureTemplate, nickname ?? string.Empty));
        }

        private static bool TryValidateBackendSuccess(string responseJson, out string backendError)
        {
            backendError = null;

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return true;
            }

            try
            {
                BackendBodyResponse response = JsonConvert.DeserializeObject<BackendBodyResponse>(responseJson);
                if (response == null)
                {
                    backendError = "Backend returned an empty response body.";
                    return false;
                }

                if (response.err != 0)
                {
                    backendError = string.IsNullOrWhiteSpace(response.errmsg)
                        ? $"Backend returned err={response.err}."
                        : response.errmsg;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                backendError = $"Failed to parse backend response: {ex.Message}";
                return false;
            }
        }

        private bool TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller)
        {
            controller = null;

            try
            {
                if (friendlySAIN.application == null)
                {
                    friendlySAIN.application = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                }

                if (friendlySAIN.application == null)
                {
                    return false;
                }

                controller = friendlySAIN.application.MatchmakerPlayerControllerClass;
                return controller != null;
            }
            catch
            {
                controller = null;
                return false;
            }
        }

        private void EnsureGroupBadgeEventLogging()
        {
            try
            {
                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
                {
                    DetachGroupBadgeEventLogging();
                    return;
                }

                if (ReferenceEquals(subscribedGroupBadgeLogController, controller))
                {
                    return;
                }

                DetachGroupBadgeEventLogging();
                subscribedGroupBadgeLogController = controller;

                if (controller.GroupPlayers != null)
                {
                    unsubscribeGroupBadgeLog = controller.GroupPlayers.ItemsChanged.Subscribe(LogGroupBadgeItemsChanged);
                }
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI][GroupBadge] Failed to subscribe to GroupPlayers.ItemsChanged.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void DetachGroupBadgeEventLogging()
        {
            try
            {
                if (unsubscribeGroupBadgeLog != null)
                {
                    unsubscribeGroupBadgeLog();
                    unsubscribeGroupBadgeLog = null;
                }

                subscribedGroupBadgeLogController = null;
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI][GroupBadge] Failed to detach GroupPlayers.ItemsChanged subscription.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void LogGroupBadgeItemsChanged()
        {
            try
            {
                SyncGroupBadgesFromKnownState();
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI][GroupBadge] Failed while logging GroupPlayers.ItemsChanged.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void SyncGroupBadgesFromKnownState()
        {
            if (!SyncGroupBadgesFromCurrentGroup())
            {
                SyncGroupBadgesFromOpeningSnapshot();
            }
        }

        private bool SyncGroupBadgesFromCurrentGroup()
        {
            try
            {
                if (rosterGridRoot == null || !rosterGridRoot.gameObject.activeInHierarchy)
                {
                    return false;
                }

                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller?.GroupPlayers == null)
                {
                    return false;
                }

                HashSet<string> groupedIds = controller.GroupPlayers
                    .Select(player => player?.AccountId)
                    .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
                    .Select(accountId => accountId!)
                    .ToHashSet(StringComparer.Ordinal);

                ApplyGroupBadgeVisibility(groupedIds);
                return true;
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI][GroupBadge] Failed to sync badge visibility from current group.");
                friendlySAIN.Log.LogError(ex);
                return false;
            }
        }

        private void SyncGroupBadgesFromOpeningSnapshot()
        {
            try
            {
                if (rosterGridRoot == null || !rosterGridRoot.gameObject.activeInHierarchy)
                {
                    return;
                }

                HashSet<string> groupedIds = Modules.SquadSideSelectionFlow.OpeningGroupAccountIds
                    .Where(accountId => !string.IsNullOrWhiteSpace(accountId))
                    .ToHashSet(StringComparer.Ordinal);

                ApplyGroupBadgeVisibility(groupedIds);
            }
            catch (Exception ex)
            {
                friendlySAIN.Log.LogError("[UI][GroupBadge] Failed to sync badge visibility from opening snapshot.");
                friendlySAIN.Log.LogError(ex);
            }
        }

        private void ApplyGroupBadgeVisibility(HashSet<string> groupedIds)
        {
            if (rosterGridRoot == null)
            {
                return;
            }

            const string prefix = "friendlySAIN_RosterTile_";
            for (int i = 0; i < rosterGridRoot.childCount; i++)
            {
                Transform tile = rosterGridRoot.GetChild(i);
                if (tile == null || !tile.name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string accountId = tile.name.Substring(prefix.Length);
                UpdateGroupBadge(accountId, groupedIds != null && groupedIds.Contains(accountId));
            }
        }

        private bool IsAccountInCurrentGroup(string accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId))
            {
                return false;
            }

            try
            {
                if (!TryGetMatchmakerController(out MatchmakerPlayerControllerClass controller) || controller == null)
                {
                    return false;
                }

                return controller.IsInGroup(accountId)
                    || (controller.GroupPlayers != null && controller.GroupPlayers.Any(player => player?.AccountId == accountId));
            }
            catch
            {
                return false;
            }
        }

    }
}
