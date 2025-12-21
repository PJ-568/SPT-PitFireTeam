using EFT;

namespace friendlySAIN.Requests
{
    internal class FollowerRegroup : BotRequest
    {
        public FollowerRegroup(IPlayer requester) : base(requester, (BotRequestType)CustomBotRequestType.Regroup)
        {
        }

        public override bool CanProceed()
        {
            if (Executor == null) return false;
            return true;
        }

        public override bool CanRequest(BotOwner requester)
        {
            return true;
        }

        public override EBotRequestMode RequestMode
        {
            get
            {
                return EBotRequestMode.Fight;
            }
        }
    }
}
