using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ColossalFramework.UI;
using ICities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RoadCategoryMod
{
    /// <summary>
    /// 游戏“选项 - 模组设置”页面（IUserMod.OnSettingsUI）的构建器。
    ///
    /// 设计目标：
    /// 1. 全部分组、按钮、提示文字均来自 Localization，可随语言实时切换；
    /// 2. 只使用游戏自带的选项模板控件（按钮 / 复选框 / 滑块 / 下拉框），
    ///    说明文字用只读 UILabel 呈现，不再用“可编辑但无作用”的文本框冒充标签；
    /// 3. 语言切换后自动重建整个设置页，让新语言立即生效；
    /// 4. 所有按钮都有明确反馈（状态行），避免“点了没反应”。
    /// </summary>
    public static class ModSettingsUI
    {
        private static readonly Color32 InfoColor = new Color32(235, 240, 248, 255);
        private static readonly Color32 HintColor = new Color32(172, 182, 196, 255);
        private static readonly Color32 StatusColor = new Color32(140, 205, 255, 255);

        private const float HintTextScale = 0.9f;

        private static UILabel _statusLabel;
        private static UILabel _dataCountLabel;
        private static UIButton _clearButton;
        private static bool _clearConfirmPending;
        private static bool _layoutLogged;

        private static string L(string key)
        {
            return Localization.Get(key);
        }

        private static string F(string key, params object[] args)
        {
            return Localization.Format(key, args);
        }

        /// <summary>由 RoadCategoryMod.OnSettingsUI 调用。</summary>
        public static void Build(UIHelperBase helper)
        {
            if (helper == null)
                return;

            // 打开设置页时同步一次语言（auto 模式跟随游戏语言，绝不在语言事件回调里调用）
            Localization.Refresh();

            _statusLabel = null;
            _dataCountLabel = null;
            _clearButton = null;
            _clearConfirmPending = false;

            ModSettings settings = ModSettings.Current;

            BuildOverview(helper);
            BuildLanguage(helper);
            BuildFloatingButton(helper, settings);
            BuildManager(helper, settings);
            BuildIntegration(helper, settings);
            BuildHotkey(helper, settings);
            BuildIcons(helper);
            BuildData(helper);
        }

        /// <summary>
        /// 管理器分组：搜索框的过滤方式（实时过滤 / 仅回车·按钮触发）。
        /// </summary>
        private static void BuildManager(UIHelperBase helper, ModSettings settings)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_MANAGER"));
            if (group == null)
                return;

            group.AddCheckbox(L("SET_MANAGER_LIVE_SEARCH"), settings.SearchLive, isChecked =>
            {
                settings.SearchLive = isChecked;
                settings.Save();
                RoadCategoryManagerUI.RefreshIfOpen();
            });

            AddHint(group, L("SET_MANAGER_LIVE_SEARCH_HINT"));
        }

        // ==================== 各分组 ====================

        private static void BuildOverview(UIHelperBase helper)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_OVERVIEW"));
            if (group == null)
                return;

            AddInfo(group, F("SET_OVERVIEW_LINE1", GetModVersion()));
            AddHint(group, L("SET_OVERVIEW_LINE2"));
            AddHint(group, L("SET_OVERVIEW_LINE3"));

            group.AddSpace(4);
            group.AddButton(L("SET_BTN_OPEN_MANAGER"), OpenManager);
            group.AddButton(L("SET_BTN_OPEN_CONFIG"), OpenConfigFolder);
            group.AddSpace(4);

            _statusLabel = AddStatusLine(group);
        }

        private static void BuildLanguage(UIHelperBase helper)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_LANGUAGE"));
            if (group == null)
                return;

            string[] codes = Localization.LanguageOptionCodes;
            string[] labels = codes.Select(c => Localization.GetLanguageOptionLabel(c)).ToArray();

            int selected = Array.IndexOf(codes, Localization.SettingCode);
            if (selected < 0)
                selected = 0;

            group.AddDropdown(L("SET_LANGUAGE_LABEL"), labels, selected, index =>
            {
                if (index < 0 || index >= codes.Length)
                    return;

                string code = codes[index];
                if (code == Localization.SettingCode)
                    return;

                Localization.SetLanguage(code);
                // 浮动按钮的 tooltip 也要跟着换语言
                IndependentWindow.RefreshLocalization();
                UnifiedUIIntegration.RefreshLocalization();
                ScheduleOptionsRebuild();
            });

            AddHint(group, F("SET_LANGUAGE_CURRENT", Localization.GetLanguageNativeName(Localization.CurrentCode)));
            AddHint(group, L("SET_LANGUAGE_HINT"));
        }

        private static void BuildFloatingButton(UIHelperBase helper, ModSettings settings)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_FLOATING"));
            if (group == null)
                return;

            group.AddCheckbox(L("SET_FLOATING_ENABLE"), settings.EnableIndependentWindow, isChecked =>
            {
                settings.EnableIndependentWindow = isChecked;
                settings.Save();

                if (!isChecked)
                {
                    IndependentWindow.DestroyIconButton();
                    SetStatus(string.Empty);
                    return;
                }

                if (IsInGame())
                {
                    IndependentWindow.CreateIconButton();
                    IndependentWindow.ApplySettings(settings);
                    SetStatus(string.Empty);
                }
                else
                {
                    SetStatus(L("SET_FLOATING_IN_GAME_ONLY"));
                }
            });

            group.AddSlider(L("SET_FLOATING_SIZE"),
                ModSettings.MinButtonSize,
                ModSettings.MaxButtonSize,
                2f,
                settings.ButtonSize,
                value =>
                {
                    // 拖动滑块会连续触发回调，只在整数像素值真正变化时才落盘
                    float rounded = Mathf.Round(value);
                    if (Mathf.Abs(rounded - settings.ButtonSize) < 0.01f)
                        return;

                    settings.ButtonSize = rounded;
                    settings.Save();
                    IndependentWindow.ApplySettings(settings);
                });

            group.AddButton(L("SET_BTN_RESET_POSITION"), () =>
            {
                settings.ResetButtonPosition();
                IndependentWindow.ApplySettings(settings);
                SetStatus(L("SET_FLOATING_POSITION_RESET"));
            });

            AddHint(group, L("SET_FLOATING_HINT1"));
            AddHint(group, L("SET_FLOATING_HINT2"));
        }

        /// <summary>
        /// UnifiedUI 集成分组：把图标注册到 UnifiedUI 工具栏（可选，未安装 UnifiedUI 时不影响模组加载）。
        /// </summary>
        private static void BuildIntegration(UIHelperBase helper, ModSettings settings)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_INTEGRATION"));
            if (group == null)
                return;

            bool available = UnifiedUIIntegration.IsAvailable;

            group.AddCheckbox(L("SET_UUI_ENABLE"), settings.AddToUnifiedUI, isChecked =>
            {
                settings.AddToUnifiedUI = isChecked;
                settings.Save();

                if (!isChecked)
                {
                    bool removed = UnifiedUIIntegration.Unregister();
                    SetStatus(removed ? L("SET_UUI_DISABLED") : L("SET_UUI_RESTART_HINT"));
                    return;
                }

                if (!UnifiedUIIntegration.IsAvailable)
                {
                    SetStatus(L("SET_UUI_UNAVAILABLE"));
                    return;
                }

                if (!IsInGame())
                {
                    SetStatus(L("SET_UUI_IN_GAME_ONLY"));
                    return;
                }

                // 面板可能还没建好（UnifiedUI 在关卡加载后才建），那就等它就绪再注册
                bool ok = UnifiedUIIntegration.TryRegister();
                if (ok)
                {
                    SetStatus(L("SET_UUI_ENABLED"));
                    return;
                }

                if (UnifiedUIIntegration.LastAttemptFailed)
                {
                    SetStatus(L("SET_UUI_FAILED"));
                    return;
                }

                UnifiedUIIntegration.RequestRegisterWhenReady();
                SetStatus(L("SET_UUI_PENDING"));
            });

            AddHint(group, L("SET_UUI_HINT"));

            // 设置页重建时（例如切换语言后）如果已经勾选但还没注册成功，继续尝试
            if (settings.AddToUnifiedUI && !UnifiedUIIntegration.IsRegistered && IsInGame())
                UnifiedUIIntegration.RequestRegisterWhenReady();

            if (!available)
                AddHint(group, L("SET_UUI_UNAVAILABLE"));
        }

        private static void BuildHotkey(UIHelperBase helper, ModSettings settings)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_HOTKEY"));
            if (group == null)
                return;

            group.AddCheckbox(L("SET_HOTKEY_ENABLE"), settings.EnableHotkey, isChecked =>
            {
                settings.EnableHotkey = isChecked;
                settings.Save();
                HotkeyListener.SetEnabled(isChecked);
            });

            string[] targets = new string[] { ModSettings.HotkeyTargetOverview, ModSettings.HotkeyTargetManager };
            string[] labels = new string[]
            {
                L("SET_HOTKEY_TARGET_OVERVIEW"),
                L("SET_HOTKEY_TARGET_MANAGER")
            };

            int selected = Array.IndexOf(targets, settings.HotkeyTarget);
            if (selected < 0)
                selected = 0;

            group.AddDropdown(L("SET_HOTKEY_TARGET"), labels, selected, index =>
            {
                if (index < 0 || index >= targets.Length)
                    return;
                settings.HotkeyTarget = targets[index];
                settings.Save();
            });

            AddHint(group, L("SET_HOTKEY_HINT"));
        }

        private static void BuildIcons(UIHelperBase helper)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_ICONS"));
            if (group == null)
                return;

            AddHint(group, L("SET_ICONS_LINE1"));
            AddHint(group, L("SET_ICONS_LINE2"));
            AddHint(group, L("SET_ICONS_LINE3"));
            AddHint(group, L("SET_ICONS_LINE4"));
            AddHint(group, L("SET_ICONS_LINE5"));
            AddHint(group, L("SET_ICONS_LINE6"));

            group.AddSpace(4);
            group.AddButton(L("SET_BTN_OPEN_ICONS"), OpenIconsFolder);
        }

        private static void BuildData(UIHelperBase helper)
        {
            UIHelperBase group = helper.AddGroup(L("SET_GROUP_DATA"));
            if (group == null)
                return;

            _dataCountLabel = AddHint(group, F("SET_DATA_COUNT", CountOverrides()));
            AddHint(group, L("SET_DATA_HINT"));

            group.AddSpace(4);
            _clearButton = group.AddButton(L("SET_DATA_CLEAR"), OnClearAllClicked) as UIButton;
        }

        // ==================== 交互逻辑 ====================

        private static void OnClearAllClicked()
        {
            int count = CountOverrides();

            if (count <= 0)
            {
                SetStatus(L("SET_DATA_CLEARED_NOTHING"));
                return;
            }

            if (!_clearConfirmPending)
            {
                // 第一次点击只做确认提示，避免误删玩家手动分类
                _clearConfirmPending = true;
                if (_clearButton != null)
                    _clearButton.text = F("SET_DATA_CLEAR_CONFIRM", count);
                return;
            }

            ClearAllOverrides();
            _clearConfirmPending = false;

            if (_clearButton != null)
                _clearButton.text = L("SET_DATA_CLEAR");
            if (_dataCountLabel != null)
                _dataCountLabel.text = F("SET_DATA_COUNT", CountOverrides());

            SetStatus(L("SET_DATA_CLEARED"));
            RoadCategoryManagerUI.RefreshIfOpen();
        }

        private static int CountOverrides()
        {
            try
            {
                var overrides = RoadCategoryConfig.GetAllOverrides();
                return overrides == null ? 0 : overrides.Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void ClearAllOverrides()
        {
            try
            {
                // 先复制一份再删除，避免遍历过程中修改集合
                var names = RoadCategoryConfig.GetAllOverrides()
                    .Select(o => o.RoadName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToArray();

                foreach (var name in names)
                    RoadCategoryConfig.RemoveManualCategory(name);

                RoadCategoryConfig.Save();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 清除手动分类失败: " + e.Message);
            }
        }

        private static void OpenManager()
        {
            if (!IsInGame())
            {
                SetStatus(L("SET_OPEN_MANAGER_IN_GAME_ONLY"));
                return;
            }

            CloseOptionsPanel();
            RoadCategoryManagerUI.Toggle();
        }

        private static void OpenConfigFolder()
        {
            try
            {
                string path = RoadCategoryConfig.PlayerConfigPath;
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 打开配置文件夹失败: " + e.Message);
            }
        }

        private static void OpenIconsFolder()
        {
            try
            {
                string dir = CustomIconLoader.IconsFolder;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start(dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 打开图标文件夹失败: " + e.Message);
            }
        }

        private static bool IsInGame()
        {
            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game";
        }

        private static void CloseOptionsPanel()
        {
            try
            {
                UIView view = UIView.GetAView();
                if (view == null)
                    return;

                var panel = view.GetComponentInChildren<OptionsMainPanel>(true);
                if (panel != null)
                    panel.OnClosed();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 关闭选项面板失败: " + e.Message);
            }
        }

        /// <summary>
        /// 语言切换后重建整个模组设置页，让新语言立即生效。
        /// 通过延迟两帧调用游戏自身的 OptionsMainPanel.RefreshPlugins()，
        /// 避免在控件回调中销毁正在派发事件的 UI。
        /// </summary>
        private static void ScheduleOptionsRebuild()
        {
            try
            {
                var go = new GameObject("RCM_OptionsRebuild");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<OptionsRebuildTicker>();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 无法刷新设置界面: " + e.Message);
            }
        }

        private class OptionsRebuildTicker : MonoBehaviour
        {
            private int _frames;

            private void Update()
            {
                if (++_frames < 3)
                    return;

                try
                {
                    UIView view = UIView.GetAView();
                    if (view != null)
                    {
                        var panel = view.GetComponentInChildren<OptionsMainPanel>(true);
                        if (panel != null)
                        {
                            MethodInfo refresh = typeof(OptionsMainPanel).GetMethod(
                                "RefreshPlugins",
                                BindingFlags.Instance | BindingFlags.NonPublic);
                            if (refresh != null)
                            {
                                refresh.Invoke(panel, null);
                                // 重建后回到本模组的设置页
                                panel.SelectMod(L("MOD_NAME"));
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[RoadCategoryMod] 刷新设置界面失败: " + e.Message);
                }

                UnityEngine.Object.Destroy(gameObject);
            }
        }

        // ==================== 只读文字控件 ====================

        /// <summary>取 UIHelper 内部承载控件的容器面板。</summary>
        private static UIComponent GetContentRoot(UIHelperBase helper)
        {
            try
            {
                var uiHelper = helper as UIHelper;
                if (uiHelper == null)
                    return null;
                return uiHelper.self as UIComponent;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static UILabel AddInfo(UIHelperBase helper, string text)
        {
            return AddLabel(helper, text, InfoColor, 1f);
        }

        private static UILabel AddHint(UIHelperBase helper, string text)
        {
            return AddLabel(helper, text, HintColor, HintTextScale);
        }

        private static UILabel AddStatusLine(UIHelperBase helper)
        {
            return AddLabel(helper, string.Empty, StatusColor, HintTextScale);
        }

        /// <summary>
        /// 在设置分组内添加一行只读文字。
        /// 游戏没有提供“标签”控件，这里直接给容器面板加 UILabel；
        /// 若容器未启用自动布局，则手动向下排列，避免文字重叠。
        /// </summary>
        private static UILabel AddLabel(UIHelperBase helper, string text, Color32 color, float textScale)
        {
            UIComponent root = GetContentRoot(helper);
            if (root == null)
                return null;

            UILabel label = root.AddUIComponent<UILabel>();
            label.name = "RCM_Label";
            label.text = text ?? string.Empty;
            label.textColor = color;
            label.textScale = textScale;
            label.autoSize = true;
            label.wordWrap = false;
            label.padding = new RectOffset(6, 6, 1, 1);

            // 若容器不是自动布局面板，则手动向下排列，避免文字重叠
            var rootPanel = root as UIPanel;
            if (rootPanel == null || !rootPanel.autoLayout)
            {
                float y = 0f;
                foreach (UIComponent child in root.components)
                {
                    if (child == label)
                        continue;
                    y = Mathf.Max(y, child.relativePosition.y + child.height);
                }
                label.relativePosition = new Vector3(4f, y + 2f);
            }

            if (!_layoutLogged)
            {
                _layoutLogged = true;
                Debug.Log("[RoadCategoryMod] 设置页文字容器: name=" + root.name +
                    ", autoLayout=" + (rootPanel != null ? rootPanel.autoLayout.ToString() : "n/a"));
            }

            return label;
        }

        private static void SetStatus(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text ?? string.Empty;
        }

        private static string GetModVersion()
        {
            try
            {
                Version version = typeof(RoadCategoryMod).Assembly.GetName().Version;
                if (version == null)
                    return "1.0.0";
                return string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
            }
            catch (Exception)
            {
                return "1.0.0";
            }
        }
    }
}
