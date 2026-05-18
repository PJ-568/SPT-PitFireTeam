using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using pitTeam.Components;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace pitTeam.Modules
{
    internal class FollowerDeathEscapeOutcomeRequest
    {
        public List<FollowerDeathEscapeOutcomeEntry> Entries { get; set; } = new List<FollowerDeathEscapeOutcomeEntry>();
    }

    internal class FollowerDeathEscapeOutcomeEntry
    {
        public string Aid { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public bool Escaped { get; set; }
        public float Chance { get; set; }
        public string ExtractName { get; set; } = string.Empty;
        public float Distance { get; set; }
        public float HealthRatio { get; set; }
        public float EquipmentPower { get; set; }
        public float EnemyAveragePower { get; set; }
        public int AliveSquadmates { get; set; }
        public bool HasSecureMeds { get; set; }
        public FlatItemsDataClass[] EquipmentItems { get; set; }
        public string[] TrackedItemIds { get; set; }
    }

    internal static partial class FollowerDeathEscapeResolver
    {
        private const string OutcomeRoute = "/singleplayer/pitfireteam/teammate/death-escape";
        private const string LostOnDeathRoute = "/singleplayer/pitfireteam/lostondeath";
        private const float MinChance = 0.05f;
        private const float MaxChance = 0.90f;
        private const float CloseExtractDistance = 150f;
        private const float FarExtractDistance = 900f;
        private const float DeathGearRecoveryDistance = 70f;
        private const float FallenTeammateSnapshotRadius = 50f;

        private static readonly EBodyPart[] HealthParts =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg
        };

        public static void ResolveAndSend(pitAIBossPlayer boss, List<BotFollowerPlayer> followers)
        {
            if (boss == null || followers == null || followers.Count == 0)
            {
                return;
            }

            try
            {
                if (pitFireTeam.teamEscape?.Value != true)
                {
                    Logger.LogInfo("[DeathEscape] Player died, but Team Escape is disabled.");
                    return;
                }

                // Include all squadmates in the result so already-dead followers appear in the summary,
                // but only followers still alive at player death get an escape roll.
                List<BotFollowerPlayer> squadmates = followers
                    .Where(follower => follower?.GetBot() != null && follower.IsSquadMate)
                    .ToList();

                List<BotFollowerPlayer> aliveSquadmates = squadmates
                    .Where(follower =>
                    {
                        BotOwner bot = follower?.GetBot();
                        return bot != null &&
                               bot.BotState == EBotState.Active &&
                               bot.GetPlayer != null &&
                               bot.HealthController?.IsAlive == true;
                    })
                    .ToList();

                if (squadmates.Count == 0)
                {
                    Logger.LogInfo("[DeathEscape] Player died, but no squadmate followers were available for escape resolution.");
                    return;
                }

                // Shared context is captured once before follower dismissal and raid cleanup can invalidate
                // bot ownership, extract objects, or group enemy state.
                Vector3 deathPosition = boss.realPlayer?.Position ?? boss.Position;
                ExtractSnapshot extract = ChooseExtract(boss, deathPosition);
                int aliveCount = aliveSquadmates.Count;
                LostOnDeathRules lostOnDeathRules = LoadLostOnDeathRules();
                HashSet<string> trackedLootIds = BuildTrackedLootIdSet(squadmates.Select(follower => follower.GetBot()));
                List<RecoverableGearCandidate> deathGearSnapshot = CreateDeathGearSnapshot(
                    squadmates.Select(follower => follower.GetBot()),
                    boss.realPlayer,
                    trackedLootIds,
                    deathPosition,
                    lostOnDeathRules);

                Logger.LogInfo(
                    $"[DeathEscape] Resolving follower escape rolls. squadmates={squadmates.Count} alive={aliveCount} " +
                    $"extract='{extract.Name}' distance={extract.Distance:0.0}");

                List<FollowerDeathEscapeOutcomeEntry> entries = new List<FollowerDeathEscapeOutcomeEntry>();
                List<BotOwner> escapedBots = new List<BotOwner>();
                Dictionary<string, BotOwner> entryBotsByAid = new Dictionary<string, BotOwner>(StringComparer.Ordinal);

                // Each follower rolls independently. Squad count and extraction route are shared inputs,
                // while health, gear power, and secure meds are follower-specific.
                foreach (BotFollowerPlayer follower in squadmates)
                {
                    BotOwner bot = follower.GetBot();
                    FollowerReadiness readiness = SnapshotReadiness(bot);
                    bool alive = bot.BotState == EBotState.Active &&
                                 bot.GetPlayer != null &&
                                 bot.HealthController?.IsAlive == true;
                    RouteThreatSnapshot routeThreat = alive
                        ? CalculateRouteEnemyAveragePower(boss, bot.Position, extract)
                        : RouteThreatSnapshot.Empty;
                    float chance = alive
                        ? CalculateChance(extract.Distance, aliveCount, readiness, routeThreat.AveragePower)
                        : 0f;
                    bool escaped = alive && UnityEngine.Random.value <= chance;
                    string aid = bot.Profile?.AccountId ?? string.Empty;

                    entries.Add(new FollowerDeathEscapeOutcomeEntry
                    {
                        Aid = aid,
                        ProfileId = bot.ProfileId ?? string.Empty,
                        Nickname = bot.Profile?.Nickname ?? "Squadmate",
                        Escaped = escaped,
                        Chance = chance,
                        ExtractName = extract.Name,
                        Distance = extract.Distance,
                        HealthRatio = readiness.HealthRatio,
                        EquipmentPower = readiness.EquipmentPower,
                        EnemyAveragePower = routeThreat.AveragePower,
                        AliveSquadmates = aliveCount,
                        HasSecureMeds = readiness.HasSecureMeds,
                        EquipmentItems = null,
                        TrackedItemIds = escaped ? GetTrackedFollowerItemIds(bot) : Array.Empty<string>()
                    });

                    if (escaped)
                    {
                        escapedBots.Add(bot);
                        if (!string.IsNullOrWhiteSpace(aid))
                        {
                            entryBotsByAid[aid] = bot;
                        }
                    }

                    Logger.LogInfo(
                        $"[DeathEscape] Roll follower='{bot.Profile?.Nickname ?? "Squadmate"}' alive={alive} " +
                        $"escaped={escaped} chance={chance:P0} health={readiness.HealthRatio:P0} " +
                        $"gear={readiness.EquipmentPower:0.0} routeEnemies={routeThreat.Count} " +
                        $"routeEnemyAvgPower={routeThreat.AveragePower:0.0} secureMeds={readiness.HasSecureMeds}");
                }

                AddMissingFallenSquadmateOutcomes(entries, deathPosition);

                ApplyDeathGearRecoveryToEscapedEquipment(
                    entries,
                    entryBotsByAid,
                    escapedBots,
                    deathGearSnapshot);

                // The normal loot-return path runs after the boss/follower link is removed on player death.
                // Send tracked loot now, but only for followers that won their escape roll.
                if (escapedBots.Count > 0)
                {
                    Logger.LogInfo($"[DeathEscape] Sending escaped follower loot for {escapedBots.Count} follower(s).");
                    InteractableObjects.SendDeathEscapeFollowerStoredItems(
                        aliveSquadmates.Select(follower => follower.GetBot()),
                        escapedBots);
                }

                SendOutcomes(entries);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to resolve follower death escape outcomes");
                Logger.LogError(ex);
            }
            finally
            {
                ClearFallenSquadmateSnapshots();
            }
        }

        private static float CalculateChance(float extractDistance, int aliveCount, FollowerReadiness readiness, float enemyAveragePower)
        {
            float distanceScore = CalculateDistanceScore(extractDistance);
            float squadScore = Mathf.Clamp01(aliveCount / 3f);
            if (aliveCount == 1)
            {
                squadScore = 0.35f;
            }
            else if (aliveCount == 2)
            {
                squadScore = 0.70f;
            }

            float equipmentScore = CalculateEquipmentScore(readiness.EquipmentPower, enemyAveragePower);
            float medScore = readiness.HasSecureMeds ? 1f : 0f;

            // Weighted survival estimate: route difficulty + follower condition + relative firepower
            // + group cohesion + secure medical supplies. Keep the final value bounded to avoid
            // guaranteed outcomes from noisy runtime data.
            float chance =
                0.20f +
                0.25f * distanceScore +
                0.25f * readiness.HealthRatio +
                0.20f * equipmentScore +
                0.15f * squadScore +
                0.10f * medScore;

            if (readiness.VitalsDestroyed)
            {
                chance *= 0.25f;
            }

            return Mathf.Clamp(chance, MinChance, MaxChance);
        }

        private static float CalculateDistanceScore(float distance)
        {
            if (distance <= 0f)
            {
                return 0.5f;
            }

            return 1f - Mathf.InverseLerp(CloseExtractDistance, FarExtractDistance, distance);
        }

        private static float CalculateEquipmentScore(float followerPower, float enemyAveragePower)
        {
            if (enemyAveragePower <= 0.01f)
            {
                return 0.75f;
            }

            float ratio = Mathf.Clamp(followerPower / enemyAveragePower, 0.25f, 1.25f);
            return Mathf.InverseLerp(0.25f, 1.25f, ratio);
        }

        private static FollowerReadiness SnapshotReadiness(BotOwner bot)
        {
            float current = 0f;
            float maximum = 0f;
            bool vitalsDestroyed = false;

            foreach (EBodyPart part in HealthParts)
            {
                try
                {
                    ValueStruct health = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                    current += Mathf.Max(0f, health.Current);
                    maximum += Mathf.Max(0f, health.Maximum);

                    if ((part == EBodyPart.Head || part == EBodyPart.Chest) && health.Current <= 0f)
                    {
                        vitalsDestroyed = true;
                    }
                }
                catch
                {
                    // Missing body-part data should not break raid-end cleanup.
                }
            }

            return new FollowerReadiness
            {
                HealthRatio = maximum > 0f ? Mathf.Clamp01(current / maximum) : 0f,
                EquipmentPower = bot.AIData?.PowerOfEquipment ?? 0f,
                HasSecureMeds = HasSecureContainerMeds(bot),
                VitalsDestroyed = vitalsDestroyed
            };
        }

        private static bool HasSecureContainerMeds(BotOwner bot)
        {
            try
            {
                Item secureContainer = bot.GetPlayer?.InventoryController?.Inventory?.Equipment?
                    .GetSlot(EquipmentSlot.SecuredContainer)?.ContainedItem;
                if (secureContainer is not SearchableItemItemClass searchable)
                {
                    return false;
                }

                foreach (Item item in searchable.GetAllItems())
                {
                    if (item == null)
                    {
                        continue;
                    }

                    if (item is StimulatorItemClass)
                    {
                        return true;
                    }

                    MedKitComponent medKit = item.GetItemComponent<MedKitComponent>();
                    if (medKit != null && medKit.HpResource > 0f)
                    {
                        return true;
                    }

                    if (item is MedsItemClass)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to inspect follower secure-container meds");
                Logger.LogError(ex);
            }

            return false;
        }

        private static ExtractSnapshot ChooseExtract(pitAIBossPlayer boss, Vector3 deathPosition)
        {
            try
            {
                ExfiltrationPoint[] candidates = GetDeathEscapeExtractCandidates(boss);
                if (candidates == null || candidates.Length == 0)
                {
                    return ExtractSnapshot.Unknown;
                }

                // Prefer simple normal extracts with no active requirements. This is a simulation of
                // a practical squad escape route, not a full key/switch/paid-extract planner.
                ExfiltrationPoint selected = candidates
                    .Where(IsUsableExtract)
                    .OrderBy(point => Vector3.Distance(deathPosition, point.transform.position))
                    .FirstOrDefault();

                if (selected == null)
                {
                    selected = candidates
                        .Where(point => point != null && point.Status != EExfiltrationStatus.NotPresent)
                        .OrderBy(point => Vector3.Distance(deathPosition, point.transform.position))
                        .FirstOrDefault();
                }

                if (selected == null)
                {
                    return ExtractSnapshot.Unknown;
                }

                return new ExtractSnapshot
                {
                    Name = selected.Settings?.Name ?? string.Empty,
                    Distance = Vector3.Distance(deathPosition, selected.transform.position),
                    Position = selected.transform.position,
                    HasPosition = true
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to choose follower death escape extract");
                Logger.LogError(ex);
                return ExtractSnapshot.Unknown;
            }
        }

        private static ExfiltrationPoint[] GetDeathEscapeExtractCandidates(pitAIBossPlayer boss)
        {
            ExfiltrationControllerClass controller = ExfiltrationControllerClass.Instance;
            if (controller == null)
            {
                return Array.Empty<ExfiltrationPoint>();
            }

            bool useAnyExtract = pitFireTeam.teamEscapeUseAnyExtract?.Value != false;
            ExfiltrationPoint[] candidates;

            if (useAnyExtract)
            {
                // Forgiving mode: the squad may route to any normal map extract that is present and usable.
                candidates = controller.ExfiltrationPoints ?? Array.Empty<ExfiltrationPoint>();
                Logger.LogInfo($"[DeathEscape] Using all map extraction points for escape routing. candidates={candidates.Length}");
                return candidates;
            }

            // Strict mode: mirror the player's spawn-side extract assignment from the raid profile.
            Profile profile = boss?.realPlayer?.Profile;
            candidates = profile != null
                ? controller.EligiblePoints(profile)
                : Array.Empty<ExfiltrationPoint>();

            Logger.LogInfo($"[DeathEscape] Using player-assigned extraction points for escape routing. candidates={candidates.Length}");
            return candidates;
        }

        private static bool IsUsableExtract(ExfiltrationPoint point)
        {
            return point != null &&
                   point.Status != EExfiltrationStatus.NotPresent &&
                   point.Requirements != null &&
                   point.Requirements.Length == 0;
        }

        private static void SendOutcomes(List<FollowerDeathEscapeOutcomeEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return;
            }

            // Death escape can mutate teammate profiles after raid end: escaped teammates may save
            // their surviving loadout state, and lost teammates may have gear stripped. Force the
            // My Squad roster to rebuild portraits the next time it is shown.
            Components.SquadControlMenuUi.RequestRosterRefreshOnNextInject();

            Logger.LogInfo(
                "[DeathEscape] Posting escape outcomes: " +
                string.Join(", ", entries.Select(entry => $"{entry.Nickname}={(entry.Escaped ? "escaped" : "lost")}({entry.Chance:P0})")));

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
            var defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            string json = new FollowerDeathEscapeOutcomeRequest
            {
                Entries = entries
            }.ToJson(defaultJsonConverters);

            Task.Run(() =>
            {
                try
                {
                    RequestHandler.PostJson(OutcomeRoute, json);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to send follower death escape outcomes");
                    Logger.LogError(ex);
                }
            });
        }


        private struct FollowerReadiness
        {
            public float HealthRatio;
            public float EquipmentPower;
            public bool HasSecureMeds;
            public bool VitalsDestroyed;
        }

        private struct ExtractSnapshot
        {
            public string Name;
            public float Distance;
            public Vector3 Position;
            public bool HasPosition;

            public static ExtractSnapshot Unknown => new ExtractSnapshot
            {
                Name = string.Empty,
                Distance = -1f,
                Position = Vector3.zero,
                HasPosition = false
            };
        }

        private readonly struct RouteThreatSnapshot
        {
            public RouteThreatSnapshot(float averagePower, int count)
            {
                AveragePower = averagePower;
                Count = count;
            }

            public float AveragePower { get; }
            public int Count { get; }

            public static RouteThreatSnapshot Empty => new RouteThreatSnapshot(0f, 0);
        }

        private readonly struct RecoverableGearCandidate
        {
            public RecoverableGearCandidate(
                Item item,
                Vector3 position,
                string ownerId,
                string ownerName,
                bool isPlayer,
                EquipmentSlot slot,
                int itemPriority,
                int sequence,
                bool useAsRecoveryCapacity,
                bool countsAsBackpackCarry,
                bool ignoreCarryWeight = false,
                string coveredByItemId = null)
            {
                Item = item;
                Position = position;
                OwnerId = ownerId;
                OwnerName = ownerName;
                IsPlayer = isPlayer;
                Slot = slot;
                ItemPriority = itemPriority;
                Sequence = sequence;
                UseAsRecoveryCapacity = useAsRecoveryCapacity;
                CountsAsBackpackCarry = countsAsBackpackCarry;
                IgnoreCarryWeight = ignoreCarryWeight;
                CoveredByItemId = coveredByItemId;
            }

            public Item Item { get; }
            public Vector3 Position { get; }
            public string OwnerId { get; }
            public string OwnerName { get; }
            public bool IsPlayer { get; }
            public EquipmentSlot Slot { get; }
            public int OwnerPriority => IsPlayer ? 0 : 1;
            public int ItemPriority { get; }
            public int Sequence { get; }
            public bool UseAsRecoveryCapacity { get; }
            public bool CountsAsBackpackCarry { get; }
            public bool IgnoreCarryWeight { get; }
            public string CoveredByItemId { get; }

            public RecoverableGearCandidate WithSequence(int sequence)
            {
                return new RecoverableGearCandidate(
                    Item,
                    Position,
                    OwnerId,
                    OwnerName,
                    IsPlayer,
                    Slot,
                    ItemPriority,
                    sequence,
                    UseAsRecoveryCapacity,
                    CountsAsBackpackCarry,
                    IgnoreCarryWeight,
                    CoveredByItemId);
            }
        }

        private sealed class RecoveryAttemptStats
        {
            public int NoNearbyCarrier { get; set; }
            public int NoEquipmentSnapshot { get; set; }
            public int OverWeight { get; set; }
            public int BackpackLimit { get; set; }
            public int NoSpace { get; set; }
        }

        private sealed class RecoveryCarrierState
        {
            public RecoveryCarrierState(InventoryEquipment equipment, float maxWeightKg)
            {
                Equipment = equipment;
                MaxWeightKg = maxWeightKg;
                CurrentWeightKg = GetCarrierStartingWeightKg(equipment);
                BackpackCarryCapacity = equipment.GetSlot(EquipmentSlot.Backpack)?.ContainedItem == null ? 2 : 1;
            }

            public InventoryEquipment Equipment { get; }
            public float MaxWeightKg { get; }
            public float CurrentWeightKg { get; private set; }
            public int BackpackCarryCapacity { get; }
            public int RecoveredBackpacks { get; private set; }
            public List<SearchableItemItemClass> ExternallyCarriedBackpacks { get; } = new List<SearchableItemItemClass>();

            public bool CanCarryWeight(float itemWeight)
            {
                return CurrentWeightKg + itemWeight <= MaxWeightKg;
            }

            public bool CanCarryBackpack(RecoverableGearCandidate candidate)
            {
                return !candidate.CountsAsBackpackCarry || RecoveredBackpacks < BackpackCarryCapacity;
            }

            public void RecordRecovered(RecoverableGearCandidate candidate, float itemWeight)
            {
                CurrentWeightKg += itemWeight;
                if (candidate.CountsAsBackpackCarry)
                {
                    RecoveredBackpacks++;
                }
            }

            public void AddExternallyCarriedBackpack(Item item)
            {
                if (item is SearchableItemItemClass backpack)
                {
                    ExternallyCarriedBackpacks.Add(backpack);
                }
            }
        }

        private sealed class FallenSquadmateInfo
        {
            public string Aid { get; set; } = string.Empty;
            public string ProfileId { get; set; } = string.Empty;
            public string Nickname { get; set; } = string.Empty;
            public Vector3 Position { get; set; }
        }

        private sealed class LostOnDeathRules
        {
            public LostOnDeathRules(Dictionary<string, bool> equipment, bool playerGearProtectedByRaidStatusOverride = false)
            {
                Equipment = equipment ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                PlayerGearProtectedByRaidStatusOverride = playerGearProtectedByRaidStatusOverride;
            }

            public static LostOnDeathRules KeepAll { get; } = new LostOnDeathRules(new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

            private Dictionary<string, bool> Equipment { get; }
            public bool PlayerGearProtectedByRaidStatusOverride { get; }

            public int LostEquipmentSlotCount => Equipment.Count(pair => pair.Value);

            public bool IsEquipmentSlotLost(EquipmentSlot slot)
            {
                string key = slot.ToString();
                return Equipment.TryGetValue(key, out bool lost) && lost;
            }
        }
    }
}
