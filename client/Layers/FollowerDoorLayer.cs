using EFT;

using UnityEngine;

using friendlySAIN.Modules;
using friendlySAIN.Brains;

using StandardBrain = GClass26;

namespace friendlySAIN.Layers
{
    /**
     * Better handler for door opening for our followers to prevent them from getting stuck.
     */
    internal class FollowerDoorLayer : GClass129
    {

        private float doorOpenTimer;
        public FollowerDoorLayer(BotOwner bot, int priority) : base(bot, priority)
        {
        }

        public override bool ShallUseNow()
        {
            var brain = botOwner_0.Brain.BaseBrain as FollowerBrain;

            if (brain != null && brain.UnderFire) return false;

            if (botOwner_0.Medecine.FirstAid.Have2Do || botOwner_0.Medecine.SurgicalKit.HaveWork || botOwner_0.Medecine.Using) return false;

            return InteractableObjects.IsOpener(botOwner_0);
        }

        public override string Name()
        {
            return "FBPDoorOpen";
        }

        public override AICoreActionResultStruct<BotLogicDecision, StandardBrain> GetDecision()
        {

            if (!InteractableObjects.IsOpener(botOwner_0))
            {
                InteractableObjects.RemoveTaker(botOwner_0);
                return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.followerPatrol, "door.Error");
            }

            doorOpenTimer = Time.time + 7f;
            return new AICoreActionResultStruct<BotLogicDecision, StandardBrain>(BotLogicDecision.doorOpen, "door.Open");
        }

        public override AICoreActionEndStruct EndDoorOpenRequest()
        {
            BotRequest curRequest = botOwner_0.BotRequestController.CurRequest;

            if (doorOpenTimer < Time.time)
            {

                if (InteractableObjects.IsOpener(botOwner_0)) InteractableObjects.RemoveOpener(botOwner_0);

                if (curRequest != null && curRequest.BotRequestType == BotRequestType.doorOpen)
                {
                    curRequest.Complete();
                }

                return new AICoreActionEndStruct("door.Timeout", true);
            }

            if (!InteractableObjects.IsOpener(botOwner_0))
            {

                if (curRequest != null && curRequest.BotRequestType == BotRequestType.doorOpen)
                {
                    curRequest.Complete();
                }

                return new AICoreActionEndStruct("door.None", true);
            }

            return aICoreActionEndStruct;
        }
    }
}
