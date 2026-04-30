using DrakiaXYZ.BigBrain.Brains;
using EFT;
using pitTeam.Utils;
using UnityEngine;
using UnityEngine.AI;

namespace pitTeam.BigBrain.Actions
{
    internal sealed class CombatDogFightAction : FollowerCombatActionBase
    {
        private const float MoveUpdateMinDelay = 0.1f;
        private const float MoveUpdateFallbackDelay = 0.33f;
        private const float RecentSeenThreshold = 1f;
        private const float UnsafeCloseThreatDistance = 8f;
        private const float UnsafeCloseThreatLookAngle = 70f;
        private const float FastFireDistance = 8f;
        private const float FastFireAngle = 35f;

        private readonly GClass178 shootLogic;
        private readonly GClass274 grenadeLogic;
        private readonly NavMeshPath dogFightPath = new NavMeshPath();

        private DogFightMoveStatus moveStatus;
        private float nextMoveUpdateTime;

        public CombatDogFightAction(BotOwner botOwner) : base(botOwner)
        {
            shootLogic = new GClass183(botOwner);
            grenadeLogic = new GClass274(botOwner);
        }

        public override void Start()
        {
            base.Start();
            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = 0f;
        }

        public override void Stop()
        {
            StopCombatShooting();
            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = 0f;

            if (BotOwner?.DogFight != null)
            {
                BotOwner.DogFight.DogFightState = BotDogFightStatus.none;
                BotOwner.DogFight.PursuitInProgress = false;
            }

            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            EnemyInfo goalEnemy = BotOwner.Memory.GoalEnemy;

            BotOwner.Mover.SetTargetMoveSpeed(1f);
            bool unsafeCloseFacing = IsUnsafeCloseThreatFacing(goalEnemy);
            if (goalEnemy != null && goalEnemy.CanShoot && goalEnemy.IsVisible)
            {
                MaintainThreatFacing(goalEnemy, goalEnemy.GetBodyPartPosition(), allowHardTurn: true);
            }

            UpdateSainLikeMovement(goalEnemy);
            BotOwner.SetPose(0.5f);
            BotOwner.Sprint(false, true);

            if (goalEnemy == null || !goalEnemy.CanShoot || !goalEnemy.IsVisible)
            {
                StopCombatShooting();
                BotOwner.LookData.SetLookPointByHearing(null);
                return;
            }

            if (BotOwner.WeaponManager.IsMelee)
            {
                BotOwner.WeaponManager.Selector.ChangeToMain();
                return;
            }

            if (BotOwner.Settings.FileSettings.Grenade.CAN_THROW_FROM_ANY_PLACE && grenadeLogic.UpdateTryThrow())
            {
                return;
            }

            Vector3 shootPoint = shootLogic.GetTarget() ?? goalEnemy.CurrPosition;
            MaintainThreatFacing(goalEnemy, shootPoint, allowHardTurn: true);
            if (unsafeCloseFacing && IsUnsafeCloseThreatFacing(goalEnemy))
            {
                BotOwner.Mover.Stop();
            }

            if (BotOwner.DoorOpener.DogFightHaveNearestDoor())
            {
                BotOwner.Mover.Stop();
            }

            Vector3 fireOrigin = BotOwner.WeaponRoot != null
                ? BotOwner.WeaponRoot.position
                : BotOwner.Position + Vector3.up * 1.2f;
            if (FollowerShotSafety.IsFriendlyInShotLane(BotOwner, fireOrigin, shootPoint))
            {
                StopCombatShooting();
                return;
            }

            if (goalEnemy.Distance <= FastFireDistance &&
                CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) <= FastFireAngle)
            {
                BotOwner.ShootData.Shoot();
            }

            shootLogic.UpdateNodeByBrain(GetData<GClass27>(data));
        }

        private void UpdateSainLikeMovement(EnemyInfo? goalEnemy)
        {
            if (goalEnemy == null)
            {
                moveStatus = DogFightMoveStatus.None;
                return;
            }

            if (BotOwner.GetPlayer?.MovementContext?.IsInPronePose == true)
            {
                BotOwner.SetPose(1f);
            }

            if (goalEnemy.IsVisible && moveStatus == DogFightMoveStatus.MovingToEnemy)
            {
                moveStatus = DogFightMoveStatus.Shooting;
                BotOwner.Mover.Stop();
                nextMoveUpdateTime = Time.time + 0.25f * Random.Range(0.66f, 1.33f);
                return;
            }

            if (nextMoveUpdateTime > Time.time)
            {
                return;
            }

            if (TryBackUpFromEnemy(goalEnemy))
            {
                moveStatus = DogFightMoveStatus.BackingUp;
                float baseTime = goalEnemy.IsVisible ? 0.75f : 1f;
                nextMoveUpdateTime = Time.time + baseTime * Random.Range(0.66f, 1.33f);
                return;
            }

            if (TryMoveTowardEnemy(goalEnemy))
            {
                moveStatus = DogFightMoveStatus.MovingToEnemy;
                nextMoveUpdateTime = Time.time + Mathf.Clamp(0.25f * Random.Range(0.5f, 1.25f), MoveUpdateMinDelay, 0.66f);
                return;
            }

            moveStatus = DogFightMoveStatus.None;
            nextMoveUpdateTime = Time.time + MoveUpdateFallbackDelay;
        }

