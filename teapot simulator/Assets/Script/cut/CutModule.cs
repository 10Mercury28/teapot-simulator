using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

public class CutModule : MonoBehaviour
{
    // ============================================================
    // CUT 区域
    // ============================================================

    [Header("区域（必须挂 Collider2D）")]
    public Collider2D areaA;
    public Collider2D areaB;
    public Collider2D areaC;

    [Header("区域可视根")]
    [Tooltip("把 A/B/C 条或图形放在这里。成功后会隐藏。")]
    public GameObject regionsRoot;


    // ============================================================
    // CUT 视频
    // ============================================================

    [Header("Cut 视频组件")]
    public VideoPlayer videoA;          // 主视频，可 scrub
    public VideoPlayer videoB;          // Fail 视频
    public VideoPlayer transition;      // Transition 视频

    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;


    // ============================================================
    // COMPLETE
    // ============================================================

    [Header("Complete 提示")]
    [Tooltip("成功切完之后显示的 Complete GameObject / Panel。")]
    public GameObject completeRoot;

    [Tooltip("如果 Complete 本身是视频，把它的 VideoPlayer 拖这里。不是视频可以留空。")]
    public VideoPlayer completeVideo;

    [Tooltip("如果 Complete 不是视频，显示多少秒以后进入 Transition。")]
    public float completeDisplayDuration = 0.8f;


    // ============================================================
    // TRAIL
    // ============================================================

    [Header("轨迹效果")]
    public TrailRenderer trailPrefab;

    private TrailRenderer activeTrail;


    // ============================================================
    // ALPHA
    // ============================================================

    [Header("透明度控制")]
    [Range(0f, 1f)]
    public float mainAlpha = 0.8f;

    [Range(0f, 1f)]
    public float failAlpha = 0.3f;

    [Range(0f, 1f)]
    public float idleAlpha = 1.0f;

    public bool enableAlphaControl = true;


    [Header("Trail 显示阈值")]
    [Range(0f, 1f)]
    public float trailAlphaThreshold = 0.75f;


    // ============================================================
    // DEBUG
    // ============================================================

    [Header("调试")]
    public Camera mainCamera;
    public bool debugLog = true;


    [Header("状态（运行时观察）")]
    public bool complete = false;


    // ============================================================
    // INTERNAL STATE
    // ============================================================

    private bool preparedA = false;
    private bool preparedB = false;
    private bool preparedT = false;

    private bool cutting = false;
    private bool failed = false;
    private bool inB = false;

    // 防止成功流程重复触发
    private bool successSequenceRunning = false;

    private float progress = 0f;

    private CutSequenceController controller;


    // ============================================================
    // ALPHA CACHE
    // ============================================================

    private float currentRegionsAlpha = 1f;

    private bool CanDrawTrail()
    {
        return !enableAlphaControl ||
               currentRegionsAlpha >= trailAlphaThreshold;
    }

    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();


    // ============================================================
    // INITIALIZE
    // ============================================================

    public void Initialize(CutSequenceController ctrl)
    {
        Debug.Log("🔥🔥🔥 NEW CUT MODULE 2026 LOADED 🔥🔥🔥"); 
        
        controller = ctrl;

        CacheAlphaTargets();

        SetupVideo(videoA, () => preparedA = true);
        SetupVideo(videoB, () => preparedB = true);
        SetupVideo(transition, () => preparedT = true);

        // Complete 绝对不能一开始显示
        if (completeRoot != null)
        {
            completeRoot.SetActive(false);
        }

        if (completeVideo != null)
        {
            completeVideo.playOnAwake = false;
            completeVideo.Stop();
        }

        ResetState();

        if (debugLog)
        {
            Debug.Log(
                $"🔍 [{name}] ▶️ Initialized by Controller"
            );
        }
    }


    // ============================================================
    // VIDEO SETUP
    // ============================================================

