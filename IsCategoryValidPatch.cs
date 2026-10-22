using System;
using HarmonyLib;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 拦截 GeneratedScrollPanel.IsCategoryValid(NetInfo, bool)，
    /// 判断一条道路是否应该显示在当前选中的分类标签下。
    /// </summary>
    [HarmonyPatch(typeof(GeneratedScrollPanel), "IsCategoryValid", new Type[] { typeof(NetInfo), typeof(bool) })]
    public static class IsCategoryValidPatch
    {
        [HarmonyPostfix]
        public static void Postfix(NetInfo info, bool ignore, GeneratedScrollPanel __instance,
                                   ref bool __result, ref string ___m_Category)
        {
            if (ignore || !(__instance is RoadsPanel) || !IsInGame())
                return;

            if (info == null)
                return;

            try
            {
                // 如果当前分类不是我们的自定义分类，不处理
                RoadCategory currentCat;
                if (!RoadClassifier.TryParseCategory(___m_Category, out currentCat))
                    return;

                // 如果道路属于保留的原版分类，不处理
                if (ShouldKeepExistingCategory(info.category))
                    return;

                // 计算道路的自定义分类，检查是否匹配当前分类
                var cats = RoadClassifier.GetCategories(info);
                __result = cats.Contains(currentCat);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] IsCategoryValidPatch failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static bool ShouldKeepExistingCategory(string cat)
        {
            switch (cat)
            {
                case "RoadsSmall":
                case "RoadsMedium":
                case "RoadsLarge":
                case "RoadsHighway":
                case "RoadsTiny":
                case "RoadsSmallHV":
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsInGame()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game";
        }
    }
}
