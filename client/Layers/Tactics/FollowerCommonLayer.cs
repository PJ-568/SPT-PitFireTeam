using EFT;
using EFT.InventoryLogic;
using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.AI;

using friendlySAIN.Utils;
using friendlySAIN.Brains;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.Tactics
{
    /** 
     * This class is not meant to be used directly as a brain layer, but within one 
     * Follower Layer that holds common decisions
     * **/
    public class FollowerCommonLayer : BaseLogicLayerSimpleAbstractClass
    {

        private CustomNavigationPoint customNavigationPoint_0;
        private CustomNavigationPoint customNavigationPoint_1;
        private CustomNavigationPoint customNavigationPoint_2;
        private CustomNavigationPoint customNavigationPoint_3;

        public CustomNavigationPoint NavigationPoint2
        {
            get
            {
                return customNavigationPoint_2;
            }
        }

        public CustomNavigationPoint NavigationPoint1
        {
            get
            {
                return customNavigationPoint_1;
            }
        }
        public CustomNavigationPoint NavigationPoint3
        {
            get
            {
                return customNavigationPoint_3;
            }
        }
        public CustomNavigationPoint NavigationPoint
        {
            get
            {
                return customNavigationPoint_0;
            }
        }

        private float coverTimer_0 = 0f;
        private float coverTimer_1 = 0f;
        private float coverTimer_2 = 0f;
        private float coverTimer_3 = 0f;

        private float heal_time = 0f;
        private float heal_block_time = 0f;

        private float dangerTimer = 0f;
        private float dangerIgnoreEquipTimer = 0f;
        private bool dangerResult = false;
        private bool dangerIgnoreEquipResult = false;

        private float _lastHitTime = 0f;

        public float LastTimeHit
        {
            get
            {
                return _lastHitTime;
            }
        }

        private bool _gettingDamaged = false;

        public bool IsBeingDamaged
        {
            get
            {
                return _gettingDamaged;
            }
        }

        private bool _isTakingHeavyDamage = false;
        public bool TakingHeavyDamage
        {
            get => _isTakingHeavyDamage;
        }

        private List<(float time, float damage, EBodyPart part)> _recentHits = new List<(float time, float damage, EBodyPart part)>();
        private const float HitTrackingWindow = 3f; // Seconds
        private const float PanicDamageThreshold = 50f;
        private const int PanicHitCountThreshold = 2;
        private const float VitalDamageThreshold = 30f;

        private System.Timers.Timer _panicResetTimer;

        private GClass641.IBotTimer _damageTimer;

        public string coverType = "close";

        private bool ordersChanged = false;

        public bool OrderHasChangedRecently
        {
            get
            {
                return ordersChanged;
            }
        }

        private NavMeshPath _navMeshPath;

        public NavMeshPath NavMeshPath
        {
            get { return _navMeshPath; }
        }

        public readonly List<string> ordersIgnoreReasons = new List<string>
        {
            "healInCover",
            "heal",
            "usingStims",
            "runToHeal",
            "IsDamaged",
            "DogFight"
        };

        public readonly List<string> closeInDecisions = new List<string>
        {
            "getInCloseFast",
            "getInCloseSlow",
            "repositionFast",
            "reposition",
            "enemy.Search",
            "assaultApproach",
            "assaultRush"
        };


        private List<BotLogicDecision> coverDecisions = new List<BotLogicDecision>
        {
            BotLogicDecision.runToCover,
            BotLogicDecision.goToCoverPointTactical,
            BotLogicDecision.goToPoint,
            BotLogicDecision.search,
            (BotLogicDecision)CustomBotDecisions.attackRetreat,
            (BotLogicDecision)CustomBotDecisions.EnemySearch
        };

        public readonly List<BotLogicDecision> ordersIgnoreDecisions = new List<BotLogicDecision>
        {
            BotLogicDecision.dogFight,
            BotLogicDecision.suppressFire,
            BotLogicDecision.shootFromPlace,
            BotLogicDecision.shootFromCover,
            BotLogicDecision.healStimulators,
            BotLogicDecision.heal
        };

        public float coverSearchRadius
        {
            get
            {
                return Props.coverSearchRadius;
            }
        }

        public float sprintDistance
        {
            get
            {
                return Props.sprintDistance;
            }
        }

        public float regroupMinDistance
        {
            get => Props.regroupMinDistance;
        }

        public float searchRadius
        {
            get { return Props.searchRadius; }
        }

        public float nearSearchRadius
        {
            get { return Props.nearSearchRadius; }
        }

        public float bossInnerRadius
        {
            get
            {
                return Props.bossInnerRadius;
            }
        }

        public float bossOuterRadius
        {
            get
            {
                return Props.bossOuterRadius;
            }
        }

        public float bossMaxCoverDistance
        {
            get
            {
                return Props.bossMaxCoverDistance;
            }
        }
        public float bossMinCoverDistance
        {
            get
            {
                return Props.bossMinCoverDistance;
            }
        }

        private AICoreActionResultStruct<BotLogicDecision, StandardBrain>? currDecision = null;

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? CurrentDecision
        {
            get
            {
                return currDecision;
            }
        }

        private bool _reachedCover = false;

        private float _triedCover = 0f;

        public bool ReachedCover
        {
            get
            {
                return _reachedCover;
            }
        }

        public FollowerCommonLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            botOwner_0 = bot;
            _navMeshPath = new NavMeshPath();
        }
        public override bool ShallUseNow()
        {
            return true;
        }
        public override string Name()
        {
            return "FBCommon";
        }
        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.standBy, "decision.None");
        }

        public override void OnActivate()
        {
            botOwner_0.GetPlayer.BeingHitAction += BeingHitAction;
            base.OnActivate();
        }

        public override void Dispose()
        {
            botOwner_0.GetPlayer.BeingHitAction -= BeingHitAction;
            _damageTimer = null;
            base.Dispose();
        }

        private void BeingHitAction(DamageInfoStruct info, EBodyPart part, float arg3)
        {
            if (info.Player == null) return;

            _lastHitTime = Time.time;

            if (part == EBodyPart.Stomach || part == EBodyPart.Chest || part == EBodyPart.Head)
            {
                if (botOwner_0.Profile.Health.BodyParts.TryGetValue(part, out var bodyPart))
                {
                    float hp = bodyPart.Health.Current * 100 / bodyPart.Health.Maximum;

                    if (
                        (hp <= 65 && part == EBodyPart.Head) ||
                        (hp <= 55 && part == EBodyPart.Chest) ||
                        (hp > 0 && hp <= 35 && part == EBodyPart.Stomach)
                    )
                    {
                        _isTakingHeavyDamage = true;
                        if (_damageTimer != null) _damageTimer.Stop();

                        _damageTimer = Utils.Utils.SetTimeout(() =>
                        {
                            _isTakingHeavyDamage = false;
                            _damageTimer = null;

                        }, 100);

                    }

                }
            }

            float now = Time.time;
            float damage = info.Damage;

            _lastHitTime = now;

            _recentHits.Add((now, damage, part));
            _recentHits.RemoveAll(hit => now - hit.time > HitTrackingWindow);

            float totalDamage = 0f;
            int hitCount = 0;
            bool vitalHit = false;
            bool instantPanic = false;

            foreach (var hit in _recentHits)
            {
                totalDamage += hit.damage;
                hitCount++;
                if (IsVital(hit.part) && hit.damage >= VitalDamageThreshold)
                {
                    instantPanic = true;
                }
                if (IsVital(hit.part)) vitalHit = true;
            }

            if (
                instantPanic ||
                totalDamage >= PanicDamageThreshold ||
                (hitCount >= PanicHitCountThreshold && vitalHit)
            )
            {
                TriggerPanic();
            }
        }

        private bool IsVital(EBodyPart part)
        {
            return part == EBodyPart.Head || part == EBodyPart.Chest || part == EBodyPart.Stomach;
        }

        private void TriggerPanic()
        {
            if (_gettingDamaged) return; // Already panicking

            _gettingDamaged = true;

            // Logically lasts 2–3s, unless extended by more hits
            _panicResetTimer?.Stop();
            _panicResetTimer = new System.Timers.Timer(2500);
            _panicResetTimer.Elapsed += (s, e) =>
            {
                _gettingDamaged = false;
                _panicResetTimer?.Stop();
                _panicResetTimer?.Dispose();
                _panicResetTimer = null;
            };
            _panicResetTimer.AutoReset = false;
            _panicResetTimer.Start();
        }

        public bool HasBoss()
        {
            return Utils.Utils.HasBoss(botOwner_0);
        }

        public pitAIBossPlayer GetBoss()
        {
            return Utils.Utils.GetBoss(botOwner_0);
        }
        public bool ShallGoNearBoss()
        {
            if (!HasBoss() || coverType != "close") return false;

            EnemyInfo goalEnemy = this.botOwner_0.Memory.GoalEnemy;
            float bossDist = Vector3.Distance(botOwner_0.Position, GetBoss().Position);

            return bossDist > Mathf.Min(bossMaxCoverDistance, nearSearchRadius) && (goalEnemy == null || !goalEnemy.HaveSeen || (goalEnemy.HaveSeen && Time.time - goalEnemy.PersonalLastSeenTime > bossMinCoverDistance));
        }

        public void ResetTimer(string timer)
        {
            if (timer == "coverTimer_0")
                coverTimer_0 = 0f;
            else if (timer == "coverTimer_1")
                coverTimer_1 = 0f;
            else if (timer == "coverTimer_2")
                coverTimer_2 = 0f;
        }

        public void OrdersChanged()
        {
            ordersChanged = true;

            Utils.Utils.SetTimeout(() =>
            {
                ordersChanged = false;
            }, 1000);
        }

        public void OrderReset()
        {
            ordersChanged = false;
        }

        public bool IsEnemyLowThreat(bool ignoreEquip = false, float maximumEnemies = 2)
        {
            if (!ignoreEquip && dangerTimer > Time.time) return dangerResult;
            else if (ignoreEquip && dangerIgnoreEquipTimer > Time.time) return dangerIgnoreEquipResult;

            if (!botOwner_0.Memory.HaveEnemy) return true;

            if (!ignoreEquip)
            {
                dangerTimer = Time.time + 1f;
                dangerResult = botOwner_0.Memory.AttackImmediately && Utils.Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, botOwner_0.Memory.GoalEnemy.CurrPosition) <= maximumEnemies;

                return dangerResult;
            }
            else
            {
                dangerIgnoreEquipTimer = Time.time + 1f;
                dangerIgnoreEquipResult = Utils.Enemy.GetEnemiesAtLocation(botOwner_0, botOwner_0.Memory.GoalEnemy, botOwner_0.Memory.GoalEnemy.CurrPosition) < 3;

                return dangerIgnoreEquipResult;
            }
        }


        public void CoverType(string type)
        {
            coverType = type;
        }
        /** Find a shoot position that is closest to the enemy but at a minimum distance and maximum from the enemy **/
        // customNavigationPoint_1
        public CustomNavigationPoint GetClosestShootCover(Vector3 centerPosition, float maxDistance = 150f, bool inbetween = false)
        {
            if (coverTimer_1 > Time.time) return customNavigationPoint_1;

            coverTimer_1 = 1f + Time.time;

            ShootPointClass shootPointClass = botOwner_0.CurrentEnemyTargetPosition(true);

            float _weaponShootDistMaxSqr = botOwner_0.LookSensor.MaxShootDist * botOwner_0.LookSensor.MaxShootDist;

            customNavigationPoint_1 = Covers.GetClosestCoverPoint(botOwner_0, centerPosition, maxDistance, point =>
            {
                // does it need to be between the bot and the enemy
                if (inbetween && !Covers.IsPointBetween(point.Position, botOwner_0.Position, centerPosition)) return false;
                // has distance enough to shoot
                if ((point.Position - shootPointClass.Point).sqrMagnitude >= _weaponShootDistMaxSqr) return false;
                // can shoot from this point
                if (Utils.Utils.CanShootToTarget(shootPointClass, point, botOwner_0.LookSensor.Mask, false))
                {
                    point.CanIShootToEnemy = true;
                    return true;
                }
                return false;
            });//Covers.GetCover(botOwner_0, centerPosition, CoverSearchType.shoot_toCover_toBot_Distances, maxDistance);

            if (customNavigationPoint_1 != null) botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);

            botOwner_0.Memory.SetCoverPoints(customNavigationPoint_1);
            return customNavigationPoint_1;
        }

        /** Find a shooting cover that is the closest to the middle point between bot and enemy **/
        // customNavigationPoint_1
        public CustomNavigationPoint GetApproachableCover(bool inbetween = false)
        {
            if (coverTimer_3 > Time.time) return customNavigationPoint_1;

            coverTimer_3 = 1f + Time.time;
            coverTimer_1 = 0f;

            GetClosestShootCover((botOwner_0.Position + botOwner_0.Memory.GoalEnemy.CurrPosition) / 2f, 120f, inbetween);

            return customNavigationPoint_1;
        }
        /** Find the closest cover point to the given position **/
        // customNavigationPoint_2
        public CustomNavigationPoint GetClosestCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            if (coverTimer_2 > Time.time) return customNavigationPoint_2;

            coverTimer_2 = 1f + Time.time;

            CustomNavigationPoint point = Covers.GetClosestCoverPoint(botOwner_0, centerPosition, searchRadius);

            customNavigationPoint_2 = point;

            botOwner_0.Memory.SetCoverPoints(point);

            return customNavigationPoint_2;
        }

        /** Find closest cover point at the given position taking into cosideration the rest of the followers **/
        public CustomNavigationPoint GetClosestCoverPointGroup(Vector3 centerPosition, float searchRadius)
        {
            if (coverTimer_2 > Time.time) return customNavigationPoint_2;

            coverTimer_2 = 1.5f + Time.time;

            customNavigationPoint_2 = Utils.Covers.GetClosestCoverPoint(botOwner_0, centerPosition, searchRadius, null);

            return customNavigationPoint_2;

        }
        /** Find a random cover point at the given position, within the given radius **/
        // customNavigationPoint_0
        public CustomNavigationPoint GetCoverPoint(Vector3 centerPosition, float searchRadius)
        {

            if (coverTimer_0 > Time.time) return customNavigationPoint_0;

            coverTimer_0 = 1f + Time.time;

            CustomNavigationPoint point1 = Covers.GetCoverPoint(botOwner_0, centerPosition, searchRadius);

            customNavigationPoint_0 = point1;
            botOwner_0.Memory.SetCoverPoints(point1);
            return customNavigationPoint_0;

        }
        /** Find the closest safe cover point to the given position, within the given radius **/
        // customNavigationPoint_3
        public CustomNavigationPoint GetClosestSafeCoverPoint(Vector3 centerPosition, float searchRadius = 100f)
        {

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;

            CustomNavigationPoint point = Utils.Covers.GetClosestCoverPoint(botOwner_0, centerPosition, searchRadius, (CustomNavigationPoint pt) =>
            {
                bool good = true;

                // should not be seen by any enemy
                try
                {
                    foreach (var enemy in botOwner_0.EnemiesController.EnemyInfos)
                    {
                        if (enemy.Value.Person.HealthController.IsAlive && pt.CanIHideFromPos(10f, true, false, enemy.Value.Person.Transform.position))
                        {
                            good = false;
                        }
                    }
                }
                catch
                {
                    // some unknown error can happen on getting enemy position
                }

                return good;
            });

            customNavigationPoint_3 = point;

            if (point != null)
            {
                botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Ambush);
            }

            return customNavigationPoint_3;
        }
        /** Is point free by the followers group **/
        public bool IsPointFreeGroup(CustomNavigationPoint point)
        {
            if (!HasBoss()) return point.IsFreeById(botOwner_0.Id);

            bool isfree = true;

            foreach (var follower in GetBoss().Followers)
            {
                if (follower.Id != botOwner_0.Id && !point.IsFreeById(follower.Id))
                {
                    isfree = false;
                    break;
                }
            }
            return isfree;

        }

        public bool TimeToHeal()
        {
            return !botOwner_0.Memory.HaveEnemy || (Time.time - this.botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime >= 15f && (this.botOwner_0.Medecine.FirstAid.Have2Do || this.botOwner_0.Medecine.SurgicalKit.HaveWork));
        }

        public float GetNavDistance(Vector3 point)
        {
            return Utils.Utils.GetNavDistance(botOwner_0.GetPlayer.Transform.position, point, _navMeshPath);
        }

        public bool SprintDistance(Vector3 point, float? distance = null)
        {
            return !Utils.Utils.IsWithinDistance(point, botOwner_0.GetPlayer.Transform.position, distance.HasValue ? distance.Value : sprintDistance);
        }

        private float GetPartHP(EBodyPart part)
        {
            return 100f * botOwner_0.HealthController.GetBodyPartHealth(part, false).Normalized;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? DogFight(out CustomNavigationPoint navpoint)
        {
            navpoint = null;

            try
            {
                if (!botOwner_0.Memory.HaveEnemy || botOwner_0.Memory.GoalEnemy.Person == null || !botOwner_0.Memory.GoalEnemy.Person.HealthController.IsAlive) return null;
                if (botOwner_0.GetPlayer == null || botOwner_0.GetPlayer.Transform == null) return null;

                FollowerBrain followerBrain = botOwner_0.Brain.BaseBrain as FollowerBrain;

                if (followerBrain == null) return null;

                EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
                Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;

                float distance = goalEnemy.Distance;

                if (distance < 18f && distance > botOwner_0.Settings?.FileSettings?.Mind?.DOG_FIGHT_IN && goalEnemy.IsVisible)
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.DogFight, "cdg");

                var dogFight = botOwner_0.DogFight;
                BotDogFightStatus dogFightState = dogFight != null ? dogFight.DogFightState : BotDogFightStatus.none;
                if (dogFightState == BotDogFightStatus.dogFight)
                {
                    _reachedCover = false;
                    if (
                        followerBrain.defaultTactic != null && followerBrain.defaultTactic.ToLower() == "marksman" &&
                        botOwner_0.WeaponManager.Selector.LastEquipmentSlot == EFT.InventoryLogic.EquipmentSlot.FirstPrimaryWeapon &&
                        Utils.Enemy.Distance(botOwner_0) > Enemy.EnemyDistance.Mid
                    )
                    {
                        botOwner_0.DogFight.DogFightState = BotDogFightStatus.shootFromPlace;
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromPlace, "cdgfp");
                    }
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.dogFight, "cdg");
                }

                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = InFightLogic();

                if (aicoreActionResultStruct.HasValue)
                {
                    _reachedCover = false;
                    return aicoreActionResultStruct.Value;
                }

                bool covertried = false;
                Enemy.EnemyDistance enemyDistance = Enemy.Distance(botOwner_0);

                // Check if the enemy is visible and can be shot
                if (goalEnemy.IsVisible && goalEnemy.CanShoot)
                {

                    float head = GetPartHP(EBodyPart.Head);
                    float chest = GetPartHP(EBodyPart.Chest);
                    float stomach = GetPartHP(EBodyPart.Stomach);
                    float leftLeg = GetPartHP(EBodyPart.LeftLeg);
                    float rightLeg = GetPartHP(EBodyPart.RightLeg);
                    float leftArm = GetPartHP(EBodyPart.LeftArm);
                    float rightArm = GetPartHP(EBodyPart.RightArm);

                    float lowest = Mathf.Min(head, chest, stomach, leftLeg, rightLeg, leftArm, rightArm);

                    bool isClose = enemyDistance < Enemy.EnemyDistance.Mid;
                    bool isMid = !isClose;

                    bool critical = lowest < 30f;
                    bool injured = lowest < 60f && Time.time - LastTimeHit < 2f;

                    //  - retreat as we are getting damaged
                    if (critical || (injured && isClose))
                    {
                        // -- find cover point behind
                        customNavigationPoint_2 = GetClosestSafeCoverPoint(botPosition);
                        covertried = true;
                        // -- found nothing, fallback to any cover
                        if (customNavigationPoint_2 == null)
                        {
                            ResetTimer("coverTimer_2");
                            customNavigationPoint_2 = Covers.GetClosestCoverPoint(botOwner_0, botPosition, coverSearchRadius);
                        }

                        navpoint = customNavigationPoint_2;

                        if (navpoint != null)
                        {
                            // -- critical damage and enemy has enough distance, run for cover
                            if (critical && enemyDistance > Enemy.EnemyDistance.VeryClose)
                            {
                                _reachedCover = false;
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "damageCritical");
                            }
                            else
                            {
                                // -- else retreat while shooting
                                if (!goalEnemy.IsVisible) botOwner_0.Steering.LookToPoint(goalEnemy.GetCenterPart());
                                _reachedCover = false;
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "backOff");
                            }
                        }
                    }
                }

                // nowhere to go, keep shooting
                if (dogFightState == BotDogFightStatus.shootFromPlace)
                {
                    _reachedCover = false;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromPlace, "cdgfp");
                }

                if (goalEnemy.IsVisible && goalEnemy.VisibleOnlyBySense == EEnemyPartVisibleType.Visible && goalEnemy.CanShoot)
                {
                    // try to shoot while moving to some cover
                    if (!covertried)
                    {
                        customNavigationPoint_2 = GetClosestCoverPoint(botPosition, Utils.Props.nearSearchRadius);
                        // check that customNavigationPoint_2 is on relative same height position
                        if (customNavigationPoint_2 != null && Mathf.Abs(customNavigationPoint_2.Position.y - botPosition.y) <= 0.2)
                        {
                            _reachedCover = false;
                            botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Protect);

                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "relocate");
                        }
                    }
                    _reachedCover = false;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromPlace, "jklu1");
                }

            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[FollowerCommonLayer] DogFight error: {ex.Message}");
            }

            return null;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? NeedHeal(out CustomNavigationPoint navpoint)
        {
            navpoint = null;

            if (botOwner_0.Medecine == null) return null;

            if (!botOwner_0.Memory.HaveEnemy) heal_block_time = 0f;

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            // damaged and has healers - only in combat
            var stims = botOwner_0.Medecine?.Stimulators;
            bool shoulUseStim = stims?.HaveSmt == true && Time.time - stims.LastEndUseTime > 3f && stims?.CanUseNow() == true && botOwner_0.GetPlayer.HealthStatus != ETagStatus.Healthy;
            if (
                (
                    shoulUseStim &&
                    botOwner_0.Memory.HaveEnemy &&
                    !botOwner_0.Memory.GoalEnemy.IsVisible &&
                    Time.time - botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime > 1.5f
                ) ||
                botOwner_0.Medecine.Stimulators.Using
            )
            {
                _reachedCover = false;
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.healStimulators, "healQuick", null);
            }
            // Check if the bot needs to heal
            if (heal_block_time < Time.time &&
                (
                    botOwner_0.Medecine.FirstAid.Have2Do ||
                    botOwner_0.Medecine.SurgicalKit.HaveWork ||
                    botOwner_0.Medecine.FirstAid.Using ||
                    botOwner_0.Medecine.SurgicalKit.Using
                )
            )
            {
                if (
                    !botOwner_0.Memory.HaveEnemy ||
                    botOwner_0.Medecine.FirstAid.Using ||
                    botOwner_0.Medecine.SurgicalKit.Using
                )
                {
                    if (!botOwner_0.Memory.HaveEnemy) heal_block_time = Time.time;
                    _reachedCover = false;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.heal, "healInCover");
                }

                float lastSeen = botOwner_0.Memory.HaveEnemy ? Time.time - botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime : 0f;
                if (!botOwner_0.Memory.HaveEnemy || (!botOwner_0.Memory.GoalEnemy.IsVisible && lastSeen > 3f))
                {
                    // - close to the enemy, but safe enough to apply meds
                    if (botOwner_0.Memory.IsInCover && Enemy.DistanceProxy(botOwner_0, botPosition) > Enemy.ProxyDistance.VeryClose)
                    {
                        heal_time = Time.time;
                        _reachedCover = false;
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.heal, "healInCover");
                        // find some new cover, 
                    }
                    else
                    {
                        // - look for a safe cover
                        GetClosestSafeCoverPoint(botPosition);

                        navpoint = customNavigationPoint_3;

                        if (customNavigationPoint_3 != null)
                        {
                            _reachedCover = false;
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToHeal");
                        }
                        // - nothing found, no heal
                        else
                        {
                            heal_block_time = Time.time + 3f;
                            return null;
                        }
                    }
                }
                else if (lastSeen <= 3f)
                {
                    // not seeing the enemy and we are far enough
                    if (Enemy.DistanceProxy(botOwner_0, botPosition) > Enemy.ProxyDistance.Close)
                    {
                        // - heal if already in cover
                        if (botOwner_0.Memory.IsInCover)
                        {
                            heal_time = Time.time;
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.heal, "healInCover");
                        }
                        // - else look for cover with enough distance
                        GetClosestSafeCoverPoint(botPosition);

                        navpoint = customNavigationPoint_3;

                        if (customNavigationPoint_3 != null)
                        {
                            _reachedCover = false;
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToHeal");
                        }
                        // - nothing found, no heal
                        else
                        {
                            heal_block_time = Time.time + 3f;
                            return null;
                        }
                    }
                    // not seeing the enemy but we are close
                    else
                    {
                        // - look for a spot to heal
                        GetClosestSafeCoverPoint(botPosition);

                        navpoint = customNavigationPoint_3;

                        if (customNavigationPoint_3 != null)
                        {
                            if (SprintDistance(customNavigationPoint_3.Position))
                            {
                                _reachedCover = false;
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToHeal");
                            }
                            else
                            {
                                _reachedCover = false;
                                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "moveToHeal");
                            }
                        }
                        // - nothing found, no heal
                        else
                        {
                            heal_block_time = Time.time + 3f;
                            return null;
                        }
                    }
                }
                // we need to heal, but are seeing the enemy
                else
                {
                    // - look for a spot to heal

                    GetClosestCoverPoint(botPosition, coverSearchRadius);

                    navpoint = customNavigationPoint_2;

                    if (customNavigationPoint_2 != null)
                    {
                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Ambush);
                        if (SprintDistance(customNavigationPoint_2.Position) && Enemy.DistanceProxy(botOwner_0, botPosition) > Enemy.ProxyDistance.VeryClose)
                        {
                            _reachedCover = false;
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToHeal");
                        }
                        else
                        {
                            _reachedCover = false;
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "moveToHeal");
                        }
                    }
                    // - nothing found, do not heal
                    heal_block_time = Time.time + 3f;
                }

            }

            return null;
        }

        public bool IsHealing(out CustomNavigationPoint point, out AICoreActionResultStruct<BotLogicDecision, StandardBrain>? result)
        {
            point = null;
            result = null;
            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreHealResultStruct = NeedHeal(out customNavigationPoint_0);
            if (aicoreHealResultStruct != null)
            {
                point = customNavigationPoint_0;
                result = aicoreHealResultStruct;
                return true;
            }

            return false;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> HoldPositionFor(float timer, string reason = "wait4it")
        {
            Utils.Utils.SetTimeout(() =>
            {
                if (botOwner_0.BotState == EBotState.Active && !botOwner_0.IsDead && botOwner_0.Memory.HaveEnemy && !botOwner_0.Memory.GoalEnemy.IsVisible)
                    botOwner_0.Steering.LookToDirection(botOwner_0.Memory.GoalEnemy.CurrPosition - botOwner_0.GetPlayer.Transform.position, 90f);
            }, 100);
            _reachedCover = false;
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(timer), reason);
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetCloserToBoss(out CustomNavigationPoint navpoint)
        {
            Vector3 bossPosition = HasBoss() ? GetBoss().Position : botOwner_0.GetPlayer.Transform.position;
            BotRequest request = botOwner_0.BotRequestController.CurRequest;

            ordersChanged = false;

            if (request != null)
            {
                ResetTimer("coverTimer_2");
            }

            GetClosestCoverPointGroup(bossPosition, bossInnerRadius);

            navpoint = customNavigationPoint_2;

            if (customNavigationPoint_2 != null)
            {
                _reachedCover = false;
                botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);

                if (!SprintDistance(customNavigationPoint_2.Position))
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.goToCoverPointTactical, "moveCloserToBoss");
                else
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "moveCloserToBossFast");
                }

            }

            _reachedCover = false;
            return BotLogicDecisions.RegroupToBoss(botOwner_0);
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> MarksManFight(out CustomNavigationPoint navpoint)
        {

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            bool enemyVisible = botOwner_0.Memory.GoalEnemy.IsVisible;
            bool canShoot = botOwner_0.Memory.GoalEnemy.CanShoot;
            bool haveSeen = botOwner_0.Memory.GoalEnemy.HaveSeen;
            float lastSeenTime = botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime;

            // If the enemy is a sniper and visible, and the bot is in cover, shoot from cover
            if (enemyVisible && botOwner_0.Memory.IsInCover)
            {
                navpoint = null;

                if (canShoot)
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "sfc");

                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(2f, 5f)), "wait4it");
            }

            // If the enemy is a sniper and visible, try to find a cover point from which you can shoot
            if (enemyVisible)
            {
                GetClosestShootCover(botPosition, 100f); // Find cover close to the bot's position

                navpoint = customNavigationPoint_1;

                if (customNavigationPoint_1 != null)
                {
                    if (SprintDistance(customNavigationPoint_1.Position))
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "relocateFast");
                    }
                }
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "reposition");
            }

            // If the sniper is not visible, try to find a cover point closer to the bot's position
            if (!enemyVisible)
            {
                GetClosestCoverPoint(botPosition, coverSearchRadius);

                navpoint = customNavigationPoint_2;

                if (customNavigationPoint_2 != null)
                {
                    botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "relocateFast");
                }
            }

            navpoint = null;

            if (coverType == "close")
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverToCover");
            else
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(2f, 5f)), "wait4it");
        }

        public bool UnderFireTakeCover(out AICoreActionResultStruct<BotLogicDecision, StandardBrain>? decision)
        {
            decision = null;
            if (_triedCover < Time.time + 5f) return false;
            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;

            // is under fire ? - try to get in cover
            if (
                !botOwner_0.Memory.IsInCover &&
                (
                    (botOwner_0.Memory.HaveEnemy && (!botOwner_0.Memory.GoalEnemy.IsVisible || !botOwner_0.Memory.GoalEnemy.CanShoot) && (botOwner_0.Memory.IsUnderFire || base.method_12(2f))) ||
                    (
                        IsBeingDamaged &&
                        (
                            Enemy.Distance(botOwner_0) > Enemy.EnemyDistance.VeryClose ||
                            (botOwner_0.Memory.GoalEnemy.Person != null && botOwner_0.Memory.GoalEnemy.Person.IsAI && botOwner_0.Memory.GoalEnemy.Person.AIData.BotOwner.Memory.IsInCover)
                        )
                    )
                )
            )
            {
                _triedCover = Time.time + 5f;
                GetCoverPoint(botPosition, Props.coverSearchRadius);

                if (customNavigationPoint_0 != null)
                {
                    if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy.CanShoot)
                        decision = new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "goToCover");
                    else
                        decision = new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToCover");

                    return true;
                }
            }

            return false;
        }
        public AICoreActionEndStruct? ShallEndCurrentDecisionCommon(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {

            AICoreActionEndStruct? result = null;

            if (botOwner_0.Medecine.FirstAid.Using || botOwner_0.Medecine.SurgicalKit.Using) return new AICoreActionEndStruct("healing", false);

            if (curDecision.Action == BotLogicDecision.suppressGrenade && botOwner_0.WeaponManager.Grenades.ThrowindNow)
            {
                return new AICoreActionEndStruct("grenade.Throw", false);
            }

            if (
                curDecision.Action == (BotLogicDecision)CustomBotDecisions.MoveToPoint
            )
            {
                if (!botOwner_0.Memory.HaveEnemy) return new AICoreActionEndStruct("enemy.None", true);
                if (botOwner_0.Memory.GoalEnemy.CanShoot) return new AICoreActionEndStruct("enemy.Shoot", true);

                if (!(botOwner_0.BotRequestController.CurRequest != null &&
                        (botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.goToPoint ||
                        botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.followMe)
                     )
                   )
                    return aICoreActionEndStruct;

                return aICoreActionEndStruct_1;
            }

            if (
                curDecision.Action == (BotLogicDecision)CustomBotDecisions.EnemySearch
            )
            {
                result = EndEnemySearch();
            }

            else if (
                curDecision.Action == (BotLogicDecision)CustomBotDecisions.CoverToCover ||
                curDecision.Action == (BotLogicDecision)CustomBotDecisions.GuardToCover
            )
            {
                result = EndCoverToCover();
            }

            else if (closeInDecisions.Contains(curDecision.Reason))
            {
                result = EndGetInClose();
            }

            else if (curDecision.Action == BotLogicDecision.runToCover)
            {
                result = EndRunToCover();
            }
            else if (
                curDecision.Action == BotLogicDecision.goToCoverPointTactical ||
                curDecision.Action == BotLogicDecision.goToPoint
            )
            {
                result = EndGoToPoint();
            }

            else if (curDecision.Action == (BotLogicDecision)CustomBotDecisions.attackRetreat)
            {
                if (method_3())
                    result = new AICoreActionEndStruct("dog", true);
                else if (botOwner_0.Memory.IsInCover)
                    result = new AICoreActionEndStruct("inCvr", true);
                else
                    result = aICoreActionEndStruct_1;
            }

            if (result.HasValue && result.Value.Value && botOwner_0.Memory.IsInCover && coverDecisions.Contains(curDecision.Action))
            {
                _reachedCover = true;
            }

            return result;
        }
        /** Shall end the current decision for followers with tactic set to "Assist" **/
        public AICoreActionEndStruct? ShallEndCurrentDecisionAllies(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision, bool? ordersChanged = null)
        {
            if (!botOwner_0.Medecine.FirstAid.Using && !botOwner_0.Medecine.SurgicalKit.Using) return null;

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return aICoreActionEndStruct;
            }

            List<BotLogicDecision> breakOffContactDecision = new List<BotLogicDecision>
            {
                BotLogicDecision.holdPosition,
                BotLogicDecision.lay,
                BotLogicDecision.shootFromPlace,
                BotLogicDecision.shootFromCover
            };

            if (breakOffContactDecision.Contains(curDecision.Action) && _isTakingHeavyDamage && botOwner_0.Memory.HaveEnemy && Vector3.Distance(botOwner_0.GetPlayer.Transform.position, botOwner_0.Memory.GoalEnemy.CurrPosition) > 25f)
            {
                return new AICoreActionEndStruct("contact.Break", true);
            }

            bool ordchanged = ordersChanged.HasValue ? ordersChanged.Value : this.ordersChanged;

            // orders changed
            if (ordchanged &&
                !ordersIgnoreReasons.Contains(curDecision.Reason) &&
                !ordersIgnoreDecisions.Contains(curDecision.Action) &&
                (
                    !botOwner_0.Memory.HaveEnemy ||
                    !botOwner_0.Memory.GoalEnemy.CanShoot
                )
            )
            {
                OrderReset();
                return new AICoreActionEndStruct("orders.Received", true);
            }

            AICoreActionEndStruct? shallEndCommon = ShallEndCurrentDecisionCommon(curDecision);

            if (shallEndCommon.HasValue) return shallEndCommon.Value;

            return null;
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            currDecision = nextDecision;
            base.DecisionChanged(prevDecision, nextDecision);
        }

        public void EndRegroupRequest()
        {
            if (
                botOwner_0.BotRequestController.CurRequest != null &&
                botOwner_0.BotRequestController.CurRequest.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup
            )
            {
                botOwner_0.BotRequestController.CurRequest.Complete();
            }
        }

        public AICoreActionEndStruct EndGetInClose()
        {

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (botOwner_0.Mover.IsComeTo(0.5f, false))
            {
                return new AICoreActionEndStruct("point.Reached", true);
            }


            return base.EndRunToCover();
        }

        public AICoreActionEndStruct EndEnemySearch()
        {
            if (ordersChanged)
                return new AICoreActionEndStruct("search.End", true);

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (Time.time - LastTimeHit <= 0.5f)
            {
                return new AICoreActionEndStruct("enemy.ShotMe", true);
            }

            if (Utils.Enemy.Distance(botOwner_0) <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionEndStruct("enemy.Close", true);
            }

            return aICoreActionEndStruct;
        }

        public AICoreActionEndStruct EndCoverToCover()
        {
            if (ordersChanged)
                return new AICoreActionEndStruct("orders.Received", true);

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            return aICoreActionEndStruct;
        }

        public override AICoreActionEndStruct EndRunToEnemy()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            return base.EndRunToEnemy();
        }

        public override AICoreActionEndStruct EndGoToEnemy()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            return base.EndGoToEnemy();
        }

        private void HealUnstuck()
        {
            if (botOwner_0.Medecine.FirstAid.Using) botOwner_0.Medecine.FirstAid.CancelCurrent();
            else if (botOwner_0.Medecine.SurgicalKit.Using) botOwner_0.Medecine.SurgicalKit.CancelCurrent();

            var player = botOwner_0.GetPlayer;
            foreach (var part in GClass3058.RealBodyParts)
            {
                if (player.ActiveHealthController.IsBodyPartBroken(part)) player.ActiveHealthController.RemoveNegativeEffects(part);
                if (player.ActiveHealthController.IsBodyPartDestroyed(part)) player.ActiveHealthController.RestoreBodyPart(part, 0);
            }

            botOwner_0.AIData.Player.ActiveHealthController.RestoreFullHealth();

            (botOwner_0.Brain.BaseBrain as FollowerBrain).HandsReset();
            botOwner_0.WeaponManager.Selector.TakePrevWeapon();

            if (botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon)
            {
                botOwner_0.WeaponManager.Selector.TryChangeToMain();
            }
        }
        public override AICoreActionEndStruct EndHeal()
        {
            if (!botOwner_0.Medecine.FirstAid.Have2Do && !botOwner_0.Medecine.SurgicalKit.HaveWork)
            {
                HealUnstuck();

                heal_block_time = Time.time + 5f;

                return new AICoreActionEndStruct("EndHeal", true);
            }
            else if (heal_time + (botOwner_0.Medecine.SurgicalKit.Using ? 20f : 7f) < Time.time)
            {
                HealUnstuck();

                heal_block_time = Time.time + 5f;

                return new AICoreActionEndStruct("EndHealTimer", true);
            }

            return aICoreActionEndStruct_1;
        }

        public override AICoreActionEndStruct EndStimulators()
        {

            if (!this.botOwner_0.Medecine.Stimulators.Using || heal_time > Time.time + 5f)
            {
                if (this.botOwner_0.Medecine.Stimulators.Using) this.botOwner_0.Medecine.Stimulators.CancelCurrent();
                return new AICoreActionEndStruct("EndHealTimer", true);
            }

            return base.EndStimulators();
        }

        public override AICoreActionEndStruct EndTakeItem()
        {
            return new AICoreActionEndStruct("enemy.Present", true);
        }

        public override AICoreActionEndStruct EndGoToPoint()
        {
            if (ordersChanged || !botOwner_0.Memory.HaveEnemy)
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("EndGoTo", true);
            }

            if (botOwner_0.Memory.GoalEnemy.IsVisible && botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.LookSensor.EnoughDistToShoot(out var info))
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            if (botOwner_0.GoToSomePointData.IsCome())
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("point.Reached", true);
            }

            return base.EndGoToPoint();
        }


        public override AICoreActionEndStruct EndRunToCover()
        {
            if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.BewareGrenade.SawGrenadeSoFar(5f))
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("saw grenade", true);
            }
            if (botOwner_0.Memory.IsInCover)
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("InCover", true);
            }
            if (!botOwner_0.CanSprintPlayer)
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("CanSprintPl", true);
            }
            if (base.method_3())
            {
                EndRegroupRequest();
                return new AICoreActionEndStruct("StartD", true);
            }
            return aICoreActionEndStruct_1;
        }

    }
}
