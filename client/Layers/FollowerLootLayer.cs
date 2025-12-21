using EFT;

using friendlySAIN.Modules;
using friendlySAIN.Brains;

using StandardBrain = GClass26;

namespace friendlySAIN.Components
{
    /**
     * Looting layer for the followers.
     */
    internal class FollowerLootLayer : GClass129
    {

        public FollowerLootLayer(BotOwner bot, int priority) : base(bot, priority)
        {

        }

        public override bool ShallUseNow()
        {
            var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            bool isTaker = InteractableObjects.IsTaker(botOwner_0);

            if (brain != null && brain.UnderFire)
            {
                if (isTaker) InteractableObjects.RemoveTaker(botOwner_0);
                return false;
            }

            if (botOwner_0.Medecine.FirstAid.Have2Do || botOwner_0.Medecine.SurgicalKit.HaveWork || botOwner_0.Medecine.Using)
            {
                if (isTaker) InteractableObjects.RemoveTaker(botOwner_0);
                return false;
            }

            if (botOwner_0.Memory.HaveEnemy)
            {
                if (isTaker) InteractableObjects.RemoveTaker(botOwner_0);
                return false;
            }

            return isTaker;
        }

        public override string Name()
        {
            return "FBPLooting";
        }
        public override AICoreActionEndStruct EndTakeItem()
        {
            if (!InteractableObjects.IsTaker(botOwner_0))
                return new AICoreActionEndStruct("item.None", true);

            return aICoreActionEndStruct;
        }
        public override AICoreActionEndStruct EndFollowerPatrolItem()
        {
            if (!InteractableObjects.IsTaker(botOwner_0))
                return new AICoreActionEndStruct("item.None", true);

            return aICoreActionEndStruct;
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {

            if (!InteractableObjects.IsTaker(botOwner_0))
            {
                InteractableObjects.RemoveTaker(botOwner_0);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.followerPatrol, "loot.Error");
            }

            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.botTakeItem, "takeItem");
        }

    }
}
