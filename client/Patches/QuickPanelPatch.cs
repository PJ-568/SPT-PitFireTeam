using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI.Gestures;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace pitTeam.Patches
{
    internal class QuickPanelPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesQuickPanel), "method_1");
        }
        /** Patch QuickGesturesPanel to disable the "Cooperative" phrase if bot is a follower **/
        [PatchPrefix]
        private static bool PatchPrefix(GesturesQuickPanel __instance)
        {
            Player player = (Player)AccessTools.Field(typeof(GesturesQuickPanel), "player_0").GetValue(__instance);
            if (player != null)
            {
                try
                {
                    // original
                    LootItem? lootItem = player.InteractableObject as LootItem;
                    bool flag = lootItem != null && lootItem.ItemOwner.RootItem.GetItemComponent<KeyComponent>() != null;
                    bool flag2 = lootItem != null && lootItem.ItemOwner.RootItem is MoneyItemClass;
                    bool flag3 = lootItem != null && (lootItem.ItemOwner.RootItem is Weapon || lootItem.ItemOwner.RootItem.GetItemComponent<KnifeComponent>() != null);

                    // modification here - set what loot item will be picked up
                    if (lootItem != null && !flag && !flag2) InteractableObjects.SetCurLootItem(lootItem);
                    else InteractableObjects.SetCurLootItem(null);

                    // original - loot command
                    __instance.method_7(EPhraseTrigger.LootKey, flag);
                    __instance.method_7(EPhraseTrigger.LootMoney, flag2);
                    __instance.method_7(EPhraseTrigger.LootWeapon, flag3);
                    __instance.method_7(EPhraseTrigger.LootGeneric, lootItem != null && !flag && !flag2 && !flag3);
                    // modification here - disable loot body and loot container command
                    Corpse? corpse = player.InteractableObject as Corpse;
                    __instance.method_7(EPhraseTrigger.LootBody, false);
                    __instance.method_7(EPhraseTrigger.CheckHim, false);
                    __instance.method_7(EPhraseTrigger.LootContainer, false);
                }
                catch (Exception e)
                {
                    Logger.LogError("Loot Command Failed:");
                    Logger.LogError(e);
                }

                // modification here - open door command
                Door? door = player.InteractableObject as Door;
                try
                {
                    bool canOpen = door != null && door.DoorState != EDoorState.Open;
                    if (door != null && canOpen)
                    {
                        InteractableObjects.SetCurDoor(door);
                    }
                    __instance.method_7(EPhraseTrigger.OpenDoor, canOpen);
                }
                catch (Exception e)
                {
                    Logger.LogError("Open Door Command Failed:");
                    Logger.LogError(e);
                }

                // original
                __instance.method_7(EPhraseTrigger.LockedDoor, door != null && (door.DoorState == EDoorState.Locked || door.DoorState == EDoorState.Shut));

                // Show cooperation for any alive non-follower AI target.
                try
                {
                    if (!pitFireTeam.pickupEnabled.Value)
                    {
                        __instance.method_7(EPhraseTrigger.Cooperation, false);
                        return false;
                    }

                    if (player.InteractablePlayer != null && player.InteractablePlayer.IsAI && player.InteractablePlayer.HealthController.IsAlive)
                    {
                        BotOwner targetBot = player.InteractablePlayer.AIData?.BotOwner;
                        if (targetBot == null || BossPlayers.IsFollower(targetBot) || player.InteractablePlayer.Side != player.Side)
                        {
                            __instance.method_7(EPhraseTrigger.Cooperation, false);

                            return false;
                        }
                        else
                        {
                            __instance.method_7(EPhraseTrigger.Cooperation, true);
                        }
                    }
                    else
                    {
                        __instance.method_7(EPhraseTrigger.Cooperation, false);
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError("Cooperation Command Failed:");
                    Logger.LogError(e);
                }

                return false;
            }

            return true;
        }
    }
}
