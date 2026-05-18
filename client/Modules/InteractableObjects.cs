using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using pitTeam.Components;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AI;


namespace pitTeam.Modules
{
    internal class InteractableObjects
    {
        public static InteractableObjects? Instance;

        private Door? _currDoor;
        private Dictionary<string, Door>? _doorsToOpen;

        private LootItem? _lootItem;
        private Vector3? _lootPosition;
        private Components.BotFollowerPlayer? _botToLoot;
        private string? _botToLootProfileId;

        private bool IsDisposed = false;

        private Dictionary<string, List<string>>? _lootedItems;
        private List<Item>? _toSendItems;
        private Dictionary<string, Dictionary<string, object>>? _followersWithLoot;

        private Dictionary<string, List<string>>? _followersEquipment;

        private bool _isBossDead = false;
        private static readonly bool EnableBackendItemReturn = true;

        private List<Player>? _enemiesSeen;
        private Player? _closestEnemySeen;

        public InteractableObjects()
        {
            if (Instance == null)
            {
                Instance = this;

                _lootedItems = new Dictionary<string, List<string>>();
                _toSendItems = new List<Item>();
                _followersWithLoot = new Dictionary<string, Dictionary<string, object>>();
                _doorsToOpen = new Dictionary<string, Door>();

                _enemiesSeen = new List<Player>();

                _followersEquipment = new Dictionary<string, List<string>>();

            }

        }
        /** Send items given to followers back to the player */
        private bool SendStoreItems()
        {
            GatherItems();

            if (!EnableBackendItemReturn)
            {
                if (_toSendItems != null && _toSendItems.Count > 0)
                {
                    Logger.LogInfo($"[Loot] BE return disabled. Kept {_toSendItems.Count} tracked follower items local-only.");
                }
                return false;
            }

            if (_toSendItems == null)
            {
                return false;
            }

            Dictionary<string, object>? member = null;
            if (_followersWithLoot != null && _followersWithLoot.Count > 0)
            {
                member = _followersWithLoot.Values.FirstOrDefault();
            }

            return SendReturnItems(_toSendItems, member, "post-raid returned follower items");
        }
        /** Gather what items where given to followers and which is still alive to count */
        private void GatherItems()
        {
            var bossPlayers = BossPlayers.Instance.GetBossPlayers();
            if (_toSendItems == null)
            {
                return;
            }

            _toSendItems.Clear();
            List<string> gathered = new List<string>();

            foreach (var player in bossPlayers)
            {
                foreach (var bot in player.Value.Followers)
                {
                    if (bot.BotState != EBotState.Active || !bot.HealthController.IsAlive)
                    {
                        continue;
                    }

                    GatherStoredItemsFromBot(bot, gathered);
                }
            }
        }

        public static void SendDeathEscapeFollowerStoredItems(IEnumerable<BotOwner> squadBots, IEnumerable<BotOwner> escapedBots)
        {
            if (Instance == null || squadBots == null || escapedBots == null || Instance._toSendItems == null)
            {
                return;
            }

            try
            {
                // Player-death cleanup is special: once at least one squadmate makes it out, the
                // squad recovers all tracked follower loot, even if the original carrier died.
                // Normal player-extract cleanup still only gathers from living followers.
                if (!escapedBots.Any())
                {
                    return;
                }

                Instance._toSendItems.Clear();
                List<string> gathered = new List<string>();

                foreach (BotOwner bot in squadBots)
                {
                    if (bot == null)
                    {
                        continue;
                    }

                    Instance.GatherStoredItemsFromBot(bot, gathered);
                }

                if (Instance._toSendItems.Count == 0)
                {
                    return;
                }

                Instance.SendCurrentStoredItems();
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to send escaped follower stored loot");
                Logger.LogError(ex);
            }
        }

        public static void SendDeathEscapeRecoveredGear(IEnumerable<Item> recoveredItems)
        {
            SendReturnItems(recoveredItems, null, "recovered death-escape gear");
        }

