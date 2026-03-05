using EFT;
using HarmonyLib;
using friendlySAIN.Modules;
using System;
using System.Reflection;

namespace friendlySAIN.SAINAddon
{
    internal static class SAINCalcGoalPatch
    {
        public static event Action<BotOwner>? OnCalcGoal;

        public static void Apply(Harmony harmony)
        {
            MethodInfo? target = AccessTools.Method(typeof(BotsGroup), "CalcGoalForBot", new[] { typeof(BotOwner) });
            if (target == null)
            {
                Modules.Logger.LogError("[Init] Failed to find BotsGroup.CalcGoalForBot for SAIN calc-goal patch.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SAINCalcGoalPatch), nameof(Postfix_CalcGoalForBot)));
            Modules.Logger.LogInfo("[Init] SAIN calc-goal patch applied.");
        }

        private static void Postfix_CalcGoalForBot(BotsGroup __instance, BotOwner bot)
        {
            if (bot == null)
            {
                return;
            }

            try
            {
                OnCalcGoal?.Invoke(bot);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError("[SAIN CalcGoal] OnCalcGoal callback error.");
                Modules.Logger.LogError(ex);
            }
        }
    }
}
