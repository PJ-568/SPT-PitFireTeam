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
            private const float CornerAheadDotThreshold = 0.05f;
            private const float CornerPathProximityMeters = 2.5f;
            private const float MaxCornerThreatAngleDegrees = 35f;
            private const float ThreatLookIntervalMin = 2f;
            private const float ThreatLookIntervalMax = 3f;

            private float nextThreatLookTime;

            public FollowerGoToPointTacticalNode(BotOwner botOwner) : base(botOwner)
            {
            }

            public override void UpdateNodeByBrain(GClass26 data)
            {
                EnemyInfo? goalEnemy = BotOwner_0.Memory?.GoalEnemy;
                if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
                {
                    BotOwner_0.BotAttackManager.UpdateNextTick();
                }
                else if (goalEnemy != null && nextThreatLookTime < Time.time)
                {
                    nextThreatLookTime = Time.time + GClass856.Random(ThreatLookIntervalMin, ThreatLookIntervalMax);
                    BotOwner_0.Steering.LookToPoint(goalEnemy.EnemyLastPosition + new Vector3(0f, 0.6f, 0f));
                }

                base.UpdateNodeByBrain(data);
            }

            public override void LookSimple()
            {

                Vector3 destination = MainTargetPosition;
                Vector3 botPosition = BotOwner_0.GetPlayer.Transform.position;
                Vector3 lookOrigin = BotOwner_0.WeaponRoot != null
                    ? BotOwner_0.WeaponRoot.position
                    : botPosition;
                Vector3 currentCorner = BotOwner_0.Mover.CurrentCornerPoint;
                Vector3 destinationLook = destination - lookOrigin;
                bool hasThreatLook = TryGetThreatLookDirection(lookOrigin, out Vector3 threatLook);

                if (ShouldUseCornerLook(currentCorner, lookOrigin, destination))
                {
                    Vector3 cornerLook = currentCorner - lookOrigin;
                    if (hasThreatLook && !IsCornerLookAlignedWithThreat(cornerLook, threatLook))
                    {
                        BotObserveDataClass.SetVectorToLook(threatLook);
                    }
                    else
                    {
                        BotObserveDataClass.SetVectorToLook(cornerLook);
                    }
                }
                else
                {
                    BotObserveDataClass.SetVectorToLook(hasThreatLook ? threatLook : destinationLook);
                }

                BotObserveDataClass.Update();
            }

            private static bool ShouldUseCornerLook(Vector3 corner, Vector3 origin, Vector3 destination)
            {
                Vector3 toDestination = destination - origin;
                toDestination.y = 0f;

                Vector3 toCorner = corner - origin;
                toCorner.y = 0f;

                if (toDestination.sqrMagnitude < 0.0001f || toCorner.sqrMagnitude < 0.0001f)
                {
                    return false;
                }

                Vector3 destDir = toDestination.normalized;
                float forwardDot = Vector3.Dot(toCorner.normalized, destDir);
                if (forwardDot <= CornerAheadDotThreshold)
                {
                    return false;
                }

                // If corner is farther than destination in the move direction, it is not an active steering corner.
                if (toCorner.sqrMagnitude >= toDestination.sqrMagnitude)
                {
                    return false;
                }

                float cornerToPath = DistancePointToSegmentXZ(corner, origin, destination);
                return cornerToPath <= CornerPathProximityMeters;
            }

            private bool TryGetThreatLookDirection(Vector3 botPosition, out Vector3 threatLook)
            {
                threatLook = Vector3.zero;

                EnemyInfo? enemy = BotOwner_0.Memory?.GoalEnemy;
                if (enemy == null)
                {
                    return false;
                }

                Vector3 enemyPos = enemy.CurrPosition;
                if (!IsFinite(enemyPos))
                {
                    enemyPos = enemy.EnemyLastPositionReal;
                }

                if (!IsFinite(enemyPos))
                {
                    return false;
                }

                Vector3 dir = enemyPos - botPosition;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                {
                    return false;
                }

                threatLook = dir;
                return true;
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

            private static bool IsFinite(Vector3 value)
            {
                return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                       !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                       !float.IsNaN(value.z) && !float.IsInfinity(value.z);
            }

            private static float DistancePointToSegmentXZ(Vector3 point, Vector3 segmentStart, Vector3 segmentEnd)
            {
                Vector2 p = new Vector2(point.x, point.z);
                Vector2 a = new Vector2(segmentStart.x, segmentStart.z);
                Vector2 b = new Vector2(segmentEnd.x, segmentEnd.z);

                Vector2 ab = b - a;
                float abLenSqr = ab.sqrMagnitude;
                if (abLenSqr <= 0.0001f)
                {
                    return Vector2.Distance(p, a);
                }

                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSqr);
                Vector2 closest = a + ab * t;
                return Vector2.Distance(p, closest);
            }
        }
    }
}
