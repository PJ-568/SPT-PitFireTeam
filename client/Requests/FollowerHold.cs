using EFT;
using friendlySAIN.Actions;
using HarmonyLib;
using System.Collections.Generic;

namespace friendlySAIN.Requests
{
    internal class FollowerHold : BotRequest
    {
        private List<BotRequest> botRequests = null;
        public FollowerHold(Player requester) : base(requester, BotRequestType.wait)
        {

        }

        public override EBotRequestMode RequestMode
        {
            get
            {
                return EBotRequestMode.Fight;
            }
        }
        public override bool CanRequest(BotOwner requester)
        {
            return true;
        }

        public override bool CanProceed()
        {
            return true;
        }

        public override bool CanStartExecute(BotOwner executor)
        {
            if (botRequests == null)
            {
                botRequests = AccessTools.Field(typeof(BotGroupRequestController), "ListOfRequests").GetValue(executor.BotsGroup.RequestsController) as List<BotRequest>;
            }

            if (botRequests != null)
            {
                var req = botRequests.Find(request => request is FollowerGoCheck);
                if (req != null)
                {
                    var reqExecutor = req.Executor;
                    if (reqExecutor == executor)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
