using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class BeforeStartController : MonoBehaviour
{
    [Header("UI 控制")]
    public CanvasGroup beforeStartCanvas;   // 整个 BeforeStart Canvas
    public Button triggerButton;            // 开始按钮

    [Header("计时与淡入")]
    public float lockDuration = 10f;        // 前10秒锁交互
    public float fadeDuration = 0.5f;       // 正常按钮淡入时间
    [Range(0f, 1f)] public float finalButtonAlpha = 0.7f;

    [Header("需要在开始时禁用的对象")]
    public List<GameObject> disableObjects;

    private bool sceneUnlocked = false;

    IEnumerator Start()
    {
        // ========= 初始化禁用对象 =========
        if (disableObjects != null)
        {
            foreach (var obj in disableObjects)
            {
                if (obj != null)
                    obj.SetActive(false);
            }
        }

        // ========= 初始化 Canvas =========
        if (beforeStartCanvas != null)
        {
            beforeStartCanvas.alpha = 1f;
            beforeStartCanvas.interactable = true;
            beforeStartCanvas.blocksRaycasts = true;
        }

        // ========= 初始化按钮 =========
        if (triggerButton != null)
        {
            // ⭐ 行为只注册一次
            triggerButton.onClick.RemoveAllListeners();
            triggerButton.onClick.AddListener(OnButtonClicked);

            CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
            if (btnCanvas != null)
                btnCanvas.alpha = 0f;

            triggerButton.interactable = false;
        }

        // ========= 正常等待流程 =========
        yield return new WaitForSeconds(lockDuration);
        yield return StartCoroutine(FadeInButton(fadeDuration));
    }

    // ================== 正常淡入 ==================

    private IEnumerator FadeInButton(float fadeTime)
    {
        if (triggerButton == null) yield break;

        CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
        if (btnCanvas == null) yield break;

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.deltaTime;
            btnCanvas.alpha = Mathf.Lerp(0f, finalButtonAlpha, t / fadeTime);
            yield return null;
        }

        btnCanvas.alpha = finalButtonAlpha;
        triggerButton.interactable = true;
    }

    // ================== 点击开始 ==================

    private void OnButtonClicked()
    {
        if (sceneUnlocked) return;

        sceneUnlocked = true;
        StartCoroutine(UnlockSequence());
    }

    private IEnumerator UnlockSequence()
    {
        // Canvas 淡出
        float t = 0f;
        float fadeTime = 0.5f;

        while (t < fadeTime)
        {
            t += Time.deltaTime;
            beforeStartCanvas.alpha = Mathf.Lerp(1f, 0f, t / fadeTime);
            yield return null;
        }

        beforeStartCanvas.alpha = 0f;
        beforeStartCanvas.interactable = false;
        beforeStartCanvas.blocksRaycasts = false;

        // 启用被禁用对象
        if (disableObjects != null)
        {
            foreach (var obj in disableObjects)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }
    }

    // ================== 给 Menu 用的接口 ==================

    public bool IsBeforeStartActive()
    {
        return !sceneUnlocked;
    }

    public void ForceShowButtonWithFade(float fadeTime)
    {
        if (sceneUnlocked) return;

        StopAllCoroutines();
        StartCoroutine(FadeInButtonUnscaled(fadeTime));
    }

    private IEnumerator FadeInButtonUnscaled(float fadeTime)
    {
        if (triggerButton == null) yield break;

        CanvasGroup btnCanvas = triggerButton.GetComponent<CanvasGroup>();
        if (btnCanvas == null) yield break;

        btnCanvas.alpha = 0f;
        triggerButton.interactable = false;

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;   // ⭐ Menu 打开时也能跑
            btnCanvas.alpha = Mathf.Lerp(0f, finalButtonAlpha, t / fadeTime);
            yield return null;
        }

        btnCanvas.alpha = finalButtonAlpha;
        triggerButton.interactable = true;
    }
}
