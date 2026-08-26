using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Collider2D))]
public class PatModule : MonoBehaviour
{
    [Header("Module State")]
    public bool active;
    public bool complete;

    private bool inTransition;
    private bool hasClickedOnce;
    private bool inputLocked;
    private bool timerActive;

    [Header("Gameplay")]
    public int requiredHits = 20;
    public float timeLimit = 3f;

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

        if (!active || inputLocked)
            return;

        // 鼠标点击
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
                    // Transition 第一次点击
                    if (inTransition && !hasClickedOnce)
                    {
                        hasClickedOnce = true;
                        StartCoroutine(SwitchToMain());
                    }
                    // Main 状态点击
                    else if (!inTransition)
                    {
                        RegisterHit();
                    }
                }
            }
        }

        // Fail Timer
        if (timerActive && !inTransition && currentHits > 0 && !complete)
        {
            timer += Time.deltaTime;
            if (timer > timeLimit)
            {
                StartCoroutine(PlayFailThenTransition());
            }
        }
    }

    public void Activate()
    {
        if (active && !complete)
        {
            Debug.LogWarning($"⚠️ [{name}] Activate() 被重复调用，已忽略。");
            return;
        }

        active = true;
        complete = false;
        inputLocked = false;
        currentHits = 0;
        timer = 0f;
        timerActive = false;
        hasClickedOnce = false;
        inTransition = true;

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

        inTransition = true;
        hasClickedOnce = false;

        Debug.Log($"▶️ [{name}] Transition 开始播放");

        // ⚠️ 修复：不能在 while 循环里根据 isPlaying == false 疯狂调用 PlayFull()，
        // 因为调用 Play 后 isPlaying 可能要等几帧才变 true，这会导致无限重置 time=0 卡死。
        // 我们利用 VideoController 的 onComplete 回调来实现安全的手动循环。
        
        System.Action playLoop = null;
        playLoop = () => {
            if (inTransition && !hasClickedOnce)
            {
                transCtrl?.PlayFull(playLoop);
            }
        };

        // 启动第一次循环
        playLoop();

        while (inTransition && !hasClickedOnce)
        {
            yield return null;
        }

        transCtrl?.StopAndReset();
        transitionRaw.enabled = false;
        Debug.Log($"⏹ [{name}] Transition 结束");
    }

    IEnumerator SwitchToMain()
    {
        inputLocked = true;
        inTransition = false;

        // ⚠️ 修复闪烁：这里不再提前调用 HideAll() 和 StopAndReset()
        // 让 Transition 画面保留在屏幕上，直到主视频首帧准备就绪。

        if (mainVideo != null && mainRaw != null && mainVideo.clip != null)
        {
            if (mainVideo.targetTexture != null) mainRaw.texture = mainVideo.targetTexture;
            mainRaw.color = Color.white;
            mainRaw.enabled = true;

            // 让主视频准备并播放一瞬间以建立首帧
            bool isReady = false;
            mainCtrl?.PrepareNow(() => isReady = true);
            
            while (!isReady) yield return null;

            mainVideo.time = 0.0;
            mainVideo.Play();

            // 等待 RT 写入第一帧
            yield return new WaitForSecondsRealtime(0.08f);

            mainCtrl?.Pause();
            Debug.Log($"🖼️ [{name}] Main 首帧已建立");
        }

        // 首帧建立完成，现在可以安全地关闭过渡视频了
        transCtrl?.StopAndReset();
        if (transitionRaw != null) transitionRaw.enabled = false;
        if (failRaw != null) failRaw.enabled = false;

        currentHits = 0;
        timer = 0f;
        timerActive = false;
        SetTargetAlpha(alphaActive);
        inputLocked = false;
    }

    void RegisterHit()
    {
        if (mainVideo == null || mainVideo.clip == null) return;

        currentHits++;
        if (currentHits == 1)
        {
            timer = 0f;
            timerActive = true;
        }

        StartCoroutine(ClickFeedback());

        float progress = Mathf.Clamp01((float)currentHits / Mathf.Max(1, requiredHits));
        double targetTime = progress * mainVideo.clip.length;

        // 【核心修复】：原逻辑是跳到 targetTime 然后播放 0.12 秒
        mainCtrl?.PlayChunk(targetTime, 0.12);

        Debug.Log($"👆 [{name}] Hit {currentHits}/{requiredHits} target={targetTime:F2}s");

        if (currentHits >= requiredHits)
        {
            Complete();
        }
    }

    IEnumerator ClickFeedback()
    {
        SetTargetAlpha(alphaClick);
        yield return new WaitForSeconds(0.2f);
        if (active && !inputLocked && !complete)
        {
            SetTargetAlpha(alphaActive);
        }
    }

    void Complete()
    {
        complete = true;
        active = false;
        timerActive = false;
        inputLocked = true;

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
        if (inputLocked) yield break;

        inputLocked = true;
        timerActive = false;
        timer = 0f;

        mainCtrl?.Pause();
        SetTargetAlpha(alphaInactive);
        
        // ⚠️ 修复闪烁：不要立刻隐藏 mainRaw

        // FAIL VIDEO
        if (failVideo != null && failRaw != null && failVideo.clip != null)
        {
            if (failVideo.targetTexture != null) failRaw.texture = failVideo.targetTexture;
            failRaw.color = Color.white;
            failRaw.enabled = true;

            Debug.Log($"💥 [{name}] Fail Video 开始");

            bool failDone = false;
            failCtrl?.PlayFull(() => failDone = true);
            
            // 等一小会，让失败视频渲染出首帧后，再隐藏主视频
            yield return new WaitForSecondsRealtime(0.1f);
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;

            while (!failDone) yield return null;

            failRaw.enabled = false;
        }
        else
        {
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;
        }

        hasClickedOnce = false;
        inTransition = true;
        inputLocked = false;

        StartCoroutine(PlayTransitionLoop());
    }

    public void Deactivate()
    {
        active = false;
        inTransition = false;
        hasClickedOnce = false;
        inputLocked = true;
        timerActive = false;
        timer = 0f;

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
}