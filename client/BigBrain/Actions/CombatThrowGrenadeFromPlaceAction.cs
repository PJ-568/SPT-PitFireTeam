using DrakiaXYZ.BigBrain.Brains;
using EFT;
using friendlySAIN.Modules;

namespace friendlySAIN.BigBrain.Actions
{
    internal sealed class CombatThrowGrenadeFromPlaceAction : FollowerCombatActionBase
    {
        private readonly GClass287 baseLogic;
        private bool loggedStart;

        public CombatThrowGrenadeFromPlaceAction(BotOwner botOwner) : base(botOwner)
        {
            baseLogic = new GClass287(botOwner);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            FollowerGrenadeRuntimeGate.EnableExplicitThrow(BotOwner);
            if (!loggedStart)
            {
                loggedStart = true;
                BotRequest? request = BotOwner.BotRequestController?.CurRequest;
                BotGrenadeController grenades = BotOwner.WeaponManager?.Grenades;
                friendlySAIN.Log?.LogInfo(
                    $"[GrenadeGate] follower={BotOwner.Profile?.Nickname ?? BotOwner.ProfileId ?? "<null>"} action-update request={request?.BotRequestType.ToString() ?? "<null>"} haveGrenade={grenades?.HaveGrenade.ToString() ?? "<null>"} ready={grenades?.ReadyToThrow.ToString() ?? "<null>"} throwing={grenades?.ThrowindNow.ToString() ?? "<null>"}");
            }

            baseLogic.UpdateNodeByBrain(GetRawData(data));
        }
    }
}
