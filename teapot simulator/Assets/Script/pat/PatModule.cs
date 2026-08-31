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

    // Snapshot variables
    private Texture2D snapshotTex;
    private RawImage snapshotRaw;

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
                if (clickedOnModule)
                {
                    currentState = PatState.SwitchingToMain;
                    StartCoroutine(SwitchToMain());
                }
                break;

            case PatState.MainActive:
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
        // 瞬间截图当前正在播放的 Transition，定格显示在最上层
        TakeSnapshotAndShow(transitionRaw, transitionVideo);

        // 现在可以立刻无缝关掉之前的视频了，屏幕上已经是我们的定格画了
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

            mainVideo.time = 0.0;
            mainVideo.Play();

            // 稍微等待新视频输出第一帧画面（此时底下的画面会被新视频盖上）
            yield return new WaitForSecondsRealtime(0.08f);

            mainCtrl?.Pause();
            Debug.Log($"🖼️ [{name}] Main 首帧已建立");
        }

        // 新视频就绪，撤掉用来遮丑的定格画面！
        HideSnapshot();

        currentHits = 0;
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
        
        // 瞬间截图当前的主视频画面定格
        TakeSnapshotAndShow(mainRaw, mainVideo);

        // FAIL VIDEO
        if (failVideo != null && failRaw != null && failVideo.clip != null)
        {
            if (failVideo.targetTexture != null) failRaw.texture = failVideo.targetTexture;
            failRaw.color = Color.white;
            failRaw.enabled = true;

            Debug.Log($"💥 [{name}] Fail Video 开始");

            bool failDone = false;
            failCtrl?.PlayFull(() => failDone = true);
            
            // 稍等 Fail 出画面，然后撤掉主视频和定格图
            yield return new WaitForSecondsRealtime(0.08f);
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;
            HideSnapshot();

            while (!failDone) yield return null;

            // Fail结束，切回 Transition
            TakeSnapshotAndShow(failRaw, failVideo);
            failRaw.enabled = false;
        }
        else
        {
            if (mainRaw != null) mainRaw.enabled = false;
            if (transitionRaw != null) transitionRaw.enabled = false;
        }

        currentState = PatState.TransitionLoop;
        StartCoroutine(PlayTransitionLoop());
        
        // 给 Transition 视频一点时间建立画面
        yield return new WaitForSecondsRealtime(0.08f);
        HideSnapshot();
    }

    public void Deactivate()
    {
        currentState = PatState.Inactive;
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

    // ==============================================
    // Snapshot 黑科技：掩盖视频加载缝隙
    // ==============================================
    private void TakeSnapshotAndShow(RawImage sourceRaw, VideoPlayer vp)
    {
        if (sourceRaw == null || vp == null || vp.targetTexture == null) return;
        
        if (snapshotRaw == null)
        {
            GameObject go = new GameObject("Pat_Snapshot_Overlay");
            go.transform.SetParent(sourceRaw.transform.parent, false);
            go.transform.SetAsLastSibling();
            snapshotRaw = go.AddComponent<RawImage>();
            
            RectTransform srt = go.GetComponent<RectTransform>();
            RectTransform rt = sourceRaw.GetComponent<RectTransform>();
            srt.anchorMin = rt.anchorMin;
            srt.anchorMax = rt.anchorMax;
            srt.pivot = rt.pivot;
            srt.sizeDelta = rt.sizeDelta;
            srt.anchoredPosition = rt.anchoredPosition;
        }
        else
        {
            snapshotRaw.transform.SetAsLastSibling();
        }

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

        snapshotRaw.texture = snapshotTex;
        snapshotRaw.color = Color.white;
        snapshotRaw.enabled = true;
    }

    private void HideSnapshot()
    {
        if (snapshotRaw != null) snapshotRaw.enabled = false;
    }
}