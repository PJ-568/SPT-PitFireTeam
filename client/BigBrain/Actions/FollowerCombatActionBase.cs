using DrakiaXYZ.BigBrain.Brains;
using EFT;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class FollowerCombatActionData : CustomLayer.ActionData
    {
        public BotLogicDecision Decision { get; }
        public string Reason { get; }
        public GClass26? Data { get; }

        public FollowerCombatActionData(BotLogicDecision decision, string reason, GClass26? data)
        {
            Decision = decision;
            Reason = reason;
            Data = data;
        }
    }

    internal abstract class FollowerCombatActionBase : CustomLogic
    {
        protected FollowerCombatActionBase(BotOwner botOwner) : base(botOwner)
        {
        }

        protected void SetCombatSprint(bool sprint, bool withDebugCallback = false)
        {
            if (sprint)
            {
                BotOwner.SetPose(1f);
                BotOwner.SetTargetMoveSpeed(1f);
            }

            // Use the mover directly for follower combat run actions. BotOwner.Sprint(true)
            // drops current aiming target every tick, which can fight combat steering and turn
            // a run decision into a walk-looking movement state.
            BotOwner.Mover.Sprint(sprint, withDebugCallback);
        }

        protected static GClass26? GetRawData(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Data;
        }

        protected static string? GetReason(CustomLayer.ActionData data)
        {
            return (data as FollowerCombatActionData)?.Reason;
        }

        protected static TData? GetData<TData>(CustomLayer.ActionData data) where TData : GClass26
        {
            return GetRawData(data) as TData;
        }
    }
}
