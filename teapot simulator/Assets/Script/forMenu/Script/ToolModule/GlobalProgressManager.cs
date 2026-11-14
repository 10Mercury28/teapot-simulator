using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using Yarn.Unity;

public class GlobalProgressManager : MonoBehaviour
{
    public static GlobalProgressManager Instance;

    [Header("📖 全局模块进度状态")]
    public int currentOrder = 0;            // 当前模块索引
    public int currentDay = 1;              // 当前天数 (1 = Day 1, 2 = Day 2)
    public bool sceneTransitioning = false; // 是否正在转场

    [Header("🤝 居士剧情与跨天状态")]
    [Tooltip("Day 1 居士对话中是否达成了'交心'（$LikeU == true）")]
    public bool jushiLikeU = false;
    public static bool IsJushiLikeUStatic = false;

    [Tooltip("Day 1 居士接待是否成交成功（$JushiSuccess == true）")]
    public bool jushiSuccess = false;
    public static bool IsJushiSuccessStatic = false;

    [Tooltip("Day 1 是否和居士聊过'蝉/摇滚'话题（$D1TalkedCicada == true）")]
    public bool talkedCicada = false;
    public static bool IsTalkedCicadaStatic = false;

    public static int CurrentDayStatic = 1;

    public static int CurrentDay
    {
        get => Instance != null ? Instance.currentDay : CurrentDayStatic;
        set
        {
            CurrentDayStatic = Mathf.Max(1, value);
            if (Instance != null) Instance.currentDay = CurrentDayStatic;
        }
    }

    public static bool IsLikeU
    {
        get
        {
            if (Instance != null && Instance.jushiLikeU) return true;
            return IsJushiLikeUStatic;
        }
        set
        {
            IsJushiLikeUStatic = value;
            if (Instance != null) Instance.jushiLikeU = value;
        }
    }

    public static bool JushiSuccess
    {
        get
        {
            if (Instance != null && Instance.jushiSuccess) return true;
            return IsJushiSuccessStatic;
        }
        set
        {
            IsJushiSuccessStatic = value;
            if (Instance != null) Instance.jushiSuccess = value;
        }
    }

    public static bool TalkedCicada
    {
        get
        {
            if (Instance != null && Instance.talkedCicada) return true;
            return IsTalkedCicadaStatic;
        }
        set
        {
            IsTalkedCicadaStatic = value;
            if (Instance != null) Instance.talkedCicada = value;
        }
    }

    [Header("🗺️ 模块场景顺序")]
    [Tooltip("完整顺序: choose(0) → pat(1) → StudioDesk D1(2) → Mindmaze(3) → choose(4) → cut(5) → StudioDesk D2(6) → end(7)")]
    public List<string> sceneSequence = new List<string>();

    [Header("🧩 调试选项")]
    public bool debugLog = true;

    private static readonly string[] DefaultFullSequence = new string[]
    {
        "chooseToolBasedOnCut", // 0: Day 1 选拍泥工具 (orderIndex = 0)
        "patScene",             // 1: Day 1 拍泥
        "StudioDesk",           // 2: Day 1 柜台接待 (Character_Jushi)
        "Mindmaze",             // 3: Day 1 入夜脑内迷宫 (MindMaze_Start)
        "chooseToolBasedOnCut", // 4: Day 2 选切壶工具 (orderIndex = 2)
        "cutScene",             // 5: Day 2 切壶
        "StudioDesk",           // 6: Day 2 柜台接待 (Character_Jushi_D2)
        "endScene"              // 7: 结局通关
    };

    // ----------------------------------------------------
    // 初始化与防护
    // ----------------------------------------------------
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 同步静态缓存（例如单场景直接测试时先写了静态属性再启动）
        if (IsJushiLikeUStatic) jushiLikeU = true;
        if (IsJushiSuccessStatic) jushiSuccess = true;
        if (IsTalkedCicadaStatic) talkedCicada = true;
        CurrentDayStatic = Mathf.Max(1, currentDay);

