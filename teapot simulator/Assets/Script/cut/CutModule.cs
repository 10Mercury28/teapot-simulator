using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Cut 核心模块：重构为规范的状态机架构，并接入 VideoController 接管视频与资源释放。
/// 与 PatModule 保持一致的设计标准：无零散轮询、无显存泄露、高容错手感。
/// </summary>
public class CutModule : MonoBehaviour
{
    // ============================================================
    // 区域配置（必须挂 Collider2D）
    // ============================================================

    [Header("区域（必须挂 Collider2D）")]
    public Collider2D areaA;
    public Collider2D areaB;
    public Collider2D areaC;

    [Header("区域可视根")]
    [Tooltip("把 A/B/C 条或图形放在这里。成功后会自动隐藏。")]
    public GameObject regionsRoot;


    // ============================================================
    // 视频组件与显示 UI
    // ============================================================

    [Header("Cut 视频组件")]
    public VideoPlayer videoA;          // 主视频，实时划动 scrub
    public VideoPlayer videoB;          // 失败视频
    public VideoPlayer transition;      // 转场视频

    [Header("待机准备循环视频 (Prep Loop)")]
    [Tooltip("可交互待机阶段循环播放的准备视频（如 cut prepare 1 / cut prepare 2）；留空则沿用主视频第 0 帧静止待机")]
    public VideoClip prepVideoClip;
    [Tooltip("可选：手动指定 Prep VideoPlayer；留空时若配了 prepVideoClip 会自动在运行时创建")]
    public VideoPlayer prepVideo;
    [Tooltip("可选：手动指定 Prep RawImage；留空时会自动在 rawA 同级创建对齐的全屏 RawImage")]
    public RawImage rawPrep;

    [Header("转场视频时长控制")]
    [Tooltip("转场视频播放时长（秒）。设置比如 3.5 秒。0 表示等原视频全部播完。")]
    public float transitionDuration = 3.5f;

    [Tooltip("允许玩家在播放转场视频期间点击鼠标提前跳过直接进入下一个 Cut")]
    public bool allowSkipTransitionOnClick = false;

    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;

    private VideoController ctrlA;
    private VideoController ctrlB;
    private VideoController ctrlTrans;
    private VideoController ctrlComplete;
    private VideoController ctrlPrep;
    private RenderTexture runtimePrepRT;
    private bool createdRuntimePrepVP = false;
    private bool createdRuntimePrepRaw = false;
    private bool mainFirstFrameRendered = false;
    private Coroutine pendingShowMainRoutine = null;


    // ============================================================
    // COMPLETE 提示
    // ============================================================

    [Header("Complete 提示")]
    [Tooltip("成功切完之后显示的 Complete GameObject / Panel。")]
    public GameObject completeRoot;

    [Tooltip("如果 Complete 本身是视频，把它的 VideoPlayer 拖这里。不是视频可留空。")]
    public VideoPlayer completeVideo;

    [Tooltip("如果 Complete 不是视频，显示多少秒以后进入 Transition。")]
    public float completeDisplayDuration = 0.8f;


    // ============================================================
    // TRAIL 轨迹效果
    // ============================================================

    [Header("轨迹效果")]
    public TrailRenderer trailPrefab;

    private TrailRenderer activeTrail;


    // ============================================================
    // 透明度控制
    // ============================================================

    [Header("透明度控制")]
    [Range(0f, 1f)] public float mainAlpha = 0.8f;
    [Range(0f, 1f)] public float failAlpha = 0.3f;
    [Range(0f, 1f)] public float idleAlpha = 1.0f;
    public bool enableAlphaControl = true;

    [Header("Trail 显示阈值")]
    [Range(0f, 1f)] public float trailAlphaThreshold = 0.75f;


    // ============================================================
    // 调试与状态机
    // ============================================================

    [Header("调试")]
    public Camera mainCamera;
    public bool debugLog = true;

    public enum ModuleState 
    { 
        Inactive, 
        Idle, 
        Cutting, 
        Failed, 
        Success, 
        Transition, 
        Done 
    }

    [Header("状态（运行时观察）")]
    public ModuleState currentState = ModuleState.Inactive;

    public bool complete => currentState == ModuleState.Done;
    public bool IsMainFrameReady { get; private set; }
    public bool HasPrepVideo => prepVideoClip != null || (prepVideo != null && (prepVideo.clip != null || !string.IsNullOrEmpty(prepVideo.url)));


    // ============================================================
    // 弹窗打断配置 (Popup Interruption)
    // ============================================================

    [Header("=== 入场弹窗打断配置 (Enter Interruption) ===")]
    [Tooltip("是否在准备好 Idle 待机后触发弹窗打断（如 Day 2 居士微信、老师微信）")]
    public bool enableEnterInterrupt = false;

    [Tooltip("是否必须前一天'交心'（LikeU）才触发。若为 true，未交心则跳过；为 false 则无条件触发。")]
    public bool requireLikeU = false;

    [Tooltip("关联的弹窗数据 SO（如 Day2_HA 或 Day2_ShaoA）")]
    public PopupWindowDataSO interruptPopupSO;

    [Tooltip("弹窗唯一 ID（若未指定 SO，则通过 ID 自动在 PopupManager 检索）")]
    public string interruptPopupId = "";

    [Header("=== 转场准备打断配置 (Transition Interruption) ===")]
    [Tooltip("是否在播放转场/准备视频期间触发打断弹窗（例如 Cut 1 转场至 Cut 2 时弹出老师通知）")]
    public bool enableTransitionInterrupt = false;

    [Tooltip("转场视频是否一直保持/循环播放，直到弹窗关闭才结束转场（对应'显示视频直至弹窗结束'）")]
    public bool transitionHoldUntilPopupClosed = true;

    [Tooltip("转场关联的弹窗数据 SO")]
    public PopupWindowDataSO transitionPopupSO;

    [Tooltip("转场弹窗唯一 ID")]
    public string transitionPopupId = "";

