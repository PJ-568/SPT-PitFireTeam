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
        private const bool EnableReactionTrace = true;
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(EffectsCommutator), "PlayHitEffect");
        }

        [PatchPostfix]
        public static void PatchPostfix(EffectsCommutator __instance, EftBulletClass info, ShotInfoClass playerHitInfo)
        {
            if (info == null) return;
            bool processed = __instance.IsHitPointAlreadyProcessed(info.HitPoint);

            if (info.Player != null)
            {
                int totalFollowers = 0;
                int forwarded = 0;
                BossPlayers.GetFollowers().ForEach(follower =>
                {
                    totalFollowers++;
                    BotOwner bot = follower.GetBot();
                    if (bot == null || !BossPlayers.IsFollower(bot)) return;
                    forwarded++;
                    FollowerAwareness.BulletFelt(bot, info, info.HitPoint);
                });
                if (EnableReactionTrace)
                {
                    Modules.Logger.LogInfo($"[ReactTrace] BulletImpact shooter={info.PlayerProfileID} processed={processed} followers={totalFollowers} forwarded={forwarded} hit={info.HitPoint}");
                }
            }
        }
    }
}
