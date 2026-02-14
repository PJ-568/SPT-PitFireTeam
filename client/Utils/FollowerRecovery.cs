using EFT;
using HarmonyLib;
using System;

namespace friendlySAIN.Utils
{
    internal static class FollowerRecovery
    {
        public static void SoftReset(BotOwner bot)
        {
            if (bot == null || bot.IsDead || bot.BotState != EBotState.Active) return;

            bot.Mover.Pause = false;
            bot.PatrollingData?.Pause();

            if (bot.BotRequestController?.CurRequest != null)
            {
                bot.BotRequestController.CurRequest.Complete();
                bot.BotRequestController.CurRequest = null;
            }

            BaseBrain baseBrain = bot.Brain?.BaseBrain;
            if (baseBrain == null) return;

            if (baseBrain.CurLayerInfo is BaseLogicLayerSimpleAbstractClass simpleLayer)
            {
                simpleLayer.CalcActionNextFrame(null);
            }
            else if (baseBrain.CurLayerInfo is BaseLogicLayerAbstractClass baseLayer)
            {
                baseLayer.Bool_1 = true;
            }

            baseBrain.CalcActionNextFrame();
        }

    }
}
