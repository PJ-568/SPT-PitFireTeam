using Comfort.Common;
using EFT;
using EFT.Counters;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.Quests;
using pitTeam.Components;
using pitTeam.Modules;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

using GestureData = GClass532;
using EventInfo = BotEventHandler.GClass692;

namespace pitTeam.Patches
{
    internal static class BossGestureCommandRouter
    {
        public static bool IsBossCommandGesture(EInteraction gesture)
        {
            return gesture == EInteraction.ComeWithMeGesture ||
                   gesture == EInteraction.ThereGesture ||
                   gesture == EInteraction.HoldGesture ||
                   gesture == (EInteraction)CustomGestures.OverThere;
        }

        public static bool TryPlayOverThereGesture(Player player)
        {
            if (player == null)
            {
                return false;
            }

            pitAIBossPlayer? boss = BossPlayers.GetBoss(player.ProfileId);
            if (boss != null)
            {
                InteractableObjects.CheckSeenEnemies(boss.Player());
                boss.GestusShown(new GestureData
                {
                    Gesture = (EInteraction)CustomGestures.OverThere,
                    Player = player
                });
            }

            if (!player.HandsController.IsInInteractionStrictCheck())
            {
                player.MovementContext.SetInteractInHands(EInteraction.ThereGesture);
            }

            return true;
        }
    }

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
    /** Handle firing TeamStatus and boss gesture commands inside PlayPhraseOrGesture so enemies do not hear command-only traffic. **/
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
            else
            {
                // fix for boss gestures not propagating to followers
                EInteraction gesture = (EInteraction)actionId;
                if (gesture == (EInteraction)CustomGestures.OverThere)
                {
                    BossGestureCommandRouter.TryPlayOverThereGesture(__instance.Player);
                    return false;
                }

                if (boss != null && BossGestureCommandRouter.IsBossCommandGesture(gesture))
                {
                    boss.GestusShown(new GestureData
                    {
                        Gesture = gesture,
                        Player = __instance.Player
                    });
                }

            }

