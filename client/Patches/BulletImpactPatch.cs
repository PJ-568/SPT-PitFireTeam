using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using Systems.Effects;

using friendlySAIN.Modules;
using friendlySAIN.Utils;

namespace friendlySAIN.Patches
{
    // patch to detect nearby bullet impacts
    public class BulletImpactPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EffectsCommutator), "PlayHitEffect");
        }

        [PatchPostfix]
        public static void PatchPostfix(EffectsCommutator __instance, EftBulletClass info, ShotInfoClass playerHitInfo)
        {
            if (info == null) return;
            _ = __instance.IsHitPointAlreadyProcessed(info.HitPoint);

            if (info.Player != null)
            {
                BossPlayers.GetFollowers().ForEach(follower =>
                {
                    BotOwner bot = follower.GetBot();
                    if (bot == null || !BossPlayers.IsFollower(bot)) return;
                    FollowerAwareness.BulletFelt(bot, info, info.HitPoint);
                });
            }
        }
    }
}
