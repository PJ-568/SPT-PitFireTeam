using EFT;

namespace friendlySAIN.Requests
{
    internal class FollowerTakeCover : BotRequest
    {

        public FollowerTakeCover(IPlayer requester, BotRequestType request = BotRequestType.getInCover) : base(requester, request)
        {

        }

        public override bool CanProceed()
        {
            if (Executor == null) return false;

            return true;
        }

        public override bool CanRequest(BotOwner owner)
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
