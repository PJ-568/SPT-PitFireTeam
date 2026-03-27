using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class FollowerCombatActionData : CustomLayer.ActionData
    {
        public GClass26? Data { get; }

        public FollowerCombatActionData(GClass26? data)
        {
            Data = data;
        }
    }

    internal abstract class FollowerCombatActionBase : CustomLogic
    {
        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
        }

        protected static GClass26? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : GClass26
        {
            return GetRawData(data) as TData;
        }
    }
}
