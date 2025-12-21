using EFT;

namespace friendlySAIN.Requests
{
    internal class FollowerTakeLootRequest : BotRequest
    {
        private bool _fromWait = false;

        public bool FromWait
        {
            get
            {
                return _fromWait;
            }
        }
        public FollowerTakeLootRequest(IPlayer requester, bool fromWait = false) : base(requester, (BotRequestType)CustomBotRequestType.TakeLoot)
        {
            _fromWait = fromWait;
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