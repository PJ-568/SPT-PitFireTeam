using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    /// <summary>
    /// Search action for enemy-last-known areas. It wraps EFT search behavior but keeps follower
    /// movement/look state stable so a search does not immediately collapse back into passive hold.
    /// </summary>
    internal sealed class CombatSearchAction : FollowerCombatActionBase
    {
        private readonly GClass235 baseLogic;

        private const float MaxCornerThreatAngleDegrees = 35f;

        public CombatSearchAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass235(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetRawData(data));
            EnsureSearchMove();
            LookSimple();
        }

        private void EnsureSearchMove()
        {
            BotSearchPoint? searchPoint = BotOwner.SearchData?.SearchPoint;
            if (searchPoint == null)
            {
                return;
            }

            Vector3 toSearchPoint = searchPoint.Position - BotOwner.Position;
            if (toSearchPoint.y < BotOwner.Settings.FileSettings.Move.Y_APPROXIMATION)
            {
                toSearchPoint.y = 0f;
            }

            if (toSearchPoint.sqrMagnitude < 4f || BotOwner.HasPathAndNotComplete)
            {
                return;
            }

            BotOwner.Mover.Sprint(false, true);
            BotOwner.SetTargetMoveSpeed(1f);
            NavMeshPathStatus status = BotOwner.GoToPoint(searchPoint.Position, false, -1f, true, false, true, false, false);
            BotOwner.SearchData.IsReachableLast = status == NavMeshPathStatus.PathComplete;
        }

        public void LookSimple()
        {
            Vector3 dest = BotOwner.Memory.HaveEnemy ? BotOwner.Memory.GoalEnemy.CurrPosition : BotOwner.Position;
            if (dest != null)
            {
                Vector3 botPos = BotOwner.GetPlayer.Transform.position;
                Vector3 corner = BotOwner.Mover.CurrentCornerPoint;

                if (Utils.Covers.IsPointBetween(corner, botPos, dest))
                {
                    Vector3 cornerDirection = corner - botPos;
                    if (IsCornerLookAlignedWithThreat(cornerDirection, dest - botPos))
                    {
                        baseLogic.BotObserveDataClass.SetVectorToLook(cornerDirection);
                    }
                }
                else
                {
                    baseLogic.BotObserveDataClass.SetVectorToLook(dest - botPos);
                }
            }
            baseLogic.BotObserveDataClass.Update();
        }

        private static bool IsCornerLookAlignedWithThreat(Vector3 cornerDirection, Vector3 threatDirection)
        {
            Vector3 look = cornerDirection;
            look.y = 0f;
            Vector3 threat = threatDirection;
            threat.y = 0f;

            if (look.sqrMagnitude < 0.0001f || threat.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            look.Normalize();
            threat.Normalize();
            return Vector3.Angle(look, threat) <= MaxCornerThreatAngleDegrees;
        }
    }
}
