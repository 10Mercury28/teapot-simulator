using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yarn.Unity;

/// <summary>
/// 方案 B 双 YarnProject 语言桥接器（DontDestroyOnLoad）：
/// 根据 OrientPearlLanguage.IsEnglish 自动为场景中的 DialogueRunner 切换中文/英文 YarnProject，
/// 并同步 LineProvider 的 LocaleCode 与 VariableStorage 的 Program。
/// 绑定方式：打开 Assets/Resources/YarnLanguageConfig.prefab，把新建好的 MyProject_EN 拖进 englishYarnProject 槽即可。
/// </summary>
[DefaultExecutionOrder(-200)]
public class YarnLanguageBridge : MonoBehaviour
{
    public static YarnLanguageBridge Instance { get; private set; }

    public const string ResourceConfigName = "YarnLanguageConfig";

    [Header("=== 中/英文 YarnProject 配置 ===")]
    [Tooltip("默认中文 YarnProject（如 Assets/YarnScript/MyProject.yarnproject）")]
    public YarnProject chineseYarnProject;

    [Tooltip("英文版 YarnProject（拿到全部英文稿并新建 MyProject_EN.yarnproject 后拖入此处）")]
    public YarnProject englishYarnProject;

    [Header("=== 调试 ===")]
    public bool debugLog = true;

    private static bool loggedMissingEnglishWarning = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitializeAfterSceneLoad()
    {
        EnsureInstance();
        ApplyToAllRunnersInScene();
    }

    public static YarnLanguageBridge EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindObjectOfType<YarnLanguageBridge>();
        if (Instance != null) return Instance;

        GameObject prefab = Resources.Load<GameObject>(ResourceConfigName);
        if (prefab != null)
        {
            GameObject go = Instantiate(prefab);
            go.name = ResourceConfigName;
            Instance = go.GetComponent<YarnLanguageBridge>() ?? go.AddComponent<YarnLanguageBridge>();
            return Instance;
        }

