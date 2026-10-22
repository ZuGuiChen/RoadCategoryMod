using System;
using ColossalFramework.UI;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 道路分类选择弹窗。
    /// 点击道路上的分类图标后弹出，允许选择分类。
    /// </summary>
    public static class CategorySelectPopup
    {
        private static UIPanel _popup;
        private static NetInfo _currentRoad;
        private static UIComponent _anchor;

        public static void Show(NetInfo road, UIComponent anchor)
        {
            if (road == null) return;
            _currentRoad = road;
            _anchor = anchor;

            // 关闭已存在的弹窗
            Close();

            var view = UIView.GetAView();

            // 创建弹窗
            _popup = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            _popup.width = 180f;
            _popup.backgroundSprite = "MenuPanel2";
            _popup.color = new Color32(58, 58, 58, 255);
            _popup.opacity = 0.97f;
            _popup.autoLayout = true;
            _popup.autoLayoutDirection = LayoutDirection.Vertical;
            _popup.autoLayoutPadding = new RectOffset(5, 5, 5, 5);
            _popup.canFocus = true;
            _popup.isInteractive = true;

            // 标题栏（含关闭按钮）
            var titleBar = _popup.AddUIComponent<UIPanel>();
            titleBar.width = 170f;
            titleBar.height = 26f;
            titleBar.autoLayout = false;

            var title = titleBar.AddUIComponent<UILabel>();
            title.text = Localization.Get("POPUP_TITLE");
            title.textColor = new Color32(255, 255, 255, 255);
            title.textScale = 1f;
            title.relativePosition = new Vector3(0f, 5f);
            title.autoSize = true;

            // 关闭按钮（buttonclose sprite 自带 × 图标）
            var closeBtn = titleBar.AddUIComponent<UIButton>();
            closeBtn.width = 24f;
            closeBtn.height = 24f;
            closeBtn.relativePosition = new Vector3(146f, 1f);
            closeBtn.normalBgSprite = "buttonclose";
            closeBtn.hoveredBgSprite = "buttonclosehover";
            closeBtn.pressedBgSprite = "buttonclosepress";
            closeBtn.eventClick += (c, p) => Close();

            // 分隔线
            var divider = _popup.AddUIComponent<UIPanel>();
            divider.width = 170f;
            divider.height = 2f;
            divider.color = new Color32(100, 100, 100, 255);

            // 获取当前分类
            var cats = RoadClassifier.GetCategories(road);
            RoadCategory currentCat = cats.Count > 0 ? cats[0] : RoadCategory.OtherRoads;

            // 分类按钮列表
            foreach (RoadCategory cat in Enum.GetValues(typeof(RoadCategory)))
            {
                var btn = _popup.AddUIComponent<UIButton>();
                btn.text = RoadClassifier.GetCategoryName(cat);
                btn.width = 170f;
                btn.height = 26f;
                btn.textColor = new Color32(255, 255, 255, 255);
                btn.textScale = 1f;
                btn.normalBgSprite = "ListItemHover";
                btn.hoveredBgSprite = "ListItemHover";
                btn.pressedBgSprite = "ListItemHover";
                btn.color = cat == currentCat ? new Color32(80, 120, 200, 255) : new Color32(60, 60, 60, 200);
                btn.hoveredColor = new Color32(80, 80, 80, 255);
                btn.pressedColor = new Color32(100, 100, 100, 255);
                btn.autoSize = false;

                btn.eventClick += (c, p) =>
                {
                    RoadCategoryConfig.SetManualCategory(road.name, cat);
                    RoadCategoryConfig.Save();
                    Close();
                    RefreshRoadPanel();
                };
            }

            // 重置按钮
            var resetBtn = _popup.AddUIComponent<UIButton>();
            resetBtn.text = Localization.Get("POPUP_RESTORE_AUTO");
            resetBtn.width = 170f;
            resetBtn.height = 26f;
            resetBtn.textColor = new Color32(200, 200, 200, 255);
            resetBtn.normalBgSprite = "ListItemHover";
            resetBtn.color = new Color32(60, 60, 60, 200);
            resetBtn.hoveredColor = new Color32(80, 80, 80, 255);
            resetBtn.autoSize = false;
            resetBtn.eventClick += (c, p) =>
            {
                RoadCategoryConfig.RemoveManualCategory(road.name);
                RoadCategoryConfig.Save();
                Close();
                RefreshRoadPanel();
            };

            // 定位弹窗到锚点附近
            if (anchor != null)
            {
                Vector3 pos = anchor.absolutePosition;
                float popupX = pos.x + anchor.width + 5f;
                float popupY = pos.y;

                // 确保弹窗在屏幕内
                if (popupX + _popup.width > view.fixedWidth)
                    popupX = pos.x - _popup.width - 5f;
                if (popupY + _popup.height > view.fixedHeight)
                    popupY = view.fixedHeight - _popup.height - 5f;
                if (popupY < 0) popupY = 5f;

                _popup.absolutePosition = new Vector3(popupX, popupY);
            }
            else
            {
                _popup.absolutePosition = new Vector3(
                    (view.fixedWidth - _popup.width) / 2f,
                    (view.fixedHeight - _popup.height) / 2f);
            }

            _popup.BringToFront();

            // 点击弹窗外部关闭（通过监听全局鼠标事件）
            _popup.eventMouseLeave += (c, p) => { };
        }

        public static void Close()
        {
            if (_popup != null)
            {
                UnityEngine.Object.Destroy(_popup.gameObject);
                _popup = null;
            }
            _currentRoad = null;
            _anchor = null;
        }

        /// <summary>
        /// 实时刷新道路面板，使手动分类立即生效。
        /// 通过重新触发 CollectAssets 来重建分类列表。
        /// </summary>
        private static void RefreshRoadPanel()
        {
            try
            {
                var view = UIView.GetAView();
                if (view == null) return;

                // 找到 RoadsPanel（道路列表面板）
                var scrollPanels = view.GetComponentsInChildren<GeneratedScrollPanel>(true);
                foreach (var sp in scrollPanels)
                {
                    if (!(sp is RoadsPanel)) continue;

                    // 调用 RefreshPanel 刷新（会重新构建道路列表）
                    var refreshMethod = sp.GetType().GetMethod("RefreshPanel",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (refreshMethod != null)
                    {
                        refreshMethod.Invoke(sp, null);
                    }

                    // 延迟两帧后修复道路按钮的解锁状态（RefreshPanel 可能会重置 isEnabled）
                    SimulationManager.instance.AddAction(() =>
                    {
                        SimulationManager.instance.AddAction(() =>
                        {
                            FixRoadButtonStates(sp);
                        });
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] RefreshRoadPanel failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 修复道路按钮的解锁状态（参考 Find It 2 的做法）。
        /// RefreshPanel 后，未解锁的道路可能会变成黑色不可用，
        /// 需要重新设置 isEnabled 状态。
        /// </summary>
        private static void FixRoadButtonStates(GeneratedScrollPanel panel)
        {
            try
            {
                var buttons = panel.GetComponentsInChildren<UIButton>(true);
                foreach (var btn in buttons)
                {
                    var prefab = btn.objectUserData as PrefabInfo;
                    if (prefab == null) continue;

                    // 根据解锁里程碑设置 isEnabled
                    btn.isEnabled = ToolsModifierControl.IsUnlocked(prefab.GetUnlockMilestone());
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] FixRoadButtonStates failed: " + e.Message);
            }
        }
    }
}
