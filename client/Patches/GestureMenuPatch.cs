using EFT.UI.Gestures;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using GestureAction = EFT.UI.Gestures.GestureBaseItem.GStruct449;
using GestureMenuItem = EFT.UI.Gestures.GesturesMenu.Class3396;

namespace friendlySAIN.Patches
{
    // Add new prhases to the menu
    internal class GestureMenuPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "InitPhraseGroups");
        }
        [PatchPostfix]
        private static void PatchPostfix(GesturesMenu __instance)
        {
            var list_0 = (List<GesturesAudioItem>)AccessTools.Field(typeof(GesturesMenu), "list_1").GetValue(__instance);
            var list_1 = (List<GestureBaseItem>)AccessTools.Field(typeof(GesturesMenu), "list_2").GetValue(__instance);

            list_0.ForEach(item =>
            {
                if (item.gameObject.name == "ENEMY")
                {
                    List<EPhraseTrigger> enemyPhrases = new List<EPhraseTrigger>
                    {
                        EPhraseTrigger.OnRepeatedContact,
                        (EPhraseTrigger)CustomPhrases.OverThere
                    };

                    enemyPhrases.ForEach(phrase =>
                    {
                        GestureMenuItem @class = new GestureMenuItem();
                        @class.gesturesMenu_0 = __instance;
                        @class.isSituational = false;
                        GestureBaseItem gestureBaseItem = item.CreateNewPhrase(phrase, false);
                        gestureBaseItem.OnPointerClicked.Subscribe(new Action<GestureAction>(@class.method_0));
                        list_1.Add(gestureBaseItem);
                    });
                }

                else if (item.gameObject.name == "TEAM STATUS")
                {
                    List<CustomPhrases> statusPhrases = new List<CustomPhrases> { CustomPhrases.TeamStatus };

                    statusPhrases.ForEach(phrase =>
                    {
                        GestureMenuItem @class = new GestureMenuItem();
                        @class.gesturesMenu_0 = __instance;
                        @class.isSituational = false;
                        GestureBaseItem gestureBaseItem = item.CreateNewPhrase((EPhraseTrigger)phrase, false);
                        gestureBaseItem.OnPointerClicked.Subscribe(new Action<GestureAction>(@class.method_0));
                        list_1.Add(gestureBaseItem);
                    });


                }
            });
        }
    }

    internal class GestureMenuAvailablePhrasesPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(GesturesMenu), "Init");
        }
        [PatchPostfix]
        private static void PatchPostfix(GesturesMenu __instance)
        {
            var hashSet_1 = (HashSet<EPhraseTrigger>)AccessTools.Field(typeof(GesturesMenu), "hashSet_1").GetValue(__instance);
            hashSet_1.Add((EPhraseTrigger)CustomPhrases.TeamStatus);
            hashSet_1.Add((EPhraseTrigger)CustomPhrases.OverThere);
        }
    }

    // patch to return friendly name for the new phrases
    internal class EPhraseTriggerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(Enum), "ToString", new Type[] { });
        }
        [PatchPrefix]
        private static bool PatchPrefix(Enum __instance, ref string __result)
        {
            if (__instance is EPhraseTrigger trigger)
            {
                if (trigger == EPhraseTrigger.OnRepeatedContact)
                {
                    __result = friendlySAIN.optionsLang.gestures["OnRepeatedContact"];
                    return false;
                }
                else if (trigger == (EPhraseTrigger)CustomPhrases.TeamStatus)
                {
                    __result = friendlySAIN.optionsLang.gestures["TeamStatus"];
                    return false;
                }
                else if (trigger == (EPhraseTrigger)CustomPhrases.OverThere)
                {
                    __result = friendlySAIN.optionsLang.gestures["OverThere"];
                    return false;
                }
            }

            return true;
        }
    }
}
