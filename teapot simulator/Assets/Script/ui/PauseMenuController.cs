using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 游戏内暂停菜单控制器：
/// 1. 支持 HUD 暂停按钮点击 与 键盘快捷键（默认 ESC）无缝呼出/关闭暂停。
/// 2. 支持多个 Resume 按钮（大按钮与小按钮同时响应恢复游戏）。
/// 3. 暂停时自动冻结时间（Time.timeScale = 0）、自动搜寻并暂停场上所有正在播放的 VideoPlayer、屏蔽交互层。
/// 4. 恢复时自动恢复时间（Time.timeScale = 1）、自动继续播放刚才暂停的视频、恢复交互层。
/// 5. 自动检索装配子节点，开箱即用。
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("=== 面板与按钮 ===")]
    [Tooltip("全屏暂停背景面板（包含 Resume 按钮等）")]
    public GameObject pauseBackground;

    [Tooltip("常驻在 HUD 画面上的暂停按钮（点击呼出暂停）")]
    public Button hudPauseButton;

    [Tooltip("恢复游戏大按钮")]
    public Button resumeButtonBig;

    [Tooltip("恢复游戏小按钮")]
    public Button resumeButtonSmall;

    [Header("=== 快捷键配置 ===")]
    [Tooltip("呼出与恢复暂停的键盘快捷键（默认 ESC）")]
    public KeyCode pauseKey = KeyCode.Escape;

    [Header("=== 交互屏蔽设置 ===")]
    [Tooltip("暂停期间临时禁用的物理交互 Layer 名称")]
    public string interactableLayerName = "GameplayInteractable";

    [Header("=== 可选视频播放器绑定 (可选，留空则自动全局查找) ===")]
    public List<VideoPlayer> extraVideoPlayers = new List<VideoPlayer>();

    [Header("=== 运行状态 (只读) ===")]
    [SerializeField] private bool isPaused = false;

    // 内部缓存：记录被暂停菜单打断的视频播放器，避免把本就未播放的视频唤醒
    private readonly HashSet<VideoPlayer> pausedVideos = new HashSet<VideoPlayer>();
    // 内部缓存：记录被暂停时屏蔽的碰撞体，恢复时仅恢复原本开启的碰撞体
    private readonly List<Collider2D> disabledColliders2D = new List<Collider2D>();
    private readonly List<Collider> disabledColliders3D = new List<Collider>();

    public bool IsPaused => isPaused;

    private void Awake()
    {
        AutoBindComponents();
        RegisterButtonEvents();
    }

    private void Start()
    {
        // 游戏开始时确保暂停界面隐藏、HUD 按钮显示、时间正常
        SetPauseState(false, isInit: true);
    }

    private void Update()
    {
        if (Input.GetKeyDown(pauseKey))
        {
            TogglePause();
        }
    }

    private void OnDestroy()
    {
        // 场景切换或销毁时，确保恢复正常时间尺度，防止污染后续场景
        if (isPaused)
        {
            Time.timeScale = 1f;
        }
    }

    // ==========================================
    // 自动装配与事件绑定
    // ==========================================

    public void AutoBindComponents()
    {
        Transform root = transform;

        // 1. 查找常驻 HUD 暂停按钮
        if (hudPauseButton == null)
        {
            Transform t = FindChildRecursive(root, "PauseButton");
            if (t != null) hudPauseButton = t.GetComponent<Button>();
        }

        // 2. 查找暂停全屏背景面板
        if (pauseBackground == null)
        {
            Transform t = FindChildRecursive(root, "Background");
            if (t != null) pauseBackground = t.gameObject;
        }

        // 3. 在 Background 下查找两个 Resume 按钮
        Transform searchRoot = pauseBackground != null ? pauseBackground.transform : root;

        if (resumeButtonBig == null)
        {
            Transform t = FindChildRecursive(searchRoot, "ResumeButton_Big");
            if (t != null) resumeButtonBig = t.GetComponent<Button>();
        }

        if (resumeButtonSmall == null)
        {
            Transform t = FindChildRecursive(searchRoot, "ResumeButton");
            if (t != null) resumeButtonSmall = t.GetComponent<Button>();
        }
    }

    private void RegisterButtonEvents()
    {
        if (hudPauseButton != null)
        {
            hudPauseButton.onClick.RemoveListener(PauseGame);
            hudPauseButton.onClick.AddListener(PauseGame);
        }

        if (resumeButtonBig != null)
        {
            resumeButtonBig.onClick.RemoveListener(ResumeGame);
            resumeButtonBig.onClick.AddListener(ResumeGame);
        }

        if (resumeButtonSmall != null)
        {
            resumeButtonSmall.onClick.RemoveListener(ResumeGame);
            resumeButtonSmall.onClick.AddListener(ResumeGame);
        }
    }

    // ==========================================
    // 暂停与恢复控制
    // ==========================================

    public void PauseGame() => SetPauseState(true);

    public void ResumeGame() => SetPauseState(false);

    public void TogglePause() => SetPauseState(!isPaused);

    public void SetPauseState(bool pause, bool isInit = false)
    {
        isPaused = pause;

        // 1. 控制 UI 显隐
        if (pauseBackground != null)
            pauseBackground.SetActive(pause);

        if (hudPauseButton != null)
            hudPauseButton.gameObject.SetActive(!pause);

        // 如果是初始化，仅重置 UI，不改动物理和视频状态
        if (isInit) return;

        // 2. 控制时间缩放
        Time.timeScale = pause ? 0f : 1f;

        // 3. 视频与交互逻辑
        if (pause)
        {
            Debug.Log("⏸️ [PauseMenu] 游戏已暂停 (TimeScale = 0)");
            PauseActiveVideos();
            DisableGameplayInteraction();
        }
        else
        {
            Debug.Log("▶️ [PauseMenu] 游戏已恢复 (TimeScale = 1)");
            ResumePausedVideos();
            EnableGameplayInteraction();
        }
    }

    // ==========================================
    // 视频冻结与恢复
    // ==========================================

    private void PauseActiveVideos()
    {
        pausedVideos.Clear();

        // 收集所有需要暂停的视频（场景中的所有 VideoPlayer + 额外指定列表）
        var candidates = new HashSet<VideoPlayer>(extraVideoPlayers);
        VideoPlayer[] sceneVps = FindObjectsOfType<VideoPlayer>();
        foreach (var vp in sceneVps)
        {
            if (vp != null) candidates.Add(vp);
        }

        foreach (var vp in candidates)
        {
            if (vp != null && vp.isPlaying)
            {
                vp.Pause();
                pausedVideos.Add(vp);
            }
        }

        if (pausedVideos.Count > 0)
        {
            Debug.Log($"📹 [PauseMenu] 已自动暂停 {pausedVideos.Count} 个正在播放的视频");
        }
    }

    private void ResumePausedVideos()
    {
        foreach (var vp in pausedVideos)
        {
            if (vp != null)
            {
                vp.Play();
            }
        }

        if (pausedVideos.Count > 0)
        {
            Debug.Log($"📹 [PauseMenu] 已自动继续 {pausedVideos.Count} 个视频播放");
        }

        pausedVideos.Clear();
    }

    // ==========================================
    // 交互阻断与恢复
    // ==========================================

    private void DisableGameplayInteraction()
    {
        disabledColliders2D.Clear();
        disabledColliders3D.Clear();

        int layer = LayerMask.NameToLayer(interactableLayerName);
        if (layer == -1) return;

        foreach (var col in FindObjectsOfType<Collider2D>())
        {
            if (col != null && col.gameObject.layer == layer && col.enabled)
            {
                disabledColliders2D.Add(col);
                col.enabled = false;
            }
        }

        foreach (var col in FindObjectsOfType<Collider>())
        {
            if (col != null && col.gameObject.layer == layer && col.enabled)
            {
                disabledColliders3D.Add(col);
                col.enabled = false;
            }
        }
    }

    private void EnableGameplayInteraction()
    {
        foreach (var col in disabledColliders2D)
        {
            if (col != null) col.enabled = true;
        }
        disabledColliders2D.Clear();

        foreach (var col in disabledColliders3D)
        {
            if (col != null) col.enabled = true;
        }
        disabledColliders3D.Clear();
    }

    // ==========================================
    // 辅助工具方法
    // ==========================================

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
