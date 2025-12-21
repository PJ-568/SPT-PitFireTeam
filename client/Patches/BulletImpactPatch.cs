using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using Systems.Effects;

using friendlySAIN.Modules;
using friendlySAIN.Brains;

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
            var player = info.Player;

            if (!__instance.IsHitPointAlreadyProcessed(info.HitPoint))
            {
                if (info.Player != null)
                {
                    BossPlayers.GetFollowers().ForEach(follower =>
                    {
                        var brain = follower.GetBot().Brain.BaseBrain as FollowerBrain;
                        if (brain != null)
                        {
                            brain.BulletFelt(info);
                        }
                    });
                }
                return;
            }
        }
    }
}
