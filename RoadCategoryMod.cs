using System;
using ICities;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 模组入口。
    /// 名称/描述与设置页均通过 Localization 输出，支持 9 种游戏官方语言。
    /// </summary>
    public class RoadCategoryMod : IUserMod
    {
        public string Name
        {
            get
            {
                // 内容管理器读取名称时顺便同步语言（auto 模式下跟随游戏语言）
                Localization.Refresh();
                return Localization.Get("MOD_NAME");
            }
        }

        public string Description
        {
            get
            {
                Localization.Refresh();
                return Localization.Get("MOD_DESCRIPTION");
            }
        }

        /// <summary>
        /// 模组启用时调用。在这里应用 Harmony 补丁，
        /// 确保道路面板初始化之前补丁就已生效。
        /// </summary>
        public void OnEnabled()
        {
            LogInstallationInfo();
            LogDuplicateInstallations();

            try
            {
                Patcher.PatchAll();
                Debug.Log("[RoadCategoryMod] Harmony patches applied in OnEnabled.");
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] OnEnabled patch failed: " + e.Message + "\n" + e.StackTrace);
            }

            // 切换界面语言时，主动刷新道路面板上自定义分类 tab 的 tooltip
            //（游戏不会因为换语言而重新填充道路面板，不刷新就会一直显示旧语言）
            try
            {
                Localization.LanguageChanged -= RoadGroupTabs.RefreshAll;
                Localization.LanguageChanged += RoadGroupTabs.RefreshAll;
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        /// <summary>记录当前生效的版本与安装路径，便于排查问题。</summary>
        private static void LogInstallationInfo()
        {
            try
            {
                var asm = typeof(RoadCategoryMod).Assembly;
                Debug.Log("[RoadCategoryMod] 版本 " + asm.GetName().Version + " 已启用，路径: " + SafeLocation(asm));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 检测同一模组是否被加载了多份（例如本地 Addons 里一份 + 工坊订阅一份）。
        /// 两份会各自打补丁、各自创建按钮，还可能出现“旧版覆盖新版”导致图标丢失。
        /// </summary>
        private static void LogDuplicateInstallations()
        {
            try
            {
                var mine = typeof(RoadCategoryMod).Assembly;
                var name = mine.GetName().Name;
                var others = new System.Collections.Generic.List<string>();

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        if (asm == mine) continue;
                        if (asm.GetName().Name != name) continue;
                        others.Add(asm.GetName().Version + " @ " + SafeLocation(asm));
                    }
                    catch (Exception)
                    {
                    }
                }

                if (others.Count == 0)
                    return;

                Debug.LogWarning("[RoadCategoryMod] 检测到重复安装（同一模组共加载 " + (others.Count + 1) + " 份）：\n" +
                                 "  当前: " + mine.GetName().Version + " @ " + SafeLocation(mine) + "\n" +
                                 "  其它: " + string.Join("\n        ", others.ToArray()) + "\n" +
                                 "  请只保留一份（本地 Addons\\Mods\\RoadCategoryMod 或创意工坊订阅之一），" +
                                 "否则会出现图标缺失、浮动按钮重复等问题。");
            }
            catch (Exception)
            {
            }
        }

        private static string SafeLocation(System.Reflection.Assembly asm)
        {
            try
            {
                string loc = asm.Location;
                return string.IsNullOrEmpty(loc) ? "(路径不可用)" : loc;
            }
            catch (Exception)
            {
                return "(路径不可用)";
            }
        }

        public void OnDisabled()
        {
            try
            {
                Localization.LanguageChanged -= RoadGroupTabs.RefreshAll;
                Patcher.UnpatchAll();
                HotkeyListener.DestroyHost();
                IndependentWindow.DestroyIconButton();
            }
            catch (Exception)
            {
                // 忽略：关闭模组时不应抛出异常
            }
        }

        /// <summary>
        /// 游戏“选项 - 模组设置”页面。具体内容见 ModSettingsUI。
        /// </summary>
        public void OnSettingsUI(UIHelperBase helper)
        {
            try
            {
                ModSettingsUI.Build(helper);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] OnSettingsUI failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>当前是否处于游戏内（主菜单/编辑器返回 false）。</summary>
        public static bool IsInGameScene()
        {
            try
            {
                return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Game";
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public class ModLoading : LoadingExtensionBase
    {
        public override void OnLevelLoaded(LoadMode mode)
        {
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame)
                return;

            // 确保补丁已应用（OnEnabled 可能因时机问题未成功）
            try
            {
                Patcher.PatchAll();
            }
            catch (Exception)
            {
                // 忽略：补丁失败不应阻止关卡加载
            }

            try
            {
                // 进入城市后重新解析语言（auto 模式跟随游戏语言）
                Localization.Refresh();

                ModSettings settings = ModSettings.Current;

                // 热键由设置独立控制（不再依赖浮动按钮）
                HotkeyListener.SetEnabled(settings.EnableHotkey);

                // 浮动按钮
                if (settings.EnableIndependentWindow)
                {
                    IndependentWindow.CreateIconButton();
                    IndependentWindow.ApplySettings(settings);
                }

                // 可选：注册到 UnifiedUI 工具栏
                // 注意：UnifiedUI 的面板由它自己的 LifeCycle 创建，我们可能比它先跑，
                // 所以这里只“请求注册”，等面板就绪后再真正注册（不会抢着创建面板）。
                if (settings.AddToUnifiedUI)
                {
                    try
                    {
                        UnifiedUIIntegration.RequestRegisterWhenReady();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[RoadCategoryMod] 请求注册 UnifiedUI 按钮失败: " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] OnLevelLoaded 初始化失败: " + e.Message);
            }
        }

        public override void OnLevelUnloading()
        {
            try
            {
                HotkeyListener.DestroyHost();
                IndependentWindow.DestroyIconButton();
            }
            catch (Exception)
            {
                // 忽略
            }
        }
    }
}