    [Header("打断运行时状态 (只读)")]
    [SerializeField] private bool isInterrupted = false;
    [SerializeField] private bool hasTriggeredInterrupt = false;
    [SerializeField] private bool isTransitionInterrupted = false;
    [SerializeField] private bool hasTriggeredTransitionInterrupt = false;

    public bool IsInterrupted() => isInterrupted;
    public bool IsTransitionInterrupted() => isTransitionInterrupted;


    // ============================================================
    // 内部状态
    // ============================================================

    private bool inB = false;
    private bool hasReachedEnd = false;
    private float progress = 0f;

    private CutSequenceController controller;

    private float currentRegionsAlpha = 1f;
    private bool regionsVisible = false;

    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();


    // ============================================================
    // INITIALIZE（由 CutSequenceController 调用）
    // ============================================================

    public void Initialize(CutSequenceController ctrl)
    {
        controller = ctrl;
        IsMainFrameReady = false;
        mainFirstFrameRendered = false;
        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }

        if (debugLog) Debug.Log($"✂️ [{name}] CutModule 初始化启动");

        // 1. 缓存引导条图形并提升 SortingOrder 确保浮在全屏视频 Canvas 上
        CacheAlphaTargets();

        // 2. 自动装配运行时 Prep 视频播放器与显示层（若配置了 prepVideoClip）
        EnsurePrepVideoSetup();

        // 3. 确保所有 VideoPlayer 的 RenderTexture 已创建并正确绑定到对应的 RawImage
        EnsureVideoRenderTexture(videoA, rawA);
        EnsureVideoRenderTexture(videoB, rawB);
        EnsureVideoRenderTexture(transition, rawTrans);
        EnsureVideoRenderTexture(prepVideo, rawPrep);

        // 4. 绑定 VideoController
        ctrlA = VideoController.GetOrCreate(videoA);
        ctrlB = VideoController.GetOrCreate(videoB);
        ctrlTrans = VideoController.GetOrCreate(transition);
        ctrlComplete = VideoController.GetOrCreate(completeVideo);
        ctrlPrep = VideoController.GetOrCreate(prepVideo);

        // 5. 初始隐藏 Complete 与转场
        if (completeRoot != null) completeRoot.SetActive(false);
        if (completeVideo != null)
        {
            completeVideo.playOnAwake = false;
            completeVideo.isLooping = false;
            completeVideo.Stop();
        }
        if (transition != null)
        {
            transition.playOnAwake = false;
            transition.isLooping = false;
        }

