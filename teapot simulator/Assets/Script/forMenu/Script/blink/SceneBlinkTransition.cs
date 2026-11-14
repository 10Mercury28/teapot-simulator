using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System;
using System.Collections;
using TMPro;

public class SceneBlinkTransition : MonoBehaviour
{
    public static SceneBlinkTransition Instance;

    public const string DefaultBlackTextPrefabPath = "Assets/prefab/BlackTextTransitionCanvas .prefab";

    [Header("转场设置 (眨眼)")]
    [Tooltip("切换场景前闭眼的持续时间 (秒)")]
    public float closeDuration = 0.5f;

    [Header("黑屏文字转场 Prefab")]
    [Tooltip("黑屏居中文字转场预制体（如 BlackTextTransitionCanvas ）")]
    public GameObject blackTextTransitionPrefab;

    [Tooltip("黑屏淡入时长 (秒)")]
    public float blackFadeInDuration = 0.45f;

    [Tooltip("文字淡入时长 (秒)")]
    public float textFadeInDuration = 0.35f;

    [Tooltip("黑屏文字停留展示时长 (秒)")]
    public float textHoldDuration = 1.2f;

    [Tooltip("转场结束后黑屏淡出时长 (秒)")]
    public float blackFadeOutDuration = 0.55f;

    public bool IsTransitioning { get; private set; } = false;

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
    /// 确保全局转场器存在（即使在编辑器单场景直接点 Play 也可正常工作）
    /// </summary>
    public static SceneBlinkTransition EnsureInstance()
    {
        if (Instance == null)
        {
            Instance = FindObjectOfType<SceneBlinkTransition>();
        }
        if (Instance == null)
        {
            GameObject go = new GameObject("SceneBlinkTransition (Auto)");
            Instance = go.AddComponent<SceneBlinkTransition>();
            DontDestroyOnLoad(go);
        }
        return Instance;
    }

    /// <summary>
    /// 🌑 闭眼后切换场景（保持黑屏）
    /// </summary>
    public void LoadSceneConcealed(string sceneName)
    {
        if (IsTransitioning) return;
        StartCoroutine(LoadSceneConcealedRoutine(sceneName));
    }

    /// <summary>
    /// 🖤 黑屏 + 居中文字过渡切场（例如 "入夜" / "Day 2"）
    /// 会动态 Instantiate BlackTextTransitionCanvas Prefab，跨场景保持覆盖，完成后自动 Destroy。
    /// </summary>
    public void LoadSceneWithBlackText(string sceneName, string displayText, Action onBlackScreenCovered = null)
    {
        if (IsTransitioning)
        {
            Debug.LogWarning($"⚠️ [SceneBlinkTransition] 正在转场中，忽略重复跳转请求：{sceneName}");
            return;
        }
        StartCoroutine(LoadSceneWithBlackTextRoutine(sceneName, displayText, onBlackScreenCovered));
    }

    /// <summary>
    /// 可供协程等待直到黑屏完全盖住屏幕（alpha == 1）
    /// </summary>
    public IEnumerator LoadSceneWithBlackTextWaitForCover(string sceneName, string displayText, Action onBlackScreenCovered = null)
    {
        if (IsTransitioning) yield break;

        bool covered = false;
        StartCoroutine(LoadSceneWithBlackTextRoutine(sceneName, displayText, () =>
        {
            covered = true;
            onBlackScreenCovered?.Invoke();
        }));

        while (!covered && IsTransitioning)
        {
            yield return null;
        }
    }

