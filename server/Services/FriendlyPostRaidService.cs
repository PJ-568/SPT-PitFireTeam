using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;
using System.Collections.Concurrent;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyPostRaidService(
    MailSendService mailSendService,
    NotificationSendHelper notificationSendHelper,
    DialogueHelper dialogueHelper,
    FriendlyLanguageService languageService,
    FriendlyTeammateService teammateService,
    TimeUtil timeUtil,
    SaveServer saveServer,
    ISptLogger<FriendlyPostRaidService> logger
)
{
    private const string KillMessageKindTraitor = "traitor";
    private const string KillMessageKindJerk = "jerk";
    private static readonly ConcurrentDictionary<string, Dictionary<string, KillMessageRecord>> KillMessageRecords = new();

    private static readonly string[] ReturnItemsMessages =
    [
        "Items received from your teammate. Ready for you to claim.",
        "Your teammate transferred these. Awaiting your confirmation.",
        "Your items are attached. Select 'Receive all' to collect them.",
        "Transfer from your teammate completed. Items are ready to be claimed.",
        "All items from your teammate are prepared. Confirm to receive.",
        "These were handed over by your teammate. Ready for pickup.",
    ];

    private static readonly string[] TeamEscapedMessages =
    [
        "Nice! We managed to get out.",
        "And that's a wrap! We made it out.",
        "Good run. Everyone made extract.",
    ];

    private static readonly string[] TeamSomeEscapedMessages =
    [
        "Well it's a shame about {0}, but at least the rest of us made it.",
        "A few of us got clipped, but some managed to get out alive.",
    ];

    private static readonly string[] FriendlyEscapedMessages =
    [
        "Glad we made it. Thanks for letting me tag along.",
        "Whew, glad I found you. Thanks for the help.",
        "Thanks for the help. I hauled what I could back.",
    ];

    public void HandleReturnItems(MongoId sessionId, FriendlyPostRaidReturnItemsRequest request)
    {
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> items = request.Items ?? [];
        if (items.Count == 0)
        {
            return;
        }

        RemoveReturnedItemsFromInsurance(sessionId, items);

        UserDialogInfo sender = GetDeliverySender();
        SendMessageDetails details = new()
        {
            RecipientId = sessionId,
            Sender = MessageType.NpcTraderMessage,
            DialogType = MessageType.NpcTraderMessage,
            SenderDetails = sender,
            Trader = FriendlyCourierTraderProfile.CourierTraderIdValue,
            MessageText = PickRandom(ReturnItemsMessages),
            Items = items,
            ItemsMaxStorageLifetimeSeconds = 86400,
        };

        mailSendService.SendMessageToPlayer(details);
        EnsureDialogHasSender(sessionId, sender);
    }

    private void RemoveReturnedItemsFromInsurance(MongoId sessionId, List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> returnedItems)
    {
        HashSet<MongoId> returnedItemIds = returnedItems
            .Where(item => item != null)
            .Select(item => item.Id)
            .ToHashSet();
        if (returnedItemIds.Count == 0)
        {
            return;
        }

        try
        {
            SptProfile profile = saveServer.GetProfile(sessionId);
            PmcData? pmcData = profile?.CharacterData?.PmcData;
            int activeInsuranceRemoved = 0;
            int scheduledInsuranceRemoved = 0;

            // Match SPT's BTR delivery behavior: once an item is returned by a delivery service,
            // remove its insurance marker so post-raid insurance cannot schedule a duplicate copy.
            if (pmcData?.InsuredItems != null)
            {
                int beforeCount = pmcData.InsuredItems.Count;
                pmcData.InsuredItems = pmcData.InsuredItems
                    .Where(insuredItem => insuredItem?.ItemId == null || !returnedItemIds.Contains(insuredItem.ItemId.Value))
                    .ToList();
                activeInsuranceRemoved = beforeCount - pmcData.InsuredItems.Count;
            }

            if (profile?.InsuranceList != null)
            {
                foreach (SPTarkov.Server.Core.Models.Eft.Profile.Insurance insurancePackage in profile.InsuranceList.Where(package => package?.Items != null))
                {
                    HashSet<MongoId> packageRemovalIds = BuildReturnedInsuranceRemovalIds(insurancePackage.Items!, returnedItemIds);
                    if (packageRemovalIds.Count == 0)
                    {
                        continue;
                    }

                    int beforeCount = insurancePackage.Items!.Count;
                    insurancePackage.Items = insurancePackage.Items
                        .Where(item => item != null && !packageRemovalIds.Contains(item.Id))
                        .ToList();
                    scheduledInsuranceRemoved += beforeCount - insurancePackage.Items.Count;
                }
            }

            if (activeInsuranceRemoved > 0 || scheduledInsuranceRemoved > 0)
            {
                logger.Info(
                    $"Removed pitFireTeam-returned item(s) from insurance tracking: active={activeInsuranceRemoved}, scheduled={scheduledInsuranceRemoved}.");
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to remove returned pitFireTeam item(s) from insurance tracking: {ex.Message}");
        }
    }

    private static HashSet<MongoId> BuildReturnedInsuranceRemovalIds(
        List<SPTarkov.Server.Core.Models.Eft.Common.Tables.Item> insuranceItems,
        HashSet<MongoId> returnedItemIds)
    {
        HashSet<MongoId> idsToRemove = insuranceItems
            .Where(item => item != null && returnedItemIds.Contains(item.Id))
            .Select(item => item.Id)
            .ToHashSet();

        bool added;
        do
        {
            added = false;
            foreach (SPTarkov.Server.Core.Models.Eft.Common.Tables.Item item in insuranceItems)
            {
                if (item == null ||
                    idsToRemove.Contains(item.Id) ||
                    string.IsNullOrWhiteSpace(item.ParentId))
                {
                    continue;
                }

                if (idsToRemove.Any(parentId => string.Equals(item.ParentId, parentId.ToString(), StringComparison.Ordinal)))
                {
                    added |= idsToRemove.Add(item.Id);
                }
            }
        }
        while (added);

        return idsToRemove;
    }

    public void HandleTeamEscaped(MongoId sessionId, FriendlyPostRaidTeamEscapedRequest request)
    {
        FriendlyPostRaidMember? member = request.Member;
        if (member == null)
        {
            return;
        }

        UserDialogInfo sender = ToSenderInfo(member);
        FriendlyPostRaidSquadInfo squad = member.SquadInfo ?? new FriendlyPostRaidSquadInfo();

        string message = PickRandom(FriendlyEscapedMessages);
        if (squad.Mate)
        {
            message = PickRandom(TeamEscapedMessages);

            if (squad.Partial)
            {
                string lostText = "the others";
                if (squad.Lost.Count > 0 && squad.Lost.Count < 3)
                {
                    lostText = JoinNames(squad.Lost);
                }

                message = string.Format(PickRandom(TeamSomeEscapedMessages), lostText);
            }
        }

        notificationSendHelper.SendMessageToPlayer(sessionId, sender, message, MessageType.UserMessage);
    }

    public void RecordKillMessage(MongoId sessionId, FriendlyPostRaidKillMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VictimProfileId) || string.IsNullOrWhiteSpace(request.MessageKind))
        {
            return;
        }

        string kind = NormalizeKillMessageKind(request.MessageKind);
        if (string.IsNullOrEmpty(kind))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.MessageText))
        {
            return;
        }

        Dictionary<string, KillMessageRecord> sessionRecords = KillMessageRecords.GetOrAdd(
            sessionId.ToString(),
            _ => new Dictionary<string, KillMessageRecord>(StringComparer.Ordinal));

        lock (sessionRecords)
        {
            var record = new KillMessageRecord(kind, request.MessageText);
            sessionRecords[request.VictimProfileId] = record;

            if (!string.IsNullOrWhiteSpace(request.VictimAccountId))
            {
                sessionRecords[$"aid:{request.VictimAccountId}"] = record;
            }
        }
    }

    public void HandleEndLocalRaidKillMessages(MongoId sessionId, EndLocalRaidRequestData request)
    {
        List<Victim> pmcVictims = request.Results?.Profile?.Stats?.Eft?.Victims?
            .Where(IsPmcVictim)
            .Where(victim => victim?.ProfileId is not null)
            .Cast<Victim>()
            .ToList()
            ?? [];

        if (pmcVictims.Count == 0)
        {
            ClearKillMessageRecords(sessionId);
            return;
        }

        long recentMessageThreshold = timeUtil.GetTimeStamp() - 60;
        foreach (Victim victim in pmcVictims)
        {
            KillMessageRecord? record = GetPostRaidKillMessageRecord(sessionId, victim);
            if (record == null)
            {
                continue;
            }

            RemoveRecentVanillaPmcResponse(sessionId, victim, recentMessageThreshold);

            if (record.Kind == KillMessageKindTraitor || record.Kind == KillMessageKindJerk)
            {
                SendVictimMessage(sessionId, victim, record.MessageText);
            }
        }

        ClearKillMessageRecords(sessionId);
    }

    public void HandleDeathEscapeSummary(MongoId sessionId, FriendlyTeammateDeathEscapeSummary summary)
    {
        if (summary.EscapedNames.Count == 0 && summary.LostNames.Count == 0)
        {
            return;
        }

        Dictionary<string, string> deathEscapeText = languageService.GetStringMap(sessionId, "deathEscape");
        string madeItOutTemplate = GetLanguageValue(deathEscapeText, "MadeItOut");
        string lostTemplate = GetLanguageValue(deathEscapeText, "Lost");
        string extractRouteTemplate = GetLanguageValue(deathEscapeText, "ExtractRoute");

        // Player-death escape reports are separate from the normal "team escaped with player"
        // messages because the player did not extract with the squad. Put each result on its own
        // line so mixed escaped/lost outcomes stay readable in post-raid mail.
        var parts = new List<string>();
        if (summary.EscapedNames.Count > 0)
        {
            parts.Add(string.Format(madeItOutTemplate, JoinNames(summary.EscapedNames)));
        }

        if (summary.LostNames.Count > 0)
        {
            parts.Add(string.Format(lostTemplate, JoinNames(summary.LostNames)));
        }

        if (!string.IsNullOrWhiteSpace(summary.ExtractName))
        {
            parts.Add(string.Format(extractRouteTemplate, summary.ExtractName));
        }

        string[] deathEscapeMessages = languageService.GetStringArray(sessionId, "deathEscapeMessages");
        string messageTemplate = deathEscapeMessages.Length > 0 ? PickRandom(deathEscapeMessages) : "{0}";
        string message = string.Format(messageTemplate, string.Join("\n", parts));

        UserDialogInfo sender = GetDeliverySender();
        SendMessageDetails details = new()
        {
            RecipientId = sessionId,
            Sender = MessageType.NpcTraderMessage,
            DialogType = MessageType.NpcTraderMessage,
            SenderDetails = sender,
            Trader = FriendlyCourierTraderProfile.CourierTraderIdValue,
            MessageText = message,
        };

        mailSendService.SendMessageToPlayer(details);
        EnsureDialogHasSender(sessionId, sender);
    }

    private KillMessageRecord? GetPostRaidKillMessageRecord(MongoId sessionId, Victim victim)
    {
        if (teammateService.IsTeammateIdentity(sessionId, victim.ProfileId, victim.AccountId))
        {
            return new KillMessageRecord("teammate", string.Empty);
        }

        return GetRecordedKillMessageRecord(sessionId, victim);
    }

    private KillMessageRecord? GetRecordedKillMessageRecord(MongoId sessionId, Victim victim)
    {
        if (!KillMessageRecords.TryGetValue(sessionId.ToString(), out var records))
        {
            return null;
        }

        lock (records)
        {
            if (victim.ProfileId is not null && records.TryGetValue(victim.ProfileId.Value.ToString(), out KillMessageRecord? byProfileId))
            {
                return byProfileId;
            }

            if (!string.IsNullOrWhiteSpace(victim.AccountId) && records.TryGetValue($"aid:{victim.AccountId}", out KillMessageRecord? byAid))
            {
                return byAid;
            }
        }

        return null;
    }

    private void RemoveRecentVanillaPmcResponse(MongoId sessionId, Victim victim, long recentMessageThreshold)
    {
        if (victim.ProfileId is null)
        {
            return;
        }

        try
        {
            Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.Dialogue> dialogs = dialogueHelper.GetDialogsForProfile(sessionId);
            if (!dialogs.TryGetValue(victim.ProfileId.Value, out var dialog) || dialog.Messages == null)
            {
                return;
            }

            int removeCount = dialog.Messages.RemoveAll(message =>
                message.UserId == victim.ProfileId.Value
                && message.MessageType == MessageType.UserMessage
                && message.Items is null
                && message.DateTime >= recentMessageThreshold);

            if (removeCount > 0 && dialog.New is > 0)
            {
                dialog.New = Math.Max(0, dialog.New.Value - removeCount);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to remove vanilla PMC response for victim '{victim.ProfileId}': {ex.Message}");
        }
    }

    private void SendVictimMessage(MongoId sessionId, Victim victim, string message)
    {
        if (victim.ProfileId is null || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        notificationSendHelper.SendMessageToPlayer(sessionId, ToSenderInfo(victim), message, MessageType.UserMessage);
    }

    private static UserDialogInfo ToSenderInfo(Victim victim)
    {
        int aid = 0;
        if (!string.IsNullOrWhiteSpace(victim.AccountId))
        {
            int.TryParse(victim.AccountId, out aid);
        }

        return new UserDialogInfo
        {
            Id = victim.ProfileId!.Value,
            Aid = aid,
            Info = new UserDialogDetails
            {
                Nickname = string.IsNullOrWhiteSpace(victim.Name) ? "PMC" : victim.Name,
                Side = string.IsNullOrWhiteSpace(victim.Side) ? "Usec" : victim.Side,
                Level = victim.Level ?? 1,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private static bool IsPmcVictim(Victim? victim)
    {
        return string.Equals(victim?.Role, "pmcBEAR", StringComparison.OrdinalIgnoreCase)
            || string.Equals(victim?.Role, "pmcUSEC", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeKillMessageKind(string? kind)
    {
        return kind?.Trim().ToLowerInvariant() switch
        {
            KillMessageKindTraitor => KillMessageKindTraitor,
            KillMessageKindJerk => KillMessageKindJerk,
            _ => string.Empty,
        };
    }

    private static void ClearKillMessageRecords(MongoId sessionId)
    {
        KillMessageRecords.TryRemove(sessionId.ToString(), out _);
    }

    private sealed record KillMessageRecord(string Kind, string MessageText);

    private void EnsureDialogHasSender(MongoId sessionId, UserDialogInfo sender)
    {
        try
        {
            Dictionary<MongoId, SPTarkov.Server.Core.Models.Eft.Profile.Dialogue> dialogs = dialogueHelper.GetDialogsForProfile(sessionId);
            if (!dialogs.TryGetValue(sender.Id, out var dialog) || dialog is null)
            {
                return;
            }

            dialog.Users ??= [];
            if (dialog.Users.All(user => user.Id != sender.Id))
            {
                dialog.Users.Add(sender);
            }
        }
        catch (Exception ex)
        {
            logger.Warning($"Failed to ensure sender details in post-raid dialog: {ex.Message}");
        }
    }

    private static UserDialogInfo GetDeliverySender()
    {
        return new UserDialogInfo
        {
            Id = FriendlyCourierTraderProfile.CourierTraderId,
            Aid = FriendlyCourierTraderProfile.CourierAid,
            Info = new UserDialogDetails
            {
                Nickname = FriendlyCourierTraderProfile.CourierNickname,
                Side = "Usec",
                Level = 1,
                MemberCategory = MemberCategory.Trader,
                SelectedMemberCategory = MemberCategory.Trader,
            },
        };
    }

    private static UserDialogInfo ToSenderInfo(FriendlyPostRaidMember member)
    {
        int parsedAid = 0;
        if (!string.IsNullOrWhiteSpace(member.Aid))
        {
            int.TryParse(member.Aid, out parsedAid);
        }

        MongoId senderId;
        if (!TryParseMongoId(member.Id, out senderId))
        {
            senderId = new MongoId("67b0f29e151899410b04aacb");
        }

        return new UserDialogInfo
        {
            Id = senderId,
            Aid = parsedAid,
            Info = member.Info ?? new UserDialogDetails
            {
                Nickname = "Squadmate",
                Side = "Usec",
                Level = 1,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private static bool TryParseMongoId(string? value, out MongoId mongoId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            try
            {
                mongoId = new MongoId(value);
                return true;
            }
            catch
            {
                // Fallback handled by caller.
            }
        }

        mongoId = default;
        return false;
    }

    private static string JoinNames(List<string> names)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count == 2)
        {
            return $"{names[0]} and {names[1]}";
        }

        return string.Join(", ", names.Take(names.Count - 1)) + $", and {names[^1]}";
    }

    private static string PickRandom(string[] values)
    {
        if (values.Length == 0)
        {
            return string.Empty;
        }

        int index = Random.Shared.Next(values.Length);
        return values[index];
    }

    private static string GetLanguageValue(Dictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "{0}";
    }
}
