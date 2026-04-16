using EFT;

using EFT.InventoryLogic;
using friendlySAIN;
using friendlySAIN.Modules;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using friendlySAIN.BigBrain;
using UnityEngine;

namespace friendlySAIN.Patches
{
    internal class SAINPatch
    {
        private const float FollowerCloseIntentRecentSeenSeconds = 2.25f;
        private static Type? squadType = null;
        private static Type? SAINEnableClass = null;
        private static Type? combatSoloLayerType = null;
        private static Type? combatSquadLayerType = null;
        private static Type? peacefulLayerType = null;
        private static Type? avoidThreatLayerType = null;
        private static Type? extractLayerType = null;
        private static Type? flashBangedLayerType = null;
        private static Type? debugLayerType = null;

        private static Type? enemyTalk = null;
        private static Type? GroupClass = null;
        private static Type? sainPlayerTalkPatch = null;
        private static Type? sainBotTalkPatch = null;
        private static Type? sainBotTalkManualUpdatePatch = null;
        private static Type? sainPlayerComponentType = null;
        private static Type? sainMoverClass = null;
        private static Type? selfActionDecisionClassType = null;
        private static Type? sainShootDataType = null;
        private static PropertyInfo? sainPlayerComponentPlayerProperty = null;
        public static void PatchSAINIfInstalled(Harmony harmony)
        {
            if (!friendlySAIN.IsSAINInstalled) return;

            if (squadType == null)
            {
                squadType = Type.GetType("SAIN.BotController.Classes.Squad, SAIN");
            }

            if (SAINEnableClass == null)
            {
                SAINEnableClass = Type.GetType("SAIN.SAINEnableClass, SAIN");
            }


            if (enemyTalk == null)
            {
                enemyTalk = Type.GetType("SAIN.SAINComponent.Classes.Talk.EnemyTalk, SAIN");

            }

            if (GroupClass == null)
            {
                GroupClass = Type.GetType("SAIN.SAINComponent.Classes.Talk.GroupTalk, SAIN");
            }

            if (sainPlayerTalkPatch == null)
            {
                sainPlayerTalkPatch = Type.GetType("SAIN.Patches.Talk.PlayerTalkPatch, SAIN");
            }

            if (sainBotTalkPatch == null)
            {
                sainBotTalkPatch = Type.GetType("SAIN.Patches.Talk.BotTalkPatch, SAIN");
            }

            if (sainBotTalkManualUpdatePatch == null)
            {
                sainBotTalkManualUpdatePatch = Type.GetType("SAIN.Patches.Talk.BotTalkManualUpdatePatch, SAIN");
            }

            if (sainPlayerComponentType == null)
            {
                sainPlayerComponentType = Type.GetType("SAIN.Components.PlayerComponent, SAIN");
                sainPlayerComponentPlayerProperty = sainPlayerComponentType?.GetProperty("Player");
            }

            if (enemyTalk != null)
            {
                harmony.Patch(AccessTools.Method(enemyTalk, "playerTalked"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchPlayerTalked), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            if (GroupClass != null)
            {
                harmony.Patch(AccessTools.Method(GroupClass, "EnemyConversation"), new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchEnemyConvesation), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
            }

            PatchFollowerLayerFallbackIfAddonMissing(harmony);
            PatchFollowerCombatPatrolStanceWithoutAddon(harmony);
            PatchFollowerReloadBlockIfAddonMissing(harmony);
            PatchFollowerWeaponSelectionGuard(harmony);
            PatchSainTalkPrefixesForFollowers(harmony);
            PatchSainPlayerVoiceLineForFollowers(harmony);


            if (squadType != null && SAINEnableClass != null)
            {
                var assignLeader = AccessTools.Method(squadType, "assignSquadLeader");
                if (assignLeader != null)
                {
                    harmony.Patch(assignLeader, new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(PatchAssignSquadLeader), BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)));
                }
                Modules.Logger.LogInfo("SAIN Patched");
            }
        }

