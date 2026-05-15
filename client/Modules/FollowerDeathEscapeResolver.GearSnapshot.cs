using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class FollowerDeathEscapeResolver
    {
        private static readonly EquipmentSlot[] RecoverableGearSlots =
        {
            EquipmentSlot.FirstPrimaryWeapon,
            EquipmentSlot.ArmorVest,
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Headwear,
            EquipmentSlot.SecondPrimaryWeapon,
            EquipmentSlot.Holster,
            EquipmentSlot.Backpack,
            EquipmentSlot.Earpiece,
            EquipmentSlot.FaceCover,
            EquipmentSlot.Eyewear
        };

        private static readonly object FallenSnapshotLock = new object();
        private static readonly List<RecoverableGearCandidate> FallenSquadmateSnapshots = new List<RecoverableGearCandidate>();
        private static readonly List<FallenSquadmateInfo> FallenSquadmateInfos = new List<FallenSquadmateInfo>();

        public static void RecordFallenSquadmate(Player player)
        {
            if (player == null || string.IsNullOrWhiteSpace(player.ProfileId))
            {
                return;
            }

            try
            {
                HashSet<string> trackedLootIds = new HashSet<string>(
                    InteractableObjects.GetStoredItems(player.ProfileId) ?? Enumerable.Empty<string>(),
                    StringComparer.Ordinal);

                string ownerId = player.Profile?.AccountId ?? player.ProfileId;
                List<RecoverableGearCandidate> candidates = GetRecoverableTopLevelGear(
                        player,
                        trackedLootIds,
                        ownerId,
                        player.Profile?.Nickname ?? "Squadmate",
                        false,
                        0,
                        LostOnDeathRules.KeepAll)
                    .ToList();

                lock (FallenSnapshotLock)
                {
                    FallenSquadmateSnapshots.RemoveAll(candidate =>
                        string.Equals(candidate.OwnerId, ownerId, StringComparison.Ordinal));
                    FallenSquadmateSnapshots.AddRange(candidates);

                    FallenSquadmateInfos.RemoveAll(info =>
                        string.Equals(info.Aid, ownerId, StringComparison.Ordinal) ||
                        string.Equals(info.ProfileId, player.ProfileId, StringComparison.Ordinal));
                    FallenSquadmateInfos.Add(new FallenSquadmateInfo
                    {
                        Aid = ownerId,
                        ProfileId = player.ProfileId,
                        Nickname = player.Profile?.Nickname ?? "Squadmate",
                        Position = player.Position
                    });
                }

                Logger.LogInfo(
                    $"[DeathEscape] Recorded fallen squadmate gear snapshot for '{player.Profile?.Nickname ?? player.ProfileId}' " +
                    $"with {candidates.Count} recoverable item(s).");
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to record fallen squadmate gear snapshot.");
                Logger.LogError(ex);
            }
        }

        public static void ClearFallenSquadmateSnapshots()
        {
            lock (FallenSnapshotLock)
            {
                FallenSquadmateSnapshots.Clear();
                FallenSquadmateInfos.Clear();
            }
        }

        private static void AddMissingFallenSquadmateOutcomes(
            List<FollowerDeathEscapeOutcomeEntry> entries,
            Vector3 playerDeathPosition)
        {
            if (entries == null)
            {
                return;
            }

            List<FallenSquadmateInfo> fallenInfos;
            lock (FallenSnapshotLock)
            {
                fallenInfos = FallenSquadmateInfos.ToList();
            }

            if (fallenInfos.Count == 0)
            {
                return;
            }

            HashSet<string> knownIds = new HashSet<string>(
                entries.SelectMany(entry => new[] { entry.Aid, entry.ProfileId })
                    .Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);

            foreach (FallenSquadmateInfo fallen in fallenInfos)
            {
                if (fallen == null ||
                    string.IsNullOrWhiteSpace(fallen.Aid) ||
                    knownIds.Contains(fallen.Aid) ||
                    (!string.IsNullOrWhiteSpace(fallen.ProfileId) && knownIds.Contains(fallen.ProfileId)) ||
                    !IsNear(fallen.Position, playerDeathPosition, FallenTeammateSnapshotRadius))
                {
                    continue;
                }

                // A teammate who died before the player does not get an escape roll, but the server
                // still needs a lost outcome so Immersive/Realistic can strip their saved gear.
                entries.Add(new FollowerDeathEscapeOutcomeEntry
                {
                    Aid = fallen.Aid,
                    ProfileId = fallen.ProfileId,
                    Nickname = fallen.Nickname,
                    Escaped = false,
                    Chance = 0f,
                    ExtractName = string.Empty,
                    Distance = 0f,
                    HealthRatio = 0f,
                    EquipmentPower = 0f,
                    EnemyAveragePower = 0f,
                    AliveSquadmates = 0,
                    HasSecureMeds = false,
                    EquipmentItems = null,
                    TrackedItemIds = Array.Empty<string>()
                });
                knownIds.Add(fallen.Aid);
                if (!string.IsNullOrWhiteSpace(fallen.ProfileId))
                {
                    knownIds.Add(fallen.ProfileId);
                }

                Logger.LogInfo($"[DeathEscape] Added lost outcome for fallen squadmate '{fallen.Nickname}'.");
            }
        }

        private static LostOnDeathRules LoadLostOnDeathRules()
        {
            try
            {
                string responseJson = RequestHandler.GetJson(LostOnDeathRoute);
                if (string.IsNullOrWhiteSpace(responseJson))
                {
                    return LostOnDeathRules.KeepAll;
                }

                JObject root = JObject.Parse(responseJson);
                JToken configToken = root["data"] ?? root;
                Dictionary<string, bool> equipment = configToken["equipment"]?.ToObject<Dictionary<string, bool>>()
                    ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

                Logger.LogInfo($"[DeathEscape] Loaded lost-on-death rules for {equipment.Count} equipment slot(s).");
                return new LostOnDeathRules(equipment);
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to load SPT lost-on-death rules; player gear recovery will be skipped to avoid duplicates.");
                Logger.LogError(ex);
                return LostOnDeathRules.KeepAll;
            }
        }

        private static List<RecoverableGearCandidate> CreateDeathGearSnapshot(
            IEnumerable<BotOwner> squadBots,
            Player player,
            HashSet<string> trackedLootIds,
            Vector3 playerDeathPosition,
            LostOnDeathRules lostOnDeathRules)
        {
            List<RecoverableGearCandidate> snapshot = new List<RecoverableGearCandidate>();
            HashSet<string> seenItemIds = new HashSet<string>(StringComparer.Ordinal);
            int sequence = 0;

            List<RecoverableGearCandidate> fallenSnapshots;
            lock (FallenSnapshotLock)
            {
                fallenSnapshots = FallenSquadmateSnapshots.ToList();
            }

            // Fallen teammates are captured at their own death time, then filtered by distance
            // from the player's death position before recovery packing begins.
            foreach (RecoverableGearCandidate fallenCandidate in fallenSnapshots)
            {
                if (!IsNear(fallenCandidate.Position, playerDeathPosition, FallenTeammateSnapshotRadius))
                {
                    continue;
                }

                RecoverableGearCandidate candidate = fallenCandidate.WithSequence(sequence++);
                if (candidate.Item == null || !seenItemIds.Add(candidate.Item.Id))
                {
                    continue;
                }

                snapshot.Add(candidate);
            }

            // Snapshot squadmates at player-death time. Living squadmates are always included
            // because they may die during the simulated escape. Already-fallen squadmates only
            // count if they died close enough for survivors to plausibly grab their gear.
            foreach (BotOwner bot in squadBots ?? Enumerable.Empty<BotOwner>())
            {
                if (bot?.GetPlayer == null)
                {
                    continue;
                }

                bool aliveAtPlayerDeath = bot.BotState == EBotState.Active &&
                                          bot.HealthController?.IsAlive == true;
                if (!aliveAtPlayerDeath && !IsNear(bot.GetPlayer.Position, playerDeathPosition, FallenTeammateSnapshotRadius))
                {
                    continue;
                }

                foreach (RecoverableGearCandidate candidate in GetRecoverableTopLevelGear(
                             bot.GetPlayer,
                             trackedLootIds,
                             bot.Profile?.AccountId ?? bot.ProfileId ?? string.Empty,
                             bot.Profile?.Nickname ?? "Squadmate",
                             false,
                             sequence,
                             lostOnDeathRules))
                {
                    sequence = candidate.Sequence + 1;
                    if (candidate.Item == null || !seenItemIds.Add(candidate.Item.Id))
                    {
                        continue;
                    }

                    snapshot.Add(candidate);
                }
            }

            if (player != null)
            {
                foreach (RecoverableGearCandidate candidate in GetRecoverableTopLevelGear(
                             player,
                             trackedLootIds,
                             player.Profile?.AccountId ?? player.ProfileId ?? "player",
                             player.Profile?.Nickname ?? "Player",
                             true,
                             sequence,
                             lostOnDeathRules))
                {
                    sequence = candidate.Sequence + 1;
                    if (candidate.Item == null || !seenItemIds.Add(candidate.Item.Id))
                    {
                        continue;
                    }

                    snapshot.Add(candidate);
                }
            }

            Logger.LogInfo(
                $"[DeathEscape] Captured death gear snapshot with {snapshot.Count} recoverable top-level item(s): " +
                $"player={snapshot.Count(candidate => candidate.IsPlayer)} squad={snapshot.Count(candidate => !candidate.IsPlayer)} " +
                $"trackedLootIds={trackedLootIds?.Count ?? 0} playerLostSlots={lostOnDeathRules?.LostEquipmentSlotCount ?? 0}.");
            return snapshot;
        }

        private static IEnumerable<RecoverableGearCandidate> GetRecoverableTopLevelGear(
            Player owner,
            HashSet<string> trackedLootIds,
            string ownerId,
            string ownerName,
            bool isPlayer,
            int startSequence,
            LostOnDeathRules lostOnDeathRules)
        {
            InventoryEquipment equipment = owner?.InventoryController?.Inventory?.Equipment;
            if (equipment == null)
            {
                yield break;
            }

            int sequence = startSequence;
            foreach (EquipmentSlot slot in RecoverableGearSlots)
            {
                if (isPlayer && lostOnDeathRules?.IsEquipmentSlotLost(slot) != true)
                {
                    continue;
                }

                Item item = equipment.GetSlot(slot)?.ContainedItem;
                if (item == null || IsNonRecoverableDeathEscapeItem(item) || IsTrackedLootItem(item, trackedLootIds))
                {
                    continue;
                }

                // Store an isolated clone now. Later raid-end cleanup or simulated follower
                // deaths must not affect what was available at the moment the player died.
                Item clone = item.CloneItemWithSameId();
                if (slot == EquipmentSlot.Backpack || slot == EquipmentSlot.TacticalVest)
                {
                    // Container grid contents have their own recovery priority. Keep armor plates
                    // and other slotted parts, but do not let rig/backpack contents ride along with
                    // the shell.
                    StripGridContents(clone);
                }

                yield return new RecoverableGearCandidate(
                    clone,
                    owner.Position,
                    ownerId,
                    ownerName,
                    isPlayer,
                    slot,
                    GetDeathGearItemPriority(slot, clone),
                    sequence++,
                    slot == EquipmentSlot.Backpack && !isPlayer);

                if (slot == EquipmentSlot.TacticalVest || slot == EquipmentSlot.Backpack)
                {
                    foreach (Item containedItem in GetRecoverableGridContents(item, trackedLootIds))
                    {
                        yield return new RecoverableGearCandidate(
                            containedItem.CloneItemWithSameId(),
                            owner.Position,
                            ownerId,
                            ownerName,
                            isPlayer,
                            slot,
                            slot == EquipmentSlot.TacticalVest ? 5 : 7,
                            sequence++,
                            false);
                    }
                }
            }
        }

        private static IEnumerable<Item> GetRecoverableGridContents(Item containerItem, HashSet<string> trackedLootIds)
        {
            if (containerItem is not CompoundItem compound || compound.Grids == null)
            {
                yield break;
            }

            foreach (StashGridClass grid in compound.Grids)
            {
                if (grid?.Items == null)
                {
                    continue;
                }

                foreach (Item item in grid.Items)
                {
                    if (item == null || IsNonRecoverableDeathEscapeItem(item) || IsTrackedLootTree(item, trackedLootIds))
                    {
                        continue;
                    }

                    yield return item;
                }
            }
        }

        private static void StripGridContents(Item item)
        {
            if (item is not CompoundItem compound || compound.Grids == null)
            {
                return;
            }

            foreach (StashGridClass grid in compound.Grids)
            {
                grid?.RemoveAll();
            }
        }

        private static HashSet<string> BuildTrackedLootIdSet(IEnumerable<BotOwner> squadBots)
        {
            HashSet<string> tracked = new HashSet<string>(StringComparer.Ordinal);
            foreach (BotOwner bot in squadBots ?? Enumerable.Empty<BotOwner>())
            {
                if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
                {
                    continue;
                }

                foreach (string itemId in InteractableObjects.GetStoredItems(bot.ProfileId) ?? Enumerable.Empty<string>())
                {
                    tracked.Add(itemId);
                }
            }

            return tracked;
        }

        private static bool IsTrackedLootTree(Item item, HashSet<string> trackedLootIds)
        {
            if (item == null || trackedLootIds == null || trackedLootIds.Count == 0)
            {
                return false;
            }

            if (trackedLootIds.Contains(item.Id))
            {
                return true;
            }

            if (item is CompoundItem compound)
            {
                foreach (Item child in compound.GetAllItems())
                {
                    if (child != null && trackedLootIds.Contains(child.Id))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsTrackedLootItem(Item item, HashSet<string> trackedLootIds)
        {
            return item != null &&
                   trackedLootIds != null &&
                   trackedLootIds.Count > 0 &&
                   trackedLootIds.Contains(item.Id);
        }

        private static bool IsNonRecoverableDeathEscapeItem(Item item)
        {
            if (item == null)
            {
                return true;
            }

            return item.IsSpecialSlotOnly;
        }

        private static bool IsNear(Vector3 a, Vector3 b, float maxDistance)
        {
            return (a - b).sqrMagnitude <= maxDistance * maxDistance;
        }
    }
}
