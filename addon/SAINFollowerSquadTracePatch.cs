using EFT;
using HarmonyLib;
using SAIN.BotController.Classes;
using SAIN.SAINComponent.Classes.Info;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINFollowerSquadTracePatch
    {
        private const bool EnableTraceLogs = true;

        public static void Apply(Harmony harmony)
        {
            if (!EnableTraceLogs)
            {
                return;
            }

            PatchMethod(
                harmony,
                AccessTools.Method(typeof(BossPlayers), nameof(BossPlayers.AddFollower)),
                nameof(Prefix_BossPlayersAddFollower),
                nameof(Postfix_BossPlayersAddFollower),
                "BossPlayers.AddFollower");

            PatchMethod(
                harmony,
                AccessTools.Method(typeof(BotSquadContainer), nameof(BotSquadContainer.RemoveFromSquad)),
                nameof(Prefix_RemoveFromSquad),
                nameof(Postfix_RemoveFromSquad),
                "BotSquadContainer.RemoveFromSquad");

            PatchMethod(
                harmony,
                AccessTools.Method(typeof(Squad), nameof(Squad.RemoveMember), new[] { typeof(string) }),
                nameof(Prefix_SquadRemoveMemberById),
                nameof(Postfix_SquadRemoveMemberById),
                "Squad.RemoveMember(string)");

            PatchMethod(
                harmony,
                AccessTools.Method(typeof(Squad), "removeMember", new[] { typeof(BotOwner) }),
                nameof(Prefix_SquadRemoveMemberByOwner),
                nameof(Postfix_SquadRemoveMemberByOwner),
                "Squad.removeMember(BotOwner)");
        }

        private static void PatchMethod(Harmony harmony, MethodBase? target, string? prefixName, string? postfixName, string label)
        {
            if (target == null)
            {
                Modules.Logger.LogError($"[Init] Failed to find {label} for SAIN squad trace patch.");
                return;
            }

            HarmonyMethod? prefix = prefixName != null ? new HarmonyMethod(typeof(SAINFollowerSquadTracePatch), prefixName) : null;
            HarmonyMethod? postfix = postfixName != null ? new HarmonyMethod(typeof(SAINFollowerSquadTracePatch), postfixName) : null;
            harmony.Patch(target, prefix: prefix, postfix: postfix);
            Modules.Logger.LogInfo($"[Init] SAIN squad trace patch applied for {label}.");
        }

        private static void Prefix_BossPlayersAddFollower(BotOwner bot, pitAIBossPlayer player)
        {
            Modules.Logger.LogInfo(
                $"[SAIN Trace] AddFollower:start bot={FmtBot(bot)} boss={FmtBoss(player)} " +
                $"group={bot?.BotsGroup?.Id.ToString() ?? "<null>"} followerBefore={BossPlayers.IsFollower(bot)}");
        }

        private static void Postfix_BossPlayersAddFollower(BotOwner bot, pitAIBossPlayer player, BotFollowerPlayer __result)
        {
            Modules.Logger.LogInfo(
                $"[SAIN Trace] AddFollower:end bot={FmtBot(bot)} boss={FmtBoss(player)} " +
                $"group={bot?.BotsGroup?.Id.ToString() ?? "<null>"} followerAfter={BossPlayers.IsFollower(bot)} " +
                $"result={( __result != null ? __result.GetType().Name : "<null>")}");
        }

        private static void Prefix_RemoveFromSquad(BotSquadContainer __instance)
        {
            Modules.Logger.LogInfo(
                $"[SAIN Trace] RemoveFromSquad:start bot={FmtBot(__instance?.BotOwner)} squad={FmtSquad(__instance?.SquadInfo)}");
        }

        private static void Postfix_RemoveFromSquad(BotSquadContainer __instance)
        {
            Modules.Logger.LogInfo(
                $"[SAIN Trace] RemoveFromSquad:end bot={FmtBot(__instance?.BotOwner)} squadNow={FmtSquad(__instance?.SquadInfo)}");
        }

        private static void Prefix_SquadRemoveMemberById(Squad __instance, string id, out int __state)
        {
            __state = __instance?.Members?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[SAIN Trace] Squad.RemoveMember(id):start id={id ?? "<null>"} squad={FmtSquad(__instance)} " +
                $"membersBefore={__state}");
        }

        private static void Postfix_SquadRemoveMemberById(Squad __instance, string id, int __state)
        {
            int membersAfter = __instance?.Members?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[SAIN Trace] Squad.RemoveMember(id):end id={id ?? "<null>"} squad={FmtSquad(__instance)} " +
                $"membersBefore={__state} membersAfter={membersAfter}");
        }

        private static void Prefix_SquadRemoveMemberByOwner(Squad __instance, BotOwner botOwner, out int __state)
        {
            __state = __instance?.Members?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[SAIN Trace] Squad.removeMember(owner):start bot={FmtBot(botOwner)} squad={FmtSquad(__instance)} " +
                $"membersBefore={__state} containsBefore={ContainsMember(__instance, botOwner?.ProfileId)}");
        }

        private static void Postfix_SquadRemoveMemberByOwner(Squad __instance, BotOwner botOwner, int __state)
        {
            int membersAfter = __instance?.Members?.Count ?? -1;
            Modules.Logger.LogInfo(
                $"[SAIN Trace] Squad.removeMember(owner):end bot={FmtBot(botOwner)} squad={FmtSquad(__instance)} " +
                $"membersBefore={__state} membersAfter={membersAfter} containsAfter={ContainsMember(__instance, botOwner?.ProfileId)}");
        }

        private static bool ContainsMember(Squad? squad, string? profileId)
        {
            return squad?.Members != null && !string.IsNullOrEmpty(profileId) && squad.Members.ContainsKey(profileId);
        }

        private static string FmtBot(BotOwner? bot)
        {
            if (bot == null)
            {
                return "<null>";
            }

            string name = bot.Profile?.Nickname ?? bot.name ?? "<unknown>";
            string id = bot.ProfileId ?? "<null>";
            string role = bot.Profile?.Info?.Settings?.Role.ToString() ?? "<null>";
            return $"{name}[{id}] role={role}";
        }

        private static string FmtBoss(pitAIBossPlayer? boss)
        {
            Player? player = boss?.realPlayer;
            if (player == null)
            {
                return "<null>";
            }

            return $"{player.Profile?.Nickname ?? "<unknown>"}[{player.ProfileId ?? "<null>"}]";
        }

        private static string FmtSquad(Squad? squad)
        {
            if (squad == null)
            {
                return "<null>";
            }

            string id = !string.IsNullOrEmpty(squad.Id) ? squad.Id : squad.GUID;
            string leader = squad.LeaderId ?? "<null>";
            int members = squad.Members?.Count ?? -1;
            return $"{id} leader={leader} members={members}";
        }
    }
}
