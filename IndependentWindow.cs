using System;
using ColossalFramework.UI;
using UnityEngine;
using System.Collections.Generic;

namespace RoadCategoryMod
{
    public static class IndependentWindow
    {
        private static bool isVisible = false;
        private static UIPanel panel;
        private static UIButton iconButton;

        public static void ToggleWindow()
        {
            if (panel == null || panel.parent == null)
                CreateWindow();
            else
                isVisible = !isVisible;

            if (panel != null)
            {
                panel.isVisible = isVisible;
                if (isVisible)
                    panel.BringToFront();
            }
        }

        private static void CreateWindow()
        {
            var view = UIView.GetAView();
            if (view == null) return;

            float viewWidth = view.fixedWidth;
            float viewHeight = view.fixedHeight;

            float panelWidth = Mathf.Min(600f, Mathf.Max(320f, viewWidth - 40f));
            float panelHeight = Mathf.Min(800f, Mathf.Max(240f, viewHeight - 80f));

            panel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            if (panel == null) return;
            panel.size = new Vector2(panelWidth, panelHeight);
            panel.relativePosition = new Vector3((viewWidth - panelWidth) / 2, (viewHeight - panelHeight) / 2);
            panel.backgroundSprite = "GenericPanel";
            panel.color = new Color32(40, 40, 40, 255);
            panel.isVisible = false;

            var title = panel.AddUIComponent(typeof(UILabel)) as UILabel;
            if (title != null)
            {
                title.text = Localization.Get("LEGACY_TITLE");
                title.textAlignment = UIHorizontalAlignment.Center;
                title.relativePosition = new Vector3(0, 10);
                title.size = new Vector2(panel.width, 30);
                title.textColor = Color.white;
            }

            var close = panel.AddUIComponent(typeof(UIButton)) as UIButton;
            if (close != null)
            {
                close.text = "✕";
                close.size = new Vector2(30, 30);
                close.relativePosition = new Vector3(panel.width - 35, 5);
                close.textColor = Color.white;
                close.normalBgSprite = "ButtonMenu";
                close.hoveredBgSprite = "ButtonMenuHovered";
                close.pressedBgSprite = "ButtonMenuPressed";
                close.eventClick += (c, p) => { isVisible = false; panel.isVisible = false; };
            }

            var scroll = panel.AddUIComponent(typeof(UIScrollablePanel)) as UIScrollablePanel;
            if (scroll == null) return;
            scroll.size = new Vector2(panel.width - 20, panel.height - 60);
            scroll.relativePosition = new Vector3(10, 45);
            scroll.autoLayout = true;
            scroll.autoLayoutDirection = LayoutDirection.Vertical;
            scroll.autoLayoutPadding = new RectOffset(5, 5, 5, 5);
            scroll.backgroundSprite = "GenericPanelLight";
            scroll.color = new Color32(50, 50, 50, 255);

            var bar = scroll.AddUIComponent(typeof(UIScrollbar)) as UIScrollbar;
            if (bar != null)
            {
                bar.orientation = UIOrientation.Vertical;
                bar.size = new Vector2(20, scroll.height);
                bar.relativePosition = new Vector3(scroll.width - 20, 0);
            }

            PopulateList(scroll);
        }

        private static void PopulateList(UIScrollablePanel parent)
        {
            while (parent.components.Count > 0)
                parent.RemoveUIComponent(parent.components[0]);

            var list = new List<NetInfo>();
            int count = PrefabCollection<NetInfo>.LoadedCount();
            for (int i = 0; i < count; i++)
            {
                var info = PrefabCollection<NetInfo>.GetLoaded((uint)i);
                if (info != null && info.m_class != null && info.m_class.m_service == ItemClass.Service.Road)
                    list.Add(info);
            }

            var groups = new Dictionary<string, List<NetInfo>>();
            foreach (var info in list)
            {
                string cat = info.m_class.m_subService.ToString();
                if (!groups.ContainsKey(cat))
                    groups[cat] = new List<NetInfo>();
                groups[cat].Add(info);
            }

            foreach (var kvp in groups)
            {
                var lbl = parent.AddUIComponent(typeof(UILabel)) as UILabel;
                if (lbl != null)
                {
                    lbl.text = kvp.Key;
                    lbl.textColor = Color.yellow;
                    lbl.textScale = 1.2f;
                    lbl.autoSize = false;
                    lbl.size = new Vector2(parent.width - 30, 30);
                    lbl.padding = new RectOffset(5, 0, 0, 0);
                }

                foreach (var road in kvp.Value)
                {
                    var btn = parent.AddUIComponent(typeof(UIButton)) as UIButton;
                    if (btn == null) continue;
                    btn.text = road.name;
                    btn.size = new Vector2(parent.width - 30, 25);
                    btn.textColor = Color.white;
                    btn.color = new Color32(60, 60, 60, 255);
                    btn.hoveredColor = new Color32(80, 80, 80, 255);
                    btn.pressedColor = new Color32(100, 100, 100, 255);
                    btn.objectUserData = road;
                    btn.eventClick += (c, p) =>
                    {
                        var info = c.objectUserData as NetInfo;
                        if (info != null)
                        {
                            ToolsModifierControl.SetTool<NetTool>();
                            var tool = ToolsModifierControl.GetTool<NetTool>();
                            if (tool != null)
                            {
                                tool.m_prefab = info;
                                var go = tool.gameObject;
                                go.SetActive(false);
                                go.SetActive(true);
                            }
                            panel.isVisible = false;
                            isVisible = false;
                        }
                    };
                }
            }
        }

