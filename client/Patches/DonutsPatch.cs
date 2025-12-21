using EFT;
using friendlySAIN.Modules;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class DonutsPatch
    {
        private static Type BotDespawnService = null;
        private static Type RegisterBotEvent = null;

        public static bool IsDonutsInstalled()
        {
            return AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Donuts");
        }

        public static void PatchDonutsIfInstalled(Harmony harmony)
        {
            if (!IsDonutsInstalled())
            {
                return;
            }

            if (BotDespawnService == null)
            {
                BotDespawnService = Type.GetType("Donuts.Spawning.Services.BotDespawnService, Donuts");
            }

            if (RegisterBotEvent == null)
            {
                RegisterBotEvent = Type.GetType("Donuts.Spawning.RegisterBotEvent, Donuts");
            }

            if (BotDespawnService != null)
            {
                var originalMethod = AccessTools.Method(BotDespawnService, "RegisterBot");
                var prefixMethod = typeof(DonutsPatch).GetMethod(nameof(RegisterBotPatch), BindingFlags.NonPublic | BindingFlags.Static);

                if (originalMethod != null && prefixMethod != null)
                {
                    harmony.Patch(originalMethod, new HarmonyMethod(prefixMethod));
                }
            }

            if (BotDespawnService != null && RegisterBotEvent != null)
            {
                Logger.LogInfo("Donuts Patched");
            }
        }

        [HarmonyPrefix]
        private static bool RegisterBotPatch(object data)
        {
            if (RegisterBotEvent == null)
            {
                return true;
            }

            FieldInfo botProperty = RegisterBotEvent.GetField("bot");

            if (botProperty == null)
            {
                return true;
            }

            BotOwner bot = botProperty.GetValue(data) as BotOwner;

            if (bot == null)
            {
                return true;
            }

            if (BossPlayers.IsFollower(bot))
            {
                return false;
            }

            return true;
        }
    }
}

