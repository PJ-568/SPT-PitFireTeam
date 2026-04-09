using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.BigBrain.Actions;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using friendlySAIN.Utils;

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

        public override void Start()
        {
            base.Start();

            if (BotOwner?.Mover != null)
            {
                BotOwner.Mover.Pause = false;
                if (BotOwner.Mover.Sprinting)
                {
                    BotOwner.Mover.Sprint(false, false);
                }
            }

            if (BotOwner?.Mover?.TargetPose < 0.85f)
            {
                BotOwner.SetPose(1f);
            }

            BotOwner?.PatrollingData?.Pause();

            if (BotOwner?.BotRequestController?.CurRequest != null)
            {
                BotOwner.BotRequestController.CurRequest.Complete();
                BotOwner.BotRequestController.CurRequest = null;
            }

            if (BotOwner != null)
            {
                FollowerRecovery.SoftReset(BotOwner);
            }
        }

        public override bool IsActive()
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active || BotOwner.GetPlayer == null || !BotOwner.GetPlayer.HealthController.IsAlive)
            {
                return false;
            }

            if (!BotOwner.BotFollower.HaveBoss) return false;
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer) return false;

            if (followerData == null) followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);

            if (followerData == null)
            {
                return false;
            }

            bool hasCommand = followerData.TryGetActiveCommand(out FollowerCommandType command, out _);
            if (hasCommand && command == FollowerCommandType.PushEnemy)
            {
                return false;
            }

            bool allowVanillaCombatRegroup = hasCommand &&
                                            command == FollowerCommandType.RegroupNearBoss &&
                                            !friendlySAIN.UseSainFollowerCombat;

            if (!allowVanillaCombatRegroup && !followerData.IsReadyForPatrolAfterCombat())
            {
                return false;
            }

            if (hasCommand && followerData.HasKnownEnemy())
            {
                if (command == FollowerCommandType.RegroupNearBoss)
                {
                    // let sain continue the regroup on entering combat
                    if (friendlySAIN.ShouldSainRegroupLayerHandle(BotOwner))
                    {
                        BotOwner.StopMove();
                        return false;
                    }

                    if (allowVanillaCombatRegroup)
                    {
                        return true;
                    }
                }

                if (command == FollowerCommandType.PushEnemy)
                {
                    return false;
                }

                InteractableObjects.RemoveTaker(BotOwner);
                InteractableObjects.RemoveOpener(BotOwner);
                followerData.ClearCommand("KnownEnemyAcquired");
                return false;
            }


            return hasCommand;
        }

        public override Action GetNextAction()
        {
            return new Action(typeof(GestureCommandAction), "GestureCommand");
        }

        public override bool IsCurrentActionEnding()
        {
            return !IsActive();
        }

    }
}
