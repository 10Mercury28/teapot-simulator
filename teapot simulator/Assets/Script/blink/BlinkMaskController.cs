using UnityEngine;
using System.Collections;
using UnityEngine.UI;

public class BlinkMaskController : MonoBehaviour
{
    [Header("上眼皮与下眼皮")]
    public RectTransform upperLid;
    public RectTransform lowerLid;

    [Header("光线淡化层 (可选)")]
    public CanvasGroup lightFadeGroup;

    [Header("眨眼参数")]
    public float minInterval = 3f;
    public float maxInterval = 5f;
    public float minBlinkDuration = 0.1f;
    public float maxBlinkDuration = 0.4f;
    public float blinkSpeedMultiplier = 10f;
    public float lidMoveDistance = 270f;

    [Header("呼吸参数")]
    public float breatheAmplitude = 10f;
    public float breatheSpeed = 0.8f;

    [Header("动画曲线")]
    public AnimationCurve blinkCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("场景启动行为")]
    [Tooltip("如果勾选，则场景以黑屏开始，然后自动睁眼")]
    public bool startBlack = false;

    private Vector2 upperStartPos;
    private Vector2 lowerStartPos;
    private bool isBlinking = false;

    void Start()
    {
        // 初始化位置（屏幕外）
        upperStartPos = new Vector2(upperLid.anchoredPosition.x, lidMoveDistance);
        lowerStartPos = new Vector2(lowerLid.anchoredPosition.x, -lidMoveDistance);

        if (startBlack)
        {
            // 🔦 如果需要从黑屏开始
            upperLid.anchoredPosition = upperStartPos - new Vector2(0, lidMoveDistance);
            lowerLid.anchoredPosition = lowerStartPos + new Vector2(0, lidMoveDistance);
            if (lightFadeGroup) lightFadeGroup.alpha = 0.9f;

            StartCoroutine(OpenEyesAfterDelay(0.3f));
        }
        else
        {
            // 正常状态：眼皮在外
            upperLid.anchoredPosition = upperStartPos;
            lowerLid.anchoredPosition = lowerStartPos;
            if (lightFadeGroup) lightFadeGroup.alpha = 0f;
        }

        StartCoroutine(BlinkLoop());
    }

    void Update()
    {
        // 🌬️ 呼吸浮动
        if (!isBlinking)
        {
            float offset = Mathf.Sin(Time.time * breatheSpeed) * breatheAmplitude;
            upperLid.anchoredPosition = upperStartPos + new Vector2(0, offset);
            lowerLid.anchoredPosition = lowerStartPos - new Vector2(0, offset);
        }
    }

    IEnumerator BlinkLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
            yield return StartCoroutine(BlinkOnce());
        }
    }

    IEnumerator BlinkOnce()
    {
        if (isBlinking) yield break;
        isBlinking = true;

        float blinkDuration = Random.Range(minBlinkDuration, maxBlinkDuration);
        float blinkSpeed = blinkSpeedMultiplier / blinkDuration;
        float t = 0f;

        Vector2 upperClosed = upperStartPos - new Vector2(0, lidMoveDistance);
        Vector2 lowerClosed = lowerStartPos + new Vector2(0, lidMoveDistance);

        // 🌓 闭眼
        while (t < 1f)
        {
            t += Time.deltaTime * blinkSpeed;
            float p = blinkCurve.Evaluate(t);
            upperLid.anchoredPosition = Vector2.Lerp(upperStartPos, upperClosed, p);
            lowerLid.anchoredPosition = Vector2.Lerp(lowerStartPos, lowerClosed, p);
            if (lightFadeGroup)
                lightFadeGroup.alpha = Mathf.Lerp(0f, 0.9f, p);
            yield return null;
        }

        // 💤 停留（闭眼）
        yield return new WaitForSeconds(blinkDuration * 0.25f);

        // 🌕 睁眼
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * blinkSpeed;
            float p = blinkCurve.Evaluate(t);
            upperLid.anchoredPosition = Vector2.Lerp(upperClosed, upperStartPos, p);
            lowerLid.anchoredPosition = Vector2.Lerp(lowerClosed, lowerStartPos, p);
            if (lightFadeGroup)
                lightFadeGroup.alpha = Mathf.Lerp(0.9f, 0f, p);
            yield return null;
        }

        isBlinking = false;
    }

    // 🎬 用于场景切换时：只闭眼、不睁开
    public IEnumerator ForceCloseEyesOnly(float duration)
    {
        if (isBlinking) yield break;
        isBlinking = true;

        float blinkSpeed = 10f / duration;
        float t = 0f;

        Vector2 upperClosed = upperStartPos - new Vector2(0, lidMoveDistance);
        Vector2 lowerClosed = lowerStartPos + new Vector2(0, lidMoveDistance);

        while (t < 1f)
        {
            t += Time.deltaTime * blinkSpeed;
            float p = blinkCurve.Evaluate(t);
            upperLid.anchoredPosition = Vector2.Lerp(upperStartPos, upperClosed, p);
            lowerLid.anchoredPosition = Vector2.Lerp(lowerStartPos, lowerClosed, p);

            if (lightFadeGroup)
                lightFadeGroup.alpha = Mathf.Lerp(0f, 0.9f, p);

            yield return null;
        }

        if (lightFadeGroup)
            lightFadeGroup.alpha = 0.9f;

        Debug.Log("🌑 闭眼完成（保持黑屏）");
        isBlinking = false;
    }

    // 🎥 从黑屏中睁眼（用于新场景启动）
    private IEnumerator OpenEyesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Debug.Log("🌅 从黑中睁眼...");

        float t = 0f;
        float duration = 0.4f;
        float blinkSpeed = 10f / duration;

        Vector2 upperClosed = upperStartPos - new Vector2(0, lidMoveDistance);
        Vector2 lowerClosed = lowerStartPos + new Vector2(0, lidMoveDistance);

        while (t < 1f)
        {
            t += Time.deltaTime * blinkSpeed;
            float p = blinkCurve.Evaluate(t);
            upperLid.anchoredPosition = Vector2.Lerp(upperClosed, upperStartPos, p);
            lowerLid.anchoredPosition = Vector2.Lerp(lowerClosed, lowerStartPos, p);
            if (lightFadeGroup)
                lightFadeGroup.alpha = Mathf.Lerp(0.9f, 0f, p);
            yield return null;
        }
    }

    public void ForceBlink() => StartCoroutine(BlinkOnce());
}
