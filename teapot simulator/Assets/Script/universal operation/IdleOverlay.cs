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

    private CanvasGroup cg;
    private Vector3 lastMousePosition;
    private float idleTimer;
    private bool idleActive;
    private bool inTransition;
    private IdleVideoSlot currentIdle1;
    private Collider2D[] cachedColliders;

    void Start()
    {
        cg = GetComponent<CanvasGroup>();
        SetOverlayVisible(false);
        DisableAll();

        cachedColliders = FindObjectsOfType<Collider2D>();
        lastMousePosition = Input.mousePosition;

        Debug.Log("🎬 IdleOverlay 初始化完成。");
    }

    void Update()
    {
        if (inTransition) return;

        if (Input.mousePosition != lastMousePosition)
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
        inTransition = true;
        idleActive = true;

        yield return SpawnBlink("Idle1 Start");

        SetOverlayVisible(true);
        LockAllObjects(true);

        currentIdle1 = idle1Slots[Random.Range(0, idle1Slots.Count)];
        if (currentIdle1.videoPlayer && currentIdle1.rawImage)
        {
            yield return SafePlay(currentIdle1.videoPlayer, currentIdle1.rawImage);
            currentIdle1.videoPlayer.isLooping = true;
        }

        inTransition = false;
    }

    IEnumerator PlayIdle2Crossfade()
    {
        inTransition = true;

        yield return SpawnBlink("Idle1 → Idle2");

        if (idle2Raw && idle2Video)
        {
            CanvasGroup cg2 = idle2Raw.GetComponent<CanvasGroup>();
            if (!cg2) cg2 = idle2Raw.gameObject.AddComponent<CanvasGroup>();
            cg2.alpha = 0;
            idle2Raw.enabled = true;

            yield return SafePlay(idle2Video, idle2Raw);

            float t = 0;
            while (t < crossfadeDuration)
            {
                t += Time.deltaTime;
                cg2.alpha = Mathf.Lerp(0, 1, t / crossfadeDuration);
                yield return null;
            }

            yield return new WaitWhile(() => idle2Video.isPlaying);

            yield return SpawnBlink("Idle2 End");
            yield return FadeCanvas(1f, 0f, fadeDuration);

            if (idle2Raw) idle2Raw.enabled = false;
        }

        idleActive = false;
        inTransition = false;
        SetOverlayVisible(false);
        LockAllObjects(false);
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
            yield return new WaitUntil(() => vp.isPrepared);
        }

        raw.texture = vp.targetTexture;
        raw.enabled = true;
        raw.color = Color.white;
        vp.Play();
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
