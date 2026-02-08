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
                        pitAIBossPlayer boss = BossPlayers.GetBoss(player.ProfileId);
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
    internal class GamePlayerOwnerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GamePlayerOwner), "PlayPhraseOrGesture");
        }

        [PatchPrefix]
        private static bool PatchPrefix(GamePlayerOwner __instance, int actionId, bool aggressive)
        {
            pitAIBossPlayer boss = BossPlayers.GetBoss(__instance.Player.ProfileId);

            if ((EPhraseTrigger)actionId == (EPhraseTrigger)CustomPhrases.TeamStatus)
            {

                if (boss != null)
                {
                    BotEventHandler.GClass670 info = new BotEventHandler.GClass670
                    {
                        phrase = (EPhraseTrigger)CustomPhrases.TeamStatus,
                        PlayerRequester = __instance.Player
                    };

                    boss.PhraseSaid(info);

                    foreach (var receiver in Receivers.GetReceivers())
                    {
                        receiver.Value.PhraseSaid(info);
                    }
                }
                return false;

            }
            else if ((EPhraseTrigger)actionId == (EPhraseTrigger)CustomPhrases.OverThere)
            {

                if (boss != null)
                {
                    InteractableObjects.CheckSeenEnemies(boss.Player());
                }

                if (!__instance.Player.HandsController.IsInInteractionStrictCheck())
                {
                    if (__instance.Player.HandsController is Player.FirearmController)
                    {

                        foreach (var receiver in Receivers.GetReceivers())
                        {
                            GestureData data = new GestureData
                            {
                                Gesture = (EInteraction)CustomGestures.OverThere,
                                Player = __instance.Player
                            };

                            receiver.Value.GestusShown(data);
                        }

                        (__instance.Player.HandsController as Player.FirearmController).CurrentOperation.ShowGesture(EInteraction.ThereGesture);

                    }
                    else if (__instance.Player.HandsIsEmpty)
                    {

                        foreach (var receiver in Receivers.GetReceivers())
                        {
                            GestureData data = new GestureData
                            {
                                Gesture = (EInteraction)CustomGestures.OverThere,
                                Player = __instance.Player
                            };

                            receiver.Value.GestusShown(data);
                        }
                    }
                }


                return false;
            }
            // fix for 0.15 not triggering the gesture shown event when it comes from the boss
            if (boss != null && actionId <= 9)
            {

                foreach (var receiver in Receivers.GetReceivers())
                {
                    receiver.Value.GestusShown(new GestureData
                    {
                        Gesture = (EInteraction)actionId,
                        Player = boss.Player()
                    });
                }
            }

            return true;
        }
    }
    /** Check if player killed a Goon to see if we need to penalize him**/
    internal class GClass1999KillPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(LocationStatisticsCollectorAbstractClass), "OnEnemyKill");
        }

        [PatchPostfix]
        private static void PatchPostfix(LocationStatisticsCollectorAbstractClass __instance, DamageInfoStruct damage, EDamageType lethalDamageType, EBodyPart bodyPart, EPlayerSide playerSide, WildSpawnType role, string playerAccountId, string playerProfileId, string playerName, string groupId, int level, int killExp, float distance, int hour, List<string> targetEquipment, HealthEffects enemyEffects, List<string> zoneIds, bool isFriendly, bool isAI)
        {
            Player player = __instance.player_0;
            if (player == null || !BossPlayers.IsPlayerBoss(player.ProfileId)) return;

            // penalize Knight standing if player kills any of the goons after they become netural
            if (Utils.Props.BossFollowersType.Contains(role) && player.Profile.TryGetTraderInfo(Utils.Props.KnightTrader, out var traderInfo) && !traderInfo.Disabled)
            {

                foreach (var data in player.Profile.QuestsData)
                {
                    if (Utils.Props.Quests["Knight"][0] == data.Id && data.Status == EFT.Quests.EQuestStatus.Success)
                    {

                        double standing = player.Profile.GetTraderStanding(Utils.Props.KnightTrader);
                        traderInfo.SetStanding(Math.Max(0.1, standing - 0.1));

                        if (groupId == player.Profile.Info.GroupId) break;

                        // - turn the Rogue faction hostile for the duration of the raid
                        Utils.Utils.FlagSet("knightKiller_" + player.ProfileId, true);
                        foreach (KeyValuePair<BotZone, GClass555> keyValuePair in Singleton<IBotGame>.Instance.BotsController.Groups())
                        {
                            foreach (BotsGroup botsGroup in keyValuePair.Value.GetGroups(true))
                            {
                                if (botsGroup.Side == EPlayerSide.Savage && botsGroup.InitialBotType == WildSpawnType.exUsec)
                                {
                                    botsGroup.AddEnemy(player, EBotEnemyCause.addPlayerToBoss);
                                }
                            }
                        }

                        break;
                    }
                }
            }
        }
    }
    /** Check if a follower made a kill to see if we need to count it as a quest kill **/
    internal class PlayerKilledPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "OnBeenKilledByAggressor");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, IPlayer aggressor, DamageInfoStruct damageInfo, EBodyPart bodyPart, EDamageType lethalDamageType)
        {
            try
            {
                if (aggressor == null || aggressor.Profile == null || aggressor.Profile.Info == null || aggressor.Profile.Info.Settings == null) return;

                Player enemyPlayer = __instance.GameWorld.GetAlivePlayerByProfileID(aggressor.ProfileId);
                if (enemyPlayer == null)
                {
                    return;
                }

                EPlayerSide playerSide = __instance.Side;

                Components.BotFollowerPlayer follower = BossPlayers.GetFollowers().Find((fl) => fl.GetBot().ProfileId == enemyPlayer.ProfileId);

                if (follower == null) return;

                BotOwner bot = follower.GetBot();
                pitAIBossPlayer boss = follower.GetBoss();

                // have kills of the Goons count as quest kills when needed
                bool knightKill = false;
                bool pipeKill = false;
                bool birdEyeKill = false;

                if (Utils.Props.BossFollowersType.Contains(enemyPlayer.Profile.Info.Settings.Role))
                {

                    // - check if the aggressor is Knight
                    if (enemyPlayer.Profile.Info.Settings.Role == WildSpawnType.bossKnight)
                    {
                        knightKill = true;
                    }
                    // - check if the aggressor is BigPipe
                    else if (enemyPlayer.Profile.Info.Settings.Role == WildSpawnType.followerBigPipe)
                    {
                        pipeKill = true;
                    }
                    // - check if the aggressor is BirdEye
                    else if (enemyPlayer.Profile.Info.Settings.Role == WildSpawnType.followerBirdEye)
                    {
                        birdEyeKill = true;
                    }
                }

                // - partial recreation of the "Test" condition that normally runs for player
                List<string> list = new List<string>();
                Item weapon2 = damageInfo.Weapon;

                list.Add("Any");

                if (playerSide == EPlayerSide.Usec)
                {
                    list.Add("Usec");
                    list.Add("AnyPmc");
                }
                else if (playerSide == EPlayerSide.Bear)
                {
                    list.Add("Bear");
                    list.Add("AnyPmc");
                }
                else if (playerSide == EPlayerSide.Savage)
                {
                    list.Add("Savage");
                    list.Add("Bot");
                }


                string location = enemyPlayer.Location;
                float distance = Vector3.Distance(enemyPlayer.Position, __instance.Position);
                float teamDistance = Vector3.Distance(enemyPlayer.Position, boss.Position);

                Utils.Utils.FlagSet("knightKill_" + boss.realPlayer.ProfileId, knightKill);
                Utils.Utils.FlagSet("pipeKill_" + boss.realPlayer.ProfileId, pipeKill);
                Utils.Utils.FlagSet("birdEyeKill_" + boss.realPlayer.ProfileId, birdEyeKill);


                // - check if the kill is a quest kill
                if ((teamDistance <= 80f || (teamDistance <= 120f && birdEyeKill)) && (knightKill || pipeKill || birdEyeKill || follower.IsSquadMate))
                {
                    try
                    {
                        // - - change profile that will be used for the quest kill so the correct equipment is counted (we do it like so as it's marked as readonly)
                        var profileField = typeof(AbstractQuestControllerClass).GetField("Profile", BindingFlags.Public | BindingFlags.Instance);
                        if (profileField != null)
                        {
                            FieldInfo attrField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
                            attrField?.SetValue(profileField, profileField.Attributes & ~FieldAttributes.InitOnly);

                            profileField.SetValue(boss.realPlayer.AbstractQuestControllerClass, aggressor.Profile);
                        }


                        list.ForEach(target =>
                        {
                            try
                            {
                                boss.realPlayer.AbstractQuestControllerClass.CheckKillConditionCounter(target, __instance.ProfileId, __instance.Inventory.EquippedInSlotsTemplateIds, weapon2, bodyPart, location, distance, __instance.Profile.Info.Settings.Role.ToStringNoBox<WildSpawnType>(), __instance.CurrentHour, __instance.HealthController.BodyPartEffects, enemyPlayer.HealthController.BodyPartEffects, __instance.TriggerZones, new string[] { });
                            }
                            catch (Exception ex)
                            {
                                Modules.Logger.LogError(ex);
                            }

                        });

                        // - - restore readonly profile
                        if (profileField != null)
                        {
                            profileField.SetValue(boss.realPlayer.AbstractQuestControllerClass, boss.realPlayer.Profile);
                        }

                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError(ex);
                    }

                    if (knightKill) Utils.Utils.FlagSet("knightKill_" + boss.realPlayer.ProfileId, false);
                    if (pipeKill) Utils.Utils.FlagSet("pipeKill_" + boss.realPlayer.ProfileId, false);
                    if (birdEyeKill) Utils.Utils.FlagSet("birdEyeKill_" + boss.realPlayer.ProfileId, false);

                }

                // - we are going to use the code that normally runs for the player but on the follower in order for his experience to be updated : LocationStatisticsCollectorAbstractClass.OnEnemyKill
                if (!follower.IsSquadMate) return;

                float num = 0f;
                SessionCountersClass sessionCounters = enemyPlayer.Profile.EftStats.SessionCounters;
                BackendConfigSettingsClass.GClass1720 experienceConfig = Singleton<BackendConfigSettingsClass>.Instance.Experience;
                if (bodyPart == EBodyPart.Head)
                {
                    if (playerSide - EPlayerSide.Usec > 1)
                    {
                        if (playerSide != EPlayerSide.Savage)
                        {
                            num = 0f;
                        }
                        else
                        {
                            num = experienceConfig.Kill.BotHeadShotMult;
                        }
                    }
                    else
                    {
                        num = experienceConfig.Kill.PmcHeadShotMult;
                    }
                }

                bool flag2 = weapon2 is ThrowWeapItemClass;

                int killExp = __instance.Profile.Info.Settings.Experience;

                int num2 = killExp;
                switch (playerSide)
                {
                    case EPlayerSide.Usec:
                    case EPlayerSide.Bear:
                        num2 = experienceConfig.Kill.VictimLevelExp;
                        break;
                    case EPlayerSide.Savage:
                        num2 = killExp;
                        if (num2 < 0)
                        {
                            num2 = experienceConfig.Kill.VictimBotLevelExp;
                        }

                        break;
                }

                int @int = sessionCounters.GetInt(SessionCounterTypesAbstractClass.Kills);
                float num3 = (float)experienceConfig.Kill.GetKillingBonusPercent(@int) / 100f;
                int num4 = (int)((float)num2 * num);
                int num5 = (int)((float)num2 * num3);


                if (num2 > 0)
                {
                    sessionCounters.AddInt(num2, SessionCounterTypesAbstractClass.ExpKillBase);
                }
                if (num4 > 0)
                {
                    sessionCounters.AddInt(num4, SessionCounterTypesAbstractClass.ExpKillBodyPartBonus);
                }
                if (num5 > 0)
                {
                    sessionCounters.AddInt(num5, SessionCounterTypesAbstractClass.ExpKillStreakBonus);
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
            }
        }
    }
    /** Have followers inscrease their skill when they shoot a weapon **/
    internal class PlayerShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "ExecuteShotSkill");
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player __instance, Item weapon)
        {
            foreach (var follower in BossPlayers.GetFollowers())
            {
                if (follower.IsSquadMate && follower.GetBot().ProfileId == __instance.ProfileId)
                {
                    if (!(weapon is ThrowWeapItemClass))
                    {
                        float num = 1f;
                        __instance.Skills.WeaponShotAction.Complete(weapon, num);
                    }
                    return false;
                }
            }
            return true;
        }

    }
}
