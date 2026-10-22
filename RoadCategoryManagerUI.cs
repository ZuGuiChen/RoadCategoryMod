using System;
using System.Collections.Generic;
using System.Diagnostics;
using ColossalFramework;
using ColossalFramework.UI;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RoadCategoryMod
{
    /// <summary>
    /// 道路分类管理器 UI 面板。
    ///
    /// 性能模型（行控件池 + 可见区虚拟化）：
    /// 1. 只为“可视区 + 少量缓冲”创建约 13 个行控件，滚动时复用并重新绑定数据，
    ///    道路数量从上百涨到几千也不会增加控件数量；
    /// 2. 列表改用**手动定位**（关闭 UIScrollablePanel 的 autoLayout、启用 customScrollBounds），
    ///    自己维护内容高度与滚动条范围，避免 autoLayout 每次增删都全量重排；
    /// 3. 道路显示名 / 车道数 / 自动分类结果按 NetInfo 缓存，搜索、切筛选、重开面板都不再重算；
    /// 4. 搜索支持两种模式：实时过滤（防抖 0.5s）或仅回车/按钮触发，可在设置页切换。
    /// </summary>
    public class RoadCategoryManagerUI : MonoBehaviour
    {
        private static RoadCategoryManagerUI _instance;

        /// <summary>列表行数据（与 UI 无关，可缓存/复用）。</summary>
        private class RowData
        {
            public NetInfo Info;
            public string DisplayName;
            public int Lanes;
            public RoadCategory Category;
            public bool IsManual;
        }

        /// <summary>一行 UI（被池复用，可绑定不同 RowData）。</summary>
        private class RoadRowView
        {
            private readonly RoadCategoryManagerUI _owner;
            private readonly UIPanel _panel;
            private readonly UILabel _star;
            private readonly UISprite _thumb;
            private readonly UILabel _name;
            private readonly UILabel _lanes;
            private readonly UIButton _reset;
            private readonly UIDropDown _dropdown;
            private bool _suppressDropdown;
            private int _itemsGeneration = -1;

            public RowData Data { get; private set; }

            public RoadRowView(RoadCategoryManagerUI owner, UIScrollablePanel parent)
            {
                _owner = owner;

                _panel = parent.AddUIComponent<UIPanel>();
                _panel.name = "RCM_Row";
                _panel.width = parent.width - 20f;
                _panel.height = RowHeight;
                _panel.backgroundSprite = "ListItemHover";
                _panel.color = new Color32(60, 60, 60, 180);
                _panel.isVisible = false;

                // 手动标记 ★
                _star = _panel.AddUIComponent<UILabel>();
                _star.text = "★";
                _star.textColor = new Color32(255, 200, 0, 255);
                _star.textScale = 1.2f;
                _star.relativePosition = new Vector3(8f, (RowHeight - 20f) / 2f);
                _star.tooltip = Localization.Get("MANAGER_MANUAL_TAG");
                _star.isVisible = false;

                // 道路缩略图
                _thumb = _panel.AddUIComponent<UISprite>();
                _thumb.width = 48f;
                _thumb.height = 40f;
                _thumb.relativePosition = new Vector3(28f, 4f);
                _thumb.color = Color.white;

                // 道路名称
                _name = _panel.AddUIComponent<UILabel>();
                _name.textColor = new Color32(255, 255, 255, 255);
                _name.autoSize = true;
                _name.width = 280f;
                _name.wordWrap = false;
                _name.relativePosition = new Vector3(82f, (RowHeight - 18f) / 2f);

                // 车道数
                _lanes = _panel.AddUIComponent<UILabel>();
                _lanes.textColor = new Color32(150, 200, 255, 255);
                _lanes.autoSize = true;
                _lanes.width = 50f;
                _lanes.textAlignment = UIHorizontalAlignment.Center;
                _lanes.relativePosition = new Vector3(372f, (RowHeight - 18f) / 2f);

                // 重置按钮
                _reset = _panel.AddUIComponent<UIButton>();
                _reset.text = Localization.Get("MANAGER_ROW_RESET");
                _reset.width = 86f;
                _reset.height = 28f;
                _reset.relativePosition = new Vector3(_panel.width - 96f, (RowHeight - 28f) / 2f);
                _reset.normalBgSprite = "ButtonMenu";
                _reset.hoveredBgSprite = "ButtonMenuHovered";
                _reset.pressedBgSprite = "ButtonMenuPressed";
                _reset.textColor = new Color32(255, 255, 255, 255);
                _reset.eventClick += (c, p) =>
                {
                    if (Data != null)
                        _owner.HandleRowReset(this, Data);
                };

                // 分类下拉框
                _dropdown = _panel.AddUIComponent<UIDropDown>();
                _dropdown.width = 150f;
                _dropdown.height = 28f;
                _dropdown.relativePosition = new Vector3(_panel.width - 256f, (RowHeight - 28f) / 2f);
                _dropdown.normalBgSprite = "TextFieldPanel";
                _dropdown.hoveredBgSprite = "TextFieldPanelHovered";
                _dropdown.focusedBgSprite = "TextFieldPanelFocused";
                _dropdown.listBackground = "GenericPanel";
                _dropdown.itemHeight = 24;
                _dropdown.listWidth = 170;
                _dropdown.itemHover = "ButtonMenuHovered";
                _dropdown.itemHighlight = "ButtonMenuFocused";
                _dropdown.textColor = new Color32(255, 255, 255, 255);
                _dropdown.popupColor = new Color32(80, 85, 95, 255);
                _dropdown.popupTextColor = new Color32(230, 230, 230, 255);
                _dropdown.canFocus = true;
                _dropdown.isInteractive = true;
                _dropdown.verticalAlignment = UIVerticalAlignment.Middle;
                _dropdown.textFieldPadding = new RectOffset(8, 20, 6, 0);
                _dropdown.itemPadding = new RectOffset(12, 12, 4, 0);
                _dropdown.eventSelectedIndexChanged += (c, index) =>
                {
                    if (_suppressDropdown || Data == null)
                        return;

                    var cat = (RoadCategory)Enum.GetValues(typeof(RoadCategory)).GetValue(index);
                    _owner.HandleRowCategoryChanged(this, Data, cat);
                };

                // 下拉箭头
                var arrow = _dropdown.AddUIComponent<UIButton>();
                _dropdown.triggerButton = arrow;
                arrow.size = _dropdown.size;
                arrow.relativePosition = Vector3.zero;
                arrow.normalFgSprite = "OptionsScrollbarThumb";
                arrow.foregroundSpriteMode = UIForegroundSpriteMode.Fill;
                arrow.horizontalAlignment = UIHorizontalAlignment.Right;
                arrow.verticalAlignment = UIVerticalAlignment.Middle;
                arrow.spritePadding = new RectOffset(0, 4, 4, 4);
                arrow.text = "";
                arrow.zOrder = 0;
            }

            public void SetY(float y)
            {
                _panel.relativePosition = new Vector3(3f, y);
            }

            public void SetVisible(bool visible)
            {
                if (_panel.isVisible != visible)
                    _panel.isVisible = visible;
            }

            /// <summary>把行控件绑定到一条道路的数据。</summary>
            public void Bind(RowData data, int itemsGeneration, string[] categoryItems)
            {
                Data = data;

                _star.isVisible = data.IsManual;
                _name.text = data.DisplayName;
                _name.tooltip = data.Info.name;
                _lanes.text = Localization.Format("MANAGER_LANES", data.Lanes);
                _reset.isEnabled = data.IsManual;

                try
                {
                    if (data.Info.m_Atlas != null && !string.IsNullOrEmpty(data.Info.m_Thumbnail))
                    {
                        _thumb.atlas = data.Info.m_Atlas;
                        _thumb.spriteName = data.Info.m_Thumbnail;
                        _thumb.color = Color.white;
                    }
                    else
                    {
                        _thumb.atlas = UIView.GetAView().defaultAtlas;
                        _thumb.spriteName = "ToolbarIconRoads";
                        _thumb.color = new Color32(150, 150, 150, 200);
                    }
                }
                catch (Exception)
                {
                    _thumb.atlas = UIView.GetAView().defaultAtlas;
                    _thumb.spriteName = "ToolbarIconRoads";
                    _thumb.color = new Color32(150, 150, 150, 200);
                }

                if (itemsGeneration != _itemsGeneration && categoryItems != null)
                {
                    _itemsGeneration = itemsGeneration;
                    _dropdown.items = categoryItems;
                }

                _suppressDropdown = true;
                try
                {
                    _dropdown.selectedIndex = Array.IndexOf(Enum.GetValues(typeof(RoadCategory)), data.Category);
                }
                finally
                {
                    _suppressDropdown = false;
                }
            }

            /// <summary>重置为自动分类后刷新本行显示。</summary>
            public void RefreshAfterReset(RoadCategory autoCategory, bool isManual)
            {
                if (Data != null)
                {
                    Data.Category = autoCategory;
                    Data.IsManual = isManual;
                }

                _star.isVisible = isManual;
                _reset.isEnabled = isManual;

                _suppressDropdown = true;
                try
                {
                    _dropdown.selectedIndex = Array.IndexOf(Enum.GetValues(typeof(RoadCategory)), autoCategory);
                }
                finally
                {
                    _suppressDropdown = false;
                }
            }

            /// <summary>玩家手动选择分类后刷新本行显示。</summary>
            public void MarkManual()
            {
                if (Data != null)
                    Data.IsManual = true;

                _star.isVisible = true;
                _reset.isEnabled = true;
            }

            /// <summary>语言变化后刷新本行的静态文字。</summary>
            public void RefreshLocalization(int itemsGeneration, string[] categoryItems)
            {
                _star.tooltip = Localization.Get("MANAGER_MANUAL_TAG");
                _reset.text = Localization.Get("MANAGER_ROW_RESET");

                if (Data != null)
                {
                    _lanes.text = Localization.Format("MANAGER_LANES", Data.Lanes);
                    Bind(Data, itemsGeneration, categoryItems);
                }
            }
        }

        private UIPanel _rootPanel;
        private UIScrollablePanel _scrollPanel;
        private UIScrollbar _scrollbar;
        private UITextField _searchField;
        private UIDropDown _categoryFilter;
        private UILabel _statusLabel;
        private UILabel _titleLabel;
        private UIButton _saveButton;
        private UIButton _resetAllButton;
        private UIButton _refreshButton;
        private UIButton _searchButton;

        private List<NetInfo> _allRoads;
        private readonly List<RowData> _rows = new List<RowData>();
        private readonly List<RoadRowView> _pool = new List<RoadRowView>();

        // 缓存：避免每次刷新都重新计算
        private readonly Dictionary<NetInfo, string> _nameCache = new Dictionary<NetInfo, string>();
        private readonly Dictionary<NetInfo, int> _laneCache = new Dictionary<NetInfo, int>();
        private readonly Dictionary<NetInfo, RoadCategory> _autoCategoryCache = new Dictionary<NetInfo, RoadCategory>();

        private string[] _categoryItems = new string[0];
        private int _itemsGeneration;

        private SearchFilterState _searchState;
        private string _searchPlaceholder = "";
        private bool _suppressSearchEvents = false;
        private int _filterCategory = -1;
        private bool _suppressFilterEvents = false;

        private float _lastScrollY = -1f;

        private const float PanelWidth = 820f;
        private const float PanelHeight = 600f;
        private const float RowHeight = 48f;
        private const float RowSpacing = RowHeight + 6f;
        private const float ContentTop = 3f;
        private const float SearchDelay = 0.5f;

        public static void Toggle()
        {
            // 每次呼出前同步语言（auto 模式跟随游戏语言）
            Localization.Refresh();

            if (_instance == null)
                Create();
            else
                _instance.SetVisible(!_instance.IsPanelVisible);
        }

        public static void Show()
        {
            Localization.Refresh();
            if (_instance == null)
                Create();
            else
                _instance.SetVisible(true);
        }

        public static void Hide()
        {
            if (_instance != null)
                _instance.SetVisible(false);
        }

        public static void Create()
        {
            if (_instance != null) return;
            var go = new GameObject("RoadCategoryManager");
            _instance = go.AddComponent<RoadCategoryManagerUI>();
        }

        /// <summary>面板已创建时刷新列表（例如在设置页清除了手动分类、改了搜索模式）。</summary>
        public static void RefreshIfOpen()
        {
            if (_instance != null && _instance._scrollPanel != null)
                _instance.RefreshList();
        }

        private bool _listBuilt = false;

        private bool IsPanelVisible
        {
            get { return _rootPanel != null && _rootPanel.isVisible; }
        }

        private void SetVisible(bool visible)
        {
            if (_rootPanel == null)
                return;

            _rootPanel.isVisible = visible;
            if (visible)
            {
                _rootPanel.BringToFront();
                // 首次显示时才构建列表（Start 里已构建则跳过）
                if (!_listBuilt)
                {
                    RefreshList();
                    _listBuilt = true;
                }
                else
                {
                    // 重新打开时重新绑定可见行（滚动位置可能过期）
                    _lastScrollY = -1f;
                    UpdateVisibleRows(true);
                }
            }

            // 让 UnifiedUI 按钮高亮状态与窗口一致
            UnifiedUIIntegration.SyncActive(visible);
        }

        void Start()
        {
            BuildUI();
            Localization.LanguageChanged += ApplyLanguage;
            LoadRoads();
            RefreshList();
            _listBuilt = true;
        }

        void Update()
        {
            if (_searchState != null)
            {
                _searchState.LiveSearch = ModSettings.Current.SearchLive;
                if (_searchState.Tick(Time.deltaTime))
                    ApplySearchIfChanged();
            }

            if (_rootPanel == null || !_rootPanel.isVisible || _scrollPanel == null)
                return;

            // 鼠标滚轮滚动（用 Unity Input 检测，不依赖 UI 事件系统）
            float scrollDelta = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scrollDelta) > 0.001f)
            {
                Vector3 mousePos = Input.mousePosition;
                Vector3 viewMousePos = new Vector3(mousePos.x, UIView.GetAView().fixedHeight - mousePos.y);
                Vector3 panelPos = _scrollPanel.parent.absolutePosition;
                if (viewMousePos.x >= panelPos.x && viewMousePos.x <= panelPos.x + _scrollPanel.parent.width &&
                    viewMousePos.y >= panelPos.y && viewMousePos.y <= panelPos.y + _scrollPanel.parent.height)
                {
                    Vector2 scrollPos = _scrollPanel.scrollPosition;
                    scrollPos.y -= scrollDelta * 120f; // 灵敏度
                    _scrollPanel.scrollPosition = scrollPos;
                }
            }

            // 滚动位置变化（滚轮 / 拖动滚动条 / 代码设置）→ 重新绑定可见行
            float y = _scrollPanel.scrollPosition.y;
            if (!Mathf.Approximately(y, _lastScrollY))
            {
                _lastScrollY = y;
                UpdateVisibleRows(false);
            }
        }

        void OnDestroy()
        {
            Localization.LanguageChanged -= ApplyLanguage;

            _pool.Clear();

            if (_rootPanel != null)
            {
                Destroy(_rootPanel.gameObject);
                _rootPanel = null;
            }
            _instance = null;
        }

        private void BuildUI()
        {
            var view = UIView.GetAView();

            // 根面板
            _rootPanel = view.AddUIComponent(typeof(UIPanel)) as UIPanel;
            _rootPanel.name = "RoadCategoryManager_Root";
            _rootPanel.width = PanelWidth;
            _rootPanel.height = PanelHeight;
            _rootPanel.backgroundSprite = "MenuPanel2";
            _rootPanel.color = new Color32(58, 58, 58, 255);
            _rootPanel.opacity = 0.97f;
            _rootPanel.canFocus = true;
            _rootPanel.isInteractive = true;
            _rootPanel.absolutePosition = new Vector3(
                Mathf.Max(0, (view.fixedWidth - PanelWidth) / 2f),
                Mathf.Max(0, (view.fixedHeight - PanelHeight) / 2f));
            _rootPanel.BringToFront();

            // 标题栏
            var titleBar = _rootPanel.AddUIComponent<UIPanel>();
            titleBar.width = PanelWidth;
            titleBar.height = 44f;
            titleBar.relativePosition = Vector3.zero;
            titleBar.backgroundSprite = "PanelTop";
            titleBar.color = new Color32(48, 48, 48, 255);

            _titleLabel = titleBar.AddUIComponent<UILabel>();
            _titleLabel.text = Localization.Get("MANAGER_TITLE");
            _titleLabel.textColor = new Color32(255, 255, 255, 255);
            _titleLabel.textScale = 1.3f;
            _titleLabel.relativePosition = new Vector3(15f, 12f);

            // 关闭按钮
            var closeBtn = titleBar.AddUIComponent<UIButton>();
            closeBtn.width = 32f;
            closeBtn.height = 32f;
            closeBtn.relativePosition = new Vector3(PanelWidth - 38f, 6f);
            closeBtn.normalBgSprite = "buttonclose";
            closeBtn.hoveredBgSprite = "buttonclosehover";
            closeBtn.pressedBgSprite = "buttonclosepress";
            closeBtn.eventClick += (c, p) => SetVisible(false);

            // 工具栏
            var toolBar = _rootPanel.AddUIComponent<UIPanel>();
            toolBar.width = PanelWidth - 20f;
            toolBar.height = 36f;
            toolBar.relativePosition = new Vector3(10f, 50f);

            // 搜索框
            _searchPlaceholder = Localization.Get("MANAGER_SEARCH");
            _searchState = new SearchFilterState(_searchPlaceholder, SearchDelay);
            _searchState.LiveSearch = ModSettings.Current.SearchLive;

            _searchField = toolBar.AddUIComponent<UITextField>();
            _searchField.width = 170f;
            _searchField.height = 30f;
            _searchField.relativePosition = new Vector3(0f, 0f);
            _searchField.padding = new RectOffset(6, 6, 7, 5);
            _searchField.normalBgSprite = "TextFieldPanel";
            _searchField.hoveredBgSprite = "TextFieldPanelHovered";
            _searchField.focusedBgSprite = "TextFieldPanelFocused";
            _searchField.selectionSprite = "EmptySprite";
            _searchField.textColor = new Color32(255, 255, 255, 255);
            _searchField.disabledTextColor = new Color32(128, 128, 128, 255);
            _searchField.text = _searchPlaceholder;
            _searchField.canFocus = true;
            _searchField.isInteractive = true;
            _searchField.builtinKeyNavigation = true;
            _searchField.readOnly = false;
            _searchField.eventGotFocus += (c, p) =>
            {
                if (_searchField.text != _searchPlaceholder)
                    return;

                _suppressSearchEvents = true;
                try
                {
                    _searchField.text = "";
                }
                finally
                {
                    _suppressSearchEvents = false;
                }
            };
            _searchField.eventLostFocus += (c, p) =>
            {
                if (!string.IsNullOrEmpty(_searchField.text))
                    return;

                _suppressSearchEvents = true;
                try
                {
                    _searchField.text = _searchPlaceholder;
                }
                finally
                {
                    _suppressSearchEvents = false;
                }
            };
            _searchField.eventTextChanged += (c, text) =>
            {
                if (_suppressSearchEvents || _searchState == null)
                    return;

                // 只有“搜索词真的变了”才可能刷新；清空时立即刷新，其它情况等防抖
                if (_searchState.OnTextChanged(text))
                    ApplySearchIfChanged();
            };
            _searchField.eventTextSubmitted += (c, text) =>
            {
                // 回车：立即搜索。失焦提交时若内容没变，OnSubmitted 会返回 false，不会刷新
                if (_suppressSearchEvents || _searchState == null)
                    return;

                if (_searchState.OnSubmitted(text))
                    ApplySearchIfChanged();
            };

            // 搜索按钮（手动模式下用它触发；实时模式下等于立即过滤）
            _searchButton = toolBar.AddUIComponent<UIButton>();
            _searchButton.text = Localization.Get("MANAGER_SEARCH_BUTTON");
            _searchButton.width = 60f;
            _searchButton.height = 30f;
            _searchButton.relativePosition = new Vector3(175f, 0f);
            _searchButton.normalBgSprite = "ButtonMenu";
            _searchButton.hoveredBgSprite = "ButtonMenuHovered";
            _searchButton.pressedBgSprite = "ButtonMenuPressed";
            _searchButton.textColor = new Color32(255, 255, 255, 255);
            _searchButton.eventClick += (c, p) =>
            {
                if (_searchState == null || _searchField == null)
                    return;

                if (_searchState.OnSubmitted(_searchField.text))
                    ApplySearchIfChanged();
            };

            // 分类筛选
            _categoryFilter = toolBar.AddUIComponent<UIDropDown>();
            _categoryFilter.width = 170f;
            _categoryFilter.height = 30f;
            _categoryFilter.relativePosition = new Vector3(240f, 0f);
            _categoryFilter.normalBgSprite = "TextFieldPanel";
            _categoryFilter.hoveredBgSprite = "TextFieldPanelHovered";
            _categoryFilter.focusedBgSprite = "TextFieldPanelFocused";
            _categoryFilter.listBackground = "GenericPanel";
            _categoryFilter.itemHeight = 24;
            _categoryFilter.listWidth = 190;
            _categoryFilter.itemHover = "ButtonMenuHovered";
            _categoryFilter.itemHighlight = "ButtonMenuFocused";
            _categoryFilter.textColor = new Color32(255, 255, 255, 255);
            _categoryFilter.popupColor = new Color32(80, 85, 95, 255);
            _categoryFilter.popupTextColor = new Color32(230, 230, 230, 255);
            _categoryFilter.canFocus = true;
            _categoryFilter.isInteractive = true;
            _categoryFilter.verticalAlignment = UIVerticalAlignment.Middle;
            _categoryFilter.textFieldPadding = new RectOffset(8, 24, 8, 0);
            _categoryFilter.itemPadding = new RectOffset(12, 12, 5, 0);
            _categoryFilter.items = BuildFilterItems();
            _categoryFilter.selectedIndex = 0;
            _categoryFilter.eventSelectedIndexChanged += (c, index) =>
            {
                if (_suppressFilterEvents) return;
                _filterCategory = index <= 0 ? -1 : (int)Enum.GetValues(typeof(RoadCategory)).GetValue(index - 1);
                RefreshList();
            };

            var filterArrow = _categoryFilter.AddUIComponent<UIButton>();
            _categoryFilter.triggerButton = filterArrow;
            filterArrow.size = _categoryFilter.size;
            filterArrow.relativePosition = Vector3.zero;
            filterArrow.normalFgSprite = "OptionsScrollbarThumb";
            filterArrow.foregroundSpriteMode = UIForegroundSpriteMode.Fill;
            filterArrow.horizontalAlignment = UIHorizontalAlignment.Right;
            filterArrow.verticalAlignment = UIVerticalAlignment.Middle;
            filterArrow.spritePadding = new RectOffset(0, 4, 4, 4);
            filterArrow.text = "";
            filterArrow.zOrder = 0;

            // 保存按钮
            _saveButton = toolBar.AddUIComponent<UIButton>();
            _saveButton.text = Localization.Get("MANAGER_SAVE");
            _saveButton.width = 90f;
            _saveButton.height = 30f;
            _saveButton.relativePosition = new Vector3(420f, 0f);
            _saveButton.normalBgSprite = "ButtonMenu";
            _saveButton.hoveredBgSprite = "ButtonMenuHovered";
            _saveButton.pressedBgSprite = "ButtonMenuPressed";
            _saveButton.textColor = new Color32(255, 255, 255, 255);
            _saveButton.eventClick += (c, p) => SaveConfig();

            // 重置全部
            _resetAllButton = toolBar.AddUIComponent<UIButton>();
            _resetAllButton.text = Localization.Get("MANAGER_RESET_ALL");
            _resetAllButton.width = 140f;
            _resetAllButton.height = 30f;
            _resetAllButton.relativePosition = new Vector3(515f, 0f);
            _resetAllButton.normalBgSprite = "ButtonMenu";
            _resetAllButton.hoveredBgSprite = "ButtonMenuHovered";
            _resetAllButton.pressedBgSprite = "ButtonMenuPressed";
            _resetAllButton.textColor = new Color32(255, 255, 255, 255);
            _resetAllButton.eventClick += (c, p) => ResetAll();

            // 刷新
            _refreshButton = toolBar.AddUIComponent<UIButton>();
            _refreshButton.text = Localization.Get("MANAGER_REFRESH");
            _refreshButton.width = 100f;
            _refreshButton.height = 30f;
            _refreshButton.relativePosition = new Vector3(660f, 0f);
            _refreshButton.normalBgSprite = "ButtonMenu";
            _refreshButton.hoveredBgSprite = "ButtonMenuHovered";
            _refreshButton.pressedBgSprite = "ButtonMenuPressed";
            _refreshButton.textColor = new Color32(255, 255, 255, 255);
            _refreshButton.eventClick += (c, p) =>
            {
                RoadCategoryConfig.Reload();
                LoadRoads();
                RefreshList();
            };

            // 滚动区域背景
            var scrollBg = _rootPanel.AddUIComponent<UIPanel>();
            scrollBg.width = PanelWidth - 30f;
            scrollBg.height = PanelHeight - 145f;
            scrollBg.relativePosition = new Vector3(10f, 92f);
            scrollBg.backgroundSprite = "GenericPanel";
            scrollBg.color = new Color32(45, 45, 45, 200);
            scrollBg.clipChildren = true;
            scrollBg.canFocus = true;
            scrollBg.isInteractive = true;

            // 虚拟化列表：关闭自动布局，自己维护内容高度
            _scrollPanel = scrollBg.AddUIComponent<UIScrollablePanel>();
            _scrollPanel.width = PanelWidth - 50f;
            _scrollPanel.height = PanelHeight - 145f;
            _scrollPanel.relativePosition = Vector3.zero;
            _scrollPanel.autoLayout = false;
            _scrollPanel.customScrollBounds = true;
            _scrollPanel.scrollWheelAmount = 60;
            _scrollPanel.clipChildren = true;
            _scrollPanel.builtinKeyNavigation = true;
            _scrollPanel.canFocus = true;
            _scrollPanel.isInteractive = true;

            // 垂直滚动条
            _scrollbar = scrollBg.AddUIComponent<UIScrollbar>();
            _scrollbar.orientation = UIOrientation.Vertical;
            _scrollbar.width = 16f;
            _scrollbar.height = PanelHeight - 145f;
            _scrollbar.relativePosition = new Vector3(PanelWidth - 46f, 0f);
            _scrollbar.pivot = UIPivotPoint.TopLeft;
            _scrollbar.minValue = 0f;
            _scrollbar.maxValue = 1f;
            _scrollbar.value = 0f;
            _scrollbar.incrementAmount = RowSpacing;

            var trackSprite = _scrollbar.AddUIComponent<UISlicedSprite>();
            trackSprite.relativePosition = Vector2.zero;
            trackSprite.autoSize = true;
            trackSprite.size = trackSprite.parent.size;
            trackSprite.fillDirection = UIFillDirection.Vertical;
            trackSprite.spriteName = "ScrollbarTrack";
            _scrollbar.trackObject = trackSprite;

            var thumbSprite = trackSprite.AddUIComponent<UISlicedSprite>();
            thumbSprite.relativePosition = Vector2.zero;
            thumbSprite.fillDirection = UIFillDirection.Vertical;
            thumbSprite.autoSize = true;
            thumbSprite.width = thumbSprite.parent.width - 4;
            thumbSprite.spriteName = "ScrollbarThumb";
            _scrollbar.thumbObject = thumbSprite;

            _scrollPanel.verticalScrollbar = _scrollbar;

            // 状态栏
            _statusLabel = _rootPanel.AddUIComponent<UILabel>();
            _statusLabel.width = PanelWidth - 20f;
            _statusLabel.height = 28f;
            _statusLabel.relativePosition = new Vector3(10f, PanelHeight - 32f);
            _statusLabel.textColor = new Color32(180, 180, 180, 255);
            _statusLabel.text = Localization.Get("MANAGER_READY");

            RebuildCategoryItems();
        }

        /// <summary>语言切换后刷新面板上的文字与列表。</summary>
        private void ApplyLanguage()
        {
            if (_rootPanel == null)
                return;

            // 标题是本地化文本，语言变了要重新取
            _nameCache.Clear();

            if (_titleLabel != null)
                _titleLabel.text = Localization.Get("MANAGER_TITLE");
            if (_saveButton != null)
                _saveButton.text = Localization.Get("MANAGER_SAVE");
            if (_resetAllButton != null)
                _resetAllButton.text = Localization.Get("MANAGER_RESET_ALL");
            if (_refreshButton != null)
                _refreshButton.text = Localization.Get("MANAGER_REFRESH");
            if (_searchButton != null)
                _searchButton.text = Localization.Get("MANAGER_SEARCH_BUTTON");

            if (_searchField != null)
            {
                string placeholder = Localization.Get("MANAGER_SEARCH");
                if (_searchField.text == _searchPlaceholder)
                {
                    _suppressSearchEvents = true;
                    try
                    {
                        _searchField.text = placeholder;
                    }
                    finally
                    {
                        _suppressSearchEvents = false;
                    }
                }
                _searchPlaceholder = placeholder;
                if (_searchState != null)
                    _searchState.SetPlaceholder(placeholder);
            }

            if (_categoryFilter != null)
            {
                _suppressFilterEvents = true;
                try
                {
                    _categoryFilter.items = BuildFilterItems();

                    int index = 0;
                    if (_filterCategory >= 0)
                    {
                        int catIndex = Array.IndexOf(Enum.GetValues(typeof(RoadCategory)), (RoadCategory)_filterCategory);
                        if (catIndex >= 0)
                            index = catIndex + 1;
                    }
                    _categoryFilter.selectedIndex = index;
                }
                finally
                {
                    _suppressFilterEvents = false;
                }
            }

            if (_statusLabel != null)
                _statusLabel.text = Localization.Get("MANAGER_READY");

            RebuildCategoryItems();

            // 池里的行也需要更新静态文字（按钮/提示）
            for (int i = 0; i < _pool.Count; i++)
                _pool[i].RefreshLocalization(_itemsGeneration, _categoryItems);

            UnifiedUIIntegration.RefreshLocalization();

            RefreshList();
        }

        private void RebuildCategoryItems()
        {
            var items = new List<string>();
            foreach (RoadCategory cat in Enum.GetValues(typeof(RoadCategory)))
                items.Add(Localization.GetCategoryName(cat));

            _categoryItems = items.ToArray();
            _itemsGeneration++;
        }

        private static string[] BuildFilterItems()
        {
            var items = new List<string>();
            items.Add(Localization.Get("MANAGER_FILTER_ALL"));
            foreach (RoadCategory cat in Enum.GetValues(typeof(RoadCategory)))
                items.Add(Localization.GetCategoryName(cat));
            return items.ToArray();
        }

        private void LoadRoads()
        {
            _allRoads = new List<NetInfo>();
            for (uint i = 0; i < PrefabCollection<NetInfo>.LoadedCount(); i++)
            {
                NetInfo info = PrefabCollection<NetInfo>.GetLoaded(i);
                if (info == null) continue;
                if (info.GetService() != ItemClass.Service.Road) continue;
                if (info.GetSubService() != ItemClass.SubService.None) continue;
                if (info.m_placementStyle != ItemClass.Placement.Manual) continue;

                if (info.m_netAI == null) continue;
                if (!(info.m_netAI is RoadBaseAI)) continue;

                if (GetLanes(info) <= 0) continue;

                _allRoads.Add(info);
            }

            // 排序用缓存好的显示名，避免 O(n log n) 次反射调用
            foreach (var info in _allRoads)
                GetDisplayName(info);

            _allRoads.Sort((a, b) => string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>按当前搜索词 / 分类筛选重建列表数据，并重新绑定可见行。</summary>
        public void RefreshList()
        {
            if (_scrollPanel == null)
                return;

            Stopwatch watch = Stopwatch.StartNew();

            BuildFilteredRows();
            long dataMs = watch.ElapsedMilliseconds;

            // 回到顶部（筛选后条数会变）
            _scrollPanel.scrollPosition = Vector2.zero;
            if (_scrollbar != null)
                _scrollbar.value = 0f;

            UpdateContentHeight();

            int needed = ComputeVisibleRowCount() + 3;
            EnsurePool(needed);

            _lastScrollY = -1f;
            UpdateVisibleRows(true);
            UpdateStatusText();

            Debug.Log(string.Format(
                "[RoadCategoryMod] 管理器列表: 道路 {0} 条, 显示 {1} 条, 数据 {2}ms, 视图 {3}ms, 行控件 {4} 个",
                _allRoads != null ? _allRoads.Count : 0,
                _rows.Count,
                dataMs,
                watch.ElapsedMilliseconds - dataMs,
                _pool.Count));
        }

        private void BuildFilteredRows()
        {
            _rows.Clear();
            if (_allRoads == null)
                return;

            string search = _searchState != null ? _searchState.AppliedSearch : "";

            foreach (var road in _allRoads)
            {
                string displayName = GetDisplayName(road);

                if (!string.IsNullOrEmpty(search))
                {
                    if (displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                        road.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                bool isManual;
                RoadCategory category = GetDisplayCategory(road, out isManual);

                if (_filterCategory >= 0 && (int)category != _filterCategory)
                    continue;

                _rows.Add(new RowData
                {
                    Info = road,
                    DisplayName = displayName,
                    Lanes = GetLanes(road),
                    Category = category,
                    IsManual = isManual
                });
            }
        }

        /// <summary>内容总高度（含行间距），用于滚动条范围。</summary>
        private void UpdateContentHeight()
        {
            if (_scrollbar == null)
                return;

            float viewport = PanelHeight - 145f;
            float content = ContentTop + _rows.Count * RowSpacing;
            _scrollbar.minValue = 0f;
            _scrollbar.maxValue = Mathf.Max(viewport, content);
            _scrollbar.scrollSize = viewport;   // 让滑块长度与可视区匹配（手动布局下不会自动算）
            _scrollbar.incrementAmount = RowSpacing;
        }

        /// <summary>可视区能显示的行数（+1 用于边界缓冲）。</summary>
        private int ComputeVisibleRowCount()
        {
            float viewport = PanelHeight - 145f;
            return Mathf.Max(1, Mathf.CeilToInt(viewport / RowSpacing) + 1);
        }

        private void EnsurePool(int count)
        {
            if (_scrollPanel == null)
                return;

            while (_pool.Count < count)
                _pool.Add(new RoadRowView(this, _scrollPanel));

            for (int i = count; i < _pool.Count; i++)
                _pool[i].SetVisible(false);
        }

        /// <summary>只绑定可视区内的行；这是虚拟化的核心。</summary>
        private void UpdateVisibleRows(bool force)
        {
            if (_scrollPanel == null)
                return;

            if (_rows.Count == 0)
            {
                for (int i = 0; i < _pool.Count; i++)
                    _pool[i].SetVisible(false);
                return;
            }

            float scrollY = _scrollPanel.scrollPosition.y;
            int first = Mathf.Max(0, Mathf.FloorToInt((scrollY - ContentTop) / RowSpacing));
            int visible = ComputeVisibleRowCount();

            for (int i = 0; i < _pool.Count; i++)
            {
                int index = first + i;
                if (index >= _rows.Count || i >= visible + 3)
                {
                    _pool[i].SetVisible(false);
                    continue;
                }

                _pool[i].SetVisible(true);
                _pool[i].SetY(ContentTop + index * RowSpacing - scrollY);

                // 数据变了才重新绑定（滚动时大多数行还是同一行）
                if (force || !ReferenceEquals(_pool[i].Data, _rows[index]))
                    _pool[i].Bind(_rows[index], _itemsGeneration, _categoryItems);
            }
        }

        private void UpdateStatusText()
        {
            if (_statusLabel == null)
                return;

            _statusLabel.text = Localization.Format(
                "MANAGER_STATUS",
                _allRoads != null ? _allRoads.Count : 0,
                _rows.Count);
        }

        // ==================== 行事件（由池中的行控件回调） ====================

        private void HandleRowReset(RoadRowView view, RowData data)
        {
            RoadCategoryConfig.RemoveManualCategory(data.Info.name);
            RoadCategoryConfig.Save();

            bool manualNow;
            RoadCategory autoCat = GetDisplayCategory(data.Info, out manualNow);
            view.RefreshAfterReset(autoCat, manualNow);

            if (_statusLabel != null)
                _statusLabel.text = Localization.Format("MANAGER_RESET_ONE", data.DisplayName);
        }

        private void HandleRowCategoryChanged(RoadRowView view, RowData data, RoadCategory category)
        {
            RoadCategoryConfig.SetManualCategory(data.Info.name, category);
            data.Category = category;
            data.IsManual = true;
            view.MarkManual();

            if (_statusLabel != null)
            {
                _statusLabel.text = Localization.Format(
                    "MANAGER_SET", data.DisplayName, RoadClassifier.GetCategoryName(category));
            }
        }

        // ==================== 数据辅助 ====================

        /// <summary>分类显示名（手动覆盖优先，自动分类结果缓存）。</summary>
        private RoadCategory GetDisplayCategory(NetInfo info, out bool isManual)
        {
            isManual = RoadCategoryConfig.HasManualCategory(info.name);
            if (isManual)
            {
                RoadCategory? manual = RoadCategoryConfig.GetManualCategory(info.name);
                if (manual.HasValue)
                    return manual.Value;
            }

            RoadCategory auto;
            if (_autoCategoryCache.TryGetValue(info, out auto))
                return auto;

            try
            {
                var cats = RoadClassifier.GetCategories(info);
                auto = cats.Count > 0 ? cats[0] : RoadCategory.OtherRoads;
            }
            catch (Exception)
            {
                auto = RoadCategory.OtherRoads;
            }

            _autoCategoryCache[info] = auto;
            return auto;
        }

        private int GetLanes(NetInfo info)
        {
            int lanes;
            if (_laneCache.TryGetValue(info, out lanes))
                return lanes;

            try
            {
                lanes = RoadClassifier.GetVehicleLaneCount(info);
            }
            catch (Exception)
            {
                lanes = 0;
            }

            _laneCache[info] = lanes;
            return lanes;
        }

        private string GetDisplayName(NetInfo info)
        {
            string name;
            if (_nameCache.TryGetValue(info, out name))
                return name;

            name = BuildDisplayName(info);
            _nameCache[info] = name;
            return name;
        }

        /// <summary>获取道路的友好显示名称（直接调用公开 API，不再反射）。</summary>
        private static string BuildDisplayName(NetInfo road)
        {
            try
            {
                string title = road.GetLocalizedTitle();
                if (!string.IsNullOrEmpty(title) && title != road.name)
                    return title;
            }
            catch (Exception)
            {
                // 忽略：退回基于内部名的处理
            }

            string name = road.name ?? string.Empty;
            if (name.EndsWith("_Data"))
                name = name.Substring(0, name.Length - 5);

            int dotIdx = name.IndexOf('.');
            if (dotIdx > 0)
            {
                string prefix = name.Substring(0, dotIdx);
                long num;
                if (long.TryParse(prefix, out num))
                    name = name.Substring(dotIdx + 1);
            }

            return name;
        }

        /// <summary>
        /// 搜索词变化后调用：只有真正需要变过滤条件时才重建列表。
        /// 点击搜索框 / 移出搜索框（占位符切换）都不会走到重建。
        /// </summary>
        private void ApplySearchIfChanged()
        {
            if (_searchState == null)
                return;

            if (_searchState.Consume())
                RefreshList();
        }

        private void SaveConfig()
        {
            RoadCategoryConfig.Save();
            if (_statusLabel != null)
                _statusLabel.text = Localization.Get("MANAGER_SAVED");
        }

        private void ResetAll()
        {
            var overrides = RoadCategoryConfig.GetAllOverrides();
            foreach (var ov in overrides.ToArray())
                RoadCategoryConfig.RemoveManualCategory(ov.RoadName);
            RoadCategoryConfig.Save();
            RefreshList();
            if (_statusLabel != null)
                _statusLabel.text = Localization.Get("MANAGER_RESET_ALL_DONE");
        }
    }
}
