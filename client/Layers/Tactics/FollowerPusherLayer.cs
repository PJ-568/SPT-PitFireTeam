using EFT;
using System;
using System.Collections.Generic;
using UnityEngine;

using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.Tactics
{
    /** 
     * This class is not meant to be used directly as a brain layer, but within one 
     * Enemy Push layer based on Boar follower fight logic
     * **/
    public class FollowerPusherLayer : GClass55
    {
        private FollowerCommonLayer commonLayer;

        public FollowerCommonLayer CommonLayer { get { return commonLayer; } }

        private bool existingCommon = false;

        public CustomNavigationPoint NavigationPoint
        {
            get
            {
                return customNavigationPoint_0;
            }
        }

        protected float holdTimer = 0f;

        public FollowerPusherLayer(BotOwner bot, int priority, FollowerCommonLayer commonLayer = null) : base(bot, priority)
        {
            if (commonLayer != null)
            {
                this.commonLayer = commonLayer;
                existingCommon = true;
            }
            else this.commonLayer = new FollowerCommonLayer(bot, priority);
        }

        public override void OnActivate()
        {
            base.OnActivate();
            if (!existingCommon) commonLayer?.OnActivate();
        }
        public override void Dispose()
        {
            base.Dispose();
            if (!existingCommon) commonLayer?.Dispose();
        }

        public void OrdersChanged()
        {
            commonLayer.OrdersChanged();
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            commonLayer.DecisionChanged(prevDecision, nextDecision);
            base.DecisionChanged(prevDecision, nextDecision);
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> EngageEnemy(bool pushOrdered = false, bool enemyLowTreat = false)
        {

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            Vector3 enemyPos = botOwner_0.Memory.GoalEnemy.CurrPosition;
            bool enemyVisible = botOwner_0.Memory.GoalEnemy.IsVisible;
            float lastEnemySeenTime = botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime;
            bool inCover = botOwner_0.Memory.IsInCover;

            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(botOwner_0);
            float enemiesAtLocation = 0;
            if (botOwner_0.Memory.GoalEnemy.ProfileId != null)
                enemiesAtLocation = enemyLowTreat ? 1 : Utils.Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, enemyPos);

            // PUSH CASE
            bool covertried = false;
            if (botOwner_0.Memory.AttackImmediately || pushOrdered)
            {
                if (
                    // - go for it if enemy is already close and if its low in numbers
                    (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close && enemiesAtLocation < 2) ||
                    // - go for it if ordered
                    (pushOrdered && enemiesAtLocation < 4)
                )
                {
                    BotLogicDecision pushDecision;

                    if (pushOrdered) pushDecision = BotLogicDecision.runToEnemy;
                    else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Close) pushDecision = BotLogicDecision.goToEnemy;
                    else pushDecision = BotLogicDecision.runToEnemy;

                    // - check if the current enemy is the closest and do a slow approach if not
                    if (!Utils.Enemy.IsClosestEnemy(botOwner_0) && distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        pushDecision = BotLogicDecision.goToEnemy;
                    }

                    // -- push if not visible or ordered
                    if (!enemyVisible || pushOrdered)
                    {
                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(enemyVisible ? BotLogicDecision.goToEnemy : pushDecision, "pushEnemy");
                    }
                    else
                    {
                        if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                        {
                            GetApproachablePoint(true);
                            covertried = true;
                            if (customNavigationPoint_0 != null) return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "getInCloseFast");
                            else if (distanceToEnemy == Utils.Enemy.EnemyDistance.Mid)
                            {
                                if (enemyVisible)
                                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                                else
                                {
                                    botOwner_0.GoToSomePointData.SetPoint(enemyPos);
                                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToPointTactical, "getInCloseSlow");
                                }
                            }
                        }
                        else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                        {
                            if (enemyVisible)
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.dogFight, "cdg");
                            else
                            {
                                botOwner_0.GoToSomePointData.SetPoint(enemyPos);
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToPointTactical, "getInCloseSlow");
                            }
                        }

                    }
                }

                // - enemy visible and push conditions not met
                if (enemyVisible)
                {
                    if (covertried)
                    {
                        if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.DogFight, "cdg");
                        }

                        return EnemySearch();
                    }

                    if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid)
                    {
                        // -- in cover
                        if (inCover)
                        {
                            // --- shoot from cover if possible
                            if (botOwner_0.Memory.CurCustomCoverPoint != null && botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                            // --- too close, prepare to fight
                            else if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                            {
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.DogFight, "cdg");
                            }
                            else
                            {
                                // --- else try and find better spot
                                botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                                GetApproachablePoint(distanceToEnemy == Utils.Enemy.EnemyDistance.Mid);
                                if (customNavigationPoint_0 != null)
                                {
                                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                                }
                                // --- no cover point found, hold position temporarily
                                else
                                {
                                    botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Ambush);
                                    float timer = Utils.Utils.Random(2f, 5f);
                                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(timer), "waitAbit");
                                }
                            }

                        }
                        // -- not in cover
                        else
                        {
                            // - gather available covers
                            ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);
                            float _weaponShootDistMaxSqr = botOwner_0.LookSensor.MaxShootDist * botOwner_0.LookSensor.MaxShootDist;
                            Vector3 enemySpot = botOwner_0.Memory.GoalEnemy.CurrPosition;
                            List<CustomNavigationPoint> shootCovers = new List<CustomNavigationPoint>();
                            List<CustomNavigationPoint> hideCovers = new List<CustomNavigationPoint>();

                            Utils.Covers.GetCoverPoints(botOwner_0, botPosition, Utils.Props.coverSearchRadius * 1.2f, point =>
                            {
                                // -- shooting covers
                                if (
                                    (point.Position - shootPointClass.Point).sqrMagnitude < _weaponShootDistMaxSqr &&
                                    Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false)
                                )
                                {
                                    point.CanIShootToEnemy = true;
                                    shootCovers.Add(point);
                                    return true;
                                }
                                // -- hide covers
                                else if (Utils.Utils.CanHide(point.Position, point.GroupPoint.WallDirection, new Vector3[] { enemySpot }, 5f * 5f, true))
                                {
                                    point.CanIShootToEnemy = false;
                                    hideCovers.Add(point);
                                    return true;
                                }

                                return false;
                            }, 30);
                            // --- find a cover point closer to the enemy
                            customNavigationPoint_0 = Utils.Covers.ClosestPoint(botOwner_0.Id, botPosition, botPosition, shootCovers, point =>
                            {
                                return true;
                            });
                            if (customNavigationPoint_0 != null)
                            {
                                botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runForCover");
                            }
                            // --- no cover point found, try to find a hiding spot
                            customNavigationPoint_0 = Utils.Covers.ClosestPoint(botOwner_0.Id, botPosition, botPosition, hideCovers, point =>
                            {
                                return true;
                            });
                            if (customNavigationPoint_0 != null)
                            {
                                botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Protect);
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "relocate");
                            }
                        }
                    }
                    // -- enemy is distant but visible
                    else
                    {
                        if (inCover)
                        {
                            // --- shoot from cover if possible
                            if (botOwner_0.Memory.CurCustomCoverPoint != null && botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                        }

                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);

                        GetApproachablePoint(true);
                        if (customNavigationPoint_0 != null)
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "getInCloseFast");
                        }
                        else
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                        }
                    }
                }
                // - enemy not visible and push conditions not met
                else
                {
                    if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToEnemy, "pushEnemy");
                    }

                    GetApproachablePoint(distanceToEnemy > Utils.Enemy.EnemyDistance.Mid);
                    if (customNavigationPoint_0 != null)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "getInCloseFast");
                    }

                    return EnemySearch();
                }

            }
            // play the intimidation game 
            else
            {
                // - shoot from cover if possible
                if (inCover)
                {
                    if (botOwner_0.Memory.CurCustomCoverPoint != null && botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                }
                // - enemy too close, just fight
                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.DogFight, "cdg");
                }
                else
                {
                    Vector3 centerPosition = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? (botPosition + enemyPos) / 2f : botPosition;
                    float radius = distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid ? Utils.Props.coverSearchRadius : Utils.Props.searchRadius;
                    if (!enemyVisible)
                    {
                        // - stay a bit
                        if (Time.time - lastEnemySeenTime < Utils.Utils.Random(2f, 3f))
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(2f, 4f)), "waitAbit");
                        }
                        // - enemy not visible, find a shooting spot
                        GetClosestAttackCoverPoint(centerPosition, radius);
                        if (customNavigationPoint_0 != null)
                        {
                            botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                            if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "getInCloseFast");
                            else
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                        }
                    }
                    else
                    {
                        // - enemy is visible 
                        if (distanceToEnemy > Utils.Enemy.EnemyDistance.Mid) GetClosestAttackCoverPoint(centerPosition, radius);
                        else GetClosestCoverPoint(centerPosition, radius);
                        if (customNavigationPoint_0 != null)
                        {
                            botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Protect);
                            if (distanceToEnemy >= Utils.Enemy.EnemyDistance.Mid)
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "getInCloseFast");
                            else
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "getInCloseSlow");
                        }
                    }
                }
            }

            return EnemySearch();
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> EnemySearch(string reason = null)
        {

            botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.EnemySearch, reason != null ? reason : "enemy.Search");
        }

        public override AICoreActionEndStruct EndRunToEnemy()
        {
            return commonLayer.EndRunToEnemy();
        }

        public AICoreActionEndStruct EndEnemySearch()
        {
            if (commonLayer.OrderHasChangedRecently)
                return new AICoreActionEndStruct("search.End", true);

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (Time.time - commonLayer.LastTimeHit <= 0.5f)
            {
                return new AICoreActionEndStruct("enemy.ShotMe", true);
            }

            if (Utils.Enemy.Distance(botOwner_0) <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionEndStruct("enemy.Close", true);
            }

            return aICoreActionEndStruct;
        }

        public override AICoreActionEndStruct EndHoldPosition()
        {
            try
            {
                if (commonLayer.OrderHasChangedRecently)
                {
                    return new AICoreActionEndStruct("EndHol", true);
                }


                AIBossPlayerLogic gclass363_0 = commonLayer.HasBoss() ? commonLayer.GetBoss().GetBossLogic() : null;

                string text;
                if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy.Person.HealthController.IsAlive && base.method_6(out text))
                {
                    return new AICoreActionEndStruct("cst", true);
                }

                if (commonLayer.TimeToHeal())
                {
                    return new AICoreActionEndStruct("wntHeal", true);
                }

                if (this.customNavigationPoint_0 != null && !this.customNavigationPoint_0.IsFreeById(this.botOwner_0.Id))
                {
                    this.customNavigationPoint_0 = null;
                }
                if (this.customNavigationPoint_0 != null && this.customNavigationPoint_0.CanIShootToEnemy && this.botOwner_0.Memory.IsInCover && this.botOwner_0.Memory.BotCurrentCoverInfo.CovPoint.Id != this.customNavigationPoint_0.Id && Time.time - this.botOwner_0.Memory.ComeToCoverTime > 3f && gclass363_0 != null)
                {
                    this.botOwner_0.Memory.Spotted(false, null, null);
                    this.botOwner_0.Memory.BotCurrentCoverInfo.SetCover(this.customNavigationPoint_0, true);
                    var gclass = gclass363_0;
                    if (gclass != null)
                    {
                        gclass.StartMoveToAttackPoint(botOwner_0.Id);
                    }
                    return new AICoreActionEndStruct("betterCover", true);
                }


                if (commonLayer.ShallGoNearBoss()) return new AICoreActionEndStruct("goNearBoss", true);

                if (base.method_7())
                {
                    return new AICoreActionEndStruct("EndHol", true);
                }
                if (!this.botOwner_0.Memory.IsInCover)
                {
                    return new AICoreActionEndStruct("notInCover", true);
                }
                EnemyInfo goalEnemy = this.botOwner_0.Memory.GoalEnemy;
                if (goalEnemy != null && goalEnemy.IsVisible && goalEnemy.CanShoot)
                {
                    return new AICoreActionEndStruct("CanShoot", true);
                }
                if (gclass363_0.IsHitted && commonLayer.coverType == "close")
                {
                    return new AICoreActionEndStruct("bossHit", true);
                }


                return aICoreActionEndStruct_1;
            }
            catch (Exception e)
            {
                Modules.Logger.LogError("EndHoldPosition Error");
                Modules.Logger.LogError(e);
                return new AICoreActionEndStruct("hpError", true);
            }
        }

        public CustomNavigationPoint GetClosestCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(centerPosition, searchRadius);

            return customNavigationPoint_0;
        }

        public CustomNavigationPoint GetClosestAttackCoverPoint(Vector3 centerPosition, float maxDistance = 150f)
        {
            customNavigationPoint_0 = commonLayer.GetClosestShootCover(centerPosition, maxDistance);
            return customNavigationPoint_0;
        }

        public CustomNavigationPoint GetApproachablePoint(bool inbetween = false)
        {
            customNavigationPoint_0 = commonLayer.GetApproachableCover(inbetween);
            return customNavigationPoint_0;

        }

        public CustomNavigationPoint GetClosestCoverPointBetween(Vector3 pointA, Vector3 pointB)
        {
            customNavigationPoint_0 = Utils.Covers.GetClosestCoverPointBetween(botOwner_0, pointA, pointB);
            if (customNavigationPoint_0 != null) botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
            return customNavigationPoint_0;
        }
    }
}
