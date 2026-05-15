using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace pitTeam.Modules
{
    internal static partial class FollowerDeathEscapeResolver
    {
        private static void ApplyDeathGearRecoveryToEscapedEquipment(
            List<FollowerDeathEscapeOutcomeEntry> entries,
            Dictionary<string, BotOwner> entryBotsByAid,
            List<BotOwner> escapedBots,
            List<RecoverableGearCandidate> deathGearSnapshot)
        {
            if (entries == null || entries.Count == 0 || escapedBots == null || escapedBots.Count == 0)
            {
                return;
            }

            try
            {
                bool recoverTeammateGear = pitFireTeam.IsFollowerLoadoutLootableMode();
                Dictionary<string, InventoryEquipment> escapedEquipment = new Dictionary<string, InventoryEquipment>(StringComparer.Ordinal);
                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (!entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        continue;
                    }

                    InventoryEquipment equipmentClone = CloneFollowerEquipment(bot);
                    if (equipmentClone != null)
                    {
                        escapedEquipment[entry.Aid] = equipmentClone;
                    }
                }

                HashSet<string> escapedOwnerIds = new HashSet<string>(
                    entries
                        .Where(entry => entry.Escaped)
                        .SelectMany(entry => new[] { entry.Aid, entry.ProfileId })
                        .Where(id => !string.IsNullOrWhiteSpace(id)),
                    StringComparer.Ordinal);

                int recovered = 0;
                List<Item> gearToReturn = new List<Item>();
                HashSet<string> recoveredGearIds = new HashSet<string>(StringComparer.Ordinal);
                RecoveryAttemptStats stats = new RecoveryAttemptStats();

                // Player gear is recoverable in every mode. Fallen teammate gear is recoverable
                // only in Immersive/Realistic. In both cases, escaped teammates only provide the
                // carry-space simulation; anything they successfully carry is returned by mail.
                List<RecoverableGearCandidate> recoverableCandidates = deathGearSnapshot
                    .Where(candidate => candidate.IsPlayer ||
                                        (recoverTeammateGear && !escapedOwnerIds.Contains(candidate.OwnerId)))
                    .ToList();

                Logger.LogInfo(
                    $"[DeathEscape] Death gear recovery start. teammateGearRecovery={recoverTeammateGear} " +
                    $"escapedCarriers={escapedBots.Count} equipmentSnapshots={escapedEquipment.Count} " +
                    $"snapshotItems={deathGearSnapshot.Count} candidateItems={recoverableCandidates.Count} " +
                    $"pickupRadius={DeathGearRecoveryDistance:0}m");

                // Fallen teammate backpack shells are packed before priority items so they can add
                // container space for the rest of the recovery simulation.
                foreach (RecoverableGearCandidate candidate in recoverableCandidates
                             .Where(candidate => candidate.UseAsRecoveryCapacity)
                             .OrderBy(candidate => candidate.OwnerPriority)
                             .ThenBy(candidate => candidate.Sequence))
                {
                    if (TryRecoverDeathGearCandidate(candidate, escapedBots, escapedEquipment, stats))
                    {
                        recovered++;
                        gearToReturn.Add(candidate.Item.CloneItemWithSameId());
                        recoveredGearIds.Add(candidate.Item.Id);
                    }
                }

                foreach (RecoverableGearCandidate candidate in recoverableCandidates
                             .Where(candidate => !candidate.UseAsRecoveryCapacity)
                             .OrderBy(candidate => candidate.OwnerPriority)
                             .ThenBy(candidate => candidate.ItemPriority)
                             .ThenBy(candidate => candidate.Sequence))
                {
                    if (!string.IsNullOrWhiteSpace(candidate.CoveredByItemId) &&
                        recoveredGearIds.Contains(candidate.CoveredByItemId))
                    {
                        Logger.LogInfo(
                            $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                            "parent carrier item was already recovered with its contents.");
                        continue;
                    }

                    if (TryRecoverDeathGearCandidate(candidate, escapedBots, escapedEquipment, stats))
                    {
                        recovered++;
                        gearToReturn.Add(candidate.Item.CloneItemWithSameId());
                        recoveredGearIds.Add(candidate.Item.Id);
                    }
                }

                if (recoveredGearIds.Count > 0)
                {
                    // Recovered death gear is mailed to the player. Strip it back out of the
                    // simulated carrier equipment before that snapshot is sent to the server, so
                    // escaped teammate profiles keep only their own surviving loadout state.
                    RemoveRecoveredDeathGearFromEscapedEquipment(escapedEquipment.Values, recoveredGearIds);
                }

                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (recoverTeammateGear && escapedEquipment.TryGetValue(entry.Aid, out InventoryEquipment equipment))
                    {
                        entry.EquipmentItems = SerializeEquipment(equipment);
                    }
                    else if (entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        entry.EquipmentItems = SerializeFollowerEquipment(bot);
                    }
                }

                if (recovered > 0)
                {
                    Logger.LogInfo($"[DeathEscape] Recovered {recovered} nearby death-gear item(s) in carry simulation.");
                }
                else if (recoverableCandidates.Count > 0)
                {
                    Logger.LogInfo(
                        $"[DeathEscape] No death gear was recovered. noNearby={stats.NoNearbyCarrier} " +
                        $"missingEquipmentSnapshot={stats.NoEquipmentSnapshot} noSpace={stats.NoSpace}");
                }

                if (gearToReturn.Count > 0)
                {
                    Logger.LogInfo($"[DeathEscape] Sending {gearToReturn.Count} recovered death-gear item(s) through return mail.");
                    InteractableObjects.SendDeathEscapeRecoveredGear(gearToReturn);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to apply nearby death gear recovery.");
                Logger.LogError(ex);

                foreach (FollowerDeathEscapeOutcomeEntry entry in entries.Where(entry => entry.Escaped))
                {
                    if (entryBotsByAid.TryGetValue(entry.Aid, out BotOwner bot))
                    {
                        entry.EquipmentItems = SerializeFollowerEquipment(bot);
                    }
                }
            }
        }

        private static bool TryRecoverDeathGearCandidate(
            RecoverableGearCandidate candidate,
            List<BotOwner> escapedBots,
            Dictionary<string, InventoryEquipment> escapedEquipment,
            RecoveryAttemptStats stats)
        {
            List<BotOwner> nearbyCarriers = escapedBots
                .Where(bot => bot != null && IsNear(bot.Position, candidate.Position, DeathGearRecoveryDistance))
                .OrderBy(bot => Vector3.Distance(bot.Position, candidate.Position))
                .ToList();

            if (nearbyCarriers.Count == 0)
            {
                stats.NoNearbyCarrier++;
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    $"no escaped teammate within {DeathGearRecoveryDistance:0}m.");
                return false;
            }

            bool hadEquipmentSnapshot = false;
            foreach (BotOwner escapedBot in nearbyCarriers)
            {
                string aid = escapedBot.Profile?.AccountId ?? string.Empty;
                if (string.IsNullOrWhiteSpace(aid) || !escapedEquipment.TryGetValue(aid, out InventoryEquipment equipment))
                {
                    stats.NoEquipmentSnapshot++;
                    continue;
                }

                hadEquipmentSnapshot = true;
                // Candidate.Item is already a player-death snapshot. Clone again so a failed
                // placement attempt cannot mutate the stored snapshot before another survivor tries.
                Item clone = candidate.Item.CloneItemWithSameId();
                if (!TryPlaceRecoveredGear(equipment, candidate, clone))
                {
                    continue;
                }

                Logger.LogInfo(
                    $"[DeathEscape] Recovered death gear '{candidate.Item.ShortName?.Localized(null) ?? candidate.Item.Name ?? candidate.Item.Id}' " +
                    $"from '{candidate.OwnerName}' into '{escapedBot.Profile?.Nickname ?? "Squadmate"}'.");
                return true;
            }

            if (hadEquipmentSnapshot)
            {
                stats.NoSpace++;
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    "nearby escaped teammates had no valid equipment slot or container space.");
            }
            else
            {
                Logger.LogInfo(
                    $"[DeathEscape] Skipped death gear '{DescribeRecoverableItem(candidate)}' from '{candidate.OwnerName}': " +
                    "nearby escaped teammates had no serializable equipment snapshot.");
            }

            return false;
        }

        private static InventoryEquipment CloneFollowerEquipment(BotOwner bot)
        {
            try
            {
                return bot?.GetPlayer?.InventoryController?.Inventory?.Equipment?.CloneItemWithSameId() as InventoryEquipment;
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeathEscape] Failed to clone escaped follower equipment for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}'.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static void RemoveRecoveredDeathGearFromEscapedEquipment(
            IEnumerable<InventoryEquipment> escapedEquipment,
            HashSet<string> recoveredGearIds)
        {
            if (escapedEquipment == null || recoveredGearIds == null || recoveredGearIds.Count == 0)
            {
                return;
            }

            foreach (InventoryEquipment equipment in escapedEquipment)
            {
                if (equipment == null)
                {
                    continue;
                }

                foreach (Item item in equipment.GetAllItems()
                             .Where(item => item != null && recoveredGearIds.Contains(item.Id))
                             .ToList())
                {
                    try
                    {
                        item.Parent?.Remove(item, false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[DeathEscape] Failed to strip mailed death gear '{item.Id}' from escaped teammate equipment snapshot.");
                        Logger.LogError(ex);
                    }
                }
            }
        }

        private static bool TryPlaceRecoveredGear(InventoryEquipment equipment, RecoverableGearCandidate candidate, Item item)
        {
            return TryEquipRecoveredGear(equipment, candidate, item) || TryPackRecoveredGear(equipment, item);
        }

        private static bool TryEquipRecoveredGear(InventoryEquipment equipment, RecoverableGearCandidate candidate, Item item)
        {
            if (equipment == null || item == null)
            {
                return false;
            }

            foreach (EquipmentSlot slot in GetRecoveryEquipmentSlotOrder(candidate.Slot))
            {
                Slot equipmentSlot = equipment.GetSlot(slot);
                if (equipmentSlot?.ContainedItem != null)
                {
                    continue;
                }

                if (equipmentSlot?.Add(item, false).Succeeded == true)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<EquipmentSlot> GetRecoveryEquipmentSlotOrder(EquipmentSlot sourceSlot)
        {
            switch (sourceSlot)
            {
                case EquipmentSlot.FirstPrimaryWeapon:
                    yield return EquipmentSlot.SecondPrimaryWeapon;
                    yield return EquipmentSlot.FirstPrimaryWeapon;
                    break;
                case EquipmentSlot.SecondPrimaryWeapon:
                    yield return EquipmentSlot.SecondPrimaryWeapon;
                    yield return EquipmentSlot.FirstPrimaryWeapon;
                    break;
                case EquipmentSlot.Holster:
                    yield return EquipmentSlot.Holster;
                    break;
                case EquipmentSlot.ArmorVest:
                    yield return EquipmentSlot.ArmorVest;
                    break;
                case EquipmentSlot.TacticalVest:
                    yield return EquipmentSlot.TacticalVest;
                    break;
                case EquipmentSlot.Headwear:
                    yield return EquipmentSlot.Headwear;
                    break;
                case EquipmentSlot.Earpiece:
                    yield return EquipmentSlot.Earpiece;
                    break;
                case EquipmentSlot.FaceCover:
                    yield return EquipmentSlot.FaceCover;
                    break;
                case EquipmentSlot.Eyewear:
                    yield return EquipmentSlot.Eyewear;
                    break;
                case EquipmentSlot.Backpack:
                    yield return EquipmentSlot.Backpack;
                    break;
            }
        }

        private static bool TryPackRecoveredGear(InventoryEquipment equipment, Item item)
        {
            if (equipment == null || item == null)
            {
                return false;
            }

            foreach (SearchableItemItemClass container in GetRecoveryContainers(equipment))
            {
                if (container?.Grids == null)
                {
                    continue;
                }

                foreach (StashGridClass grid in container.Grids)
                {
                    if (grid?.AddAnywhere(item, EErrorHandlingType.Ignore).Succeeded == true)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<SearchableItemItemClass> GetRecoveryContainers(InventoryEquipment equipment)
        {
            HashSet<string> yielded = new HashSet<string>(StringComparer.Ordinal);
            foreach (EquipmentSlot slot in GetRecoveryContainerOrder())
            {
                SearchableItemItemClass rootContainer = equipment.GetSlot(slot)?.ContainedItem as SearchableItemItemClass;
                if (rootContainer == null || !yielded.Add(rootContainer.Id))
                {
                    continue;
                }

                yield return rootContainer;

                // If a recovered backpack/container was packed into another container, use
                // its grids too. This is how fallen teammates' backpacks expand survivor space.
                foreach (SearchableItemItemClass nested in rootContainer.GetAllItems().OfType<SearchableItemItemClass>())
                {
                    if (nested != null && yielded.Add(nested.Id))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static IEnumerable<EquipmentSlot> GetRecoveryContainerOrder()
        {
            yield return EquipmentSlot.Backpack;
            yield return EquipmentSlot.TacticalVest;
            yield return EquipmentSlot.Pockets;

            if (pitFireTeam.IsFollowerLoadoutRealisticMode())
            {
                yield return EquipmentSlot.SecuredContainer;
            }
        }

        private static int GetDeathGearItemPriority(EquipmentSlot slot, Item item)
        {
            if (slot == EquipmentSlot.FirstPrimaryWeapon)
            {
                return 0;
            }

            if (slot == EquipmentSlot.ArmorVest || IsArmoredVest(slot, item))
            {
                return 1;
            }

            if (slot == EquipmentSlot.Headwear)
            {
                return 2;
            }

            if (slot == EquipmentSlot.SecondPrimaryWeapon)
            {
                return 3;
            }

            if (slot == EquipmentSlot.Holster)
            {
                return 4;
            }

            return 6;
        }

        private static bool IsArmoredVest(EquipmentSlot slot, Item item)
        {
            if (slot != EquipmentSlot.TacticalVest || item == null)
            {
                return false;
            }

            return item.TryGetItemComponent<ArmorHolderComponent>(out _)
                   || item.GetItemComponentsInChildren<ArmorComponent>(true).Any()
                   || item.GetItemComponentsInChildren<CompositeArmorComponent>(true).Any();
        }

        private static string DescribeRecoverableItem(RecoverableGearCandidate candidate)
        {
            Item item = candidate.Item;
            string itemName = item?.ShortName?.Localized(null) ?? item?.Name ?? item?.Id ?? "unknown";
            return $"{itemName}/{candidate.Slot}";
        }

        private static FlatItemsDataClass[] SerializeFollowerEquipment(BotOwner bot)
        {
            try
            {
                Item equipment = bot?.GetPlayer?.InventoryController?.Inventory?.Equipment;
                if (equipment == null)
                {
                    return null;
                }

                return Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { equipment });
            }
            catch (Exception ex)
            {
                Logger.LogError($"[DeathEscape] Failed to serialize escaped follower equipment for '{bot?.Profile?.Nickname ?? bot?.ProfileId ?? "unknown"}'.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static FlatItemsDataClass[] SerializeEquipment(InventoryEquipment equipment)
        {
            try
            {
                return equipment == null
                    ? null
                    : Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(new Item[] { equipment });
            }
            catch (Exception ex)
            {
                Logger.LogError("[DeathEscape] Failed to serialize recovered escaped follower equipment.");
                Logger.LogError(ex);
                return null;
            }
        }

        private static string[] GetTrackedFollowerItemIds(BotOwner bot)
        {
            if (bot == null || string.IsNullOrEmpty(bot.ProfileId))
            {
                return Array.Empty<string>();
            }

            return InteractableObjects.GetStoredItems(bot.ProfileId)?.ToArray() ?? Array.Empty<string>();
        }
    }
}
