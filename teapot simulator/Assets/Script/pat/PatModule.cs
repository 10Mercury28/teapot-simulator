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

    // Snapshot variables
    private Texture2D snapshotTex;

    void Start()
    {
        controller = GetComponentInParent<PatSequenceController>();

        HideAll();

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

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = true;

        HideAll();
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
            yield break;

        transitionRaw.enabled = true;
        transitionRaw.color = Color.white;

        if (transitionVideo.targetTexture != null)
            transitionRaw.texture = transitionVideo.targetTexture;

        transitionVideo.playbackSpeed = Mathf.Max(0.1f, transitionSpeed);

        Debug.Log($"▶️ [{name}] Transition 开始播放");

        System.Action playLoop = null;
        playLoop = () => {
            if (currentState == PatState.TransitionLoop)
            {
                transCtrl?.PlayFull(playLoop);
            }
        };

        // 启动第一次循环
        playLoop();

        while (currentState == PatState.TransitionLoop)
        {
            yield return null;
        }

        transCtrl?.StopAndReset();
        transitionRaw.enabled = false;
        Debug.Log($"⏹ [{name}] Transition 结束");
    }

    IEnumerator SwitchToMain()
    {
        // 瞬间截图当前正在播放的 Transition，更新到那个始终垫底的背景图上
        UpdateFallbackBackground(transitionVideo);

        // 现在可以立刻无缝关掉之前的视频了，屏幕最底下的 -1 Canvas 已经印上了我们的定格画
        transCtrl?.StopAndReset();
        if (transitionRaw != null) transitionRaw.enabled = false;
        if (failRaw != null) failRaw.enabled = false;

        if (mainVideo != null && mainRaw != null && mainVideo.clip != null)
        {
            if (mainVideo.targetTexture != null) mainRaw.texture = mainVideo.targetTexture;
            mainRaw.color = Color.white;
            mainRaw.enabled = true;

            // 准备并让主视频播放一瞬间以建立首帧
            bool isReady = false;
            mainCtrl?.PrepareNow(() => isReady = true);
            
            while (!isReady) yield return null;

            if (retainHitsOnInterrupt && hasTriggeredInterrupt && currentHits > 0)
            {
                float progress = Mathf.Clamp01((float)currentHits / Mathf.Max(1, requiredHits));
                mainVideo.time = progress * mainVideo.clip.length;
            }
            else
            {
                mainVideo.time = 0.0;
            }

            mainVideo.Play();

            // 稍微等待新视频输出第一帧画面（此时底下的画面会被新视频盖上）
            yield return new WaitForSecondsRealtime(0.08f);

            mainCtrl?.Pause();
            Debug.Log($"🖼️ [{name}] Main 首帧已建立 (time={mainVideo.time:F2}s, hits={currentHits})");
        }

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
        
        // ⚠️ 取消这里的延迟隐藏！
        // 因为 0.2 秒的死板延迟经常会快于成功视频加载的时间，导致漏出蓝屏。
        // 现在我们把隐藏的控制权交给 PatSequenceController。
        // 它会在确认成功视频真正有画面后，调用 ForceHideAndRelease()。

        SetTargetAlpha(alphaComplete);
        ApplyAlphaInstant(alphaComplete);

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        Debug.Log($"✅ [{name}] Module Complete");
        controller?.OnModuleCompleted(this);
    }

    /// <summary>
    /// 由 PatSequenceController 在成功视频真正开始播放后调用，确保无缝衔接
    /// </summary>
    public void ForceHideAndRelease()
    {
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
        
        // 瞬间截图当前的主视频画面，印到底层背景上
        UpdateFallbackBackground(mainVideo);

        // FAIL VIDEO
        if (failVideo != null && failRaw != null && failVideo.clip != null)
        {
            if (failVideo.targetTexture != null) failRaw.texture = failVideo.targetTexture;
            failRaw.color = Color.white;
            failRaw.enabled = true;

            Debug.Log($"💥 [{name}] Fail Video 开始");

            bool failDone = false;
            failCtrl?.PlayFull(() => failDone = true);
            
            // 稍等 Fail 出画面，然后撤掉主视频
            yield return new WaitForSecondsRealtime(0.08f);
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;

            while (!failDone) yield return null;

            // Fail结束，切回 Transition
            UpdateFallbackBackground(failVideo);
            failRaw.enabled = false;
        }
        else
        {
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;
        }

        currentState = PatState.TransitionLoop;
        StartCoroutine(PlayTransitionLoop());
    }

    public void Deactivate()
    {
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

        transCtrl?.StopAndReset();
        mainCtrl?.Pause();
        failCtrl?.StopAndReset();

        // ⚠️ 修复模块切换蓝屏：延迟 0.15 秒再隐藏，
        // 给下一个模块的视频留出足够的时间加载第一帧，完美无缝衔接。
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
    // Snapshot 黑科技：更新垫底的 Canvas 背景图
    // ==============================================
    private void UpdateFallbackBackground(VideoPlayer vp)
    {
        if (fallbackBackground == null || vp == null || vp.targetTexture == null) return;

        RenderTexture rtTex = vp.targetTexture;
        if (snapshotTex == null || snapshotTex.width != rtTex.width || snapshotTex.height != rtTex.height)
        {
            if (snapshotTex != null) Destroy(snapshotTex);
            snapshotTex = new Texture2D(rtTex.width, rtTex.height, TextureFormat.RGB24, false);
        }

        RenderTexture currentActiveRT = RenderTexture.active;
        RenderTexture.active = rtTex;
        snapshotTex.ReadPixels(new Rect(0, 0, rtTex.width, rtTex.height), 0, 0);
        snapshotTex.Apply();
        RenderTexture.active = currentActiveRT;

        fallbackBackground.texture = snapshotTex;
        fallbackBackground.color = Color.white;
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