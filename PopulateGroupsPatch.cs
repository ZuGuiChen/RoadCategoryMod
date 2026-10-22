using System;
using ColossalFramework.UI;
using HarmonyLib;
using UnityEngine;
using static GeneratedGroupPanel;

namespace RoadCategoryMod
{
    /// <summary>
    /// 拦截 GeneratedGroupPanel.PopulateGroups，为我们的自定义分类 tab 设置
    /// 前景图标（替代文字）与鼠标悬停 tooltip。
    ///
    /// 识别方式见 RoadGroupTabs：以 tab 的 `name`（`RCM/10`）为主，兼容旧的 tooltip 标记。
    /// 具体设置逻辑放在 RoadGroupTabs 里，这样“切换语言时主动刷新”可以复用同一套代码。
    /// </summary>
    [HarmonyPatch(typeof(GeneratedGroupPanel), "PopulateGroups", new Type[] { typeof(GroupFilter), typeof(Comparison<GroupInfo>) })]
    public static class PopulateGroupsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GroupFilter filter, Comparison<GroupInfo> comparison,
                                   GeneratedGroupPanel __instance, UITabstrip ___m_Strip)
        {
            if (!(__instance is RoadsGroupPanel) || !RoadCategoryMod.IsInGameScene())
                return;

            try
            {
                RoadGroupTabs.ApplyStrip(___m_Strip);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] PopulateGroupsPatch failed: " + e.Message + "\n" + e.StackTrace);
            }
        }
    }
}
