using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 主菜单总控制器：管理 Main, NewGame, Archive, Settings 四个面板的显示与切换，
/// 并提供中英文语言切换（Sprite 动态轮换）以及开始游戏、退出功能。
/// 支持在 Inspector 手动拖拽绑定，也支持在 Awake 中自动根据节点名称装配。
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [Header("=== 核心面板 (四兄弟) ===")]
    [Tooltip("主界面背景面板")]
    public GameObject mainBackground;
    [Tooltip("新建游戏确认面板")]
    public GameObject newGameBackground;
    [Tooltip("档案室面板")]
    public GameObject archiveBackground;
    [Tooltip("系统设置面板")]
    public GameObject settingBackground;

    [Header("=== 主界面 5 个按钮 ===")]
    public Button newGameButton;
    public Button archiveButton;
    public Button settingsButton;
    public Button startGameButton;
    public Button exitButton;

    [Header("=== 弹窗确认按钮 ===")]
    public Button newGameConfirmButton;
    public Button archiveConfirmButton;
    public Button settingConfirmButton;

    [Header("=== 语言切换设置 ===")]
    public Button languageButton;
    public Image languageButtonImage;
    [Tooltip("中文状态对应的按钮贴图")]
    public Sprite chineseSprite;
    [Tooltip("英文状态对应的按钮贴图")]
    public Sprite englishSprite;

    [Header("=== 场景跳转配置 ===")]
    [Tooltip("是否优先使用 Build Index 加载场景（默认 true）")]
    public bool loadByIndex = true;
    [Tooltip("点击 StartGame 跳转的目标场景 Build Index（默认 1）")]
    public int startSceneBuildIndex = 1;
    [Tooltip("点击 StartGame 跳转的目标场景名（若 loadByIndex 为 false 时使用）")]
    public string startSceneName = "chooseToolBasedOnCut";

    [Header("=== 开场动画 (Intro Cutscene) ===")]
    [Tooltip("点击开始游戏后是否播放开场 Intro 视频（测试时可取消勾选一键跳过）")]
    public bool playIntroCutscene = true;
    [Tooltip("中文版 Intro 视频 (Assets/IntroCutscene/ch.mp4)")]
    public VideoClip introVideoChinese;
    [Tooltip("英文版 Intro 视频 (Assets/IntroCutscene/eng.mp4)")]
    public VideoClip introVideoEnglish;
    [Tooltip("Intro 播完后黑屏转场显示的字幕文字")]
    public string firstDayBlackText = "第一天";
    [Tooltip("英文模式下的黑屏字幕文字（留空则沿用 firstDayBlackText）")]
    public string firstDayBlackTextEnglish = "Day 1";
    [Tooltip("黑屏文字转场预制体（留空则由 SceneBlinkTransition 自动加载；挂上后打包版也能稳定生效）")]
    public GameObject blackTextTransitionPrefab;

    private bool isStartingGame = false;
    private GameObject runtimeIntroCanvasObj;
    private RenderTexture runtimeIntroRT;

    private void Awake()
    {
        AutoBindComponents();
        RegisterEvents();
    }

    private void Start()
    {
        // 初始状态：只激活主界面，隐藏三个弹窗
        if (mainBackground != null) mainBackground.SetActive(true);
        if (newGameBackground != null) newGameBackground.SetActive(false);
        if (archiveBackground != null) archiveBackground.SetActive(false);
        if (settingBackground != null) settingBackground.SetActive(false);

        // 读取已保存的语言偏好 (0 = 中文, 1 = 英文)
        int currentLang = PlayerPrefs.GetInt("OP.UI.Language", OrientPearlLanguage.Current);
        OrientPearlLanguage.Select(currentLang);
        UpdateLanguageSprite(currentLang);
    }

    /// <summary>
    /// 若 Inspector 未手动分配，自动根据节点名称在层级中查找
    /// </summary>
    public void AutoBindComponents()
    {
        Transform canvasTrans = transform;

        // 1. 查找面板
        if (mainBackground == null) mainBackground = FindChildGameObject(canvasTrans, "Main_Background");
        if (newGameBackground == null) newGameBackground = FindChildGameObject(canvasTrans, "NewGame_Background");
        if (archiveBackground == null) archiveBackground = FindChildGameObject(canvasTrans, "Archive_Background");
        if (settingBackground == null) settingBackground = FindChildGameObject(canvasTrans, "Setting_Background");

        // 2. 查找主按钮
        if (mainBackground != null)
        {
            if (newGameButton == null) newGameButton = FindChildComponent<Button>(mainBackground.transform, "NewGame");
            if (archiveButton == null) archiveButton = FindChildComponent<Button>(mainBackground.transform, "Archive");
            if (settingsButton == null) settingsButton = FindChildComponent<Button>(mainBackground.transform, "Settings");
            if (startGameButton == null) startGameButton = FindChildComponent<Button>(mainBackground.transform, "StartGame");
            if (exitButton == null) exitButton = FindChildComponent<Button>(mainBackground.transform, "Exit");
        }

        // 3. 查找弹窗确认按钮
        if (newGameBackground != null && newGameConfirmButton == null)
            newGameConfirmButton = FindChildComponent<Button>(newGameBackground.transform, "ConfirmButton");

        if (archiveBackground != null && archiveConfirmButton == null)
            archiveConfirmButton = FindChildComponent<Button>(archiveBackground.transform, "ConfirmButton");

        if (settingBackground != null)
        {
            if (settingConfirmButton == null)
                settingConfirmButton = FindChildComponent<Button>(settingBackground.transform, "ConfirmButton");

            if (languageButton == null)
                languageButton = FindChildComponent<Button>(settingBackground.transform, "LanguageButton");
        }

        if (languageButton != null && languageButtonImage == null)
        {
            languageButtonImage = languageButton.GetComponent<Image>();
        }
    }

    private void RegisterEvents()
    {
        // 主界面 5 个按钮
        if (newGameButton != null)
        {
            newGameButton.onClick.RemoveListener(OpenNewGame);
            newGameButton.onClick.AddListener(OpenNewGame);
        }

        if (archiveButton != null)
        {
            archiveButton.onClick.RemoveListener(OpenArchive);
            archiveButton.onClick.AddListener(OpenArchive);
        }

        if (settingsButton != null)
        {
            settingsButton.onClick.RemoveListener(OpenSettings);
            settingsButton.onClick.AddListener(OpenSettings);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartGame);
            startGameButton.onClick.AddListener(StartGame);
        }

        if (exitButton != null)
        {
            exitButton.onClick.RemoveListener(QuitGame);
            exitButton.onClick.AddListener(QuitGame);
        }

        // 弹窗确认按钮
        if (newGameConfirmButton != null)
        {
            newGameConfirmButton.onClick.RemoveListener(CloseNewGame);
            newGameConfirmButton.onClick.AddListener(CloseNewGame);
        }

        if (archiveConfirmButton != null)
        {
            archiveConfirmButton.onClick.RemoveListener(CloseArchive);
            archiveConfirmButton.onClick.AddListener(CloseArchive);
        }

        if (settingConfirmButton != null)
        {
            settingConfirmButton.onClick.RemoveListener(CloseSettings);
            settingConfirmButton.onClick.AddListener(CloseSettings);
        }

        // 语言切换按钮
        if (languageButton != null)
        {
            languageButton.onClick.RemoveListener(ToggleLanguage);
            languageButton.onClick.AddListener(ToggleLanguage);
        }
    }

    // ==========================================
    // 面板显隐交互
    // ==========================================

    public void OpenNewGame()
    {
        if (newGameBackground != null) newGameBackground.SetActive(true);
    }

    public void CloseNewGame()
    {
        if (newGameBackground != null) newGameBackground.SetActive(false);
    }

    public void OpenArchive()
    {
        if (archiveBackground != null) archiveBackground.SetActive(true);
    }

    public void CloseArchive()
    {
        if (archiveBackground != null) archiveBackground.SetActive(false);
    }

    public void OpenSettings()
    {
        if (settingBackground != null) settingBackground.SetActive(true);
    }

    public void CloseSettings()
    {
        if (settingBackground != null) settingBackground.SetActive(false);
    }

    // ==========================================
    // 语言切换 (0: 中文, 1: 英文)
    // ==========================================

    public void ToggleLanguage()
    {
        int current = OrientPearlLanguage.Current;
        int next = (current == 0) ? 1 : 0;

        OrientPearlLanguage.Select(next);
        PlayerPrefs.SetInt("OP.UI.Language", next);
        PlayerPrefs.Save();

        UpdateLanguageSprite(next);
        Debug.Log($"🌐 [MainMenu] 语言已切换为: {(next == 0 ? "简体中文" : "English")}");
    }

    public void UpdateLanguageSprite(int langIndex)
    {
        if (languageButtonImage == null) return;

        if (langIndex == 0)
        {
            if (chineseSprite != null) languageButtonImage.sprite = chineseSprite;
        }
        else
        {
            if (englishSprite != null) languageButtonImage.sprite = englishSprite;
        }
    }

    // ==========================================
    // 游戏流程与退出
    // ==========================================

    public void StartGame()
    {
        string targetScene = ResolveTargetSceneName(startSceneBuildIndex);
        BeginStartGameFlow(targetScene);
    }

    public void StartGameByIndex(int sceneIndex)
    {
        string targetScene = ResolveSceneNameByBuildIndex(sceneIndex);
        BeginStartGameFlow(targetScene);
    }

    private string ResolveTargetSceneName(int buildIndex)
    {
        if (loadByIndex)
        {
            return ResolveSceneNameByBuildIndex(buildIndex);
        }
        return !string.IsNullOrEmpty(startSceneName) ? startSceneName : "chooseToolBasedOnCut";
    }

    private string ResolveSceneNameByBuildIndex(int buildIndex)
    {
        string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
        if (!string.IsNullOrEmpty(scenePath))
        {
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (!string.IsNullOrEmpty(sceneName)) return sceneName;
        }
        return !string.IsNullOrEmpty(startSceneName) ? startSceneName : "chooseToolBasedOnCut";
    }

    private void BeginStartGameFlow(string targetSceneName)
    {
        if (isStartingGame) return;
        isStartingGame = true;
        StartCoroutine(StartGameFlowRoutine(targetSceneName));
    }

    private IEnumerator StartGameFlowRoutine(string targetSceneName)
    {
        bool isEnglish = OrientPearlLanguage.Current == 1;
        VideoClip clip = (isEnglish && introVideoEnglish != null)
            ? introVideoEnglish
            : (introVideoChinese != null ? introVideoChinese : introVideoEnglish);
        string blackText = (isEnglish && !string.IsNullOrEmpty(firstDayBlackTextEnglish))
            ? firstDayBlackTextEnglish
            : firstDayBlackText;

        if (playIntroCutscene && clip != null)
        {
            Debug.Log($"🎬 [MainMenu] 开始播放全屏 Intro Cutscene: {clip.name} (禁止点击跳过)");
            yield return StartCoroutine(PlayFullscreenIntroCutsceneRoutine(clip));
        }

        GlobalProgressManager global = GlobalProgressManager.EnsureInstance();
        GlobalProgressManager.CurrentDay = 1;
        GlobalProgressManager.IsLikeU = false;
        GlobalProgressManager.JushiSuccess = false;
        GlobalProgressManager.TalkedCicada = false;
        global.currentOrder = 0;

        SceneBlinkTransition trans = SceneBlinkTransition.EnsureInstance();
        if (blackTextTransitionPrefab != null && trans.blackTextTransitionPrefab == null)
        {
            trans.blackTextTransitionPrefab = blackTextTransitionPrefab;
        }

        Debug.Log($"🚀 [MainMenu] Intro 完成，通过黑屏字幕 '{blackText}' 进入第一关: {targetSceneName}");
        global.TransitionToScene(targetSceneName, blackText, 1);
    }

    private IEnumerator PlayFullscreenIntroCutsceneRoutine(VideoClip clip)
    {
        runtimeIntroCanvasObj = new GameObject("Canvas_IntroCutscene");
        Canvas canvas = runtimeIntroCanvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1500;

        CanvasScaler scaler = runtimeIntroCanvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        runtimeIntroCanvasObj.AddComponent<GraphicRaycaster>();

        // 黑色衬底并拦截所有鼠标点击，确保播片期间无任何交互或误点按钮
        GameObject backdropGo = new GameObject("IntroBackdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        backdropGo.transform.SetParent(runtimeIntroCanvasObj.transform, false);
        RectTransform backdropRt = backdropGo.GetComponent<RectTransform>();
        backdropRt.anchorMin = Vector2.zero;
        backdropRt.anchorMax = Vector2.one;
        backdropRt.offsetMin = Vector2.zero;
        backdropRt.offsetMax = Vector2.zero;
        Image backdropImg = backdropGo.GetComponent<Image>();
        backdropImg.color = Color.black;
        backdropImg.raycastTarget = true;

        GameObject rawGo = new GameObject("IntroVideoRaw", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        rawGo.transform.SetParent(runtimeIntroCanvasObj.transform, false);
        RectTransform rawRt = rawGo.GetComponent<RectTransform>();
        rawRt.anchorMin = Vector2.zero;
        rawRt.anchorMax = Vector2.one;
        rawRt.offsetMin = Vector2.zero;
        rawRt.offsetMax = Vector2.zero;
        RawImage rawImg = rawGo.GetComponent<RawImage>();
        rawImg.color = Color.white;
        rawImg.raycastTarget = false;
        rawImg.enabled = false;

        runtimeIntroRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
        {
            name = "RT_MainMenuIntro",
            hideFlags = HideFlags.DontSave
        };
        runtimeIntroRT.Create();
        rawImg.texture = runtimeIntroRT;

        VideoPlayer vp = runtimeIntroCanvasObj.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.aspectRatio = VideoAspectRatio.FitInside;
        vp.audioOutputMode = VideoAudioOutputMode.Direct;
        vp.targetTexture = runtimeIntroRT;
        vp.clip = clip;

        vp.Prepare();
        float prepTimeout = 3.0f;
        while (!vp.isPrepared && prepTimeout > 0f)
        {
            prepTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        bool finished = false;
        bool gotFirstFrame = false;
        VideoPlayer.EventHandler endHandler = (player) => { finished = true; };
        VideoPlayer.FrameReadyEventHandler frameHandler = (player, frameIdx) => { gotFirstFrame = true; };

        vp.loopPointReached += endHandler;
        vp.sendFrameReadyEvents = true;
        vp.frameReady += frameHandler;

        vp.time = 0;
        vp.Play();

        float firstFrameTimeout = 1.0f;
        while (!gotFirstFrame && firstFrameTimeout > 0f)
        {
            firstFrameTimeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        vp.frameReady -= frameHandler;
        vp.sendFrameReadyEvents = false;
        rawImg.enabled = true;

        float clipLen = vp.length > 0.1 ? (float)vp.length : (float)clip.length;
        float maxWait = clipLen > 0.1f ? clipLen + 2.5f : 12f;
        float elapsed = 0f;
        while (!finished && elapsed < maxWait)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        vp.loopPointReached -= endHandler;
        vp.Pause();
    }

    private void OnDestroy()
    {
        if (runtimeIntroCanvasObj != null)
        {
            Destroy(runtimeIntroCanvasObj);
            runtimeIntroCanvasObj = null;
        }

        if (runtimeIntroRT != null)
        {
            runtimeIntroRT.Release();
            Destroy(runtimeIntroRT);
            runtimeIntroRT = null;
        }
    }

    public void QuitGame()
    {
        Debug.Log("👋 [MainMenu] 退出游戏");
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ==========================================
    // 查找工具辅助方法
    // ==========================================

    private static GameObject FindChildGameObject(Transform root, string targetName)
    {
        if (root == null) return null;
        Transform found = FindDeepChild(root, targetName);
        return found != null ? found.gameObject : null;
    }

    private static T FindChildComponent<T>(Transform root, string targetName) where T : Component
    {
        if (root == null) return null;
        Transform found = FindDeepChild(root, targetName);
        return found != null ? found.GetComponent<T>() : null;
    }

    private static Transform FindDeepChild(Transform parent, string targetName)
    {
        if (parent.name == targetName) return parent;

        foreach (Transform child in parent)
        {
            if (child.name == targetName) return child;
            Transform result = FindDeepChild(child, targetName);
            if (result != null) return result;
        }
        return null;
    }
}