        private bool TryMoveTowardEnemy(EnemyInfo goalEnemy)
        {
            Vector3 moveTarget = goalEnemy.IsVisible
                ? goalEnemy.CurrPosition
                : goalEnemy.EnemyLastPositionReal;

            if (!NavMesh.SamplePosition(moveTarget, out NavMeshHit navMeshHit, 1.5f, -1))
            {
                return false;
            }

            return BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
        }

        private bool TryBackUpFromEnemy(EnemyInfo goalEnemy)
        {
            Vector3? target = FindBackUpTarget(goalEnemy);
            if (target == null)
            {
                return false;
            }

            Vector3 botPosition = BotOwner.Position;
            Vector3 targetDirection = target.Value - botPosition;
            targetDirection.y = 0f;
            if (targetDirection.sqrMagnitude < 0.01f)
            {
                return false;
            }

            Vector3 away = -targetDirection.normalized;
            Vector3 preferred = botPosition + away * 3f;

            const int maxIterations = 5;
            for (int i = 0; i < maxIterations; i++)
            {
                Vector3 random = Random.onUnitSphere * 2f;
                random.y = Mathf.Clamp(random.y, -0.5f, 0.5f);
                Vector3 candidate = preferred + random;
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit navMeshHit, 2f, -1))
                {
                    continue;
                }

                if ((navMeshHit.position - botPosition).sqrMagnitude <= 1f)
                {
                    continue;
                }

                if (BotOwner.GoToPoint(navMeshHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }

            if (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold * Random.Range(0.66f, 1.33f))
            {
                Vector3 direction = (botPosition - target.Value).normalized;
                Vector3 random = Random.onUnitSphere * Random.Range(1.25f, 2f);
                random.y = 0f;
                Vector3 point = botPosition + direction * Random.Range(1f, 2f) + random;
                if (NavMesh.Raycast(botPosition, point, out NavMeshHit raycastHit, -1))
                {
                    if (raycastHit.distance <= 0.5f)
                    {
                        dogFightPath.ClearCorners();
                        if (NavMesh.CalculatePath(botPosition, point, -1, dogFightPath) &&
                            dogFightPath.status == NavMeshPathStatus.PathComplete &&
                            dogFightPath.corners.Length > 0)
                        {
                            Vector3 pathEnd = dogFightPath.corners[dogFightPath.corners.Length - 1];
                            return BotOwner.GoToPoint(pathEnd, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
                        }
                    }

                    return BotOwner.GoToPoint(raycastHit.position, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
                }

                return BotOwner.GoToPoint(point, false, -1f, false, false) == NavMeshPathStatus.PathComplete;
            }

            return false;
        }

        private Vector3? FindBackUpTarget(EnemyInfo goalEnemy)
        {
            BotOwner? enemyBot = goalEnemy.Person?.AIData?.BotOwner;
            if (enemyBot != null &&
                (enemyBot.WeaponManager.Reload.Reloading ||
                 !enemyBot.WeaponManager.HaveBullets ||
                 enemyBot.Medecine?.Using == true ||
                 (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold)))
            {
                return goalEnemy.CurrPosition;
            }

            if (goalEnemy.IsVisible && Time.time - goalEnemy.PersonalSeenTime < RecentSeenThreshold)
            {
                return goalEnemy.CurrPosition;
            }

            return goalEnemy.EnemyLastPositionReal;
        }

        private bool IsUnsafeCloseThreatFacing(EnemyInfo? goalEnemy)
        {
            return goalEnemy != null &&
                   goalEnemy.IsVisible &&
                   goalEnemy.CanShoot &&
                   goalEnemy.Distance <= UnsafeCloseThreatDistance &&
                   CombatAttackMoveLook.GetThreatLookAngle(BotOwner, goalEnemy) > UnsafeCloseThreatLookAngle;
        }

        private void MaintainThreatFacing(EnemyInfo goalEnemy, Vector3 shootPoint, bool allowHardTurn)
        {
            if (!CombatAttackMoveLook.TryLookThreatFacing(BotOwner, goalEnemy, allowHardTurn))
            {
                BotOwner.Steering.LookToPoint(shootPoint);
            }
        }

        private enum DogFightMoveStatus
        {
            None,
            BackingUp,
            MovingToEnemy,
            Shooting,
        }
    }
}
