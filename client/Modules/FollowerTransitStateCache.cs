using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace pitTeam.Modules
{
    internal static class FollowerTransitStateCache
    {
        private static readonly Dictionary<string, TransitFollowerState> StatesByKey =
            new Dictionary<string, TransitFollowerState>(StringComparer.Ordinal);

        private static readonly HashSet<string> TransitSpawnProfileIds =
            new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<string>> ProtectedEquipmentIdsByProfileId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private static readonly Dictionary<string, List<string>> TrackedReturnItemIdsByProfileId =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public static bool TryCapture(
            BotOwner bot,
            IEnumerable<string> protectedEquipmentIds,
            IEnumerable<string> trackedReturnItemIds,
            out Profile profile)
        {
            profile = null;
            if (bot?.Profile == null)
            {
                return false;
            }

            try
            {
                profile = CreateProfileSnapshot(bot);
                if (profile == null)
                {
                    return false;
                }

                List<string> protectedIds = protectedEquipmentIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                List<string> trackedReturnIds = trackedReturnItemIds?
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList() ?? new List<string>();

                TransitFollowerState state = new TransitFollowerState(profile, protectedIds, trackedReturnIds);
                StoreState(profile.AccountId, state);
                StoreState(bot.AccountId, state);
                StoreState(profile.ProfileId, state);
                StoreState(bot.ProfileId, state);

                WildSpawnType? role = profile.Info?.Settings?.Role;
                if (role.HasValue)
                {
                    StoreState(GetRoleKey(role.Value), state);
                }

                Modules.Logger.LogInfo(
                    $"[Transit] Captured carried follower state for '{profile.Nickname ?? profile.ProfileId}' " +
                    $"protectedEquipmentIds={protectedIds.Count} trackedReturnItemIds={trackedReturnIds.Count}.");
                return true;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[Transit] Failed to capture follower state for '{bot.Profile?.Nickname ?? bot.ProfileId}'.");
                Modules.Logger.LogError(ex);
                profile = null;
                return false;
            }
        }

        public static bool TryConsumeProfile(string memberId, WildSpawnType role, out Profile profile)
        {
            profile = null;

            if (TryTakeState(memberId, out TransitFollowerState state) ||
                TryTakeState(GetRoleKey(role), out state))
            {
                profile = state.Profile?.Clone();
                if (profile == null)
                {
                    return false;
                }

                TrackTransitSpawnProfile(profile, state);
                Modules.Logger.LogInfo(
                    $"[Transit] Reusing carried follower profile for '{profile.Nickname ?? profile.ProfileId}' instead of fetching server storage.");
                return true;
            }

            return false;
        }

        public static bool IsTransitSpawnProfile(string profileId)
        {
            return !string.IsNullOrWhiteSpace(profileId) &&
                   TransitSpawnProfileIds.Contains(profileId);
        }

        public static bool TryConsumeProtectedEquipmentIds(Profile profile, out List<string> protectedEquipmentIds)
        {
            protectedEquipmentIds = null;
            if (profile == null)
            {
                return false;
            }

            if (TryRemoveProtectedEquipmentIds(profile.ProfileId, out protectedEquipmentIds) ||
                TryRemoveProtectedEquipmentIds(profile.AccountId, out protectedEquipmentIds))
            {
                return true;
            }

            return false;
        }

        public static bool TryConsumeTrackedReturnItemIds(Profile profile, out List<string> trackedReturnItemIds)
        {
            trackedReturnItemIds = null;
            if (profile == null)
            {
                return false;
            }

            if (TryRemoveTrackedReturnItemIds(profile.ProfileId, out trackedReturnItemIds) ||
                TryRemoveTrackedReturnItemIds(profile.AccountId, out trackedReturnItemIds))
            {
                return true;
            }

            return false;
        }

        public static void Clear()
        {
            StatesByKey.Clear();
            TransitSpawnProfileIds.Clear();
            ProtectedEquipmentIdsByProfileId.Clear();
            TrackedReturnItemIdsByProfileId.Clear();
        }

        private static Profile CreateProfileSnapshot(BotOwner bot)
        {
            CompleteProfileDescriptorClass descriptor = new CompleteProfileDescriptorClass(bot.Profile, GClass2240.Instance);

            Inventory liveInventory = bot.GetPlayer?.InventoryController?.Inventory;
            if (liveInventory != null)
            {
                descriptor.Inventory = new EFTInventoryClass(liveInventory, GClass2240.Instance);
            }

            if (bot.GetPlayer?.ActiveHealthController != null)
            {
                try
                {
                    descriptor.Health = bot.GetPlayer.ActiveHealthController.Store(
                        Singleton<BackendConfigSettingsClass>.Instance.transitSettings,
                        null);
                }
                catch
                {
                    descriptor.Health = bot.GetPlayer.ActiveHealthController.Store(null);
                }
            }

            if (bot.GetPlayer?.Skills != null)
            {
                descriptor.Skills = new SkillsDescriptorClass(bot.GetPlayer.Skills);
            }

            return new Profile(descriptor);
        }

        private static void StoreState(string key, TransitFollowerState state)
        {
            if (string.IsNullOrWhiteSpace(key) || state == null)
            {
                return;
            }

            StatesByKey[key] = state;
        }

        private static bool TryTakeState(string key, out TransitFollowerState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(key) || !StatesByKey.TryGetValue(key, out state))
            {
                return false;
            }

            TransitFollowerState capturedState = state;
            foreach (string stateKey in StatesByKey
                         .Where(pair => ReferenceEquals(pair.Value, capturedState))
                         .Select(pair => pair.Key)
                         .ToList())
            {
                StatesByKey.Remove(stateKey);
            }

            return true;
        }

        private static void TrackTransitSpawnProfile(Profile profile, TransitFollowerState state)
        {
            if (!string.IsNullOrWhiteSpace(profile.ProfileId))
            {
                TransitSpawnProfileIds.Add(profile.ProfileId);
                ProtectedEquipmentIdsByProfileId[profile.ProfileId] = state.ProtectedEquipmentIds.ToList();
                TrackedReturnItemIdsByProfileId[profile.ProfileId] = state.TrackedReturnItemIds.ToList();
            }

            if (!string.IsNullOrWhiteSpace(profile.AccountId))
            {
                ProtectedEquipmentIdsByProfileId[profile.AccountId] = state.ProtectedEquipmentIds.ToList();
                TrackedReturnItemIdsByProfileId[profile.AccountId] = state.TrackedReturnItemIds.ToList();
            }
        }

        private static bool TryRemoveProtectedEquipmentIds(string key, out List<string> protectedEquipmentIds)
        {
            protectedEquipmentIds = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !ProtectedEquipmentIdsByProfileId.TryGetValue(key, out protectedEquipmentIds))
            {
                return false;
            }

            ProtectedEquipmentIdsByProfileId.Remove(key);
            protectedEquipmentIds = protectedEquipmentIds.ToList();
            return true;
        }

        private static bool TryRemoveTrackedReturnItemIds(string key, out List<string> trackedReturnItemIds)
        {
            trackedReturnItemIds = null;
            if (string.IsNullOrWhiteSpace(key) ||
                !TrackedReturnItemIdsByProfileId.TryGetValue(key, out trackedReturnItemIds))
            {
                return false;
            }

            TrackedReturnItemIdsByProfileId.Remove(key);
            trackedReturnItemIds = trackedReturnItemIds.ToList();
            return true;
        }

        private static string GetRoleKey(WildSpawnType role)
        {
            return $"role:{role}";
        }

        private sealed class TransitFollowerState
        {
            public TransitFollowerState(
                Profile profile,
                List<string> protectedEquipmentIds,
                List<string> trackedReturnItemIds)
            {
                Profile = profile;
                ProtectedEquipmentIds = protectedEquipmentIds ?? new List<string>();
                TrackedReturnItemIds = trackedReturnItemIds ?? new List<string>();
            }

            public Profile Profile { get; }
            public List<string> ProtectedEquipmentIds { get; }
            public List<string> TrackedReturnItemIds { get; }
        }
    }
}
