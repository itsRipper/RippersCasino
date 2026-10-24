using System.Collections.Generic;
using EFT;
using EFT.Hideout;
using HarmonyLib;

namespace RippersCasino.Client
{
    internal static class IntelligenceCenterAccess
    {
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.PropertyGetter(typeof(HideoutAreasInfo), "Templates"), postfix: new HarmonyMethod(typeof(IntelligenceCenterAccess), nameof(Templates)));
            harmony.Patch(AccessTools.Method(typeof(AreaScreenSubstrate), "IsVisibilityPass"), postfix: new HarmonyMethod(typeof(IntelligenceCenterAccess), nameof(LaptopPanel)));
            harmony.Patch(AccessTools.Method(typeof(AreaScreenSubstrate), "DataToDisplay"), prefix: new HarmonyMethod(typeof(IntelligenceCenterAccess), nameof(LaptopStage)));
        }
        private static void LaptopStage(Stage __0, ELevelType __1, AreaData ____dataToShow)
        {
            if (__1 == ELevelType.Current && ____dataToShow?.Template.Type == EAreaType.IntelligenceCenter && ____dataToShow.CurrentLevel >= 1)
                CasinoPlugin.RegisterLaptopStage(__0);
        }
        private static void LaptopPanel(EPanelType __0, ELevelType __1, AreaData ____dataToShow, ref bool __result)
        {
            if (__0 == EPanelType.Interactions && __1 == ELevelType.Current && ____dataToShow?.Template.Type == EAreaType.IntelligenceCenter && ____dataToShow.CurrentLevel >= 1)
                __result = true;
        }
        private static void Templates(IEnumerable<AreaTemplate> __result)
        {
            foreach (var template in __result)
                if (template.Type == EAreaType.IntelligenceCenter)
                    CasinoPlugin.RegisterLaptop(template);
        }
    }
}