        private void GatherStoredItemsFromBot(BotOwner bot, List<string> gathered)
        {
            if (_toSendItems == null || bot?.GetPlayer?.InventoryController == null)
            {
                return;
            }

            var storedItems = GetStoredItems(bot.ProfileId);
            if (storedItems == null)
            {
                return;
            }

            foreach (var stored in storedItems)
            {
                if (gathered.Contains(stored)) continue;
                Item item = FindStoredReturnItem(bot.GetPlayer.InventoryController, stored);
                if (item == null)
                {
                    continue;
                }

                // If both a container and one of its children are tracked, return the container
                // tree once. Sending overlapping roots to /returnitems duplicates nested rigs/loot.
                if (HasTrackedAncestor(item, storedItems))
                {
                    gathered.Add(stored);
                    continue;
                }

                _toSendItems.Add(item.CloneItem());
                gathered.Add(stored);
            }
        }

        private bool SendCurrentStoredItems()
        {
            if (!EnableBackendItemReturn || _toSendItems == null)
            {
                return false;
            }

            Dictionary<string, object>? member = null;
            if (_followersWithLoot != null && _followersWithLoot.Count > 0)
            {
                member = _followersWithLoot.Values.FirstOrDefault();
            }

            return SendReturnItems(_toSendItems, member, "post-raid returned follower items");
        }

