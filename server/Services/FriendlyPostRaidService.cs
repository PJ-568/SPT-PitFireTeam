using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Dialog;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils;

namespace friendlySAIN.Server.Services;

[Injectable]
public class FriendlyPostRaidService(
    MailSendService mailSendService,
    NotificationSendHelper notificationSendHelper,
    DialogueHelper dialogueHelper,
    ISptLogger<FriendlyPostRaidService> logger
)
{
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

        UserDialogInfo sender = GetDeliverySender();
        SendMessageDetails details = new()
        {
            RecipientId = sessionId,
            Sender = MessageType.UserMessage,
            DialogType = MessageType.UserMessage,
            SenderDetails = sender,
            MessageText = PickRandom(ReturnItemsMessages),
            Items = items,
            ItemsMaxStorageLifetimeSeconds = 86400,
        };

        mailSendService.SendMessageToPlayer(details);
        EnsureDialogHasSender(sessionId, sender);
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
            Id = new MongoId("67b0f29e151899410b04aacb"),
            Aid = 900001,
            Info = new UserDialogDetails
            {
                Nickname = "Squadmate Courier",
                Side = "Usec",
                Level = 50,
                MemberCategory = MemberCategory.Developer,
                SelectedMemberCategory = MemberCategory.Developer,
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
}
