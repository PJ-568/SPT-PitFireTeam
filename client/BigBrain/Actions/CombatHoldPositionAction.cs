using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatHoldPositionAction : FollowerCombatActionBase
    {
        private readonly EnemyFacingHoldLogic baseLogic;

        public CombatHoldPositionAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new EnemyFacingHoldLogic(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            baseLogic.UpdateNodeByBrain(GetData<GClass28>(data));
        }
    }

    internal sealed class EnemyFacingHoldLogic : GClass278
    {
        private const float SignificantEnemyMoveSqr = 1f;
        private const float SignificantCornerSwitchAngle = 20f;
        private const float CornerSwitchLockDuration = 0.45f;
        private const float CornerSwitchPersistDuration = 0.2f;
        private const float CornerSwitchScoreMargin = 0.18f;
        private const float MinCornerAlignmentScore = 0.05f;
        private const float WallFacingProbeDistance = 1.25f;
        private const float PointLookLockDuration = 0.75f;
        private const float PointLookSwitchPersistDuration = 0.2f;
        private const float SignificantPointLookMoveSqr = 9f;
        private const float SignificantPointLookAngle = 18f;

        private EnemyInfo? currentEnemy;
        private LookTargetMode currentLookMode;
        private Vector3 currentLookPoint;
        private Vector3 currentEnemyDirection;
        private Vector3 currentCornerLookDirection;
        private int currentCornerSide;
        private int currentCoverPointId = -1;
        private bool currentEnemyVisible;
        private float currentCornerAssignedAt;
        private int pendingSwitchSide;
        private float pendingSwitchSince;
        private float currentPointAssignedAt;
        private Vector3 pendingPointLookPoint;
        private float pendingPointSwitchSince;

        public EnemyFacingHoldLogic(BotOwner botOwner) : base(botOwner)
        {
        }

        public override void Look()
        {
            if (TryLookTowardEnemy())
            {
                return;
            }

            if (BotOwner_0.NeutralsCheskData.ClosestsVisible != null && BotOwner_0.NeutralsCheskData.IsInPeriod())
            {
                BotOwner_0.Steering.LookToPoint(BotOwner_0.NeutralsCheskData.ClosestsVisible.Position + Vector3.up);
                return;
            }

            base.Look();
        }

        private bool TryLookTowardEnemy()
        {
            EnemyInfo enemy = BotOwner_0.Memory.GoalEnemy ?? BotOwner_0.Memory.LastEnemy;
            if (enemy == null)
            {
                ClearCurrentLook();
                return false;
            }

            if (CanKeepCurrentLook(enemy))
            {
                ApplyCurrentLook();
                return true;
            }

            if (TryAcquireNewLook(enemy))
            {
                ApplyCurrentLook();
                return true;
            }

            ClearCurrentLook();
            return false;
        }

        private bool CanKeepCurrentLook(EnemyInfo enemy)
        {
            if (!ReferenceEquals(currentEnemy, enemy))
            {
                return false;
            }

            if (currentLookMode == LookTargetMode.Corner)
            {
                return CanKeepCornerLook(enemy);
            }

            return CanKeepPointLook(enemy);
        }

        private bool CanKeepPointLook(EnemyInfo enemy)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            if (enemyLookPoint == Vector3.zero)
            {
                return false;
            }

            if (enemy.IsVisible != currentEnemyVisible)
            {
                return false;
            }

            if (enemy.IsVisible)
            {
                pendingPointLookPoint = Vector3.zero;
                pendingPointSwitchSince = 0f;
                return (enemyLookPoint - currentLookPoint).sqrMagnitude <= SignificantEnemyMoveSqr;
            }

            if (Time.time - currentPointAssignedAt < PointLookLockDuration)
            {
                return true;
            }

            if (!ShouldSwitchPointLook(enemyLookPoint))
            {
                return true;
            }

            return false;
        }

        private bool ShouldSwitchPointLook(Vector3 enemyLookPoint)
        {
            Vector3 currentDirection = currentLookPoint - BotOwner_0.Position;
            currentDirection.y = 0f;
            Vector3 nextDirection = enemyLookPoint - BotOwner_0.Position;
            nextDirection.y = 0f;

            if (currentDirection.sqrMagnitude <= 0.001f || nextDirection.sqrMagnitude <= 0.001f)
            {
                pendingPointLookPoint = Vector3.zero;
                pendingPointSwitchSince = 0f;
                return false;
            }

            float moveDeltaSqr = (enemyLookPoint - currentLookPoint).sqrMagnitude;
            float angleDelta = Vector3.Angle(currentDirection, nextDirection);
            if (moveDeltaSqr <= SignificantPointLookMoveSqr && angleDelta <= SignificantPointLookAngle)
            {
                pendingPointLookPoint = Vector3.zero;
                pendingPointSwitchSince = 0f;
                return false;
            }

            if ((pendingPointLookPoint - enemyLookPoint).sqrMagnitude > 0.25f)
            {
                pendingPointLookPoint = enemyLookPoint;
                pendingPointSwitchSince = Time.time;
                return false;
            }

            return Time.time - pendingPointSwitchSince >= PointLookSwitchPersistDuration;
        }

        private bool CanKeepCornerLook(EnemyInfo enemy)
        {
            if (enemy.IsVisible)
            {
                return false;
            }

            CustomNavigationPoint coverPoint = BotOwner_0.Memory.CurCustomCoverPoint;
            if (coverPoint == null || coverPoint.Id != currentCoverPointId)
            {
                return false;
            }

            if (!IsCornerUsable(coverPoint, currentCornerSide))
            {
                return false;
            }

            if (!TryGetEnemyDirection(enemy, coverPoint, out Vector3 enemyDirection))
            {
                return false;
            }

            if (GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide) < MinCornerAlignmentScore)
            {
                return false;
            }

            int preferredSide = GetPreferredCornerSide(coverPoint, enemyDirection);
            if (preferredSide == currentCornerSide)
            {
                currentEnemyDirection = enemyDirection;
                pendingSwitchSide = 0;
                return true;
            }

            if (!CanSwitchCornerSide(coverPoint, enemyDirection, preferredSide))
            {
                currentEnemyDirection = enemyDirection;
                return true;
            }

            return Vector3.Angle(currentEnemyDirection, enemyDirection) < SignificantCornerSwitchAngle;
        }

        private bool CanSwitchCornerSide(CustomNavigationPoint coverPoint, Vector3 enemyDirection, int preferredSide)
        {
            if (Time.time - currentCornerAssignedAt < CornerSwitchLockDuration)
            {
                return false;
            }

            float currentScore = GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide);
            float preferredScore = GetCornerSideScore(coverPoint, enemyDirection, preferredSide);
            if (preferredScore <= currentScore + CornerSwitchScoreMargin)
            {
                pendingSwitchSide = 0;
                return false;
            }

            if (pendingSwitchSide != preferredSide)
            {
                pendingSwitchSide = preferredSide;
                pendingSwitchSince = Time.time;
                return false;
            }

            return Time.time - pendingSwitchSince >= CornerSwitchPersistDuration;
        }

        private static float GetCornerSideScore(CustomNavigationPoint coverPoint, Vector3 enemyDirection, int side)
        {
            Vector3 sideDirection;
            if (coverPoint.BordersLightHave)
            {
                sideDirection = side > 0 ? coverPoint.LeftBorderLight : coverPoint.RightBorderLight;
            }
            else
            {
                sideDirection = GClass855.Rotate90(coverPoint.ToWallVector, side > 0 ? GClass855.SideTurn.left : GClass855.SideTurn.right);
            }

            sideDirection.y = 0f;
            if (sideDirection.sqrMagnitude <= 0.001f)
            {
                return -1f;
            }

            sideDirection = GClass855.NormalizeFastSelf(sideDirection);
            return Vector3.Dot(sideDirection, enemyDirection);
        }

        private bool TryAcquireNewLook(EnemyInfo enemy)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            bool hasReliableEnemyLookPoint = enemyLookPoint != Vector3.zero;
            Vector3 fallbackEnemyDirectionPoint = enemy.CurrPosition + Vector3.up * 0.8f;

            if (enemy.IsVisible)
            {
                SetPointLook(enemy, enemyLookPoint, true);
                return true;
            }

            CustomNavigationPoint coverPoint = BotOwner_0.Memory.CurCustomCoverPoint;
            if (coverPoint != null)
            {
                Vector3 cornerTargetPoint = hasReliableEnemyLookPoint ? enemyLookPoint : fallbackEnemyDirectionPoint;
                if (TryGetEnemyDirectionFromPoint(cornerTargetPoint, coverPoint, out Vector3 enemyDirection))
                {
                    int preferredSide = GetPreferredCornerSide(coverPoint, enemyDirection);
                    if (TrySetCornerLook(enemy, coverPoint, enemyDirection, preferredSide))
                    {
                        return true;
                    }

                    if (TrySetCornerLook(enemy, coverPoint, enemyDirection, -preferredSide))
                    {
                        return true;
                    }
                }
            }

            if (hasReliableEnemyLookPoint && !IsFacingWall())
            {
                SetPointLook(enemy, enemyLookPoint, false);
                return true;
            }

            return false;
        }

        private static bool PreferLeftCorner(CustomNavigationPoint coverPoint, Vector3 enemyDirection)
        {
            if (coverPoint.BordersLightHave)
            {
                return Vector3.Angle(coverPoint.LeftBorderLight, enemyDirection) <=
                       Vector3.Angle(coverPoint.RightBorderLight, enemyDirection);
            }

            Vector3 leftDirection = GClass855.Rotate90(coverPoint.ToWallVector, GClass855.SideTurn.left);
            Vector3 rightDirection = GClass855.Rotate90(coverPoint.ToWallVector, GClass855.SideTurn.right);
            return Vector3.Dot(leftDirection, enemyDirection) >= Vector3.Dot(rightDirection, enemyDirection);
        }

        private static int GetPreferredCornerSide(CustomNavigationPoint coverPoint, Vector3 enemyDirection)
        {
            return PreferLeftCorner(coverPoint, enemyDirection) ? 1 : -1;
        }

        private bool TrySetCornerLook(EnemyInfo enemy, CustomNavigationPoint coverPoint, Vector3 enemyDirection, int side)
        {
            if (!IsCornerUsable(coverPoint, side))
            {
                return false;
            }

            if (GetCornerSideScore(coverPoint, enemyDirection, side) < MinCornerAlignmentScore)
            {
                return false;
            }

            currentEnemy = enemy;
            currentLookMode = LookTargetMode.Corner;
            currentEnemyDirection = enemyDirection;
            currentCornerLookDirection = GetCornerLookDirection(coverPoint, side);
            currentCornerSide = side;
            currentCoverPointId = coverPoint.Id;
            currentEnemyVisible = false;
            currentCornerAssignedAt = Time.time;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            return true;
        }

        private void SetPointLook(EnemyInfo enemy, Vector3 enemyLookPoint, bool enemyVisible)
        {
            currentEnemy = enemy;
            currentLookMode = LookTargetMode.Point;
            currentLookPoint = enemyLookPoint;
            currentEnemyVisible = enemyVisible;
            currentCoverPointId = -1;
            currentCornerSide = 0;
            currentEnemyDirection = Vector3.zero;
            currentCornerLookDirection = Vector3.zero;
            currentCornerAssignedAt = 0f;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            currentPointAssignedAt = Time.time;
            pendingPointLookPoint = Vector3.zero;
            pendingPointSwitchSince = 0f;
        }

        private void ApplyCurrentLook()
        {
            if (currentLookMode == LookTargetMode.Corner)
            {
                CustomNavigationPoint coverPoint = BotOwner_0.Memory.CurCustomCoverPoint;
                if (coverPoint != null && coverPoint.Id == currentCoverPointId && IsCornerUsable(coverPoint, currentCornerSide))
                {
                    if (currentEnemy != null && TryGetEnemyDirection(currentEnemy, coverPoint, out Vector3 enemyDirection))
                    {
                        if (GetCornerSideScore(coverPoint, enemyDirection, currentCornerSide) < MinCornerAlignmentScore)
                        {
                            BotOwner_0.Steering.LookToPoint(GetEnemyLookPoint(currentEnemy));
                            return;
                        }
                    }

                    if (currentCornerLookDirection.sqrMagnitude <= 0.001f)
                    {
                        currentCornerLookDirection = GetCornerLookDirection(coverPoint, currentCornerSide);
                    }

                    BotOwner_0.Steering.LookToDirection(currentCornerLookDirection);
                    return;
                }
            }

            if (currentLookMode == LookTargetMode.Point)
            {
                BotOwner_0.Steering.LookToPoint(currentLookPoint);
            }
        }

        private void ClearCurrentLook()
        {
            currentEnemy = null;
            currentLookMode = LookTargetMode.None;
            currentLookPoint = Vector3.zero;
            currentEnemyDirection = Vector3.zero;
            currentCornerLookDirection = Vector3.zero;
            currentCornerSide = 0;
            currentCoverPointId = -1;
            currentEnemyVisible = false;
            currentCornerAssignedAt = 0f;
            pendingSwitchSide = 0;
            pendingSwitchSince = 0f;
            currentPointAssignedAt = 0f;
            pendingPointLookPoint = Vector3.zero;
            pendingPointSwitchSince = 0f;
        }

        private Vector3 GetEnemyLookPoint(EnemyInfo enemy)
        {
            if (enemy.IsVisible)
            {
                return enemy.GetBodyPartPosition();
            }

            Vector3 lastKnownPoint = enemy.EnemyLastPositionReal + Vector3.up * 0.8f;
            if (!IsUsableDirectionPoint(lastKnownPoint, BotOwner_0.Position))
            {
                return Vector3.zero;
            }

            return lastKnownPoint;
        }

        private bool TryGetEnemyDirection(EnemyInfo enemy, CustomNavigationPoint coverPoint, out Vector3 enemyDirection)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            enemyDirection = enemyLookPoint - coverPoint.Position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                enemyDirection = enemyLookPoint - BotOwner_0.Position;
                enemyDirection.y = 0f;
            }

            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            enemyDirection = GClass855.NormalizeFastSelf(enemyDirection);
            return true;
        }

        private bool TryGetEnemyDirectionFromPoint(Vector3 enemyLookPoint, CustomNavigationPoint coverPoint, out Vector3 enemyDirection)
        {
            enemyDirection = enemyLookPoint - coverPoint.Position;
            enemyDirection.y = 0f;
            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                enemyDirection = enemyLookPoint - BotOwner_0.Position;
                enemyDirection.y = 0f;
            }

            if (enemyDirection.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            enemyDirection = GClass855.NormalizeFastSelf(enemyDirection);
            return true;
        }

        private static bool IsUsableDirectionPoint(Vector3 point, Vector3 origin)
        {
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z))
            {
                return false;
            }

            if (float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
            {
                return false;
            }

            return (point - origin).sqrMagnitude > 0.01f;
        }

        private bool IsFacingWall()
        {
            Vector3 origin = BotOwner_0.MyHead != null ? BotOwner_0.MyHead.position : BotOwner_0.Position + Vector3.up * 1.4f;
            Vector3 forward = BotOwner_0.Transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.001f)
            {
                return false;
            }

            forward = GClass855.NormalizeFastSelf(forward);
            return Physics.Raycast(new Ray(origin, forward), WallFacingProbeDistance, LayerMaskClass.HighPolyWithTerrainMask);
        }

        private static bool IsCornerUsable(CustomNavigationPoint coverPoint, int side)
        {
            return side > 0 ? coverPoint.CanLookLeft : coverPoint.CanLookRight;
        }

        private Vector3 GetCornerLookDirection(CustomNavigationPoint coverPoint, int side)
        {
            Vector3 lookDirection = BotOwner_0.LookData.RotateWallBySide(coverPoint, side);
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                return GClass855.NormalizeFastSelf(lookDirection);
            }

            Vector3 fallbackDirection = side > 0 ? coverPoint.LeftBorderLight : coverPoint.RightBorderLight;
            fallbackDirection.y = 0f;
            if (fallbackDirection.sqrMagnitude > 0.001f)
            {
                return GClass855.NormalizeFastSelf(fallbackDirection);
            }

            return Vector3.zero;
        }

        private enum LookTargetMode
        {
            None,
            Point,
            Corner
        }
    }
}
