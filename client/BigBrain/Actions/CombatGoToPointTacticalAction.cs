using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Utils;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatGoToPointTacticalAction : FollowerCombatActionBase
    {
        private readonly FollowerGoToPointTacticalNode baseLogic;

        public CombatGoToPointTacticalAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new FollowerGoToPointTacticalNode(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass30>(data));
        }

        private sealed class FollowerGoToPointTacticalNode : GClass239
        {
            public FollowerGoToPointTacticalNode(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void LookSimple()
            {
                Vector3 destination = MainTargetPosition;
                Vector3 botPosition = BotOwner_0.GetPlayer.Transform.position;
                Vector3 currentCorner = BotOwner_0.Mover.CurrentCornerPoint;

                if (Covers.IsPointBetween(currentCorner, botPosition, destination))
                {
                    BotObserveDataClass.SetVectorToLook(currentCorner - BotOwner_0.Position);
                }
                else
                {
                    BotObserveDataClass.SetVectorToLook(destination - botPosition);
                }

                BotObserveDataClass.Update();
            }
        }
    }
}
