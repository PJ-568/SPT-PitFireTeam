using pitTeam.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace pitTeam.Server.Services;

[Injectable]
public class FriendlyRecruitService(
    FileUtil fileUtil,
    JsonUtil jsonUtil,
    ProfileHelper profileHelper,
    FriendlyTeammateService teammateService,
    ISptLogger<FriendlyRecruitService> logger
)
{
    private const string ModFolderName = "pitFireTeam-ServerMod";
    private const string RecruitRequestsFileName = "recruit-requests.json";

    public void QueueRecruitPickups(MongoId sessionId, List<FriendlyRecruitPickupCandidate>? candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return;
        }

        var playerLevel = profileHelper.GetPmcProfile(sessionId)?.Info?.Level ?? 1;
        var successfulCandidates = new List<FriendlyRecruitRequestEntry>();
        foreach (var candidate in candidates)
        {
            if (!IsValidCandidate(candidate))
            {
                continue;
            }

            if (Random.Shared.Next(0, 101) > CalculateRecruitChance(playerLevel, Math.Max(1, candidate.Level)))
            {
                continue;
            }

            successfulCandidates.Add(new FriendlyRecruitRequestEntry
            {
                ProfileId = candidate.ProfileId,
                Nickname = candidate.Nickname.Trim(),
                Level = Math.Max(1, candidate.Level),
                Side = candidate.Side?.Trim() ?? string.Empty,
                Voice = candidate.Voice.Trim(),
                Head = candidate.Head.Trim(),
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
        }

        if (successfulCandidates.Count == 0)
        {
            return;
        }

        var pending = LoadRecruitRequests(sessionId);
        var picked = successfulCandidates[Random.Shared.Next(successfulCandidates.Count)];
        if (pending.Any(entry => string.Equals(entry.ProfileId, picked.ProfileId, StringComparison.Ordinal)))
        {
            return;
        }

        pending.Add(picked);
        SaveRecruitRequests(sessionId, pending);
        logger.Info($"Queued recruit pickup request '{picked.Nickname}' for session '{sessionId}'");
    }

    public List<FriendlySocialFriendRequestEntry> ListRecruitFriendRequests(MongoId sessionId)
    {
        var pending = LoadRecruitRequests(sessionId);
        if (pending.Count == 0)
        {
            return [];
        }

        var toId = sessionId.ToString();
        return pending.Select(entry => new FriendlySocialFriendRequestEntry
        {
            Id = $"pitfireteam-recruit-{entry.ProfileId}",
            From = entry.ProfileId,
            To = toId,
            Date = entry.CreatedAt,
            Profile = new FriendlySocialFriendProfile
            {
                Id = entry.ProfileId,
                Aid = entry.ProfileId,
                Info = new FriendlySocialFriendInfo
                {
                    Nickname = entry.Nickname,
                    Side = string.Equals(entry.Side, Sides.Bear, StringComparison.OrdinalIgnoreCase) ? Sides.Bear : Sides.Usec,
                    Level = entry.Level,
                    MemberCategory = MemberCategory.Unheard,
                    SelectedMemberCategory = MemberCategory.Unheard,
                    Ignored = false,
                    Banned = false,
                }
            }
        }).ToList();
    }

    public bool AcceptRecruitRequest(MongoId sessionId, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var pending = LoadRecruitRequests(sessionId);
        var request = pending.FirstOrDefault(entry => string.Equals(entry.ProfileId, profileId, StringComparison.Ordinal));
        if (request == null)
        {
            return false;
        }

        teammateService.CreateTeammateFromRecruitCandidate(sessionId, request);
        pending.Remove(request);
        SaveRecruitRequests(sessionId, pending);
        return true;
    }

    public bool AcceptAllRecruitRequests(MongoId sessionId)
    {
        var pending = LoadRecruitRequests(sessionId);
        if (pending.Count == 0)
        {
            return false;
        }

        foreach (var request in pending.ToList())
        {
            teammateService.CreateTeammateFromRecruitCandidate(sessionId, request);
        }

        SaveRecruitRequests(sessionId, []);
        return true;
    }

    public bool DeclineRecruitRequest(MongoId sessionId, string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        var pending = LoadRecruitRequests(sessionId);
        var removed = pending.RemoveAll(entry => string.Equals(entry.ProfileId, profileId, StringComparison.Ordinal)) > 0;
        if (removed)
        {
            SaveRecruitRequests(sessionId, pending);
        }

        return removed;
    }

    private List<FriendlyRecruitRequestEntry> LoadRecruitRequests(MongoId sessionId)
    {
        var path = GetRecruitRequestsFilePath(sessionId);
        if (!fileUtil.FileExists(path))
        {
            return [];
        }

        return jsonUtil.DeserializeFromFile<List<FriendlyRecruitRequestEntry>>(path) ?? [];
    }

    private void SaveRecruitRequests(MongoId sessionId, List<FriendlyRecruitRequestEntry> requests)
    {
        var json = jsonUtil.Serialize(requests, indented: true);
        if (json == null)
        {
            return;
        }

        fileUtil.WriteFile(GetRecruitRequestsFilePath(sessionId), json);
    }

    private string GetRecruitRequestsFilePath(MongoId sessionId)
    {
        return Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", "teammates", sessionId.ToString(), RecruitRequestsFileName);
    }

    private static bool IsValidCandidate(FriendlyRecruitPickupCandidate candidate)
    {
        return candidate != null &&
               !string.IsNullOrWhiteSpace(candidate.ProfileId) &&
               !string.IsNullOrWhiteSpace(candidate.Nickname) &&
               !string.IsNullOrWhiteSpace(candidate.Voice) &&
               !string.IsNullOrWhiteSpace(candidate.Head);
    }

    private static int CalculateRecruitChance(int playerLevel, int botLevel)
    {
        var levelDifference = botLevel - playerLevel;
        if (levelDifference >= 10)
        {
            return 0;
        }

        if (levelDifference <= -10)
        {
            return 100;
        }

        return (int)Math.Round((1 - (levelDifference + 10) / 20.0) * 100);
    }
}
