using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;
using UnityEngine;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.Tactics
{
    /** 
     * This class is not meant to be used directly as a brain layer, but within one 
     * Sniper fight layer based on BirdEye's fight logic
     * **/
    internal class FollowerSniperLayer : GClass73
    {

        protected float coverTimer = 0f;
        protected float holdTimer = 0f;

        private FollowerCommonLayer commonLayer;

        public CustomNavigationPoint NavigationPoint
        {
            get
            {
                return customNavigationPoint_0;
            }
        }

        public FollowerCommonLayer CommonLayer { get { return commonLayer; } }
        public FollowerSniperLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            commonLayer = new FollowerCommonLayer(bot, priority);
        }

        public override void OnActivate()
        {
            base.OnActivate();
            commonLayer?.OnActivate();
        }
        public override void Dispose()
        {
            base.Dispose();
            commonLayer?.Dispose();
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

        // dummy 
        public override bool ShallUseNow()
        {
            return true;
        }

        public override ShootPointClass GetShootPoint()
        {
            return botOwner_0.CurrentEnemyTargetPosition(true);
        }
        // dummy 
        public override string Name()
        {
            return "FBSniper";
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return commonLayer.HoldPositionFor(Time.time + Utils.Utils.Random(1f, 2f));
            }

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            bool enemyVisible = botOwner_0.Memory.GoalEnemy.IsVisible;

            if (enemyVisible)
            {
                // enemy visible (can't shoot) and we are not in cover
                if (!botOwner_0.Memory.IsInCover)
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

                    // - find cover to shoot from
                    customNavigationPoint_0 = Utils.Covers.ClosestPoint(botOwner_0.Id, botPosition, botPosition, shootCovers, point =>
                    {
                        return true;
                    });

                    // - no attack cover, just find a cover 
                    if (customNavigationPoint_0 == null)
                    {
                        customNavigationPoint_0 = Utils.Covers.ClosestPoint(botOwner_0.Id, botPosition, botPosition, hideCovers, point =>
                        {
                            return true;
                        });
                    }

                    if (customNavigationPoint_0 != null && coverTimer < Time.time)
                    {
                        if (!commonLayer.SprintDistance(customNavigationPoint_0.Position, 25f))
                        {
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "relocate");
                        }
                        coverTimer = Time.time + Utils.Utils.Random(3f, 5f);
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "relocateFast");
                    }
                    // - found nothing, fallback
                    if (holdTimer < Time.time)
                    {
                        float timer = Utils.Utils.Random(2f, 5f);
                        holdTimer = Time.time + timer + Utils.Utils.Random(2f, 3f);
                        return commonLayer.HoldPositionFor(timer);
                    }

                    // - alternative fallback
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.dogFight, "dgf");

                }
                // enemy visible and in cover
                else
                {
                    // - try to shot enemy
                    if (botOwner_0.Memory.CurCustomCoverPoint != null && botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                    // - else find better spot
                    else
                    {
                        GetClosestAttackCoverPoint(botPosition);
                        if (customNavigationPoint_0 != null && coverTimer < Time.time)
                        {
                            coverTimer = Time.time + Utils.Utils.Random(3f, 5f);
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToCoverPointTactical, "relocate");
                        }
                    }

                    // -- fallback #1, just wait
                    if (holdTimer < Time.time)
                    {
                        float timer = Utils.Utils.Random(2f, 5f);
                        holdTimer = Time.time + timer + Utils.Utils.Random(3f, 5f);
                        return commonLayer.HoldPositionFor(timer);
                    }

                    // - fallback #2
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.SniperSearch, "sniper.Search");
                }
            }
            // enemy not visible
            else
            {
                // - look for a shooting spot
                GetClosestAttackCoverPoint(botPosition);

                if (customNavigationPoint_0 != null && coverTimer < Time.time)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToCoverPointTactical, "reposition");
                }
                // -- fallback #1, just wait
                if (holdTimer < Time.time)
                {
                    float timer = Utils.Utils.Random(2f, 5f);
                    holdTimer = Time.time + timer + Utils.Utils.Random(3f, 5f);
                    return commonLayer.HoldPositionFor(timer);
                }

                // - fallback #2, search for a sniping spot
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.SniperSearch, "sniper.Search");
            }
        }

        /**
         * Have Sniper switch to secondary weapon (if avaialable) if he is getting into close combat
         **/
        public void CheckCanSwitchToSecondary(AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision, Utils.Enemy.EnemyDistance enemyDistance)
        {
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            bool enemyClose = enemyDistance == Utils.Enemy.EnemyDistance.Close;
            bool enemyVeryClose = enemyDistance <= Utils.Enemy.EnemyDistance.VeryClose;
            // enemy very close, switch to close combat ASAP
            if (goalEnemy != null && enemyVeryClose)
            {
                if (
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon &&
                    botOwner_0.WeaponManager.Selector.CanChangeToSecondWeapons &&
                    (
                        !botOwner_0.Memory.GoalEnemy.HaveSeen ||
                        Time.time - botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime > 1.5f
                    )
                )
                {
                    Weapon SecondaryWeapon = botOwner_0.GetPlayer.InventoryController.Inventory.Equipment.GetSlot(EquipmentSlot.SecondPrimaryWeapon).ContainedItem as Weapon;
                    if (SecondaryWeapon != null && SecondaryWeapon.GetCurrentMagazine() != null && SecondaryWeapon.GetCurrentMagazine().Cartridges.Count > 0)
                    {
                        botOwner_0.WeaponManager.Selector.TryChangeWeapon(true);
                    }
                }
            }
            else if (goalEnemy != null && customNavigationPoint_0 != null)
            {
                // switch to secondary weapon if we are getting closer to the enemy
                var proxydist = Utils.Enemy.DistanceProxy(botOwner_0, customNavigationPoint_0.Position);
                if (
                    !botOwner_0.Memory.GoalEnemy.IsVisible &&
                        (
                            decision.Reason == "repositionFast" ||
                            decision.Reason == "reposition" ||
                            enemyClose
                        )
                    &&
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.SecondPrimaryWeapon &&
                    botOwner_0.WeaponManager.Selector.CanChangeToSecondWeapons &&
                    proxydist < Utils.Enemy.ProxyDistance.Mid && proxydist > Utils.Enemy.ProxyDistance.VeryClose
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeWeapon(true);

                }
                // switch back to sniper if we are moving to a sniper shot
                else if (
                    Utils.Enemy.DistanceProxy(botOwner_0, customNavigationPoint_0.Position) >= Utils.Enemy.ProxyDistance.Mid &&
                    !botOwner_0.Memory.GoalEnemy.IsVisible &&
                        (
                            decision.Reason == "repositionFast" ||
                            decision.Reason == "reposition" ||
                            decision.Reason == "relocateFast" ||
                            decision.Reason == "sniper.Search"
                        )
                    &&
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeToMain();
                }
            }
        }
        public AICoreActionEndStruct EndSniperSearch()
        {
            if (commonLayer.OrderHasChangedRecently)
                return new AICoreActionEndStruct("search.End", true);

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot)
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

        public override AICoreActionEndStruct EndGoToPoint()
        {

            if (commonLayer.CurrentDecision.HasValue && commonLayer.CurrentDecision.Value.Reason == "relocateFast")
            {
                if (botOwner_0.Memory.GoalEnemy.CanShoot)
                {
                    return new AICoreActionEndStruct("enemy.canSh", true);
                }

                return base.EndGoToPoint();
            }
            return commonLayer.EndGoToPoint();
        }

        protected virtual void GetClosestAttackCoverPoint(Vector3 centerPosition)
        {
            customNavigationPoint_0 = commonLayer.GetClosestShootCover(centerPosition, 150f);
        }

        protected virtual void GetClosestCoverPoint(Vector3 centerPosition, float searchRadius, float safeDistance = 5f, Func<CustomNavigationPoint, bool> extraChecks = null)
        {
            customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(centerPosition, searchRadius);
        }
    }
}
