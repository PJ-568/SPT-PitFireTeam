using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.AI;


namespace friendlySAIN.Modules
{
    internal class InteractableObjects
    {
        public static InteractableObjects Instance;

        private Door? _currDoor;
        private Dictionary<string, Door> _doorsToOpen;

        private LootItem _lootItem;
        private Vector3? _lootPosition;
        private Components.BotFollowerPlayer _botToLoot;
        private string _botToLootProfileId;

        private bool IsDisposed = false;

        private Dictionary<string, List<string>> _lootedItems;
        private List<Item> _toSendItems;
        private Dictionary<string, Dictionary<string, object>> _followersWithLoot;

        private Dictionary<string, List<string>> _followersEquipment;

        private bool _isBossDead = false;
        private const bool EnableBackendItemReturn = false;

        List<Player> _enemiesSeen;
        Player _closestEnemySeen;

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
                if (_toSendItems.Count > 0)
                {
                    Logger.LogInfo($"[Loot] BE return disabled. Kept {_toSendItems.Count} tracked follower items local-only.");
                }
                return false;
            }

            var flatItems = Singleton<ItemFactoryClass>.Instance.TreeToFlatItems(_toSendItems);

            var converterClass = typeof(AbstractGame).Assembly.GetTypes()
                .First(t => t.GetField("Converters", BindingFlags.Static | BindingFlags.Public) != null);

            var _defaultJsonConverters = Traverse.Create(converterClass).Field<JsonConverter[]>("Converters").Value;

            if (flatItems != null && flatItems.Any())
            {
                RequestHandler.PostJson("/singleplayer/returnitems", new
                {
                    items = flatItems,
                }.ToJson(_defaultJsonConverters));

                return true;
            }

