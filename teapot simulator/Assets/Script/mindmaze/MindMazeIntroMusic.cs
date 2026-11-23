using System.Collections;
using UnityEngine;

/// <summary>
/// 脑内迷宫（Mindmaze）开场音乐播放器：
/// 进入场景后等待转场淡入完成（可选），播放指定开场音频，并在结尾或指定时长后按 n 秒平滑渐弱至停止。
/// </summary>
public class MindMazeIntroMusic : MonoBehaviour
{
    [Header("开场音频配置")]
    [Tooltip("脑内迷宫开场音频（如 Assets/Audio/MindMaze_10s.mp3）")]
    public AudioClip introClip;

    [Tooltip("开场音乐峰值音量")]
    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("渐弱时长（秒，即用户指定的 n 秒渐弱）")]
    [Min(0f)]
    public float fadeOutDuration = 3f;

    [Tooltip("勾选：自动在音频结束前的最后 fadeOutDuration 秒开始渐弱；不勾选：使用下方 holdBeforeFadeSeconds 作为渐弱起点")]
    public bool fadeAtClipEnd = true;

    [Tooltip("仅当 fadeAtClipEnd = false 时生效：播放多少秒后开始渐弱")]
    [Min(0f)]
    public float holdBeforeFadeSeconds = 7f;

    [Tooltip("可选：开场淡入时长（秒，0 表示直接以设定音量开始）")]
    [Min(0f)]
    public float fadeInDuration = 0f;

    [Tooltip("揭幕后额外延迟多少秒再开始播放（秒）")]
    [Min(0f)]
    public float playDelay = 0f;

    [Header("转场同步与组件")]
    [Tooltip("若通过黑屏字卡转场进入 Mindmaze，等黑屏淡出揭幕后再开始播开场音乐")]
    public bool waitForSceneBlinkFadeIn = true;

    [Tooltip("播放用的 AudioSource（留空将在 Awake 自动创建并配置）")]
    public AudioSource audioSource;

    public bool debugLog = true;

    private Coroutine playRoutine;

    private void Awake()
    {
        EnsureAudioSource();
    }

    private void Start()
    {
        PlayNow();
    }

    private void OnDisable()
    {
        StopImmediately();
    }

    private void OnDestroy()
    {
        StopImmediately();
    }

    /// <summary>
    /// 立即按当前 Inspector 参数重新播放一次开场音乐（含 n 秒渐弱）
    /// </summary>
    public void PlayNow()
    {
        EnsureAudioSource();

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }

        playRoutine = StartCoroutine(PlayIntroMusicRoutine());
    }

    /// <summary>
    /// 立即停止开场音乐并终止渐弱协程
    /// </summary>
    public void StopImmediately()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (audioSource != null)
        {
            if (audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            audioSource.time = 0f;
            audioSource.volume = 0f;
        }
    }

    private void EnsureAudioSource()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
    }

    private IEnumerator PlayIntroMusicRoutine()
    {
        if (introClip == null)
        {
            if (debugLog) Debug.LogWarning("⚠️ [MindMazeIntroMusic] 未指定 introClip，跳过开场音乐播放。");
            playRoutine = null;
            yield break;
        }

        if (waitForSceneBlinkFadeIn && SceneBlinkTransition.Instance != null)
        {
            float blinkTimeout = 3.5f;
            while (SceneBlinkTransition.Instance != null &&
                   SceneBlinkTransition.Instance.IsTransitioning &&
                   blinkTimeout > 0f)
            {
                blinkTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (playDelay > 0f)
        {
            float delayTimer = 0f;
            while (delayTimer < playDelay)
            {
                delayTimer += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        EnsureAudioSource();
        if (audioSource == null)
        {
            playRoutine = null;
            yield break;
        }

        float clipLen = introClip.length;
        float targetVol = Mathf.Clamp01(volume);
        float effectiveFadeOut = Mathf.Clamp(fadeOutDuration, 0f, clipLen);
        float fadeStartTime = fadeAtClipEnd
            ? Mathf.Max(0f, clipLen - effectiveFadeOut)
            : Mathf.Clamp(holdBeforeFadeSeconds, 0f, clipLen);

        if (!fadeAtClipEnd)
        {
            effectiveFadeOut = Mathf.Min(Mathf.Max(0f, fadeOutDuration), Mathf.Max(0f, clipLen - fadeStartTime));
        }

        float effectiveFadeIn = Mathf.Clamp(fadeInDuration, 0f, fadeStartTime);
        float endTime = fadeStartTime + effectiveFadeOut;

        audioSource.clip = introClip;
        audioSource.loop = false;
        audioSource.time = 0f;
        audioSource.volume = effectiveFadeIn > 0f ? 0f : targetVol;
        audioSource.Play();

        if (debugLog)
        {
            Debug.Log($"🎵 [MindMazeIntroMusic] 播放 '{introClip.name}' (len={clipLen:F2}s, fadeStart={fadeStartTime:F2}s, fadeOut={effectiveFadeOut:F2}s)");
        }

        float elapsed = 0f;
        while (audioSource != null && audioSource.isPlaying)
        {
            if (elapsed < effectiveFadeIn && effectiveFadeIn > 0f)
            {
                audioSource.volume = Mathf.Lerp(0f, targetVol, Mathf.Clamp01(elapsed / effectiveFadeIn));
            }
            else if (elapsed < fadeStartTime)
            {
                audioSource.volume = targetVol;
            }
            else if (elapsed < endTime && effectiveFadeOut > 0f)
            {
                float fadeT = Mathf.Clamp01((elapsed - fadeStartTime) / effectiveFadeOut);
                audioSource.volume = Mathf.Lerp(targetVol, 0f, fadeT);
            }
            else
            {
                audioSource.volume = 0f;
                audioSource.Stop();
                break;
            }

            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }

        if (audioSource != null)
        {
            audioSource.volume = 0f;
            if (audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        if (debugLog)
        {
            Debug.Log("🔇 [MindMazeIntroMusic] 开场音乐渐弱结束并停止。");
        }

        playRoutine = null;
    }
}
