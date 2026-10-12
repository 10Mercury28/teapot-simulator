using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class SceneBlinkTransition : MonoBehaviour
{
    public static SceneBlinkTransition Instance;

    [Header("转场设置")]
    [Tooltip("切换场景前闭眼的持续时间 (秒)")]
    public float closeDuration = 0.5f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 🌑 闭眼后切换场景（保持黑屏）
    /// </summary>
    public void LoadSceneConcealed(string sceneName)
    {
        StartCoroutine(LoadSceneConcealedRoutine(sceneName));
    }

    private IEnumerator LoadSceneConcealedRoutine(string sceneName)
    {
        BlinkMaskController blink = FindObjectOfType<BlinkMaskController>();

        if (blink != null)
        {
            Debug.Log("🎬 执行闭眼并保持黑屏...");
            // 执行闭眼动画，不睁眼
            yield return StartCoroutine(blink.ForceCloseEyesOnly(closeDuration));
        }

        // 异步加载下一个场景（黑屏中）
        AsyncOperation asyncLoad = null;
        try
        {
            asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneBlinkTransition] 加载场景 '{sceneName}' 异常: {ex.Message}");
        }

        if (asyncLoad == null)
        {
            Debug.LogError($"❌ [SceneBlinkTransition] 无法加载场景 '{sceneName}' (未在 Build Settings 中添加或场景尚未制作)。正在恢复画面防永久黑屏卡死！");
            if (blink != null)
            {
                blink.ForceOpenEyes(0.1f);
            }
            yield break;
        }

        asyncLoad.allowSceneActivation = false;

        while (!asyncLoad.isDone)
        {
            if (asyncLoad.progress >= 0.9f)
                asyncLoad.allowSceneActivation = true;
            yield return null;
        }

        Debug.Log($"🌑 已加载新场景：{sceneName}（保持黑屏）");
    }

}