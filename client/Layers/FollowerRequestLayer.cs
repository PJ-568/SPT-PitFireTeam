using EFT;
using System;

using UnityEngine;

using friendlySAIN.Brains;
using friendlySAIN.Utils;

using StandardBrain = GClass26;

namespace friendlySAIN.Components
{
    /**
     * Generic FightReqNull extended class for the follower
     */
    internal class FollowerRequestLayer : GClass81
    {
        float coverTimer = 0f;
        float suppressTime = 0f;

        private CustomNavigationPoint customNavigationPoint_0;

        float heal_time = 0f;
        public FollowerRequestLayer(BotOwner bot, int priority) : base(bot, priority)
        {
        }

        AICoreActionResultStruct<BotLogicDecision, StandardBrain>? regroupDecision = null;
        AICoreActionResultStruct<BotLogicDecision, StandardBrain>? hideDecision = null;

        public override string Name()
        {
            if (botOwner_0.BotRequestController.CurRequest != null)
            {
                return "FBPReq:" + botOwner_0.BotRequestController.CurRequest.BotRequestType.ToString();
            }
            return "FBPReq:Null";
        }

        public override bool ShallUseNow()
        {

            if (
                botOwner_0.Medecine.FirstAid.Have2Do ||
                botOwner_0.Medecine.SurgicalKit.HaveWork ||
                botOwner_0.Medecine.Using ||
                (botOwner_0.Medecine.Stimulators != null && botOwner_0.Medecine.Stimulators.Using)
            ) return false;

            if (botOwner_0.Memory.HaveEnemy)
            {
                BotRequest request = botOwner_0.BotRequestController.CurRequest;
                if (request?.BotRequestType == BotRequestType.wait)
                    botOwner_0.BotRequestController.CurRequest.Complete();


                return false;
            }

            var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            if (brain != null && brain.UnderFire)
            {
                return false;
            }
            BotRequest currRequest = botOwner_0.BotRequestController.CurRequest;

            if (currRequest == null)
            {
                return false;
            }

            return true;
        }

        private bool HasBoss()
        {
            return botOwner_0.BotFollower.HaveBoss;
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            BotRequest request = botOwner_0.BotRequestController.CurRequest;

            if (request == null)
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HasBoss() ? BotLogicDecision.followerPatrol : HoldOrCover(botOwner_0), "req:Error");
            }

            switch (request.BotRequestType)
            {
                // on follow me request from the boss, just come closer to the boss or get out of hold position
                case BotRequestType.followMe:
                    regroupDecision = null;
                    hideDecision = null;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:comeHere");

                case (BotRequestType)CustomBotRequestType.Regroup:
                    hideDecision = null;
                    if (regroupDecision.HasValue)
                    {
                        return regroupDecision.Value;
                    }

                    Utils.Utils.SetTimeout(() =>
                    {
                        BotRequest req = botOwner_0.BotRequestController.CurRequest;

                        if (botOwner_0 != null && !botOwner_0.IsDead && botOwner_0.BotState == EBotState.Active && req != null && req.BotRequestType == (BotRequestType)CustomBotRequestType.Regroup)
                        {
                            req.Complete();
                        }
                        regroupDecision = null;
                    }, 2000);

                    regroupDecision = BotLogicDecisions.RegroupToBoss(botOwner_0);

                    return regroupDecision.Value;

                // stay in place
                case BotRequestType.wait:
                    hideDecision = null;
                    regroupDecision = null;
                    if (heal_time + 30f < Time.time && (botOwner_0.Medecine.FirstAid.Have2Do || botOwner_0.Medecine.SurgicalKit.HaveWork))
                    {
                        heal_time = Time.time;
                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.heal, "heal");
                    }
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.holdPosition, "req:holdPos");

                // spread out requests
                case BotRequestType.getInCover:
                case BotRequestType.hide:
                    regroupDecision = null;

                    if (hideDecision.HasValue) return hideDecision.Value;

                    GetCoverPoint(botOwner_0.GetPlayer.Transform.position, 50f);
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

                        hideDecision = new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.runToCover, "req:runHide");
                        return hideDecision.Value;

                    }
                    else
                    {
                        request.Complete();

                        return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.holdPosition, "req:cantHide");
                    }
                // "over there" request
                case BotRequestType.goToPoint:
                    regroupDecision = null;
                    hideDecision = null;
                    return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.MoveToPoint, "req:goCheck");
            }


            botOwner_0.BotTalk.TrySay(EPhraseTrigger.Negative, false);
            request.Complete();
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HasBoss() ? BotLogicDecision.followerPatrol : HoldOrCover(botOwner_0), "req:Unhandled");
        }

        public override AICoreActionEndStruct ShallEndCurrentDecision(AICoreActionResultStruct<BotLogicDecision, StandardBrain> curDecision)
        {
            AICoreActionEndStruct result;

            if (curDecision.Action == BotLogicDecision.goToPoint && botOwner_0.Mover.IsComeTo(0.5f, false))
            {
                regroupDecision = null;
                hideDecision = null;
                result = new AICoreActionEndStruct("point.Reached", true);
            }

            result = base.ShallEndCurrentDecision(curDecision);

            if (result.Value)
            {
                regroupDecision = null;
                hideDecision = null;
            }

            return result;
        }

        public override AICoreActionEndStruct EndSuppressFire()
        {
            BotRequest curRequest = this.botOwner_0.BotRequestController.CurRequest;
            if (curRequest != null && curRequest.BotRequestType == BotRequestType.suppressionFire)
            {
                if (suppressTime < Time.time)
                {
                    suppressTime = 0;
                    curRequest.Complete();

                    return aICoreActionEndStruct;
                }
                return aICoreActionEndStruct_1;
            }
            return aICoreActionEndStruct;
        }

        public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
        {
            if (this.customNavigationPoint_0 != null && (!this.customNavigationPoint_0.IsFreeById(this.botOwner_0.Id) || this.customNavigationPoint_0.IsSpotted))
            {
                this.customNavigationPoint_0 = null;
            }
            if (this.customNavigationPoint_0 != null)
            {
                return this.customNavigationPoint_0;
            }
            else
            {
                GetCoverPoint(botOwner_0.GetPlayer.Transform.position, 70f);
            }

            return this.customNavigationPoint_0;
        }

        private void GetCoverPoint(Vector3 centerPosition, float searchRadius)
        {
            if (this.coverTimer > Time.time) return;

            this.coverTimer = 1f + Time.time;

            customNavigationPoint_0 = Covers.GetCoverPoint(botOwner_0, centerPosition, searchRadius);
            botOwner_0.Memory.SetCoverPoints(customNavigationPoint_0);
        }


    }
}
