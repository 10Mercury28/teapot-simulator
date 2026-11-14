using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class PatModule : MonoBehaviour
{
    public enum PatState { Inactive, TransitionLoop, SwitchingToMain, MainActive, FailSequence, Done }

    [Header("Module State")]
    public PatState currentState = PatState.Inactive;

    public bool active => currentState != PatState.Inactive && currentState != PatState.Done;
    public bool complete => currentState == PatState.Done;
    public bool inTransition => currentState == PatState.TransitionLoop || currentState == PatState.SwitchingToMain;
    public int CurrentHits => currentHits;

    [Header("Gameplay")]
    public int requiredHits = 20;
    public float timeLimit = 3f;

    [Header("🔔 弹窗打断配置 (Popup Interruption)")]
    [Tooltip("是否在此模块启用拍泥打断弹窗？(例如 Module 2 和 Module 4 勾选启用)")]
    public bool enableInterruptPopup = false;

    [Tooltip("拍到第几下时触发打断 (流程图中的 x 或 y 暴露参数)")]
    public int interruptHitCount = 3;

    [Tooltip("触发的弹窗数据 SO (如大众点评或老师通知)")]
    public PopupWindowDataSO interruptPopupSO;

    [Tooltip("或者通过 popupId 唤出 (与 interruptPopupSO 二选一，若 SO 为空则用 ID)")]
    public string interruptPopupId;

    [Tooltip("处理完弹窗后是否直接视作本模块完成并加载下一项？\n- False (推荐): 关闭弹窗后返回拍泥，拍完剩余次数才完成\n- True: 弹窗关闭后直接跳过本模块")]
    public bool completeModuleOnPopupDismiss = false;

    [Tooltip("被打断后重新拍泥时，是否保留已拍击的进度？\n- True (推荐): 保留打断前的次数，接着拍剩下的次数\n- False: 从第 1 下重新拍满 requiredHits")]
    public bool retainHitsOnInterrupt = true;

    [Tooltip("弹窗关闭后是否自动切回拍泥？\n- False: 保持在 Idle 待机旋转，玩家点击泥巴切回拍泥 (原生丝滑手感)\n- True: 弹窗关闭瞬间自动切回拍泥")]
    public bool autoResumePatOnPopupDismiss = false;

    private bool isInterrupted = false;
    private bool hasTriggeredInterrupt = false;
    private Coroutine interruptIdleRoutine;

    [Header("Transition Settings")]
    [Range(0.2f, 2f)]
    public float transitionSpeed = 1f;

    [Header("Video Players")]
    public VideoPlayer mainVideo;
    public VideoPlayer failVideo;
    public VideoPlayer transitionVideo;

    private VideoController mainCtrl;
    private VideoController failCtrl;
    private VideoController transCtrl;

    [Header("Raw Images (仅用于视频显示)")]
    public RawImage mainRaw;
    public RawImage failRaw;
    public RawImage transitionRaw;

    [Header("防穿帮垫底背景")]
    public RawImage fallbackBackground;

    [Header("透明度控制（仅作用于模块本体方块）")]
    [Range(0f, 1f)] public float alphaInactive = 0.1f;
    [Range(0f, 1f)] public float alphaActive = 0.4f;
    [Range(0f, 1f)] public float alphaClick = 0.2f;
    [Range(0f, 1f)] public float alphaComplete = 0.0f;

    public float alphaLerpSpeed = 8f;

    private int currentHits;
    private float timer;

    private PatSequenceController controller;
    private float targetAlpha;

    private List<SpriteRenderer> sprites = new List<SpriteRenderer>();

    // Snapshot variables (GPU-only RenderTexture avoids ReadPixels stall & format mismatch)
    private static RenderTexture sharedFallbackRT;
    public bool HasFirstFrameReady { get; private set; }

    void Start()
    {
        controller = GetComponentInParent<PatSequenceController>();

        HideAll();
        EnsureFallbackInitialized();

        sprites.Clear();
        GetComponentsInChildren(true, sprites);

        targetAlpha = alphaInactive;
        ApplyAlphaInstant(alphaInactive);

        // 使用全新的 VideoController 接管视频
        mainCtrl = VideoController.GetOrCreate(mainVideo);
        failCtrl = VideoController.GetOrCreate(failVideo);
        transCtrl = VideoController.GetOrCreate(transitionVideo);
    }

    void Update()
    {
        LerpAlphaToTarget();

        if (!active)
            return;

        // 鼠标点击判定
        bool clickedOnModule = false;
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 screenPos = Input.mousePosition;
            if (Camera.main != null)
            {
                screenPos.z = Mathf.Abs(Camera.main.transform.position.z);
                Vector3 mouse = Camera.main.ScreenToWorldPoint(screenPos);
                Vector2 pos = new Vector2(mouse.x, mouse.y);
                Collider2D col = GetComponent<Collider2D>();
                if (col != null && col.OverlapPoint(pos))
                {
                    clickedOnModule = true;
                }
            }
        }

        switch (currentState)
        {
            case PatState.TransitionLoop:
                if (isInterrupted)
                {
                    // 弹窗未处理前，禁止点击切回拍泥，一直停留在 Idle 待机
                    break;
                }

                if (clickedOnModule)
                {
                    currentState = PatState.SwitchingToMain;
                    StartCoroutine(SwitchToMain());
                }
                break;

            case PatState.MainActive:
                if (isInterrupted)
                {
                    // 处于被弹窗打断状态：阻断拍击输入，不累加超时计时器
                    break;
                }

                if (clickedOnModule)
                {
                    RegisterHit();
                }

                if (currentHits > 0)
                {
                    timer += Time.deltaTime;
                    if (timer > timeLimit)
                    {
                        StartCoroutine(PlayFailThenTransition());
                    }
                }
                break;
        }
    }

    public void Activate()
    {
        if (active || complete)
        {
            Debug.LogWarning($"⚠️ [{name}] Activate() 被重复调用，已忽略。");
            return;
        }

        currentState = PatState.TransitionLoop;
        currentHits = 0;
        timer = 0f;
        isInterrupted = false;
        hasTriggeredInterrupt = false;
        HasFirstFrameReady = false;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        HideAll();
        EnsureFallbackInitialized();
        SetTargetAlpha(alphaInactive);

        // 提前让主视频和失败视频准备好，无缝衔接
        mainCtrl?.PrepareNow();
        failCtrl?.PrepareNow();

        StartCoroutine(PlayTransitionLoop());
        Debug.Log($"🎯 [{name}] Activated");
    }

    IEnumerator PlayTransitionLoop()
    {
        if (transitionVideo == null || transitionRaw == null || transitionVideo.clip == null)
        {
            HasFirstFrameReady = true;
            yield break;
        }

        if (transitionVideo.targetTexture != null)
            transitionRaw.texture = transitionVideo.targetTexture;

        transitionVideo.playbackSpeed = Mathf.Max(0.1f, transitionSpeed);
        transitionVideo.isLooping = true;

        if (!transitionVideo.isPrepared)
        {
            bool ready = false;
            transCtrl?.PrepareNow(() => ready = true);
            float prepTimeout = 1.5f;
            while (!ready && prepTimeout > 0f && currentState == PatState.TransitionLoop)
            {
                prepTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (currentState != PatState.TransitionLoop)
        {
            transitionVideo.isLooping = false;
            yield break;
        }

        transitionVideo.time = 0.0;
        transitionVideo.Play();
        yield return StartCoroutine(WaitForVideoFrameRendered(transitionVideo));

        if (currentState != PatState.TransitionLoop)
        {
            transitionVideo.isLooping = false;
            yield break;
        }

        transitionRaw.color = Color.white;
        transitionRaw.enabled = true;
        yield return new WaitForEndOfFrame();

        // Fail -> Transition 切换时，等 Transition 真正画出首帧后再隐藏上层 Fail/Main RawImage
        if (failRaw != null && failRaw.enabled) failRaw.enabled = false;
        if (mainRaw != null && mainRaw.enabled) mainRaw.enabled = false;

        HasFirstFrameReady = true;
        Debug.Log($"▶️ [{name}] Transition 开始播放 (原生无缝循环)");

        while (currentState == PatState.TransitionLoop)
        {
            yield return null;
        }

        transitionVideo.isLooping = false;
        Debug.Log($"⏹ [{name}] Transition 状态退出");
    }

    IEnumerator SwitchToMain()
    {
        // 先截当前 Transition 画面垫底并暂停（不立刻关闭 transitionRaw）
        UpdateFallbackBackground(transitionVideo);
        transCtrl?.Pause();

        if (mainVideo != null && mainRaw != null && mainVideo.clip != null)
        {
            if (mainVideo.targetTexture != null) mainRaw.texture = mainVideo.targetTexture;

            // 准备并让主视频真正渲染出第一帧，再揭开显示
            bool isReady = false;
            mainCtrl?.PrepareNow(() => isReady = true);
            while (!isReady) yield return null;

            double targetTime = 0.0;
            if (retainHitsOnInterrupt && hasTriggeredInterrupt && currentHits > 0)
            {
                float progress = Mathf.Clamp01((float)currentHits / Mathf.Max(1, requiredHits));
                targetTime = progress * mainVideo.clip.length;
            }

            long prevFrame = mainVideo.frame;
            mainVideo.time = targetTime;
            mainVideo.Play();

            yield return StartCoroutine(WaitForVideoFrameRendered(mainVideo, 0.35f, prevFrame));
            mainCtrl?.Pause();

            mainRaw.color = Color.white;
            mainRaw.enabled = true;
            yield return new WaitForEndOfFrame();

            Debug.Log($"🖼️ [{name}] Main 首帧已建立 (time={mainVideo.time:F2}s, hits={currentHits})");
        }

        transCtrl?.StopAndReset();
        if (transitionRaw != null) transitionRaw.enabled = false;
        if (failRaw != null) failRaw.enabled = false;

        if (!retainHitsOnInterrupt || !hasTriggeredInterrupt)
        {
            currentHits = 0;
        }

        timer = 0f;
        SetTargetAlpha(alphaActive);
        
        currentState = PatState.MainActive;
    }

    void RegisterHit()
    {
        if (mainVideo == null || mainVideo.clip == null) return;

        currentHits++;
        if (currentHits == 1)
        {
            timer = 0f;
        }

        StartCoroutine(ClickFeedback());

        float progress = Mathf.Clamp01((float)currentHits / Mathf.Max(1, requiredHits));
        double targetTime = progress * mainVideo.clip.length;

        // 【核心修复】：原逻辑是跳到 targetTime 然后播放 0.12 秒
        mainCtrl?.PlayChunk(targetTime, 0.12);

        Debug.Log($"👆 [{name}] Hit {currentHits}/{requiredHits} target={targetTime:F2}s");

        // 检查是否达到打断阈值 (对应流程图: 开始拍第 x / y 下触发打断并弹出消息)
        if (enableInterruptPopup && !hasTriggeredInterrupt && currentHits >= interruptHitCount)
        {
            TriggerInterrupt();
            return;
        }

        if (currentHits >= requiredHits)
        {
            Complete();
        }
    }

    IEnumerator ClickFeedback()
    {
        SetTargetAlpha(alphaClick);
        yield return new WaitForSeconds(0.2f);
        if (currentState == PatState.MainActive)
        {
            SetTargetAlpha(alphaActive);
        }
    }

    void Complete()
    {
        currentState = PatState.Done;

        mainCtrl?.Pause();
        SnapshotCurrentFrameToFallback();
        
        // 保持当前模块最后一帧可见，交由 PatSequenceController 在下一模块/成功视频真正首帧上屏后再隐藏释放。

        SetTargetAlpha(alphaComplete);
        ApplyAlphaInstant(alphaComplete);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Debug.Log($"✅ [{name}] Module Complete");
        controller?.OnModuleCompleted(this);
    }

    public void SnapshotCurrentFrameToFallback()
    {
        if (mainRaw != null && mainRaw.enabled && mainVideo != null)
            UpdateFallbackBackground(mainVideo);
        else if (transitionRaw != null && transitionRaw.enabled && transitionVideo != null)
            UpdateFallbackBackground(transitionVideo);
        else if (failRaw != null && failRaw.enabled && failVideo != null)
            UpdateFallbackBackground(failVideo);
    }

    /// <summary>
    /// 由 PatSequenceController 在成功视频或下一模块真正渲染首帧后调用，确保无缝衔接
    /// </summary>
    public void ForceHideAndRelease()
    {
        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed -= HandlePopupClosed;
        }
        StopAllCoroutines();
        if (transitionVideo != null) transitionVideo.isLooping = false;
        HideAll();
        transCtrl?.Release();
        mainCtrl?.Release();
        failCtrl?.Release();
    }

    IEnumerator PlayFailThenTransition()
    {
        currentState = PatState.FailSequence;
        timer = 0f;

        mainCtrl?.Pause();
        SetTargetAlpha(alphaInactive);
        
        // 截当前主视频画面到垫底背景，并暂留 mainRaw 直到 fail 首帧就绪
        UpdateFallbackBackground(mainVideo);

        // FAIL VIDEO
        if (failVideo != null && failRaw != null && failVideo.clip != null)
        {
            if (failVideo.targetTexture != null) failRaw.texture = failVideo.targetTexture;
            failVideo.isLooping = false;

            if (!failVideo.isPrepared)
            {
                bool ready = false;
                failCtrl?.PrepareNow(() => ready = true);
                while (!ready) yield return null;
            }

            long prevFrame = failVideo.frame;
            failVideo.time = 0.0;
            failVideo.Play();

            Debug.Log($"💥 [{name}] Fail Video 开始");
            yield return StartCoroutine(WaitForVideoFrameRendered(failVideo, 0.35f, prevFrame));

            failRaw.color = Color.white;
            failRaw.enabled = true;
            yield return new WaitForEndOfFrame();

            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;

            double clipLen = failVideo.clip != null ? failVideo.clip.length : failVideo.length;
            float maxWait = Mathf.Max(0.5f, (float)clipLen + 0.5f);
            float elapsed = 0f;
            while (failVideo.isPlaying && elapsed < maxWait)
            {
                // 持续保存最后有效帧或临近结尾前确保 fallback 为最新画面
                if (failVideo.frame >= 0 && elapsed > 0.05f)
                    UpdateFallbackBackground(failVideo);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            UpdateFallbackBackground(failVideo);
            failCtrl?.StopAndReset();
        }
        else
        {
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;
        }

        // 注意：若 failRaw 仍开着，PlayTransitionLoop 会在 transition 首帧绘制完毕后统一关闭 failRaw
        currentState = PatState.TransitionLoop;
        StartCoroutine(PlayTransitionLoop());
    }

    public void Deactivate()
    {
        SnapshotCurrentFrameToFallback();
        currentState = PatState.Inactive;
        timer = 0f;

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed -= HandlePopupClosed;
        }

        if (interruptIdleRoutine != null)
        {
            StopCoroutine(interruptIdleRoutine);
            interruptIdleRoutine = null;
        }

        isInterrupted = false;

        StopAllCoroutines();
        if (transitionVideo != null) transitionVideo.isLooping = false;

        transCtrl?.StopAndReset();
        mainCtrl?.Pause();
        failCtrl?.StopAndReset();

        // ⚠️ 保险延迟隐藏：正常切换已由 PatSequenceController 等下一模块首帧渲染后调用 ForceHideAndRelease
        StartCoroutine(DelayedDeactivateRoutine());

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        if (complete)
        {
            SetTargetAlpha(alphaComplete);
            ApplyAlphaInstant(alphaComplete);
        }
        else
        {
            SetTargetAlpha(alphaInactive);
            ApplyAlphaInstant(alphaInactive);
        }
    }

    IEnumerator DelayedDeactivateRoutine()
    {
        yield return new WaitForSecondsRealtime(0.15f);
        HideAll();
        
        // 彻底释放当前模块的视频资源！
        // 如果不释放，4个模块 = 12个视频同时驻留后台，直接导致硬件解码器死机（停住不播）。
        transCtrl?.Release();
        mainCtrl?.Release();
        failCtrl?.Release();
    }

    void HideAll()
    {
        if (transitionRaw != null) transitionRaw.enabled = false;
        if (mainRaw != null) mainRaw.enabled = false;
        if (failRaw != null) failRaw.enabled = false;
    }

    void ApplyAlphaInstant(float a)
    {
        foreach (var sr in sprites)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a = a;
            sr.color = c;
        }
    }

    void SetTargetAlpha(float a)
    {
        targetAlpha = a;
    }

    void LerpAlphaToTarget()
    {
        foreach (var sr in sprites)
        {
            if (sr == null) continue;
            Color c = sr.color;
            c.a = Mathf.Lerp(c.a, targetAlpha, Time.deltaTime * alphaLerpSpeed);
            sr.color = c;
        }
    }

    // ==============================================
    // GPU 垫底截图：零 ReadPixels 回读卡顿与格式兼容问题
    // ==============================================
    private void EnsureFallbackInitialized()
    {
        if (fallbackBackground == null) return;
        fallbackBackground.enabled = true;
        if (fallbackBackground.texture == null)
        {
            fallbackBackground.color = Color.black;
        }
    }

    private void UpdateFallbackBackground(VideoPlayer vp)
    {
        if (fallbackBackground == null || vp == null || vp.targetTexture == null) return;

        RenderTexture rtTex = vp.targetTexture;
        if (sharedFallbackRT == null || !sharedFallbackRT.IsCreated() ||
            sharedFallbackRT.width != rtTex.width || sharedFallbackRT.height != rtTex.height ||
            sharedFallbackRT.format != rtTex.format)
        {
            if (sharedFallbackRT != null)
            {
                sharedFallbackRT.Release();
                Destroy(sharedFallbackRT);
            }
            sharedFallbackRT = new RenderTexture(rtTex.width, rtTex.height, 0, rtTex.format);
            sharedFallbackRT.name = "RT_PatFallbackSnapshot";
            sharedFallbackRT.Create();
        }

        Graphics.Blit(rtTex, sharedFallbackRT);
        fallbackBackground.texture = sharedFallbackRT;
        fallbackBackground.color = Color.white;
        fallbackBackground.enabled = true;
    }

    private static IEnumerator WaitForVideoFrameRendered(VideoPlayer vp, float timeoutSeconds = 0.35f, long previousFrame = long.MinValue)
    {
        if (vp == null) yield break;
        float t = 0f;
        while (t < timeoutSeconds)
        {
            if (vp.frame >= 0 && (previousFrame == long.MinValue || vp.frame != previousFrame || t > 0.05f))
                break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();
    }

    // ==============================================
    // 🔔 弹窗打断逻辑实现
    // ==============================================

    private void TriggerInterrupt()
    {
        hasTriggeredInterrupt = true;
        isInterrupted = true;
        Debug.Log($"🚨 [{name}] 拍击达到第 {interruptHitCount} 次，触发打断！播放 Fail 动画 -> 切入 Idle 待机，并呼出弹窗。");

        timer = 0f; // 暂停计时器，避免打断期间超时判 Fail

        // 1. 触发原生丝滑的 Fail -> Transition (Idle) 动画序列
        StartCoroutine(PlayFailThenTransition());

        // 2. 呼出 PopupManager 弹窗 (常驻 + 循环提示音骚扰)
        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed += HandlePopupClosed;

            if (interruptPopupSO != null)
            {
                PopupManager.Instance.Show(interruptPopupSO);
            }
            else if (!string.IsNullOrEmpty(interruptPopupId))
            {
                PopupManager.Instance.ShowById(interruptPopupId);
            }
            else
            {
                Debug.LogWarning($"⚠️ [{name}] enableInterruptPopup 为 true，但未指定 interruptPopupSO 或 interruptPopupId！");
            }
        }
        else
        {
            Debug.LogWarning("⚠️ [PatModule] 场景中未找到 PopupManager 实例，打断弹窗无法拉起！");
        }
    }

    private void HandlePopupClosed(PopupWindowDataSO data)
    {
        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed -= HandlePopupClosed;
        }

        if (!isInterrupted) return;

        Debug.Log($"✅ [{name}] 弹窗已处理完毕！解除打断锁定。当前泥巴停留在 Idle 待机界面，点击即可切回拍泥完成剩余次数！");
        isInterrupted = false;

        if (completeModuleOnPopupDismiss)
        {
            // 若勾选了跳过，则直接完成本模块
            Complete();
        }
        else if (autoResumePatOnPopupDismiss)
        {
            // 若勾选了自动切回，关闭弹窗瞬间自动调用切回拍泥
            if (currentState == PatState.TransitionLoop)
            {
                currentState = PatState.SwitchingToMain;
                StartCoroutine(SwitchToMain());
            }
        }
    }

    private void OnDestroy()
    {
        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed -= HandlePopupClosed;
        }
    }
}