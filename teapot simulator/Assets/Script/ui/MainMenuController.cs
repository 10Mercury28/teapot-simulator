using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

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
    public string startSceneName = "begin";

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
        if (loadByIndex)
        {
            Debug.Log($"🚀 [MainMenu] 开始游戏，加载 Build Index [{startSceneBuildIndex}] 场景");
            SceneManager.LoadScene(startSceneBuildIndex);
        }
        else
        {
            Debug.Log($"🚀 [MainMenu] 开始游戏，加载场景: {startSceneName}");
            SceneManager.LoadScene(startSceneName);
        }
    }

    public void StartGameByIndex(int sceneIndex)
    {
        Debug.Log($"🚀 [MainMenu] 开始游戏，加载 Build Index [{sceneIndex}] 场景");
        SceneManager.LoadScene(sceneIndex);
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
