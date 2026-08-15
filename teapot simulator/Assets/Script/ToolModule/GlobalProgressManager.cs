using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class GlobalProgressManager : MonoBehaviour
{
    public static GlobalProgressManager Instance;

    [Header("📖 全局模块进度状态")]
    public int currentOrder = 0;            // 当前模块索引
    public bool sceneTransitioning = false; // 是否正在转场

    [Header("🗺️ 模块场景顺序")]
    [Tooltip("模块顺序: choose → pat → choose → cut → choose → end")]
    public List<string> sceneSequence = new List<string>();

    [Header("🧩 调试选项")]
    public bool debugLog = true;

    // ----------------------------------------------------
    // 初始化与防护
    // ----------------------------------------------------
    void Awake()
    {
        // 防止多实例
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // ✅ 防止 GlobalControllers 场景被误渲染
        DisableRenderingComponents();

        // 初始化默认场景顺序
        if (sceneSequence == null || sceneSequence.Count == 0)
        {
            sceneSequence = new List<string>()
            {
                "chooseToolBasedOnCut",  // 0
                "patScene",              // 1
                "chooseToolBasedOnCut",  // 2
                "cutScene",              // 3
                "chooseToolBasedOnCut",  // 4
                "endScene"               // 5
            };

            if (debugLog)
                Debug.Log("🧭 自动初始化场景顺序：choose → pat → choose → cut → choose → end");
        }
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ----------------------------------------------------
    // 场景加载回调
    // ----------------------------------------------------
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        sceneTransitioning = false;

        // ✅ 若回到起始页则自动重置全局状态
        if (scene.name == "beginScene" || scene.name == "IntroScene")
        {
            Debug.Log("🔄 检测到返回起始页，自动重置全局进度...");
            ResetProgress();
        }

        if (debugLog)
            Debug.Log($"🔁 场景加载完成: {scene.name}, 当前模块索引 = {currentOrder}");
    }

    // ----------------------------------------------------
    // 核心逻辑：前进到下一个模块
    // ----------------------------------------------------
    public void AdvanceOrder()
    {
        if (sceneTransitioning)
        {
            Debug.Log("⚠️ [Global] 已在转场中，忽略重复调用。");
            return;
        }

        sceneTransitioning = true;
        currentOrder++;

        if (currentOrder >= sceneSequence.Count)
        {
            Debug.Log("🎉 全部流程完成！");
            return;
        }

        string nextScene = sceneSequence[currentOrder];
        Debug.Log($"🚀 [Global] 跳转到场景：{nextScene} (Index={currentOrder})");

        if (SceneBlinkTransition.Instance != null)
            SceneBlinkTransition.Instance.LoadSceneConcealed(nextScene);
        else
            SceneManager.LoadScene(nextScene);
    }

    // ----------------------------------------------------
    // 手动跳转场景（调试/备用）
    // ----------------------------------------------------
    public void ForceLoad(string sceneName)
    {
        if (debugLog) Debug.Log($"⚡ 强制跳转至: {sceneName}");
        SceneManager.LoadScene(sceneName);
    }

    // ----------------------------------------------------
    // 🧹 全局重置函数
    // ----------------------------------------------------
    public void ResetProgress()
    {
        currentOrder = 0;
        sceneTransitioning = false;

        if (debugLog)
            Debug.Log("🌍 GlobalProgressManager 已重置：currentOrder = 0, 转场标记清空。");
    }

    // ----------------------------------------------------
    // 🚫 防止 GlobalControllers 场景被渲染
    // ----------------------------------------------------
    private void DisableRenderingComponents()
    {
        // 禁用场景中所有摄像机
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            cam.enabled = false;
        }

        // 禁用音频监听器
        foreach (var listener in GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }

        if (debugLog)
            Debug.Log("🛡️ 已禁用 GlobalControllers 内所有摄像机与 AudioListener，防止渲染画面。");
    }
}
