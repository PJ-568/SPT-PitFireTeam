using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;

namespace friendlySAIN.BigBrain
{
    internal sealed class FollowerRequestLayer : CustomLayer
    {
        private const bool EnableRequestLayerDebug = false;
        private BotFollowerPlayer? followerData;

        public FollowerRequestLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
        }

        public override string GetName()
        {
            return "friendlySAIN.FollowerRequest";
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;

            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (followerData == null)
            {
                return false;
            }

            bool hasCommand = followerData.TryGetActiveCommand(out _, out _);

            return hasCommand;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(GestureCommandAction), "GestureCommand");
        }

        public override bool IsCurrentActionEnding()
        {
            if (!IsActive()) return true;

            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            return followerData == null || !followerData.TryGetActiveCommand(out _, out _);
        }

    }
}
