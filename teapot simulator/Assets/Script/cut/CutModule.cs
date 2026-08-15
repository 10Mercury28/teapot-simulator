/* 成功无trail
 using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Canvas))]
public class CutModule : MonoBehaviour
{
    [Header("区域（必须挂 Collider2D）")]
    public Collider2D areaA;
    public Collider2D areaB;
    public Collider2D areaC;

    [Header("可选：区域可视根（把ABC条/图形都放到这里，便于统一改透明度）")]
    public GameObject regionsRoot;

    [Header("视频组件")]
    public VideoPlayer videoA;      // 主视频（可 scrub / 停第一帧）
    public VideoPlayer videoB;      // 失败视频
    public VideoPlayer transition;  // 成功过渡视频
    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;

    [Header("轨迹效果")]
    public TrailRenderer trailPrefab;
    private TrailRenderer activeTrail;

    [Header("透明度控制")]
    [Range(0f, 1f)] public float mainAlpha = 0.8f;  // A 显示/播放时
    [Range(0f, 1f)] public float failAlpha = 0.3f;  // B 播放时
    [Range(0f, 1f)] public float idleAlpha = 1.0f;  // 其它时刻（例如初始化）
    public bool enableAlphaControl = true;

    [Header("调试")]
    public Camera mainCamera;
    public bool debugLog = true;

    [Header("状态（只读观察）")]
    public bool complete = false;

    // 运行态
    private bool preparedA = false, preparedB = false, preparedT = false;
    private bool cutting = false, failed = false, inB = false;
    private float progress = 0f;

    private CutSequenceController controller;

    // 缓存可调透明度目标
    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();

    public void Initialize(CutSequenceController ctrl)
    {
        controller = ctrl;

        CacheAlphaTargets(); // ✅ 收集 regionsRoot 下所有可调透明度对象

        SetupVideo(videoA, v => preparedA = true);
        SetupVideo(videoB, v => preparedB = true);
        SetupVideo(transition, v => preparedT = true);

        ResetState();

        if (debugLog) Debug.Log($"🔍 [{name}] ▶️ Initialized by Controller");
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
        progress = 0f;
        failed = false;
        inB = false;
        cutting = false;
        complete = false;

        EnableOnly(rawA);

        if (videoA)
        {
            videoA.Pause();
            videoA.time = 0;
            videoA.StepForward();
        }

        // 初始交互可用、透明度按 idle
        SetRegionsActive(true);
        ApplyRegionsAlpha(idleAlpha);

        if (debugLog) LogStatus("🔄 State Reset");
    }

    private void EnableOnly(RawImage active)
    {
        if (rawA) rawA.enabled = (active == rawA);
        if (rawB) rawB.enabled = (active == rawB);
        if (rawTrans) rawTrans.enabled = (active == rawTrans);

        // 基于可见层同步透明度（只要启用了控制）
        if (!enableAlphaControl || !regionsRoot) return;

        if (active == rawA)
            ApplyRegionsAlpha(mainAlpha);
        else if (active == rawB)
            ApplyRegionsAlpha(failAlpha);
        else
            ApplyRegionsAlpha(idleAlpha);
    }

    private void ApplyRegionsAlpha(float a)
    {
        // CanvasGroup 优先，能“一键控制”
        foreach (var g in groups)
            if (g) g.alpha = a;

        // 其次逐个 Graphic/SpriteRenderer
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

    private void SetRegionsActive(bool on)
    {
        if (regionsRoot)
        {
            regionsRoot.SetActive(on);
        }
        else
        {
            if (areaA) areaA.gameObject.SetActive(on);
            if (areaB) areaB.gameObject.SetActive(on);
            if (areaC) areaC.gameObject.SetActive(on);
        }
    }

    private void LogStatus(string prefix)
    {
        if (!debugLog) return;
        Debug.Log($"{prefix}\n" +
                  $"    RAW => A[{rawA?.enabled}], B[{rawB?.enabled}], T[{rawTrans?.enabled}]\n" +
                  $"    STATE => cutting[{cutting}], failed[{failed}], inB[{inB}], complete[{complete}]");
    }

    void Update()
    {
        if (complete) return;
        if (!mainCamera) mainCamera = Camera.main;

        Vector2 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        // 按下开始
        if (Input.GetMouseButtonDown(0) && IsInside(mouseWorld, areaA))
        {
            cutting = true;
            failed = false;
            inB = false;
            progress = 0f;

            if (trailPrefab && activeTrail == null)
            {
                activeTrail = Instantiate(trailPrefab, mouseWorld, Quaternion.identity);
                activeTrail.Clear();
            }

            // A 层被看到 → 应用 mainAlpha（保证视觉一致）
            if (rawA && rawA.enabled && enableAlphaControl)
                ApplyRegionsAlpha(mainAlpha);

            if (debugLog) LogStatus("✂️ Cut started in A");
        }

        // 拖动中
        if (cutting && !failed && Input.GetMouseButton(0))
        {
            if (activeTrail)
                activeTrail.transform.position = mouseWorld;

            if (IsInside(mouseWorld, areaB))
            {
                inB = true;
                UpdateVideoA(mouseWorld);  // 实时 scrub 视频A
                if (rawA && rawA.enabled && enableAlphaControl)
                    ApplyRegionsAlpha(mainAlpha);
            }
            else if (inB)
            {
                failed = true;
                StartCoroutine(PlayVideoB());
            }
        }

        // 松手结束
        if (Input.GetMouseButtonUp(0))
        {
            if (activeTrail)
                activeTrail.emitting = false;

            if (cutting && !failed)
            {
                if (inB && IsInside(mouseWorld, areaC))
                {
                    StartCoroutine(PlayTransition());
                }
                else
                {
                    failed = true;
                    StartCoroutine(PlayVideoB());
                }
            }
            cutting = false;
        }
    }

    // A 按 x 坐标进度 scrub
    private void UpdateVideoA(Vector2 mouseWorld)
    {
        if (!preparedA || !videoA || !areaB) return;

        float width = areaB.bounds.size.x;
        float startX = areaB.bounds.min.x;
        float current = Mathf.Clamp(mouseWorld.x, startX, startX + width);

        float newProgress = Mathf.InverseLerp(startX, startX + width, current);
        progress = Mathf.Max(progress, newProgress);

        double t = videoA.length * progress;
        videoA.Pause();
        videoA.time = t;
        videoA.StepForward();
    }

    // 失败：B 从 (1-progress) 处开播，播完再回到 A 的第一帧
    private IEnumerator PlayVideoB()
    {
        if (!videoB) yield break;

        // 切层到 B，并把 ABC 区域设为 failAlpha
        EnableOnly(rawB); // 内部会自动应用 failAlpha

        if (!videoB.isPrepared)
        {
            preparedB = false;
            videoB.Prepare();
            yield return new WaitUntil(() => videoB.isPrepared);
            preparedB = true;
        }

        double startTime = videoB.length * Mathf.Clamp01(1f - progress);
        videoB.time = startTime;
        videoB.Play();

        yield return new WaitUntil(() => !videoB.isPlaying);

        videoB.Stop();
        videoB.time = 0;

        // 回到 A 的第一帧（并恢复 mainAlpha）
        EnableOnly(rawA); // 内部会自动应用 mainAlpha

        if (videoA)
        {
            videoA.Pause();
            videoA.time = 0;
            videoA.StepForward();
        }

        // 允许重新交互
        SetRegionsActive(true);
        if (activeTrail)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }
        failed = false;
        inB = false;
        cutting = false;
        if (debugLog) LogStatus("🔁 Fail handled → back to A");
    }

    // 成功：隐藏区域 → 播放 transition → 标记 complete → 通知控制器
    private IEnumerator PlayTransition()
    {
        // 1) 立即隐藏 ABC（bar）
        SetRegionsActive(false);

        // 2) 预载下一个模块并让它显示 A 的第一帧（避免空白）
        if (controller != null)
        {
            int idx = System.Array.IndexOf(controller.modules, this);
            if (idx >= 0 && idx + 1 < controller.modules.Length)
            {
                CutModule next = controller.modules[idx + 1];
                if (next && !next.gameObject.activeSelf)
                {
                    next.gameObject.SetActive(true);
                    next.Initialize(controller);
                    if (next.videoA)
                    {
                        next.videoA.Pause();
                        next.videoA.time = 0;
                        next.videoA.StepForward();
                    }
                    if (next.rawA) next.rawA.enabled = true;
                    if (next.debugLog) Debug.Log($"👀 预载下一模块首帧：{next.name}");
                }
            }
        }

        // 3) 切到过渡层并播放
        EnableOnly(rawTrans); // 转场时无需调整 regions 透明度（已隐藏）

        if (transition)
        {
            if (!transition.isPrepared)
            {
                preparedT = false;
                transition.Prepare();
                yield return new WaitUntil(() => transition.isPrepared);
                preparedT = true;
            }
            transition.time = 0;
            transition.Play();
            yield return new WaitUntil(() => !transition.isPlaying);
            transition.Stop();
        }

        // 4) 完成并通知
        complete = true;
        controller?.OnModuleCompleted(this);

        // 清理轨迹
        if (activeTrail)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        if (debugLog) LogStatus("✅ Transition done → complete");
    }

    private bool IsInside(Vector2 p, Collider2D c) => c && c.OverlapPoint(p);
}
*/



