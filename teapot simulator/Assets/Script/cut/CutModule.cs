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

    [Header("转场视频时长控制")]
    [Tooltip("转场视频播放时长（秒）。设置比如 3.5 秒。0 表示等原视频全部播完。")]
    public float transitionDuration = 3.5f;

    [Tooltip("允许玩家在播放转场视频期间点击鼠标提前跳过直接进入下一个 Cut")]
    public bool allowSkipTransitionOnClick = true;

    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;

    private VideoController ctrlA;
    private VideoController ctrlB;
    private VideoController ctrlTrans;
    private VideoController ctrlComplete;


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


    // ============================================================
    // 内部状态
    // ============================================================

    private bool inB = false;
    private bool hasReachedEnd = false;
    private float progress = 0f;

    private CutSequenceController controller;

    private float currentRegionsAlpha = 1f;

    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();


    // ============================================================
    // INITIALIZE（由 CutSequenceController 调用）
    // ============================================================

    public void Initialize(CutSequenceController ctrl)
    {
        controller = ctrl;

        if (debugLog) Debug.Log($"✂️ [{name}] CutModule 初始化启动");

        // 1. 缓存引导条图形并提升 SortingOrder 确保浮在全屏视频 Canvas 上
        CacheAlphaTargets();

        // 2. 绑定 VideoController
        ctrlA = VideoController.GetOrCreate(videoA);
        ctrlB = VideoController.GetOrCreate(videoB);
        ctrlTrans = VideoController.GetOrCreate(transition);
        ctrlComplete = VideoController.GetOrCreate(completeVideo);

        // 3. 初始隐藏 Complete 与转场
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

        // 4. 准备主视频并建立第 0 帧（仅准备当前立即可见的主视频，避免多视频并发抢占解码器）
        StartCoroutine(SetupMainVideoInitialFrame());
    }

    private IEnumerator SetupMainVideoInitialFrame()
    {
        EnableOnly(rawA);
        SetRegionsActive(true);
        ApplyRegionsAlpha(idleAlpha);

        if (videoA != null)
        {
            bool prepared = false;
            ctrlA?.PrepareNow(() => prepared = true);
            while (!prepared) yield return null;

            // 强制渲染一次首帧到 RenderTexture
            yield return StartCoroutine(RenderFirstFrame(videoA));
        }

        progress = 0f;
        inB = false;
        hasReachedEnd = false;
        currentState = ModuleState.Idle;

        if (debugLog) Debug.Log($"🎬 [{name}] Cut 主视频第 0 帧准备完毕，进入 Idle 等待切割");
    }

    private IEnumerator RenderFirstFrame(VideoPlayer vp)
    {
        if (vp == null) yield break;
        vp.time = 0;
        vp.Play();
        yield return new WaitForEndOfFrame();
        yield return new WaitForSecondsRealtime(0.06f);
        vp.Pause();
        vp.time = 0;
    }


    // ============================================================
    // UPDATE 状态机驱动
    // ============================================================

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

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }
        if (mainCamera == null) return;

        Vector2 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        switch (currentState)
        {
            case ModuleState.Idle:
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
                hasReachedEnd = true;
                inB = true;
                progress = 1f;

                // 划到末尾显示切断帧
                ScrubMainVideo(1f);

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
            ctrlA.ScrubTo(p);
        }
        else if (videoA != null && videoA.isPrepared)
        {
            videoA.Pause();
            videoA.time = videoA.length * Mathf.Clamp01(p);
            videoA.StepForward();
        }
    }


    // ============================================================
    // 失败流程（FAIL）
    // ============================================================

    private void TriggerFail()
    {
        currentState = ModuleState.Failed;
        if (debugLog) Debug.Log($"💥 [{name}] 切割失败，开始播放失败视频");
        StartCoroutine(FailRoutine());
    }

    private IEnumerator FailRoutine()
    {
        EnableOnly(rawB);

        if (videoB != null)
        {
            if (!videoB.isPrepared)
            {
                bool prepared = false;
                ctrlB?.PrepareNow(() => prepared = true);
                while (!prepared) yield return null;
            }

            // 从与切割进度对应的倒放位置起播
            double startTime = videoB.length * Mathf.Clamp01(1f - progress);
            videoB.isLooping = false;
            videoB.time = startTime;
            videoB.Play();

            yield return null;
            float maxFailDuration = Mathf.Max(0.5f, (float)(videoB.length - startTime) + 0.2f);
            float failTimer = 0f;
            while (videoB.isPlaying && failTimer < maxFailDuration)
            {
                failTimer += Time.deltaTime;
                yield return null;
            }
            videoB.Stop();
            videoB.time = 0;
        }

        // ⭐ 彻底重置并强制刷新 videoA 回到第 0 帧
        if (videoA != null)
        {
            yield return StartCoroutine(RenderFirstFrame(videoA));
        }

        // 返回 RawA
        EnableOnly(rawA);
        SetRegionsActive(true);
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

        // 3. TRANSITION 转场阶段
        currentState = ModuleState.Transition;
        EnableOnly(rawTrans);

        if (transition != null && (transition.clip != null || !string.IsNullOrEmpty(transition.url)))
        {
            float maxWait = transitionDuration > 0f ? transitionDuration : -1f;
            yield return StartCoroutine(PlayOneShotVideo(transition, maxWait, allowSkipTransitionOnClick));
        }

        // 4. 当前 Cut 彻底完成
        currentState = ModuleState.Done;

        if (activeTrail != null)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        // ⭐ 停止当前视频，不强制震荡 enabled 避免 Direct3D 解码器卡死
        ReleaseResources();

        if (debugLog) Debug.Log($"✅ [{name}] CutModule 全部流程结束，通知 Controller 启动下一个 Cut");

        // 5. 通知 CutSequenceController
        if (controller != null)
        {
            controller.OnModuleCompleted(this);
        }
        else
        {
            Debug.LogWarning($"⚠️ [{name}] CutSequenceController 为 null！");
        }
    }

    /// <summary>
    /// 单次精确播放视频：强制关闭循环，结合 loopPointReached、时长上限与可跳过防护
    /// </summary>
    private IEnumerator PlayOneShotVideo(VideoPlayer vp, float maxDuration = -1f, bool allowSkip = false)
    {
        if (vp == null) yield break;

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

        vp.time = 0;
        vp.Play();

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
        vp.Stop();
    }

    /// <summary>
    /// 停止所有视频并重置
    /// </summary>
    public void ReleaseResources()
    {
        ctrlA?.StopAndReset();
        ctrlB?.StopAndReset();
        ctrlTrans?.StopAndReset();
        ctrlComplete?.StopAndReset();
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