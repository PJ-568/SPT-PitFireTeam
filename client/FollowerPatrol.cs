using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

using friendlySAIN.Modules;
using friendlySAIN.Brains;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * A modified version of the FollowerPatrol class to ensure our followers stay around the player boss
     */
    public class FollowerPatrol : GClass193<StandardBrain>
    {

        protected Player player_0;

        protected pitAIBossPlayer boss_0;

        /** holder for how frequent show the distance between bots and the boss be checked  **/
        private float float_3 = 0f;
        /** holder for how frequent move around the boss when standing still  **/
        private float float_4 = 0f;
        /** last position the bot registered to move to **/
        private Vector3 vector3_0;
        /** flag to tell the bot to no longer move once in cover **/
        private bool bool_0 = false;
        /** helper for checking if bot is in range of the boss **/
        private bool bool_1 = false;
        /** helper for checking if bot is currently stationary at a checkpoint **/
        private float float_6 = 0f;
        private float float_7 = 0f;
        /** helper for checking if bot is currently moving to a checkpoint **/
        private bool bool_6;

        private bool bool_7 = false;

        protected bool shouldPatrol = false;

        private Vector3? leaderLastPosition = null;
        private Vector3? leaderLastCamp = null;

        private float float_5 = 0f;
        private bool bool_2 = false;

        protected CustomNavigationPoint lastCoverPoint;
        protected bool nocover = false;

        public bool IsInited { get; set; }

        private bool wasHit = false;

        private bool init = false;

        private float patrolRadius;

        private bool doPeacefulActions = false;
        private bool doPeaceLook = false;
        private bool doPeaceHardAim = false;
        private bool doSecondWpnWatch = false;



        public FollowerPatrol(BotOwner owner) : base(owner)
        {
            vector3_0 = owner.Position;

            IsInited = true;

            patrolRadius = friendlySAIN.patrolRadius.Value;
        }

        private void Init()
        {
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate += OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose += OnAgentDispose;

            init = true;
        }
        private void OnAgentUpdate(AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision)
        {
            if (
                botOwner_0.Memory.HaveEnemy
            )
            {
                bool_0 = false;
                bool_1 = false;
                float_4 = 0f;

                bool_2 = false;
                bool_6 = false;
                bool_7 = false;
                float_3 = 0f;
                float_4 = 0f;
                float_6 = 0f;
                float_7 = 0f;
                float_5 = 0f;

                lastCoverPoint = null;
                nocover = false;

                doPeacefulActions = false;
                doPeaceLook = false;
                doPeaceHardAim = false;
                doSecondWpnWatch = false;
            }
        }

        private void OnAgentDispose(object sender, EventArgs e)
        {
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate -= OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose -= OnAgentDispose;
            init = false;
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {

            FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            // let the bot turn to the direction he was hit from
            if (brain != null && brain.WasHit)
            {
                wasHit = true;
            }


            if (!init) Init();

            botOwner_0.DoorOpener.Update();

            if (boss_0 == null)
            {
                if (brain == null || brain.playerBoss == null)
                {
                    botOwner_0.StopMove();
                    return;
                }
                boss_0 = brain.playerBoss;
                player_0 = brain.playerBoss.realPlayer;
            }

            PatrolAround(brain.canPatrol);
            try
            {
                if (!shouldPatrol) Follow();
                else Patrol();
            }
            catch (Exception e)
            {
                Modules.Logger.LogError(e);
                botOwner_0.StopMove();
            }
        }
        /** Boss following logic  **/
        protected virtual void Follow(bool following = false, float distance = 0f)
        {

            if (bool_7)
            {
                botOwner_0.GoToSomePointData.UpdateToGo(false);
                //if (!wasHit) botOwner_0.Steering.LookToMovingDirection();
            }
            else if (botOwner_0.Mover.TargetPose != 1f) botOwner_0.Mover.SetPose(1f);

            if (float_3 < Time.time)
            {

                float_3 = Time.time + Utils.Utils.Random(1f, 2f);

                Vector3 leaderPosition = player_0.Transform.position;
                FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

                bool flag2;
                bool flag;
                float num;

                // check if we are in range of the boss
                if (following)
                {
                    num = distance;
                }
                else
                {
                    num = Mathf.Abs((bool_0 ? vector3_0 : (leaderPosition - botOwner_0.Position)).magnitude);
                }

                flag = (flag2 = num < brain.followDistance) != bool_1;
                bool_1 = flag2;

                // we are in range of the boss
                if (flag2)
                {


                    if (bool_0)
                    {
                        botOwner_0.StopMove();
                        return;
                    }

                    if (bool_7)
                    {
                        if (botOwner_0.GoToSomePointData.IsCome())
                        {
                            bool_7 = false;
                        }
                        return;
                    }

                    if (float_4 < Time.time || flag)
                    {
                        float_4 = Time.time + 8f;

                        int reachDist = brain.followDistance;

                        CustomNavigationPoint nearPoint = null;
                        // check if we can find a cover point near the boss to hide and wait
                        if (lastCoverPoint == null && !nocover)
                        {
                            List<CustomNavigationPoint> coverPoints = botOwner_0.Covers.GetClosePoints(boss_0.realPlayer.Transform.position, reachDist + 5f);

                            float maxDist = reachDist;
                            float radius = maxDist;

                            NavMeshPath navMeshPath = new NavMeshPath();
                            List<CustomNavigationPoint> availCover = new List<CustomNavigationPoint>();
                            coverPoints.ForEach((point) =>
                            {
                                float dist = (leaderPosition - point.Position).magnitude;
                                if (point.IsFreeById(botOwner_0.Id) && Utils.Utils.GetNavDistance(leaderPosition, point.Position, navMeshPath) <= maxDist)
                                {
                                    availCover.Add(point);
                                }
                            });

                            CustomNavigationPoint cover = availCover.Count > 0 ? availCover.GetRandomItem() : null;
                            if (cover != null)
                            {
                                nearPoint = cover;
                            }
                        }
                        else
                            nearPoint = lastCoverPoint;

                        if (nearPoint != null)
                        {
                            lastCoverPoint = nearPoint;
                            botOwner_0.Memory.SetCoverPoints(nearPoint);


                            botOwner_0.GoToSomePointData.SetPoint(nearPoint.Position);
                            botOwner_0.GoToSomePointData.UpdateToGo(false);
                            if (!wasHit) botOwner_0.Steering.LookToPathDestPoint();

                            bool_7 = true;
                            float_3 = Time.time + 0.5f;
                            return;
                        }
                        // no cover found, we will just roam around the boss
                        nocover = true;
                        float minR = Mathf.Min(1f, reachDist * 0.19f);
                        float maxR = Mathf.Min(5f, reachDist * 0.65f);
                        float num2 = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minR, maxR);
                        float num3 = (float)Utils.Utils.RandomSing() * Utils.Utils.Random(minR, maxR);
                        float x = num2 + leaderPosition.x;
                        float z = num3 + leaderPosition.z;

                        NavMeshHit navMeshHit;
                        if (!NavMesh.SamplePosition(new Vector3(x, leaderPosition.y, z), out navMeshHit, 2f, -1))
                        {
                            botOwner_0.StopMove();
                            bool_0 = true;
                            return;
                        }

                        botOwner_0.GoToSomePointData.SetPoint(navMeshHit.position);
                        botOwner_0.GoToSomePointData.UpdateToGo(false);

                        if (!wasHit) botOwner_0.Steering.LookToPathDestPoint();

                        bool_7 = true;
                        float_3 = Time.time + 0.5f;
                    }
                }
                // out of range of the boss
                else
                {
                    bool_7 = false;
                    lastCoverPoint = null;
                    nocover = false;
                    method_0(leaderPosition);
                    bool val = num > Math.Min(brain.followDistance + 3, 16);

                    if (botOwner_0.Mover.TargetPose != 1f) botOwner_0.Mover.SetPose(1f);

                    if (val)
                        botOwner_0.Mover.Sprint(true, false);
                    else if (!val) botOwner_0.Mover.Sprint(false, false);
                }
            }
        }

        /** Patrol around the boss logic  **/
        protected virtual void Patrol()
        {
            if (float_5 > Time.time)
            {
                Follow();
            }

            bool_7 = false;

            // check if leader is still moving
            Vector3 playerPosition = player_0.Transform.position;
            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            FollowerBrain brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            if (!bool_2)
            {
                Vector3 leaderPosition = new Vector3(
                    Mathf.Floor(playerPosition.x / 3f) * 3f,
                    Mathf.Floor(playerPosition.y / 3f) * 3f,
                    Mathf.Floor(playerPosition.z / 3f) * 3f
                );

                float num = Mathf.Abs((bool_0 ? vector3_0 : (leaderPosition - botPosition)).magnitude);
                bool flag2;
                flag2 = num < brain.followDistance;

                // - boss might or not move, but bot is out range - keep moving
                if (!flag2)
                {
                    Follow(true, num);
                    float_5 = Time.time + 5f;
                    return;
                }


                if (leaderLastPosition.HasValue && leaderLastPosition != leaderPosition)
                {
                    leaderLastPosition = leaderPosition;
                    Follow(true, num);
                    float_5 = Time.time + 5f;
                    return;
                }
                else
                    leaderLastPosition = leaderPosition;

                // - check if leader is camping
                bool_2 = true;
            }

            if (!bool_2) return;

            // frequency on which to check if the leader is camping or moving
            if (float_7 > Time.time) return;
            float_7 = Time.time + 1.5f;

            float campRadius = 30f;
            float perimeterRadius = patrolRadius;

            Vector3 bossPosition = new Vector3(
                Mathf.Floor(playerPosition.x / campRadius) * campRadius,
                Mathf.Floor(playerPosition.y / campRadius) * campRadius,
                Mathf.Floor(playerPosition.z / campRadius) * campRadius
            );
            // check if leader is out of camping
            if (leaderLastCamp.HasValue && bossPosition != leaderLastCamp)
            {
                leaderLastCamp = null;
                bool_2 = false;
                float_5 = Time.time + 5f;
                botOwner_0.Mover.SetTargetMoveSpeed(1f);
                Follow();
                return;
            }
            else
            {
                leaderLastCamp = bossPosition;
            }

            // roam around the boss
            // - wait in checkpoint if bot is there
            if (float_6 > Time.time)
            {
                if (doPeacefulActions)
                {
                    botOwner_0.PeacefulActions.UpdateAction();
                }
                else if (doPeaceLook)
                {
                    botOwner_0.PeaceLook.ManualUpdate();
                }
                else if (doPeaceHardAim)
                {
                    botOwner_0.PeaceHardAim.ManualUpdate();
                }
                else if (doSecondWpnWatch)
                {
                    botOwner_0.SecondWeaponData.ManualUpdate();
                }

                bool_6 = false;
                return;
            }
            // - if bot is moving to a checkpoint wait to reach it
            if (bool_6)
            {
                if (botOwner_0.Mover.IsComeTo(botOwner_0.Settings.FileSettings.Move.REACH_DIST, false))
                {
                    botOwner_0.StopMove();
                    if (!wasHit) botOwner_0.LookData.SetLookPointByHearing(null);
                    float_6 = Time.time + Utils.Utils.Random(6f, 10f);
                }
                else if (!wasHit) botOwner_0.Steering.LookToMovingDirection();

                return;
            }

            // - select a checkpoint to move to
            List<Vector3> carePositions = new List<Vector3> { playerPosition };

            pitAIBossPlayer boss = BossPlayers.GetBoss(player_0.ProfileId);

            if (boss == null)
            {
                carePositions.Add(botPosition);
            }
            else
            {
                foreach (BotOwner follower in boss.Followers)
                {
                    carePositions.Add(follower.GetPlayer.Transform.position);
                }
            }

            Vector3[] finalcarePositions = carePositions.ToArray();
            // - get a valid position to move to, must be reachable and far enough from other teammates
            for (int i = 0; i < 30; i++)
            {
                Vector3 randomPosition = bossPosition + UnityEngine.Random.insideUnitSphere * perimeterRadius;

                NavMeshHit navMeshHit;
                if (!NavMesh.SamplePosition(randomPosition, out navMeshHit, 10f, -1)) continue;

                if (!Utils.Utils.IsDangerPositionFarEnough(navMeshHit.position, finalcarePositions, 2f * 2f)) continue;

                if (botOwner_0.GoToPoint(navMeshHit.position, true, -1f, false, false) == NavMeshPathStatus.PathComplete)
                {

                    botOwner_0.Mover.Sprint(false, false);
                    botOwner_0.Mover.SetTargetMoveSpeed(0.5f);
                    if (!wasHit) botOwner_0.Steering.LookToPoint(navMeshHit.position + Vector3.up * 1.5f);

                    doPeacefulActions = false;
                    doPeaceLook = false;
                    doPeaceHardAim = false;
                    doSecondWpnWatch = false;


                    bool hasActions = botOwner_0.PeacefulActions.HaveActions();
                    bool hasLook = botOwner_0.PeaceLook.HaveActions();
                    bool hasHardAim = botOwner_0.PeaceHardAim.HaveActions();
                    bool hasSecondWpnWatch = botOwner_0.SecondWeaponData.HaveActions();

                    // decide which peaceful action to do by randomly selecting one from the available
                    if (hasActions && UnityEngine.Random.value > 0.5f)
                        doPeacefulActions = true;

                    if (!doPeacefulActions && hasLook && UnityEngine.Random.value > 0.5f)
                        doPeaceLook = true;

                    if (!doPeacefulActions && !doPeaceLook && hasHardAim && UnityEngine.Random.value > 0.5f)
                        doPeaceHardAim = true;

                    if (!doPeacefulActions && !doPeaceLook && !doPeaceHardAim && hasSecondWpnWatch && UnityEngine.Random.value > 0.5f)
                        doSecondWpnWatch = true;

                    bool_6 = true;

                    return;
                }
            }

        }
        /** Find closest nav or cover point to the leader's position **/
        protected void method_0(Vector3 leaderPosition)
        {
            bool_0 = false;
            NavMeshHit navMeshHit;

            if (method_1(leaderPosition) == NavMeshPathStatus.PathComplete)
            {
                bool_0 = false;
            }
            else if (NavMesh.SamplePosition(leaderPosition, out navMeshHit, 1.5f, -1) && method_1(leaderPosition) != NavMeshPathStatus.PathComplete)
            {
                bool_0 = true;
            }

            if (bool_0)
            {
                CustomNavigationPoint freeClosePoint = botOwner_0.Covers.GetFreeClosePoint(leaderPosition, 0f, false);
                if (freeClosePoint != null)
                {
                    bool_0 = true;
                    method_1(freeClosePoint.Position);
                }
            }
        }
        /** Check if given position can be moved to, and move to it **/
        protected NavMeshPathStatus method_1(Vector3 v)
        {
            NavMeshPathStatus navMeshPathStatus = botOwner_0.GoToPoint(v, true, -1f, false, false);
            if (navMeshPathStatus == NavMeshPathStatus.PathComplete)
            {
                if (!wasHit) botOwner_0.Steering.LookToMovingDirection();
                // store the last position we moved to
                vector3_0 = v;
            }
            return navMeshPathStatus;
        }


        public void PatrolAround(bool state = false)
        {
            shouldPatrol = state;

            if (state == false)
            {
                bool_6 = false;
                float_6 = 0f;
                float_5 = 0f;
                bool_2 = false;
                float_7 = 0f;
            }
        }
    }
}
