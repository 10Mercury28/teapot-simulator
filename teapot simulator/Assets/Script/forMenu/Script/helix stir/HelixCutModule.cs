using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Canvas))]
public class HelixCutModule : SequenceModuleBase
{
    [Header("区域（必须挂 Collider2D）")]
    public Collider2D areaA;
    public Collider2D areaB;

    [Header("区域根节点（透明度控制）")]
    public GameObject regionsRoot;

    [Header("视频组件")]
    public VideoPlayer videoA;
    public VideoPlayer videoB;
    public VideoPlayer transition;
    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;

    [Header("轨迹效果")]
    public TrailRenderer trailPrefab;
    private TrailRenderer activeTrail;

    [Header("透明度控制")]
    [Range(0f, 1f)] public float mainAlpha = 0.8f;
    [Range(0f, 1f)] public float failAlpha = 0.3f;
    [Range(0f, 1f)] public float idleAlpha = 1f;
    public bool enableAlphaControl = true;

    [Header("Trail 显示阈值")]
    [Range(0f, 1f)] public float trailAlphaThreshold = 0.75f;

    [Header("Helix 切割")]
    public HelixPath helixPath;
    public float helixThreshold = 0.2f;
    public float requiredCoverage = 0.7f;

    [Header("调试")]
    public Camera mainCamera;
    public bool debugLog = true;

    private bool preparedA = false, preparedB = false, preparedT = false;
    private bool cutting = false;
    private bool failed = false;
    private float progress = 0f;

    private GeneralSequenceController controller;

    private List<Vector3> helixTrailPoints = new();

    private float currentRegionsAlpha = 1f;
    private bool CanDrawTrail() => (!enableAlphaControl) || (currentRegionsAlpha >= trailAlphaThreshold);

    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();

    // ------------------ 初始化 ----------------------
    public override void Initialize(GeneralSequenceController ctrl)
    {
        controller = ctrl;
        CacheAlphaTargets();

        SetupVideo(videoA, v => preparedA = true);
        SetupVideo(videoB, v => preparedB = true);
        SetupVideo(transition, v => preparedT = true);

        ResetState();

        if (debugLog)
            Debug.Log($"▶️ [HelixCutModule] Initialized");
    }

    private void CacheAlphaTargets()
    {
        graphics.Clear();
        sprites.Clear();
        groups.Clear();
        if (!regionsRoot) return;

        regionsRoot.GetComponentsInChildren(true, graphics);
        regionsRoot.GetComponentsInChildren(true, sprites);
        regionsRoot.GetComponentsInChildren(true, groups);
    }

    private void SetupVideo(VideoPlayer vp, System.Action<VideoPlayer> onReady)
    {
        if (!vp) return;
        vp.playOnAwake = false;
        vp.Pause();
        vp.Prepare();
        vp.prepareCompleted += _ => onReady?.Invoke(vp);
    }

    private void ResetState()
    {
        complete = false;
        cutting = false;
        failed = false;
        progress = 0f;
        helixTrailPoints.Clear();

        EnableOnly(rawA);

        if (videoA)
        {
            videoA.time = 0;
            videoA.StepForward();
        }

        ApplyRegionsAlpha(idleAlpha);
    }

    // ------------------ Update 主循环 ----------------------
    void Update()
    {
        if (complete) return;
        if (!mainCamera) mainCamera = Camera.main;

        Vector2 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        if (Input.GetMouseButtonDown(0) && IsInside(mouseWorld, areaA))
        {
            StartCut(mouseWorld);
        }

        if (cutting && !failed && Input.GetMouseButton(0))
        {
            UpdateCut(mouseWorld);
        }

        if (Input.GetMouseButtonUp(0))
        {
            FinishHelixCut();
        }
    }

    // ------------------ 切割开始 ----------------------
    private void StartCut(Vector2 pos)
    {
        cutting = true;
        failed = false;
        helixTrailPoints.Clear();

        // Trail
        if (trailPrefab && activeTrail == null)
        {
            activeTrail = Instantiate(trailPrefab, pos, Quaternion.identity);
            activeTrail.emitting = false;
            activeTrail.Clear();
            activeTrail.time = 999f;
            activeTrail.autodestruct = false;
            StartCoroutine(EnableTrailAfterFrame(activeTrail));
        }

        if (debugLog)
            Debug.Log("✂️ Helix cutting started");
    }

    // ------------------ 切割中 ----------------------
    private void UpdateCut(Vector2 mouseWorld)
    {
        if (activeTrail)
        {
            bool allow = CanDrawTrail();
            if (!allow)
            {
                activeTrail.emitting = false;
                activeTrail.gameObject.SetActive(false);
            }
            else
            {
                if (!activeTrail.gameObject.activeSelf)
                    activeTrail.gameObject.SetActive(true);

                activeTrail.emitting = true;
                activeTrail.transform.position =
                    new Vector3(mouseWorld.x, mouseWorld.y, activeTrail.transform.position.z);
            }

            helixTrailPoints.Add(activeTrail.transform.position);
        }

        // Scrub 视频（保持直线逻辑）
        if (IsInside(mouseWorld, areaB))
            UpdateVideoA(mouseWorld);
    }

