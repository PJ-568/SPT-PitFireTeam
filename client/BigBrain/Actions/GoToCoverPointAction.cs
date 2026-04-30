using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace pitTeam.BigBrain.Actions
{
    internal class GoToCoverPointAction : CustomLogic
    {
        private readonly GClass212 baseLogic;
        private CustomNavigationPoint? lastPoint;
        private GClass31? cachedCoverData;
        internal sealed class GoToCoverPointActionData : CustomLayer.ActionData
        {
            public CustomNavigationPoint? Point { get; }

            public GoToCoverPointActionData(CustomNavigationPoint? point)
            {
                Point = point;
            }
        }

        public GoToCoverPointAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass212(botOwner);
        }

        public override void Start()
        {
            lastPoint = null;
            cachedCoverData = null;
        }

        public override void Update(CustomLayer.ActionData data)
        {
            CustomNavigationPoint? point = null;
            if (data is GoToCoverPointActionData goToCoverData && goToCoverData.Point != null)
            {
                point = goToCoverData.Point;
            }

            if (!ReferenceEquals(point, lastPoint))
            {
                lastPoint = point;
                cachedCoverData = point != null ? new GClass31(point) : null;
            }

            baseLogic.UpdateNodeByBrain(cachedCoverData);
        }
    }
}
