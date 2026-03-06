using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using SAIN.Layers;
using SAIN.SAINComponent.Classes.EnemyClasses;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINFollowerCombatRegroupAction : BotAction
    {
        private const float MoveIssueInterval = 0.75f;
        private const float TargetRefreshInterval = 0.8f;
        private const float BossReanchorDistance = 2.5f;
        private const float DesiredBossBuffer = 4f;
        private const float MinBossBuffer = 2.25f;
        private const float MaxBossBuffer = 7f;
        private const float FollowerSpacing = 1.75f;
        private const float ArriveDistance = 1.5f;

        private float _nextMoveIssueTime;
        private float _nextRefreshTargetTime;
        private bool _haveTarget;
        private Vector3 _targetPosition;
        private Vector3 _lastBossPosition;
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly List<BotOwner> _aliveFollowers = new List<BotOwner>(8);

        public SAINFollowerCombatRegroupAction(BotOwner botOwner)
            : base(botOwner, nameof(SAINFollowerCombatRegroupAction))
        {
        }

        public override void Start()
        {
            base.Start();
            _nextMoveIssueTime = 0f;
            _nextRefreshTargetTime = 0f;
            _haveTarget = false;
            _targetPosition = Vector3.zero;
            _lastBossPosition = Vector3.zero;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!TryGetBoss(out pitAIBossPlayer boss, out Vector3 bossPosition))
            {
                return;
            }

            if (NeedRetarget(bossPosition) || !HasCompletePath(_targetPosition))
            {
                if (TrySelectRegroupTarget(boss, bossPosition, out Vector3 target))
                {
                    _targetPosition = target;
                    _haveTarget = true;
                }
                else
                {
                    _targetPosition = bossPosition;
                    _haveTarget = true;
                }

                _lastBossPosition = bossPosition;
                _nextRefreshTargetTime = Time.time + TargetRefreshInterval;
            }

            Enemy enemy = Bot.GoalEnemy;
            bool hasEnemy = enemy != null;
            bool enemyLOS = enemy?.InLineOfSight == true;
            float leadDist = (_targetPosition - BotOwner.Position).magnitude;
            float enemyDist = hasEnemy ? enemy.KnownPlaces.BotDistanceFromLastKnown : 999f;
            bool sprint = hasEnemy && leadDist > 20f && !enemyLOS && enemyDist > 50f;

            if (_nextMoveIssueTime <= Time.time)
            {
                _nextMoveIssueTime = Time.time + MoveIssueInterval;
                if (sprint)
                {
                    Bot.Mover.RunToPoint(_targetPosition);
                }
                else
                {
                    if ((bossPosition - BotOwner.Position).magnitude <= MinBossBuffer && leadDist <= ArriveDistance)
                    {
                        Bot.Mover.Stop();
                    }
                    else
                    {
                        Bot.Mover.WalkToPoint(_targetPosition);
                    }
                }
            }

            Bot.Mover.SetTargetPose(1f);
            Bot.Mover.SetTargetMoveSpeed(1f);
        }

        public override void OnSteeringTicked()
        {
            Enemy enemy = Bot.GoalEnemy;
            if (!Shoot.ShootAnyVisibleEnemies(enemy))
            {
                Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
            }

            if (!Bot.Steering.SteerByPriority(enemy))
            {
                Bot.Steering.LookToMovingDirection();
            }
        }

        private bool NeedRetarget(Vector3 bossPosition)
        {
            if (!_haveTarget || Time.time >= _nextRefreshTargetTime)
            {
                return true;
            }

            if ((_lastBossPosition - bossPosition).sqrMagnitude >= BossReanchorDistance * BossReanchorDistance)
            {
                return true;
            }

            return (_targetPosition - BotOwner.Position).sqrMagnitude <= ArriveDistance * ArriveDistance;
        }

        private bool TryGetBoss(out pitAIBossPlayer boss, out Vector3 position)
        {
            boss = default!;
            position = default;
            if (BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer playerBoss || playerBoss.realPlayer == null)
            {
                return false;
            }

            boss = playerBoss;
            position = playerBoss.realPlayer.Transform.position;
            return true;
        }

        private bool TrySelectRegroupTarget(pitAIBossPlayer boss, Vector3 bossPosition, out Vector3 target)
        {
            target = default;
            BuildAliveFollowerSnapshot(boss);
            int slotIndex = GetFollowerSlotIndex();
            int aliveCount = _aliveFollowers.Count;
            float angleStep = aliveCount > 0 ? 360f / aliveCount : 45f;

            float bestScore = float.MaxValue;
            bool found = false;

            for (int i = 0; i < Mathf.Max(aliveCount, 8); i++)
            {
                int slot = aliveCount > 0 ? (slotIndex + i) % aliveCount : i;
                float angleDeg = slot * angleStep;
                if (TryEvaluateCandidate(boss, bossPosition, angleDeg, DesiredBossBuffer, ref bestScore, ref target))
                {
                    found = true;
                }
                if (TryEvaluateCandidate(boss, bossPosition, angleDeg + (angleStep * 0.5f), MinBossBuffer + 0.5f, ref bestScore, ref target))
                {
                    found = true;
                }
            }

            for (int i = 0; i < 12; i++)
            {
                float angle = Random.Range(0f, 360f);
                float radius = Random.Range(MinBossBuffer + 0.25f, MaxBossBuffer);
                if (TryEvaluateCandidate(boss, bossPosition, angle, radius, ref bestScore, ref target))
                {
                    found = true;
                }
            }

            return found;
        }

        private bool TryEvaluateCandidate(pitAIBossPlayer boss, Vector3 bossPosition, float angleDeg, float radius, ref float bestScore, ref Vector3 bestTarget)
        {
            Vector3 dir = Quaternion.Euler(0f, angleDeg, 0f) * Vector3.forward;
            Vector3 raw = bossPosition + (dir.normalized * Mathf.Clamp(radius, MinBossBuffer, MaxBossBuffer));

            if (!NavMesh.SamplePosition(raw, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                return false;
            }

            if ((hit.position - bossPosition).magnitude < MinBossBuffer)
            {
                return false;
            }

            if (IsCrowded(boss, hit.position))
            {
                return false;
            }

            if (!TryGetPathLength(hit.position, out float pathLength))
            {
                return false;
            }

            float score = Mathf.Abs((hit.position - bossPosition).magnitude - DesiredBossBuffer) * 2f + pathLength;
            if (score >= bestScore)
            {
                return false;
            }

            bestScore = score;
            bestTarget = hit.position;
            return true;
        }

        private void BuildAliveFollowerSnapshot(pitAIBossPlayer boss)
        {
            _aliveFollowers.Clear();
            var followers = boss?.Followers;
            if (followers == null || followers.Count == 0)
            {
                return;
            }

            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                _aliveFollowers.Add(follower);
            }
        }

        private int GetFollowerSlotIndex()
        {
            if (_aliveFollowers.Count == 0)
            {
                return 0;
            }

            for (int i = 0; i < _aliveFollowers.Count; i++)
            {
                if (_aliveFollowers[i] == BotOwner)
                {
                    return i;
                }
            }

            return 0;
        }

        private bool IsCrowded(pitAIBossPlayer boss, Vector3 candidate)
        {
            if (boss?.realPlayer != null && (candidate - boss.realPlayer.Transform.position).magnitude < MinBossBuffer)
            {
                return true;
            }

            var followers = boss?.Followers;
            if (followers == null || followers.Count == 0)
            {
                return false;
            }

            float spacingSqr = FollowerSpacing * FollowerSpacing;
            for (int i = 0; i < followers.Count; i++)
            {
                BotOwner follower = followers[i];
                if (follower == null || follower == BotOwner || follower.IsDead || follower.BotState != EBotState.Active)
                {
                    continue;
                }

                if ((follower.Position - candidate).sqrMagnitude < spacingSqr)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasCompletePath(Vector3 target)
        {
            return TryGetPathLength(target, out _);
        }

        private bool TryGetPathLength(Vector3 target, out float length)
        {
            length = 0f;
            if (!NavMesh.CalculatePath(BotOwner.Position, target, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            var corners = _path.corners;
            if (corners == null || corners.Length == 0)
            {
                return false;
            }

            for (int i = 1; i < corners.Length; i++)
            {
                length += Vector3.Distance(corners[i - 1], corners[i]);
            }

            return true;
        }
    }
}