        private static void PatchFollowerCombatPatrolStanceWithoutAddon(Harmony harmony)
        {
            sainMoverClass ??= Type.GetType("SAIN.SAINComponent.Classes.Mover.SAINMoverClass, SAIN");

            MethodInfo? updateStance = sainMoverClass != null
                ? AccessTools.Method(sainMoverClass, "UpdateStance", new[] { typeof(float) })
                : null;
            if (updateStance != null)
            {
                harmony.Patch(updateStance, postfix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(ForcePatrolOffForFollowerCombat), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerReloadBlockIfAddonMissing(Harmony harmony)
        {
            selfActionDecisionClassType ??= Type.GetType("SAIN.SAINComponent.Classes.Decision.SelfActionDecisionClass, SAIN");
            if (selfActionDecisionClassType == null)
            {
                return;
            }

            MethodInfo? tryReload = AccessTools.GetDeclaredMethods(selfActionDecisionClassType)
                .FirstOrDefault(method =>
                    method.Name == "TryReload" &&
                    method.GetParameters().Length == 2 &&
                    method.GetParameters()[0].ParameterType == typeof(BotOwner));

            if (tryReload != null)
            {
                harmony.Patch(
                    tryReload,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BlockFollowerSainReloadIfAddonMissing), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerWeaponSelectionGuard(Harmony harmony)
        {
            sainShootDataType ??= Type.GetType("SAIN.SAINComponent.Classes.SAINShootData, SAIN");
            if (sainShootDataType == null)
            {
                return;
            }

            MethodInfo? tryChangeWeapon = AccessTools.Method(sainShootDataType, "TryChangeWeapon", new[] { typeof(EquipmentSlot) });
            if (tryChangeWeapon != null)
            {
                harmony.Patch(
                    tryChangeWeapon,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(GuardFollowerSainWeaponSelection), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        private static void PatchFollowerLayerFallbackIfAddonMissing(Harmony harmony)
        {
            combatSoloLayerType ??= Type.GetType("SAIN.Layers.Combat.Solo.CombatSoloLayer, SAIN");
            combatSquadLayerType ??= Type.GetType("SAIN.Layers.Combat.Squad.CombatSquadLayer, SAIN");
            peacefulLayerType ??= Type.GetType("SAIN.Layers.Peace.PeacefulLayer, SAIN");
            avoidThreatLayerType ??= Type.GetType("SAIN.Layers.SAINAvoidThreatLayer, SAIN");
            extractLayerType ??= Type.GetType("SAIN.Layers.ExtractLayer, SAIN");
            flashBangedLayerType ??= Type.GetType("SAIN.Layers.Peace.FlashBangedLayer, SAIN");
            debugLayerType ??= Type.GetType("SAIN.Layers.Combat.Run.DebugLayer, SAIN");

            var disablePrefix = new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(DisableSainLayerForFollowersWithoutAddon), BindingFlags.NonPublic | BindingFlags.Static));

            PatchLayerIsActive(harmony, combatSoloLayerType, disablePrefix);
            PatchLayerIsActive(harmony, combatSquadLayerType, disablePrefix);
            PatchLayerIsActive(harmony, peacefulLayerType, disablePrefix);
            PatchLayerIsActive(harmony, avoidThreatLayerType, disablePrefix);
            PatchLayerIsActive(harmony, extractLayerType, disablePrefix);
            PatchLayerIsActive(harmony, flashBangedLayerType, disablePrefix);
            PatchLayerIsActive(harmony, debugLayerType, disablePrefix);
        }

        private static void PatchLayerIsActive(Harmony harmony, Type? layerType, HarmonyMethod disablePrefix)
        {
            MethodInfo? isActive = layerType != null ? AccessTools.Method(layerType, "IsActive") : null;
            if (isActive != null)
            {
                harmony.Patch(isActive, prefix: disablePrefix);
            }
        }

        private static void PatchSainTalkPrefixesForFollowers(Harmony harmony)
        {
            var bypassPrefix = new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(BypassSainTalkPatchForFollower), BindingFlags.NonPublic | BindingFlags.Static));

            if (sainPlayerTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainPlayerTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkPatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkPatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }

            if (sainBotTalkManualUpdatePatch != null)
            {
                MethodInfo? patchPrefix = AccessTools.Method(sainBotTalkManualUpdatePatch, "PatchPrefix");
                if (patchPrefix != null)
                {
                    harmony.Patch(patchPrefix, prefix: bypassPrefix);
                }
            }
        }

        private static void PatchSainPlayerVoiceLineForFollowers(Harmony harmony)
        {
            MethodInfo? playVoiceLine =
                sainPlayerComponentType != null
                ? AccessTools.Method(sainPlayerComponentType, "PlayVoiceLine", new[] { typeof(EPhraseTrigger), typeof(ETagStatus), typeof(bool) })
                : null;

            if (playVoiceLine != null)
            {
                harmony.Patch(
                    playVoiceLine,
                    prefix: new HarmonyMethod(typeof(SAINPatch).GetMethod(nameof(GuardSainPlayerVoiceLineForFollower), BindingFlags.NonPublic | BindingFlags.Static)));
            }
        }

        [HarmonyPrefix]
        private static bool BypassSainTalkPatchForFollower(object[] __args, ref bool __result)
        {
            try
            {
                if (__args == null || __args.Length == 0) return true;

                BotOwner? botOwner = null;

                if (__args[0] is Player player)
                {
                    if (!player.IsAI) return true;
                    botOwner = player.AIData?.BotOwner;
                }
                else if (__args[0] is BotTalk botTalk)
                {
                    botOwner = botTalk.BotOwner_0;
                }

                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // Skip SAIN talk-patch logic for followers so EFT/plugin talk behavior can run.
                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool PatchPlayerTalked(EPhraseTrigger phrase, ETagStatus mask, Player player)
        {
            if (phrase == (EPhraseTrigger)CustomPhrases.TeamStatus || phrase == (EPhraseTrigger)CustomPhrases.OverThere)
            {
                return false;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool PatchEnemyConvesation(EPhraseTrigger trigger, ETagStatus status, Player player)
        {
            return PatchPlayerTalked(trigger, status, player);
        }

        [HarmonyPrefix]
        private static bool GuardSainPlayerVoiceLineForFollower(object __instance, EPhraseTrigger phrase, ETagStatus mask, bool aggressive, ref bool __result)
        {
            if (!friendlySAIN.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                Player? player = sainPlayerComponentPlayerProperty?.GetValue(__instance) as Player;
                BotOwner? botOwner = player?.AIData?.BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                if (FollowerForcedPhraseGate.ShouldBlock(botOwner, phrase))
                {
                    __result = false;
                    return false;
                }

                if (FollowerMutedCombatPhraseGate.ShouldBlock(botOwner, phrase))
                {
                    __result = false;
                    return false;
                }

                if (phrase == EPhraseTrigger.OnRepeatedContact && !FollowerContactPhraseGate.ShouldAllow(botOwner))
                {
                    __result = false;
                    return false;
                }

                if (FollowerTalkFrequencyGate.ShouldBlockCombatTalk(botOwner, phrase))
                {
                    __result = false;
                    return false;
                }
            }
            catch
            {
                return true;
            }

            return true;
        }

        [HarmonyPrefix]
        private static bool PatchAssignSquadLeader(object __instance, object sain)
        {
            try
            {
                if (sain == null) return true;

                var botOwnerProp = AccessTools.Property(sain.GetType(), "BotOwner");
                var botOwner = botOwnerProp?.GetValue(sain) as BotOwner;
                if (botOwner == null) return true;

                if (BossPlayers.IsFollower(botOwner))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }
            return true;
        }

        [HarmonyPrefix]
        private static bool DisableSainLayerForFollowersWithoutAddon(object __instance, ref bool __result)
        {
            if (!friendlySAIN.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPostfix]
        private static void ForcePatrolOffForFollowerCombat(object __instance)
        {
            if (!friendlySAIN.ShouldDisableSainForFollowers)
            {
                return;
            }

            try
            {
                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return;
                }

                MovementContext movementContext = botOwner.GetPlayer?.MovementContext;
                if (movementContext == null || !movementContext.IsInPatrol)
                {
                    return;
                }

                if (FollowerCombatLayer.IsFollowerCombatLayerActive(botOwner) || botOwner.Memory?.HaveEnemy == true)
                {
                    movementContext.SetPatrol(false);
                }
            }
            catch
            {
            }
        }

        [HarmonyPrefix]
        private static bool BlockFollowerSainReloadIfAddonMissing(BotOwner botOwner)
        {
            if (!friendlySAIN.ShouldDisableSainForFollowers)
            {
                return true;
            }

            try
            {
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                // SAIN self-action reload can call into BotReload.CanReload(), which in turn
                // can trigger a forced switch back to the main weapon. Keep the rest of SAIN's
                // decision tick alive so enemy acquisition and other passive bookkeeping still run.
                return false;
            }
            catch
            {
                return true;
            }
        }

        [HarmonyPrefix]
        private static bool GuardFollowerSainWeaponSelection(object __instance, EquipmentSlot slot)
        {
            if (!friendlySAIN.IsSAINInstalled)
            {
                return true;
            }

            try
            {
                if (slot != EquipmentSlot.FirstPrimaryWeapon)
                {
                    return true;
                }

                BotOwner? botOwner = AccessTools.Property(__instance.GetType(), "BotOwner")?.GetValue(__instance) as BotOwner;
                if (botOwner == null || !BossPlayers.IsFollower(botOwner))
                {
                    return true;
                }

                var selector = botOwner.WeaponManager?.Selector;
                if (selector == null || selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon)
                {
                    return true;
                }

                EnemyInfo? goalEnemy = botOwner.Memory?.GoalEnemy;
                if (goalEnemy == null)
                {
                    return true;
                }

                if (ShouldHoldFollowerSecondary(goalEnemy))
                {
                    return false;
                }
            }
            catch
            {
                return true;
            }

            return true;
        }

        private static bool ShouldHoldFollowerSecondary(EnemyInfo goalEnemy)
        {
            float closeQuarterDistance = CombatDistanceConfiguration.Instance.GetCloseQuarterDistance();
            if (goalEnemy.Distance <= closeQuarterDistance)
            {
                return true;
            }

            if (goalEnemy.IsVisible && goalEnemy.CanShoot)
            {
                return true;
            }

            return Time.time - goalEnemy.PersonalSeenTime <= FollowerCloseIntentRecentSeenSeconds;
        }
    }
}