    private IEnumerator LoadSceneConcealedRoutine(string sceneName)
    {
        IsTransitioning = true;
        try
        {
            BlinkMaskController blink = FindObjectOfType<BlinkMaskController>();

            if (blink != null)
            {
                Debug.Log("🎬 执行闭眼并保持黑屏...");
                yield return StartCoroutine(blink.ForceCloseEyesOnly(closeDuration));
            }

            AsyncOperation asyncLoad = TryLoadSceneAsync(sceneName);
            if (asyncLoad == null)
            {
                if (blink != null) blink.ForceOpenEyes(0.1f);
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
        finally
        {
            IsTransitioning = false;
        }
    }

    private IEnumerator LoadSceneWithBlackTextRoutine(string sceneName, string displayText, Action onBlackScreenCovered)
    {
        IsTransitioning = true;
        GameObject overlay = null;

        try
        {
            GameObject prefab = ResolveBlackTextPrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"⚠️ [SceneBlinkTransition] 未找到 BlackTextTransitionCanvas Prefab，回退为闭眼切场 → {sceneName}");
                onBlackScreenCovered?.Invoke();
                IsTransitioning = false;
                LoadSceneConcealed(sceneName);
                yield break;
            }

            // 1. 生成并挂跨场景保留，首帧立即将整张黑屏与文字透明度清零防闪烁
            overlay = Instantiate(prefab);
            overlay.name = "BlackTextTransitionCanvas (Active)";
            DontDestroyOnLoad(overlay);
            overlay.SetActive(true);

            CanvasGroup cg = overlay.GetComponent<CanvasGroup>();
            if (cg == null) cg = overlay.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = true;
            cg.interactable = true;

            Canvas canvas = overlay.GetComponent<Canvas>();
            if (canvas != null && canvas.sortingOrder < 2000)
            {
                canvas.sortingOrder = 2000;
            }

            TMP_Text tmpText = overlay.GetComponentInChildren<TMP_Text>(true);
            Text uiText = overlay.GetComponentInChildren<Text>(true);
            bool hasText = !string.IsNullOrEmpty(displayText);

            if (tmpText != null)
            {
                tmpText.text = hasText ? displayText : "";
                tmpText.gameObject.SetActive(hasText);
                SetTextAlpha(tmpText, 0f);
            }
            if (uiText != null)
            {
                uiText.text = hasText ? displayText : "";
                uiText.gameObject.SetActive(hasText);
                SetTextAlpha(uiText, 0f);
            }

            // 2. 黑屏淡入完全盖住当前场景
            yield return StartCoroutine(FadeCanvasGroup(cg, 0f, 1f, blackFadeInDuration));
            onBlackScreenCovered?.Invoke();

            // 3. 启动异步切场景，同时文字淡入
            AsyncOperation asyncLoad = TryLoadSceneAsync(sceneName);
            if (asyncLoad == null)
            {
                yield return StartCoroutine(FadeCanvasGroup(cg, 1f, 0f, 0.25f));
                yield break;
            }
            asyncLoad.allowSceneActivation = false;

            if (hasText)
            {
                yield return StartCoroutine(FadeTexts(tmpText, uiText, 0f, 1f, textFadeInDuration));
            }

            // 4. 等待最短可读停留时间与异步加载就绪
            float holdTimer = hasText ? textHoldDuration : 0.25f;
            float elapsed = 0f;
            while (elapsed < holdTimer || asyncLoad.progress < 0.9f)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            // 5. 激活新场景并等待其初始化完成
            asyncLoad.allowSceneActivation = true;
            while (!asyncLoad.isDone)
            {
                yield return null;
            }

            // 多等 2 帧确保新场景 Awakes/Starts 渲染就位
            yield return null;
            yield return null;

            Debug.Log($"🖤 [SceneBlinkTransition] 已完成黑屏字卡过渡 '{displayText}' -> 场景 '{sceneName}'，开始淡出揭幕。");

            // 6. 文字与黑屏平滑淡出，结束后销毁预制体实例
            if (hasText)
            {
                yield return StartCoroutine(FadeTexts(tmpText, uiText, 1f, 0f, Mathf.Min(0.25f, blackFadeOutDuration * 0.5f)));
            }
            yield return StartCoroutine(FadeCanvasGroup(cg, 1f, 0f, blackFadeOutDuration));
        }
        finally
        {
            if (overlay != null)
            {
                Destroy(overlay);
            }
            IsTransitioning = false;
        }
    }

    public bool HasBlackTextPrefab() => ResolveBlackTextPrefab() != null;

    private GameObject ResolveBlackTextPrefab()
    {
        if (blackTextTransitionPrefab != null) return blackTextTransitionPrefab;

        GameObject loaded = Resources.Load<GameObject>("BlackTextTransitionCanvas ") ??
                            Resources.Load<GameObject>("BlackTextTransitionCanvas");
        if (loaded != null)
        {
            blackTextTransitionPrefab = loaded;
            return loaded;
        }

#if UNITY_EDITOR
        GameObject editorPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DefaultBlackTextPrefabPath);
        if (editorPrefab != null)
        {
            blackTextTransitionPrefab = editorPrefab;
            return editorPrefab;
        }
#endif
        return null;
    }

    private AsyncOperation TryLoadSceneAsync(string sceneName)
    {
        try
        {
            return SceneManager.LoadSceneAsync(sceneName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [SceneBlinkTransition] 加载场景 '{sceneName}' 异常 (请检查 Build Settings 是否已添加该场景): {ex.Message}");
            return null;
        }
    }

    private static IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        if (duration <= 0f)
        {
            cg.alpha = to;
            yield break;
        }

        float t = 0f;
        cg.alpha = from;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            yield return null;
        }
        cg.alpha = to;
    }

    private static IEnumerator FadeTexts(TMP_Text tmp, Text uiText, float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetTextAlpha(tmp, to);
            SetTextAlpha(uiText, to);
            yield break;
        }

        float t = 0f;
        SetTextAlpha(tmp, from);
        SetTextAlpha(uiText, from);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, to, Mathf.Clamp01(t / duration));
            SetTextAlpha(tmp, a);
            SetTextAlpha(uiText, a);
            yield return null;
        }
        SetTextAlpha(tmp, to);
        SetTextAlpha(uiText, to);
    }

    private static void SetTextAlpha(Graphic textGraphic, float alpha)
    {
        if (textGraphic == null) return;
        Color c = textGraphic.color;
        c.a = alpha;
        textGraphic.color = c;
    }
}
