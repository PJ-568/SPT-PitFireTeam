using EFT;
using EFT.InventoryLogic;
using EFT.UI.Screens;
using pitTeam.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace pitTeam.Modules
{
    /// <summary>
    /// Owns the in-raid "View Backpack" teammate interaction.
    ///
    /// This deliberately opens the follower's live backpack through the same inventory UI path used for loot
    /// containers, but it treats the backpack as already searched for the local player. The interaction is
    /// scoped to spawned squad followers only; recruited/picked-up allies are filtered out by IsSquadMate.
    /// </summary>
    internal static class TeammateBackpackInspection
    {
        private const float MaxInteractionDistance = 2.5f;
        private const float QuickInteractionMaxAngle = 18f;

        private static GamePlayerOwner? _owner;
        private static BotOwner? _targetBot;
        private static BotFollowerPlayer? _targetFollower;
        private static SearchableItemItemClass? _targetBackpack;
        private static HashSet<string>? _initialBackpackItemIds;
        private static HashSet<string>? _initialTrackedItemIds;
        private static float _openedAtTime;
        private static bool _closeRequested;
        private static bool _ending;

        public static void Update(GamePlayerOwner owner)
        {
            if (_owner == null || _targetBot == null || _targetFollower == null)
            {
                return;
            }

            if (!ReferenceEquals(owner, _owner))
            {
                return;
            }

            try
            {
                if (_closeRequested)
                {
                    if (!CurrentScreenSingletonClass.Instance.CheckCurrentScreen(EEftScreenType.Inventory))
                    {
                        EndInspection("InterruptClosed", true);
                        return;
                    }
                }

                if (!CurrentScreenSingletonClass.Instance.CheckCurrentScreen(EEftScreenType.Inventory))
                {
                    if (!_closeRequested && Time.time - _openedAtTime < 1f)
                    {
                        return;
                    }

                    EndInspection("ScreenClosed", true);
                    return;
                }

                if (owner.Player?.HealthController?.IsAlive != true)
                {
                    RequestClose("PlayerDead");
                    return;
                }

                if (!IsInspectableBotActive(_targetBot, _targetFollower))
                {
                    RequestClose("TargetInvalid");
                    return;
                }

                // Medical and pickup work are animation/inventory-operation owners. Interrupting those by opening
                // a second transfer UI can leave EFT's operation pipeline in an inconsistent state.
                if (HasActiveOrPendingHealWork(_targetBot))
                {
                    RequestClose("FollowerHealing");
                    return;
                }

                if (HasActiveOrPendingPickupWork(_targetBot, _targetFollower))
                {
                    RequestClose("FollowerPickup");
                    return;
                }

                if (_targetFollower.HasKnownEnemy() || _targetBot.Memory?.HaveEnemy == true || _targetBot.Memory?.IsUnderFire == true)
                {
                    RequestClose("CombatInterrupt");
                    return;
                }

            }
            catch (Exception ex)
            {
                Logger.LogError("[TeammateBackpack] Failed while updating backpack inspection");
                Logger.LogError(ex);
                EndInspection("UpdateFailed", true);
            }
        }

        public static bool CanShowQuickInteraction(Player player)
        {
            return TryGetInspectableBackpack(player, out _, out _, out _);
        }

        public static bool TryOpenFromQuickInteraction(GamePlayerOwner owner)
        {
            try
            {
                if (owner == null ||
                    !TryGetInspectableBackpack(owner, out BotOwner targetBot, out _, out _))
                {
                    return false;
                }

                TryOpen(owner, targetBot);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("[TeammateBackpack] Failed to open backpack from quick interaction");
                Logger.LogError(ex);
                return false;
            }
        }

        private static void TryOpen(GamePlayerOwner owner, BotOwner targetBot)
        {
            try
            {
                if (!TryGetInspectableBackpack(owner, out BotOwner liveBot, out BotFollowerPlayer follower, out SearchableItemItemClass backpack) ||
                    liveBot != targetBot)
                {
                    return;
                }

                if (_owner != null)
                {
                    EndInspection("Replaced", true);
                }

                _owner = owner;
                _targetBot = liveBot;
                _targetFollower = follower;
                _targetBackpack = backpack;
                _initialBackpackItemIds = SnapshotAllItemIds(backpack);
                _initialTrackedItemIds = SnapshotTrackedItemIdsInBackpack(liveBot, _initialBackpackItemIds);
                _openedAtTime = Time.time;
                _closeRequested = false;
                _ending = false;
                follower.SetBackpackInspectionActive(true);

                // Mark visible before opening the UI so SearchableView and drag/drop validation agree that
                // this teammate backpack has already been searched.
                MarkBackpackVisible(owner.Player.SearchController, backpack);

                owner.ShowInventoryScreenLoot(backpack, () => EndInspection("InventoryClosed", true), false);
            }
            catch (Exception ex)
            {
                Logger.LogError("[TeammateBackpack] Failed to open teammate backpack");
                Logger.LogError(ex);
                EndInspection("OpenFailed", true);
            }
        }

        private static bool TryGetInspectableBackpack(
            GamePlayerOwner owner,
            out BotOwner targetBot,
            out BotFollowerPlayer follower,
            out SearchableItemItemClass backpack)
        {
            return TryGetInspectableBackpack(owner?.Player, out targetBot, out follower, out backpack);
        }

        private static bool TryGetInspectableBackpack(
            Player player,
            out BotOwner targetBot,
            out BotFollowerPlayer follower,
            out SearchableItemItemClass backpack)
        {
            targetBot = null;
            follower = null;
            backpack = null;

            Player targetPlayer = ResolveInteractionTargetPlayer(player);
            if (player == null ||
                targetPlayer == null ||
                !targetPlayer.IsAI ||
                targetPlayer.HealthController?.IsAlive != true)
            {
                return false;
            }

            targetBot = targetPlayer.AIData?.BotOwner;
            if (targetBot == null || !IsInspectableBotActive(targetBot, out follower))
            {
                return false;
            }

            if (HasActiveOrPendingHealWork(targetBot))
            {
                return false;
            }

            if (HasActiveOrPendingPickupWork(targetBot, follower))
            {
                return false;
            }

            if (Vector3.Distance(player.Position, targetPlayer.Position) > MaxInteractionDistance)
            {
                return false;
            }

            backpack = GetBackpack(targetBot);
            return backpack != null;
        }

        private static Player ResolveInteractionTargetPlayer(Player player)
        {
            if (player == null)
            {
                return null;
            }

            // EFT's InteractablePlayer is updated by a narrow collider raycast and can stay stale until the
            // next stock interaction event. Keep it only if the player is still really looking at that follower.
            Player stockTarget = player.InteractablePlayer;
            if (IsLookedAtCandidate(player, stockTarget))
            {
                return stockTarget;
            }

            // Fall back to our squad roster instead of physics hits. This keeps the prompt stable behind/near
            // followers where EFT's player collider raycast may miss, while the narrow angle gate prevents stale
            // lower-left prompts from sticking around when the player looks away.
            Ray ray = player.InteractionRay;
            Player bestPlayer = null;
            float bestAngle = float.MaxValue;
            foreach (BotFollowerPlayer follower in BossPlayers.GetFollowers())
            {
                if (follower?.IsSquadMate != true)
                {
                    continue;
                }

                Player candidate = follower.GetBot()?.GetPlayer;
                if (!IsLookedAtCandidate(player, candidate))
                {
                    continue;
                }

                float angle = Vector3.Angle(ray.direction, (GetTargetPoint(candidate) - ray.origin).normalized);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    bestPlayer = candidate;
                }
            }

            return bestPlayer;
        }

        private static bool IsLookedAtCandidate(Player requester, Player candidate)
        {
            if (requester == null ||
                candidate == null ||
                candidate == requester ||
                !candidate.IsAI ||
                candidate.HealthController?.IsAlive != true ||
                Vector3.Distance(requester.Position, candidate.Position) > MaxInteractionDistance)
            {
                return false;
            }

            Ray ray = requester.InteractionRay;
            Vector3 toTarget = GetTargetPoint(candidate) - ray.origin;
            if (toTarget.sqrMagnitude < 0.01f)
            {
                return false;
            }

            return Vector3.Angle(ray.direction, toTarget.normalized) <= QuickInteractionMaxAngle;
        }

        private static Vector3 GetTargetPoint(Player player)
        {
            if (player?.MainParts != null &&
                player.MainParts.TryGetValue(BodyPartType.body, out var bodyPart))
            {
                return bodyPart.Position;
            }

            return player != null ? player.Position + Vector3.up * 1.1f : Vector3.zero;
        }

        private static bool IsInspectableBotActive(BotOwner bot, out BotFollowerPlayer follower)
        {
            follower = BossPlayers.Instance?.GetFollower(bot);
            return IsInspectableBotActive(bot, follower);
        }

        private static bool IsInspectableBotActive(BotOwner bot, BotFollowerPlayer follower)
        {
            return bot != null &&
                   follower != null &&
                   follower.IsSquadMate &&
                   bot.BotState == EBotState.Active &&
                   !bot.IsDead &&
                   bot.GetPlayer != null &&
                   bot.GetPlayer.HealthController?.IsAlive == true;
        }

        private static bool HasActiveOrPendingHealWork(BotOwner bot)
        {
            if (bot?.Medecine == null)
            {
                return false;
            }

            BotLogicDecision currentDecision = bot.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            return bot.Medecine.FirstAid?.Have2Do == true ||
                   bot.Medecine.SurgicalKit?.HaveWork == true ||
                   bot.Medecine.FirstAid?.Using == true ||
                   bot.Medecine.SurgicalKit?.Using == true ||
                   bot.Medecine.Stimulators?.Using == true ||
                   currentDecision == BotLogicDecision.heal ||
                   currentDecision == BotLogicDecision.healStimulators;
        }

        private static bool HasActiveOrPendingPickupWork(BotOwner bot, BotFollowerPlayer follower)
        {
            if (bot == null)
            {
                return false;
            }

            // The TakeLoot command owns a short asynchronous pickup flow: approach, pickup animation, then an
            // inventory network transaction. Backpack inspection must not overlap that flow.
            if (follower != null &&
                follower.TryPeekActiveCommand(out FollowerCommandType command, out _, out _) &&
                (command == FollowerCommandType.TakeLootItem ||
                 command == FollowerCommandType.TakeBodyGear))
            {
                return true;
            }

            if (InteractableObjects.IsTaker(bot) ||
                InteractableObjects.IsBodyLootTaker(bot))
            {
                return true;
            }

            return bot.GetPlayer?.CurrentManagedState is PickupStateClass;
        }

        private static SearchableItemItemClass? GetBackpack(BotOwner bot)
        {
            try
            {
                return bot?.GetPlayer?.InventoryController?.Inventory?.Equipment?
                    .GetSlot(EquipmentSlot.Backpack)
                    .ContainedItem as SearchableItemItemClass;
            }
            catch
            {
                return null;
            }
        }

        private static void MarkBackpackVisible(IPlayerSearchController searchController, SearchableItemItemClass backpack)
        {
            if (searchController == null || backpack == null)
            {
                return;
            }

            if (!searchController.IsItemKnown(backpack))
            {
                searchController.SetItemAsKnown(backpack, false);
            }

            // Stock search does two separate things: marks searchable containers as searched and marks contained
            // item instances as known at their current addresses. We reproduce that completed state immediately.
            searchController.SetItemAsSearched<SearchableItemItemClass>(backpack);
            foreach (Item item in backpack.GetAllItems())
            {
                if (!searchController.IsItemKnown(item))
                {
                    searchController.SetItemAsKnown(item, false);
                }

                if (item is SearchableItemItemClass searchable)
                {
                    searchController.SetItemAsSearched<SearchableItemItemClass>(searchable);
                }
            }
        }

        public static bool ShouldTreatItemExamined(InventoryController controller, Item item)
        {
            // Do not call InventoryController.Examine here. That permanently adds templates to the player's
            // encyclopedia and grants examine XP. For teammate inspection, unknown templates are treated as
            // examined only while the item remains inside the active inspected backpack.
            return _owner?.Player?.InventoryController == controller && IsItemInsideActiveBackpack(item);
        }

        public static bool ShouldTreatAddressSearched(ItemAddress address)
        {
            // Drag/drop checks parent containers separately from SearchableView. This scoped override makes
            // transfer validation see the active teammate backpack tree as already searched.
            return IsAddressInsideActiveBackpack(address);
        }

        public static bool ShouldTreatObservedItemKnown(Item item, ItemAddress address)
        {
            return IsItemInsideActiveBackpack(item) || IsAddressInsideActiveBackpack(address);
        }

        public static bool IsActiveBackpack(CompoundItem item)
        {
            return _targetBackpack != null && item == _targetBackpack;
        }

        public static string GetActiveBackpackTitle()
        {
            string name = _targetBot?.Profile?.Nickname;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = _targetBot?.Profile?.Info?.Nickname;
            }

            return string.IsNullOrWhiteSpace(name) ? "Teammate" : name;
        }

        private static bool IsItemInsideActiveBackpack(Item item)
        {
            if (_targetBackpack == null || item == null)
            {
                return false;
            }

            if (item == _targetBackpack)
            {
                return true;
            }

            try
            {
                foreach (Item parent in item.GetAllParentItems(false))
                {
                    if (parent == _targetBackpack)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool IsAddressInsideActiveBackpack(ItemAddress address)
        {
            if (_targetBackpack == null || address == null)
            {
                return false;
            }

            try
            {
                foreach (Item parent in address.GetAllParentItems(false))
                {
                    if (parent == _targetBackpack)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void RequestClose(string reason)
        {
            if (_closeRequested)
            {
                return;
            }

            _closeRequested = true;
            ClearInspectionFlag(reason);

            try
            {
                if (CurrentScreenSingletonClass.Instance.CheckCurrentScreen(EEftScreenType.Inventory))
                {
                    _owner?.CloseInventoryIfOpen();
                    return;
                }

                EndInspection(reason, true);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[TeammateBackpack] Failed to close inspection screen reason={reason}");
                Logger.LogError(ex);
                EndInspection(reason, true);
            }
        }

        private static void EndInspection(string reason, bool clearInspectionFlag)
        {
            if (_ending)
            {
                return;
            }

            _ending = true;
            try
            {
                TrackBackpackItemChanges();

                if (clearInspectionFlag)
                {
                    ClearInspectionFlag(reason);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[TeammateBackpack] Failed to end backpack inspection cleanly");
                Logger.LogError(ex);
            }
            finally
            {
                _owner = null;
                _targetBot = null;
                _targetFollower = null;
                _targetBackpack = null;
                _initialBackpackItemIds = null;
                _initialTrackedItemIds = null;
                _openedAtTime = 0f;
                _closeRequested = false;
                _ending = false;
            }
        }

        private static void ClearInspectionFlag(string reason)
        {
            _targetFollower?.SetBackpackInspectionActive(false);
        }

        private static void TrackBackpackItemChanges()
        {
            if (_targetBot == null || _targetBackpack == null || _initialBackpackItemIds == null)
            {
                return;
            }

            // Items newly placed into the follower backpack become return-tracked like normal handed-over loot.
            HashSet<string> currentBackpackItemIds = SnapshotAllItemIds(_targetBackpack);
            foreach (Item item in _targetBackpack.GetAllItems())
            {
                if (!_initialBackpackItemIds.Contains(item.Id) &&
                    !HasNewAncestorInBackpack(item, _targetBackpack, _initialBackpackItemIds))
                {
                    InteractableObjects.StoreItem(_targetBot, item);
                }
            }

            if (_initialTrackedItemIds == null)
            {
                return;
            }

            // If the player removes an item that was previously marked for return, unmark it immediately so
            // post-raid return handling does not try to give back something the player already took.
            foreach (string trackedItemId in _initialTrackedItemIds)
            {
                if (!currentBackpackItemIds.Contains(trackedItemId))
                {
                    InteractableObjects.RemoveStoredItem(_targetBot.ProfileId, trackedItemId);
                }
            }
        }

        private static HashSet<string> SnapshotAllItemIds(SearchableItemItemClass backpack)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (Item item in backpack.GetAllItems())
            {
                ids.Add(item.Id);
            }

            return ids;
        }

        private static HashSet<string> SnapshotTrackedItemIdsInBackpack(BotOwner bot, HashSet<string> backpackItemIds)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            List<string>? trackedItems = InteractableObjects.GetStoredItems(bot.ProfileId);
            if (trackedItems == null || backpackItemIds == null)
            {
                return ids;
            }

            foreach (string itemId in trackedItems)
            {
                if (backpackItemIds.Contains(itemId))
                {
                    ids.Add(itemId);
                }
            }

            return ids;
        }

        private static bool HasNewAncestorInBackpack(Item item, SearchableItemItemClass backpack, HashSet<string> initialItemIds)
        {
            try
            {
                Item? parent = item?.Parent?.Container?.ParentItem;
                HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

                while (parent != null && parent != backpack)
                {
                    if (!visited.Add(parent.Id))
                    {
                        return false;
                    }

                    if (!initialItemIds.Contains(parent.Id))
                    {
                        return true;
                    }

                    parent = parent.Parent?.Container?.ParentItem;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