using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(Canvas))]
public class CutModule : MonoBehaviour
{
    [Header("区域（必须挂 Collider2D）")]
    public Collider2D areaA;
    public Collider2D areaB;
    public Collider2D areaC;

    [Header("可选：区域可视根（把ABC条/图形都放到这里，便于统一改透明度）")]
    public GameObject regionsRoot;

    [Header("视频组件")]
    public VideoPlayer videoA;      // 主视频（可 scrub / 停第一帧）
    public VideoPlayer videoB;      // 失败视频
    public VideoPlayer transition;  // 成功过渡视频
    public RawImage rawA;
    public RawImage rawB;
    public RawImage rawTrans;

    [Header("轨迹效果")]
    public TrailRenderer trailPrefab;
    private TrailRenderer activeTrail;

    [Header("透明度控制")]
    [Range(0f, 1f)] public float mainAlpha = 0.8f;
    [Range(0f, 1f)] public float failAlpha = 0.3f;
    [Range(0f, 1f)] public float idleAlpha = 1.0f;
    public bool enableAlphaControl = true;

    [Header("Trail 显示阈值")]
    [Range(0f, 1f)] public float trailAlphaThreshold = 0.75f;

    [Header("调试")]
    public Camera mainCamera;
    public bool debugLog = true;

    [Header("状态（只读观察）")]
    public bool complete = false;

