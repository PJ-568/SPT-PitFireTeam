using EFT;
using System;
using UnityEngine;

using friendlySAIN.Components;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.Tactics
{
    /** 
     * This class is not meant to be used directly as a brain layer, but within one 
     * Hold position layer based on Boar follower fight logic
     * **/
    public class FollowerHolderLayer : GClass55
    {
        private FollowerCommonLayer commonLayer;

        public FollowerCommonLayer CommonLayer { get { return commonLayer; } }

        private bool existingCommon = false;

        public bool onlyAssist = false;

        public CustomNavigationPoint NavigationPoint
        {
            get
            {
                return customNavigationPoint_0;
            }
        }

        public FollowerHolderLayer(BotOwner bot, int priority, FollowerCommonLayer commonLayer = null) : base(bot, priority)
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

        public bool ShallGoNearBoss()
        {
            return commonLayer.ShallGoNearBoss();
        }

        public override void DecisionChanged(AICoreActionResultStruct<BotLogicDecision, StandardBrain>? prevDecision, AICoreActionResultStruct<BotLogicDecision, StandardBrain> nextDecision)
        {
            commonLayer.DecisionChanged(prevDecision, nextDecision);
            base.DecisionChanged(prevDecision, nextDecision);
        }

        public AICoreActionResultStruct<BotLogicDecision, StandardBrain> DefendPosition(Vector3 interestPosition)
        {
            Vector3 botPosition = botOwner_0.GetPlayer.Transform.position;
            Vector3 enemyPosition = botOwner_0.Memory.GoalEnemy.CurrPosition;
            bool enemyVisible = botOwner_0.Memory.GoalEnemy.IsVisible;

            bool covertried = false;
            // If the bot is already in cover
            if (botOwner_0.Memory.IsInCover)
            {
                // - if the enemy is visible and can be shot, shoot from cover
                if (enemyVisible && (botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy || botOwner_0.Memory.GoalEnemy.CanShoot))
                {
                    if (botOwner_0.Memory.CurCustomCoverPoint.CanIShootToEnemy)
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromCover, "shootFromCover");
                    else
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.shootFromPlace, "shootEnemy");

                }
                // - else check if the bot needs to get close to the boss
                if (ShallGoNearBoss())
                {
                    customNavigationPoint_0 = commonLayer.GetClosestCoverPointGroup(interestPosition, commonLayer.coverSearchRadius);

                    if (customNavigationPoint_0 != null)
                    {
                        if (!commonLayer.SprintDistance(customNavigationPoint_0.Position))
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "regroupToBoss");
                        else
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "regroupToBossFast");
                    }
                    else
                    {
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverBoss");
                    }
                }
                // - else, hold position
                return commonLayer.HoldPositionFor(Utils.Utils.Random(2f, 3f), "holdPositionInCover");

            }
            else if (enemyVisible)
            {
                // else if can engage enemy, start fight
                AICoreActionResultStruct<BotLogicDecision, StandardBrain>? aicoreActionResultStruct = commonLayer.DogFight(out customNavigationPoint_0);
                if (aicoreActionResultStruct.HasValue)
                    return aicoreActionResultStruct.Value;
                // else retreat
                else
                {
                    customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(botPosition, commonLayer.coverSearchRadius);
                    covertried = true;
                    if (customNavigationPoint_0 != null)
                    {
                        if (!commonLayer.SprintDistance(customNavigationPoint_0.Position, 22f))
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "backOff");
                        else
                            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "moveToCover");
                    }
                }
            }

            // if the bot is not in cover, find the closest cover and move to it
            if (commonLayer.HasBoss() && commonLayer.coverType == "close")
            {
                if (!covertried)
                    customNavigationPoint_0 = commonLayer.GetClosestCoverPointGroup(interestPosition, commonLayer.coverSearchRadius);

                if (customNavigationPoint_0 != null)
                {
                    if (!commonLayer.SprintDistance(customNavigationPoint_0.Position))
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.attackRetreat, "regroupToBoss");
                    else
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "regroupToBossFast");
                }

                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverBoss");
            }
            else
            {
                if (!covertried)
                    customNavigationPoint_0 = commonLayer.GetClosestCoverPoint(interestPosition, commonLayer.coverSearchRadius);

                if (customNavigationPoint_0 != null)
                {
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "moveToCover");
                }
            }

            // final fallback decision
            return commonLayer.HoldPositionFor(Utils.Utils.Random(2f, 3f), "holdPositionInCover");
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


                if (ShallGoNearBoss()) return new AICoreActionEndStruct("goNearBoss", true);

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
                if (gclass363_0 != null && gclass363_0.IsHitted && commonLayer.coverType == "close")
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
    }
}
