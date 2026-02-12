using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Components;
using friendlySAIN.Modules;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace friendlySAIN.BigBrain.Actions
{
    internal class FollowAction : CustomLogic
    {
        private const int DefaultFollowDistance = 12;

        private Player? player_0;
        private pitAIBossPlayer? boss_0;
        private BotFollowerPlayer? followerData;

        private float float_3 = 0f;
        private float float_4 = 0f;
        private Vector3 vector3_0;
        private bool bool_0 = false;
        private bool bool_1 = false;
        private float float_6 = 0f;
        private float float_7 = 0f;
        private bool bool_6;
        private bool bool_7 = false;

        private bool shouldPatrol = false;

        private Vector3? leaderLastPosition = null;
        private Vector3? leaderLastCamp = null;

        private float float_5 = 0f;
        private bool bool_2 = false;

        private CustomNavigationPoint? lastCoverPoint;
        private bool nocover = false;

        private float patrolRadius;

        private bool doPeacefulActions = false;
        private bool doPeaceLook = false;
        private bool doPeaceHardAim = false;
        private bool doSecondWpnWatch = false;

        private bool? sprintState = null;

        public FollowAction(BotOwner botOwner) : base(botOwner)
        {
            vector3_0 = botOwner.Position;
            patrolRadius = friendlySAIN.patrolRadius.Value;
        }

        public override void Start()
        {
            base.Start();
            bool_0 = false;
            bool_1 = false;
            bool_2 = false;
            bool_6 = false;
            bool_7 = false;
            float_3 = 0f;
            float_4 = 0f;
            float_5 = 0f;
            float_6 = 0f;
            float_7 = 0f;
            sprintState = null;
            ResetPeaceActions();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (BotOwner == null || !BotOwner.BotFollower.HaveBoss) return;

            BotOwner.DoorOpener.UpdateDoorInteractionStatus();
            followerData ??= BossPlayers.Instance?.GetFollower(BotOwner);
            PatrolAround(followerData?.CanPatrol == true);

            if (!TryGetBossAndPlayer())
            {
                BotOwner.StopMove();
                return;
            }

            try
            {
                if (!shouldPatrol)
                {
                    Follow();
                }
                else
                {
                    Patrol();
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError(ex);
                BotOwner.StopMove();
            }
        }

        private bool TryGetBossAndPlayer()
        {
            if (BotOwner.BotFollower.BossToFollow is not pitAIBossPlayer boss || boss.realPlayer == null)
            {
                return false;
            }
            boss_0 = boss;
            player_0 = boss.realPlayer;
            return true;
        }

        private void Follow(bool following = false, float distance = 0f)
        {
            if (player_0 == null) return;

            if (bool_7)
            {
                BotOwner.GoToSomePointData.UpdateToGo(false);
            }
            else if (BotOwner.Mover.TargetPose != 1f)
            {
                BotOwner.Mover.SetPose(1f);
            }

            if (float_3 >= Time.time) return;
            float_3 = Time.time + Utils.Utils.Random(1f, 2f);

            Vector3 leaderPosition = player_0.Transform.position;
            int followDistance = DefaultFollowDistance;

            float num;
            if (following)
            {
                num = distance;
            }
            else
            {
                num = Mathf.Abs((bool_0 ? vector3_0 : (leaderPosition - BotOwner.Position)).magnitude);
            }

            bool inRange = num < followDistance;
            bool rangeChanged = inRange != bool_1;
            bool_1 = inRange;

            if (inRange)
            {
                if (bool_0)
                {
                    BotOwner.StopMove();
                    return;
                }

                if (bool_7)
                {
                    if (BotOwner.GoToSomePointData.IsCome())
                    {
                        bool_7 = false;
                    }
                    return;
                }

                if (float_4 < Time.time || rangeChanged)
                {
                    float_4 = Time.time + 8f;
                    int reachDist = followDistance;

                    CustomNavigationPoint? nearPoint = null;
                    if (lastCoverPoint == null && !nocover)
                    {
                        List<CustomNavigationPoint> coverPoints = BotOwner.Covers.GetClosePoints(player_0.Transform.position, reachDist + 5f);

                        float maxDist = reachDist;
                        NavMeshPath navMeshPath = new NavMeshPath();
                        List<CustomNavigationPoint> availCover = new List<CustomNavigationPoint>();

                        foreach (var point in coverPoints)
                        {
                            if (point == null) continue;
                            if (!point.IsFreeById(BotOwner.Id)) continue;
                            if (Utils.Utils.GetNavDistance(leaderPosition, point.Position, navMeshPath) <= maxDist)
                            {
                                availCover.Add(point);
                            }
                        }

                        if (availCover.Count > 0)
                        {
                            nearPoint = availCover[UnityEngine.Random.Range(0, availCover.Count)];
                        }
                    }
                    else
                    {
                        nearPoint = lastCoverPoint;
                    }

                    if (nearPoint != null)
                    {
                        lastCoverPoint = nearPoint;
                        BotOwner.Memory.SetCoverPoints(nearPoint);
                        BotOwner.GoToSomePointData.SetPoint(nearPoint.Position);
                        BotOwner.GoToSomePointData.UpdateToGo(false);
                        BotOwner.Steering.LookToPathDestPoint();

                        bool_7 = true;
                        float_3 = Time.time + 0.5f;
                        return;
                    }

                    nocover = true;
                    float minR = Mathf.Min(1f, reachDist * 0.19f);
                    float maxR = Mathf.Min(5f, reachDist * 0.65f);
                    float num2 = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minR, maxR);
                    float num3 = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minR, maxR);
                    float x = num2 + leaderPosition.x;
                    float z = num3 + leaderPosition.z;

                    if (!NavMesh.SamplePosition(new Vector3(x, leaderPosition.y, z), out NavMeshHit navMeshHit, 2f, -1))
                    {
                        BotOwner.StopMove();
                        bool_0 = true;
                        return;
                    }

                    BotOwner.GoToSomePointData.SetPoint(navMeshHit.position);
                    BotOwner.GoToSomePointData.UpdateToGo(false);
                    BotOwner.Steering.LookToPathDestPoint();
                    bool_7 = true;
                    float_3 = Time.time + 0.5f;
                }

                return;
            }

            bool_7 = false;
            lastCoverPoint = null;
            nocover = false;
            method_0(leaderPosition);
            bool mustSprint = num > Math.Min(followDistance + 3, 16);

            if (BotOwner.Mover.TargetPose != 1f) BotOwner.Mover.SetPose(1f);
            SetSprint(mustSprint);
        }

        private void Patrol()
        {
            if (player_0 == null || boss_0 == null) return;

            if (float_5 > Time.time)
            {
                Follow();
            }

            bool_7 = false;

            Vector3 playerPosition = player_0.Transform.position;
            Vector3 botPosition = BotOwner.GetPlayer.Transform.position;
            int followDistance = DefaultFollowDistance;

            if (!bool_2)
            {
                Vector3 leaderPosition = new Vector3(
                    Mathf.Floor(playerPosition.x / 3f) * 3f,
                    Mathf.Floor(playerPosition.y / 3f) * 3f,
                    Mathf.Floor(playerPosition.z / 3f) * 3f
                );

                float num = Mathf.Abs((bool_0 ? vector3_0 : (leaderPosition - botPosition)).magnitude);
                bool inRange = num < followDistance;

                if (!inRange)
                {
                    Follow(true, num);
                    float_5 = Time.time + 5f;
                    return;
                }

                if (leaderLastPosition.HasValue && leaderLastPosition.Value != leaderPosition)
                {
                    leaderLastPosition = leaderPosition;
                    Follow(true, num);
                    float_5 = Time.time + 5f;
                    return;
                }

                leaderLastPosition = leaderPosition;
                bool_2 = true;
            }

            if (!bool_2) return;
            if (float_7 > Time.time) return;

            float_7 = Time.time + 1.5f;
            float campRadius = 30f;
            float perimeterRadius = patrolRadius;

            Vector3 bossPosition = new Vector3(
                Mathf.Floor(playerPosition.x / campRadius) * campRadius,
                Mathf.Floor(playerPosition.y / campRadius) * campRadius,
                Mathf.Floor(playerPosition.z / campRadius) * campRadius
            );

            if (leaderLastCamp.HasValue && bossPosition != leaderLastCamp.Value)
            {
                leaderLastCamp = null;
                bool_2 = false;
                float_5 = Time.time + 5f;
                BotOwner.Mover.SetTargetMoveSpeed(1f);
                Follow();
                return;
            }
            leaderLastCamp = bossPosition;

            if (float_6 > Time.time)
            {
                if (doPeacefulActions)
                {
                    BotOwner.PeacefulActions.UpdateAction();
                }
                else if (doPeaceLook)
                {
                    BotOwner.PeaceLook.ManualUpdate();
                }
                else if (doPeaceHardAim)
                {
                    BotOwner.PeaceHardAim.ManualUpdate();
                }
                else if (doSecondWpnWatch)
                {
                    BotOwner.SecondWeaponData.ManualUpdate();
                }

                bool_6 = false;
                return;
            }

            if (bool_6)
            {
                if (BotOwner.Mover.IsComeTo(BotOwner.Settings.FileSettings.Move.REACH_DIST, false))
                {
                    BotOwner.StopMove();
                    BotOwner.LookData.SetLookPointByHearing(null);
                    float_6 = Time.time + Utils.Utils.Random(6f, 10f);
                }
                else
                {
                    BotOwner.Steering.LookToMovingDirection();
                }
                return;
            }

            List<Vector3> carePositions = new List<Vector3> { playerPosition };
            foreach (BotOwner follower in boss_0.Followers)
            {
                carePositions.Add(follower.GetPlayer.Transform.position);
            }
            Vector3[] finalCarePositions = carePositions.ToArray();

            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPosition = bossPosition + UnityEngine.Random.insideUnitSphere * perimeterRadius;
                if (!NavMesh.SamplePosition(randomPosition, out NavMeshHit navMeshHit, 10f, -1)) continue;
                if (!Utils.Utils.IsDangerPositionFarEnough(navMeshHit.position, finalCarePositions, 2f * 2f)) continue;

                if (BotOwner.GoToPoint(navMeshHit.position, true, -1f, false, false) != NavMeshPathStatus.PathComplete) continue;

                SetSprint(false);
                BotOwner.Mover.SetTargetMoveSpeed(0.5f);
                BotOwner.Steering.LookToPoint(navMeshHit.position + Vector3.up * 1.5f);

                ResetPeaceActions();

                bool hasActions = BotOwner.PeacefulActions.HaveActions();
                bool hasLook = BotOwner.PeaceLook.HaveActions();
                bool hasHardAim = BotOwner.PeaceHardAim.HaveActions();
                bool hasSecondWpnWatch = BotOwner.SecondWeaponData.HaveActions();

                if (hasActions && UnityEngine.Random.value > 0.5f) doPeacefulActions = true;
                if (!doPeacefulActions && hasLook && UnityEngine.Random.value > 0.5f) doPeaceLook = true;
                if (!doPeacefulActions && !doPeaceLook && hasHardAim && UnityEngine.Random.value > 0.5f) doPeaceHardAim = true;
                if (!doPeacefulActions && !doPeaceLook && !doPeaceHardAim && hasSecondWpnWatch && UnityEngine.Random.value > 0.5f) doSecondWpnWatch = true;

                bool_6 = true;
                return;
            }
        }

        private void method_0(Vector3 leaderPosition)
        {
            bool_0 = false;

            if (method_1(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                bool_0 = false;
            }
            else if (NavMesh.SamplePosition(leaderPosition, out _, 1.5f, -1) && method_1(leaderPosition) != NavMeshPathStatus.PathComplete)
            {
                bool_0 = true;
            }

            if (bool_0)
            {
                CustomNavigationPoint freeClosePoint = BotOwner.Covers.GetFreeClosePoint(leaderPosition, 0f, false);
                if (freeClosePoint != null)
                {
                    bool_0 = true;
                    method_1(freeClosePoint.Position);
                }
            }
        }

        private NavMeshPathStatus method_1(Vector3 v)
        {
            NavMeshPathStatus navMeshPathStatus = BotOwner.GoToPoint(v, true, -1f, false, false);
            if (navMeshPathStatus == NavMeshPathStatus.PathComplete)
            {
                BotOwner.Steering.LookToMovingDirection();
                vector3_0 = v;
            }
            return navMeshPathStatus;
        }

        private void SetSprint(bool state)
        {
            if (sprintState.HasValue && sprintState.Value == state) return;
            BotOwner.Mover.Sprint(state, false);
            sprintState = state;
        }

        private void PatrolAround(bool state = false)
        {
            shouldPatrol = state;

            if (!state)
            {
                bool_6 = false;
                float_6 = 0f;
                float_5 = 0f;
                bool_2 = false;
                float_7 = 0f;
            }
        }

        private void ResetPeaceActions()
        {
            doPeacefulActions = false;
            doPeaceLook = false;
            doPeaceHardAim = false;
            doSecondWpnWatch = false;
        }
    }
}
