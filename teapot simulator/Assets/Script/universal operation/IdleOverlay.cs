using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Collections;
using System.Collections.Generic;

[System.Serializable]
public class IdleVideoSlot
{
    public string name;
    public RawImage rawImage;
    public VideoPlayer videoPlayer;
}

[RequireComponent(typeof(CanvasGroup))]
public class IdleOverlay : MonoBehaviour
{
    [Header("Idle 1 Settings")]
    public List<IdleVideoSlot> idle1Slots = new List<IdleVideoSlot>();
    public float idleDelay = 10f;

    [Header("Idle 2 Settings")]
    public RawImage idle2Raw;
    public VideoPlayer idle2Video;

    [Header("Fade Settings")]
    public float fadeDuration = 1f;
    public float crossfadeDuration = 1f;

    [Header("Blink Overlay Prefab")]
    public GameObject blinkPrefab; // 👁️ 拖入 Canvas_BlinkOverlay

    [Header("Debug")]
    public bool debugLog = true;

    private static IdleOverlay _activeInstance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _activeInstance = null;
    }

    public static IdleOverlay ActiveInstance => _activeInstance;
    public static bool IsAnyIdleOverlayBlocking => _activeInstance != null && _activeInstance.IsBlocking;
    public bool IsBlocking => idleActive || inTransition || externalHoldActive;

    private CanvasGroup cg;
    private Vector3 lastMousePosition;
    private float idleTimer;
    private bool idleActive;
    private bool inTransition;
    private bool externalHoldActive;
    private bool externalExitRequested;
    private bool isExitingToIdle2;
    private System.Action onExternalExitCompleted;
    private IdleVideoSlot currentIdle1;
    private Collider2D[] cachedColliders;
    private PatSequenceController patSequence;

    void OnEnable()
    {
        _activeInstance = this;
    }

    void OnDisable()
    {
        if (_activeInstance == this)
            _activeInstance = null;
    }

    void OnDestroy()
    {
        if (_activeInstance == this)
            _activeInstance = null;
    }

    void Start()
    {
        cg = GetComponent<CanvasGroup>();
        SetOverlayVisible(false);
        DisableAll();

        cachedColliders = FindObjectsOfType<Collider2D>();
        patSequence = FindObjectOfType<PatSequenceController>();
        lastMousePosition = Input.mousePosition;

        Debug.Log("🎬 IdleOverlay 初始化完成。");
    }

    private bool ShouldSuppressIdle()
    {
        if (Time.timeScale <= 0f)
            return true;

        if (BeforeStartController.IsAnyBeforeStartBlocking)
            return true;

        if (PopupManager.ExistingInstance != null && PopupManager.ExistingInstance.HasActivePopup)
            return true;

        if (patSequence != null && patSequence.allComplete)
            return true;

        if (SceneBlinkTransition.Instance != null && SceneBlinkTransition.Instance.IsTransitioning)
            return true;

        return false;
    }

    void Update()
    {
        if (inTransition) return;

        if (externalHoldActive) return;

        if (!idleActive && ShouldSuppressIdle())
        {
            idleTimer = 0f;
            lastMousePosition = Input.mousePosition;
            return;
        }

        bool hasUserActivity = Input.mousePosition != lastMousePosition || Input.GetMouseButtonDown(0);
        if (hasUserActivity)
        {
            lastMousePosition = Input.mousePosition;
            idleTimer = 0f;

            if (idleActive)
                StartCoroutine(PlayIdle2Crossfade());
        }
        else
        {
            idleTimer += Time.deltaTime;
            if (!idleActive && idleTimer >= idleDelay)
                StartCoroutine(PlayNextIdle1());
        }
    }

    IEnumerator SpawnBlink(string reason)
    {
        if (blinkPrefab == null)
        {
            Debug.LogWarning($"👁️ IdleOverlay 未设置 Blink Prefab。跳过眨眼 ({reason})");
            yield break;
        }

        Debug.Log($"👁️ IdleOverlay 触发眨眼 ({reason})");
        GameObject blink = Instantiate(blinkPrefab);
        CanvasGroup cg = blink.GetComponent<CanvasGroup>();
        AudioSource audio = blink.GetComponent<AudioSource>();
        if (audio) audio.Play();

        float t = 0f;
        float dur = 0.6f;
        while (t < dur)
        {
            t += Time.deltaTime;
            if (cg) cg.alpha = Mathf.Sin((t / dur) * Mathf.PI);
            yield return null;
        }
        Destroy(blink);
    }

    IEnumerator PlayNextIdle1()
    {
        if (ShouldSuppressIdle() || idle1Slots == null || idle1Slots.Count == 0)
            yield break;

        inTransition = true;
        idleActive = true;

        yield return SpawnBlink("Idle1 Start");

        DisableAll();
        SetOverlayVisible(true);
        LockAllObjects(true);

        currentIdle1 = idle1Slots[Random.Range(0, idle1Slots.Count)];
        if (currentIdle1 != null && currentIdle1.videoPlayer && currentIdle1.rawImage)
        {
            currentIdle1.videoPlayer.isLooping = true;
            yield return SafePlay(currentIdle1.videoPlayer, currentIdle1.rawImage);
            currentIdle1.videoPlayer.isLooping = true;
        }

        inTransition = false;
    }

    public void EnterExternalIdle1(bool useBlink = true)
    {
        externalHoldActive = true;
        externalExitRequested = false;

        if (idleActive && !isExitingToIdle2)
            return;

        if (idle1Slots == null || idle1Slots.Count == 0)
        {
            Debug.LogWarning("⚠️ IdleOverlay: idle1Slots 为空，无法进入外部 Idle 1。");
            externalHoldActive = false;
            return;
        }

        StartCoroutine(EnterExternalIdle1Routine(useBlink));
    }

    private IEnumerator EnterExternalIdle1Routine(bool useBlink)
    {
        while (inTransition)
            yield return null;

        if (!externalHoldActive)
            yield break;

        inTransition = true;
        idleActive = true;
        isExitingToIdle2 = false;

        if (useBlink)
            yield return SpawnBlink("PatInterrupt → Idle1");

        DisableAll();
        SetOverlayVisible(true);
        LockAllObjects(true);

        currentIdle1 = idle1Slots[Random.Range(0, idle1Slots.Count)];
        if (currentIdle1 != null && currentIdle1.videoPlayer && currentIdle1.rawImage)
        {
            currentIdle1.videoPlayer.isLooping = true;
            yield return SafePlay(currentIdle1.videoPlayer, currentIdle1.rawImage);
            currentIdle1.videoPlayer.isLooping = true;
        }

        inTransition = false;

        if (externalExitRequested)
        {
            externalExitRequested = false;
            StartCoroutine(PlayIdle2Crossfade());
        }
    }

    public void ExitExternalIdleToIdle2(System.Action onCompleted = null)
    {
        if (!externalHoldActive && !idleActive && !inTransition)
        {
            onCompleted?.Invoke();
            return;
        }

        if (onCompleted != null)
            onExternalExitCompleted += onCompleted;

        if (isExitingToIdle2)
            return;

        if (inTransition)
        {
            externalExitRequested = true;
            return;
        }

        externalExitRequested = false;
        StartCoroutine(PlayIdle2Crossfade());
    }

    public void CancelExternalHoldImmediate()
    {
        externalHoldActive = false;
        externalExitRequested = false;
        isExitingToIdle2 = false;
        onExternalExitCompleted = null;
    }

    IEnumerator PlayIdle2Crossfade()
    {
        inTransition = true;
        isExitingToIdle2 = true;

        yield return SpawnBlink("Idle1 → Idle2");

        if (idle2Raw && idle2Video)
        {
            CanvasGroup cg2 = idle2Raw.GetComponent<CanvasGroup>();
            if (!cg2) cg2 = idle2Raw.gameObject.AddComponent<CanvasGroup>();
            cg2.alpha = 0;
            idle2Raw.enabled = true;

            idle2Video.isLooping = false;
            yield return SafePlay(idle2Video, idle2Raw);

            float t = 0;
            while (t < crossfadeDuration)
            {
                t += Time.deltaTime;
                cg2.alpha = Mathf.Lerp(0, 1, t / crossfadeDuration);
                yield return null;
            }

            float clipLen = idle2Video.clip != null ? (float)idle2Video.clip.length : (float)idle2Video.length;
            float maxWait = clipLen > 0.1f ? clipLen + 2f : 5f;
            float waitElapsed = 0f;
            while (idle2Video.isPlaying && waitElapsed < maxWait)
            {
                waitElapsed += Time.deltaTime;
                yield return null;
            }

            yield return SpawnBlink("Idle2 End");
            yield return FadeCanvas(1f, 0f, fadeDuration);

            if (idle2Raw) idle2Raw.enabled = false;
        }

        idleActive = false;
        externalHoldActive = false;
        externalExitRequested = false;
        isExitingToIdle2 = false;
        SetOverlayVisible(false);
        DisableAll();
        LockAllObjects(false);
        idleTimer = 0f;
        lastMousePosition = Input.mousePosition;
        inTransition = false;

        System.Action cb = onExternalExitCompleted;
        onExternalExitCompleted = null;
        cb?.Invoke();
    }

    IEnumerator SafePlay(VideoPlayer vp, RawImage raw)
    {
        if (vp == null || raw == null) yield break;
        if (vp.targetTexture == null)
        {
            vp.targetTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
            vp.targetTexture.Create();
        }

        if (!vp.isPrepared)
        {
            vp.Prepare();
            float prepTimeout = 3f;
            while (!vp.isPrepared && prepTimeout > 0f)
            {
                prepTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        vp.time = 0.0;
        vp.Play();

        float frameWait = 0.25f;
        while (vp.frame < 0 && frameWait > 0f)
        {
            frameWait -= Time.unscaledDeltaTime;
            yield return null;
        }

        raw.texture = vp.targetTexture;
        raw.color = Color.white;
        raw.enabled = true;
    }

    IEnumerator FadeCanvas(float from, float to, float dur)
    {
        float t = 0;
        while (t < dur)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, t / dur);
            yield return null;
        }
        cg.alpha = to;
    }

    void LockAllObjects(bool locked)
    {
        foreach (var col in cachedColliders)
        {
            if (col != null) col.enabled = !locked;
        }
    }

    void SetOverlayVisible(bool visible)
    {
        cg.alpha = visible ? 1f : 0f;
        cg.blocksRaycasts = visible;
        cg.interactable = visible;
    }

    void DisableAll()
    {
        foreach (var slot in idle1Slots)
        {
            if (slot.rawImage) slot.rawImage.enabled = false;
            if (slot.videoPlayer) slot.videoPlayer.Stop();
        }
        if (idle2Raw) idle2Raw.enabled = false;
        if (idle2Video) idle2Video.Stop();
    }
}