        private static Item? FindStoredReturnItem(InventoryController inventoryController, string itemId)
        {
            if (inventoryController?.Inventory?.Equipment == null || string.IsNullOrWhiteSpace(itemId))
            {
                return null;
            }

            foreach (EquipmentSlot slot in GetTrackedReturnSearchSlots())
            {
                Item root = inventoryController.Inventory.Equipment.GetSlot(slot)?.ContainedItem;
                if (root == null)
                {
                    continue;
                }

                if (root.Id == itemId)
                {
                    return root;
                }

                if (root is CompoundItem compound)
                {
                    foreach (Item child in compound.GetAllItems())
                    {
                        if (child != null && child.Id == itemId)
                        {
                            return child;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<EquipmentSlot> GetTrackedReturnSearchSlots()
        {
            yield return EquipmentSlot.TacticalVest;
            yield return EquipmentSlot.Backpack;
            yield return EquipmentSlot.Pockets;
            yield return EquipmentSlot.FirstPrimaryWeapon;
            yield return EquipmentSlot.SecondPrimaryWeapon;
            yield return EquipmentSlot.Holster;
        }

        private static IEnumerable<Item> RemoveNestedReturnRoots(IEnumerable<Item> items)
        {
            List<Item> roots = items?.Where(item => item != null).ToList() ?? new List<Item>();
            if (roots.Count <= 1)
            {
                foreach (Item root in roots)
                {
                    yield return root;
                }

                yield break;
            }

            List<HashSet<string>> rootTrees = roots
                .Select(GetItemTreeIds)
                .ToList();

            for (int index = 0; index < roots.Count; index++)
            {
                Item root = roots[index];
                bool coveredByOtherRoot = false;

                for (int otherIndex = 0; otherIndex < roots.Count; otherIndex++)
                {
                    if (index == otherIndex)
                    {
                        continue;
                    }

                    if (string.Equals(roots[otherIndex].Id, root.Id, StringComparison.Ordinal))
                    {
                        coveredByOtherRoot = otherIndex < index;
                        if (coveredByOtherRoot)
                        {
                            break;
                        }

                        continue;
                    }

                    if (rootTrees[otherIndex].Contains(root.Id))
                    {
                        coveredByOtherRoot = true;
                        break;
                    }
                }

                if (!coveredByOtherRoot)
                {
                    yield return root;
                }
            }
        }

        private static bool HasTrackedAncestor(Item item, IEnumerable<string> trackedItemIds)
        {
            if (item == null || trackedItemIds == null)
            {
                return false;
            }

            HashSet<string> tracked = new HashSet<string>(trackedItemIds, StringComparer.Ordinal);
            Item? parent = item.Parent?.Container?.ParentItem;
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);

            while (parent != null)
            {
                if (!visited.Add(parent.Id))
                {
                    return false;
                }

                if (tracked.Contains(parent.Id))
                {
                    return true;
                }

                parent = parent.Parent?.Container?.ParentItem;
            }

            return false;
        }

        private static HashSet<string> GetItemTreeIds(Item item)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            if (item == null)
            {
                return ids;
            }

            ids.Add(item.Id);
            if (item is CompoundItem compound)
            {
                foreach (Item child in compound.GetAllItems())
                {
                    if (child != null)
                    {
                        ids.Add(child.Id);
                    }
                }
            }

            return ids;
        }

        private static bool SendReturnItems(
            IEnumerable<Item> items,
            Dictionary<string, object>? member,
            string context)
        {
            if (!EnableBackendItemReturn || items == null)
            {
                return false;
            }

            try
            {
                List<Item> rootItems = RemoveNestedReturnRoots(items.Where(item => item != null)).ToList();
                if (rootItems.Count == 0)
                {
                    return false;
                }

                foreach (Item root in rootItems)
                {
                    DetachReturnRoot(root);
                }

                FlatItemsDataClass[] flatItems = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(rootItems);
                if (flatItems == null || !flatItems.Any())
                {
                    return false;
                }

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
                var defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                string returnItemsJson = new
                {
                    items = flatItems,
                    member,
                }.ToJson(defaultJsonConverters);

                Task.Run(() =>
                {
                    try
                    {
                        RequestHandler.PostJson("/singleplayer/returnitems", returnItemsJson);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"Failed to send {context}");
                        Logger.LogError(ex);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to prepare {context}");
                Logger.LogError(ex);
                return false;
            }
        }

        private static void DetachReturnRoot(Item item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                // Return mail accepts a list of root item trees. Recovered items may have been
                // cloned from inside a backpack/rig and still carry the old parent address, which
                // can make nested backpack contents disappear or attach to the wrong mailed root.
                item.CurrentAddress = null;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to detach return root '{item.Id}' before mail serialization.");
                Logger.LogError(ex);
            }
        }

        public void Destroy()
        {
            if (IsDisposed) return;


            try
            {
                SendEscapedFollowerDefaultLoadoutOutcomes();
                NpcMessage.SendLostTeammateOutcomes();

                if (!SendStoreItems())
                {
                    NpcMessage.NpcSendThankYou();
                }
                else
                {
                    string? id = NpcMessage.GetNpcType("boss");
                    if (id == null) id = NpcMessage.GetNpcType("ally");

                    if (id != null)
                    {
                        NpcMessage.NpcSendThankYou(id);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Error sending stored loot");
                Logger.LogError(e);
            }

            if (_lootedItems != null)
            {
                foreach (var stack in _lootedItems)
                {
                    stack.Value.Clear();
                }
            }

            _lootedItems?.Clear();
            _toSendItems?.Clear();
            _followersWithLoot?.Clear();
            _enemiesSeen?.Clear();

            _followersEquipment?.Clear();

            _currDoor = null;
            _doorsToOpen?.Clear();

            _lootItem = null;
            _lootedItems = null;

            _enemiesSeen = null;

            _doorsToOpen = null;

            _isBossDead = false;

            IsDisposed = true;
            Instance = null;
        }

        private void SendEscapedFollowerDefaultLoadoutOutcomes()
        {
            if (_isBossDead || BossPlayers.Instance == null)
            {
                return;
            }

            try
            {
                var entries = new List<object>();
                var seenAids = new HashSet<string>(StringComparer.Ordinal);

                foreach (var boss in BossPlayers.Instance.GetBossPlayers())
                {
                    foreach (var bot in boss.Value.Followers)
                    {
                        if (bot == null ||
                            bot.BotState != EBotState.Active ||
                            bot.HealthController?.IsAlive != true ||
                            bot.GetPlayer?.InventoryController?.Inventory?.Equipment == null ||
                            string.IsNullOrWhiteSpace(bot.Profile?.AccountId) ||
                            !seenAids.Add(bot.Profile.AccountId))
                        {
                            continue;
                        }

                        FlatItemsDataClass[] equipmentItems = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(
                            new Item[] { bot.GetPlayer.InventoryController.Inventory.Equipment });
                        if (equipmentItems == null || equipmentItems.Length == 0)
                        {
                            continue;
                        }

                        entries.Add(new
                        {
                            Aid = bot.Profile.AccountId,
                            ProfileId = bot.ProfileId ?? string.Empty,
                            Nickname = bot.Profile?.Nickname ?? "Squadmate",
                            Escaped = true,
                            Chance = 1d,
                            ExtractName = string.Empty,
                            Distance = 0d,
                            HealthRatio = CalculateHealthRatio(bot),
                            EquipmentPower = 0d,
                            EnemyAveragePower = 0d,
                            AliveSquadmates = 0,
                            HasSecureMeds = false,
                            EquipmentItems = equipmentItems,
                            TrackedItemIds = GetStoredItems(bot.ProfileId)?.ToArray() ?? Array.Empty<string>()
                        });
                    }
                }

                if (entries.Count == 0)
                {
                    return;
                }

                var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                    .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);
                var defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

                string json = new
                {
                    Notify = false,
                    Entries = entries
                }.ToJson(defaultJsonConverters);

                Task.Run(() =>
                {
                    try
                    {
                        RequestHandler.PostJson("/singleplayer/pitfireteam/teammate/death-escape", json);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("Failed to send escaped teammate loadout outcomes");
                        Logger.LogError(ex);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to prepare escaped teammate loadout outcomes");
                Logger.LogError(ex);
            }
        }

        private static double CalculateHealthRatio(BotOwner bot)
        {
            if (bot?.GetPlayer?.ActiveHealthController == null)
            {
                return 1d;
            }

            float current = 0f;
            float maximum = 0f;
            foreach (EBodyPart part in GClass3058.RealBodyParts)
            {
                try
                {
                    ValueStruct health = bot.GetPlayer.ActiveHealthController.GetBodyPartHealth(part, false);
                    current += Mathf.Max(0f, health.Current);
                    maximum += Mathf.Max(0f, health.Maximum);
                }
                catch
                {
                    // Missing body-part data should not block raid-end persistence.
                }
            }

            return maximum > 0f ? Mathf.Clamp01(current / maximum) : 1d;
        }

        public static void Dispose()
        {
            if (Instance != null)
            {
                Instance.Destroy();
                Instance = null;
            }
        }
        /** Set what door the boss wants to open */
        public static void SetCurDoor(Door? door)
        {

            if (Instance != null)
                Instance._currDoor = door;
        }
        public static Door? GetCurDoor()
        {
            if (Instance == null) return null;
            return Instance._currDoor;
        }
        /** Set what loot item the boss wants to be picked up */
        public static void SetCurLootItem(LootItem? item)
        {
            if (Instance != null)
            {
                Instance._lootItem = item;
            }
        }

        public static LootItem? GetCurLootItem()
        {
            if (Instance == null) return null;
            return Instance._lootItem;
        }

        public static Vector3 GetLootPosition()
        {
            if (Instance?._lootItem != null && TryGetLootNavPosition(Instance._lootItem, out Vector3 livePosition))
            {
                Instance._lootPosition = livePosition;
                return livePosition;
            }

            return Instance?._lootPosition ?? Vector3.zero;
        }

        /** Set what bot is going to pick up the loot */
        public static bool SetTaker(BotOwner bot, LootItem? lootItem = null)
        {
            if (Instance == null) return false;

            var _follower = BossPlayers.Instance.GetFollower(bot);

            if (_follower == null) return false;

            if (lootItem != null)
            {
                // Pin the target at command issue time. The quick panel can clear its current
                // interactable before the follower action starts moving toward the item.
                Instance._lootItem = lootItem;
            }

            if (Instance._lootItem != null)
            {
                try
                {
                    Collider collider = Instance._lootItem.GetComponentInChildren<Collider>();
                    if (collider == null)
                    {
                        return false;
                    }

                    Vector3 center = collider.bounds.center;
                    center.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;

                    NavMeshHit navMeshHit;
                    if (!NavMesh.SamplePosition(center, out navMeshHit, 2f, -1))
                    {
                        return false;
                    }

                    Instance._lootPosition = navMeshHit.position;

                    Instance._botToLoot = _follower;
                    Instance._botToLootProfileId = bot.ProfileId;

                    return true;

                }
                catch (Exception ex)
                {
                    Logger.LogError("Could not make bot a Loot Taker");
                    Logger.LogError(ex);
                }
            }

            return false;
        }

        private static bool TryGetLootNavPosition(LootItem lootItem, out Vector3 position)
        {
            position = Vector3.zero;

            if (lootItem == null)
            {
                return false;
            }

            Vector3 samplePoint = lootItem.transform.position;

            try
            {
                Collider collider = lootItem.GetComponentInChildren<Collider>();
                if (collider != null)
                {
                    samplePoint = collider.bounds.center;
                    samplePoint.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;
                }

                if (NavMesh.SamplePosition(samplePoint, out NavMeshHit navMeshHit, 2f, -1))
                {
                    position = navMeshHit.position;
                    return true;
                }
            }
            catch
            {
                // fall back to raw transform position below
            }

            position = lootItem.transform.position;
            return true;
        }

        public static bool IsTaker(BotOwner bot)
        {
            if (Instance == null || bot == null) return false;
            if (!string.IsNullOrEmpty(Instance._botToLootProfileId) &&
                !string.IsNullOrEmpty(bot.ProfileId) &&
                string.Equals(Instance._botToLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                return true;
            }

            var _follower = BossPlayers.Instance.GetFollower(bot);
            return _follower != null && _follower == Instance._botToLoot;
        }

        public static void RemoveTaker(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;

            if (!string.IsNullOrEmpty(Instance._botToLootProfileId) &&
                string.Equals(Instance._botToLootProfileId, bot.ProfileId, StringComparison.Ordinal))
            {
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
                return;
            }

            Components.BotFollowerPlayer follower = BossPlayers.Instance.GetFollower(bot);
            if (follower != null && Instance._botToLoot == follower)
            {
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
            }
        }
        /** Set what bot is going to open the door */
        public static bool SetOpener(BotOwner bot, Door? door = null)
        {
            if (Instance == null || bot == null) return false;
            if (Instance._currDoor != null && Instance._doorsToOpen != null)
            {
                if (!Instance._doorsToOpen.ContainsKey(bot.ProfileId))
                {
                    Instance._doorsToOpen.Add(bot.ProfileId, Instance._currDoor);
                }
                else
                {
                    Instance._doorsToOpen[bot.ProfileId] = door != null ? door : Instance._currDoor;
                }
                return true;
            }
            return false;
        }

        public static bool IsOpener(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null || bot == null) return false;
            return Instance._doorsToOpen.ContainsKey(bot.ProfileId);
        }

        public static void RemoveOpener(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            if (Instance._doorsToOpen.ContainsKey(bot.ProfileId)) Instance._doorsToOpen.Remove(bot.ProfileId);
        }

        public static Door? GetDoorToOpen(BotOwner bot)
        {
            if (Instance == null || Instance._doorsToOpen == null) return null;
            if (!Instance._doorsToOpen.ContainsKey(bot.ProfileId)) return null;
            return Instance._doorsToOpen[bot.ProfileId];
        }

        public static void ClearCurLootItem()
        {
            if (Instance != null)
            {
                Instance._lootItem = null;
                Instance._lootPosition = null;
                Instance._botToLoot = null;
                Instance._botToLootProfileId = null;
            }
        }
        /** Store the item that was given to a follower */
        public static void StoreItem(BotOwner bot, Item item)
        {
            if (Instance == null || Instance._lootedItems == null || Instance._followersWithLoot == null)
            {
                return;
            }

            if (!Instance._lootedItems.ContainsKey(bot.ProfileId))
            {
                Instance._lootedItems.Add(bot.ProfileId, new List<string>());
                Instance._followersWithLoot.Add(bot.ProfileId, new Dictionary<string, object> {
                    { "_id" , bot.ProfileId  },
                    { "aid" , bot.Profile.AccountId },
                    {
                        "Info" , new Dictionary<string, object>{
                            { "Level", bot.Profile.Info.Level },
                            { "MemberCategory", bot.Profile.Info.MemberCategory },
                            { "Nickname",  bot.Profile.Info.Nickname },
                            { "Side",  bot.Profile.Info.Side },
                        }
                    },
                });
            }

            var list = Instance._lootedItems[bot.ProfileId];

            // Track the largest meaningful root. If a whole backpack/rig is tracked, its
            // children ride inside that one return tree and must not be mailed separately.
            if (HasTrackedAncestor(item, list))
            {
                return;
            }

            HashSet<string> treeIds = GetItemTreeIds(item);
            list.RemoveAll(itemId =>
                !string.Equals(itemId, item.Id, StringComparison.Ordinal) &&
                treeIds.Contains(itemId));

            if (!list.Contains(item.Id))
            {
                list.Add(item.Id);
            }
        }

        public static void RemoveStoredItem(string bot, string itemId)
        {
            if (Instance?._lootedItems != null && Instance._lootedItems.ContainsKey(bot))
            {
                var list = Instance._lootedItems[bot];
                if (list.Contains(itemId))
                {
                    list.Remove(itemId);
                }
            }
        }

        public static List<string>? GetStoredItems(string bot)
        {
            if (Instance?._lootedItems != null && Instance._lootedItems.ContainsKey(bot))
            {
                return Instance._lootedItems[bot];
            }

            return null;
        }

        public static void ClearStoredItems(string bot)
        {
            if (Instance == null || Instance._isBossDead || Instance._lootedItems == null || Instance._followersWithLoot == null) return;

            if (Instance._lootedItems.ContainsKey(bot))
            {
                Instance._lootedItems.Remove(bot);
                Instance._followersWithLoot.Remove(bot);
            }
        }

        /** Store what enemies the player might have seen during "CONTACT" phrase */
        public static void CheckSeenEnemies(IPlayer player)
        {
            if (Instance == null || player == null) return;
            if (Instance._enemiesSeen == null) return;
            if (player.Transform == null) return;

            Instance._closestEnemySeen = null;
            Instance._enemiesSeen.Clear();

            pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);

            if (boss == null || boss.bossGroup == null) return;

            float scanDistance = pitFireTeam.scanDistance.Value;

            Vector3 playerPosition = player.Transform.position;
            Vector3 playerLookDirection = player.LookDirection;
            float sphereRadius = scanDistance / 2;
            float sphereDistance = scanDistance / 2;

            RaycastHit[] hits = new RaycastHit[20];
            Ray visionRay = new Ray(playerPosition, playerLookDirection);
            int numHits = Physics.SphereCastNonAlloc(
                    visionRay,
                    sphereRadius,
                    hits,
                    sphereDistance,
                    LayerMaskClass.PlayerMask
                );

            // get all enemies the boss might have seen
            for (int i = 0; i < numHits; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider != null && hit.collider.gameObject != null)
                {
                    var enemy = hit.collider.gameObject.GetComponent<Player>();
                    if (enemy != null)
                    {
                        if (boss.Followers.Find(fl => fl.ProfileId == enemy.ProfileId) != null) continue;

                        if (player.ProfileId == enemy.ProfileId) continue;

                        bool isenemy = boss.bossGroup.IsEnemy(enemy);

                        if (!isenemy && boss.bossGroup.IsPlayerEnemy(enemy))
                        {
                            isenemy = true;
                        }

                        if (isenemy)
                        {
                            BotOwner enemyBot = enemy.GetComponent<BotOwner>();
                            if (enemyBot != null)
                            {
                                // who ever has the play in sights is the enemy
                                if (enemyBot.Memory.GoalEnemy != null && enemyBot.Memory.GoalEnemy.ProfileId == player.ProfileId)
                                {
                                    isenemy = true;
                                }
                                else
                                {
                                    var bossAllies = Utils.Props.BossFollowersType.ToList();
                                    bossAllies.Add(WildSpawnType.exUsec);
                                    // do not mark as enemy an ally
                                    if (
                                        enemy.Side == player.Side && new EPlayerSide[] { EPlayerSide.Bear, EPlayerSide.Usec }.Contains(enemy.Side) &&
                                        Utils.Utils.FlagGet("pitFireTeam") && !Utils.Utils.FlagGet("isBadGuy") &&
                                        !enemyBot.BotsGroup.IsEnemy(player)
                                       )
                                    {
                                        isenemy = false;
                                    }
                                    // do not mark as enemy a boss allly
                                    else if (
                                        !enemyBot.BotsGroup.IsEnemy(player) &&
                                        bossAllies.Contains(enemy.Profile.Info.Settings.Role) &&
                                        Utils.Utils.PlayerHasKnightQuest(player.Profile)
                                    )
                                    {
                                        isenemy = false;
                                    }
                                }
                            }
                        }

                        if (isenemy)
                        {
                            if (player.PlayerBones?.WeaponRoot == null) continue;
                            if (enemy.MainParts == null) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.head, out var headPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.body, out var bodyPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.leftArm, out var leftArmPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.rightArm, out var rightArmPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.leftLeg, out var leftLegPart)) continue;
                            if (!enemy.MainParts.TryGetValue(BodyPartType.rightLeg, out var rightLegPart)) continue;

                            Vector3 firePos = player.PlayerBones.WeaponRoot.position;
                            // - we check if any part of the enemy is visible to the player
                            if (
                                Utils.Utils.CanShootToTarget(new ShootPointClass(headPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootPointClass(bodyPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootPointClass(leftArmPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootPointClass(rightArmPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootPointClass(leftLegPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false) ||
                                Utils.Utils.CanShootToTarget(new ShootPointClass(rightLegPart.Position, 1), firePos, LayerMaskClass.HighPolyWithTerrainMask, false)
                            )
                            {
                                Instance._enemiesSeen.Add(enemy);
                            }
                        }
                    }
                }
            }

            float dist = Mathf.Infinity;
            Player? closest = null;
            foreach (var item in Instance._enemiesSeen)
            {
                float edist = Vector3.Distance(playerPosition, item.Position);
                if (edist < dist)
                {
                    dist = edist;
                    closest = item;
                }
            }

            if (closest != null)
            {
                Instance._closestEnemySeen = closest;
            }
        }
        /** Get all enemies the player might have seen during "CONTACT" phrase */
        public static List<Player> GetSeenEnemies()
        {
            if (Instance == null || Instance._enemiesSeen == null) return new List<Player>();
            return Instance._enemiesSeen;

        }
        /** Get the closest enemy the player might have seen during "CONTACT" phrase */
        public static Player GetClosestSeenEnemy()
        {
            if (Instance == null) return null;
            return Instance._closestEnemySeen;
        }

        public static void BossIsDead()
        {
            if (Instance == null) return;
            Instance._isBossDead = true;
        }

        public static bool IsBossDead()
        {
            if (Instance == null) return false;
            return Instance._isBossDead;
        }


        private static void ModEquipmentStore(Slot slot, List<string> items)
        {
            if (slot.ContainedItem != null)
            {
                items.Add(slot.ContainedItem.Id);

                if (slot.ContainedItem is Mod mod)
                {
                    foreach (Slot modSlot in mod.Slots)
                    {
                        if (modSlot.ContainedItem != null) ModEquipmentStore(modSlot, items);
                    }
                }
            }
        }
        public static void StoreEquipment(Profile profile)
        {
            if (Instance == null || Instance._followersEquipment == null)
            {
                return;
            }

            if (!Instance._followersEquipment.ContainsKey(profile.ProfileId))
            {
                List<string> items = new List<string>();
                foreach (EquipmentSlot slotType in Enum.GetValues(typeof(EquipmentSlot)))
                {
                    if (
                        slotType == EquipmentSlot.Dogtag ||
                        slotType == EquipmentSlot.SecuredContainer ||
                        slotType == EquipmentSlot.Pockets ||
                        slotType == EquipmentSlot.ArmBand ||
                        slotType == EquipmentSlot.Scabbard
                    ) continue;

                    Slot botSlot = profile.Inventory.Equipment.GetSlot(slotType);

                    if (botSlot.IsSpecial) continue;

                    Item contained = botSlot.ContainedItem;

                    if (contained != null)
                    {
                        if (!pitFireTeam.IsFollowerLoadoutLootableMode())
                        {
                            try
                            {
                                FieldInfo? componentsField = AccessTools.Field(typeof(Item), "Components");
                                List<IItemComponent>? components = componentsField?.GetValue(contained) as List<IItemComponent>;
                                if (components != null)
                                {
                                    components.Add(new UnlootableComponent(contained, contained.Template));
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError("Could not make item unlootable");
                                Logger.LogError(ex.ToString());
                            }
                        }

                        if (slotType == EquipmentSlot.Backpack) items.Add(contained.Id);
                        else
                        {
                            items.Add(contained.Id);
                            if (contained is Weapon weapon)
                            {
                                foreach (Slot slot in weapon.Slots)
                                {
                                    if (slot.Locked) continue;
                                    if (slot.ContainedItem != null && !(slot.ContainedItem is MagazineItemClass) && !(slot.ContainedItem is AmmoItemClass))
                                    {
                                        ModEquipmentStore(slot, items);
                                    }
                                }
                            }
                            else if (slotType == EquipmentSlot.Headwear || slotType == EquipmentSlot.TacticalVest || slotType == EquipmentSlot.ArmorVest)
                            {
                                if (contained is CompoundItem compoundItem)
                                {
                                    foreach (Slot slot in compoundItem.Slots)
                                    {
                                        if (slot.Locked) continue;

                                        if (slot.ContainedItem != null && !contained.IsUnremovable)
                                        {
                                            items.Add(slot.ContainedItem.Id);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (items.Count > 0)
                {
                    Instance._followersEquipment.Add(profile.ProfileId, items);
                }
            }
        }


        public static Dictionary<string, List<string>> GetStoredEquipment()
        {
            if (Instance?._followersEquipment == null) return new Dictionary<string, List<string>>();
            return Instance._followersEquipment;
        }
    }
}