    private void SetupVideo(VideoPlayer vp, System.Action onReady)
    {
        if (vp == null)
            return;

        vp.playOnAwake = false;

        vp.Pause();

        if (vp.isPrepared)
        {
            onReady?.Invoke();
            return;
        }

        vp.Prepare();

        // 不依赖 callback 累积。
        StartCoroutine(WaitForVideoPrepared(vp, onReady));
    }


    private IEnumerator WaitForVideoPrepared(
        VideoPlayer vp,
        System.Action onReady
    )
    {
        if (vp == null)
            yield break;

        yield return new WaitUntil(
            () => vp == null || vp.isPrepared
        );

        if (vp != null)
        {
            onReady?.Invoke();
        }
    }


    // ============================================================
    // RESET
    // ============================================================

    private void ResetState()
    {
        progress = 0f;

        failed = false;
        inB = false;
        cutting = false;

        complete = false;

        successSequenceRunning = false;

        EnableOnly(rawA);

        // Complete 隐藏
        if (completeRoot != null)
        {
            completeRoot.SetActive(false);
        }

        // 视频 A 回第一帧
        if (videoA != null)
        {
            videoA.Pause();
            videoA.time = 0;

            if (videoA.isPrepared)
            {
                videoA.StepForward();
            }
        }

        // ABC 恢复
        SetRegionsActive(true);

        ApplyRegionsAlpha(idleAlpha);

        // 清掉上一次可能残留的 Trail
        if (activeTrail != null)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        if (debugLog)
        {
            LogStatus("🔄 State Reset");
        }
    }


    // ============================================================
    // CACHE ALPHA TARGETS
    // ============================================================

    private void CacheAlphaTargets()
    {
        graphics.Clear();
        sprites.Clear();
        groups.Clear();

        if (regionsRoot == null)
            return;

        regionsRoot.GetComponentsInChildren(
            true,
            graphics
        );

        regionsRoot.GetComponentsInChildren(
            true,
            sprites
        );

        regionsRoot.GetComponentsInChildren(
            true,
            groups
        );
    }


    // ============================================================
    // RAW IMAGE SWITCH
    // ============================================================

    private void EnableOnly(RawImage active)
    {
        if (rawA != null)
            rawA.enabled = active == rawA;

        if (rawB != null)
            rawB.enabled = active == rawB;

        if (rawTrans != null)
            rawTrans.enabled = active == rawTrans;


        if (activeTrail != null)
        {
            activeTrail.gameObject.SetActive(
                CanDrawTrail()
            );
        }


        if (!enableAlphaControl ||
            regionsRoot == null)
        {
            return;
        }


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


    // ============================================================
    // ALPHA
    // ============================================================

    private void ApplyRegionsAlpha(float alpha)
    {
        currentRegionsAlpha = alpha;

        foreach (CanvasGroup g in groups)
        {
            if (g != null)
            {
                g.alpha = alpha;
            }
        }


        foreach (Graphic graphic in graphics)
        {
            if (graphic == null)
                continue;

            Color c = graphic.color;

            graphic.color =
                new Color(
                    c.r,
                    c.g,
                    c.b,
                    alpha
                );
        }


        foreach (SpriteRenderer sprite in sprites)
        {
            if (sprite == null)
                continue;

            Color c = sprite.color;

            sprite.color =
                new Color(
                    c.r,
                    c.g,
                    c.b,
                    alpha
                );
        }
    }


    // ============================================================
    // REGION ACTIVE
    // ============================================================

    private void SetRegionsActive(bool on)
    {
        if (regionsRoot != null)
        {
            regionsRoot.SetActive(on);
            return;
        }

        if (areaA != null)
            areaA.gameObject.SetActive(on);

        if (areaB != null)
            areaB.gameObject.SetActive(on);

        if (areaC != null)
            areaC.gameObject.SetActive(on);
    }


    // ============================================================
    // DEBUG
    // ============================================================

    private void LogStatus(string prefix)
    {
        if (!debugLog)
            return;

        Debug.Log(
            prefix + "\n" +
            $"    RAW => A[{(rawA != null && rawA.enabled)}], " +
            $"B[{(rawB != null && rawB.enabled)}], " +
            $"T[{(rawTrans != null && rawTrans.enabled)}]\n" +
            $"    STATE => cutting[{cutting}], " +
            $"failed[{failed}], " +
            $"inB[{inB}], " +
            $"successRunning[{successSequenceRunning}], " +
            $"complete[{complete}]"
        );
    }


    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        if (complete ||
            successSequenceRunning)
        {
            return;
        }


        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }


