using EFT;

using System;
using System.Collections.Generic;

using UnityEngine;

using friendlySAIN.Brains;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Actions
{
    /**
     * Shooting position search action for a bot sniper follower
     */
    public class FollowerSniperSearch : GClass193<StandardBrain>
    {
        private Vector3? spotPosition;

        private bool sprint = false;

        private float float_4 = 0f;
        private float float_5 = 0f;

        protected Vector3? _lastTarget;
        protected Vector3? _lastPosition;
        protected Vector3? _lastCover;
        protected Vector3? _lastSpot;

        protected float _nextShootPositionUpdateTime = 0f;

        protected bool covering = false;

        protected bool _hasCome = false;

        protected float minDist = 10f;

        protected float maxDist = 100f;

        protected float searchPose = 0.1f;

        protected Queue<Action> _actionsQueue = new Queue<Action>();

        protected bool _init = false;

        protected BotLogicDecision Action = (BotLogicDecision)CustomBotDecisions.SniperSearch;

        protected string searchType = "SniperSearch";
        public FollowerSniperSearch(BotOwner bot) : base(bot)
        {

        }

        protected virtual void Init()
        {
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate += OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose += OnAgentDispose;

            _init = true;
        }
        protected void OnAgentUpdate(AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision)
        {
            if (
                decision.Action != Action
            )
            {
                _actionsQueue.Clear();
                spotPosition = null;
                _lastTarget = null;
            }
        }

        protected void OnAgentDispose(object sender, EventArgs e)
        {
            _actionsQueue?.Clear();
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnUpdate -= OnAgentUpdate;
            (botOwner_0.Brain.Agent as FollowerAIAgent<BotLogicDecision>).OnDispose -= OnAgentDispose;
            _init = false;
        }

        public override void UpdateNodeByBrain(StandardBrain data)
        {
            try
            {
                botOwner_0.DoorOpener.Update();

                try
                {
                    if (!_init)
                    {
                        Init();
                    }
                }
                catch (Exception ex)
                {
                    Modules.Logger.LogError("Failed to init Search");
                    Modules.Logger.LogError(ex);
                }

                if (!botOwner_0.Memory.HaveEnemy) return;

                while (_actionsQueue.Count > 0)
                {
                    Action action = _actionsQueue.Dequeue();
                    action();
                }

                if (spotPosition.HasValue)
                {
                    if (botOwner_0.GoToSomePointData.IsCome())
                    {
                        if (!_hasCome)
                        {
                            botOwner_0.SetPose(searchPose);
                            botOwner_0.StopMove();
                            botOwner_0.Steering.LookToPoint(botOwner_0.Memory.GoalEnemy.GetCenterPart());
                            _hasCome = true;
                        }
                        ReachSearchPoint();
                        return;
                    }

                    botOwner_0.GoToSomePointData.UpdateToGo(sprint);

                    _hasCome = false;


                    if (float_4 < Time.time)
                    {
                        float_4 = Time.time + 2f;
                        sprint = !Utils.Utils.IsWithinDistance(spotPosition.Value, botOwner_0.GetPlayer.Transform.position, 20f); ;
                    }
                    return;
                }

                if (_lastTarget.HasValue && float_5 < Time.time)
                {
                    float_5 = Time.time + Utils.Utils.Random(3f, 4f);

                    UpdateShootPosition();


                    if (!_lastTarget.HasValue) return;

                    if (_lastSpot.HasValue)
                    {
                        spotPosition = _lastSpot;
                        covering = false;
                    }
                    // else find a position from where we can see the enemy
                    else if (_lastPosition.HasValue)
                    {
                        spotPosition = _lastPosition.Value;
                        covering = false;
                    }
                    // else find a position from where we can cover boss
                    else if (_lastCover.HasValue)
                    {
                        spotPosition = _lastCover.Value;
                        covering = true;

                    }
                    // nothing found - stay in place
                    if (!spotPosition.HasValue)
                    {
                        SetSearchPosition();
                        return;
                    }

                    botOwner_0.GoToSomePointData.SetPoint((Vector3)spotPosition);
                    sprint = !Utils.Utils.IsWithinDistance(spotPosition.Value, botOwner_0.GetPlayer.Transform.position, 20f);
                    botOwner_0.GoToSomePointData.UpdateToGo(sprint);
                    botOwner_0.LookData.ResetUpdateTime();
                    botOwner_0.LookData.SetLookPointByHearing(null);
                }
                else
                {
                    _hasCome = false;
                    if (!_lastTarget.HasValue)
                    {
                        UpdateShootPosition();
                    }
                    else SetSearchPosition();
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"{searchType} Error");
                Modules.Logger.LogError(ex);
            }
        }

        protected void RefreshSearchPoint(Vector3? enemyCurrPosition = null)
        {
            Vector3 enemyPosition = enemyCurrPosition.HasValue ? enemyCurrPosition.Value : botOwner_0.Memory.GoalEnemy.CurrPosition;

            Vector3 targetSpot = new Vector3(
                Mathf.Floor(enemyPosition.x / 12f) * 12f,
                Mathf.Floor(enemyPosition.y / 2f) * 2f,
                Mathf.Floor(enemyPosition.z / 12f) * 12f
            );

            if (targetSpot != _lastTarget)
            {
                _lastTarget = targetSpot;
                spotPosition = null;
                covering = false; ;
            }
        }

        protected virtual void ReachSearchPoint()
        {
            SetSearchPosition();
            spotPosition = null;
            covering = false;
        }

        protected virtual void SetSearchPosition()
        {
            if (botOwner_0.Mover.TargetPose != searchPose) botOwner_0.SetPose(searchPose);
            botOwner_0.StopMove();
            if (botOwner_0.Memory.HaveEnemy) botOwner_0.Steering.LookToPoint(botOwner_0.Memory.GoalEnemy.GetCenterPart());
        }

        protected virtual void UpdateShootPosition()
        {
            if (_nextShootPositionUpdateTime > Time.time) return;

            _nextShootPositionUpdateTime = Time.time + 2f;

            if (!botOwner_0.Memory.HaveEnemy)
            {
                _lastTarget = null;
                return;
            }

            Vector3 enemySpot = botOwner_0.Memory.GoalEnemy.CurrPosition;

            RefreshSearchPoint(enemySpot);


            if (_lastTarget.HasValue)
            {
                ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                float _weaponShootDistMaxSqr = botOwner_0.LookSensor.MaxShootDist * botOwner_0.LookSensor.MaxShootDist;

                // get closest cover to the bot from where he can shoot the enemy
                CustomNavigationPoint Spot = Utils.Covers.GetClosestCoverPoint(
                    botOwner_0,
                    botOwner_0.Position,
                    maxDist,
                    point =>
                    {
                        if ((point.Position - shootPointClass.Point).sqrMagnitude >= _weaponShootDistMaxSqr) return false;
                        if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                        {
                            return true;
                        }
                        return false;
                    }
                );

                if (Spot != null) _lastSpot = Spot.Position;
                else _lastSpot = null;


                if (!_lastSpot.HasValue)
                {
                    _actionsQueue.Enqueue(() =>
                    {
                        // else get a shooting spot relative to the bot
                        if (botOwner_0.IsDead || botOwner_0.BotState != EBotState.Active || !botOwner_0.Memory.HaveEnemy) return;

                        _lastPosition = Utils.Covers.FindShootPosition(
                            botOwner_0,
                            minDist,
                            maxDist
                        );

                        if (!_lastPosition.HasValue && botOwner_0.BotFollower.HaveBoss)
                        {
                            _actionsQueue.Enqueue(() =>
                            {
                                // else find the closest cover to the boss and cover him
                                Vector3 botPos = botOwner_0.GetPlayer.Transform.position;
                                Vector3 bossPos = botOwner_0.BotFollower.BossToFollow.Position;
                                bool protectBoss = (botOwner_0.Brain.BaseBrain as FollowerBrain).bossNeedsProtection;

                                CustomNavigationPoint cover = Utils.Covers.GetClosestCoverPoint(
                                    botOwner_0,
                                    protectBoss ? bossPos : botPos,
                                    50f,
                                    (CustomNavigationPoint point) =>
                                    {
                                        if (!point.CanIHide(new Vector3[] { enemySpot }, 5f, true)) return false;

                                        return true;
                                    }
                                );

                                if (cover != null)
                                {
                                    _lastCover = cover.Position;

                                }
                                else
                                {
                                    _lastCover = null;
                                }
                            });
                        }

                    });
                }
            }
            else
            {
                _lastPosition = null;
            }
        }
    }
}
