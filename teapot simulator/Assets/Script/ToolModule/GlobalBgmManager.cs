using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局背景音乐管理器（跨场景 DontDestroyOnLoad）：
/// 1. 在所有非排除场景间无缝持续播放统一 BGM（不打断、不重头播放）。
/// 2. 在切入排除场景（默认 Mindmaze）前平滑渐弱并停止，再由转场器切场景。
/// 3. 从排除场景离开并进入普通场景时，不恢复旧进度，直接从头（00:00）重新播放。
/// </summary>
public class GlobalBgmManager : MonoBehaviour
{
    public static GlobalBgmManager Instance { get; private set; }

    public const string ResourcePrefabName = "GlobalBgmManager";
    public const string DefaultBgmAssetPath = "Assets/Audio/Background_Music.mp3";

    [Header("全局背景音乐配置")]
    [Tooltip("统一跨场景背景音乐（如 Assets/Audio/Background_Music.mp3）")]
    public AudioClip bgmClip;

    [Tooltip("背景音乐目标音量")]
    [Range(0f, 1f)]
    public float bgmVolume = 0.6f;

    [Header("排除场景与渐入渐出")]
    [Tooltip("不播放统一背景音乐的场景名列表（区分/不区分大小写均可，默认包含 Mindmaze）")]
    public List<string> excludedScenes = new List<string> { "Mindmaze" };

    [Tooltip("进入排除场景（如 Mindmaze）前，背景音乐渐弱到静音的时长（秒）")]
    [Min(0f)]
    public float fadeOutBeforeExcludedScene = 1.5f;

    [Tooltip("勾选：先等 BGM 完全渐弱结束并停止，再执行黑屏/切场转场；不勾选：渐弱与转场同步进行")]
    public bool waitForFadeOutBeforeTransition = true;

    [Tooltip("初次启动或从 Mindmaze 回到普通场景从头播放时的淡入时长（秒，0 为立即满音量）")]
    [Min(0f)]
    public float fadeInDuration = 0.8f;

    [Header("组件引用与调试")]
    [Tooltip("用于播放全局 BGM 的 AudioSource（留空将在 Awake 自动获取或添加）")]
    public AudioSource audioSource;

    public bool debugLog = true;

    private Coroutine activeFadeCoroutine;
    private bool isFading = false;
    private bool isFadingOut = false;
    private bool hasHandledInitialScene = false;
    private int fadeVersion = 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoInitializeAfterSceneLoad()
    {
        EnsureInstance();
    }

    /// <summary>
    /// 确保全局 BGM 管理器存在（优先复用场景中已有实例，否则从 Resources 加载预制体，再否则自动创建）。
    /// </summary>
    public static GlobalBgmManager EnsureInstance()
    {
        if (Instance != null) return Instance;

        Instance = FindObjectOfType<GlobalBgmManager>();
        if (Instance != null) return Instance;

        GameObject prefab = Resources.Load<GameObject>(ResourcePrefabName);
        if (prefab != null)
        {
            GameObject instanceObj = Instantiate(prefab);
            instanceObj.name = ResourcePrefabName;
            Instance = instanceObj.GetComponent<GlobalBgmManager>();
            if (Instance == null)
            {
                Instance = instanceObj.AddComponent<GlobalBgmManager>();
            }
            return Instance;
        }

        GameObject go = new GameObject("GlobalBgmManager (Auto)");
        Instance = go.AddComponent<GlobalBgmManager>();
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

        EnsureAudioSource();
        ResolveDefaultClipIfMissing();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (!hasHandledInitialScene)
        {
            hasHandledInitialScene = true;
            HandleSceneEntered(SceneManager.GetActiveScene().name);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying && !isFading && audioSource != null && audioSource.isPlaying)
        {
            audioSource.volume = Mathf.Clamp01(bgmVolume);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        hasHandledInitialScene = true;
        HandleSceneEntered(scene.name);
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
    }

    private void ResolveDefaultClipIfMissing()
    {
        if (bgmClip != null) return;

#if UNITY_EDITOR
        bgmClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultBgmAssetPath);
#endif
    }

