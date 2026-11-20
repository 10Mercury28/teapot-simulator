using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

public class BeforeStartController : MonoBehaviour
{
    [Header("开始按钮开关")]
    [Tooltip("是否需要点击「开始」按钮才进入游玩；取消勾选后不显示开始按钮，播完开场动画后直接进入可游玩状态")]
    public bool requireStartButton = true;

    [Tooltip("仅在取消勾选 Require Start Button 时生效：0 = 播完整个开场视频后自动进入；> 0 = 播放指定秒数后自动淡出进入")]
    [Min(0f)]
    public float autoStartMaxWaitSeconds = 0f;

    [Header("UI 控制")]
    public CanvasGroup beforeStartCanvas;   // 整个 BeforeStart Canvas
    public Button triggerButton;            // 开始按钮

    [Header("视频组件 (可选)")]
    public VideoPlayer videoPlayer;
    public RawImage videoRaw;

    [Header("计时与淡入")]
    public float lockDuration = 10f;        // 前10秒锁交互
    public float fadeDuration = 0.5f;       // 正常按钮淡入时间
    [Range(0f, 1f)] public float finalButtonAlpha = 0.7f;

    [Header("需要在开始时禁用的对象")]
    public List<GameObject> disableObjects;

    [Header("流程协同")]
    [Tooltip("若为 true，点击开始按钮后仅隐藏按钮并保持视频播放，实际淡出和解锁由 CutSequenceController 接管")]
    public bool deferUnlockToSequenceController = false;

