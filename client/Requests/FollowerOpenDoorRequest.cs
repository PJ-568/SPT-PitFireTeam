using EFT;
using EFT.Interactive;
using friendlySAIN.Modules;

namespace friendlySAIN.Requests
{
    internal class FollowerOpenDoorRequest : BotRequest
    {
        private Door _door;
        public FollowerOpenDoorRequest(Door door, IPlayer requester) : base(requester, BotRequestType.doorOpen)
        {
            BotRequestType = BotRequestType.doorOpen;
            _door = door;
        }

        public Door Door
        {
            get { return _door; }
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

            if (Executor == null) return false;

            return true;
        }
        public override bool CanRequest(BotOwner requester)
        {
            return true;
        }

        public new void AddPossibleExecutors(BotOwner bot)
        {
            InteractableObjects.SetOpener(bot, _door);
            base.AddPossibleExecutors(bot);
        }
    }
}