        if (mainCamera == null)
            return;


        Vector2 mouseWorld =
            mainCamera.ScreenToWorldPoint(
                Input.mousePosition
            );


        // --------------------------------------------------------
        // Mouse Down：必须从 A 开始
        // --------------------------------------------------------

        if (Input.GetMouseButtonDown(0) &&
            IsInside(mouseWorld, areaA))
        {
            if (!CanDrawTrail())
            {
                if (debugLog)
                {
                    Debug.Log(
                        "✋ Trail blocked: regions alpha below threshold."
                    );
                }

                return;
            }


            cutting = true;
            failed = false;
            inB = false;

            progress = 0f;


            // 创建 Trail
            if (trailPrefab != null &&
                activeTrail == null)
            {
                activeTrail =
                    Instantiate(
                        trailPrefab,
                        mouseWorld,
                        Quaternion.identity
                    );

                activeTrail.emitting = false;

                activeTrail.Clear();

                activeTrail.time = 999f;

                activeTrail.autodestruct = false;

                StartCoroutine(
                    EnableTrailAfterFrame(
                        activeTrail
                    )
                );

                activeTrail.gameObject.SetActive(
                    CanDrawTrail()
                );
            }


            if (rawA != null &&
                rawA.enabled &&
                enableAlphaControl)
            {
                ApplyRegionsAlpha(mainAlpha);
            }


            if (debugLog)
            {
                LogStatus("✂️ Cut started in A");
            }
        }


        // --------------------------------------------------------
        // Dragging
        // --------------------------------------------------------

        if (cutting &&
            !failed &&
            Input.GetMouseButton(0))
        {
            // Trail
            if (activeTrail != null)
            {
                bool allow = CanDrawTrail();

                if (!allow)
                {
                    activeTrail.emitting = false;

                    activeTrail.gameObject.SetActive(
                        false
                    );
                }
                else
                {
                    if (!activeTrail.gameObject.activeSelf)
                    {
                        activeTrail.gameObject.SetActive(
                            true
                        );
                    }

                    if (!activeTrail.emitting)
                    {
                        activeTrail.emitting = true;
                    }

                    activeTrail.transform.position =
                        new Vector3(
                            mouseWorld.x,
                            mouseWorld.y,
                            activeTrail.transform.position.z
                        );
                }
            }


            // 进入 B
            if (IsInside(mouseWorld, areaB))
            {
                inB = true;

                UpdateVideoA(mouseWorld);

                if (rawA != null &&
                    rawA.enabled &&
                    enableAlphaControl)
                {
                    ApplyRegionsAlpha(mainAlpha);
                }
            }

            // 曾经进入 B，现在离开 B
            // => Fail
            else if (inB)
            {
                failed = true;

                StartCoroutine(
                    PlayVideoB()
                );
            }
        }


        // --------------------------------------------------------
        // Mouse Up
        // --------------------------------------------------------