    public bool IsExcludedScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || excludedScenes == null) return false;
        string normalized = sceneName.Trim();

        for (int i = 0; i < excludedScenes.Count; i++)
        {
            string entry = excludedScenes[i];
            if (!string.IsNullOrWhiteSpace(entry) &&
                string.Equals(entry.Trim(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool ShouldFadeOutBeforeLoading(string targetSceneName)
    {
        return IsExcludedScene(targetSceneName) &&
               audioSource != null &&
               (audioSource.isPlaying || isFadingOut);
    }

    private void HandleSceneEntered(string sceneName)
    {
        EnsureAudioSource();
        ResolveDefaultClipIfMissing();

        if (IsExcludedScene(sceneName))
        {
            CancelActiveFade();
            isFading = false;
            isFadingOut = false;

            if (audioSource != null)
            {
                if (audioSource.isPlaying)
                {
                    audioSource.Stop();
                }
                audioSource.time = 0f;
                audioSource.volume = 0f;
            }

            if (debugLog)
            {
                Debug.Log($"🔇 [GlobalBgmManager] 已进入排除场景 '{sceneName}'，统一 BGM 保持停止。");
            }
            return;
        }

        if (bgmClip == null)
        {
            if (debugLog) Debug.LogWarning("⚠️ [GlobalBgmManager] 未配置 bgmClip，无法播放全局背景音乐。");
            return;
        }

        if (audioSource.isPlaying && audioSource.clip == bgmClip && !isFadingOut)
        {
            if (debugLog)
            {
                Debug.Log($"🎵 [GlobalBgmManager] 进入场景 '{sceneName}'，跨场景无缝继承继续播放 '{bgmClip.name}'。");
            }
            return;
        }

        PlayFromBeginningWithFadeIn(sceneName);
    }

    private void PlayFromBeginningWithFadeIn(string sceneName)
    {
        CancelActiveFade();

        audioSource.clip = bgmClip;
        audioSource.loop = true;
        audioSource.time = 0f;

        float targetVol = Mathf.Clamp01(bgmVolume);
        if (fadeInDuration > 0f)
        {
            audioSource.volume = 0f;
            audioSource.Play();
            activeFadeCoroutine = StartCoroutine(FadeInRoutine(targetVol, fadeInDuration));
        }
        else
        {
            audioSource.volume = targetVol;
            audioSource.Play();
        }

        if (debugLog)
        {
            Debug.Log($"🎵 [GlobalBgmManager] 在场景 '{sceneName}' 从头开始播放 '{bgmClip.name}' (fadeIn={fadeInDuration:F2}s)。");
        }
    }

    /// <summary>
    /// 供场景切换前等待：将当前全局 BGM 在 durationOverride（默认 fadeOutBeforeExcludedScene）秒内平滑渐弱至停止并归零进度。
    /// </summary>
    public IEnumerator FadeOutAndStopRoutine(float durationOverride = -1f)
    {
        CancelActiveFade();
        int myVersion = ++fadeVersion;

        EnsureAudioSource();
        isFading = true;
        isFadingOut = true;

        float duration = durationOverride >= 0f ? durationOverride : fadeOutBeforeExcludedScene;

        try
        {
            if (audioSource != null && audioSource.isPlaying)
            {
                float startVol = audioSource.volume;
                if (duration > 0f && startVol > 0.0001f)
                {
                    if (debugLog)
                    {
                        Debug.Log($"🔉 [GlobalBgmManager] 转场前开始渐弱停止全局 BGM ({duration:F2}s)...");
                    }

                    float elapsed = 0f;
                    while (elapsed < duration)
                    {
                        if (fadeVersion != myVersion || audioSource == null || !audioSource.isPlaying)
                        {
                            yield break;
                        }

                        elapsed += Time.unscaledDeltaTime;
                        audioSource.volume = Mathf.Lerp(startVol, 0f, Mathf.Clamp01(elapsed / duration));
                        yield return null;
                    }
                }

                if (fadeVersion == myVersion && audioSource != null)
                {
                    audioSource.volume = 0f;
                    if (audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                    audioSource.time = 0f;
                }
            }
            else if (audioSource != null && fadeVersion == myVersion)
            {
                audioSource.volume = 0f;
                audioSource.time = 0f;
            }

            if (fadeVersion == myVersion && debugLog)
            {
                Debug.Log("🔇 [GlobalBgmManager] 全局 BGM 已渐弱结束并停止。");
            }
        }
        finally
        {
            if (fadeVersion == myVersion)
            {
                isFading = false;
                isFadingOut = false;
            }
        }
    }

    /// <summary>
    /// 非阻塞触发渐弱停止（在管理器自身上启动协程）。
    /// </summary>
    public void FadeOutAndStop(float durationOverride = -1f)
    {
        CancelActiveFade();
        activeFadeCoroutine = StartCoroutine(FadeOutAndStopTrackedRoutine(durationOverride));
    }

    private IEnumerator FadeOutAndStopTrackedRoutine(float durationOverride)
    {
        yield return FadeOutAndStopRoutine(durationOverride);
        activeFadeCoroutine = null;
    }

    private IEnumerator FadeInRoutine(float targetVolume, float duration)
    {
        int myVersion = ++fadeVersion;
        isFading = true;
        isFadingOut = false;

        try
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (fadeVersion != myVersion || audioSource == null || !audioSource.isPlaying)
                {
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                audioSource.volume = Mathf.Lerp(0f, Mathf.Clamp01(bgmVolume), Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (fadeVersion == myVersion && audioSource != null)
            {
                audioSource.volume = Mathf.Clamp01(bgmVolume);
            }
        }
        finally
        {
            if (fadeVersion == myVersion)
            {
                isFading = false;
                activeFadeCoroutine = null;
            }
        }
    }

    private void CancelActiveFade()
    {
        fadeVersion++;
        if (activeFadeCoroutine != null)
        {
            StopCoroutine(activeFadeCoroutine);
            activeFadeCoroutine = null;
        }
    }
}
