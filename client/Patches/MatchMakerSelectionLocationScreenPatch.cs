using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using SPT.Common.Http;
using SPT.Common.Utils;
using SPT.Reflection.Patching;
using System.Collections.Generic;
using System.Reflection;

namespace friendlySAIN.Patches
{
    internal class MatchMakerSelectionLocationScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), "method_5");
        }
        [PatchPostfix]
        private static void PatchPostfix(MatchMakerSelectionLocationScreen __instance, RaidSettings raidSettings)
        {

            if (!raidSettings.IsPmc) return;

            string json = RequestHandler.GetJson("/singleplayer/autoteam");

            var ids = Json.Deserialize<List<string>>(json);

            if (friendlySAIN.application == null)
            {
                try
                {
                    friendlySAIN.application = SPT.Reflection.Utils.ClientAppUtils.GetMainApp();
                }
                catch { }
            }

            if (friendlySAIN.application == null) return;

            MatchmakerPlayerControllerClass controller = friendlySAIN.application.MatchmakerPlayerControllerClass;

            var group = controller.GroupPlayers;

            if (group == null) return;

            var inviteIds = new List<string>();

            foreach (var id in ids)
            {
                bool found = false;
                foreach (var player in group)
                {
                    if (player.AccountId == id)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    inviteIds.Add(id);
                }
            }

            if (inviteIds.Count > 0)
            {
                foreach (var id in inviteIds)
                {
                    controller.SendInvite(id, true, null);
                }
            }
        }
    }
}
