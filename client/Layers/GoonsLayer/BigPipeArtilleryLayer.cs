using EFT;

using JetBrains.Annotations;
using System;


using friendlySAIN.Brains;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers.GoonsLayer
{
    /**
     * BigPipe's artillery support layer
     */
    internal class BigPipeArtilleryLayer : KnightFightLayer
    {

        protected GClass60 supportLayer;
        public BigPipeArtilleryLayer([NotNull] BotOwner owner, int priority) : base(owner, priority)
        {
            supportLayer = new GClass60(owner, priority);
        }

        public override string Name()
        {
            return "PipeFight";
        }

        public override void OnActivate()
        {
            supportLayer?.OnActivate();
            base.OnActivate();
        }

        public override void Dispose()
        {
            supportLayer?.Dispose();
            base.Dispose();
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {
            if (!botOwner_0.Memory.HaveEnemy)
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.holdPosition, "enemy.None");
            }

            BotRequest request = botOwner_0.BotRequestController.CurRequest;

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
            if (Utils.Enemy.Distance(botOwner_0) >= Utils.Enemy.EnemyDistance.Distant)
            {
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>((BotLogicDecision)CustomBotDecisions.CoverToCover, "coverBoss");
            }

            AICoreActionResultStruct<BotLogicDecision, StandardBrain> baseDecision;

            try
            {
                baseDecision = KnightFight();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("baseDecision Error: " + ex.Message);
                Modules.Logger.LogInfo("Trace: " + ex.StackTrace);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(1f, 2f)), "baseDecision.Error");
            }

            if (
                baseDecision.Reason == "regroupToBossFast" ||
                baseDecision.Reason == "regroupToBoss" ||
                baseDecision.Reason == "pushEnemy" ||
                (commonLayer.OrderHasChangedRecently && request != null && request.BotRequestType == BotRequestType.attackClose) ||
                botOwner_0.Memory.GoalEnemy.Owner.IsRole(WildSpawnType.marksman) ||
                baseDecision.Action == BotLogicDecision.shootFromPlace
            )
                return baseDecision;


            /*if (Utils.EnemyInfo.Distance(botOwner_0) <= Utils.EnemyInfo.EnemyDistance.Close)
            {
                return followerFightLayer.CloseFight();
            }*/

            AICoreActionResultStruct<BotLogicDecision, StandardBrain> supportDecision;

            try
            {
                supportDecision = supportLayer.GetDecision();
            }
            catch (Exception ex)
            {
                Modules.Logger.LogInfo("supportDecision Error: " + ex.Message);
                Modules.Logger.LogInfo("Trace: " + ex.StackTrace);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(HoldFor(Utils.Utils.Random(1f, 2f)), "supportDecision.Error");
            }

            if (
                supportDecision.Action == BotLogicDecision.suppressFire ||
                supportDecision.Action == BotLogicDecision.shootToSmoke ||
                supportDecision.Action == BotLogicDecision.suppressGrenade
            )
            {
                if (supportDecision.Action == BotLogicDecision.suppressGrenade)
                {
                    var brain = (botOwner_0.Brain.BaseBrain as FollowerBrain);
                    if (!brain.IsThrowingGrenade) brain.OnThrow();
                }

                return supportDecision;
            }

            if (supportDecision.Reason == "IsInSmoke")
            {
                //GetClosestCoverPoint(bossPosition, fightRange);
                if (customNavigationPoint_0 != null)
                {
                    return supportDecision;
                }
            }

            return baseDecision;
        }

    }
}