    // 内部状态
    private bool preparedA = false, preparedB = false, preparedT = false;
    private bool cutting = false, failed = false, inB = false;
    private float progress = 0f;

    private CutSequenceController controller;

    // 缓存透明度
    private float currentRegionsAlpha = 1f;
    private bool CanDrawTrail() => (!enableAlphaControl) || (currentRegionsAlpha >= trailAlphaThreshold);

    // 缓存可调透明度目标
    private readonly List<Graphic> graphics = new();
    private readonly List<SpriteRenderer> sprites = new();
    private readonly List<CanvasGroup> groups = new();

    public void Initialize(CutSequenceController ctrl)
    {
        controller = ctrl;

        CacheAlphaTargets();

        SetupVideo(videoA, v => preparedA = true);
        SetupVideo(videoB, v => preparedB = true);
        SetupVideo(transition, v => preparedT = true);

        ResetState();

        if (debugLog) Debug.Log($"🔍 [{name}] ▶️ Initialized by Controller");
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
        progress = 0f;
        failed = false;
        inB = false;
        cutting = false;
        complete = false;

        EnableOnly(rawA);

        if (videoA)
        {
            videoA.Pause();
            videoA.time = 0;
            videoA.StepForward();
        }

        SetRegionsActive(true);
        ApplyRegionsAlpha(idleAlpha);

        if (activeTrail)
            activeTrail.gameObject.SetActive(CanDrawTrail());

        if (debugLog) LogStatus("🔄 State Reset");
    }

