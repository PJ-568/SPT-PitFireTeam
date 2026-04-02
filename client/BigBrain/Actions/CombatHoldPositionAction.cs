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

        private EnemyInfo? currentEnemy;
        private LookTargetMode currentLookMode;
        private Vector3 currentLookPoint;
        private Vector3 currentEnemyDirection;
        private int currentCornerSide;
        private int currentCoverPointId = -1;
        private bool currentEnemyVisible;

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

            return (enemyLookPoint - currentLookPoint).sqrMagnitude <= SignificantEnemyMoveSqr;
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

            int preferredSide = GetPreferredCornerSide(coverPoint, enemyDirection);
            if (preferredSide == currentCornerSide)
            {
                currentEnemyDirection = enemyDirection;
                return true;
            }

            return Vector3.Angle(currentEnemyDirection, enemyDirection) < SignificantCornerSwitchAngle;
        }

        private bool TryAcquireNewLook(EnemyInfo enemy)
        {
            Vector3 enemyLookPoint = GetEnemyLookPoint(enemy);
            if (enemyLookPoint == Vector3.zero)
            {
                return false;
            }

            if (enemy.IsVisible)
            {
                SetPointLook(enemy, enemyLookPoint, true);
                return true;
            }

            CustomNavigationPoint coverPoint = BotOwner_0.Memory.CurCustomCoverPoint;
            if (coverPoint != null && TryGetEnemyDirection(enemy, coverPoint, out Vector3 enemyDirection))
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

            SetPointLook(enemy, enemyLookPoint, false);
            return true;
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

            currentEnemy = enemy;
            currentLookMode = LookTargetMode.Corner;
            currentEnemyDirection = enemyDirection;
            currentCornerSide = side;
            currentCoverPointId = coverPoint.Id;
            currentEnemyVisible = false;
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
        }

        private void ApplyCurrentLook()
        {
            if (currentLookMode == LookTargetMode.Corner)
            {
                CustomNavigationPoint coverPoint = BotOwner_0.Memory.CurCustomCoverPoint;
                if (coverPoint != null && coverPoint.Id == currentCoverPointId && IsCornerUsable(coverPoint, currentCornerSide))
                {
                    BotOwner_0.Steering.LookToDirection(BotOwner_0.LookData.RotateWallBySide(coverPoint, currentCornerSide));
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
            currentCornerSide = 0;
            currentCoverPointId = -1;
            currentEnemyVisible = false;
        }

        private Vector3 GetEnemyLookPoint(EnemyInfo enemy)
        {
            if (enemy.IsVisible)
            {
                return enemy.GetBodyPartPosition();
            }

            return enemy.EnemyLastPositionReal + Vector3.up * 0.8f;
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

        private static bool IsCornerUsable(CustomNavigationPoint coverPoint, int side)
        {
            return side > 0 ? coverPoint.CanLookLeft : coverPoint.CanLookRight;
        }

        private enum LookTargetMode
        {
            None,
            Point,
            Corner
        }
    }
}
