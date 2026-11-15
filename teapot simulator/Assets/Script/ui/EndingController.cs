using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// 新结局场景控制器：
/// 管理 Ending 场景的显示与 Replay 重玩/返回主菜单功能。
/// 点击 ConfirmButton 后，重置全局游戏进度（GlobalProgressManager），
/// 并跳转至指定场景（默认 Build Index 0 = MainMenu，也可设为 1 直接开始新一局游戏）。
/// </summary>
public class EndingController : MonoBehaviour
{
    [Header("=== 按钮绑定 ===")]
    [Tooltip("重玩/确认按钮（默认自动按名称绑定 ConfirmButton）")]
    public Button confirmButton;

    [Header("=== 跳转设置 ===")]
    [Tooltip("是否通过 Build Index 加载目标场景")]
    public bool loadByIndex = true;

    [Tooltip("重玩跳转的目标场景 Build Index（默认 0 = MainMenu，也可设为 1 直接开始新一局）")]
    public int targetBuildIndex = 0;

    [Tooltip("重玩跳转的目标场景名（若 loadByIndex 为 false 时使用）")]
    public string targetSceneName = "MainMenu";

    [Header("=== 重置设置 ===")]
    [Tooltip("是否在点击 Replay 时完全重置全局游戏进度")]
    public bool resetGlobalProgress = true;

    private void Awake()
    {
        AutoBindComponents();
        RegisterEvents();
    }

    public void AutoBindComponents()
    {
        if (confirmButton == null)
        {
            Transform found = FindChildRecursive(transform, "ConfirmButton");
            if (found != null)
            {
                confirmButton = found.GetComponent<Button>();
            }
        }
    }

    private void RegisterEvents()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(OnReplayClicked);
            confirmButton.onClick.AddListener(OnReplayClicked);
        }
    }

    /// <summary>
    /// 点击 ConfirmButton 触发 Replay 逻辑
    /// </summary>
    public void OnReplayClicked()
    {
        Debug.Log("🔄 [EndingController] 点击 Replay/Confirm，重置游戏进度并准备跳转...");

        // 1. 确保时间流速正常
        Time.timeScale = 1f;

        // 2. 重置全局进度与单例
        if (resetGlobalProgress)
        {
            if (GlobalProgressManager.Instance != null)
            {
                GlobalProgressManager.Instance.ResetProgress();
            }

            foreach (var mgr in FindObjectsOfType<GlobalProgressManager>())
            {
                Destroy(mgr.gameObject);
            }
        }

        // 3. 跳转到目标场景
        if (loadByIndex)
        {
            Debug.Log($"🚀 [EndingController] 加载目标场景 Build Index: {targetBuildIndex}");
            SceneManager.LoadScene(targetBuildIndex);
        }
        else
        {
            Debug.Log($"🚀 [EndingController] 加载目标场景名称: {targetSceneName}");
            SceneManager.LoadScene(targetSceneName);
        }
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null) return null;
        if (parent.name == childName) return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }
}