            return true;
        }
    }

    /** Have follower kills grant the same raid XP counters used by the legacy plugin. **/
    internal class PlayerKilledPatch : ModulePatch
    {
        private static FieldInfo _questControllerProfileField;
        private static bool _questControllerProfileFieldPrepared;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "OnBeenKilledByAggressor");
        }

        [PatchPostfix]
        private static void PatchPostfix(Player __instance, IPlayer aggressor, DamageInfoStruct damageInfo, EBodyPart bodyPart, EDamageType lethalDamageType)
        {
            try
            {
                if (aggressor?.Profile?.Info?.Settings == null || __instance?.GameWorld == null)
                {
                    return;
                }

                Player followerPlayer = __instance.GameWorld.GetAlivePlayerByProfileID(aggressor.ProfileId);
                if (followerPlayer == null)
                {
                    return;
                }

                BotFollowerPlayer follower = BossPlayers.GetFollowers().Find(fl => fl.GetBot().ProfileId == followerPlayer.ProfileId);
                if (follower == null || !follower.IsSquadMate)
                {
                    return;
                }

                TryCreditFollowerKillQuestProgress(__instance, followerPlayer, follower, damageInfo, bodyPart);

                EPlayerSide killedPlayerSide = __instance.Side;
                SessionCountersClass sessionCounters = followerPlayer.Profile.EftStats.SessionCounters;
                var experienceSettings = Singleton<BackendConfigSettingsClass>.Instance.Experience;

                float headshotMultiplier = 0f;
                if (bodyPart == EBodyPart.Head)
                {
                    if (killedPlayerSide - EPlayerSide.Usec > 1)
                    {
                        if (killedPlayerSide == EPlayerSide.Savage)
                        {
                            headshotMultiplier = experienceSettings.Kill.BotHeadShotMult;
                        }
                    }
                    else
                    {
                        headshotMultiplier = experienceSettings.Kill.PmcHeadShotMult;
                    }
                }

                int killExperience = __instance.Profile.Info.Settings.Experience;
                int baseKillExperience = killExperience;
                switch (killedPlayerSide)
                {
                    case EPlayerSide.Usec:
                    case EPlayerSide.Bear:
                        baseKillExperience = experienceSettings.Kill.VictimLevelExp;
                        break;
                    case EPlayerSide.Savage:
                        if (baseKillExperience < 0)
                        {
                            baseKillExperience = experienceSettings.Kill.VictimBotLevelExp;
                        }

                        break;
                }

                int killCount = sessionCounters.GetInt(SessionCounterTypesAbstractClass.Kills);
                float streakMultiplier = (float)experienceSettings.Kill.GetKillingBonusPercent(killCount) / 100f;
                int bodyPartBonus = (int)(baseKillExperience * headshotMultiplier);
                int streakBonus = (int)(baseKillExperience * streakMultiplier);

                if (baseKillExperience > 0)
                {
                    sessionCounters.AddInt(baseKillExperience, SessionCounterTypesAbstractClass.ExpKillBase);
                }

                if (bodyPartBonus > 0)
                {
                    sessionCounters.AddInt(bodyPartBonus, SessionCounterTypesAbstractClass.ExpKillBodyPartBonus);
                }

                if (streakBonus > 0)
                {
                    sessionCounters.AddInt(streakBonus, SessionCounterTypesAbstractClass.ExpKillStreakBonus);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        private static void TryCreditFollowerKillQuestProgress(
            Player victim,
            Player followerPlayer,
            BotFollowerPlayer follower,
            DamageInfoStruct damageInfo,
            EBodyPart bodyPart)
        {
            pitAIBossPlayer boss = follower.GetBoss();
            if (boss?.realPlayer?.AbstractQuestControllerClass == null)
            {
                return;
            }

            if (Vector3.Distance(followerPlayer.Position, boss.Position) > 80f)
            {
                return;
            }

            List<string> targets = BuildQuestKillTargets(victim.Side);
            if (targets.Count == 0)
            {
                return;
            }

            AbstractQuestControllerClass questController = boss.realPlayer.AbstractQuestControllerClass;
            Profile originalProfile = null;

            try
            {
                FieldInfo profileField = GetQuestControllerProfileField();
                if (profileField != null)
                {
                    originalProfile = profileField.GetValue(questController) as Profile;
                    profileField.SetValue(questController, followerPlayer.Profile);
                }

                string location = followerPlayer.Location;
                float distance = Vector3.Distance(followerPlayer.Position, victim.Position);
                HealthEffects victimEffects = victim.HealthController?.BodyPartEffects;
                HealthEffects followerEffects = followerPlayer.HealthController?.BodyPartEffects;
                string[] followerBuffs = followerPlayer.HealthController?.ActiveBuffsNames() ?? Array.Empty<string>();
                List<string> victimZones = victim.TriggerZones ?? new List<string>();
                List<string> victimEquipment = victim.Inventory?.EquippedInSlotsTemplateIds ?? new List<string>();
                string victimRole = victim.Profile?.Info?.Settings?.Role.ToStringNoBox<WildSpawnType>() ?? string.Empty;

                foreach (string target in targets)
                {
                    questController.CheckKillConditionCounter(
                        target,
                        victim.ProfileId,
                        victimEquipment,
                        damageInfo.Weapon,
                        bodyPart,
                        location,
                        distance,
                        victimRole,
                        victim.CurrentHour,
                        victimEffects,
                        followerEffects,
                        victimZones,
                        followerBuffs);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                try
                {
                    FieldInfo profileField = GetQuestControllerProfileField();
                    if (profileField != null && originalProfile != null)
                    {
                        profileField.SetValue(questController, originalProfile);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex);
                }
            }
        }

        private static List<string> BuildQuestKillTargets(EPlayerSide victimSide)
        {
            List<string> targets = new List<string> { "Any" };
            switch (victimSide)
            {
                case EPlayerSide.Usec:
                    targets.Add("Usec");
                    targets.Add("AnyPmc");
                    break;
                case EPlayerSide.Bear:
                    targets.Add("Bear");
                    targets.Add("AnyPmc");
                    break;
                case EPlayerSide.Savage:
                    targets.Add("Savage");
                    targets.Add("Bot");
                    break;
            }

            return targets;
        }

        private static FieldInfo GetQuestControllerProfileField()
        {
            if (_questControllerProfileFieldPrepared)
            {
                return _questControllerProfileField;
            }

            _questControllerProfileFieldPrepared = true;
            _questControllerProfileField = typeof(AbstractQuestControllerClass).GetField("Profile", BindingFlags.Public | BindingFlags.Instance);
            if (_questControllerProfileField == null)
            {
                return null;
            }

            FieldInfo attributesField = typeof(FieldInfo).GetField("m_fieldAttributes", BindingFlags.NonPublic | BindingFlags.Instance);
            attributesField?.SetValue(_questControllerProfileField, _questControllerProfileField.Attributes & ~FieldAttributes.InitOnly);
            return _questControllerProfileField;
        }
    }

    /** Have followers increase weapon skills when they shoot. **/
    internal class PlayerShotPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Player), "ExecuteShotSkill");
        }

        [PatchPrefix]
        private static bool PatchPrefix(Player __instance, Item weapon)
        {
            pitAIBossPlayer? bossPlayer = BossPlayers.GetBoss(__instance.ProfileId);

            foreach (var follower in BossPlayers.GetFollowers())
            {
                if (!follower.IsSquadMate || follower.GetBot().ProfileId != __instance.ProfileId)
                {
                    continue;
                }

                if (weapon is not ThrowWeapItemClass)
                {
                    __instance.Skills.WeaponShotAction.Complete(weapon, 1f);
                }

                return false;
            }

            return true;
        }
    }
}
