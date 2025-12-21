using EFT;
using friendlySAIN.Layers.Tactics;
using System;

namespace friendlySAIN.Layers
{
    /**
     * Ovewrite of the default avoid danger layer to regroup with the boss if needed - this is for BTR case when they run from it
     */
    internal class FollowerAvoidDanger : GClass45
    {
        protected CustomNavigationPoint customNavigationPoint_0;

        private FollowerCommonLayer commonLayer;

        public FollowerAvoidDanger(BotOwner bot, int priority) : base(bot, priority)
        {
            commonLayer = new FollowerCommonLayer(bot, priority);
        }

        public override void OnActivate()
        {
            base.OnActivate();
            commonLayer.OnActivate();
        }

        public override void Dispose()
        {
            base.Dispose();
            commonLayer.Dispose();
        }

        public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
        {
            customNavigationPoint_0 = commonLayer.FindPoint(data, p, checkCurrent);
            return customNavigationPoint_0;
        }
    }
}
