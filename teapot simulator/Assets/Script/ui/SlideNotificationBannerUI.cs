using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 窄窗口控制器 (SlideNotificationBannerUI)
/// 负责 600x200 右侧滑入横幅的动效、手势拖拽划走以及点击交互。
/// 遵守需求：
/// 1. 不自动消失，不点击则常驻；
/// 2. 点击触发回调，通知 PopupManager 决定是直接关闭还是展开大抽屉；
/// 3. 支持向右拖拽划走 (Swipe to Dismiss)。
/// </summary>
public class SlideNotificationBannerUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerDownHandler
{
    [Header("=== UI 节点引用 ===")]
    [Tooltip("横幅卡片 RectTransform (通常停靠在屏幕右中)")]
    public RectTransform cardRect;

    [Tooltip("横幅 CanvasGroup (用于淡入淡出及射线拦截)")]
    public CanvasGroup canvasGroup;

    [Tooltip("横幅背景图 (600x200)")]
    public Image backgroundImage;

    [Tooltip("头像/来源图标 (150x150)")]
    public Image avatarImage;

    [Tooltip("标题文本 (TextMeshProUGUI)")]
    public TextMeshProUGUI titleText;

    [Tooltip("正文摘要文本 (TextMeshProUGUI)")]
    public TextMeshProUGUI summaryText;

    [Header("=== 动效与停靠参数 ===")]
    [Tooltip("停靠在屏幕右边缘时的边距 (像素)")]
    public float marginRight = 25f;

    [Tooltip("滑入动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideInDuration = 0.35f;

    [Tooltip("滑出动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideOutDuration = 0.25f;

    [Tooltip("滑入曲线")]
    public AnimationCurve slideInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("滑出曲线")]
    public AnimationCurve slideOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("=== 拖拽划走 (Swipe to Dismiss) ===")]
    [Tooltip("向右拖拽超过卡片宽度的多少百分比算作划走成功")]
    [Range(0.1f, 0.8f)] public float dismissThresholdPercent = 0.35f;

    [Tooltip("拖拽判定死区 (超过此像素位移才认定为滑动，避免点击误触)")]
    public float dragDeadzone = 12f;

    // 事件
    public event Action OnClicked;
    public event Action OnDismissedBySwipe;

    public bool IsVisible { get; private set; } = false;
    public bool IsAnimating { get; private set; } = false;

    private Vector2 visiblePos;
    private Vector2 hiddenPos;
    private float cardWidth = 600f;

    private Coroutine activeAnimRoutine;

    // 拖拽判定
    private bool isDragging = false;
    private bool dragExceededDeadzone = false;
    private Vector2 pointerDownPos;
    private Vector2 dragStartCardPos;

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (cardRect == null) cardRect = GetComponent<RectTransform>();

        if (canvasGroup == null)
        {
            canvasGroup = cardRect.GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = cardRect.gameObject.AddComponent<CanvasGroup>();
        }

        // 统一设为 Middle-Right 锚点
        cardRect.anchorMin = new Vector2(1f, 0.5f);
        cardRect.anchorMax = new Vector2(1f, 0.5f);
        cardRect.pivot = new Vector2(1f, 0.5f);

        cardWidth = Mathf.Max(cardRect.rect.width, 200f);
        visiblePos = new Vector2(-marginRight, 0f);
        hiddenPos = new Vector2(cardWidth + 80f, 0f);
    }

    /// <summary>
    /// 填入数据
    /// </summary>
    public void BindData(PopupWindowDataSO data, Sprite defaultBg = null, Sprite defaultAvatar = null)
    {
        if (data == null) return;

        // 1. 标题与摘要
        if (titleText != null)
        {
            titleText.text = !string.IsNullOrEmpty(data.bannerTitle) ? data.bannerTitle : "新消息";
        }

        if (summaryText != null)
        {
            summaryText.text = data.bannerSummary ?? "";
        }

        // 2. 头像
        if (avatarImage != null)
        {
            Sprite spr = data.avatarSprite != null ? data.avatarSprite : defaultAvatar;
            if (spr != null)
            {
                avatarImage.sprite = spr;
                avatarImage.gameObject.SetActive(true);
            }
            else
            {
                avatarImage.gameObject.SetActive(false);
            }
        }

        // 3. 背景图
        if (backgroundImage != null)
        {
            Sprite bg = data.bannerBackground != null ? data.bannerBackground : defaultBg;
            if (bg != null)
            {
                backgroundImage.sprite = bg;
                backgroundImage.type = Image.Type.Simple;
            }
        }
    }

    /// <summary>
    /// 滑入显示（常驻，不自动消失）
    /// </summary>
    public void Show()
    {
        Initialize();
        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
        activeAnimRoutine = StartCoroutine(SlideInRoutine());
    }

    /// <summary>
    /// 滑出隐藏
    /// </summary>
    public void Hide(Action onComplete = null)
    {
        if (!IsVisible && !IsAnimating)
        {
            onComplete?.Invoke();
            return;
        }

        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
        activeAnimRoutine = StartCoroutine(SlideOutRoutine(onComplete));
    }

    /// <summary>
    /// 瞬间隐藏
    /// </summary>
    public void SnapToHidden()
    {
        Initialize();
        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);

        cardRect.anchoredPosition = hiddenPos;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        IsVisible = false;
        IsAnimating = false;
        isDragging = false;
    }