        public static void CreateIconButton()
        {
            if (iconButton != null && iconButton.parent != null)
                return;

            // 浮动按钮只在游戏内创建，避免在主菜单/编辑器里出现残留按钮
            if (!RoadCategoryMod.IsInGameScene())
                return;

            var view = UIView.GetAView();
            if (view == null) return;

            var settings = ModSettings.Current;
            float size = Mathf.Clamp(settings.ButtonSize, ModSettings.MinButtonSize, ModSettings.MaxButtonSize);

            iconButton = view.AddUIComponent(typeof(UIButton)) as UIButton;
            if (iconButton == null) return;
            iconButton.name = "RCM_FloatButton";
            iconButton.size = new Vector2(size, size);

            float maxX = Mathf.Max(0f, view.fixedWidth - size);
            float maxY = Mathf.Max(0f, view.fixedHeight - size);
            iconButton.relativePosition = new Vector3(
                Mathf.Clamp(settings.ButtonX, 0f, maxX),
                Mathf.Clamp(settings.ButtonY, 0f, maxY));

            // 圆形底图（运行时生成；生成失败时回退旧版方形底图）
            UiIcons.EnsureAtlas();
            UITextureAtlas circleAtlas = UiIcons.Atlas;
            if (circleAtlas != null)
            {
                iconButton.atlas = circleAtlas;
                iconButton.normalBgSprite = UiIcons.CircleNormal;
                iconButton.hoveredBgSprite = UiIcons.CircleHovered;
                iconButton.pressedBgSprite = UiIcons.CirclePressed;
                // 颜色已在贴图里烘焙好，避免把前景图标一起染色
                iconButton.color = Color.white;
                iconButton.hoveredColor = Color.white;
                iconButton.pressedColor = Color.white;
                iconButton.foregroundSpriteMode = UIForegroundSpriteMode.Scale;
            }
            else
            {
                iconButton.color = new Color32(60, 60, 60, 200);
                iconButton.hoveredColor = new Color32(80, 80, 80, 220);
                iconButton.pressedColor = new Color32(120, 120, 120, 255);
                iconButton.normalBgSprite = "ButtonMenu";
                iconButton.hoveredBgSprite = "ButtonMenuHovered";
                iconButton.pressedBgSprite = "ButtonMenuPressed";
            }

            // 前景图标：优先使用 Icons\float_button.png
            string floatSprite = UiIcons.FloatIconSpriteName;
            if (!string.IsNullOrEmpty(floatSprite) && circleAtlas != null)
            {
                iconButton.normalFgSprite = floatSprite;
                iconButton.hoveredFgSprite = floatSprite;
                iconButton.pressedFgSprite = floatSprite;
                iconButton.text = "";
                ApplyIconPadding(iconButton, size);
            }
            else
            {
                iconButton.text = "🛣️";
                iconButton.textScale = 1.5f;
                iconButton.textColor = Color.white;
            }

            iconButton.tooltip = Localization.Get("FLOAT_BUTTON_TOOLTIP");

            // 移动标志：移动超过阈值时不触发点击
            bool isDragging = false;
            Vector2 dragStartPos = Vector2.zero;

            iconButton.eventClick += (c, p) =>
            {
                // 移动时不触发点击
                if (!isDragging)
                    RoadCategoryManagerUI.Toggle();
            };

            iconButton.eventMouseDown += (c, p) =>
            {
                // 使用屏幕坐标计算偏移（避免p.position随按钮移动而变化）
                float mouseX = Input.mousePosition.x;
                float mouseY = UIView.GetAView().fixedHeight - Input.mousePosition.y;
                iconButton.objectUserData = new Vector2(mouseX - iconButton.relativePosition.x, mouseY - iconButton.relativePosition.y);
                iconButton.BringToFront();
                isDragging = false;
                dragStartPos = new Vector2(mouseX, mouseY);
            };

            iconButton.eventMouseMove += (c, p) =>
            {
                if (Input.GetMouseButton(0) && iconButton.objectUserData != null)
                {
                    Vector2 offset = (Vector2)iconButton.objectUserData;
                    float mouseX = Input.mousePosition.x;
                    float mouseY = UIView.GetAView().fixedHeight - Input.mousePosition.y;

                    // 移动超过5像素判定为拖动，不触发点击
                    if (Vector2.Distance(new Vector2(mouseX, mouseY), dragStartPos) > 5f)
                        isDragging = true;

                    float newX = mouseX - offset.x;
                    float newY = mouseY - offset.y;
                    newX = Mathf.Clamp(newX, 0, UIView.GetAView().fixedWidth - iconButton.width);
                    newY = Mathf.Clamp(newY, 0, UIView.GetAView().fixedHeight - iconButton.height);
                    iconButton.relativePosition = new Vector3(newX, newY);
                }
            };

            iconButton.eventMouseUp += (c, p) =>
            {
                if (iconButton.objectUserData != null)
                {
                    var pos = iconButton.relativePosition;
                    var saveSettings = ModSettings.Current;
                    saveSettings.ButtonX = pos.x;
                    saveSettings.ButtonY = pos.y;
                    saveSettings.Save();
                    iconButton.objectUserData = null;
                }
            };
        }