    private static BeforeStartController _activeInstance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _activeInstance = null;
    }

    public static bool IsAnyBeforeStartBlocking => _activeInstance != null && _activeInstance.IsBeforeStartActive();

    private bool sceneUnlocked = false;
    private bool userClickedStart = false;
    private bool videoFinished = false;

    public bool HasUserClickedStart() => userClickedStart;

    void Awake()
    {
        _activeInstance = this;

        // ========= 自动绑定组件（在首帧渲染前即藏好开始按钮，防闪一帧） =========
        if (beforeStartCanvas == null)
            beforeStartCanvas = GetComponent<CanvasGroup>();

        if (videoPlayer == null)
            videoPlayer = GetComponentInChildren<VideoPlayer>(true);

        if (videoRaw == null)
            videoRaw = GetComponentInChildren<RawImage>(true);

        if (triggerButton == null)
            triggerButton = GetComponentInChildren<Button>(true);

        if (videoPlayer != null)
            videoPlayer.isLooping = requireStartButton;

        if (disableObjects != null)
        {
            foreach (var obj in disableObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }

        if (beforeStartCanvas != null)
        {
            beforeStartCanvas.alpha = 1f;
            beforeStartCanvas.interactable = true;
            beforeStartCanvas.blocksRaycasts = true;
        }

        if (triggerButton != null)
        {
            if (!requireStartButton)
            {
                triggerButton.interactable = false;
                CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
                if (btnCanvas != null)
                    btnCanvas.alpha = 0f;
                triggerButton.gameObject.SetActive(false);
            }
            else
            {
                triggerButton.gameObject.SetActive(true);

                var btnTmp = triggerButton.GetComponentInChildren<TMPro.TMP_Text>();
                if (btnTmp != null && (btnTmp.text == "Button" || string.IsNullOrWhiteSpace(btnTmp.text)))
                {
                    btnTmp.text = "开始";
                }
                var btnText = triggerButton.GetComponent<Text>();
                if (btnText != null && (btnText.text == "Button" || string.IsNullOrWhiteSpace(btnText.text)))
                {
                    btnText.text = "开始";
                }

                // ⭐ 行为只注册一次
                triggerButton.onClick.RemoveAllListeners();
                triggerButton.onClick.AddListener(OnButtonClicked);

                CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
                if (btnCanvas != null)
                    btnCanvas.alpha = 0f;

                triggerButton.interactable = false;
            }
        }
    }

    void OnEnable()
    {
        if (!sceneUnlocked)
            _activeInstance = this;
    }

    void OnDisable()
    {
        if (_activeInstance == this)
            _activeInstance = null;
    }

    void OnDestroy()
    {
        if (videoPlayer != null)
            videoPlayer.loopPointReached -= HandleVideoLoopPointReached;

        if (_activeInstance == this)
            _activeInstance = null;
    }

    private void HandleVideoLoopPointReached(VideoPlayer vp)
    {
        videoFinished = true;
    }

    IEnumerator Start()
    {
        videoFinished = false;

        // ========= 播放引导视频 =========
        if (videoPlayer != null)
        {
            if (videoRaw != null && videoPlayer.targetTexture != null)
            {
                videoRaw.texture = videoPlayer.targetTexture;
                videoRaw.enabled = true;
            }

            videoPlayer.isLooping = requireStartButton;
            videoPlayer.loopPointReached -= HandleVideoLoopPointReached;
            videoPlayer.loopPointReached += HandleVideoLoopPointReached;

            if (!videoPlayer.isPrepared)
            {
                videoPlayer.Prepare();
                float prepTimeout = 2.5f;
                while (!videoPlayer.isPrepared && prepTimeout > 0f)
                {
                    prepTimeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            videoPlayer.Play();
        }

        if (requireStartButton)
        {
            // ========= 正常等待流程 =========
            float waitTime = Mathf.Min(lockDuration, 3f);
            if (waitTime > 0f)
            {
                yield return new WaitForSeconds(waitTime);
            }
            yield return StartCoroutine(FadeInButton(fadeDuration));
        }
        else
        {
            // ========= 自动开局流程：等待开场动画/时间结束后直接进入可游玩状态 =========
            if (videoPlayer != null && videoPlayer.clip != null && videoPlayer.isPrepared)
            {
                float clipLen = videoPlayer.length > 0.05 ? (float)videoPlayer.length : (float)videoPlayer.clip.length;
                float maxWait = autoStartMaxWaitSeconds > 0f
                    ? autoStartMaxWaitSeconds
                    : (clipLen > 0f ? clipLen + 2f : Mathf.Max(lockDuration, 3f));

                float elapsed = 0f;
                while (!videoFinished && elapsed < maxWait)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            else if (autoStartMaxWaitSeconds > 0f)
            {
                yield return new WaitForSeconds(autoStartMaxWaitSeconds);
            }

            if (videoPlayer != null)
            {
                videoPlayer.loopPointReached -= HandleVideoLoopPointReached;
            }

            BeginUnlockFlow();
        }
    }

    void Update()
    {
        if (!requireStartButton) return;

        // 如果在演示期间玩家点击了鼠标，且已超过 1.5 秒且未点击开始，可提前唤出开始按钮
        if (!sceneUnlocked && !userClickedStart && Time.timeSinceLevelLoad > 1.5f && Input.GetMouseButtonDown(0))
        {
            if (triggerButton != null && !triggerButton.interactable)
            {
                StopAllCoroutines();
                StartCoroutine(FadeInButton(0.2f));
            }
        }
    }

    // ================== 正常淡入 ==================

    private IEnumerator FadeInButton(float fadeTime)
    {
        if (triggerButton == null) yield break;

        CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
        if (btnCanvas == null) yield break;

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            btnCanvas.alpha = Mathf.Lerp(0f, finalButtonAlpha, t / fadeTime);
            yield return null;
        }

        btnCanvas.alpha = finalButtonAlpha;
        triggerButton.interactable = true;
    }

    // ================== 点击开始 ==================

    private void OnButtonClicked()
    {
        BeginUnlockFlow();
    }

    private void BeginUnlockFlow()
    {
        if (userClickedStart) return;

        userClickedStart = true;

        // 立即隐藏并禁用开始按钮，但可保持引导/待机视频继续播放
        if (triggerButton != null)
        {
            triggerButton.interactable = false;
            CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
            if (btnCanvas != null)
                btnCanvas.alpha = 0f;
            else
                triggerButton.gameObject.SetActive(false);
        }

        // 若未启用外部协同控制，按原逻辑直接解锁
        if (!deferUnlockToSequenceController)
        {
            sceneUnlocked = true;
            StartCoroutine(UnlockSequence(fadeDuration));
        }
    }

    /// <summary>
    /// 由外部控制器（如 CutSequenceController）在打断弹窗关闭后调用，平滑淡出并结束引导视频
    /// </summary>
    public void FadeOutAndUnlock(float fadeTime = 0.5f)
    {
        if (sceneUnlocked) return;
        sceneUnlocked = true;
        StartCoroutine(UnlockSequence(fadeTime));
    }

    private IEnumerator UnlockSequence(float fadeTime = 0.5f)
    {
        // Canvas 淡出
        float t = 0f;

        while (t < fadeTime)
        {
            t += Time.deltaTime;
            if (beforeStartCanvas != null)
                beforeStartCanvas.alpha = Mathf.Lerp(1f, 0f, t / fadeTime);
            yield return null;
        }

        if (beforeStartCanvas != null)
        {
            beforeStartCanvas.alpha = 0f;
            beforeStartCanvas.interactable = false;
            beforeStartCanvas.blocksRaycasts = false;
        }

        // 停止引导视频，释放解码资源
        if (videoPlayer != null)
        {
            videoPlayer.Stop();
        }

        // 启用被禁用对象
        if (disableObjects != null)
        {
            foreach (var obj in disableObjects)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }
    }

    // ================== 给 Menu 用的接口 ==================

    public bool IsBeforeStartActive()
    {
        return !sceneUnlocked;
    }

    public void ForceShowButtonWithFade(float fadeTime)
    {
        if (sceneUnlocked || !requireStartButton) return;

        StopAllCoroutines();
        StartCoroutine(FadeInButtonUnscaled(fadeTime));
    }

    private IEnumerator FadeInButtonUnscaled(float fadeTime)
    {
        if (triggerButton == null) yield break;

        CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
        if (btnCanvas == null) yield break;

        btnCanvas.alpha = 0f;
        triggerButton.interactable = false;

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;   // ⭐ Menu 打开时也能跑
            btnCanvas.alpha = Mathf.Lerp(0f, finalButtonAlpha, t / fadeTime);
            yield return null;
        }

        btnCanvas.alpha = finalButtonAlpha;
        triggerButton.interactable = true;
    }
}
