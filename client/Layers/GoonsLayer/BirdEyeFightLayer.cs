using EFT;
using EFT.InventoryLogic;
using System;
using UnityEngine;

using friendlySAIN.Layers.Tactics;
using friendlySAIN.Utils;
using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.GoonsLayer
{
    /**
     * Overwrite of BirdEye's fight layer
     */
    public class BirdEyeFightLayer : GClass73
    {
        private FollowerSniperLayer followerSniperLayer;
        private FollowerCommonLayer followerCommonLayer;
        private FollowerHolderLayer holderLayer;

        private float hadEnemy = 0f;

        private float recalled = 0f;

        protected bool ordersChanged
        {
            get
            {
                return (bool)followerCommonLayer?.OrderHasChangedRecently;
            }
        }

        public BirdEyeFightLayer(BotOwner bot, int priority) : base(bot, priority)
        {

            followerSniperLayer = new FollowerSniperLayer(bot, priority);
            followerCommonLayer = followerSniperLayer.CommonLayer;
            holderLayer = new FollowerHolderLayer(bot, priority, followerCommonLayer);
        }

        public override bool ShallUseNow()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                if (
                        botOwner_0.BotRequestController.CurRequest != null &&
                        botOwner_0.BotRequestController.CurRequest.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup
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
            hadEnemy = Time.time + 1f;
            return true;
        }


        public override void OnActivate()
        {
            base.OnActivate();
            followerSniperLayer?.OnActivate();
            followerCommonLayer?.OnActivate();
            holderLayer?.OnActivate();
        }

        public override void Dispose()
        {
            base.Dispose();
            followerSniperLayer?.Dispose();
            followerCommonLayer?.Dispose();
            holderLayer?.Dispose();
        }

        public override string Name()
        {
            return "BirdEyeFight";
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {

            if (!botOwner_0.Memory.HaveEnemy)
            {
                // is still healing?
                if (followerCommonLayer.IsHealing(out customNavigationPoint_0, out var decision))
                {
                    hadEnemy = Time.time + 1f;
                    return decision.Value;
                }

                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.holdPosition, "enemy.None");
            }

            try
            {
                BotRequest request = botOwner_0.BotRequestController.CurRequest;

                Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
                Vector3 bossPosition = request != null ? botOwner_0.BotRequestController.CurRequest.Requester.Position : botPosition;

                EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

                // is in dogfight?
                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = followerCommonLayer.DogFight(out customNavigationPoint_0);
                if (aicoreActionResultStruct.HasValue)
                {
                    return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
                }
                // needs healing?
                aicoreActionResultStruct = followerCommonLayer.NeedHeal(out customNavigationPoint_0);
                if (aicoreActionResultStruct != null)
                {
                    return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
                }

                // player needs help or has call for a regroup
                if (
                    (request != null &&
                    request.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup &&
                    Utils.Utils.GetNavDistance(botPosition, bossPosition) > followerCommonLayer.regroupMinDistance)
                    || recalled > Time.time
                )
                {
                    if (!botOwner_0.Memory.HaveEnemy || !goalEnemy.CanShoot)
                    {

                        if (request != null && request.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup)
                        {
                            recalled = Time.time + 2f;
                            request.Complete();
                        }

                        aicoreActionResultStruct = followerCommonLayer.GetCloserToBoss(out customNavigationPoint_0);
                        if (aicoreActionResultStruct != null)
                            return (AICoreActionResultStruct<BotLogicDecision, StandardBrain>)aicoreActionResultStruct;
                    }
                    else
                    {
                        botOwner_0.BotTalk.TrySay(EPhraseTrigger.DontKnow, false);
                        request.Complete();
                    }
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

                // do not pursue a marksman
                if (botOwner_0.Memory.HaveEnemy && goalEnemy.Owner.IsRole(WildSpawnType.marksman))
                {
                    return followerCommonLayer.MarksManFight(out customNavigationPoint_0);
                }

                AICoreActionResultStruct<BotLogicDecision, StandardBrain> decision;

                // default to hold tactic if enemy is too close
                Utils.Enemy.EnemyDistance enemyDistance = Utils.Enemy.Distance(botOwner_0);

                if (enemyDistance <= Utils.Enemy.EnemyDistance.VeryClose)
                {
                    decision = new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.dogFight, "getReady");
                }
                else if (enemyDistance <= Utils.Enemy.EnemyDistance.Close)
                {
                    if (followerCommonLayer.HasBoss() && Vector3.Distance(followerCommonLayer.GetBoss().Position, botPosition) <= 35f)
                    {
                        decision = holderLayer.DefendPosition(bossPosition);
                    }
                    else
                    {
                        decision = holderLayer.DefendPosition(botPosition);
                    }

                    customNavigationPoint_0 = holderLayer.NavigationPoint;
                }
                else
                {
                    decision = followerSniperLayer.GetDecision();
                    customNavigationPoint_0 = followerSniperLayer.NavigationPoint;
                }
                // enemy very close, switch to close combat ASAP
                followerSniperLayer.CheckCanSwitchToSecondary(decision, enemyDistance);

                return decision;
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("BirdEye Decision Error: " + ex.Message);
                Modules.Logger.LogInfo("Trace: " + ex.StackTrace);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(1f, 2f)), "decision.Error");
            }
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {
            AICoreActionEndStruct? common = followerCommonLayer.ShallEndCurrentDecisionAllies(curDecision);

            if (common != null) return (AICoreActionEndStruct)common;

            if (
                    curDecision.Action == (BotLogicDecision)CustomBotDecisions.EnemySearch ||
                    curDecision.Action == (BotLogicDecision)CustomBotDecisions.SniperSearch
                )
            {
                return followerSniperLayer.EndSniperSearch();
            }

            return base.ShallEndCurrentDecision(curDecision);
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            followerSniperLayer.DecisionChanged(prevDecision, nextDecision);
        }


        public override AICoreActionEndStruct EndHoldPosition()
        {
            AICoreActionEndStruct endHold;

            if (followerCommonLayer.OrderHasChangedRecently)
            {
                endHold = new AICoreActionEndStruct("EndHol", true);
            }
            else
            {
                endHold = holderLayer.EndHoldPosition();
            }

            EnemyInfo goalEnemy = botOwner_0.Memory.GoalEnemy;

            // switch back to primary weapon if enemy is no longer close
            if (goalEnemy != null && !goalEnemy.IsVisible && endHold.Value)
            {
                if (
                    botOwner_0.WeaponManager.Selector.LastEquipmentSlot != EquipmentSlot.FirstPrimaryWeapon &&
                    Enemy.DistanceProxy(botOwner_0, botOwner_0.GetPlayer.Transform.position) >= Enemy.ProxyDistance.Mid
                )
                {
                    botOwner_0.WeaponManager.Selector.TryChangeToMain();
                }
            }

            return endHold;
        }

        public override AICoreActionEndStruct EndHeal()
        {
            return followerCommonLayer.EndHeal();
        }

        public override AICoreActionEndStruct EndStimulators()
        {
            return followerCommonLayer.EndStimulators();
        }

        public override AICoreActionEndStruct EndTakeItem()
        {
            return followerCommonLayer.EndTakeItem();
        }

        public override AICoreActionEndStruct EndGoToPoint()
        {
            return followerSniperLayer.EndGoToPoint();
        }

        protected bool HasBoss()
        {
            return followerCommonLayer.HasBoss();
        }

        protected pitAIBossPlayer GetBoss()
        {
            return followerCommonLayer.GetBoss();
        }

        public void OrdersChanged()
        {
            followerSniperLayer.OrdersChanged();
        }

        public override ShootPointClass GetShootPoint()
        {
            return followerSniperLayer.GetShootPoint();
        }

        public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
        {
            customNavigationPoint_0 = Covers.FindPoint(botOwner_0, customNavigationPoint_0, 100f);
            return customNavigationPoint_0;
        }
    }
}
