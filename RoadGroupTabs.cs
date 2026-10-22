using System;
using ColossalFramework.UI;
using Debug = UnityEngine.Debug;

namespace RoadCategoryMod
{
    /// <summary>
    /// 道路面板顶部“父级分类”标签（tab）的图标与 tooltip 刷新。
    ///
    /// 背景（v1.3.1 修复）：
    /// 旧实现靠“tab 的 tooltip 里是否含有 RCM/ 标记”来识别我们的标签，
    /// 但第一次填充后 tooltip 会被换成当前语言的分类名（例如“社区小路”），标记就丢了；
    /// 而且**切换界面语言时游戏不会重新填充道路面板**，于是 tooltip 一直停在旧语言。
    ///
    /// 现在改为：
    /// 1. 用 tab 的 `name` 识别（游戏 `AddTab(category)` 会把 tab 名字设成 `RCM/10`），比 tooltip 可靠；
    /// 2. 同时兼容旧版本写在 tooltip 里的标记；
    /// 3. 监听 `Localization.LanguageChanged`，语言变化时主动刷新已存在的 tab（不需要重新填充面板）。
    /// </summary>
    public static class RoadGroupTabs
    {
        /// <summary>语言变化时调用：刷新当前道路面板上所有自定义分类 tab 的 tooltip 与图标。</summary>
        public static void RefreshAll()
        {
            if (!IsInGame())
                return;

            try
            {
                UIView view = UIView.GetAView();
                if (view == null)
                    return;

                RoadsGroupPanel panel = view.GetComponentInChildren<RoadsGroupPanel>(true);
                if (panel == null)
                    return;

                Apply(panel);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 刷新道路面板分类标签失败: " + e.Message);
            }
        }

        /// <summary>刷新一个道路分类面板里的全部 tab。</summary>
        public static void Apply(RoadsGroupPanel panel)
        {
            if (panel == null)
                return;

            UITabstrip[] strips = panel.GetComponentsInChildren<UITabstrip>(true);
            foreach (UITabstrip strip in strips)
                ApplyStrip(strip);
        }

        /// <summary>刷新一个 tab 条里的全部 tab（由 PopulateGroups 补丁调用）。</summary>
        public static void ApplyStrip(UITabstrip strip)
        {
            if (strip == null || strip.tabs == null)
                return;

            foreach (UIComponent tab in strip.tabs)
            {
                UIButton button = tab as UIButton;
                if (button == null)
                    continue;

                RoadCategory category;
                if (!TryIdentify(button, out category))
                    continue;

                ApplyButton(button, category);
            }
        }

        /// <summary>从 tab 的 name（优先）或 tooltip 中识别出我们的分类。</summary>
        private static bool TryIdentify(UIButton button, out RoadCategory category)
        {
            category = RoadCategory.RuralRoads;

            if (!string.IsNullOrEmpty(button.name) &&
                RoadClassifier.TryParseCategoryFromText(button.name, out category))
                return true;

            // 兼容旧版本：标记曾写在 tooltip 里
            if (!string.IsNullOrEmpty(button.tooltip) &&
                RoadClassifier.TryParseCategoryFromText(button.tooltip, out category))
                return true;

            return false;
        }

        private static void ApplyButton(UIButton button, RoadCategory category)
        {
            string displayName = RoadClassifier.GetCategoryName(category);

            // 优先使用自定义 PNG 图标
            string customSprite = CustomIconLoader.GetCustomSprite(category);
            if (!string.IsNullOrEmpty(customSprite) && CustomIconLoader.CustomAtlas != null)
            {
                if (button.atlas != null)
                    CustomIconLoader.AddCustomIconsToAtlas(button.atlas);

                button.normalFgSprite = customSprite;
                button.hoveredFgSprite = customSprite;
                button.pressedFgSprite = customSprite;
                button.focusedFgSprite = customSprite;
                button.disabledFgSprite = customSprite;
            }
            else
            {
                string spriteBase = GetSpriteBase(category);
                button.normalFgSprite = spriteBase;
                button.hoveredFgSprite = spriteBase + "Hovered";
                button.pressedFgSprite = spriteBase + "Pressed";
                button.focusedFgSprite = spriteBase + "Focused";
                button.disabledFgSprite = spriteBase + "Disabled";
            }

            // 只显示图标
            button.text = "";

            // tooltip 跟随当前语言
            button.tooltip = displayName;

            button.isVisible = true;
            button.Invalidate();
        }

        /// <summary>自定义图标配置优先，其次使用游戏内置图标。</summary>
        private static string GetSpriteBase(RoadCategory category)
        {
            string custom = IconConfig.GetCustomIcon(category);
            if (!string.IsNullOrEmpty(custom))
                return custom;

            return RoadClassifier.GetDefaultSpriteBase(category);
        }

        private static bool IsInGame()
        {
            return RoadCategoryMod.IsInGameScene();
        }
    }
}
