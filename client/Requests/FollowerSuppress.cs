using EFT;

namespace friendlySAIN.Requests
{
    internal class FollowerSuppress : BotRequest
    {
        public FollowerSuppress(IPlayer requester) : base(requester, BotRequestType.suppressionFire)
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
