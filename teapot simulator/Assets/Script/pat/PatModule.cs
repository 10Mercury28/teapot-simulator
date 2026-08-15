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

    // 防止连续点击留下多个 Pause coroutine
    private Coroutine pauseCoroutine;

    void Start()
    {
        controller = GetComponentInParent<PatSequenceController>();

        HideAll();

        sprites.Clear();
        GetComponentsInChildren(true, sprites);

        targetAlpha = alphaInactive;
        ApplyAlphaInstant(alphaInactive);

        // -----------------------------
        // VideoPlayer 基础设置
        // -----------------------------
        ConfigureVideoPlayer(mainVideo);
        ConfigureVideoPlayer(failVideo);
        ConfigureVideoPlayer(transitionVideo);
    }

    void ConfigureVideoPlayer(VideoPlayer vp)
    {
        if (vp == null)
            return;

        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.skipOnDrop = true;
        vp.waitForFirstFrame = true;
    }

    void Update()
    {
        // ===============================================
        // Alpha 必须始终更新
        // 不能放到 active 检查之后
        // ===============================================
        LerpAlphaToTarget();

        if (!active || inputLocked)
            return;

        // ===============================================
        // 鼠标点击
        // ===============================================
        if (Input.GetMouseButtonDown(0))
        {
            Vector3 screenPos = Input.mousePosition;

            if (Camera.main != null)
            {
                screenPos.z = Mathf.Abs(Camera.main.transform.position.z);

                Vector3 mouse =
                    Camera.main.ScreenToWorldPoint(screenPos);

                Vector2 pos =
                    new Vector2(mouse.x, mouse.y);

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

        // ===============================================
        // Fail Timer
        // ===============================================
        if (
            timerActive &&
            !inTransition &&
            currentHits > 0 &&
            !complete
        )
        {
            timer += Time.deltaTime;

            if (timer > timeLimit)
            {
                StartCoroutine(PlayFailThenTransition());
            }
        }
    }

    // ==========================================================
    // ACTIVATE
    // ==========================================================

    public void Activate()
    {
        // 防止同一个 module 被重复 Activate
        if (active && !complete)
        {
            Debug.LogWarning(
                $"⚠️ [{name}] Activate() 被重复调用，已忽略。"
            );

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

        if (col != null)
            col.enabled = true;

        // 清掉以前的 pause
        if (pauseCoroutine != null)
        {
            StopCoroutine(pauseCoroutine);
            pauseCoroutine = null;
        }

        HideAll();

        SetTargetAlpha(alphaInactive);

        // ===============================================
        // 玩家观看 Transition 时，
        // 提前 Prepare Main / Fail
        // ===============================================

        if (
            mainVideo != null &&
            mainVideo.clip != null &&
            !mainVideo.isPrepared
        )
        {
            mainVideo.Prepare();
        }

        if (
            failVideo != null &&
            failVideo.clip != null &&
            !failVideo.isPrepared
        )
        {
            failVideo.Prepare();
        }

        StartCoroutine(PlayTransitionLoop());

        Debug.Log($"🎯 [{name}] Activated");
    }

    // ==========================================================
    // TRANSITION
    // ==========================================================

    IEnumerator PlayTransitionLoop()
    {
        if (
            transitionVideo == null ||
            transitionRaw == null ||
            transitionVideo.clip == null
        )
        {
            Debug.LogWarning(
                $"⚠️ [{name}] Transition Video / RawImage / Clip 缺失"
            );

            yield break;
        }

        transitionRaw.enabled = true;
        transitionRaw.color = Color.white;

        if (transitionVideo.targetTexture != null)
        {
            transitionRaw.texture =
                transitionVideo.targetTexture;
        }

        transitionVideo.Stop();

        transitionVideo.isLooping = false;

        transitionVideo.playbackSpeed =
            Mathf.Max(0.1f, transitionSpeed);

        // ===============================================
        // 真正等待 Prepare
        // ===============================================

        if (!transitionVideo.isPrepared)
        {
            transitionVideo.Prepare();

            while (!transitionVideo.isPrepared)
                yield return null;
        }

        inTransition = true;
        hasClickedOnce = false;

        transitionVideo.time = 0.0;
        transitionVideo.Play();

        Debug.Log(
            $"▶️ [{name}] Transition 开始播放"
        );

        while (inTransition && !hasClickedOnce)
        {
            // 播完后重新播放
            if (
                transitionVideo.isPrepared &&
                !transitionVideo.isPlaying
            )
            {
                transitionVideo.time = 0.0;
                transitionVideo.Play();
            }

            yield return null;
        }

        transitionVideo.Stop();

        transitionRaw.enabled = false;

        Debug.Log(
            $"⏹ [{name}] Transition 结束"
        );
    }

    // ==========================================================
    // TRANSITION -> MAIN
    // ==========================================================

    IEnumerator SwitchToMain()
    {
        inputLocked = true;
        inTransition = false;

        if (transitionVideo != null)
            transitionVideo.Stop();

        HideAll();

        if (
            mainVideo != null &&
            mainRaw != null &&
            mainVideo.clip != null
        )
        {
            // ===============================================
            // 强制重新确认 RT 绑定
            // ===============================================

            if (mainVideo.targetTexture != null)
            {
                mainRaw.texture =
                    mainVideo.targetTexture;
            }

            mainRaw.color = Color.white;
            mainRaw.enabled = true;

            // ===============================================
            // 真正等待 Main Prepare 完成
            // ===============================================

            if (!mainVideo.isPrepared)
            {
                Debug.Log(
                    $"⏳ [{name}] 等待 MainVideo Prepare..."
                );

                mainVideo.Prepare();

                while (!mainVideo.isPrepared)
                    yield return null;
            }

            Debug.Log(
                $"✅ [{name}] MainVideo Prepared"
            );

            mainVideo.time = 0.0;

            mainVideo.Play();

            // ===============================================
            // 等待它真的开始播放
            // ===============================================

            float startTimeout = 1.0f;

            while (
                !mainVideo.isPlaying &&
                startTimeout > 0f
            )
            {
                startTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            // ===============================================
            // 原来是 0.02 秒
            // 太短，RenderTexture 很可能还没写入第一帧
            // ===============================================

            yield return
                new WaitForSecondsRealtime(0.08f);

            mainVideo.Pause();

            Debug.Log(
                $"🖼️ [{name}] Main 首帧已建立 | " +
                $"frame={mainVideo.frame} | " +
                $"RT={mainVideo.targetTexture?.name}"
            );
        }

        currentHits = 0;

        timer = 0f;
        timerActive = false;

        SetTargetAlpha(alphaActive);

        inputLocked = false;
    }

    // ==========================================================
    // HIT
    // ==========================================================

    void RegisterHit()
    {
        if (
            mainVideo == null ||
            mainVideo.clip == null
        )
            return;

        currentHits++;

        if (currentHits == 1)
        {
            timer = 0f;
            timerActive = true;
        }

        StartCoroutine(ClickFeedback());

        float progress =
            Mathf.Clamp01(
                (float)currentHits /
                Mathf.Max(1, requiredHits)
            );

        double targetTime =
            progress * mainVideo.clip.length;

        // SwitchToMain 已经保证视频 Prepared。
        // 如果这里意外丢失 Prepared 状态，不直接硬 Play。
        if (!mainVideo.isPrepared)
        {
            Debug.LogWarning(
                $"⚠️ [{name}] MainVideo unexpectedly not prepared."
            );

            mainVideo.Prepare();
        }
        else
        {
            mainVideo.time = targetTime;

            mainVideo.Play();

            // ===============================================
            // 核心修复：
            // 取消上一次点击遗留下来的 Pause coroutine
            // ===============================================

            if (pauseCoroutine != null)
                StopCoroutine(pauseCoroutine);

            pauseCoroutine =
                StartCoroutine(
                    PauseVideoAfter(mainVideo, 0.12f)
                );
        }

        Debug.Log(
            $"👆 [{name}] Hit {currentHits}/{requiredHits} " +
            $"target={targetTime:F2}s"
        );

        if (currentHits >= requiredHits)
        {
            Complete();
        }
    }

    IEnumerator ClickFeedback()
    {
        SetTargetAlpha(alphaClick);

        yield return
            new WaitForSeconds(0.2f);

        if (
            active &&
            !inputLocked &&
            !complete
        )
        {
            SetTargetAlpha(alphaActive);
        }
    }

    IEnumerator PauseVideoAfter(
        VideoPlayer vp,
        float delay
    )
    {
        yield return
            new WaitForSecondsRealtime(delay);

        if (vp != null)
            vp.Pause();

        pauseCoroutine = null;
    }

    // ==========================================================
    // COMPLETE
    // ==========================================================

    void Complete()
    {
        complete = true;
        active = false;
        timerActive = false;
        inputLocked = true;

        if (pauseCoroutine != null)
        {
            StopCoroutine(pauseCoroutine);
            pauseCoroutine = null;
        }

        if (mainVideo != null)
            mainVideo.Pause();

        HideAll();

        // SpriteRenderer 直接隐藏
        SetTargetAlpha(alphaComplete);
        ApplyAlphaInstant(alphaComplete);

        Collider2D col = GetComponent<Collider2D>();

        if (col != null)
            col.enabled = false;

        Debug.Log(
            $"✅ [{name}] Module Complete"
        );

        // ===============================================
        // 注意：
        // 不再 gameObject.SetActive(false)
        // ===============================================

        controller?.OnModuleCompleted(this);
    }

    // ==========================================================
    // FAIL
    // ==========================================================

    IEnumerator PlayFailThenTransition()
    {
        // 防止 timer 每帧重复启动 fail coroutine
        if (inputLocked)
            yield break;

        inputLocked = true;
        timerActive = false;

        timer = 0f;

        if (pauseCoroutine != null)
        {
            StopCoroutine(pauseCoroutine);
            pauseCoroutine = null;
        }

        if (mainVideo != null)
            mainVideo.Pause();

        SetTargetAlpha(alphaInactive);

        if (mainRaw != null)
            mainRaw.enabled = false;

        // ===============================================
        // FAIL VIDEO
        // ===============================================

        if (
            failVideo != null &&
            failRaw != null &&
            failVideo.clip != null
        )
        {
            if (failVideo.targetTexture != null)
            {
                failRaw.texture =
                    failVideo.targetTexture;
            }

            failRaw.color = Color.white;
            failRaw.enabled = true;

            if (!failVideo.isPrepared)
            {
                failVideo.Prepare();

                while (!failVideo.isPrepared)
                    yield return null;
            }

            failVideo.time = 0.0;
            failVideo.Play();

            float startTimeout = 1.0f;

            while (
                !failVideo.isPlaying &&
                startTimeout > 0f
            )
            {
                startTimeout -=
                    Time.unscaledDeltaTime;

                yield return null;
            }

            Debug.Log(
                $"💥 [{name}] Fail Video 开始"
            );

            while (failVideo.isPlaying)
                yield return null;

            failVideo.Pause();

            failRaw.enabled = false;
        }

        // ===============================================
        // FAIL -> TRANSITION
        // ===============================================

        hasClickedOnce = false;
        inTransition = true;
        inputLocked = false;

        StartCoroutine(
            PlayTransitionLoop()
        );
    }

    // ==========================================================
    // DEACTIVATE
    // ==========================================================

    public void Deactivate()
    {
        active = false;
        inTransition = false;

        hasClickedOnce = false;
        inputLocked = true;

        timerActive = false;
        timer = 0f;

        // 停掉这个 module 以前遗留的 coroutine
        StopAllCoroutines();

        pauseCoroutine = null;

        if (transitionVideo != null)
            transitionVideo.Stop();

        if (mainVideo != null)
            mainVideo.Pause();

        if (failVideo != null)
            failVideo.Stop();

        HideAll();

        Collider2D col =
            GetComponent<Collider2D>();

        if (col != null)
            col.enabled = false;

        // 已经完成的 module 保持透明
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

    // ==========================================================
    // DISPLAY
    // ==========================================================

    void HideAll()
    {
        if (transitionRaw != null)
            transitionRaw.enabled = false;

        if (mainRaw != null)
            mainRaw.enabled = false;

        if (failRaw != null)
            failRaw.enabled = false;
    }

    void ApplyAlphaInstant(float a)
    {
        foreach (var sr in sprites)
        {
            if (sr == null)
                continue;

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
            if (sr == null)
                continue;

            Color c = sr.color;

            c.a = Mathf.Lerp(
                c.a,
                targetAlpha,
                Time.deltaTime * alphaLerpSpeed
            );

            sr.color = c;
        }
    }
}