        // 6. 先准备主视频第 0 帧与待机 Prep 循环视频，首帧就绪并切入待机后再按需弹入场弹窗
        StartCoroutine(SetupMainVideoInitialFrame());
    }

    private static void EnsureVideoRenderTexture(VideoPlayer vp, RawImage raw)
    {
        if (vp == null) return;
        if (vp.targetTexture != null)
        {
            if (!vp.targetTexture.IsCreated())
            {
                vp.targetTexture.Create();
            }
            if (raw != null && raw.texture != vp.targetTexture)
            {
                raw.texture = vp.targetTexture;
            }
        }
    }

    private bool HasValidPrepSource()
    {
        return prepVideo != null && rawPrep != null && (prepVideo.clip != null || !string.IsNullOrEmpty(prepVideo.url));
    }

    private void EnsurePrepVideoSetup()
    {
        if (prepVideo == null && prepVideoClip != null)
        {
            GameObject vpGo = new GameObject("VP_Prep");
            vpGo.transform.SetParent(transform, false);

            prepVideo = vpGo.AddComponent<VideoPlayer>();
            prepVideo.playOnAwake = false;
            prepVideo.isLooping = true;
            prepVideo.renderMode = VideoRenderMode.RenderTexture;
            prepVideo.aspectRatio = VideoAspectRatio.FitInside;
            prepVideo.audioOutputMode = VideoAudioOutputMode.Direct;
            prepVideo.clip = prepVideoClip;

            if (runtimePrepRT == null)
            {
                runtimePrepRT = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
                {
                    name = $"RT_CutPrep_{name}",
                    hideFlags = HideFlags.DontSave
                };
                runtimePrepRT.Create();
            }

            prepVideo.targetTexture = runtimePrepRT;
            createdRuntimePrepVP = true;
        }
        else if (prepVideo != null && prepVideoClip != null && prepVideo.clip == null)
        {
            prepVideo.clip = prepVideoClip;
        }

        if (rawPrep == null && prepVideo != null && rawA != null)
        {
            GameObject rawGo = new GameObject("prep", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawGo.transform.SetParent(rawA.transform.parent, false);
            rawGo.transform.SetSiblingIndex(rawA.transform.GetSiblingIndex());

            RectTransform srcRt = rawA.rectTransform;
            RectTransform dstRt = rawGo.GetComponent<RectTransform>();
            dstRt.anchorMin = srcRt.anchorMin;
            dstRt.anchorMax = srcRt.anchorMax;
            dstRt.anchoredPosition = srcRt.anchoredPosition;
            dstRt.sizeDelta = srcRt.sizeDelta;
            dstRt.pivot = srcRt.pivot;
            dstRt.localRotation = srcRt.localRotation;
            dstRt.localScale = srcRt.localScale;
            dstRt.localPosition = srcRt.localPosition;

            rawPrep = rawGo.GetComponent<RawImage>();
            rawPrep.color = Color.white;
            rawPrep.raycastTarget = false;
            rawPrep.uvRect = rawA.uvRect;
            rawPrep.texture = prepVideo.targetTexture;
            rawPrep.enabled = false;
            createdRuntimePrepRaw = true;
        }

        if (prepVideo != null && rawPrep != null && prepVideo.targetTexture != null)
        {
            rawPrep.texture = prepVideo.targetTexture;
        }
    }

    private IEnumerator SetupMainVideoInitialFrame()
    {
        SetRegionsActive(false);
        ApplyRegionsAlpha(idleAlpha);

        if (videoA != null)
        {
            if (!videoA.enabled) videoA.enabled = true;

            bool prepared = false;
            ctrlA?.PrepareNow(() => prepared = true);

            float timeout = 2.5f;
            while (!prepared && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (!prepared)
            {
                Debug.LogWarning($"⚠️ [{name}] 主视频 Prepare 超时，继续流程避免卡死");
            }

            // 强制渲染一次首帧到 RenderTexture，确保开始切割瞬间切换零黑帧
            yield return StartCoroutine(RenderFirstFrame(videoA, false));
        }

        if (HasValidPrepSource())
        {
            if (!prepVideo.enabled) prepVideo.enabled = true;
            prepVideo.isLooping = true;

            if (!prepVideo.isPrepared)
            {
                bool prepReady = false;
                ctrlPrep?.PrepareNow(() => prepReady = true);

                float prepTimeout = 2.5f;
                while (!prepReady && prepTimeout > 0f)
                {
                    prepTimeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            EnsureVideoRenderTexture(prepVideo, rawPrep);
            prepVideo.time = 0;
            prepVideo.Play();
            yield return StartCoroutine(WaitForVideoFirstFrame(prepVideo, 0.6f));
            EnableOnly(rawPrep);
        }
        else
        {
            EnsureVideoRenderTexture(videoA, rawA);
            EnableOnly(rawA);
        }

        progress = 0f;
        inB = false;
        hasReachedEnd = false;
        currentState = ModuleState.Idle;
        IsMainFrameReady = true;

        // 进入 Prepare 后再拉起入场打断弹窗（例如 Cut 1 居士微信 / Cut 2 老师微信）
        CheckAndTriggerEnterInterrupt();

        SetRegionsActive(!ShouldHideInteractiveUI());
        ApplyRegionsAlpha(idleAlpha);

        if (debugLog) Debug.Log($"🎬 [{name}] Cut 进入 Idle 等待切割 (Prep循环={HasValidPrepSource()}, 弹窗挂起={isInterrupted})");
    }

    private IEnumerator RenderFirstFrame(VideoPlayer vp, bool forceSeekToZero = false)
    {
        if (vp == null) yield break;
        if (!vp.enabled) vp.enabled = true;
        if (vp.targetTexture != null && !vp.targetTexture.IsCreated())
        {
            vp.targetTexture.Create();
        }
        if (vp.canSetPlaybackSpeed) vp.playbackSpeed = 1f;
        vp.isLooping = false;

        bool needSeek = forceSeekToZero || vp.time > 0.05 || vp.frame > 0;
        int renderedFrames = 0;
        bool seekDone = !needSeek;
        VideoPlayer.FrameReadyEventHandler onFrame = (player, idx) =>
        {
            if (seekDone) renderedFrames++;
        };
        VideoPlayer.EventHandler onSeek = (player) => { seekDone = true; };

        bool prevSendEvents = vp.sendFrameReadyEvents;
        vp.sendFrameReadyEvents = true;
        vp.frameReady += onFrame;
        if (needSeek) vp.seekCompleted += onSeek;

        if (needSeek)
        {
            vp.time = 0;
        }
        vp.Play();

        float timeout = 1.4f;
        while (timeout > 0f && !(seekDone && renderedFrames >= 4))
        {
            if (!seekDone && timeout < 0.7f)
            {
                seekDone = true;
            }
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        if (vp != null)
        {
            vp.frameReady -= onFrame;
            if (needSeek) vp.seekCompleted -= onSeek;
            vp.sendFrameReadyEvents = prevSendEvents;
            vp.Pause();
            bool gotFrame = renderedFrames >= 1;
            if (vp == videoA)
            {
                mainFirstFrameRendered = gotFrame;
            }
            if (debugLog)
            {
                Debug.Log($"🎞️ [{name}] {vp.name} 首帧渲染{(gotFrame ? "成功" : "超时")} (frames={renderedFrames}, frame={vp.frame}, time={vp.time:F2}s, RT={vp.targetTexture?.name}, created={vp.targetTexture?.IsCreated()})");
            }
        }
    }

    private static IEnumerator WaitForVideoFirstFrame(VideoPlayer vp, float timeoutSeconds = 0.6f, long previousFrame = long.MinValue)
    {
        if (vp == null) yield break;

        bool gotFrame = false;
        float t = 0f;
        long targetMinFrame = previousFrame;
        VideoPlayer.FrameReadyEventHandler onFrame = (player, idx) =>
        {
            if (targetMinFrame == long.MinValue || idx != targetMinFrame || t > 0.1f)
            {
                gotFrame = true;
            }
        };

        bool prevSendEvents = vp.sendFrameReadyEvents;
        vp.sendFrameReadyEvents = true;
        vp.frameReady += onFrame;

        while (t < timeoutSeconds && !gotFrame)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return new WaitForEndOfFrame();

        if (vp != null)
        {
            vp.frameReady -= onFrame;
            vp.sendFrameReadyEvents = prevSendEvents;
        }
    }


    // ============================================================
    // UPDATE 状态机驱动
    // ============================================================

    private bool ShouldHideInteractiveUI()
    {
        bool popupShowing = PopupManager.ExistingInstance != null && PopupManager.ExistingInstance.HasActivePopup;
        return isInterrupted || popupShowing;
    }

    private void Update()
    {
        if (currentState == ModuleState.Inactive || 
            currentState == ModuleState.Done || 
            currentState == ModuleState.Failed || 
            currentState == ModuleState.Success || 
            currentState == ModuleState.Transition)
        {
            return;
        }

        if (Time.timeScale <= 0f)
        {
            return;
        }

        bool hideInteractive = ShouldHideInteractiveUI();

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        if (mainCamera == null) return;

        Vector2 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        switch (currentState)
        {
            case ModuleState.Idle:
                // 任何弹窗显示或入场挂起期间：隐藏当前交互引导条并锁定切割输入
                if (hideInteractive)
                {
                    if (regionsVisible) SetRegionsActive(false);
                    return;
                }

                if (!regionsVisible)
                {
                    SetRegionsActive(true);
                    ApplyRegionsAlpha(idleAlpha);
                }

                if (Input.GetMouseButtonDown(0) && IsInside(mouseWorld, areaA))
                {
                    if (!CanDrawTrail())
                    {
                        if (debugLog) Debug.Log("✋ Trail blocked: regions alpha below threshold.");
                        return;
                    }

                    StartCutting(mouseWorld);
                }
                break;

            case ModuleState.Cutting:
                if (hideInteractive)
                {
                    if (regionsVisible) SetRegionsActive(false);
                    if (activeTrail != null && activeTrail.gameObject.activeSelf)
                        activeTrail.gameObject.SetActive(false);
                    return;
                }

                if (!regionsVisible)
                {
                    SetRegionsActive(true);
                    ApplyRegionsAlpha(mainAlpha);
                }

                HandleCuttingInput(mouseWorld);
                break;
        }
    }


    // ============================================================
    // 切割输入与拖拽判定
    // ============================================================

    private void StartCutting(Vector2 mouseWorld)
    {
        currentState = ModuleState.Cutting;
        inB = false;
        hasReachedEnd = false;
        progress = 0f;

        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }

        EnsureVideoRenderTexture(videoA, rawA);

        if (videoA != null)
        {
            pendingShowMainRoutine = StartCoroutine(EnsureMainVideoVisibleDuringCut());
        }
        else
        {
            if (prepVideo != null && prepVideo.isPlaying)
            {
                prepVideo.Pause();
            }
            EnableOnly(rawA);
        }

        if (trailPrefab != null && activeTrail == null)
        {
            activeTrail = Instantiate(trailPrefab, mouseWorld, Quaternion.identity);
            activeTrail.emitting = false;
            activeTrail.Clear();
            activeTrail.time = 999f;
            activeTrail.autodestruct = false;
            StartCoroutine(EnableTrailAfterFrame(activeTrail));
            activeTrail.gameObject.SetActive(CanDrawTrail());
        }

        if (rawA != null && rawA.enabled && enableAlphaControl)
        {
            ApplyRegionsAlpha(mainAlpha);
        }

        if (debugLog) Debug.Log($"✂️ [{name}] Cut started in A");
    }

    private IEnumerator EnsureMainVideoVisibleDuringCut()
    {
        if (videoA != null)
        {
            if (!videoA.enabled) videoA.enabled = true;
            EnsureVideoRenderTexture(videoA, rawA);

            if (!videoA.isPrepared)
            {
                videoA.Prepare();
                float prepTimeout = 1.5f;
                while (!videoA.isPrepared && prepTimeout > 0f && currentState == ModuleState.Cutting)
                {
                    prepTimeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            if (currentState != ModuleState.Cutting)
            {
                pendingShowMainRoutine = null;
                yield break;
            }

            int targetFrames = mainFirstFrameRendered ? 1 : 4;
            int renderedFrames = 0;
            VideoPlayer.FrameReadyEventHandler onFrame = (player, idx) => { renderedFrames++; };
            bool prevSendEvents = videoA.sendFrameReadyEvents;
            videoA.sendFrameReadyEvents = true;
            videoA.frameReady += onFrame;

            bool startedPlaybackOurselves = false;
            if (!videoA.isPlaying && progress <= 0.01f)
            {
                if (videoA.canSetPlaybackSpeed) videoA.playbackSpeed = 1f;
                if (!mainFirstFrameRendered && videoA.time > 0.25)
                {
                    videoA.time = 0;
                }
                videoA.Play();
                startedPlaybackOurselves = true;
            }

            float waitTimeout = 0.8f;
            while (renderedFrames < targetFrames && waitTimeout > 0f && currentState == ModuleState.Cutting)
            {
                waitTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForEndOfFrame();

            if (videoA != null)
            {
                videoA.frameReady -= onFrame;
                videoA.sendFrameReadyEvents = prevSendEvents;
                if (startedPlaybackOurselves && progress <= 0.04f)
                {
                    videoA.Pause();
                }
                if (renderedFrames >= 1) mainFirstFrameRendered = true;
            }
        }

        if (currentState == ModuleState.Cutting)
        {
            if (prepVideo != null && prepVideo.isPlaying)
            {
                prepVideo.Pause();
            }
            EnableOnly(rawA);
            if (rawA != null && rawA.enabled && enableAlphaControl)
            {
                ApplyRegionsAlpha(mainAlpha);
            }
        }

        pendingShowMainRoutine = null;
    }

    private void HandleCuttingInput(Vector2 mouseWorld)
    {
        // 1. 按住鼠标拖拽
        if (Input.GetMouseButton(0))
        {
            // 更新 Trail 跟随
            if (activeTrail != null)
            {
                bool allow = CanDrawTrail();
                if (!allow)
                {
                    activeTrail.emitting = false;
                    activeTrail.gameObject.SetActive(false);
                }
                else
                {
                    if (!activeTrail.gameObject.activeSelf) activeTrail.gameObject.SetActive(true);
                    if (!activeTrail.emitting) activeTrail.emitting = true;
                    activeTrail.transform.position = new Vector3(mouseWorld.x, mouseWorld.y, activeTrail.transform.position.z);
                }
            }

            // ⭐ 终点到达判定（触碰 areaC、划到 areaB 右端、或划满 88%）
            bool reachedC = (areaC != null && IsInside(mouseWorld, areaC)) ||
                            (areaB != null && mouseWorld.x >= (areaB.bounds.max.x - 0.4f)) ||
                            progress >= 0.88f;

            if (reachedC)
            {
                bool firstReach = !hasReachedEnd;
                hasReachedEnd = true;
                inB = true;
                progress = 1f;

                if (firstReach)
                {
                    // 划到末尾显示切断帧
                    ScrubMainVideo(1f);
                }

                if (rawA != null && rawA.enabled && enableAlphaControl)
                {
                    ApplyRegionsAlpha(mainAlpha);
                }
            }
            else if (!hasReachedEnd)
            {
                // ⭐ 轨道容差判定：上下允许 2.5 倍半高，给足手抖余量
                bool inTrack = false;
                if (areaB != null)
                {
                    Bounds b = areaB.bounds;
                    float yDist = Mathf.Abs(mouseWorld.y - b.center.y);
                    float allowedHalfHeight = Mathf.Max(b.extents.y * 2.5f, 1.2f);
                    bool yOk = yDist <= allowedHalfHeight;
                    bool xOk = mouseWorld.x >= (b.min.x - 0.8f) && mouseWorld.x <= (b.max.x + 0.8f);
                    inTrack = yOk && xOk;
                }

                if (inTrack || IsInside(mouseWorld, areaB))
                {
                    inB = true;
                    if (areaB != null)
                    {
                        float width = areaB.bounds.size.x;
                        float startX = areaB.bounds.min.x;
                        float currentX = Mathf.Clamp(mouseWorld.x, startX, startX + width);
                        float newProgress = Mathf.InverseLerp(startX, startX + width, currentX);
                        progress = Mathf.Max(progress, newProgress); // 进度只能向前
                    }

                    ScrubMainVideo(progress);

                    if (rawA != null && rawA.enabled && enableAlphaControl)
                    {
                        ApplyRegionsAlpha(mainAlpha);
                    }
                }
                else if (inB) // 曾经在轨道内，但在中途未完成时严重偏离出轨 => 失败
                {
                    TriggerFail();
                }
            }
        }

        // 2. 松开鼠标
        if (Input.GetMouseButtonUp(0))
        {
            // 淡出 Trail
            if (activeTrail != null)
            {
                TrailRenderer trailToFade = activeTrail;
                activeTrail = null;
                StartCoroutine(FadeOutAndDestroyTrail(trailToFade, 0.5f));
            }

            if (currentState == ModuleState.Cutting)
            {
                // ⭐ 成功判定：到达过终点 / 松手在 areaC 内 / 进度达到 85% 以上 / X 处于末尾
                bool isSuccess = hasReachedEnd ||
                                 (areaC != null && IsInside(mouseWorld, areaC)) ||
                                 progress >= 0.85f ||
                                 (areaB != null && mouseWorld.x >= (areaB.bounds.max.x - 0.5f));

                if (isSuccess)
                {
                    TriggerSuccess();
                }
                else
                {
                    TriggerFail();
                }
            }
        }
    }

    private void ScrubMainVideo(float p)
    {
        if (ctrlA != null)
        {
            ctrlA.ScrubTo(p, false);
        }
    }


    // ============================================================
    // 失败流程（FAIL）
    // ============================================================

    private void TriggerFail()
    {
        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }
        ctrlA?.CancelScrub();
        currentState = ModuleState.Failed;
        if (debugLog) Debug.Log($"💥 [{name}] 切割失败，开始播放失败视频");
        StartCoroutine(FailRoutine());
    }

    private IEnumerator FailRoutine()
    {
        ctrlA?.CancelScrub();

        if (videoB != null)
        {
            if (!videoB.enabled) videoB.enabled = true;
            EnsureVideoRenderTexture(videoB, rawB);

            if (!videoB.isPrepared)
            {
                bool prepared = false;
                ctrlB?.PrepareNow(() => prepared = true);

                float timeout = 2.0f;
                while (!prepared && timeout > 0f)
                {
                    timeout -= Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // 从与切割进度对应的倒放位置起播，先确认 RenderTexture 渲染出帧再切 rawB，杜绝 1 帧穿帮
            double startTime = videoB.length * Mathf.Clamp01(1f - progress);
            long prevFrame = videoB.frame;
            videoB.isLooping = false;
            videoB.time = startTime;
            videoB.Play();

            yield return StartCoroutine(WaitForVideoFirstFrame(videoB, 0.6f, prevFrame));
            EnableOnly(rawB);

            float maxFailDuration = Mathf.Max(0.5f, (float)(videoB.length - startTime) + 0.2f);
            float failTimer = 0f;
            while (videoB.isPlaying && failTimer < maxFailDuration)
            {
                failTimer += Time.deltaTime;
                yield return null;
            }
            // 暂定在最后一帧，等 videoA 第 0 帧重绘完成后再 Stop 并切回 rawA
            videoB.Pause();
        }
        else
        {
            EnableOnly(rawB);
        }

        // ⭐ 彻底重置并强制刷新 videoA 回到第 0 帧
        if (videoA != null)
        {
            yield return StartCoroutine(RenderFirstFrame(videoA, true));
        }

        // 若配置了 Prep 循环视频，失败后切回持续动态的 Prep 待机循环；否则返回静止的 RawA
        if (HasValidPrepSource())
        {
            if (!prepVideo.enabled) prepVideo.enabled = true;
            EnsureVideoRenderTexture(prepVideo, rawPrep);
            prepVideo.isLooping = true;
            long prevPrepFrame = prepVideo.frame;
            prepVideo.Play();
            yield return StartCoroutine(WaitForVideoFirstFrame(prepVideo, 0.6f, prevPrepFrame));
            EnableOnly(rawPrep);
        }
        else
        {
            EnsureVideoRenderTexture(videoA, rawA);
            EnableOnly(rawA);
        }

        if (videoB != null)
        {
            videoB.Stop();
            videoB.time = 0;
        }
        SetRegionsActive(!ShouldHideInteractiveUI());
        ApplyRegionsAlpha(idleAlpha);

        if (activeTrail != null)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        inB = false;
        hasReachedEnd = false;
        progress = 0f;
        currentState = ModuleState.Idle;

        if (debugLog) Debug.Log($"🔁 [{name}] 失败处理完毕 → 干净重置回第 0 帧并进入 Idle");
    }


    // ============================================================
    // 成功流程（SUCCESS → COMPLETE → TRANSITION → DONE）
    // ============================================================

    private void TriggerSuccess()
    {
        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }
        ctrlA?.CancelScrub();
        currentState = ModuleState.Success;
        if (debugLog) Debug.Log($"🎉 [{name}] Cut 成功！");
        StartCoroutine(SuccessRoutine());
    }

    private IEnumerator SuccessRoutine()
    {
        // 1. 关闭引导线
        SetRegionsActive(false);

        // 2. COMPLETE 提示阶段
        if (completeRoot != null)
        {
            completeRoot.SetActive(true);
        }

        if (completeVideo != null && (completeVideo.clip != null || !string.IsNullOrEmpty(completeVideo.url)))
        {
            yield return StartCoroutine(PlayOneShotVideo(completeVideo));
        }
        else if (completeDisplayDuration > 0f)
        {
            yield return new WaitForSeconds(completeDisplayDuration);
        }

        if (completeRoot != null)
        {
            completeRoot.SetActive(false);
        }

        // 3. TRANSITION 转场阶段：保持 rawA 直至 transition 首帧渲染出来再切换至 rawTrans
        currentState = ModuleState.Transition;

        if (transition != null && (transition.clip != null || !string.IsNullOrEmpty(transition.url)))
        {
            if (enableTransitionInterrupt && (!string.IsNullOrEmpty(transitionPopupId) || transitionPopupSO != null))
            {
                yield return StartCoroutine(PlayTransitionVideoUntilPopupClosed(transition, () => EnableOnly(rawTrans)));
            }
            else
            {
                float maxWait = transitionDuration > 0f ? transitionDuration : -1f;
                yield return StartCoroutine(PlayOneShotVideo(transition, maxWait, allowSkipTransitionOnClick, () => EnableOnly(rawTrans)));
            }
        }
        else
        {
            EnableOnly(rawTrans);
        }

        // 4. 当前 Cut 彻底完成
        currentState = ModuleState.Done;

        if (activeTrail != null)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        if (debugLog) Debug.Log($"✅ [{name}] CutModule 全部流程结束，通知 Controller 启动下一个 Cut");

        // 5. 通知 CutSequenceController（若有 Controller 接管，则交由它等下一模块首帧就绪后再调用 ReleaseResources）
        if (controller != null)
        {
            controller.OnModuleCompleted(this);
        }
        else
        {
            ReleaseResources();
            Debug.LogWarning($"⚠️ [{name}] CutSequenceController 为 null！");
        }
    }

    /// <summary>
    /// 单次精确播放视频：强制关闭循环，结合 loopPointReached、时长上限与可跳过防护
    /// </summary>
    private IEnumerator PlayOneShotVideo(VideoPlayer vp, float maxDuration = -1f, bool allowSkip = false, System.Action onFirstFrameRendered = null)
    {
        if (vp == null) yield break;

        EnsureVideoRenderTexture(vp, vp == transition ? rawTrans : null);
        vp.isLooping = false;
        vp.playOnAwake = false;

        if (!vp.isPrepared)
        {
            vp.Prepare();
            float prepTimeout = 2f;
            while (!vp.isPrepared && prepTimeout > 0f)
            {
                prepTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        long prevFrame = vp.frame;
        vp.time = 0;
        vp.Play();
        yield return StartCoroutine(WaitForVideoFirstFrame(vp, 0.6f, prevFrame));
        onFirstFrameRendered?.Invoke();

        bool finished = false;
        VideoPlayer.EventHandler endHandler = (player) => { finished = true; };
        vp.loopPointReached += endHandler;

        float clipLen = (float)vp.length;
        if (clipLen <= 0.1f) clipLen = 3.5f;

        float targetDuration = (maxDuration > 0f) ? Mathf.Min(clipLen, maxDuration) : clipLen;

        if (debugLog) Debug.Log($"🎬 [{name}] 播放转场视频: {vp.name} (素材原长: {clipLen:F1}s, 播放限时: {targetDuration:F1}s, 支持点击跳过: {allowSkip})");

        float timer = 0f;
        yield return null;

        while (!finished && timer < targetDuration)
        {
            if (allowSkip && timer > 0.4f && Input.GetMouseButtonDown(0))
            {
                if (debugLog) Debug.Log($"⏩ [{name}] 玩家点击提前跳过转场视频");
                break;
            }

            timer += Time.deltaTime;
            yield return null;
        }

        vp.loopPointReached -= endHandler;
        vp.Pause();
    }

    /// <summary>
    /// 仅释放主视频、失败视频与待机 Prep 循环视频的解码器资源（转场阶段交接时先释放前序视频，减轻 macOS 并发解码压力）
    /// </summary>
    public void ReleaseMainAndFailVideos()
    {
        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }
        mainFirstFrameRendered = false;
        ctrlA?.CancelScrub();
        ctrlA?.Release();
        ctrlB?.Release();
        ctrlPrep?.Release();
        if (videoA != null) { videoA.Stop(); videoA.enabled = false; }
        if (videoB != null) { videoB.Stop(); videoB.enabled = false; }
        if (prepVideo != null) { prepVideo.Stop(); prepVideo.enabled = false; }
    }

    /// <summary>
    /// 停止所有视频并彻底释放硬件解码器资源（macOS AVFoundation 防卡死）
    /// </summary>
    public void ReleaseResources()
    {
        if (pendingShowMainRoutine != null)
        {
            StopCoroutine(pendingShowMainRoutine);
            pendingShowMainRoutine = null;
        }
        mainFirstFrameRendered = false;
        ctrlA?.CancelScrub();
        ctrlA?.Release();
        ctrlB?.Release();
        ctrlTrans?.Release();
        ctrlComplete?.Release();
        ctrlPrep?.Release();

        if (videoA != null) { videoA.Stop(); videoA.enabled = false; }
        if (videoB != null) { videoB.Stop(); videoB.enabled = false; }
        if (transition != null) { transition.Stop(); transition.enabled = false; }
        if (completeVideo != null) { completeVideo.Stop(); completeVideo.enabled = false; }

        if (prepVideo != null)
        {
            prepVideo.Stop();
            prepVideo.enabled = false;
            if (createdRuntimePrepVP) prepVideo.targetTexture = null;
        }

        if (rawPrep != null)
        {
            rawPrep.texture = null;
            rawPrep.enabled = false;
            if (createdRuntimePrepRaw)
            {
                Destroy(rawPrep.gameObject);
                rawPrep = null;
                createdRuntimePrepRaw = false;
            }
        }

        if (createdRuntimePrepVP && prepVideo != null)
        {
            Destroy(prepVideo.gameObject);
            prepVideo = null;
            createdRuntimePrepVP = false;
        }

        if (runtimePrepRT != null)
        {
            runtimePrepRT.Release();
            Destroy(runtimePrepRT);
            runtimePrepRT = null;
        }
    }

    // ============================================================
    // 弹窗打断逻辑
    // ============================================================

    private void CheckAndTriggerEnterInterrupt()
    {
        if (!enableEnterInterrupt || hasTriggeredInterrupt) return;

        // 若需要交心，检查前一天是否达成交心
        if (requireLikeU && !GlobalProgressManager.IsLikeU)
        {
            if (debugLog) Debug.Log($"ℹ️ [{name}] requireLikeU 为 true 但未达成交心，跳过本次居士弹窗打断。");
            return;
        }

        hasTriggeredInterrupt = true;
        isInterrupted = true;
        SetRegionsActive(false);

        string popupDesc = interruptPopupSO != null ? interruptPopupSO.name : interruptPopupId;
        if (debugLog) Debug.Log($"🔔 [{name}] 触发切壶入场打断弹窗！Popup: '{popupDesc}'");

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed += HandlePopupClosed;
            bool shown = false;
            if (interruptPopupSO != null)
            {
                shown = PopupManager.Instance.Show(interruptPopupSO);
            }
            else if (!string.IsNullOrEmpty(interruptPopupId))
            {
                shown = PopupManager.Instance.ShowById(interruptPopupId);
            }

            if (!shown)
            {
                Debug.LogWarning($"⚠️ [{name}] enableEnterInterrupt 为 true，但弹窗拉起失败，自动解除打断！");
                PopupManager.Instance.OnPopupClosed -= HandlePopupClosed;
                isInterrupted = false;
                SetRegionsActive(!ShouldHideInteractiveUI());
            }
        }
        else
        {
            Debug.LogWarning($"⚠️ [{name}] 未找到 PopupManager 实例，无法拉起打断弹窗！");
            isInterrupted = false;
            SetRegionsActive(!ShouldHideInteractiveUI());
        }
    }

    private void HandlePopupClosed(PopupWindowDataSO data)
    {
        if (PopupManager.ExistingInstance != null)
        {
            PopupManager.ExistingInstance.OnPopupClosed -= HandlePopupClosed;
        }

        if (!isInterrupted) return;

        isInterrupted = false;
        bool hide = ShouldHideInteractiveUI();
        SetRegionsActive(!hide);
        if (!hide) ApplyRegionsAlpha(idleAlpha);
        if (debugLog) Debug.Log($"🔓 [{name}] 弹窗已处理关闭，解除切壶打断，玩家现在可以开始切割！");
    }

    // ============================================================
    // 转场准备打断视频逻辑 (Transition Video Until Popup Closed)
    // ============================================================

    private IEnumerator PlayTransitionVideoUntilPopupClosed(VideoPlayer vp, System.Action onFirstFrameRendered = null)
    {
        if (vp == null) yield break;

        EnsureVideoRenderTexture(vp, vp == transition ? rawTrans : null);
        isTransitionInterrupted = true;
        hasTriggeredTransitionInterrupt = true;

        vp.isLooping = true;
        vp.playOnAwake = false;

        if (!vp.isPrepared)
        {
            vp.Prepare();
            float prepTimeout = 2f;
            while (!vp.isPrepared && prepTimeout > 0f)
            {
                prepTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        long prevFrame = vp.frame;
        vp.time = 0;
        vp.Play();
        yield return StartCoroutine(WaitForVideoFirstFrame(vp, 0.6f, prevFrame));
        onFirstFrameRendered?.Invoke();

        if (debugLog) Debug.Log($"🎬 [{name}] 播放准备/转场视频，并保持播放直至弹窗结束: {vp.name}");

        // 稍微等待 0.25 秒让转场画面稳定展开
        yield return new WaitForSeconds(0.25f);

        TriggerTransitionInterruptPopup();

        // 核心挂起：等待弹窗完全关闭（不再误把点击弹窗本身当成跳过转场）
        float interruptTimer = 0f;
        while (isTransitionInterrupted)
        {
            // 防御保护 1：如果没有任何活跃弹窗，自动恢复
            if (PopupManager.ExistingInstance != null && !PopupManager.ExistingInstance.HasActivePopup)
            {
                isTransitionInterrupted = false;
                break;
            }

            // 防御保护 2：安全超时（25 秒，受 PauseMenu TimeScale 冻结控制），避免死锁
            interruptTimer += Time.deltaTime;
            if (interruptTimer > 25f)
            {
                Debug.LogWarning($"⚠️ [{name}] 转场打断超时 (25s)，自动收起并进入下一 Cut");
                if (PopupManager.ExistingInstance != null) PopupManager.ExistingInstance.CloseAll();
                isTransitionInterrupted = false;
                break;
            }

            yield return null;
        }

        if (debugLog) Debug.Log($"🔓 [{name}] 转场准备打断弹窗已结束，定格转场末帧等待下一 Cut 就绪！");

        vp.isLooping = false;
        vp.Pause();
    }

    private void TriggerTransitionInterruptPopup()
    {
        string popupDesc = transitionPopupSO != null ? transitionPopupSO.name : transitionPopupId;
        if (debugLog) Debug.Log($"🔔 [{name}] 触发切壶转场准备打断弹窗！Popup: '{popupDesc}'");

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.OnPopupClosed += HandleTransitionPopupClosed;
            bool shown = false;
            if (transitionPopupSO != null)
            {
                shown = PopupManager.Instance.Show(transitionPopupSO);
            }
            else if (!string.IsNullOrEmpty(transitionPopupId))
            {
                shown = PopupManager.Instance.ShowById(transitionPopupId);
            }

            if (!shown)
            {
                Debug.LogWarning($"⚠️ [{name}] enableTransitionInterrupt 为 true，但未指定有效弹窗或拉起失败，解除转场打断！");
                PopupManager.Instance.OnPopupClosed -= HandleTransitionPopupClosed;
                isTransitionInterrupted = false;
            }
        }
        else
        {
            Debug.LogWarning($"⚠️ [{name}] 未找到 PopupManager 实例，无法拉起转场打断弹窗！");
            isTransitionInterrupted = false;
        }
    }

    private void HandleTransitionPopupClosed(PopupWindowDataSO data)
    {
        if (PopupManager.ExistingInstance != null)
        {
            PopupManager.ExistingInstance.OnPopupClosed -= HandleTransitionPopupClosed;
        }

        if (!isTransitionInterrupted) return;

        isTransitionInterrupted = false;
        if (debugLog) Debug.Log($"🔓 [{name}] 转场弹窗已关闭！");
    }

    public void ForceClearInterrupt()
    {
        isInterrupted = false;
        isTransitionInterrupted = false;
        SetRegionsActive(true);
        ApplyRegionsAlpha(idleAlpha);
    }

    private void UnsubscribeAllPopups()
    {
        PopupManager mgr = PopupManager.ExistingInstance;
        if (mgr != null)
        {
            mgr.OnPopupClosed -= HandlePopupClosed;
            mgr.OnPopupClosed -= HandleTransitionPopupClosed;
        }
    }

    private void OnDisable()
    {
        UnsubscribeAllPopups();
    }

    private void OnDestroy()
    {
        UnsubscribeAllPopups();
        ReleaseResources();
    }


    // ============================================================
    // 区域与层级控制
    // ============================================================

    private void CacheAlphaTargets()
    {
        graphics.Clear();
        sprites.Clear();
        groups.Clear();

        if (regionsRoot != null)
        {
            regionsRoot.GetComponentsInChildren(true, graphics);
            regionsRoot.GetComponentsInChildren(true, sprites);
            regionsRoot.GetComponentsInChildren(true, groups);
        }

        // ⭐ 确保切割引导线 SpriteRenderer 浮在视频 Canvas (SortingOrder 61) 之上
        foreach (SpriteRenderer sprite in sprites)
        {
            if (sprite != null && sprite.sortingOrder <= 61)
            {
                sprite.sortingOrder = 70;
            }
        }

        EnsureSpriteSorting(areaA);
        EnsureSpriteSorting(areaB);
        EnsureSpriteSorting(areaC);
    }

    private void EnsureSpriteSorting(Collider2D col)
    {
        if (col == null) return;
        var sr = col.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sortingOrder <= 61)
        {
            sr.sortingOrder = 70;
        }
    }

    private void EnableOnly(RawImage active)
    {
        if (rawA != null) rawA.enabled = (active == rawA);
        if (rawB != null) rawB.enabled = (active == rawB);
        if (rawTrans != null) rawTrans.enabled = (active == rawTrans);
        if (rawPrep != null) rawPrep.enabled = (active == rawPrep);

        if (activeTrail != null)
        {
            activeTrail.gameObject.SetActive(CanDrawTrail());
        }

        if (!enableAlphaControl || regionsRoot == null) return;

        if (active == rawA)
        {
            ApplyRegionsAlpha(mainAlpha);
        }
        else if (active == rawB)
        {
            ApplyRegionsAlpha(failAlpha);
        }
        else
        {
            ApplyRegionsAlpha(idleAlpha);
        }
    }

    private void ApplyRegionsAlpha(float alpha)
    {
        currentRegionsAlpha = alpha;

        foreach (CanvasGroup g in groups)
        {
            if (g != null) g.alpha = alpha;
        }

        foreach (Graphic graphic in graphics)
        {
            if (graphic == null) continue;
            Color c = graphic.color;
            graphic.color = new Color(c.r, c.g, c.b, alpha);
        }

        foreach (SpriteRenderer sprite in sprites)
        {
            if (sprite == null) continue;
            Color c = sprite.color;
            sprite.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    private void SetRegionsActive(bool on)
    {
        regionsVisible = on;

        if (regionsRoot != null)
        {
            regionsRoot.SetActive(on);
            return;
        }

        if (areaA != null) areaA.gameObject.SetActive(on);
        if (areaB != null) areaB.gameObject.SetActive(on);
        if (areaC != null) areaC.gameObject.SetActive(on);
    }

    private bool CanDrawTrail()
    {
        return !enableAlphaControl || currentRegionsAlpha >= trailAlphaThreshold;
    }

    private bool IsInside(Vector2 point, Collider2D collider)
    {
        return collider != null && collider.OverlapPoint(point);
    }

    private IEnumerator EnableTrailAfterFrame(TrailRenderer trail)
    {
        yield return null;
        if (trail != null) trail.emitting = true;
    }

    private IEnumerator FadeOutAndDestroyTrail(TrailRenderer trail, float duration)
    {
        if (trail == null) yield break;

        float startTime = Time.time;
        float startWidth = trail.startWidth;
        Material trailMaterial = trail.material;
        Color startColor = trailMaterial.color;
        Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (Time.time - startTime < duration)
        {
            if (trail == null) yield break;
            float t = (Time.time - startTime) / duration;
            trail.startWidth = Mathf.Lerp(startWidth, 0f, t);
            trail.endWidth = trail.startWidth * 0.5f;
            trailMaterial.color = Color.Lerp(startColor, endColor, t);
            yield return null;
        }

        if (trail != null)
        {
            Destroy(trail.gameObject);
        }
    }
}