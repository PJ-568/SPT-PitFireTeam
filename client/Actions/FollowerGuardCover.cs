using EFT;
using UnityEngine;

using friendlySAIN.Brains;
using friendlySAIN.Utils;

namespace friendlySAIN.Actions
{
    /**
     * Alternative action to enemy search for the follower with "Guard" tactic
     */
    public class FollowerGuardCover : FollowerSniperSearch
    {
        public FollowerGuardCover(BotOwner owner)
        : base(owner)
        {
            maxDist = Props.searchRadius;
            minDist = 5f;
            searchPose = 0.1f;
            Action = (BotLogicDecision)CustomBotDecisions.GuardToCover;
            searchType = "GuardCover";
        }

        protected override void UpdateShootPosition()
        {
            if (_nextShootPositionUpdateTime > Time.time) return;

            _nextShootPositionUpdateTime = Time.time + 3f;

            if (!botOwner_0.Memory.HaveEnemy)
            {
                _lastTarget = null;
                return;
            }

            Vector3 enemySpot = botOwner_0.Memory.GoalEnemy.CurrPosition;

            RefreshSearchPoint(enemySpot);


            if (_lastTarget.HasValue)
            {
                Vector3 bossPos = botOwner_0.BotFollower.BossToFollow.Player().Transform.position;
                Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
                bool protectBoss = (botOwner_0.Brain.BaseBrain as FollowerBrain).bossNeedsProtection;
                ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);

                // get closest attack point from the bot's position
                CustomNavigationPoint Spot = Utils.Covers.GetClosestCoverPoint(
                        botOwner_0,
                        botPosition,
                        maxDist,
                        point =>
                        {
                            if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                            {
                                return true;
                            }
                            return false;
                        }
                );

                if (Spot != null)
                {
                    _lastSpot = Spot.Position;
                    return;
                }

                _lastSpot = null;

                _actionsQueue.Enqueue(() =>
                {
                    // else get the next cover between the bot and the enemy
                    shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                    float maxDistFromBoss = Props.searchRadius * Props.searchRadius;
                    Vector3 middlePoint = botPosition + enemySpot;
                    float mdist = 5f;
                    CustomNavigationPoint Spot2 = Utils.Covers.GetClosestCoverPointBetween(
                        botOwner_0,
                        botPosition,
                        enemySpot,
                        mdist,
                        point =>
                        {
                            if ((point.Position - bossPos).sqrMagnitude > maxDistFromBoss) return false;

                            if (Utils.Utils.CanShootToTarget(shootPointClass, point.FirePosition, botOwner_0.LookSensor.Mask, false))
                            {
                                return true;
                            }
                            else if (Utils.Utils.CanHide(point.Position, point.WallDirection, new Vector3[] { enemySpot }, mdist * mdist, true))
                            {
                                return true;
                            }
                            return false;
                        }
                    );

                    if (Spot2 != null)
                    {
                        _lastSpot = Spot2.Position;
                        return;
                    }
                    _lastSpot = null;

                    _actionsQueue.Enqueue(() =>
                    {
                        // else find the next available spot from where the bot can shoot the enemy, relative to his position
                        if (botOwner_0.IsDead || botOwner_0.BotState != EBotState.Active || !botOwner_0.Memory.HaveEnemy) return;
                        _lastPosition = Utils.Covers.FindShootPosition(
                            botOwner_0,
                            minDist,
                            maxDist
                        );

                        if (_lastPosition != null)
                        {
                            return;
                        }
                        _lastPosition = null;

                        _actionsQueue.Enqueue(() =>
                        {
                            // if boss needs protection
                            if (protectBoss)
                            {
                                // - get closest attack point from the bot's position
                                shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                                CustomNavigationPoint Spot3 = Utils.Covers.GetClosestCoverPoint(
                                    botOwner_0,
                                    bossPos,
                                    Props.searchRadius,
                                    point =>
                                    {

                                        if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                                        {
                                            return true;
                                        }
                                        return false;
                                    }
                                );
                                if (Spot3 != null)
                                {
                                    _lastSpot = Spot3.Position;
                                    return;
                                }

                                _lastSpot = null;

                                // - else get closest cover to the boss relative to the enemy and cover him
                                _actionsQueue.Enqueue(() =>
                                {
                                    shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                                    middlePoint = bossPos + enemySpot;
                                    CustomNavigationPoint Spot5 = Utils.Covers.GetClosestCoverPoint(
                                        botOwner_0,
                                        bossPos,
                                        Props.bossMaxCoverDistance,
                                        point =>
                                        {
                                            if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                                            {
                                                return true;
                                            }
                                            else if (point.CanIHide(new Vector3[] { enemySpot }, 5f, true))
                                            {
                                                return true;
                                            }
                                            return false;
                                        }
                                    );

                                    if (Spot5 != null)
                                    {
                                        _lastCover = Spot5.Position;
                                        return;
                                    }

                                    _lastCover = null;
                                });
                            }
                            else
                            {
                                // else get closest cover
                                CustomNavigationPoint Spot4 = Utils.Covers.GetClosestCoverPoint(
                                    botOwner_0,
                                    botPosition,
                                    30f,
                                    point =>
                                    {
                                        if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                                        {
                                            point.CanIShootToEnemy = true;
                                        }

                                        if (point.CanIHide(new Vector3[] { enemySpot }, minDist, true))
                                        {
                                            return true;
                                        }

                                        return false;
                                    }
                                );

                                if (Spot4 != null)
                                {
                                    _lastCover = Spot4.Position;
                                    return;
                                }

                                _lastCover = null;
                            }
                        });

                    });
                });
            }
            else
            {
                _lastPosition = null;
            }
        }
    }
}
