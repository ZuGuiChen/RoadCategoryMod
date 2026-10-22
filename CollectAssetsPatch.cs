using System;
using System.Collections.Generic;
using ColossalFramework;
using HarmonyLib;
using UnityEngine;
using static GeneratedGroupPanel;

namespace RoadCategoryMod
{
    /// <summary>
    /// 核心补丁：拦截 GeneratedGroupPanel.CollectAssets，
    /// 用我们的自定义分类替换原版的 RoadsSmall/RoadsMedium/RoadsLarge/RoadsHighway。
    /// 参考 Better Road Toolbar 的实现。
    /// </summary>
    [HarmonyPatch(typeof(GeneratedGroupPanel), "CollectAssets")]
    public static class CollectAssetsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GroupFilter filter,
                                   Comparison<GroupInfo> comparison,
                                   ref PoolList<GroupInfo> __result,
                                   GeneratedGroupPanel __instance)
        {
            // 只处理道路面板，且在游戏中（非主菜单/编辑器）
            if (!(__instance is RoadsGroupPanel) || !IsInGame())
                return;

            // 只处理 Net 类型的过滤器
            if (!filter.IsFlagSet(GroupFilter.Net))
                return;

            try
            {
                // 保留非默认分类（交叉口、收费站、DLC 道路等）
                var miscTabs = new List<GroupInfo>();
                foreach (var group in __result)
                {
                    if (!IsDefaultRoadCategory(group.name))
                    {
                        miscTabs.Add(group);
                    }
                }

                __result.Clear();

                // 遍历所有道路 prefab，收集需要的自定义分类
                var neededCategories = new List<RoadCategory>();
                bool toolManagerExists = Singleton<ToolManager>.exists;

                for (uint i = 0; i < PrefabCollection<NetInfo>.LoadedCount(); i++)
                {
                    NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                    if (info == null)
                        continue;

                    // 只处理道路服务、手动放置、subService 为 None 的道路
                    if (info.GetService() != ItemClass.Service.Road)
                        continue;
                    if (info.GetSubService() != ItemClass.SubService.None)
                        continue;
                    if (info.m_placementStyle != ItemClass.Placement.Manual)
                        continue;
                    if (toolManagerExists && !info.m_availableIn.IsFlagSet(Singleton<ToolManager>.instance.m_properties.m_mode))
                        continue;

                    // 严格过滤：只收集真正的道路（m_netAI 是 RoadBaseAI，且有车行道）
                    if (info.m_netAI == null || !(info.m_netAI is RoadBaseAI))
                        continue;
                    if (RoadClassifier.GetVehicleLaneCount(info) <= 0)
                        continue;

                    // 保留原版特殊分类（如 DLC 道路）
                    if (ShouldKeepExistingCategory(info.category))
                        continue;

                    var cats = RoadClassifier.GetCategories(info);
                    foreach (var cat in cats)
                    {
                        if (!neededCategories.Contains(cat))
                            neededCategories.Add(cat);
                    }
                }

                // 按枚举值排序（即我们定义的显示顺序）
                neededCategories.Sort();

                // 确保所有分类都显示（包括 OtherRoads，即使暂时没有道路归属）
                foreach (RoadCategory cat in Enum.GetValues(typeof(RoadCategory)))
                {
                    if (!neededCategories.Contains(cat))
                        neededCategories.Add(cat);
                }
                neededCategories.Sort();

                // 添加我们的自定义分类标签
                foreach (var cat in neededCategories)
                {
                    __result.Add(RoadClassifier.CreateGroup(cat));
                }

                // 追加保留的非默认分类
                foreach (var misc in miscTabs)
                {
                    __result.Add(misc);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] CollectAssetsPatch failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static bool IsDefaultRoadCategory(string cat)
        {
            switch (cat)
            {
                case "RoadsSmall":
                case "RoadsMedium":
                case "RoadsLarge":
                case "RoadsHighway":
                case "RoadsTiny":
                case "RoadsSmallHV":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldKeepExistingCategory(string cat)
        {
            // 保留交叉口、收费站、DLC 道路等非默认分类
            if (IsDefaultRoadCategory(cat))
                return false;
            return true;
        }

        private static bool IsInGame()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game";
        }
    }
}