    private IEnumerator SlideInRoutine()
    {
        IsAnimating = true;
        IsVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        Vector2 startPos = cardRect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideInDuration);
            float curveT = slideInCurve.Evaluate(t);

            cardRect.anchoredPosition = Vector2.LerpUnclamped(startPos, visiblePos, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, curveT);

            yield return null;
        }

        cardRect.anchoredPosition = visiblePos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        IsAnimating = false;
    }

    private IEnumerator SlideOutRoutine(Action onComplete)
    {
        IsAnimating = true;

        Vector2 startPos = cardRect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideOutDuration);
            float curveT = slideOutCurve.Evaluate(t);

            cardRect.anchoredPosition = Vector2.LerpUnclamped(startPos, hiddenPos, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(1f, 0f, curveT);

            yield return null;
        }

        cardRect.anchoredPosition = hiddenPos;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        IsVisible = false;
        IsAnimating = false;

        onComplete?.Invoke();
    }

    // ============================================================
    // 手势交互：拖拽划走 & 点击
    // ============================================================

    public void OnPointerDown(PointerEventData eventData)
    {
        pointerDownPos = eventData.position;
        dragExceededDeadzone = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!IsVisible || IsAnimating) return;

        isDragging = true;
        dragStartCardPos = cardRect.anchoredPosition;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        float deltaX = eventData.position.x - pointerDownPos.x;

        if (Mathf.Abs(deltaX) > dragDeadzone)
        {
            dragExceededDeadzone = true;
        }

        // 仅允许向右划出屏幕
        float allowedX = Mathf.Max(visiblePos.x, dragStartCardPos.x + deltaX);
        cardRect.anchoredPosition = new Vector2(allowedX, visiblePos.y);

        if (canvasGroup != null)
        {
            float dragProgress = Mathf.Clamp01((allowedX - visiblePos.x) / (cardWidth * 0.8f));
            canvasGroup.alpha = Mathf.Lerp(1f, 0.4f, dragProgress);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        float draggedDistanceX = cardRect.anchoredPosition.x - visiblePos.x;
        float thresholdDistance = cardWidth * dismissThresholdPercent;

        bool shouldDismiss = draggedDistanceX >= thresholdDistance || eventData.delta.x > 8f;

        if (shouldDismiss)
        {
            Hide(() => OnDismissedBySwipe?.Invoke());
        }
        else
        {
            if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
            activeAnimRoutine = StartCoroutine(SpringBackRoutine());
        }
    }

    private IEnumerator SpringBackRoutine()
    {
        IsAnimating = true;
        Vector2 startPos = cardRect.anchoredPosition;
        float elapsed = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = slideInCurve.Evaluate(t);

            cardRect.anchoredPosition = Vector2.LerpUnclamped(startPos, visiblePos, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, curveT);
            yield return null;
        }

        cardRect.anchoredPosition = visiblePos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        IsAnimating = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // 若拖拽超过阈值，屏蔽点击
        if (dragExceededDeadzone) return;

        OnClicked?.Invoke();
    }
}