    private void EnableOnly(RawImage active)
    {
        if (rawA) rawA.enabled = (active == rawA);
        if (rawB) rawB.enabled = (active == rawB);
        if (rawTrans) rawTrans.enabled = (active == rawTrans);

        // 🧩 根据透明度阈值控制 trail 显示
        if (activeTrail)
            activeTrail.gameObject.SetActive(CanDrawTrail());

        if (!enableAlphaControl || !regionsRoot) return;

        if (active == rawA)
            ApplyRegionsAlpha(mainAlpha);
        else if (active == rawB)
            ApplyRegionsAlpha(failAlpha);
        else
            ApplyRegionsAlpha(idleAlpha);
    }

    private void ApplyRegionsAlpha(float a)
    {
        currentRegionsAlpha = a; // 记录当前透明度

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

    private void SetRegionsActive(bool on)
    {
        if (regionsRoot)
        {
            regionsRoot.SetActive(on);
        }
        else
        {
            if (areaA) areaA.gameObject.SetActive(on);
            if (areaB) areaB.gameObject.SetActive(on);
            if (areaC) areaC.gameObject.SetActive(on);
        }
    }

    private void LogStatus(string prefix)
    {
        if (!debugLog) return;
        Debug.Log($"{prefix}\n" +
                  $"    RAW => A[{rawA?.enabled}], B[{rawB?.enabled}], T[{rawTrans?.enabled}]\n" +
                  $"    STATE => cutting[{cutting}], failed[{failed}], inB[{inB}], complete[{complete}]");
    }

    void Update()
    {
        if (complete) return;
        if (!mainCamera) mainCamera = Camera.main;

        Vector2 mouseWorld = mainCamera.ScreenToWorldPoint(Input.mousePosition);

        // 🖱️ 按下开始
        if (Input.GetMouseButtonDown(0) && IsInside(mouseWorld, areaA))
        {
            if (!CanDrawTrail())
            {
                if (debugLog) Debug.Log("✋ Trail blocked: regions alpha below threshold.");
                return;
            }

            cutting = true;
            failed = false;
            inB = false;
            progress = 0f;

            if (trailPrefab && activeTrail == null)
            {
                activeTrail = Instantiate(trailPrefab, mouseWorld, Quaternion.identity);
                activeTrail.emitting = false;
                activeTrail.Clear();
                activeTrail.time = 999f;
                activeTrail.autodestruct = false;
                StartCoroutine(EnableTrailAfterFrame(activeTrail));
                activeTrail.gameObject.SetActive(CanDrawTrail());
            }

            if (rawA && rawA.enabled && enableAlphaControl)
                ApplyRegionsAlpha(mainAlpha);

            if (debugLog) LogStatus("✂️ Cut started in A");
        }

        // 🪶 拖动中
        if (cutting && !failed && Input.GetMouseButton(0))
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
                    if (!activeTrail.gameObject.activeSelf) activeTrail.gameObject.SetActive(true);
                    if (!activeTrail.emitting) activeTrail.emitting = true;
                    activeTrail.transform.position = new Vector3(mouseWorld.x, mouseWorld.y, activeTrail.transform.position.z);
                }
            }

