using EFT;
using friendlySAIN.BigBrain;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;


namespace friendlySAIN.Patches
{
    internal class FollowRequestPatch : ModulePatch
    {

        private static double MathClamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGroupRequestController), "TryAskFollowMeRequest");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotGroupRequestController __instance, ref bool __result, IPlayer player, BotOwner posibleExecuter)
        {

            pitAIBossPlayer playerBoss = BossPlayers.Instance.GetBossPlayer(player.ProfileId);

            if (playerBoss != null && posibleExecuter != null)
            {
                bool isAFollower = BossPlayers.IsFollower(posibleExecuter);

                if (isAFollower)
                {
                    // if BOT is already a follower, allow "follow me" request to take place if it is the boss who is requesting it
                    if (posibleExecuter.BotFollower.HaveBoss)
                    {
                        if (posibleExecuter.BotFollower.BossToFollow.IsMe(playerBoss.Player()))
                        {
                            return true;
                            // - this is a follower of someone else
                        }
                        else
                        {
                            posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                            posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                            __result = false;
                            return false;
                        }
                    }
                }
                // allow player to request a BOT to follow him
                if (player.Side == posibleExecuter.Side)
                {
                    Utils.SpawnHelper.EnsureRecruitDefaults();

                    bool canPickup = false;
                    List<Components.BotFollowerPlayer> followers = BossPlayers.GetFollowersByBoss(player.ProfileId);
                    List<Components.BotFollowerPlayer> activeFollowers = followers.FindAll(f =>
                    {
                        if (f == null) return false;
                        BotOwner bot = f.GetBot();
                        return bot != null &&
                               !bot.IsDead &&
                               bot.BotState == EBotState.Active &&
                               bot.GetPlayer != null &&
                               bot.GetPlayer.HealthController != null &&
                               bot.GetPlayer.HealthController.IsAlive;
                    });
                    int configuredPickups = Math.Max(1, Utils.SpawnHelper.Pickups);
                    int pickLimit = configuredPickups + activeFollowers.FindAll(f => f.IsSquadMate).Count;
                    int hardPickupLimit = Math.Min(10, configuredPickups);
                    int currentPickups = activeFollowers.FindAll(f => !f.IsSquadMate).Count;
                    // if restrictions are enabled
                    if (Utils.SpawnHelper.Restrictions && pickLimit > 0)
                    {
                        // - SCAV : based on fence level
                        if (player.Side == EPlayerSide.Savage)
                        {
                            double standing = player.Profile.FenceInfo.Standing;

                            if (standing >= 1.0)
                            {
                                double ratio = (standing - 1.0) / (6.0 - 1.0); // 0 to 1
                                int maxAllowedByStanding = (int)Math.Round(1 + ratio * (10 - 1));

                                int effectiveLimit = Math.Min(maxAllowedByStanding, hardPickupLimit);

                                if (currentPickups < effectiveLimit)
                                {
                                    canPickup = true;
                                }
                            }
                        }
                        // - PMC:
                        else
                        {
                            int playerLevel = player.Profile.Info.Level;
                            int botLevel = posibleExecuter.Profile.Info.Level;
                            int levelDiff = playerLevel - botLevel;
                            // - - limit reached → deny
                            if (currentPickups >= hardPickupLimit)
                            {
                                canPickup = false;
                            }
                            else if (levelDiff >= 10)
                            {
                                // - - player much stronger → always allow
                                canPickup = true;
                            }
                            else if (levelDiff <= -10)
                            {
                                // - - bot much stronger → always deny
                                canPickup = false;
                            }
                            else if (playerLevel == botLevel)
                            {
                                // -- equal levels → 50/50 chance
                                canPickup = new System.Random().NextDouble() < 0.5;
                            }
                            else
                            {
                                // -- different levels → 0% to 100% chance based on level difference
                                double chance = MathClamp((levelDiff + 10) / 20.0, 0.0, 1.0);
                                canPickup = new System.Random().NextDouble() < chance;
                            }
                        }
                    }
                    else
                    {
                        canPickup = currentPickups < hardPickupLimit;
                    }

                    // add BOT as follower to the player BOSS if limit was not reached
                    if (canPickup)
                    {
                        // Defer conversion to BotOwner manual-update cycle to avoid recruit-time activation races.
                        BotOwnerManualUpdatePatch.BotOwnerUpdate[posibleExecuter.ProfileId] = me =>
                        {
                            BotOwnerManualUpdatePatch.BotOwnerUpdate.Remove(me.ProfileId);

                            try
                            {
                                if (me == null || me.IsDead || me.BotState != EBotState.Active || me.GetPlayer == null || !me.GetPlayer.HealthController.IsAlive)
                                {
                                    return;
                                }

                                if (BossPlayers.AddFollower(me, playerBoss) != null)
                                {
                                    Utils.Utils.SetTimeout(() =>
                                    {
                                        if (me.IsDead || me.BotState != EBotState.Active) return;
                                        me.BotTalk.TrySay(EPhraseTrigger.Roger, false);
                                        me.Gesture.TryGestus(EInteraction.OkGesture, true);
                                    }, UnityEngine.Random.Range(300, 700));
                                }
                                else
                                {
                                    me.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                                }
                            }
                            catch (Exception ex)
                            {
                                Modules.Logger.LogError("Failed deferred recruit conversion");
                                Modules.Logger.LogError(ex);
                            }
                        };
                    }
                    else
                    {
                        // bot signals "NO"
                        posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                        posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);
                    }

                    __result = false;
                    return false;
                }
                else
                {
                    // bot signals "NO"
                    posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Toxic);
                    posibleExecuter.Gesture.TryGestus(EInteraction.GetOffGesture, true);
                    __result = false;
                    return false;
                }

            }
            // allow default to take place
            return true;
        }
    }

    internal class HoldRequestPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BotGroupRequestController), "TryActivateWait");

        }
        [PatchPrefix]
        private static bool PatchPrefix(BotGroupRequestController __instance, IPlayer player, BotOwner posibleExecuter)
        {

            pitAIBossPlayer playerBoss = BossPlayers.Instance.GetBossPlayer(player.ProfileId);


            if (playerBoss != null && posibleExecuter != null)
            {
                // boss can only send hold requests to it's followers
                if (BossPlayers.IsFollower(posibleExecuter, playerBoss))
                {

                    return true;
                }

                // bot signals "NO"
                posibleExecuter.BotTalk.TrySay(EPhraseTrigger.Negative);
                posibleExecuter.Gesture.TryGestus(EInteraction.NoGesture, true);

                return false;
            }
            // allow default to take place
            return true;
        }
    }

}
