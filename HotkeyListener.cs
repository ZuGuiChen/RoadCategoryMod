using System;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// Ctrl+Shift+Y 热键监听。
    ///
    /// 与旧版不同：热键不再依附于“启用浮动按钮”，而是由设置项独立控制，
    /// 并且可以在“道路总览窗口”和“道路分类管理器”之间切换。
    /// </summary>
    public class HotkeyListener : MonoBehaviour
    {
        private static GameObject _host;

        /// <summary>启用/停用热键监听（可重复调用）。</summary>
        public static void SetEnabled(bool enabled)
        {
            if (enabled)
                Ensure();
            else
                DestroyHost();
        }

        public static void Ensure()
        {
            if (_host != null)
                return;

            try
            {
                _host = new GameObject("RCM_Hotkey");
                _host.AddComponent<HotkeyListener>();
                UnityEngine.Object.DontDestroyOnLoad(_host);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 创建热键监听失败: " + e.Message);
                _host = null;
            }
        }

        public static void DestroyHost()
        {
            if (_host != null)
            {
                UnityEngine.Object.Destroy(_host);
                _host = null;
            }
        }

        private void Update()
        {
            try
            {
                if (RoadCategoryMod.IsInGameScene())
                {
                    bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                    bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                    if (ctrl && shift && Input.GetKeyDown(KeyCode.Y))
                    {
                        if (ModSettings.Current.HotkeyTarget == ModSettings.HotkeyTargetManager)
                            RoadCategoryManagerUI.Toggle();
                        else
                            IndependentWindow.ToggleWindow();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 热键处理失败: " + e.Message);
            }
        }
    }
}
