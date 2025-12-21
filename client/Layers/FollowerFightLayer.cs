
using EFT;
using EFT.InventoryLogic;

using System;

using UnityEngine;
using UnityEngine.AI;

using friendlySAIN.Layers.Tactics;
using friendlySAIN.Modules;
using friendlySAIN.Brains;
using friendlySAIN.Utils;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers
{
    // GClass48 is followerBoar Fight layer
    /**
     * Main fight layer for the followers
     */
    internal class FollowerFightLayer : GClass55
    {

        private float bossInnerRadius
        {
            get { return commonLayer.bossInnerRadius; }
        }
        public float bossOuterRadius
        {
            get { return commonLayer.bossOuterRadius; }
        }

        private float coverTimer = 0f;

        private float float_3 = 0f;

        private float grSuppressTime = 0f;
        private bool grSupport = false;

        private bool ordersAreHold = false;
        private bool ordersAreAttack = false;
        private bool ordersAreReqroup = false;

        private bool holdTactic = false;
        private bool rushTactic = false;
        private bool allyTactic = false;
        private bool sniperTactic = false;
        private bool guardTactic = false;

        private bool bossUnderAttack = false;

        private string tactic = "default";

        private float hadEnemy = 0f;

        private float recalled = 0f;

        public NavMeshPath NavMeshPath
        {
            get
            {
                return commonLayer.NavMeshPath;
            }
        }

        private FollowerCommonLayer commonLayer;
        private FollowerHolderLayer holderLayer;
        private FollowerPusherLayer pusherLayer;
        private FollowerSniperLayer sniperLayer;
        private FollowerGuard guardLayer;

        private bool ordersChanged
        {
            get
            {
                return commonLayer.OrderHasChangedRecently;
            }
        }

        public FollowerCommonLayer CommonLayer
        {
            get => commonLayer;
        }

        public FollowerPusherLayer PusherLayer
        {
            get => pusherLayer;
        }

        public FollowerHolderLayer HolderLayer
        {
            get => holderLayer;
        }

        public FollowerSniperLayer SniperLayer
        {
            get => sniperLayer;
        }

        public FollowerFightLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            sniperLayer = new FollowerSniperLayer(bot, priority);
            commonLayer = sniperLayer.CommonLayer;
            holderLayer = new FollowerHolderLayer(bot, priority, commonLayer);
            pusherLayer = new FollowerPusherLayer(bot, priority, commonLayer);
            // guard is support
            guardLayer = new FollowerGuard(bot, priority, PusherLayer);

        }
        public override void OnActivate()
        {
            base.OnActivate();
            sniperLayer.OnActivate();
            holderLayer.OnActivate();
            pusherLayer.OnActivate();
            guardLayer.OnActivate();
        }

        public override void Dispose()
        {
            base.Dispose();
            sniperLayer.Dispose();
            holderLayer.Dispose();
            pusherLayer.Dispose();
            guardLayer.Dispose();
        }

        public void SetBossFightTactic(string tactic)
        {
            rushTactic = false;
            holdTactic = false;
            allyTactic = false;
            sniperTactic = false;
            guardTactic = false;


            if (tactic != null) tactic = tactic.ToLower();

            if (tactic == "ally" || tactic == "assist")
            {
                allyTactic = true;
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Assist");
            }
            else if (tactic == "push")
            {
                rushTactic = true;
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Push");
            }
            else if (tactic == "defend")
            {
                holdTactic = true;
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Hold");
            }
            else if (tactic == "marksman")
            {
                sniperTactic = true;
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Marksman");
            }
            else if (tactic == "guard" || tactic == "support")
            {
                guardTactic = true;
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Guard");
            }
            else
            {
                (botOwner_0.Brain.BaseBrain as FollowerBrain).SetTactic("Default");
            }
        }


        public void CoverType(string type)
        {
            commonLayer.CoverType(type);
        }
        public override string Name()
        {
            if (holdTactic) tactic = "defend";
            else if (rushTactic) tactic = "push";
            else if (allyTactic) tactic = "assist";
            else if (sniperTactic) tactic = "marksman";
            else if (guardTactic) tactic = "guard";
            else tactic = "default";
            if (ordersAreAttack) tactic += ":atk";
            else if (ordersAreHold) tactic += ":hld";

            return "FBPFight" + tactic;
        }
        public override bool ShallUseNow()
        {
            if (commonLayer.CurrentDecision.HasValue && commonLayer.CurrentDecision.Value.Action == BotLogicDecision.suppressGrenade)
            {
                return true;
            }

            if (!botOwner_0.Memory.HaveEnemy)
            {
                if (ordersAreAttack || ordersAreHold)
                {
                    ordersAreAttack = false;
                    ordersAreHold = false;

                    if (
                        botOwner_0.BotRequestController.CurRequest != null &&
                        botOwner_0.BotRequestController.CurRequest.BotRequestType == BotRequestType.attackClose
                    )
                    {
                        botOwner_0.BotRequestController.CurRequest.Complete();
                    }
                }

                grSupport = false;

                if (hadEnemy > Time.time)
                {
                    return true;
                }

                return false;
            }

            if (InteractableObjects.IsTaker(botOwner_0)) return false;

            hadEnemy = Time.time + 1f;

            return true;
        }

        public bool HasBoss()
        {
            return commonLayer.HasBoss();
        }

        public pitAIBossPlayer GetBoss()
        {
            return commonLayer.GetBoss();
        }

        public bool ShallGoNearBoss()
        {
            return commonLayer.ShallGoNearBoss();
        }

        public void OrdersChanged()
        {
            commonLayer.OrdersChanged();
        }
        public void OrdersReset()
        {
            commonLayer.OrderReset();
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> DefendPosition(Vector3 interestPosition)
        {
            AICoreActionResultStruct<BotLogicDecision, StandardBrain> holder = holderLayer.DefendPosition(interestPosition);
            customNavigationPoint_0 = holderLayer.NavigationPoint;

            return holder;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> EngageEnemy(bool pushOrdered = false, bool isEnemyLowThreat = false)
        {
            AICoreActionResultStruct<BotLogicDecision, StandardBrain> engage = pusherLayer.EngageEnemy(pushOrdered, isEnemyLowThreat);
            customNavigationPoint_0 = pusherLayer.NavigationPoint;

            return engage;
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> GuardTactic()
        {
            AICoreActionResultStruct<BotLogicDecision, StandardBrain> engage = guardLayer.GetDecision();
            customNavigationPoint_0 = pusherLayer.NavigationPoint;

            return engage;
        }


        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> DefaultTactic()
        {
            Vector3 interestPosition = HasBoss() ? GetBoss().Position : botOwner_0.GetPlayer.Transform.position;

            if (commonLayer.coverType == "far")
            {
                interestPosition = botOwner_0.GetPlayer.Transform.position;
            }

            Utils.Enemy.EnemyDistance enemyDistance = Enemy.Distance(botOwner_0);

            bool isEnemyLowThreat = commonLayer.IsEnemyLowThreat();

            if (allyTactic)
            {
                if (botOwner_0.Memory.AttackImmediately && isEnemyLowThreat)
                {
                    if (enemyDistance <= Enemy.EnemyDistance.Mid)
                        return EngageEnemy();
                    else
                        return pusherLayer.EnemySearch();
                }

                if (enemyDistance > Enemy.EnemyDistance.Mid || !isEnemyLowThreat) return sniperLayer.GetDecision();

                return pusherLayer.EnemySearch();
            }
            else if ((ordersAreHold || holdTactic) && !ordersAreAttack)
            {

                if (!ordersAreHold && !ordersAreAttack && holdTactic)
                {

                    if (isEnemyLowThreat && enemyDistance <= Utils.Enemy.EnemyDistance.Close)
                        return EngageEnemy(false, isEnemyLowThreat);

                }

                return DefendPosition(interestPosition);
            }
            else if (ordersAreAttack || rushTactic) return EngageEnemy(ordersAreAttack);

            if (enemyDistance <= Enemy.EnemyDistance.Mid)
            {
                return EngageEnemy(false, isEnemyLowThreat);
            }
            else
            {
                if (enemyDistance >= Enemy.EnemyDistance.Mid || !botOwner_0.Memory.AttackImmediately)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.GuardToCover, "coverBoss");
                }
                else if (!isEnemyLowThreat)
                {
                    return sniperLayer.GetDecision();
                }

                return pusherLayer.EnemySearch();
            }
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> DecideTactic()
        {
            if (guardTactic)
            {
                if (ordersAreAttack) return EngageEnemy(true);

                if (commonLayer.coverType == "far")
                {
                    if (Enemy.Distance(botOwner_0) <= Enemy.EnemyDistance.Mid)
                    {
                        return EngageEnemy();
                    }
                    else
                    {
                        return pusherLayer.EnemySearch();
                    }
                }

                return GuardTactic();
            }

            if (!guardTactic && !sniperTactic && botOwner_0.Memory.HaveEnemy && float_3 < Time.time)
            {

                float_3 = Time.time + Utils.Utils.Random(3f, 5f);
                EnemyInfo enemyInfo = botOwner_0.Memory.GoalEnemy;

                // borrow the auto suppression from guard layer
                if (!enemyInfo.IsSuppressed() && enemyInfo.ShallISuppress())
                {

                    bool useGrenade = botOwner_0.Settings.FileSettings.Core.CanGrenade && Utils.Utils.Random(0f, 2f) > 1f && Utils.Enemy.Distance(botOwner_0) == Utils.Enemy.EnemyDistance.Close;
                    // - check if player is too close when using grenade
                    Vector3 playerPos = commonLayer.HasBoss() ? commonLayer.GetBoss().Player().Transform.position : botOwner_0.GetPlayer.Position;
                    if ((playerPos - enemyInfo.CurrPosition).sqrMagnitude < 12f * 12f)
                    {
                        useGrenade = false;
                    }

                    AICoreActionResultStruct<BotLogicDecision, StandardBrain>? suppress = guardLayer.SuppressFire(useGrenade);

                    if (suppress.HasValue) return suppress.Value;

                    return DefaultTactic();
                }
            }

            return DefaultTactic();
        }
        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            BotRequest request = botOwner_0.BotRequestController.CurRequest;

            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            Vector3 bossPosition = HasBoss() ? GetBoss().Position : botPosition;

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


            if (request != null && request.BotRequestType == BotRequestType.wait)
            {
                ordersAreHold = true;
            }
            else
            {
                ordersAreHold = false;
            }

            if (request != null && request.BotRequestType == BotRequestType.attackClose)
            {
                ordersAreAttack = true;
            }
            else
            {
                ordersAreAttack = false;
            }

            if ((request != null && request.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup) || recalled > Time.time)
            {
                ordersAreReqroup = true;
            }
            else
            {
                ordersAreReqroup = false;
            }

            // assist and sniper do not do push
            if (allyTactic || sniperTactic)
            {
                ordersAreAttack = false;
                if (allyTactic) ordersAreHold = false;
            }

            // is in dogfight
            AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = commonLayer.DogFight(out customNavigationPoint_0);

            if (aicoreActionResultStruct != null)
            {
                return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
            }
            // needs healing?
            aicoreActionResultStruct = commonLayer.NeedHeal(out customNavigationPoint_0);
            if (aicoreActionResultStruct != null)
            {
                if (request != null && request.BotRequestType != BotRequestType.wait) request.Complete(); // cancel requests when needing to heal
                return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
            }

            // is under fire ? - try to get in cover
            if (
                !ordersAreReqroup && commonLayer.UnderFireTakeCover(out var coverDecision)
            )
            {
                return coverDecision.Value;
            }

            AIBossPlayerLogic gclass363_0 = HasBoss() ? GetBoss().GetBossLogic() : null;
            bossUnderAttack = gclass363_0 != null ? gclass363_0.IsHitted : false;

            // Check if the bot has received the regroup command
            if (ordersAreReqroup && GetNavDistance(bossPosition) > commonLayer.regroupMinDistance && (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.CanShoot))
            {
                if (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.CanShoot)
                {
                    recalled = Time.time + 2f;
                    if (request != null && request.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup)
                    {
                        request.Complete();
                    }
                }

                return commonLayer.GetCloserToBoss(out customNavigationPoint_0);
            }

            // suppression fire request
            if (request != null && request.BotRequestType == BotRequestType.suppressionFire)
            {
                if (sniperTactic)
                {
                    request.Complete();
                }
                else
                {
                    if (grSupport && grSuppressTime > Time.time)
                    {
                        return guardLayer.GrenadierDecision();
                    }

                    grSupport = false;

                    // - guard(support) can use grenade launcher 
                    if ((botOwner_0.Brain.BaseBrain as FollowerBrain)?.defaultTactic == "Guard" && grSuppressTime < Time.time)
                    {
                        AICoreActionResultStruct<BotLogicDecision, StandardBrain>? launcherDecicion = guardLayer.CanDoGrenadierSuppressRequest(new Ray(request.Requester.Transform.position, request.Requester.LookDirection));

                        if (launcherDecicion.HasValue)
                        {
                            grSuppressTime = Time.time + 5f;
                            grSupport = true;

                            return launcherDecicion.Value;
                        }
                    }

                    AICoreActionResultStruct<BotLogicDecision, StandardBrain>? suppress = guardLayer.SuppressFire(false);
                    if (suppress.HasValue) return suppress.Value;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(guardLayer.SuppressFallback(), "No Sup");
                }

            }

            // throw grenade request
            if (request != null && request.BotRequestType == BotRequestType.throwGrenade)
            {
                if (sniperTactic)
                    request.Complete();
                else
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.throwGrenadeFromPlace, "throwGrenadeRequest");
            }

            // spread out request
            if (
                request != null &&
                (request.BotRequestType == BotRequestType.getInCover || request.BotRequestType == BotRequestType.hide)
            )
            {
                if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy.CanShoot)
                {
                    request.Complete();
                }
                else
                {
                    GetCoverPoint(botPosition, Props.coverSearchRadius);

                    if (customNavigationPoint_0 != null)
                    {
                        Utils.Utils.SetTimeout(() =>
                        {
                            if (
                                botOwner_0 != null && !botOwner_0.IsDead && botOwner_0.BotState == EBotState.Active && request != null &&
                                (request.BotRequestType == BotRequestType.hide || request.BotRequestType == BotRequestType.getInCover)
                            )
                            {
                                request.Complete();
                            }

                        }, 4000);
                        botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Ambush);
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "runToCover");
                    }
                    else
                    {
                        request.Complete();
                    }
                }
            }

            float lastSeenTime = botOwner_0.Memory.HaveEnemy ? Time.time - botOwner_0.Memory.GoalEnemy.PersonalLastSeenTime : Time.time;
            // check if the boss is under attack
            if (
                !allyTactic && bossUnderAttack && (commonLayer.coverType == "close") &&
                (!botOwner_0.Memory.HaveEnemy || (!botOwner_0.Memory.GoalEnemy.IsVisible && lastSeenTime < 2.5f))
            )
            {
                // - switch the bot's enemy to the one attacking the boss
                BotOwner closestEnemy = HasBoss() ? GetBoss().ClosestEnemy() : null;
                if (closestEnemy != null)
                {
                    GetBoss().PrioritizeEnemy(botOwner_0, closestEnemy);
                }
                // - guard(support) tries to get in front of the boss
                if (guardTactic)
                {
                    if (GetNavDistance(bossPosition) > commonLayer.sprintDistance)
                    {
                        GetClosestAttackCoverPoint(closestEnemy ? closestEnemy.Position : bossPosition, bossOuterRadius);
                        if (customNavigationPoint_0 != null)
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "protectBossFast");
                        else
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.attackMoving, "protectBossSlow");
                    }
                    else
                    {
                        return EngageEnemy();
                    }
                }
                // - sniper tries to find shooting spot
                if (sniperTactic || holdTactic)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverToCover");
                }

                // - try and get back to the boss
                GetClosestCoverPoint(bossPosition, bossOuterRadius);

                if (customNavigationPoint_0 != null)
                {
                    botOwner_0.Tactic.SetTactic(BotsGroup.BotCurrentTactic.Attack);

                    if (commonLayer.SprintDistance(customNavigationPoint_0.Position))
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "protectBossFast");
                    }
                    else
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "protectBossSlow");
                    }
                }
                else
                {
                    return BotLogicDecisions.RegroupToBoss(botOwner_0);
                }
            }

            if (request != null && (request.BotRequestType == BotRequestType.goToPoint || request.BotRequestType == BotRequestType.followMe))
            {
                if (!botOwner_0.Memory.HaveEnemy || !botOwner_0.Memory.GoalEnemy.IsVisible)
                {
                    // come here request
                    if (request.BotRequestType == BotRequestType.followMe)
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:comeHere");
                    }
                    // go check request
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:goCheck");
                }
                else
                    request.Complete();
            }

            if (botOwner_0.Memory.HaveEnemy && botOwner_0.Memory.GoalEnemy.Owner.IsRole(WildSpawnType.marksman))
                return commonLayer.MarksManFight(out customNavigationPoint_0);


            if (commonLayer.ReachedCover)
            {
                return commonLayer.HoldPositionFor(Utils.Utils.Random(2f, 3f));
            }

            // ally tactic will make the bot always fight in hold mode
            if (allyTactic)
            {
                ordersAreAttack = false;
                ordersAreHold = false;

                return DecideTactic();
            }

            if (sniperTactic)
            {
                AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision = sniperLayer.GetDecision();
                customNavigationPoint_0 = sniperLayer.NavigationPoint;

                sniperLayer.CheckCanSwitchToSecondary(decision, Utils.Enemy.Distance(botOwner_0));
                return decision;
            }

            AICoreActionResultStruct<BotLogicDecision, StandardBrain> tct02 = DecideTactic();

            guardLayer.AutoToShotgun(tct02);

            return tct02;
        }

        public override AICoreActionEndStruct EndHoldPosition()
        {
            AICoreActionEndStruct endHold;
            if (ordersAreHold || allyTactic || (holdTactic && !ordersAreAttack))
                endHold = holderLayer.EndHoldPosition();
            else if (guardTactic)
                endHold = guardLayer.EndHoldPosition();
            else
                endHold = pusherLayer.EndHoldPosition();

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            if (endHold.Value && sniperTactic && goalEnemy != null && !goalEnemy.IsVisible)
            {
                if (
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                    Utils.Enemy.DistanceProxy(botOwner_0, botOwner_0.GetPlayer.Transform.position) >= Utils.Enemy.ProxyDistance.Mid
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeToMain();
                }
            }

            return endHold;
        }

        public override AICoreActionEndStruct EndSuppressFire()
        {
            AICoreActionEndStruct shouldEnd = base.EndSuppressFire();

            if (shouldEnd.Value)
            {
                BotRequest curRequest = botOwner_0.BotRequestController.CurRequest;
                if (curRequest != null && curRequest.BotRequestType == BotRequestType.suppressionFire)
                {
                    curRequest.Complete();
                    grSupport = false;
                    grSuppressTime = 0f;
                }
            }

            return shouldEnd;
        }

        public override AICoreActionEndStruct EndShootFromCover()
        {
            if (guardTactic)
            {
                return guardLayer.EndShootFromCover();
            }
            return base.EndShootFromCover();
        }
        public override AICoreActionEndStruct EndRunToEnemy()
        {
            return commonLayer.EndRunToEnemy();
        }

        public override AICoreActionEndStruct EndGoToEnemy()
        {
            return commonLayer.EndGoToEnemy();
        }

        public override AICoreActionEndStruct EndGoToPoint()
        {

            return commonLayer.EndGoToPoint();
        }
        public override AICoreActionEndStruct EndRunToCover()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            return base.EndRunToCover();
        }

        public override AICoreActionEndStruct EndGoToCoverPointTactical()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionEndStruct("enemy.None", true);
            }

            if (botOwner_0.Memory.GoalEnemy.CanShoot)
            {
                return new AICoreActionEndStruct("enemy.canSh", true);
            }

            return base.EndGoToCoverPointTactical();
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
            if (!botOwner_0.Memory.HaveEnemy)
                return new AICoreActionEndStruct("enemy.None", true);

            else if (!botOwner_0.Memory.GoalEnemy.IsVisible)
                return base.EndFollowerPatrolItem();
            else
                return new AICoreActionEndStruct("enemy.Present", true);
        }

        public override AICoreActionEndStruct EndDoorOpenRequest()
        {
            InteractableObjects.SetCurDoor(null);
            return new AICoreActionEndStruct("enemy.Present", true);
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {
            if (curDecision.Action == BotLogicDecision.heal
            )
            {
                return commonLayer.EndHeal();
            }

            if (curDecision.Action == BotLogicDecision.suppressGrenade && botOwner_0.WeaponManager.Grenades.ThrowindNow)
            {
                return new AICoreActionEndStruct("grenade.Throw", false);
            }

            if (!botOwner_0.Memory.HaveEnemy)
            {
                return aICoreActionEndStruct;
            }

            // orders changed
            if (
                ordersChanged &&
                (

                    ordersAreAttack ||
                    ordersAreHold ||
                    (
                        !commonLayer.ordersIgnoreReasons.Contains(curDecision.Reason) &&
                        !commonLayer.ordersIgnoreDecisions.Contains(curDecision.Action) &&
                        (
                            !botOwner_0.Memory.HaveEnemy ||
                            !botOwner_0.Memory.GoalEnemy.CanShoot
                        )
                    )
                )
            )
            {
                if (!ordersAreAttack && !ordersAreHold) commonLayer.OrderReset();
                return new AICoreActionEndStruct("orders.Received", true);
            }

            AICoreActionEndStruct? shallEndCommon = commonLayer.ShallEndCurrentDecisionCommon(curDecision);

            if (shallEndCommon.HasValue) return shallEndCommon.Value;

            return base.ShallEndCurrentDecision(curDecision);
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            commonLayer.DecisionChanged(prevDecision, nextDecision);
        }
        public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
        {
            if (coverTimer > Time.time) return customNavigationPoint_0;

            coverTimer = 1f + Time.time;

            customNavigationPoint_0 = Covers.FindPoint(botOwner_0, customNavigationPoint_0, 100f);

            return customNavigationPoint_0;
        }

        public void GetClosestCoverPoint(Vector3 centerPosition, float searchRadius, Func<CustomNavigationPoint, bool> extraChecks = null)
        {
            customNavigationPoint_0 = Utils.Covers.GetClosestCoverPoint(botOwner_0, centerPosition, searchRadius, extraChecks);
        }
        /** Find the closest safe cover point to the given position, within the given radius **/
        public void GetClosestSafeCoverPoint(Vector3 centerPosition)
        {
            customNavigationPoint_0 = commonLayer.GetClosestSafeCoverPoint(centerPosition);
        }

        /** Find closest cover point to pointA between pointA and pointB ensuring it is at minimum safeDistance from danger **/

        /** Find a random cover point at the given position, within the given radius **/
        public CustomNavigationPoint GetCoverPoint(Vector3 centerPosition, float searchRadius)
        {

            if (this.coverTimer > Time.time) return customNavigationPoint_0;

            this.coverTimer = 1f + Time.time;

            CustomNavigationPoint point1 = Covers.GetCoverPoint(botOwner_0, centerPosition, searchRadius);


            customNavigationPoint_0 = point1;
            botOwner_0.Memory.SetCoverPoints(point1);
            return customNavigationPoint_0;

        }

        public void GetApproachablePoint()
        {
            customNavigationPoint_0 = commonLayer.GetApproachableCover();

        }
        /** Find a shoot positionm that is closest to the enemy but at a minimum distance and maximum from the enemy **/
        public void GetClosestAttackCoverPoint(Vector3 centerPosition, float maxDistance = 100f)
        {
            customNavigationPoint_0 = pusherLayer.GetClosestAttackCoverPoint(centerPosition, maxDistance);
        }

        private void GetClosestCoverPointGroup(Vector3 centerPosition, float searchRadius)
        {
            customNavigationPoint_0 = commonLayer.GetClosestCoverPointGroup(centerPosition, searchRadius);
        }
        /** Is point free by the followers group **/
        private bool IsPointFreeGroup(CustomNavigationPoint point)
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

        public float GetNavDistance(Vector3 point)
        {
            return commonLayer.GetNavDistance(point);
        }
    }
}
