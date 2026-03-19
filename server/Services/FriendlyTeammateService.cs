using friendlySAIN.Server.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Constants;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Bots;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using CommonCustomization = SPTarkov.Server.Core.Models.Eft.Common.Tables.Customization;
using CommonInfo = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;

namespace friendlySAIN.Server.Services;

[Injectable]
public class FriendlyTeammateService(
    BotGenerator botGenerator,
    FileUtil fileUtil,
    HashUtil hashUtil,
    JsonUtil jsonUtil,
    ItemHelper itemHelper,
    ProfileHelper profileHelper,
    SaveServer saveServer,
    ICloner cloner,
    ISptLogger<FriendlyTeammateService> logger
)
{
    private const string ModFolderName = "friendlySAIN-ServerMod";
    private const string TeammateFolderName = "teammates";
    private const string DefaultLoadoutName = "Default";
    private const string DefaultLoadoutId = "000000000000000000000000";
    private const int RelativeLevelDelta = 5;

    public SearchFriendResponse CreateTeammate(MongoId sessionId, FriendlyTeammateCreateRequest request)
    {
        var playerPmc = GetPlayerProfile(sessionId);
        var nickname = NormalizeRequiredValue(request.Nickname, "nickname");
        var voice = NormalizeRequiredValue(request.Voice, "voice");
        var head = NormalizeRequiredValue(request.Head, "head");

        EnsureNicknameIsUnique(sessionId, nickname);

        var teammate = botGenerator.PrepareAndGenerateBot(
            sessionId,
            new BotGenerationDetails
            {
                IsPmc = true,
                Side = playerPmc.Info!.Side!,
                Role = GetPmcRole(playerPmc.Info.Side),
                PlayerLevel = Math.Max(1, playerPmc.Info.Level ?? 1),
                PlayerName = playerPmc.Info.Nickname,
                BotRelativeLevelDeltaMax = RelativeLevelDelta,
                BotRelativeLevelDeltaMin = RelativeLevelDelta,
                BotCountToGenerate = 1,
                BotDifficulty = "hard",
                LocationSpecificPmcLevelOverride = new MinMax<int>
                {
                    Min = Math.Max(1, (playerPmc.Info.Level ?? 1) - RelativeLevelDelta),
                    Max = Math.Min(100, (playerPmc.Info.Level ?? 1) + RelativeLevelDelta),
                },
                IsPlayerScav = false,
                AllPmcsHaveSameNameAsPlayer = false,
            }
        );

        teammate.Info!.Nickname = nickname;
        teammate.Info.LowerNickname = nickname.ToLowerInvariant();
        teammate.Customization!.Voice = voice;
        teammate.Customization.Head = head;
        teammate.Aid = GetUniqueAccountId(sessionId);

        NormalizeTeammateProfile(teammate, playerPmc);
        SaveDefaultEquipmentSnapshot(sessionId, teammate, overwrite: true);
        SaveTeammate(sessionId, teammate);
        SaveTeammateSettings(sessionId, teammate, new FriendlyTeammateSettings { SelectedLoadoutId = DefaultLoadoutId });

        logger.Info($"Created teammate '{nickname}' for session '{sessionId}' with aid '{teammate.Aid}'");

        return ToFriendSummary(teammate);
    }

    public List<SearchFriendResponse> ListTeammates(MongoId sessionId)
    {
        return LoadTeammates(sessionId).Select(ToFriendSummary).ToList();
    }

    public List<FriendlyTeammateFollowerDetailsResponse> ListFollowerDetails(MongoId sessionId)
    {
        var fullProfile = profileHelper.GetFullProfile(sessionId);
        var loadoutNames = GetCustomEquipmentBuilds(fullProfile)
            .ToDictionary(build => build.Id.ToString(), build => build.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        return LoadTeammates(sessionId)
            .Select(teammate =>
            {
                var settings = GetTeammateSettings(sessionId, teammate);
                var loadoutId = NormalizeCurrentLoadoutId(fullProfile, settings.SelectedLoadoutId);
                var equipmentName = string.Equals(loadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase)
                    ? DefaultLoadoutName
                    : loadoutNames.TryGetValue(loadoutId, out var customName)
                        ? customName
                        : DefaultLoadoutName;

                return new FriendlyTeammateFollowerDetailsResponse
                {
                    Aid = teammate.Aid?.ToString() ?? string.Empty,
                    Tactic = "Default",
                    Equipment = equipmentName,
                    Voice = teammate.Customization?.Voice?.ToString() ?? string.Empty,
                    Head = teammate.Customization?.Head?.ToString() ?? string.Empty,
                };
            })
            .ToList();
    }

    public List<UserDialogInfo> ListTeammateDialogs(MongoId sessionId)
    {
        return LoadTeammates(sessionId).Select(ToFriendDialog).ToList();
    }

    public GetOtherProfileResponse GetTeammateProfile(MongoId sessionId, GetOtherProfileRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.AccountId);
        return ToOtherProfileResponse(teammate);
    }

    public FriendlyTeammateProfileOptionsResponse GetProfileOptions(MongoId sessionId, FriendlyTeammateProfileOptionsRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var profile = profileHelper.GetFullProfile(sessionId);
        var selectedLoadoutId = NormalizeCurrentLoadoutId(profile, GetTeammateSettings(sessionId, teammate).SelectedLoadoutId);

        var response = new FriendlyTeammateProfileOptionsResponse
        {
            CurrentLoadoutId = selectedLoadoutId,
            Loadouts =
            [
                new FriendlyTeammateLoadoutOption
                {
                    Id = DefaultLoadoutId,
                    Name = DefaultLoadoutName,
                },
            ],
        };

        foreach (var build in GetCustomEquipmentBuilds(profile))
        {
            response.Loadouts.Add(
                new FriendlyTeammateLoadoutOption
                {
                    Id = build.Id.ToString(),
                    Name = build.Name ?? string.Empty,
                }
            );
        }

        return response;
    }

    public void SetTeammateSuit(MongoId sessionId, FriendlyTeammateSuitRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        if (request.Suit == null || request.Suit.Length < 2)
        {
            throw new FriendlyTeammateException("Missing teammate suit values");
        }

        teammate.Customization ??= new CommonCustomization();
        teammate.Customization.Body = NormalizeRequiredValue(request.Suit[0], "body");
        teammate.Customization.Feet = NormalizeRequiredValue(request.Suit[1], "feet");

        SaveTeammate(sessionId, teammate);
    }

    public void RenameTeammate(MongoId sessionId, FriendlyTeammateRenameRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var nickname = NormalizeRequiredValue(request.Nickname, "nickname");

        EnsureNicknameIsUnique(sessionId, nickname, teammate.Aid);

        teammate.Info ??= new CommonInfo();
        teammate.Info.Nickname = nickname;
        teammate.Info.LowerNickname = nickname.ToLowerInvariant();

        SaveTeammate(sessionId, teammate);
    }

    public void SetTeammateLoadout(MongoId sessionId, FriendlyTeammateLoadoutRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.Aid);
        var selectedLoadoutId = NormalizeRequiredValue(request.LoadoutId, "loadoutId");

        if (string.Equals(selectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            RestoreDefaultEquipment(sessionId, teammate);
            SaveTeammateSettings(sessionId, teammate, new FriendlyTeammateSettings { SelectedLoadoutId = DefaultLoadoutId });
            SaveTeammate(sessionId, teammate);
            return;
        }

        var fullProfile = profileHelper.GetFullProfile(sessionId);
        var playerPmc = GetPlayerProfile(sessionId);
        var equipmentBuild = GetCustomEquipmentBuilds(fullProfile)
            .FirstOrDefault(build => string.Equals(build.Id.ToString(), selectedLoadoutId, StringComparison.OrdinalIgnoreCase));

        if (equipmentBuild == null)
        {
            throw new FriendlyTeammateException($"Unable to find teammate equipment build '{selectedLoadoutId}'");
        }

        ApplyEquipmentBuild(teammate, equipmentBuild, playerPmc);
        SaveTeammateSettings(sessionId, teammate, new FriendlyTeammateSettings { SelectedLoadoutId = selectedLoadoutId });
        SaveTeammate(sessionId, teammate);
    }

    public bool TryGetTeammateProfile(MongoId sessionId, string? accountId, out GetOtherProfileResponse? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        var teammate = LoadTeammates(sessionId).FirstOrDefault(candidate => candidate.Aid?.ToString() == accountId);
        if (teammate == null)
        {
            return false;
        }

        profile = ToOtherProfileResponse(teammate);
        return true;
    }

    public bool TryGetRaidGroupCharacter(MongoId sessionId, string? accountId, out GroupCharacter? groupCharacter)
    {
        groupCharacter = null;
        if (!TryFindByAccountId(sessionId, accountId, out var teammate))
        {
            return false;
        }

        groupCharacter = ToGroupCharacter(teammate!);
        return true;
    }

    public bool TryGetSpawnProfile(MongoId sessionId, string? accountId, double? healthMultiplier, out BotBase? profile)
    {
        profile = null;
        if (!TryFindByAccountId(sessionId, accountId, out var teammate))
        {
            return false;
        }

        var clone = cloner.Clone(teammate!) ?? teammate;
        ApplyTemporaryHealthMultiplier(clone!, healthMultiplier);
        profile = clone;
        return true;
    }

    public bool DeleteTeammate(MongoId sessionId, FriendlyTeammateDeleteRequest request)
    {
        var teammate = FindByAccountId(sessionId, request.AccountId);
        return DeleteTeammate(sessionId, teammate);
    }

    public bool DeleteTeammateByProfileId(MongoId sessionId, MongoId teammateId)
    {
        var teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Id == teammateId);
        if (teammate == null)
        {
            return false;
        }

        return DeleteTeammate(sessionId, teammate);
    }

    private bool DeleteTeammate(MongoId sessionId, BotBase teammate)
    {
        var filePath = GetTeammateFilePath(sessionId, teammate);
        var deleted = fileUtil.DeleteFile(filePath);
        fileUtil.DeleteFile(GetTeammateSettingsFilePath(sessionId, teammate));
        fileUtil.DeleteFile(GetDefaultEquipmentFilePath(sessionId, teammate));
        if (deleted)
        {
            logger.Info($"Deleted teammate '{teammate.Info?.Nickname}' for session '{sessionId}'");
        }

        return deleted;
    }

    private string GetPmcRole(string? side)
    {
        return side switch
        {
            Sides.Usec => Sides.PmcUsec,
            Sides.Bear => Sides.PmcBear,
            _ => throw new FriendlyTeammateException($"Unsupported teammate side '{side}'"),
        };
    }

    private PmcData GetPlayerProfile(MongoId sessionId)
    {
        var pmc = profileHelper.GetPmcProfile(sessionId);
        if (pmc?.Info?.Side is null)
        {
            throw new FriendlyTeammateException($"Unable to resolve PMC profile for session '{sessionId}'");
        }

        return pmc;
    }

    private void EnsureNicknameIsUnique(MongoId sessionId, string nickname, int? ignoreAid = null)
    {
        var exists = LoadTeammates(sessionId).Any(profile =>
            (!ignoreAid.HasValue || profile.Aid != ignoreAid.Value) &&
            string.Equals(profile.Info?.Nickname, nickname, StringComparison.OrdinalIgnoreCase)
        );

        if (exists)
        {
            throw new FriendlyTeammateException($"Teammate nickname '{nickname}' already exists");
        }
    }

    private void NormalizeTeammateProfile(BotBase teammate, PmcData playerPmc)
    {
        teammate.Info ??= new CommonInfo();
        teammate.Customization ??= new CommonCustomization();
        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        teammate.Stats ??= new Stats();
        teammate.Stats.Eft ??= new EftStats();
        teammate.Hideout ??= new Hideout();
        teammate.Inventory.HideoutAreaStashes ??= [];

        teammate.Info.Side = playerPmc.Info?.Side;
        teammate.Info.MemberCategory = MemberCategory.Unheard;
        teammate.Info.SelectedMemberCategory = MemberCategory.Unheard;
        teammate.Info.BannedState = playerPmc.Info?.BannedState;
        teammate.Info.BannedUntil = playerPmc.Info?.BannedUntil;
        teammate.Info.RegistrationDate = playerPmc.Info?.RegistrationDate;
        teammate.Achievements = playerPmc.Achievements;
        teammate.Stats.Eft.TotalInGameTime = playerPmc.Stats?.Eft?.TotalInGameTime ?? teammate.Stats.Eft.TotalInGameTime ?? 0;
        teammate.Stats.Eft.OverallCounters = playerPmc.Stats?.Eft?.OverallCounters ?? teammate.Stats.Eft.OverallCounters;
    }

    private List<BotBase> LoadTeammates(MongoId sessionId)
    {
        var directory = GetTeammateDirectory(sessionId);
        if (!fileUtil.DirectoryExists(directory))
        {
            return [];
        }

        var teammates = new List<BotBase>();
        foreach (var file in fileUtil.GetFiles(directory).Where(IsTeammateProfileFile))
        {
            var teammate = jsonUtil.DeserializeFromFile<BotBase>(file);
            if (teammate?.Id is null)
            {
                continue;
            }

            teammates.Add(teammate);
        }

        return teammates.OrderBy(profile => profile.Aid ?? int.MaxValue).ToList();
    }

    private BotBase FindByAccountId(MongoId sessionId, string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new FriendlyTeammateException("Missing teammate accountId");
        }

        if (!int.TryParse(accountId, out var aid))
        {
            throw new FriendlyTeammateException($"Invalid teammate accountId '{accountId}'");
        }

        var teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Aid == aid);
        return teammate ?? throw new FriendlyTeammateException($"Unable to find teammate with accountId '{accountId}'");
    }

    private bool TryFindByAccountId(MongoId sessionId, string? accountId, out BotBase? teammate)
    {
        teammate = null;
        if (string.IsNullOrWhiteSpace(accountId) || !int.TryParse(accountId, out var aid))
        {
            return false;
        }

        teammate = LoadTeammates(sessionId).FirstOrDefault(profile => profile.Aid == aid);
        return teammate != null;
    }

    private void SaveTeammate(MongoId sessionId, BotBase teammate)
    {
        var filePath = GetTeammateFilePath(sessionId, teammate);
        var json = jsonUtil.Serialize(teammate, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate profile");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private string GetTeammateDirectory(MongoId sessionId)
    {
        return System.IO.Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", TeammateFolderName, sessionId.ToString());
    }

    private string GetTeammateFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}.json");
    }

    private string GetTeammateSettingsFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}-settings.json");
    }

    private string GetDefaultEquipmentFilePath(MongoId sessionId, BotBase teammate)
    {
        return System.IO.Path.Combine(GetTeammateDirectory(sessionId), $"{teammate.Aid}-equipment.json");
    }

    private string NormalizeRequiredValue(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new FriendlyTeammateException($"Missing teammate {fieldName}");
        }

        return normalized;
    }

    private int GetUniqueAccountId(MongoId sessionId)
    {
        var usedAids = new HashSet<int>(
            saveServer.GetProfiles().Values.Select(profile => profile.ProfileInfo?.Aid ?? 0).Where(aid => aid > 0)
        );

        foreach (var teammate in LoadTeammates(sessionId))
        {
            if (teammate.Aid is > 0)
            {
                usedAids.Add(teammate.Aid.Value);
            }
        }

        var teammateRoot = System.IO.Path.Combine(fileUtil.GetModPath(ModFolderName), "Resources", TeammateFolderName);
        if (fileUtil.DirectoryExists(teammateRoot))
        {
            foreach (var teammateAid in fileUtil
                .GetFiles(teammateRoot, recursive: true, searchPattern: "*.json")
                .Where(IsTeammateProfileFile)
                .Select(path => jsonUtil.DeserializeFromFile<BotBase>(path)?.Aid ?? 0)
                .Where(aid => aid > 0))
            {
                usedAids.Add(teammateAid);
            }
        }

        for (var attempts = 0; attempts < 1024; attempts++)
        {
            var candidateAid = hashUtil.GenerateAccountId();
            if (!usedAids.Contains(candidateAid))
            {
                return candidateAid;
            }
        }

        throw new FriendlyTeammateException("Unable to allocate a unique teammate account id");
    }

    private SearchFriendResponse ToFriendSummary(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        return new SearchFriendResponse
        {
            Id = teammate.Id ?? throw new FriendlyTeammateException("Teammate profile is missing Id"),
            Aid = teammate.Aid,
            Info = new UserDialogDetails
            {
                Nickname = info.Nickname,
                Side = info.Side,
                Level = info.Level,
                MemberCategory = MemberCategory.Unheard,
                SelectedMemberCategory = MemberCategory.Unheard,
            },
        };
    }

    private UserDialogInfo ToFriendDialog(BotBase teammate)
    {
        SearchFriendResponse summary = ToFriendSummary(teammate);
        return new UserDialogInfo
        {
            Id = summary.Id,
            Aid = summary.Aid,
            Info = summary.Info,
        };
    }

    private GetOtherProfileResponse ToOtherProfileResponse(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        var customization = teammate.Customization ?? new CommonCustomization();
        var inventory = teammate.Inventory ?? new BotBaseInventory { Items = [] };
        var stats = teammate.Stats ?? new Stats { Eft = new EftStats() };

        return new GetOtherProfileResponse
        {
            Id = teammate.Id,
            Aid = teammate.Aid,
            Info = new OtherProfileInfo
            {
                Nickname = info.Nickname,
                Side = info.Side,
                Experience = info.Experience,
                MemberCategory = (int)MemberCategory.Unheard,
                BannedState = info.BannedState,
                BannedUntil = info.BannedUntil,
                RegistrationDate = info.RegistrationDate,
            },
            Customization = new OtherProfileCustomization
            {
                Head = customization.Head,
                Body = customization.Body,
                Feet = customization.Feet,
                Hands = customization.Hands,
                Dogtag = customization.DogTag,
                Voice = customization.Voice,
            },
            Skills = teammate.Skills,
            Equipment = new OtherProfileEquipment
            {
                Id = inventory.Equipment?.ToString(),
                Items = inventory.Items ?? [],
            },
            Achievements = teammate.Achievements,
            FavoriteItems = [],
            PmcStats = new OtherProfileStats
            {
                Eft = new OtherProfileSubStats
                {
                    TotalInGameTime = stats.Eft?.TotalInGameTime,
                    OverAllCounters = stats.Eft?.OverallCounters,
                },
            },
            ScavStats = new OtherProfileStats
            {
                Eft = new OtherProfileSubStats
                {
                    TotalInGameTime = stats.Eft?.TotalInGameTime,
                    OverAllCounters = stats.Eft?.OverallCounters,
                },
            },
            Hideout = teammate.Hideout ?? new Hideout(),
            CustomizationStash = inventory.HideoutCustomizationStashId?.ToString() ?? string.Empty,
            HideoutAreaStashes = inventory.HideoutAreaStashes ?? [],
            Items = [],
        };
    }

    private GroupCharacter ToGroupCharacter(BotBase teammate)
    {
        var info = teammate.Info ?? throw new FriendlyTeammateException("Teammate profile is missing Info");
        var customization = teammate.Customization ?? new CommonCustomization();
        var inventory = teammate.Inventory ?? new BotBaseInventory { Items = [] };

        return new GroupCharacter
        {
            Id = teammate.Id?.ToString(),
            Aid = teammate.Aid,
            Info = new CharacterInfo
            {
                Nickname = info.Nickname,
                SavageNickname = info.Nickname,
                Side = info.Side,
                Level = info.Level,
                MemberCategory = MemberCategory.Unheard,
                GameVersion = info.GameVersion,
                HasCoopExtension = info.HasCoopExtension,
            },
            VisualRepresentation = new PlayerVisualRepresentation
            {
                Info = new VisualInfo
                {
                    Nickname = info.Nickname,
                    Side = info.Side,
                    Level = info.Level,
                    MemberCategory = MemberCategory.Unheard,
                    GameVersion = info.GameVersion,
                },
                Customization = new SPTarkov.Server.Core.Models.Eft.Match.Customization
                {
                    Head = customization.Head?.ToString(),
                    Body = customization.Body?.ToString(),
                    Feet = customization.Feet?.ToString(),
                    Hands = customization.Hands?.ToString(),
                },
                Equipment = new SPTarkov.Server.Core.Models.Eft.Match.Equipment
                {
                    Id = inventory.Equipment?.ToString(),
                    Items = inventory.Items ?? [],
                },
            },
            IsLeader = false,
            IsReady = true,
            LookingGroup = false,
            Region = string.Empty,
        };
    }

    private void ApplyTemporaryHealthMultiplier(BotBase teammate, double? healthMultiplier)
    {
        if (healthMultiplier is null || Math.Abs(healthMultiplier.Value - 1d) < 0.0001d)
        {
            return;
        }

        var bodyParts = teammate.Health?.BodyParts;
        if (bodyParts == null)
        {
            return;
        }

        foreach (var bodyPart in bodyParts.Values)
        {
            var health = bodyPart?.Health;
            if (health?.Maximum == null)
            {
                continue;
            }

            health.Maximum *= healthMultiplier.Value;
            health.Current = health.Maximum;
        }
    }

    private bool IsTeammateProfileFile(string path)
    {
        if (!string.Equals(fileUtil.GetFileExtension(path), "json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string fileName = System.IO.Path.GetFileName(path);
        return !fileName.EndsWith("-equipment.json", StringComparison.OrdinalIgnoreCase)
            && !fileName.EndsWith("-settings.json", StringComparison.OrdinalIgnoreCase);
    }

    private FriendlyTeammateSettings GetTeammateSettings(MongoId sessionId, BotBase teammate)
    {
        string filePath = GetTeammateSettingsFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            return new FriendlyTeammateSettings { SelectedLoadoutId = DefaultLoadoutId };
        }

        FriendlyTeammateSettings? settings = jsonUtil.DeserializeFromFile<FriendlyTeammateSettings>(filePath);
        return settings ?? new FriendlyTeammateSettings { SelectedLoadoutId = DefaultLoadoutId };
    }

    private void SaveTeammateSettings(MongoId sessionId, BotBase teammate, FriendlyTeammateSettings settings)
    {
        string filePath = GetTeammateSettingsFilePath(sessionId, teammate);
        string? json = jsonUtil.Serialize(settings, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate settings");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private void SaveDefaultEquipmentSnapshot(MongoId sessionId, BotBase teammate, bool overwrite = false)
    {
        teammate.Inventory ??= new BotBaseInventory { Items = [] };

        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!overwrite && fileUtil.FileExists(filePath))
        {
            return;
        }

        var items = cloner.Clone(teammate.Inventory.Items ?? []) ?? [];
        string? json = jsonUtil.Serialize(items, indented: true);
        if (json is null)
        {
            throw new FriendlyTeammateException("Unable to serialize teammate default equipment");
        }

        fileUtil.WriteFile(filePath, json);
    }

    private void RestoreDefaultEquipment(MongoId sessionId, BotBase teammate)
    {
        string filePath = GetDefaultEquipmentFilePath(sessionId, teammate);
        if (!fileUtil.FileExists(filePath))
        {
            throw new FriendlyTeammateException("Teammate default equipment snapshot is missing");
        }

        List<Item>? items = jsonUtil.DeserializeFromFile<List<Item>>(filePath);
        if (items == null || items.Count == 0)
        {
            throw new FriendlyTeammateException("Unable to load teammate default equipment");
        }

        teammate.Inventory ??= new BotBaseInventory();
        teammate.Inventory.Items = cloner.Clone(items) ?? items;
        teammate.Inventory.Equipment = teammate.Inventory.Items.First().Id;
    }

    private List<EquipmentBuild> GetCustomEquipmentBuilds(SptProfile profile)
    {
        return profile.UserBuildData?.EquipmentBuilds?
            .Where(build => build.BuildType == EquipmentBuildType.Custom)
            .Where(build => !string.IsNullOrWhiteSpace(build.Name))
            .ToList()
            ?? [];
    }

    private string NormalizeCurrentLoadoutId(SptProfile profile, string? selectedLoadoutId)
    {
        if (string.IsNullOrWhiteSpace(selectedLoadoutId)
            || string.Equals(selectedLoadoutId, DefaultLoadoutId, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultLoadoutId;
        }

        bool exists = GetCustomEquipmentBuilds(profile)
            .Any(build => string.Equals(build.Id.ToString(), selectedLoadoutId, StringComparison.OrdinalIgnoreCase));

        return exists ? selectedLoadoutId : DefaultLoadoutId;
    }

    private void ApplyEquipmentBuild(BotBase teammate, EquipmentBuild equipmentBuild, PmcData playerPmc)
    {
        if (equipmentBuild.Items == null || equipmentBuild.Items.Count == 0)
        {
            throw new FriendlyTeammateException("Teammate equipment build has no items");
        }

        var clonedBuild = cloner.Clone(equipmentBuild.Items) ?? equipmentBuild.Items;
        var normalizedBuild = itemHelper.ReplaceIDs(clonedBuild, playerPmc).ToList();
        MongoId rootId = normalizedBuild.First().Id;

        teammate.Inventory ??= new BotBaseInventory { Items = [] };
        var specialItems = (teammate.Inventory.Items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.SlotId))
            .Where(item =>
                item.SlotId!.Contains("Dogtag", StringComparison.OrdinalIgnoreCase)
                || item.SlotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase)
                || item.SlotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase))
            .Select(item => cloner.Clone(item) ?? item)
            .ToList();

        var mergedItems = normalizedBuild
            .Where(item =>
                string.IsNullOrWhiteSpace(item.SlotId)
                || (!item.SlotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                    && !item.SlotId.Contains("SecuredContainer", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        MongoId? pocketsId = mergedItems.FirstOrDefault(item =>
            string.Equals(item.SlotId, "Pockets", StringComparison.OrdinalIgnoreCase))?.Id;

        foreach (var item in specialItems)
        {
            item.ParentId = item.SlotId != null && item.SlotId.Contains("SpecialSlot", StringComparison.OrdinalIgnoreCase)
                ? pocketsId ?? rootId
                : rootId;
            mergedItems.Add(item);
        }

        teammate.Inventory.Items = mergedItems;
        teammate.Inventory.Equipment = rootId;
    }
}
