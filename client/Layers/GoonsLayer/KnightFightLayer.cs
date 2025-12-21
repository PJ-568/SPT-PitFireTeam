using EFT;
using EFT.InventoryLogic;

using System;
using UnityEngine;

using friendlySAIN.Brains;
using friendlySAIN.Layers.Tactics;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.GoonsLayer
{
    /**
     * Ovewrite the fight layer for Knight to user our cover system and stay around the player boss
     */
    internal class KnightFightLayer : GClass77
    {

        protected float coverTimer = 0f;

        protected readonly float sprintDistance = 15f;

        protected readonly float fightRange = 50f;
        protected readonly float fightLongRange = 100f;

        protected bool bool_15;

        protected bool bool_16;

        protected bool bool_17;

        protected float float_63 = 0f;
        protected float float_62 = 0f;
        protected float float_69 = 0f;
        protected float float_72 = 0f;

        protected int int_17 = 0;

        protected FollowerCommonLayer commonLayer;
        protected FollowerPusherLayer pusherLayer;
        protected FollowerGuard guardLayer;

        private float hadEnemy = 0f;

        private float recalled = 0f;

        public KnightFightLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            pusherLayer = new FollowerPusherLayer(bot, priority);
            commonLayer = pusherLayer.CommonLayer;
            guardLayer = new FollowerGuard(bot, priority, pusherLayer);
        }
        public override void OnActivate()
        {
            pusherLayer?.OnActivate();
            base.OnActivate();

            if (botOwner_0.WeaponManager.Grenades != null)
            {
                botOwner_0.WeaponManager.Grenades.OnGrenadeThrowStart += OnThrowGrenade;
            }

            if (botOwner_0.WeaponManager.Grenades != null) botOwner_0.WeaponManager.Grenades.OnGrenadeThrowStart -= OnThrowGrenade;

            if (gclass428_0 == null)
            {
                gclass428_0 = new GClass428(botOwner_0, botOwner_0.Boss);
            }
        }

        public override void Dispose()
        {
            pusherLayer?.Dispose();
            base.Dispose();
        }

        public void OnThrowGrenade()
        {
            float killa_AFTER_GRENADE_SUPPRESS_DELAY = botOwner_0.Settings.FileSettings.Boss.KILLA_AFTER_GRENADE_SUPPRESS_DELAY;
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            if (killa_AFTER_GRENADE_SUPPRESS_DELAY > 0f && goalEnemy != null && !goalEnemy.CanShoot)
            {
                nullable_0 = new BotLogicDecision?(BotLogicDecision.holdPosition);
                HoldFor(killa_AFTER_GRENADE_SUPPRESS_DELAY);
            }
        }

        public override bool ShallUseNow()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                if (
                        botOwner_0.BotRequestController.CurRequest != null &&
                        (botOwner_0.BotRequestController.CurRequest.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup ||
                        botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.attackClose)
                    )
                {
                    botOwner_0.BotRequestController.CurRequest.Complete();
                }

                if (hadEnemy > Time.time)
                {
                    return true;
                }

                return false;
            }

            if (!bool_15)
            {
                bool_15 = true;
                botOwner_0.Brain.BaseBrain.OnLayerChangedTo += OnLayerChanged;
            }

            hadEnemy = Time.time + 1f;

            return true;
        }

        public new void OnLayerChanged(AICoreLayerClass<BotLogicDecision> layer)
        {
            this.int_17 = 0;
            if (layer == this)
            {
                this.float_63 = Time.time;
                return;
            }
            this.float_63 = -1000f;
        }
        protected bool HasBoss()
        {
            return commonLayer.HasBoss();
        }

        protected pitAIBossPlayer GetBoss()
        {
            return commonLayer.GetBoss();
        }

        public void OrdersChanged()
        {
            pusherLayer.OrdersChanged();
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            pusherLayer.DecisionChanged(prevDecision, nextDecision);

            base.DecisionChanged(prevDecision, nextDecision);
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                // is still healing?
                if (commonLayer.IsHealing(out customNavigationPoint_0, out var decision))
                {
                    hadEnemy = Time.time + 1f;
                    return decision.Value;
                }

                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.holdPosition, "enemy.None");
            }

            // is in dogfight?
            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = commonLayer.DogFight(out customNavigationPoint_0);
            if (aicoreActionResultStruct != null) return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;

            // needs healing?
            aicoreActionResultStruct = commonLayer.NeedHeal(out customNavigationPoint_0);
            if (aicoreActionResultStruct != null) return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;

            // player requests?
            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? preFightDecision = KnightPreFight();
            if (preFightDecision != null) return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)preFightDecision;

            if (commonLayer.ReachedCover)
            {
                return commonLayer.HoldPositionFor(Utils.Utils.Random(2f, 3f));
            }

            // do not go after distant enemies
            if (Utils.Enemy.Distance(botOwner_0) >= Utils.Enemy.EnemyDistance.Far)
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverBoss");
            }

            try
            {
                return KnightFight();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("KnightFight Error: " + ex.Message);
                Modules.Logger.LogInfo("Trace: " + ex.StackTrace);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(1f, 2f)), "decision.Error");
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> KnightAssault()
        {
            bool enemyVisible = botOwner_0.Memory.GoalEnemy.IsVisible;
            Utils.Enemy.EnemyDistance distanceToEnemy = Utils.Enemy.Distance(botOwner_0);

            // If the enemy is visible
            if (enemyVisible)
            {
                // If the enemy is close or mid-range
                if (distanceToEnemy <= Utils.Enemy.EnemyDistance.Mid && commonLayer.IsEnemyLowThreat(false, 2))
                {
                    // Rush towards the enemy
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToEnemy, "assaultRush");
                }
                else
                {
                    // Find a cover point closer to the enemy
                    GetClosestAttackCoverPoint((botOwner_0.Position + botOwner_0.Memory.GoalEnemy.EnemyLastPosition) / 2f);
                    if (customNavigationPoint_0 != null)
                    {
                        // Move towards the cover point while suppressing the enemy
                        botOwner_0.Steering.LookToPoint(botOwner_0.Memory.GoalEnemy.GetCenterPart());
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMovingWithSuppress, "assaultApproach");
                    }
                    else if (commonLayer.IsEnemyLowThreat(false, 2))
                    {
                        // No cover point found, move towards the enemy while suppressing
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToEnemy, "assaultRush");
                    }
                }
            }
            // If the enemy is not visible
            else
            {
                // Find a cover point closer to the enemy's last known position
                GetClosestAttackCoverPoint((botOwner_0.Position + botOwner_0.Memory.GoalEnemy.EnemyLastPosition) / 2f);
                if (customNavigationPoint_0 != null)
                {
                    // Move towards the cover point while suppressing
                    botOwner_0.Steering.LookToPoint(botOwner_0.Memory.GoalEnemy.GetCenterPart());
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMovingWithSuppress, "assaultApproach");
                }
                else if (commonLayer.IsEnemyLowThreat(false, 2))
                {
                    // No cover point found, move towards the enemy's last known position while suppressing
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToEnemy, "assaultRush");
                }


            }

            return pusherLayer.EngageEnemy();
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? KnightPreFight()
        {

            BotRequest request = botOwner_0.BotRequestController.CurRequest;


            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            Vector3 bossPosition = request != null ? botOwner_0.BotRequestController.CurRequest.Requester.Position : botPosition;

            bool shouldRegroup = request != null && request.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup;

            // player needs help or has call for a regroup
            if (
                (shouldRegroup &&
                Utils.Utils.GetNavDistance(botPosition, bossPosition) > commonLayer.regroupMinDistance) ||
                recalled > Time.time
            )
            {
                if (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.CanShoot)
                {
                    recalled = Time.time + 2f;
                    if (shouldRegroup) request.Complete();
                    return commonLayer.GetCloserToBoss(out customNavigationPoint_0);
                }
                else
                {
                    botOwner_0.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                    request.Complete();
                }
            }

            // is under fire ? - try to get in cover
            if (
                !shouldRegroup && commonLayer.UnderFireTakeCover(out var coverDecision)
            )
            {
                return coverDecision.Value;
            }

            // come here request during fights
            if (request != null && request.BotRequestType == BotRequestType.followMe)
            {
                if (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.IsVisible)
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:comeHere");
                else
                    request.Complete();
            }

            // go there request during fights
            if (request != null && request.BotRequestType == BotRequestType.goToPoint)
            {
                if (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.IsVisible)
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:goCheck");
                else
                    request.Complete();
            }

            // player requested a suppression fire
            if (request != null && request.BotRequestType == BotRequestType.suppressionFire)
            {
                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? suppress = guardLayer.SuppressFire(false);
                if (suppress.HasValue) return suppress.Value;
            }

            // do not pursue a marksman
            if (botOwner_0.Memory.GoalEnemy.Owner.IsRole(WildSpawnType.marksman))
            {
                return commonLayer.MarksManFight(out customNavigationPoint_0);
            }

            // player suggested to do a push
            if (request != null && request.BotRequestType == BotRequestType.attackClose)
            {
                AICoreActionResultStruct<BotLogicDecision, StandardBrain> forcePush = pusherLayer.EngageEnemy(true);
                customNavigationPoint_0 = pusherLayer.NavigationPoint;
                return forcePush;
            }

            return null;
        }

        /** Adaptation of original GetDecision from GClass65 */
        public AICoreActionResultStruct<BotLogicDecision, StandardBrain>? KnightBaseDecision()
        {
            if (method_24() && botOwner_0.Brain.LastDecision != null)
            {
                BotLogicDecision? lastDecision = botOwner_0.Brain.LastDecision;
                if (!(lastDecision.GetValueOrDefault() == (BotLogicDecision)CustomBotDecisions.attackRetreat & lastDecision != null))
                {
                    if (botOwner_0.Memory.GoalEnemy != null && botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.Memory.GoalEnemy.IsVisible)
                    {
                        return commonLayer.DogFight(out customNavigationPoint_0);
                    }

                    GetClosestAttackCoverPoint((botOwner_0.Position + botOwner_0.Memory.GoalEnemy.EnemyLastPosition) / 2f);
                    if (customNavigationPoint_0 != null)
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "enemyNear");
                }
            }

            bool_16 = false;
            bool_17 = false;
            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;
            if (float_63 + 10f < Time.time && botOwner_0.Memory.GoalEnemy != null && !botOwner_0.Memory.GoalEnemy.CanShoot)
            {
                if (goalEnemy != null)
                {
                    if (goalEnemy.IsVisible)
                    {
                        goto IL_19D;
                    }
                    BotLogicDecision? lastDecision = botOwner_0.Brain.LastDecision;
                    if (lastDecision.GetValueOrDefault() == BotLogicDecision.heal & lastDecision != null)
                    {
                        goto IL_19D;
                    }
                }
                if (botOwner_0.Medecine.FirstAid.Have2Do && botOwner_0.Memory.IsInCover && botOwner_0.Memory.LastEnemyTimeSeen + 6f < Time.time)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.heal, "HealInCover");
                }

            IL_19D:
                return KnightAssault();
            }

            int num = (botOwner_0.BotsGroup.MembersCount > 1) ? 1 : 2;
            if (int_17 >= num)
            {
                int_17 = 0;
                return commonLayer.HoldPositionFor(7f, "bad covers");
            }
            float_62 = Time.time;


            if (method_13())
            {
                if (method_17(true))
                {
                    GetClosestCoverPoint(botOwner_0.GetPlayer.Transform.position, fightRange);
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "loseTarget");
                }

                if (botOwner_0.WeaponManager.Grenades.HaveGrenadeOfType(ThrowWeapType.smoke_grenade))
                {
                    AIGreanageThrowData aigreanageThrowData = new AIGreanageThrowData();
                    aigreanageThrowData.Direction = botOwner_0.LookDirection;
                    aigreanageThrowData.Ang = 30f;
                    aigreanageThrowData.Force = 6f;
                    aigreanageThrowData.GrenadeType = new ThrowWeapType?(ThrowWeapType.smoke_grenade);
                    botOwner_0.WeaponManager.Grenades.SetThrowData(aigreanageThrowData);

                    botOwner_0.WeaponManager.Grenades.DoThrow();
                    var brain = (botOwner_0.Brain.BaseBrain as FollowerBrain);
                    if (!brain.IsThrowingGrenade) brain.OnThrow();

                    float_69 = Time.time;
                    float_72 = Time.time;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.suppressFire, "suppress1");
                }
            }

            if (method_21())
            {
                if (botOwner_0.Memory.IsInCover)
                {
                    botOwner_0.Memory.Spotted(false, null, new float?(32f));
                    int_17++;
                }

                if (method_13() && botOwner_0.Memory.GoalEnemy.CanShoot && botOwner_0.Memory.GoalEnemy.IsVisible)
                {
                    return commonLayer.DogFight(out customNavigationPoint_0);
                }

                return KnightAssault();
            }

            return null;
        }


        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> KnightFight()
        {
            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? baseDecision = KnightBaseDecision();

            if (baseDecision.HasValue) return baseDecision.Value;

            bool useGrenade = botOwner_0.Settings.FileSettings.Core.CanGrenade && Utils.Utils.Random(0f, 2f) > 1f && Utils.Enemy.Distance(botOwner_0) == Utils.Enemy.EnemyDistance.Close;

            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? suppress = guardLayer.SuppressFire(useGrenade);
            if (suppress.HasValue) return suppress.Value;

            AICoreActionResultStruct<BotLogicDecision, StandardBrain> push = pusherLayer.EngageEnemy();
            customNavigationPoint_0 = pusherLayer.NavigationPoint;

            return push;
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {
            AICoreActionEndStruct? common = commonLayer.ShallEndCurrentDecisionAllies(curDecision);

            if (common != null)
            {
                return (AICoreActionEndStruct)common;
            }

            if (curDecision.Reason == "assaultRush" && Utils.Enemy.Distance(botOwner_0) <= Utils.Enemy.EnemyDistance.VeryClose)
            {
                return new AICoreActionEndStruct("assault.closeEnough", true);
            }

            return base.ShallEndCurrentDecision(curDecision);
        }

        public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
        {
            customNavigationPoint_0 = commonLayer.FindPoint(data, p, checkCurrent);
            return customNavigationPoint_0;
        }

        public override AICoreActionEndStruct EndGoToPoint()
        {
            return commonLayer.EndGoToPoint();
        }

        public override AICoreActionEndStruct EndHoldPosition()
        {
            return pusherLayer.EndHoldPosition();
        }

        public override AICoreActionEndStruct EndRunToEnemy()
        {
            return pusherLayer.EndRunToEnemy();
        }

        public override AICoreActionEndStruct EndHeal()
        {
            return commonLayer.EndHeal();
        }

        public override AICoreActionEndStruct EndStimulators()
        {
            return commonLayer.EndStimulators();
        }

        public override AICoreActionEndStruct EndTakeItem()
        {
            return commonLayer.EndTakeItem();
        }

        public override AICoreActionEndStruct EndFollowerPatrolItem()
        {
            if (botOwner_0.Memory.HaveEnemy) return new AICoreActionEndStruct("enemy.Present", true);
            return base.EndFollowerPatrolItem();
        }

        protected void GetClosestCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(centerPosition, searchRadius);
        }
        protected virtual void GetCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            customNavigationPoint_0 = commonLayer.GetCoverPoint(centerPosition, searchRadius);
        }

        protected void GetApproachablePoint()
        {
            customNavigationPoint_0 = commonLayer.GetApproachableCover();
        }

        protected void GetClosestAttackCoverPoint(Vector3 centerPosition)
        {
            customNavigationPoint_0 = commonLayer.GetClosestShootCover(centerPosition);
        }
    }
}