        DisableRenderingComponents();
        EnsureValidSceneSequence();
    }

    /// <summary>
    /// 确保 GlobalProgressManager 单例存在；若在编辑器中直接从任意子场景启动，自动创建并同步所在进度。
    /// </summary>
    public static GlobalProgressManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindObjectOfType<GlobalProgressManager>();
        if (Instance != null) return Instance;

        GameObject go = new GameObject("GlobalProgressManager (Auto)");
        Instance = go.AddComponent<GlobalProgressManager>();
        Instance.SyncStateFromDirectActiveScene(SceneManager.GetActiveScene().name);
        return Instance;
    }

    private void EnsureValidSceneSequence()
    {
        // 如果序列为空，或是早期未含 StudioDesk/Mindmaze 的旧 5 步占位序列，则自动升级为完整 8 步流程
        bool isLegacyFiveStep =
            sceneSequence != null &&
            sceneSequence.Count == 5 &&
            sceneSequence[0].Trim() == "chooseToolBasedOnCut" &&
            sceneSequence[1].Trim() == "patScene" &&
            sceneSequence[2].Trim() == "chooseToolBasedOnCut" &&
            sceneSequence[3].Trim() == "cutScene" &&
            sceneSequence[4].Trim() == "endScene";

        if (sceneSequence == null || sceneSequence.Count == 0 || isLegacyFiveStep)
        {
            sceneSequence = new List<string>(DefaultFullSequence);
            if (debugLog)
            {
                Debug.Log("🧭 [GlobalProgressManager] 已初始化完整 8 步场景序列：choose(D1) → pat → StudioDesk(D1) → Mindmaze → choose(D2) → cut → StudioDesk(D2) → end");
            }
            return;
        }

        for (int i = 0; i < sceneSequence.Count; i++)
        {
            if (string.IsNullOrEmpty(sceneSequence[i])) continue;
            sceneSequence[i] = sceneSequence[i].Trim();
            if (sceneSequence[i] == "patScene 1")
            {
                sceneSequence[i] = "patScene";
            }
        }
    }

    private void SyncStateFromDirectActiveScene(string sceneName)
    {
        switch (sceneName)
        {
            case "patScene":
                currentDay = 1;
                CurrentDayStatic = 1;
                currentOrder = 1;
                break;
            case "Mindmaze":
                currentDay = 1;
                CurrentDayStatic = 1;
                currentOrder = 3;
                break;
            case "cutScene":
                currentDay = 2;
                CurrentDayStatic = 2;
                currentOrder = 5;
                break;
            case "StudioDesk":
                currentOrder = CurrentDay >= 2 ? 6 : 2;
                break;
            case "chooseToolBasedOnCut":
                currentOrder = CurrentDay >= 2 ? 4 : 0;
                break;
            case "endScene":
                currentDay = 2;
                CurrentDayStatic = 2;
                currentOrder = 7;
                break;
        }

        if (debugLog)
        {
            Debug.Log($"🧩 [GlobalProgressManager] 检测到直接启动场景 '{sceneName}'，已自动同步进度：Day={currentDay}, currentOrder={currentOrder}");
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

        if (scene.name == "begin" || scene.name == "beginScene" || scene.name == "IntroScene")
        {
            Debug.Log("🔄 检测到返回起始页，自动重置全局进度...");
            ResetProgress();
        }
        else if (scene.name == "cutScene")
        {
            CurrentDay = 2;
        }

        if (debugLog)
            Debug.Log($"🔁 场景加载完成: {scene.name}, Day={currentDay}, 模块索引={currentOrder}");
    }

    // ----------------------------------------------------
    // 工具选区索引兼容映射
    // ----------------------------------------------------
    /// <summary>
    /// 返回 chooseToolBasedOnCut 场景当前应激活的 ChooseModule.orderIndex：
    /// Day 1 拍泥工具在场景中配置为 orderIndex = 0，Day 2 切壶工具为 orderIndex = 2。
    /// </summary>
    public int GetExpectedToolOrderIndex()
    {
        return CurrentDay >= 2 ? 2 : 0;
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

        EnsureValidSceneSequence();
        sceneTransitioning = true;
        currentOrder++;

        if (currentOrder >= sceneSequence.Count)
        {
            Debug.Log("🎉 全部流程完成！");
            sceneTransitioning = false;
            return;
        }

        // Day 2 阶段从 index 4 (chooseToolBasedOnCut Day 2) 开始
        if (currentOrder >= 4)
        {
            CurrentDay = 2;
        }
        else
        {
            CurrentDay = 1;
        }

        string nextScene = sceneSequence[currentOrder];
        string blackText = GetDefaultBlackTextForTransition(nextScene, currentOrder);
        Debug.Log($"🚀 [Global] 跳转到场景：{nextScene} (Day={currentDay}, Index={currentOrder}, BlackText='{blackText}')");

        StartSceneLoad(nextScene, blackText);
    }

    /// <summary>
    /// 由 Yarn 指令或其他脚本主动跳往指定场景，同时保持 Day / Order 全局状态对齐。
    /// </summary>
    public void TransitionToScene(string sceneName, string blackScreenText = "", int nextDay = 0)
    {
        if (sceneTransitioning)
        {
            Debug.Log("⚠️ [Global] 已在转场中，忽略重复调用。");
            return;
        }

        EnsureValidSceneSequence();
        sceneTransitioning = true;

        ApplySceneTargetState(sceneName, nextDay);
        string effectiveText = !string.IsNullOrEmpty(blackScreenText)
            ? blackScreenText
            : GetDefaultBlackTextForTransition(sceneName, currentOrder);

        Debug.Log($"🚀 [Global] 指定切场 → '{sceneName}' (Day={currentDay}, Index={currentOrder}, BlackText='{effectiveText}')");
        StartSceneLoad(sceneName, effectiveText);
    }

    /// <summary>
    /// 协程版指定切场：挂起直到黑屏完全覆盖画面（用于 Yarn <<load_scene>> 无缝接 <<stop>> 防 UI 提前消失）
    /// </summary>
    public IEnumerator TransitionToSceneWaitForCover(string sceneName, string blackScreenText = "", int nextDay = 0)
    {
        if (sceneTransitioning) yield break;

        EnsureValidSceneSequence();
        sceneTransitioning = true;

        ApplySceneTargetState(sceneName, nextDay);
        string effectiveText = !string.IsNullOrEmpty(blackScreenText)
            ? blackScreenText
            : GetDefaultBlackTextForTransition(sceneName, currentOrder);

        Debug.Log($"🚀 [Global] 协同切场 → '{sceneName}' (Day={currentDay}, Index={currentOrder}, BlackText='{effectiveText}')");

        SceneBlinkTransition trans = SceneBlinkTransition.EnsureInstance();
        if (!string.IsNullOrEmpty(effectiveText) || trans.HasBlackTextPrefab())
        {
            yield return trans.LoadSceneWithBlackTextWaitForCover(sceneName, effectiveText);
        }
        else
        {
            trans.LoadSceneConcealed(sceneName);
        }
    }

    private void ApplySceneTargetState(string sceneName, int nextDay)
    {
        if (nextDay > 0)
        {
            CurrentDay = nextDay;
        }
        else if (sceneName == "cutScene")
        {
            CurrentDay = 2;
        }
        else if (sceneName == "Mindmaze" || sceneName == "patScene")
        {
            CurrentDay = 1;
        }
        else if (sceneName == "StudioDesk" && SceneManager.GetActiveScene().name == "Mindmaze")
        {
            // MindMaze 第 5 段失败回到前一天重玩时，清空当天的居士状态以便重新经历
            CurrentDay = 1;
            IsLikeU = false;
            JushiSuccess = false;
            TalkedCicada = false;
        }

        switch (sceneName)
        {
            case "chooseToolBasedOnCut":
                currentOrder = CurrentDay >= 2 ? 4 : 0;
                break;
            case "patScene":
                currentOrder = 1;
                break;
            case "StudioDesk":
                currentOrder = CurrentDay >= 2 ? 6 : 2;
                break;
            case "Mindmaze":
                currentOrder = 3;
                break;
            case "cutScene":
                currentOrder = 5;
                break;
            case "endScene":
                currentOrder = 7;
                break;
        }
    }

    private string GetDefaultBlackTextForTransition(string sceneName, int targetOrder)
    {
        if (sceneName == "Mindmaze")
            return "入夜";
        if (sceneName == "chooseToolBasedOnCut" && (CurrentDay >= 2 || targetOrder >= 4))
            return "Day 2";
        return "";
    }

    private void StartSceneLoad(string nextScene, string blackText)
    {
        SceneBlinkTransition trans = SceneBlinkTransition.EnsureInstance();
        if (!string.IsNullOrEmpty(blackText))
        {
            trans.LoadSceneWithBlackText(nextScene, blackText);
        }
        else if (trans != null)
        {
            trans.LoadSceneConcealed(nextScene);
        }
        else
        {
            SceneManager.LoadScene(nextScene);
        }
    }

    // ----------------------------------------------------
    // Yarn 变量跨场景双向同步助手 (供 Day1YarnCommands 与 MindMaze 共用)
    // ----------------------------------------------------
    public static void SyncGlobalVarsToYarn(DialogueRunner runner)
    {
        if (runner == null || runner.VariableStorage == null) return;

        runner.VariableStorage.SetValue("$LikeU", IsLikeU);
        runner.VariableStorage.SetValue("$JushiSuccess", JushiSuccess);
        runner.VariableStorage.SetValue("$D1TalkedCicada", TalkedCicada);
    }

    public static void CaptureYarnVarsToGlobal(DialogueRunner runner)
    {
        if (runner == null || runner.VariableStorage == null) return;

        if ((runner.VariableStorage.Contains("$LikeU") || runner.VariableStorage.Program != null) &&
            runner.VariableStorage.TryGetValue("$LikeU", out bool likeU))
        {
            if (CurrentDay <= 1 || likeU)
            {
                IsLikeU = likeU;
            }
        }
        if ((runner.VariableStorage.Contains("$JushiSuccess") || runner.VariableStorage.Program != null) &&
            runner.VariableStorage.TryGetValue("$JushiSuccess", out bool jushiSuc))
        {
            if (CurrentDay <= 1 || jushiSuc)
            {
                JushiSuccess = jushiSuc;
            }
        }
        if ((runner.VariableStorage.Contains("$D1TalkedCicada") || runner.VariableStorage.Program != null) &&
            runner.VariableStorage.TryGetValue("$D1TalkedCicada", out bool cicada))
        {
            if (CurrentDay <= 1 || cicada)
            {
                TalkedCicada = cicada;
            }
        }
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
        CurrentDay = 1;
        sceneTransitioning = false;
        IsLikeU = false;
        JushiSuccess = false;
        TalkedCicada = false;

        if (debugLog)
            Debug.Log("🌍 GlobalProgressManager 已重置：currentOrder = 0, Day = 1, 剧情状态清空。");
    }

    // ----------------------------------------------------
    // 🚫 防止 GlobalControllers 场景被渲染
    // ----------------------------------------------------
    private void DisableRenderingComponents()
    {
        foreach (var cam in GetComponentsInChildren<Camera>(true))
        {
            cam.enabled = false;
        }

        foreach (var listener in GetComponentsInChildren<AudioListener>(true))
        {
            listener.enabled = false;
        }

        if (debugLog)
            Debug.Log("🛡️ 已禁用 GlobalControllers 内所有摄像机与 AudioListener，防止渲染画面。");
    }
}
