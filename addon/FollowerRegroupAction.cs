using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using SAIN.Layers;

using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.SAINAddon
{
    internal sealed class SAINRegroupAction : BotAction
    {
        private const bool EnableRegroupDebugLogs = false;
        private const float ArriveDistance = 3f;
        private const float CloseTargetLockDistance = 8f;
        private const float RunDistance = 10f;
        private const float ArrivalSettleDuration = 0.35f;

        private BotFollowerPlayer? _followerData;
        private float _nextRefreshAt;
        private float _nextMoveLogAt;
        private float _nextMoveIssueAt;
        private bool _loggedStart;
        private bool _loggedArrival;
        private Vector3 _lastMoveLogPos;
        private bool _hasLastMoveLogPos;
        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private float _nextStuckRepathAt;
        private int _stuckRepathCount;
        private bool _arrivalSettling;
        private float _arrivalClearAt;
        private bool _bossAnchorInitialized;
        private Vector3 _bossAnchorPosition;
        private float _nextBossAnchorCheckAt;

        public SAINRegroupAction(BotOwner botOwner) : base(botOwner, nameof(SAINRegroupAction))
        {
        }

        public override void Start()
        {
            base.Start();
            _nextRefreshAt = 0f;
            _nextMoveLogAt = 0f;
            _nextMoveIssueAt = 0f;
            _loggedStart = false;
            _loggedArrival = false;
            _hasLastMoveLogPos = false;
            _hasMoveTarget = false;
            _moveTarget = Vector3.zero;
            _nextStuckRepathAt = 0f;
            _stuckRepathCount = 0;
            _arrivalSettling = false;
            _arrivalClearAt = 0f;
            _bossAnchorInitialized = false;
            _bossAnchorPosition = Vector3.zero;
            _nextBossAnchorCheckAt = 0f;
        }

        public override void Stop()
        {
            if (Bot != null)
            {
                Bot.Mover.Stop();
            }

            if (EnableRegroupDebugLogs && BotOwner != null && _loggedStart)
            {
                Modules.Logger.LogInfo($"[SAIN Regroup] action stop follower={BotOwner.Profile?.Nickname ?? BotOwner.name}");
            }
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || BotOwner.BotState != EBotState.Active)
            {
                return;
            }

            if (_followerData == null || Time.time >= _nextRefreshAt)
            {
                _followerData = BossPlayers.Instance?.GetFollower(BotOwner);
                _nextRefreshAt = Time.time + 0.5f;
            }

            bool isRegroupCommand = _followerData != null
                && _followerData.TryGetActiveCommand(out FollowerCommandType command, out _)
                && command == FollowerCommandType.RegroupNearBoss;

            if (EnableRegroupDebugLogs && isRegroupCommand && !_loggedStart)
            {
                Modules.Logger.LogInfo(
                    $"[SAIN Regroup] action start follower={BotOwner.Profile?.Nickname ?? BotOwner.name}");
                _loggedStart = true;
            }

            if (!isRegroupCommand)
            {
                return;
            }

            // Route ownership changed (e.g. combat ended): release SAIN regroup without clearing command so vanilla can pick it up.
            if (!friendlySAIN.ShouldSainRegroupLayerHandle(BotOwner))
            {
                Bot.Mover.Stop();
                Bot.Mover.ActivePath?.Cancel(0.05f);
                return;
            }

            if (!TryGetBossPosition(out Vector3 bossPos))
            {
                return;
            }

            if (ShouldInterruptRegroupForThreatOrState())
            {
                Bot.Mover.Stop();
                Bot.Mover.ActivePath?.Cancel(0.05f);
                _followerData?.ClearCommand();
                return;
            }

            if (!_bossAnchorInitialized)
            {
                _bossAnchorInitialized = true;
                _bossAnchorPosition = bossPos;
                _nextBossAnchorCheckAt = Time.time + 0.5f;
            }

            if (Time.time >= _nextBossAnchorCheckAt)
            {
                _nextBossAnchorCheckAt = Time.time + 0.5f;
                if ((_bossAnchorPosition - bossPos).sqrMagnitude > 10f * 10f)
                {
                    _bossAnchorPosition = bossPos;
                    _hasMoveTarget = false;
                }
            }

            // Boss moved toward us: finish regroup early without forcing path to the selected offset point.
            float bossVerticalDiff = Mathf.Abs(BotOwner.Position.y - bossPos.y);
            if (bossVerticalDiff <= 1.75f)
            {
                float navDistanceToBoss = Utils.Utils.GetNavDistance(BotOwner.Position, bossPos);
                if (navDistanceToBoss <= 6f)
                {
                    StartArrivalSettle(navDistanceToBoss);
                    return;
                }
            }

            if (_arrivalSettling)
            {
                HoldArrivalSettle();
                if (Time.time >= _arrivalClearAt)
                {
                    if (EnableRegroupDebugLogs && !_loggedArrival)
                    {
                        _loggedArrival = true;
                        Modules.Logger.LogInfo($"[SAIN Regroup] arrived follower={BotOwner.Profile?.Nickname ?? BotOwner.name} (settled)");
                    }
                    _followerData?.ClearCommand();
                    _arrivalSettling = false;
                }
                return;
            }

            float currentTargetDist = _hasMoveTarget ? Vector3.Distance(BotOwner.Position, _moveTarget) : float.MaxValue;
            bool isCloseToTarget = currentTargetDist <= CloseTargetLockDistance;

            if (!_hasMoveTarget || (!isCloseToTarget && Vector3.Distance(_moveTarget, bossPos) > 6f))
            {
                _moveTarget = SelectRegroupTarget(bossPos);
                _hasMoveTarget = true;
                _stuckRepathCount = 0;
            }

            if (!isCloseToTarget && !HasCompletePathTo(_moveTarget))
            {
                _moveTarget = SelectRegroupTarget(bossPos);
                _hasMoveTarget = true;
            }

            Bot.Mover.SetTargetPose(1f);
            Bot.Mover.SetTargetMoveSpeed(1f);

            float dist = Vector3.Distance(BotOwner.Position, _moveTarget);
            if (dist <= ArriveDistance)
            {
                StartArrivalSettle(dist);
                return;
            }

            bool sprint = dist > RunDistance && (Bot.GoalEnemy?.InLineOfSight != true);
            bool moved = true;
            if (_nextMoveIssueAt <= Time.time)
            {
                _nextMoveIssueAt = Time.time + 0.5f;
                moved = sprint
                    ? Bot.Mover.RunToPoint(_moveTarget, true, ArriveDistance, SAIN.SAINComponent.Classes.Mover.ESprintUrgency.High, true)
                    : Bot.Mover.WalkToPoint(_moveTarget, true, ArriveDistance, true);
            }

            if (Time.time >= _nextMoveLogAt)
            {
                _nextMoveLogAt = Time.time + 1f;
                float delta = _hasLastMoveLogPos ? Vector3.Distance(_lastMoveLogPos, BotOwner.Position) : 0f;
                _lastMoveLogPos = BotOwner.Position;
                _hasLastMoveLogPos = true;

                if (delta < 0.25f && dist > CloseTargetLockDistance && Time.time >= _nextStuckRepathAt)
                {
                    _moveTarget = SelectRegroupTarget(bossPos);
                    _hasMoveTarget = true;
                    _stuckRepathCount++;
                    _nextStuckRepathAt = Time.time + 1.5f;
                    if (EnableRegroupDebugLogs)
                    {
                        Modules.Logger.LogInfo(
                            $"[SAIN Regroup] repath follower={BotOwner.Profile?.Nickname ?? BotOwner.name} " +
                            $"reason=low_progress count={_stuckRepathCount} newTarget={Fmt(_moveTarget)}");
                    }
                }



                if (EnableRegroupDebugLogs)
                {
                    string currentActionName = Bot.CurrentAction?.Name ?? "<none>";
                    var activePath = Bot.Mover.ActivePath;
                    string lastCornerText = "<none>";
                    if (activePath?.PathCorners != null && activePath.PathCorners.Count > 0)
                    {
                        lastCornerText = Fmt(activePath.GetLastCorner().Position);
                    }
                    string pathInfo = activePath == null
                        ? "path=<null>"
                        : $"pathStatus={activePath.PathStatus} moveStatus={activePath.Status} " +
                        $"corners={activePath.PathCorners?.Count ?? 0} dest={Fmt(activePath.Destination)} " +
                        $"last={lastCornerText}";

                    Modules.Logger.LogInfo(
                        $"[SAIN Regroup] move follower={BotOwner.Profile?.Nickname ?? BotOwner.name} dist={dist:F1} " +
                        $"target={Fmt(_moveTarget)} pos={Fmt(BotOwner.Position)} action={currentActionName} " +
                        $"bossOff={Vector3.Distance(_moveTarget, bossPos):F1} " +
                        $"run={(dist > RunDistance)} moved={moved} delta={delta:F2} pause={BotOwner.Mover.Pause} " +
                        $"sainLayersActive={Bot.SAINLayersActive} activeLayer={Bot.ActiveLayer} {pathInfo}");
                }
            }
        }

        public override void OnSteeringTicked()
        {
            if (_arrivalSettling)
            {
                return;
            }

            var enemy = Bot.GoalEnemy;
            if (!Shoot.ShootAnyVisibleEnemies(enemy))
            {
                Bot.Suppression.TrySuppressAnyEnemy(enemy, Bot.EnemyController.KnownEnemies);
            }
            if (!Bot.Steering.SteerByPriority(enemy))
            {
                Bot.Steering.LookToMovingDirection();
            }
        }

        private bool TryGetBossPosition(out Vector3 bossPos)
        {
            bossPos = default;

            if (BotOwner?.BotFollower?.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }

            bossPos = boss.realPlayer.Transform.position;
            return true;
        }

        private bool ShouldInterruptRegroupForThreatOrState()
        {
            if (BotOwner.Memory?.HaveEnemy == true && BotOwner.Memory.GoalEnemy?.IsVisible == true)
            {
                if (BotOwner.Memory.GoalEnemy.CanShoot && BotOwner.LookSensor.EnoughDistToShoot(out _))
                {
                    return true;
                }
            }

            BotLogicDecision currentDecision = BotOwner.Brain?.Agent?.LastResult().Action ?? BotLogicDecision.holdPosition;
            bool healing = BotOwner.Medecine?.FirstAid?.Using == true ||
                           BotOwner.Medecine?.SurgicalKit?.Using == true ||
                           currentDecision == BotLogicDecision.heal;
            if (healing)
            {
                return true;
            }

            bool dangerNow = currentDecision == BotLogicDecision.runAwayGrenade ||
                             currentDecision == BotLogicDecision.runAwayBTR ||
                             BotOwner.BewareGrenade?.ShallRunAway() == true ||
                             BotOwner.BewareBTR?.ShallRunAway() == true;
            return dangerNow;
        }

        private void StartArrivalSettle(float dist)
        {
            _arrivalSettling = true;
            _arrivalClearAt = Time.time + ArrivalSettleDuration;

            // Stop movement/sprint first, then clear command on the next moments to avoid jumpy handoff.
            HoldArrivalSettle();

            if (EnableRegroupDebugLogs)
            {
                Modules.Logger.LogInfo(
                    $"[SAIN Regroup] arrival settle follower={BotOwner.Profile?.Nickname ?? BotOwner.name} dist={dist:F1} clearIn={ArrivalSettleDuration:F2}s");
            }
        }

        private void HoldArrivalSettle()
        {
            if (Bot.Mover.Running)
                Bot.Mover.ActivePath?.RequestEndSprint(SAIN.SAINComponent.Classes.Mover.ESprintUrgency.None, "friendly regroup arrival settle");
            Bot.Mover.ActivePath?.Cancel(0.05f);

            Bot.Mover.SetTargetPose(1f);
            Bot.Mover.SetTargetMoveSpeed(0f);
            Bot.Mover.Stop();

        }

        private Vector3 SelectBossOffsetTarget(Vector3 bossPos)
        {
            const float minRadius = 3f;
            const float maxRadius = 4f;
            const int angleSteps = 16;

            Vector3 bestPoint = bossPos;
            bool hasBest = false;
            float bestScore = float.MinValue;
            float startAngle = Random.Range(0f, 360f);

            for (int i = 0; i < angleSteps; i++)
            {
                float angle = startAngle + (360f / angleSteps) * i;
                float radius = (i & 1) == 0 ? minRadius : maxRadius;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                Vector3 candidate = new Vector3(bossPos.x + offset.x, bossPos.y, bossPos.z + offset.z);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                {
                    continue;
                }

                if (Mathf.Abs(hit.position.y - bossPos.y) > 2f)
                {
                    continue;
                }
                if (Mathf.Abs(hit.position.y - BotOwner.Position.y) > 2.5f)
                {
                    continue;
                }

                NavMeshPath path = new NavMeshPath();
                if (!NavMesh.CalculatePath(BotOwner.Position, hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                {
                    continue;
                }

                float bossRingDist = Mathf.Abs(Vector3.Distance(hit.position, bossPos) - 3.5f);
                float botPathDist = CalculatePathLength(path);
                float score = -bossRingDist - (botPathDist * 0.01f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPoint = hit.position;
                    hasBest = true;
                }
            }

            if (!hasBest && NavMesh.SamplePosition(bossPos, out NavMeshHit bossHit, 1.5f, NavMesh.AllAreas))
            {
                bestPoint = bossHit.position;
            }

            return bestPoint;
        }

        private Vector3 SelectRegroupTarget(Vector3 bossPos)
        {
            Vector3 ringTarget = SelectBossOffsetTarget(bossPos);
            if (HasCompletePathTo(ringTarget))
            {
                return ringTarget;
            }

            if (TrySelectApproachTarget(bossPos, out Vector3 approachTarget))
            {
                return approachTarget;
            }

            return ringTarget;
        }

        private bool TrySelectApproachTarget(Vector3 bossPos, out Vector3 target)
        {
            target = default;

            Vector3 sampledBoss = bossPos;
            if (!NavMesh.SamplePosition(bossPos, out NavMeshHit bossHit, 2.5f, NavMesh.AllAreas))
            {
                return false;
            }
            sampledBoss = bossHit.position;

            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(BotOwner.Position, sampledBoss, NavMesh.AllAreas, path))
            {
                return false;
            }

            if (path.corners == null || path.corners.Length < 2)
            {
                return false;
            }

            float totalLen = CalculatePathLength(path);
            if (totalLen < 2f)
            {
                return false;
            }

            // Move along the path toward the boss instead of forcing an unreachable boss-ring point.
            float desiredTravel = Mathf.Clamp(totalLen - 4f, 4f, 12f);
            Vector3 pointOnPath = GetPointAlongPath(path, desiredTravel);

            if (!NavMesh.SamplePosition(pointOnPath, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
            {
                return false;
            }

            if (Mathf.Abs(hit.position.y - BotOwner.Position.y) > 2.5f)
            {
                return false;
            }

            if (!HasCompletePathTo(hit.position))
            {
                return false;
            }

            target = hit.position;
            return true;
        }

        private bool HasCompletePathTo(Vector3 point)
        {
            NavMeshPath path = new NavMeshPath();
            return NavMesh.CalculatePath(BotOwner.Position, point, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
        }

        private static float CalculatePathLength(NavMeshPath path)
        {
            if (path == null || path.corners == null || path.corners.Length < 2)
            {
                return 0f;
            }

            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            return length;
        }

        private static Vector3 GetPointAlongPath(NavMeshPath path, float distanceFromStart)
        {
            if (path.corners == null || path.corners.Length == 0)
            {
                return Vector3.zero;
            }

            if (path.corners.Length == 1 || distanceFromStart <= 0f)
            {
                return path.corners[0];
            }

            float remaining = distanceFromStart;
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 a = path.corners[i - 1];
                Vector3 b = path.corners[i];
                float segLen = Vector3.Distance(a, b);
                if (segLen <= 0.001f)
                {
                    continue;
                }

                if (remaining <= segLen)
                {
                    float t = remaining / segLen;
                    return Vector3.Lerp(a, b, t);
                }

                remaining -= segLen;
            }

            return path.corners[path.corners.Length - 1];
        }

        private static string Fmt(Vector3 v)
        {
            return $"({v.x:F1},{v.y:F1},{v.z:F1})";
        }
    }
}