            if (IsInside(mouseWorld, areaB))
            {
                inB = true;
                UpdateVideoA(mouseWorld);
                if (rawA && rawA.enabled && enableAlphaControl)
                    ApplyRegionsAlpha(mainAlpha);
            }
            else if (inB)
            {
                failed = true;
                StartCoroutine(PlayVideoB());
            }
        }

        // 🖱️ 松手结束
        if (Input.GetMouseButtonUp(0))
        {
            if (activeTrail)
            {
                StartCoroutine(FadeOutAndDestroyTrail(activeTrail, 0.5f));
                activeTrail = null;
            }

            if (cutting && !failed)
            {
                if (inB && IsInside(mouseWorld, areaC))
                {
                    StartCoroutine(PlayTransition());
                }
                else
                {
                    failed = true;
                    StartCoroutine(PlayVideoB());
                }
            }
            cutting = false;
        }
    }

    // 延迟一帧启用 Trail（防止半圆伪影）
    private IEnumerator EnableTrailAfterFrame(TrailRenderer tr)
    {
        yield return null;
        if (tr) tr.emitting = true;
    }

    // A 区域视频 Scrub
    private void UpdateVideoA(Vector2 mouseWorld)
    {
        if (!preparedA || !videoA || !areaB) return;

        float width = areaB.bounds.size.x;
        float startX = areaB.bounds.min.x;
        float current = Mathf.Clamp(mouseWorld.x, startX, startX + width);

        float newProgress = Mathf.InverseLerp(startX, startX + width, current);
        progress = Mathf.Max(progress, newProgress);

        double t = videoA.length * progress;
        videoA.Pause();
        videoA.time = t;
        videoA.StepForward();
    }

    // 失败流程
    private IEnumerator PlayVideoB()
    {
        if (!videoB) yield break;

        EnableOnly(rawB);

        if (!videoB.isPrepared)
        {
            preparedB = false;
            videoB.Prepare();
            yield return new WaitUntil(() => videoB.isPrepared);
            preparedB = true;
        }

        double startTime = videoB.length * Mathf.Clamp01(1f - progress);
        videoB.time = startTime;
        videoB.Play();

        yield return new WaitUntil(() => !videoB.isPlaying);

        videoB.Stop();
        videoB.time = 0;

        EnableOnly(rawA);

        if (videoA)
        {
            videoA.Pause();
            videoA.time = 0;
            videoA.StepForward();
        }

        SetRegionsActive(true);
        if (activeTrail)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }
        failed = false;
        inB = false;
        cutting = false;
        if (debugLog) LogStatus("🔁 Fail handled → back to A");
    }

    // 成功流程
    private IEnumerator PlayTransition()
    {
        SetRegionsActive(false);

        if (controller != null)
        {
            int idx = System.Array.IndexOf(controller.modules, this);
            if (idx >= 0 && idx + 1 < controller.modules.Length)
            {
                CutModule next = controller.modules[idx + 1];
                if (next && !next.gameObject.activeSelf)
                {
                    next.gameObject.SetActive(true);
                    next.Initialize(controller);
                    if (next.videoA)
                    {
                        next.videoA.Pause();
                        next.videoA.time = 0;
                        next.videoA.StepForward();
                    }
                    if (next.rawA) next.rawA.enabled = true;
                    if (next.debugLog) Debug.Log($"👀 预载下一模块首帧：{next.name}");
                }
            }
        }

        EnableOnly(rawTrans);

        if (transition)
        {
            if (!transition.isPrepared)
            {
                preparedT = false;
                transition.Prepare();
                yield return new WaitUntil(() => transition.isPrepared);
                preparedT = true;
            }
            transition.time = 0;
            transition.Play();
            yield return new WaitUntil(() => !transition.isPlaying);
            transition.Stop();
        }

        complete = true;
        controller?.OnModuleCompleted(this);

        if (activeTrail)
        {
            Destroy(activeTrail.gameObject);
            activeTrail = null;
        }

        if (debugLog) LogStatus("✅ Transition done → complete");
    }

    private bool IsInside(Vector2 p, Collider2D c) => c && c.OverlapPoint(p);

    // 🕓 轨迹淡出
    private IEnumerator FadeOutAndDestroyTrail(TrailRenderer trail, float duration)
    {
        if (!trail) yield break;
        float startTime = Time.time;
        float startWidth = trail.startWidth;
        float endWidth = 0f;
        Color startColor = trail.material.color;
        Color endColor = new Color(startColor.r, startColor.g, startColor.b, 0f);

        while (Time.time - startTime < duration)
        {
            float t = (Time.time - startTime) / duration;
            trail.startWidth = Mathf.Lerp(startWidth, endWidth, t);
            trail.endWidth = trail.startWidth * 0.5f;
            trail.material.color = Color.Lerp(startColor, endColor, t);
            yield return null;
        }

        Destroy(trail.gameObject);
    }
}





