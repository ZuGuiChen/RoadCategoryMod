using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 补丁：让原版子级过滤器（单向/双向/基础/装饰/公交/自带/创意工坊）
    /// 在我们的自定义分类面板中也能显示。
    ///
    /// 原因：每个 UIFilterType 有 m_whiteListedPanels 白名单，
    /// 只包含原版面板名（如 RoadsSmallPanel），我们的自定义面板名
    /// （RCM/10Panel）不在白名单中，所以过滤器被隐藏。
    ///
    /// 参考 Better Road Toolbar 的 UiFilterPatches.cs 实现。
    /// </summary>
    [HarmonyPatch]
    public static class UIFilterPatches
    {
        // GeneratedScrollPanel.UIFilterType 是私有嵌套类型，通过反射获取
        private static Type UIFilterType = typeof(GeneratedScrollPanel).GetNestedType("UIFilterType", BindingFlags.NonPublic);

        // 我们所有自定义面板的名称（category + "Panel"）
        private static string[] AllCustomPanels = Enum.GetValues(typeof(RoadCategory))
            .Cast<RoadCategory>()
            .Select(cat => RoadClassifier.Identifier + ((int)cat).ToString() + "Panel")
            .ToArray();

        // 指定 patch 目标：UIFilterType 的构造函数
        public static MethodBase TargetMethod()
        {
            if (UIFilterType == null)
            {
                Debug.LogError("[RoadCategoryMod] UIFilterType not found!");
                return null;
            }
            var constructors = AccessTools.GetDeclaredConstructors(UIFilterType);
            return constructors[0];
        }

        [HarmonyPostfix]
        public static void ConstructorPostfix(ref object __instance,
                                              ref string ___m_Name,
                                              ref string[] ___m_whiteListedPanels,
                                              ref string[] ___m_blackListedPanels)
        {
            if (!IsInGame())
                return;

            try
            {
                // 只处理道路相关的过滤器
                switch (___m_Name)
                {
                    case "RoadsOneWay":
                    case "RoadsTwoWay":
                    case "RoadsNotDecorated":
                    case "RoadsDecorated":
                    case "RoadsOneLane":
                    case "RoadsTwoLane":
                    case "RoadsPublicTransport":
                        // 把我们的所有自定义面板添加到白名单
                        ___m_whiteListedPanels = AllCustomPanels;
                        break;
                    default:
                        return;
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] UIFilterPatches failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static bool IsInGame()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game";
        }
    }
}