            return false;
        }
        /** Gather what items where given to followers and which is still alive to count */
        private void GatherItems()
        {
            var bossPlayers = BossPlayers.Instance.GetBossPlayers();
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

                    InventoryController _botInventoryController = bot.GetPlayer.InventoryController;

                    SearchableItemItemClass tacVest = (SearchableItemItemClass)
                        _botInventoryController.Inventory.Equipment
                            .GetSlot(EquipmentSlot.TacticalVest)
                            .ContainedItem;

                    SearchableItemItemClass backpack = (SearchableItemItemClass)
                        _botInventoryController.Inventory.Equipment
                            .GetSlot(EquipmentSlot.Backpack)
                            .ContainedItem;

                    SearchableItemItemClass pockets = (SearchableItemItemClass)
                        _botInventoryController.Inventory.Equipment
                            .GetSlot(EquipmentSlot.Pockets)
                            .ContainedItem;

                    var storedItems = GetStoredItems(bot.ProfileId);

                    if (storedItems != null)
                    {
                        foreach (var stored in storedItems)
                        {
                            if (gathered.Contains(stored)) continue;

                            bool found = false;
                            if (tacVest.Grids.Length > 0)
                            {
                                foreach (var item in tacVest.GetAllItems())
                                {
                                    if (item.Id == stored)
                                    {
                                        _toSendItems.Add(item.CloneItem());
                                        gathered.Add(stored);
                                        found = true;
                                        break;
                                    }
                                }
                            }

                            if (!found && backpack.Grids.Length > 0)
                            {
                                foreach (var item in backpack.GetAllItems())
                                {
                                    if (item.Id == stored)
                                    {
                                        _toSendItems.Add(item.CloneItem());
                                        found = true;
                                        gathered.Add(stored);
                                        break;
                                    }
                                }
                            }

                            if (!found && pockets.Grids.Length > 0)
                            {
                                foreach (var item in pockets.GetAllItems())
                                {
                                    if (item.Id == stored)
                                    {
                                        _toSendItems.Add(item.CloneItem());
                                        gathered.Add(stored);
                                        found = true;
                                        break;
                                    }
                                }
                            }

                            if (!found)
                            {
                                var primaryWeapon = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.FirstPrimaryWeapon).ContainedItem;
                                if (primaryWeapon != null && primaryWeapon.Id == stored)
                                {
                                    _toSendItems.Add(primaryWeapon.CloneItem());
                                    gathered.Add(stored);
                                    found = true;
                                }
                            }

                            if (!found)
                            {
                                var secondaryWeapon = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem;
                                if (secondaryWeapon != null && secondaryWeapon.Id == stored)
                                {
                                    _toSendItems.Add(secondaryWeapon.CloneItem());
                                    gathered.Add(stored);
                                    found = true;
                                }
                            }

                            if (!found)
                            {
                                var meleeWeapon = _botInventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.Holster).ContainedItem;
                                if (meleeWeapon != null && meleeWeapon.Id == stored)
                                {
                                    _toSendItems.Add(meleeWeapon.CloneItem());
                                    gathered.Add(stored);
                                    found = true;
                                }
                            }
                        }
                    }
                }
            }
        }

        public void Destroy()
        {
            if (IsDisposed) return;


            try
            {
                if (!SendStoreItems())
                {
                    NpcMessage.NpcSendThankYou();
                }
                else
                {
                    string id = NpcMessage.GetNpcType("boss");
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

            foreach (var stack in _lootedItems)
            {
                stack.Value.Clear();
            }

            _lootedItems.Clear();
            _toSendItems.Clear();
            _followersWithLoot.Clear();
            _enemiesSeen.Clear();

            _followersEquipment.Clear();

            _currDoor = null;
            _doorsToOpen.Clear();

            _lootItem = null;
            _lootedItems = null;

            _enemiesSeen = null;

            _doorsToOpen = null;

            _isBossDead = false;

            IsDisposed = true;
            Instance = null;
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
        public static void SetCurLootItem(LootItem item)
        {
            if (Instance != null)
            {
                Instance._lootItem = item;
            }
        }

        public static LootItem GetCurLootItem()
        {
            if (Instance == null) return null;
            return Instance._lootItem;
        }

        public static Vector3 GetLootPosition()
        {
            return (Vector3)Instance._lootPosition;
        }

        /** Set what bot is going to pick up the loot */
        public static bool SetTaker(BotOwner bot)
        {
            if (Instance == null) return false;

            var _follower = BossPlayers.Instance.GetFollower(bot);

            if (_follower == null) return false;

            if (Instance._lootItem != null)
            {
                try
                {
                    Collider collider = Instance._lootItem.GetComponentInChildren<Collider>();

                    Vector3 center = collider.bounds.center;
                    center.y = collider.bounds.center.y - collider.bounds.extents.y - 0.4f;

                    NavMeshHit navMeshHit;
                    if (!NavMesh.SamplePosition(center, out navMeshHit, 2f, -1))
                    {
                        return false;
                    }

                    bot.ItemTaker.ThrownItems.Add(Instance._lootItem);
                    bot.ItemTaker.ItemToTake = Instance._lootItem;

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
        public static bool SetOpener(BotOwner bot, Door door = null)
        {
            if (Instance == null || bot == null) return false;
            if (Instance._currDoor != null)
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
            if (Instance == null || bot == null) return false;
            return Instance._doorsToOpen.ContainsKey(bot.ProfileId);
        }

        public static void RemoveOpener(BotOwner bot)
        {
            if (Instance == null || bot == null || string.IsNullOrEmpty(bot.ProfileId)) return;
            if (Instance._doorsToOpen.ContainsKey(bot.ProfileId)) Instance._doorsToOpen.Remove(bot.ProfileId);
        }

        public static Door GetDoorToOpen(BotOwner bot)
        {
            if (Instance == null) return null;
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

            if (!list.Contains(item.Id))
            {
                list.Add(item.Id);
            }
        }

        public static void RemoveStoredItem(string bot, string itemId)
        {
            if (Instance._lootedItems.ContainsKey(bot))
            {
                var list = Instance._lootedItems[bot];
                if (list.Contains(itemId))
                {
                    list.Remove(itemId);
                }
            }
        }

        public static List<string> GetStoredItems(string bot)
        {
            if (Instance._lootedItems.ContainsKey(bot))
            {
                return Instance._lootedItems[bot];
            }

            return null;
        }

        public static void ClearStoredItems(string bot)
        {
            if (Instance == null || Instance._isBossDead) return;

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

            float scanDistance = friendlySAIN.scanDistance.Value;

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
                                        Utils.Utils.FlagGet("friendlySAIN") && !Utils.Utils.FlagGet("isBadGuy") &&
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

                if (slot.ContainedItem is Mod) foreach (Slot modSlot in (slot.ContainedItem as Mod).Slots)
                {
                    if (modSlot.ContainedItem != null) ModEquipmentStore(modSlot, items);
                }
            }
        }
        public static void StoreEquipment(Profile profile)
        {
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
                        try
                        {
                            List<IItemComponent>? components = AccessTools.Field(typeof(Item), "Components").GetValue(contained) as List<IItemComponent>;
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

                        if (slotType == EquipmentSlot.Backpack) items.Add(contained.Id);
                        else
                        {
                            items.Add(contained.Id);
                            if (contained is Weapon)
                            {
                                foreach (Slot slot in (contained as Weapon).Slots)
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
                                if (contained is CompoundItem)
                                {
                                    foreach (Slot slot in (contained as CompoundItem).Slots)
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
            if (Instance == null) return new Dictionary<string, List<string>>();
            return Instance._followersEquipment;
        }
    }
}
