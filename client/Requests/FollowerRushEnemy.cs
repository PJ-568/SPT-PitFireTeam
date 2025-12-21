using EFT;

namespace friendlySAIN.Requests
{
    internal class FollowerRushEnemy : BotRequest
    {
        private BotOwner botOwner_0;
        public FollowerRushEnemy(BotOwner bot, Player requester, BotRequestType request = BotRequestType.attackClose) : base(requester, request)
        {
            botOwner_0 = bot;

            Utils.Utils.SetTimeout(() =>
            {
                if (botOwner_0 != null && !botOwner_0.IsDead && botOwner_0.BotState == EBotState.Active && botOwner_0.BotRequestController.CurRequest != null && botOwner_0.BotRequestController.CurRequest.BotRequestType == request)
                {
                    botOwner_0.BotRequestController.CurRequest.Complete();
                }
            }, 4000);
        }

        public override EBotRequestMode RequestMode
        {
            get
            {
                return EBotRequestMode.Fight;
            }
        }

        public override bool CanProceed()
        {
            return Executor != null && Executor.Memory.HaveEnemy;
        }

        public override bool CanRequest(BotOwner requester)
        {
            return true;
        }
    }
}
