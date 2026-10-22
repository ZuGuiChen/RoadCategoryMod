using System;
using ColossalFramework;
using ColossalFramework.UI;
using HarmonyLib;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 补丁：拦截 GeneratedScrollPanel.CreateButton，
    /// 为每个道路按钮添加分类图标，鼠标悬停时显示，点击弹出分类选择。
    /// 参考 Find It 2 的 CreateButtonPatch 实现。
    /// </summary>
    [HarmonyPatch(typeof(GeneratedScrollPanel))]
    [HarmonyPatch("CreateButton")]
    [HarmonyPatch(new Type[] { typeof(string), typeof(string), typeof(string), typeof(int), typeof(UITextureAtlas), typeof(UIComponent), typeof(bool), typeof(UITextureAtlas), typeof(string) })]
    public static class CreateButtonPatch
    {
        [HarmonyPostfix]
        public static void Postfix(UIButton __result, GeneratedScrollPanel __instance)
        {
            try
            {
                // 只处理道路面板
                if (!(__instance is RoadsPanel))
                    return;

                if (__result == null)
                    return;

                // 延迟初始化，确保按钮完全创建
                __result.eventVisibilityChanged += (component, visible) =>
                {
                    if (visible)
                        InitCategoryIcon(component as UIButton);
                };

                // 如果已经可见，立即初始化
                if (__result.isVisible)
                    InitCategoryIcon(__result);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] CreateButtonPatch failed: " + e.Message);
            }
        }

        private static void InitCategoryIcon(UIButton button)
        {
            if (button == null) return;

            try
            {
                // 获取道路 prefab
                NetInfo road = button.objectUserData as NetInfo;
                if (road == null) return;

                // 只处理道路
                if (road.GetService() != ItemClass.Service.Road)
                    return;

                // 修复按钮的解锁状态（RefreshPanel 后可能会重置 isEnabled）
                try
                {
                    button.isEnabled = ToolsModifierControl.IsUnlocked(road.GetUnlockMilestone());
                }
                catch { }

                // 检查是否已经添加了分类图标
                if (button.Find<UISprite>("RCM_CategoryIcon") != null)
                    return;

                // 添加分类图标（左下角，避免与 Find It 2 的右上角图标重叠）
                UISprite categoryIcon = button.AddUIComponent<UISprite>();
                categoryIcon.name = "RCM_CategoryIcon";
                categoryIcon.size = new Vector2(26f, 22f);
                categoryIcon.atlas = UIView.GetAView().defaultAtlas;
                categoryIcon.spriteName = "ToolbarIconRoads";
                categoryIcon.opacity = 0.75f;
                categoryIcon.color = new Color32(255, 220, 100, 255);
                categoryIcon.tooltipBox = UIView.GetAView().defaultTooltipBox;
                categoryIcon.relativePosition = new Vector3(2f, button.height - categoryIcon.height - 2f);
                categoryIcon.isVisible = false;

                // 获取当前分类名称作为 tooltip
                categoryIcon.tooltip = BuildCategoryTooltip(road);

                // 鼠标悬停按钮时显示分类图标
                button.eventMouseEnter += (c, p) =>
                {
                    categoryIcon.isVisible = true;
                    // 更新 tooltip（分类可能已改变）
                    categoryIcon.tooltip = BuildCategoryTooltip(road);
                };

                button.eventMouseLeave += (c, p) =>
                {
                    // 如果鼠标不在分类图标上，隐藏
                    if (!categoryIcon.containsMouse)
                        categoryIcon.isVisible = false;
                };

                // 分类图标悬停效果
                categoryIcon.eventMouseEnter += (c, p) =>
                {
                    categoryIcon.opacity = 1f;
                };

                categoryIcon.eventMouseLeave += (c, p) =>
                {
                    categoryIcon.opacity = 0.6f;
                    // 如果鼠标不在按钮上，隐藏
                    if (!button.containsMouse)
                        categoryIcon.isVisible = false;
                };

                // 点击分类图标弹出分类选择
                categoryIcon.eventClick += (c, p) =>
                {
                    p.Use();
                    CategorySelectPopup.Show(road, categoryIcon);
                };
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] InitCategoryIcon failed: " + e.Message);
            }
        }

        /// <summary>
        /// 生成分类图标的 tooltip：
        /// “★ 手动：小型道路（点击修改）” / “自动：小型道路（点击修改）”
        /// 文案随界面语言变化，每次悬停时重新生成。
        /// </summary>
        private static string BuildCategoryTooltip(NetInfo road)
        {
            try
            {
                var cats = RoadClassifier.GetCategories(road);
                RoadCategory cat = cats.Count > 0 ? cats[0] : RoadCategory.OtherRoads;
                bool isManual = RoadCategoryConfig.HasManualCategory(road.name);
                string key = isManual ? "ROAD_TOOLTIP_MANUAL" : "ROAD_TOOLTIP_AUTO";
                return Localization.Format(key, RoadClassifier.GetCategoryName(cat));
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