        if (Input.GetMouseButtonUp(0))
        {
            // Trail 淡出
            if (activeTrail != null)
            {
                TrailRenderer trailToFade =
                    activeTrail;

                activeTrail = null;

                StartCoroutine(
                    FadeOutAndDestroyTrail(
                        trailToFade,
                        0.5f
                    )
                );
            }


            if (cutting &&
                !failed)
            {
                // ------------------------------------------------
                // SUCCESS
                // A → B → C
                // ------------------------------------------------

                if (inB &&
                    IsInside(mouseWorld, areaC))
                {
                    successSequenceRunning = true;

                    StartCoroutine(
                        PlaySuccessSequence()
                    );
                }

                // ------------------------------------------------
                // FAIL
                // ------------------------------------------------

                else
                {
                    failed = true;

                    StartCoroutine(
                        PlayVideoB()
                    );
                }
            }


            cutting = false;
        }
    }


    // ============================================================
    // ENABLE TRAIL AFTER ONE FRAME
    // ============================================================

    private IEnumerator EnableTrailAfterFrame(
        TrailRenderer trail
    )
    {
        yield return null;

        if (trail != null)
        {
            trail.emitting = true;
        }
    }


    // ============================================================
    // VIDEO A SCRUB
    // ============================================================

    private void UpdateVideoA(
        Vector2 mouseWorld
    )
    {
        if (!preparedA ||
            videoA == null ||
            areaB == null)
        {
            return;
        }


        float width =
            areaB.bounds.size.x;

        float startX =
            areaB.bounds.min.x;


        float currentX =
            Mathf.Clamp(
                mouseWorld.x,
                startX,
                startX + width
            );


        float newProgress =
            Mathf.InverseLerp(
                startX,
                startX + width,
                currentX
            );


        // 只能向前
        progress =
            Mathf.Max(
                progress,
                newProgress
            );


        double videoTime =
            videoA.length *
            progress;


        videoA.Pause();

        videoA.time =
            videoTime;

        videoA.StepForward();
    }


    // ============================================================
    // FAIL VIDEO
    // ============================================================

    private IEnumerator PlayVideoB()
    {
        if (videoB == null)
        {
            failed = false;
            inB = false;
            cutting = false;
            yield break;
        }


        EnableOnly(rawB);


        if (!videoB.isPrepared)
        {
            preparedB = false;

            videoB.Prepare();

            yield return new WaitUntil(
                () => videoB.isPrepared
            );

            preparedB = true;
        }


        double startTime =
            videoB.length *
            Mathf.Clamp01(
                1f - progress
            );


        videoB.time =
            startTime;

        videoB.Play();


        // 等至少一帧，否则有些 VideoPlayer
        // 会在 Play() 后立刻报告 isPlaying=false
        yield return null;


        yield return new WaitUntil(
            () => !videoB.isPlaying
        );


        videoB.Stop();

        videoB.time = 0;


        // 返回 A
        EnableOnly(rawA);


        if (videoA != null)
        {
            videoA.Pause();

            videoA.time = 0;

            if (videoA.isPrepared)
            {
                videoA.StepForward();
            }
        }


        SetRegionsActive(true);


        if (activeTrail != null)
        {
            Destroy(
                activeTrail.gameObject
            );

            activeTrail = null;
        }


        failed = false;
        inB = false;
        cutting = false;

        progress = 0f;


        if (debugLog)
        {
            LogStatus(
                "🔁 Fail handled → back to A"
            );
        }
    }


    // ============================================================
    // SUCCESS
    //
    // 正确顺序：
    //
    // Cut 成功
    // ↓
    // Complete
    // ↓
    // Transition
    // ↓
    // complete = true
    // ↓
    // Controller
    // ↓
    // Cut 2
    //
    // ============================================================

    private IEnumerator PlaySuccessSequence()
    {
        successSequenceRunning = true;

        cutting = false;
        failed = false;


        // --------------------------------------------------------
        // 1. 成功以后立即关闭 ABC
        // --------------------------------------------------------

        SetRegionsActive(false);


        // --------------------------------------------------------
        // 2. COMPLETE
        // --------------------------------------------------------

        if (debugLog)
        {
            Debug.Log(
                $"🎉 [{name}] Cut 成功 → 显示 Complete"
            );
        }


        if (completeRoot != null)
        {
            completeRoot.SetActive(true);
        }


        // 如果 Complete 是视频
        if (completeVideo != null)
        {
            completeVideo.playOnAwake = false;


            if (!completeVideo.isPrepared)
            {
                completeVideo.Prepare();

                yield return new WaitUntil(
                    () => completeVideo.isPrepared
                );
            }


            completeVideo.Stop();

            completeVideo.time = 0;

            completeVideo.Play();


            yield return null;


            yield return new WaitUntil(
                () => !completeVideo.isPlaying
            );


            completeVideo.Stop();
        }

        // 如果 Complete 只是文字 / Panel
        else
        {
            yield return new WaitForSeconds(
                completeDisplayDuration
            );
        }


        if (completeRoot != null)
        {
            completeRoot.SetActive(false);
        }


        if (debugLog)
        {
            Debug.Log(
                $"🎬 [{name}] Complete 结束 → 开始 Transition"
            );
        }


        // --------------------------------------------------------
        // 3. TRANSITION
        // --------------------------------------------------------

        EnableOnly(rawTrans);


        if (transition != null)
        {
            if (!transition.isPrepared)
            {
                preparedT = false;

                transition.Prepare();

                yield return new WaitUntil(
                    () => transition.isPrepared
                );

                preparedT = true;
            }


            transition.Stop();

            transition.time = 0;

            transition.Play();


            yield return null;


            yield return new WaitUntil(
                () => !transition.isPlaying
            );


            transition.Stop();
        }


        // --------------------------------------------------------
        // 4. 当前 Cut 完整结束
        // --------------------------------------------------------

        complete = true;

        successSequenceRunning = false;


        // Trail 清理
        if (activeTrail != null)
        {
            Destroy(
                activeTrail.gameObject
            );

            activeTrail = null;
        }


        if (debugLog)
        {
            LogStatus(
                "✅ Complete + Transition done → complete"
            );
        }


        // --------------------------------------------------------
        // 5. 最后才通知 Controller
        //
        // 注意：
        // 这里以前会先预加载 Cut2，
        // Controller 又 Initialize Cut2 一次。
        //
        // 现在完全删除预加载。
        // Cut2 只由 Controller 启动一次。
        // --------------------------------------------------------

        if (controller != null)
        {
            controller.OnModuleCompleted(this);
        }
        else
        {
            Debug.LogWarning(
                $"⚠️ [{name}] CutSequenceController 为 null！"
            );
        }
    }


    // ============================================================
    // COLLIDER CHECK
    // ============================================================

    private bool IsInside(
        Vector2 point,
        Collider2D collider
    )
    {
        return collider != null &&
               collider.OverlapPoint(point);
    }


    // ============================================================
    // TRAIL FADE
    // ============================================================

    private IEnumerator FadeOutAndDestroyTrail(
        TrailRenderer trail,
        float duration
    )
    {
        if (trail == null)
            yield break;


        float startTime =
            Time.time;

        float startWidth =
            trail.startWidth;


        Material trailMaterial =
            trail.material;

        Color startColor =
            trailMaterial.color;


        Color endColor =
            new Color(
                startColor.r,
                startColor.g,
                startColor.b,
                0f
            );


        while (
            Time.time - startTime <
            duration
        )
        {
            if (trail == null)
                yield break;


            float t =
                (Time.time - startTime) /
                duration;


            trail.startWidth =
                Mathf.Lerp(
                    startWidth,
                    0f,
                    t
                );


            trail.endWidth =
                trail.startWidth *
                0.5f;


            trailMaterial.color =
                Color.Lerp(
                    startColor,
                    endColor,
                    t
                );


            yield return null;
        }


        if (trail != null)
        {
            Destroy(
                trail.gameObject
            );
        }
    }
}