        GameObject autoGo = new GameObject("YarnLanguageBridge (Auto)");
        Instance = autoGo.AddComponent<YarnLanguageBridge>();
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        OrientPearlLanguage.LanguageChanged += OnLanguageChanged;
    }

    private void Start()
    {
        ApplyToAllRunnersInScene();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        OrientPearlLanguage.LanguageChanged -= OnLanguageChanged;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyToAllRunnersInScene();
        StartCoroutine(ApplyNextFrameRoutine());
    }

    private IEnumerator ApplyNextFrameRoutine()
    {
        yield return null;
        ApplyToAllRunnersInScene();
    }

    private void OnLanguageChanged()
    {
        loggedMissingEnglishWarning = false;
        ApplyToAllRunnersInScene();
    }

    public static void ApplyToAllRunnersInScene()
    {
        DialogueRunner[] runners = FindObjectsOfType<DialogueRunner>(true);
        if (runners == null || runners.Length == 0) return;

        for (int i = 0; i < runners.Length; i++)
        {
            ApplyToRunner(runners[i]);
        }
    }

    public static void ApplyToRunner(DialogueRunner runner)
    {
        if (runner == null) return;
        if (runner.IsDialogueRunning) return;

        YarnLanguageBridge bridge = EnsureInstance();
        if (bridge == null) return;

        if (bridge.chineseYarnProject == null && !OrientPearlLanguage.IsEnglish)
        {
            bridge.chineseYarnProject = runner.YarnProject;
        }

        YarnProject targetProject = OrientPearlLanguage.IsEnglish
            ? bridge.ResolveEnglishProject()
            : bridge.ResolveChineseProject(runner.YarnProject);

        if (OrientPearlLanguage.IsEnglish && targetProject != null &&
            !string.IsNullOrEmpty(runner.startNode) &&
            !ProjectContainsNode(targetProject, runner.startNode))
        {
            if (bridge.debugLog)
            {
                Debug.Log($"ℹ️ [YarnLanguageBridge] 英文剧本 '{targetProject.name}' 暂未包含节点 '{runner.startNode}'，本场景先回退中文剧本。");
            }
            targetProject = bridge.ResolveChineseProject(runner.YarnProject);
        }

        if (targetProject == null)
        {
            if (OrientPearlLanguage.IsEnglish && !loggedMissingEnglishWarning)
            {
                loggedMissingEnglishWarning = true;
                Debug.LogWarning("⚠️ [YarnLanguageBridge] 当前为英文模式，但 englishYarnProject 尚未绑定，暂继续使用默认中文剧本。请在 Assets/Resources/YarnLanguageConfig.prefab 拖入 MyProject_Eng。");
            }
            return;
        }

        if (runner.YarnProject != targetProject)
        {
            runner.SetProject(targetProject);
            if (bridge.debugLog)
            {
                Debug.Log($"🌐 [YarnLanguageBridge] 已将 DialogueRunner ({runner.name}) 切换为 {(OrientPearlLanguage.IsEnglish ? "英文" : "中文")} YarnProject: {targetProject.name}");
            }
        }

        if (runner.VariableStorage != null)
        {
            runner.VariableStorage.Program = targetProject.Program;
        }

        SyncLineProvider(runner.LineProvider, targetProject);
    }

    private static bool ProjectContainsNode(YarnProject project, string nodeName)
    {
        return project != null &&
               project.Program != null &&
               project.Program.Nodes != null &&
               project.Program.Nodes.ContainsKey(nodeName);
    }

    private YarnProject ResolveChineseProject(YarnProject fallback)
    {
        if (chineseYarnProject != null) return chineseYarnProject;

        YarnProject res = Resources.Load<YarnProject>("MyProject");
        if (res != null)
        {
            chineseYarnProject = res;
            return res;
        }

#if UNITY_EDITOR
        YarnProject ed = UnityEditor.AssetDatabase.LoadAssetAtPath<YarnProject>("Assets/YarnScript/MyProject.yarnproject");
        if (ed != null)
        {
            chineseYarnProject = ed;
            return ed;
        }
#endif
        return fallback;
    }

    private YarnProject ResolveEnglishProject()
    {
        if (englishYarnProject != null) return englishYarnProject;

        YarnProject res = Resources.Load<YarnProject>("MyProject_Eng") ?? Resources.Load<YarnProject>("MyProject_EN");
        if (res != null)
        {
            englishYarnProject = res;
            return res;
        }

#if UNITY_EDITOR
        string[] candidatePaths =
        {
            "Assets/YarnScript/MyProject_Eng.yarnproject",
            "Assets/YarnScript/eng/MyProject_Eng.yarnproject",
            "Assets/YarnScript/eng/MyProject_EN.yarnproject",
            "Assets/YarnScript/MyProject_EN.yarnproject"
        };
        for (int i = 0; i < candidatePaths.Length; i++)
        {
            YarnProject ed = UnityEditor.AssetDatabase.LoadAssetAtPath<YarnProject>(candidatePaths[i]);
            if (ed != null)
            {
                englishYarnProject = ed;
                return ed;
            }
        }
#endif
        return null;
    }

    private static void SyncLineProvider(ILineProvider lineProvider, YarnProject project)
    {
        if (lineProvider == null || project == null) return;

        lineProvider.YarnProject = project;

        if (lineProvider is BuiltinLocalisedLineProvider builtin)
        {
            string targetLocale = ResolveBaseLocaleKey(project);
            if (!string.IsNullOrEmpty(targetLocale))
            {
                builtin.LocaleCode = targetLocale;
                builtin.AssetLocaleCode = targetLocale;
            }
        }
    }

    private static string ResolveBaseLocaleKey(YarnProject project)
    {
        if (project == null || project.localizations == null) return null;

        if (project.baseLocalization != null)
        {
            foreach (var kv in project.localizations)
            {
                if (kv.Value == project.baseLocalization)
                {
                    return kv.Key;
                }
            }
        }

        foreach (var kv in project.localizations)
        {
            if (!string.IsNullOrEmpty(kv.Key))
            {
                return kv.Key;
            }
        }

        return null;
    }
}