        /// <summary>
        /// 把最新设置（大小 / 位置）应用到已存在的浮动按钮上。
        /// 按钮不存在时什么也不做（下次创建时自动使用新设置）。
        /// </summary>
        public static void ApplySettings(ModSettings settings)
        {
            if (iconButton == null)
                return;
            if (settings == null)
                settings = ModSettings.Current;

            var view = UIView.GetAView();
            float viewWidth = view != null ? view.fixedWidth : Screen.width;
            float viewHeight = view != null ? view.fixedHeight : Screen.height;

            float size = Mathf.Clamp(settings.ButtonSize, ModSettings.MinButtonSize, ModSettings.MaxButtonSize);
            iconButton.size = new Vector2(size, size);
            ApplyIconPadding(iconButton, size);

            float maxX = Mathf.Max(0f, viewWidth - size);
            float maxY = Mathf.Max(0f, viewHeight - size);
            iconButton.relativePosition = new Vector3(
                Mathf.Clamp(settings.ButtonX, 0f, maxX),
                Mathf.Clamp(settings.ButtonY, 0f, maxY));

            iconButton.tooltip = Localization.Get("FLOAT_BUTTON_TOOLTIP");
        }

        /// <summary>按当前语言刷新浮动按钮的提示文字。</summary>
        public static void RefreshLocalization()
        {
            if (iconButton != null)
                iconButton.tooltip = Localization.Get("FLOAT_BUTTON_TOOLTIP");
        }

        /// <summary>
        /// 让自定义图标在圆形底图里留出适当内边距（图标尺寸的 ~22%）。
        /// UIButton 的 spritePadding 是私有字段，只能反射设置；失败也不影响功能。
        /// </summary>
        private static void ApplyIconPadding(UIButton button, float size)
        {
            if (button == null || string.IsNullOrEmpty(UiIcons.FloatIconSpriteName))
                return;

            try
            {
                int pad = Mathf.Max(4, Mathf.RoundToInt(size * 0.22f));
                var paddingField = typeof(UIButton).GetField("m_SpritePadding",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (paddingField != null)
                    paddingField.SetValue(button, new RectOffset(pad, pad, pad, pad));
            }
            catch (Exception)
            {
                // 忽略：反射失败时仅影响内边距
            }
        }

        public static void DestroyIconButton()
        {
            if (iconButton != null)
            {
                UnityEngine.Object.Destroy(iconButton.gameObject);
                iconButton = null;
            }
        }
    }
}
