using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// PatUIController (Final)
/// - 管理单个 module 的 UI 显示
/// - dim = 0.2, bright = 1.0, complete = 0
/// - 不拦截点击，不 SetActive(false)
/// </summary>
public class PatUIController : MonoBehaviour
{
    [Header("Canvas Group")]
    public CanvasGroup uiGroup;

    [Header("Start Image")]
    public Image startImage;

    [Header("Stacked Images")]
    public Transform stackRoot;
    public List<Sprite> stackSprites = new List<Sprite>();

    [Header("Click Feedback")]
    public Image clickFeedbackImage;
    public Sprite clickSprite;

    [Header("Alpha Settings")]
    [Range(0f, 1f)] public float dimAlpha = 0.2f;
    [Range(0f, 1f)] public float brightAlpha = 1.0f;

    private List<Image> spawnedImages = new List<Image>();
    private int currentIndex = 0;

    void Awake()
    {
        EnsureNoRaycast();
    }

    // ===================== 基础显示 =====================

    public void ShowStart()
    {
        ClearStack();

        if (startImage)
        {
            startImage.enabled = true;
            startImage.raycastTarget = false;
            SetAlpha(dimAlpha);
        }

        currentIndex = 0;
    }

    public void BuildStack(int count)
    {
        ClearStack();
        if (startImage) startImage.enabled = false;

        currentIndex = 0;
        int n = Mathf.Min(count, stackSprites.Count);

        for (int i = 0; i < n; i++)
        {
            GameObject go = new GameObject("Stack_" + i, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(stackRoot, false);

            Image img = go.GetComponent<Image>();
            img.sprite = stackSprites[i];
            img.color = Color.white;
            img.raycastTarget = false;

            spawnedImages.Add(img);
        }

        SetAlpha(brightAlpha);
    }

    public void OnHit()
    {
        StartCoroutine(FlashClick());

        if (currentIndex < spawnedImages.Count)
        {
            spawnedImages[currentIndex].enabled = false;
            currentIndex++;
        }
    }

    // ===================== Alpha 控制 =====================

    public void Dim()
    {
        SetAlpha(dimAlpha);
    }

    public void Bright()
    {
        SetAlpha(brightAlpha);
    }

    /// <summary>
    /// module 完成时调用：UI 彻底消失
    /// </summary>
    public void HideOnComplete()
    {
        SetAlpha(0f);
    }

    void SetAlpha(float a)
    {
        if (!uiGroup) return;
        uiGroup.alpha = a;
        uiGroup.blocksRaycasts = false;
        uiGroup.interactable = false;
    }

    // ===================== Helpers =====================

    IEnumerator FlashClick()
    {
        if (!clickFeedbackImage || !clickSprite) yield break;

        clickFeedbackImage.sprite = clickSprite;
        clickFeedbackImage.enabled = true;
        clickFeedbackImage.raycastTarget = false;

        yield return new WaitForSeconds(0.15f);

        clickFeedbackImage.enabled = false;
    }

    void ClearStack()
    {
        foreach (var img in spawnedImages)
            if (img) Destroy(img.gameObject);

        spawnedImages.Clear();
    }

    void EnsureNoRaycast()
    {
        if (uiGroup)
        {
            uiGroup.blocksRaycasts = false;
            uiGroup.interactable = false;
        }

        if (startImage) startImage.raycastTarget = false;
        if (clickFeedbackImage) clickFeedbackImage.raycastTarget = false;
    }
}
