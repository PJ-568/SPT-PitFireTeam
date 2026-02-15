using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using GestureData = GClass532;
using EventInfo = BotEventHandler.GClass692;

namespace friendlySAIN.Patches
{

    internal class AIDataContructPatch : ModulePatch
    {

        public static Dictionary<string, PlayerAIDataClass> playerAIData = new Dictionary<string, PlayerAIDataClass>();
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Constructor(typeof(PlayerAIDataClass), new Type[] { typeof(BotOwner), typeof(Player) });
        }
        // overwrite AIData to make it use our pitAIBossPlayer
        [PatchPostfix]
        private static void PatchPostfix(PlayerAIDataClass __instance, BotOwner owner, Player player)
        {
            if (owner == null && player != null)
            {
                // remove old AIBossPlayer
                try
                {
                    if (__instance.AIBossPlayer != null && __instance.AIBossPlayer.GetType() != typeof(pitAIBossPlayer))
                    {
                        __instance.AIBossPlayer.Dispose();
                        pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);
                        // replace AIBossPlayer with ours
                        if (boss != null)
                        {
                            var field = AccessTools.Field(typeof(PlayerAIDataClass), "AibossPlayer_0");
                            field.SetValue(__instance, boss);
                            Logger.LogInfo("Replaced AIBossPlayer in AIData with ours");
                        }
                    }

                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to dispose old AIBossPlayer");
                    Logger.LogError(ex);
                }



                if (!playerAIData.ContainsKey(player.ProfileId))
                    playerAIData.Add(player.ProfileId, __instance);
            }

        }
    }
    /** Handle firing TeamStatus and OverThere commands inside PlayPhraseOrGesture so that the enemy does not hear them **/
    internal class PlayPhraseOrGesturePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), "PlayPhraseOrGesture");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GamePlayerOwner __instance, int actionId, bool aggressive)
        {
            pitAIBossPlayer? boss = BossPlayers.GetBoss(__instance.Player.ProfileId);

            if (!GClass3937.IsPlayerGesture(actionId) &&
                (EPhraseTrigger)actionId == (EPhraseTrigger)CustomPhrases.TeamStatus)
            {

                if (boss != null)
                {
                    EventInfo info = new EventInfo
                    {
                        phrase = (EPhraseTrigger)CustomPhrases.TeamStatus,
                        PlayerRequester = __instance.Player
                    };

                    boss.PhraseSaid(info);

                }
                return false;

            }
            else if (!GClass3937.IsPlayerGesture(actionId) &&
                     (EPhraseTrigger)actionId == (EPhraseTrigger)CustomPhrases.OverThere)
            {

                if (boss != null)
                {
                    InteractableObjects.CheckSeenEnemies(boss.Player());
                }

                if (!__instance.Player.HandsController.IsInInteractionStrictCheck())
                {
                    GestureData data = new GestureData
                    {
                        Gesture = (EInteraction)CustomGestures.OverThere,
                        Player = __instance.Player
                    };

                    boss?.GestusShown(data);

                    // Use hands controller directly for player gestures.
                    __instance.Player.MovementContext.SetInteractInHands(EInteraction.ThereGesture);
                }


                return false;
            } else
            {
                // fix for boss gestures not propagating to followers
                List<EInteraction> bossInteractions = new List<EInteraction> { EInteraction.ComeWithMeGesture, EInteraction.ThereGesture, EInteraction.HoldGesture };
                if (boss != null && bossInteractions.Contains((EInteraction)actionId))
                {
                    boss.GestusShown(new GestureData
                    {
                        Gesture = (EInteraction)actionId,
                        Player = __instance.Player
                    });
                }

            }

            return true;
        }
    }
}
