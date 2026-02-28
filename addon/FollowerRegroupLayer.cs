using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using SAIN.Layers;
using SAIN.Models.Enums;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINRegroupLayer : SAINLayer
    {
        private const bool EnableRegroupDebugLogs = false;
        public static readonly string Name = BuildLayerName("friendlySAIN Regroup");

        private BotFollowerPlayer? _followerData;
        private bool? _lastActiveState;

        public SAINRegroupLayer(BotOwner botOwner, int priority)
            : base(botOwner, priority, Name, ESAINLayer.Squad)
        {
        }

        public override bool IsActive()
        {
            bool active = false;

            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return FinishActive(false);
            }

            if (!BotOwner.BotFollower.HaveBoss || BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer)
            {
                return FinishActive(false);
            }

            _followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            if (_followerData == null)
            {
                return FinishActive(false);
            }

            active = friendlySAIN.ShouldSainRegroupLayerHandle(BotOwner) &&
                _followerData.TryGetActiveCommand(out FollowerCommandType command, out _)
                && command == FollowerCommandType.RegroupNearBoss;

            return FinishActive(active);
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(SAINRegroupAction), "friendlySAIN Regroup");
        }

        public override bool IsCurrentActionEnding()
        {
            return !IsActive();
        }

        private bool FinishActive(bool active)
        {
            CheckActiveChanged(active);
            return TrackActive(active);
        }

        private bool TrackActive(bool active)
        {
            if (BotOwner?.BotFollower == null || !BotOwner.BotFollower.HaveBoss || BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer)
            {
                return active;
            }

            if (EnableRegroupDebugLogs && _lastActiveState != active)
            {
                _lastActiveState = active;
            }
            else if (!EnableRegroupDebugLogs)
            {
                _lastActiveState = active;
            }

            return active;
        }
    }
}
