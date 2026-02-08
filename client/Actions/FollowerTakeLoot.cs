using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Action for a bot follower to take loot given by the player
     */
    public class FollowerTakeLoot : GClass170<StandardBrain>
    {
        private Components.BotFollowerPlayer _follower;

        private LootItem _lootItem = null;

        private bool bool_1 = false;

        private bool bool_2 = false;

        public FollowerTakeLoot(BotOwner bot) : base(bot)
        {
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            botOwner_0.DoorOpener.Update();


            if (bool_1)
            {
                return;
            }


            if (_follower == null)
            {
                _follower = BossPlayers.Instance.GetFollower(botOwner_0);
            }

            if (_lootItem == null)
            {
                _lootItem = botOwner_0.ItemTaker.ItemToTake;
            }


            if (_lootItem == null)
            {
                ClearLoot();
                return;
            }

            if (bool_2)
            {
                if (botOwner_0.GoToSomePointData.IsCome() && !bool_1)
                {
                    bool_1 = true;

                    PickupLoot().HandleExceptions();

                    return;
                }

                return;
            }

            botOwner_0.GoToSomePointData.SetPoint(InteractableObjects.GetLootPosition());
            botOwner_0.GoToSomePointData.UpdateToGo(false);
            botOwner_0.Steering.LookToMovingDirection();

            bool_2 = true;
        }


        private async Task PickupLoot()
        {
            Item item = _lootItem.Item;

            if (item == null)
            {
                ClearLoot();
                return;
            }

            Vector3 pos = _lootItem.transform.position;
            botOwner_0.Steering.LookToPoint(pos);

            await Task.Delay(2000);

            if (botOwner_0.IsDead || InteractableObjects.Instance == null || !InteractableObjects.IsTaker(botOwner_0))
            {
                ClearLoot();
                return;
            }

            try
            {
                InventoryController inventoryControllerClass = botOwner_0.GetPlayer.InventoryController;
                InventoryEquipment equipment = inventoryControllerClass.Inventory.Equipment;

                // order for general loot
                List<EquipmentSlot> possibleSlots = new List<EquipmentSlot> {
                    EquipmentSlot.Backpack,
                    EquipmentSlot.TacticalVest,
                    EquipmentSlot.ArmorVest,
                    EquipmentSlot.Pockets
                };
                List<Type> equipTypes = new List<Type>
                {
                    typeof(ThrowWeapItemClass),
                    typeof(MedicalItemClass),
                    typeof(MagazineItemClass)
                };
                // find an available grid in the equipment slots to which the key can be transferred
                bool wasTransferred = false;

                // - for special items, like grenades or mags, see if they can be equipped in the tactical vest
                bool isSpecial = equipTypes.Any(t => item.GetType() == t);

                if (isSpecial)
                {
                    var gStruct01 = InteractionsHandlerClass.QuickFindAppropriatePlace(_lootItem.Item, inventoryControllerClass, equipment.ToEnumerable<InventoryEquipment>(), InteractionsHandlerClass.EMoveItemOrder.PrioritizeTargetsOrder, true);
                    if (gStruct01.Succeeded)
                    {
                        botOwner_0.ItemTaker.method_1(botOwner_0.GetPlayer, gStruct01.Value, _lootItem.ItemOwner.RootItem, _lootItem.LastOwner);
                        wasTransferred = true;


                        if (item is MagazineItemClass mag)
                        {
                            inventoryControllerClass.StrictCheckMagazine(mag, false, 0, false, true);
                        }
                    }
                }
                // - for backpacks, check if the item can be equipped in the backpack slot
                // - for armor, check if the item can be equipped in the armor slot
                // - for tactical vests, check if the item can be equipped in the tactical vest slot
                // - for helmets, check if the item can be equipped in the helmet slot
                bool isBackpack = item.GetType() == typeof(BackpackItemClass);
                bool isArmor = item.GetType() == typeof(ArmorItemClass);
                bool isTacticalVest = item.GetType() == typeof(VestItemClass);
                bool isHelmet = item.GetType() == typeof(HeadwearItemClass);
                // equipment.GetSlot(EquipmentSlot.TacticalVest).ContainedItem == null
                if (isBackpack || isArmor || isTacticalVest || isHelmet)
                {
                    ItemAddress slotLocation = inventoryControllerClass.FindSlotToPickUp(item);
                    if (slotLocation != null)
                    {
                        var gstruct = InteractionsHandlerClass.Move(item, slotLocation, inventoryControllerClass, true);
                        if (gstruct.Succeeded)
                        {
                            wasTransferred = true;
                            await inventoryControllerClass.TryRunNetworkTransaction(gstruct, null);
                        }
                    }
                }

                // - if the item is a weapon, check if it can be equipped
                if (!wasTransferred && item is Weapon weapon && item.GetItemComponent<KnifeComponent>() == null)
                {
                    ItemAddress location = null;

                    if (
                        (item is PistolItemClass && equipment.GetSlot(EquipmentSlot.Holster).ContainedItem == null) ||
                        (item is PistolItemClass == false && equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem == null)
                    )
                        location = inventoryControllerClass.FindSlotToPickUp(weapon);

                    if (location != null)
                    {
                        var gstruct = InteractionsHandlerClass.Move(weapon, location, inventoryControllerClass, true);
                        if (gstruct.Succeeded)
                        {
                            wasTransferred = true;

                            await inventoryControllerClass.TryRunNetworkTransaction(gstruct, null);

                            try
                            {
                                botOwner_0.WeaponManager.UpdateWeaponsList();

                                await Task.Delay(1800);
                            }
                            catch (Exception e)
                            {
                                Modules.Logger.LogError(e);
                            }
                        }
                    }
                }

                if (!wasTransferred) foreach (EquipmentSlot slot in possibleSlots)
                    {

                        if (botOwner_0.ItemTaker.method_10(slot, _lootItem))
                        {
                            wasTransferred = true;
                            break;
                        }
                    }

                if (botOwner_0.IsDead || botOwner_0.BotState != EBotState.Active)
                {
                    ClearLoot();
                    return;
                }

                if (!wasTransferred)
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
                }

                if (wasTransferred && _follower.IsSquadMate)
                {
                    InteractableObjects.StoreItem(botOwner_0, item);
                }

                await Task.Delay(1000);
                ClearLoot();
            }
            catch (Exception e)
            {
                Modules.Logger.LogError("Failed to pickup Loot");
                Modules.Logger.LogError(e);
                ClearLoot();
            }
        }

        private void ClearLoot()
        {
            if (_lootItem != null) botOwner_0.ItemTaker.method_7(botOwner_0.ItemTaker.ItemToTake);

            InteractableObjects.RemoveTaker(botOwner_0);
            InteractableObjects.ClearCurLootItem();

            BotRequest currRequest = botOwner_0.BotRequestController.CurRequest;
            if (currRequest != null && currRequest.BotRequestType == (BotRequestType)CustomBotRequestType.TakeLoot)
            {
                currRequest.Complete();
            }

            bool_1 = false;
            bool_2 = false;
            _lootItem = null;
        }
    }
}