    // ------------------ 视频 Scrub ----------------------
    private void UpdateVideoA(Vector2 mouseWorld)
    {
        if (!preparedA || !videoA || !areaB) return;

        float width = areaB.bounds.size.x;
        float startX = areaB.bounds.min.x;
        float current = Mathf.Clamp(mouseWorld.x, startX, startX + width);

        float newProgress = Mathf.InverseLerp(startX, startX + width, current);
        progress = Mathf.Max(progress, newProgress);

        double t = videoA.length * progress;
        videoA.time = t;
        videoA.StepForward();
    }

    // ------------------ 松手：螺旋判定 ----------------------
    private void FinishHelixCut()
    {
        if (activeTrail)
        {
            StartCoroutine(FadeOutAndDestroyTrail(activeTrail, 0.5f));
            activeTrail = null;
        }

        if (!cutting || failed)
        {
            cutting = false;
            return;
        }

        float cov = CalculateCoverage();

        if (debugLog)
            Debug.Log($"🌀 Helix Coverage = {cov}");

        if (cov >= requiredCoverage)
        {
            StartCoroutine(PlayTransition());
        }
        else
        {
            failed = true;
            StartCoroutine(PlayVideoB());
        }

        cutting = false;
    }

    // ------------------ 螺旋覆盖率 ----------------------
    private float CalculateCoverage()
    {
        if (!helixPath)
        {
            Debug.LogError("❌ HelixPath 未绑定！");
            return 0f;
        }

        var spiral = helixPath.GetPoints();
        int hit = 0;

        foreach (var s in spiral)
        {
            foreach (var t in helixTrailPoints)
            {
                if (Vector2.Distance(s, t) < helixThreshold)
                {
                    hit++;
                    break;
                }
            }
        }

        return (float)hit / spiral.Count;
    }

    // ------------------ 切割成功：Transition ----------------------
    private IEnumerator PlayTransition()
    {
        complete = true;

        EnableOnly(rawTrans);

        if (transition)
        {
            if (!transition.isPrepared)
                yield return new WaitUntil(() => transition.isPrepared);

            transition.time = 0;
            transition.Play();
            yield return new WaitUntil(() => !transition.isPlaying);
        }

        controller.OnModuleCompleted(this);
    }

    // ------------------ 切割失败：Fail 视频 ----------------------
    private IEnumerator PlayVideoB()
    {
        EnableOnly(rawB);

        if (!videoB.isPrepared)
            yield return new WaitUntil(() => videoB.isPrepared);

        videoB.time = videoB.length * Mathf.Clamp01(1f - progress);
        videoB.Play();

        yield return new WaitUntil(() => !videoB.isPlaying);

        EnableOnly(rawA);
        progress = 0f;
        failed = false;
    }

    // ------------------ 工具 ----------------------
    private void EnableOnly(RawImage active)
    {
        if (rawA) rawA.enabled = (active == rawA);
        if (rawB) rawB.enabled = (active == rawB);
        if (rawTrans) rawTrans.enabled = (active == rawTrans);

        if (!enableAlphaControl || !regionsRoot) return;

        if (active == rawA) ApplyRegionsAlpha(mainAlpha);
        else if (active == rawB) ApplyRegionsAlpha(failAlpha);
        else ApplyRegionsAlpha(idleAlpha);
    }

    private void ApplyRegionsAlpha(float a)
    {
        currentRegionsAlpha = a;

        foreach (var g in groups)
            if (g) g.alpha = a;

        foreach (var gr in graphics)
            if (gr)
            {
                var c = gr.color;
                gr.color = new Color(c.r, c.g, c.b, a);
            }

        foreach (var sr in sprites)
            if (sr)
            {
                var c = sr.color;
                sr.color = new Color(c.r, c.g, c.b, a);
            }
    }

    private bool IsInside(Vector2 p, Collider2D c) =>
        c && c.OverlapPoint(p);

    private IEnumerator FadeOutAndDestroyTrail(TrailRenderer trail, float duration)
    {
        if (!trail) yield break;

        float start = Time.time;
        float startWidth = trail.startWidth;
        Color startColor = trail.material.color;

        while (Time.time - start < duration)
        {
            float t = (Time.time - start) / duration;

            trail.startWidth = Mathf.Lerp(startWidth, 0f, t);
            trail.endWidth = trail.startWidth * 0.5f;

            trail.material.color = Color.Lerp(startColor,
                new Color(startColor.r, startColor.g, startColor.b, 0f), t);

            yield return null;
        }

        Destroy(trail.gameObject);
    }

    private IEnumerator EnableTrailAfterFrame(TrailRenderer tr)
    {
        yield return null;
        if (tr) tr.emitting = true;
    }
}
