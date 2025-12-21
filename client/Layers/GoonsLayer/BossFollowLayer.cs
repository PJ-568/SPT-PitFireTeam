using EFT;

using friendlySAIN.Layers;

namespace friendlySAIN.Layers.GoonsLayer
{
    public class BossFollowLayer : FollowerLayer
    {
        public BossFollowLayer(BotOwner bot, int priority) : base(bot, priority)
        {
        }
        public override string Name()
        {
            return "BossFLP";
        }
    }
}