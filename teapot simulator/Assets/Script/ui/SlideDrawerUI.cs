using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 展开大窗口抽屉控制器 (SlideDrawerUI)
/// 负责 600x900 大抽屉窗口的动效、内容绑定（550宽语音条/订单图、标题、正文）及关闭/订单确认交互。
/// </summary>
public class SlideDrawerUI : MonoBehaviour
{
    [Header("=== 核心节点引用 ===")]
    [Tooltip("整个大抽屉的根物体 (SlideDrawer)")]
    public GameObject drawerRoot;

    [Tooltip("大抽屉卡片 RectTransform (600x900)")]
    public RectTransform cardRect;

    [Tooltip("卡片的 CanvasGroup")]
    public CanvasGroup cardCanvasGroup;

    [Tooltip("半透明黑色遮罩 (BackdropMask)")]
    public CanvasGroup backdropMask;

    [Tooltip("背景底图 (600x900)")]
    public Image drawerBgImage;

    [Header("=== 头部标题与关闭 ===")]
    [Tooltip("大抽屉标题文本 (TextMeshProUGUI)")]
    public TextMeshProUGUI titleText;

    [Tooltip("关闭按钮 (带有 exit 图标)")]
    public Button closeButton;

    [Header("=== 正文与图片展示区 ===")]
    [Tooltip("550 宽的主体图片展示 (如语音条、聊天条、跑腿订单展开图等)")]
    public Image contentImage;

    [Tooltip("主体图片的 LayoutElement 或 RectTransform (方便自动适配 550 宽)")]
    public LayoutElement contentImageLayout;

    [Tooltip("详细文字正文 (TextMeshProUGUI)")]
    public TextMeshProUGUI detailText;

    [Header("=== 跑腿订单专用按钮区 ===")]
    [Tooltip("跑腿订单底部确认栏 (平时自动隐藏，订单类型时显示)")]
    public GameObject orderActionArea;

    [Tooltip("订单确认按钮")]
    public Button orderConfirmButton;

    [Tooltip("订单确认按钮文字")]
    public TextMeshProUGUI orderConfirmButtonText;

    [Header("=== 动效参数 ===")]
    [Tooltip("抽屉展开时距离屏幕右边缘的距离")]
    public float marginRight = 0f;

    [Tooltip("滑入动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideInDuration = 0.35f;

    [Tooltip("滑出动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideOutDuration = 0.25f;

    [Tooltip("遮罩最大不透明度")]
    [Range(0.1f, 1f)] public float backdropMaxAlpha = 0.5f;

    [Tooltip("滑入曲线")]
    public AnimationCurve slideInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("滑出曲线")]
    public AnimationCurve slideOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    // 事件
    public event Action OnCloseRequested;
    public event Action OnOrderConfirmed;

    public bool IsVisible { get; private set; } = false;
    public bool IsAnimating { get; private set; } = false;

    private Vector2 visiblePos;
    private Vector2 hiddenPos;
    private float cardWidth = 600f;
    private Coroutine activeAnimRoutine;

    private void Awake()
    {
        Initialize();

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(() => OnCloseRequested?.Invoke());
        }

        if (orderConfirmButton != null)
        {
            orderConfirmButton.onClick.AddListener(() => OnOrderConfirmed?.Invoke());
        }

        // 点击遮罩也可以快速收回抽屉
        if (backdropMask != null)
        {
            Button maskBtn = backdropMask.GetComponent<Button>();
            if (maskBtn != null)
            {
                maskBtn.onClick.AddListener(() => OnCloseRequested?.Invoke());
            }
        }
    }

    public void Initialize()
    {
        if (drawerRoot == null) drawerRoot = gameObject;
        if (cardRect == null) cardRect = GetComponent<RectTransform>();

        if (cardCanvasGroup == null && cardRect != null)
        {
            cardCanvasGroup = cardRect.GetComponent<CanvasGroup>();
            if (cardCanvasGroup == null) cardCanvasGroup = cardRect.gameObject.AddComponent<CanvasGroup>();
        }

        // 统一设为 Middle-Right 锚点
        if (cardRect != null)
        {
            cardRect.anchorMin = new Vector2(1f, 0.5f);
            cardRect.anchorMax = new Vector2(1f, 0.5f);
            cardRect.pivot = new Vector2(1f, 0.5f);

            cardWidth = Mathf.Max(cardRect.rect.width, 300f);
            visiblePos = new Vector2(-marginRight, 0f);
            hiddenPos = new Vector2(cardWidth + 100f, 0f);
        }
    }

    /// <summary>
    /// 绑定数据
    /// </summary>
    public void BindData(PopupWindowDataSO data, Sprite defaultBg = null)
    {
        if (data == null) return;

        // 1. 标题
        if (titleText != null)
        {
            string t = !string.IsNullOrEmpty(data.drawerTitle) ? data.drawerTitle : data.bannerTitle;
            titleText.text = t ?? "";
        }

        // 2. 背景图
        if (drawerBgImage != null)
        {
            Sprite bg = data.drawerBackground != null ? data.drawerBackground : defaultBg;
            if (bg != null)
            {
                drawerBgImage.sprite = bg;
            }
        }

        // 3. 550 宽主体图片展示 (语音条、质问条、订单展开图)
        if (contentImage != null)
        {
            if (data.drawerContentImage != null)
            {
                contentImage.sprite = data.drawerContentImage;
                contentImage.preserveAspect = true;
                contentImage.gameObject.SetActive(true);
            }
            else
            {
                contentImage.gameObject.SetActive(false);
            }
        }

        // 4. 正文详细文本
        if (detailText != null)
        {
            if (!string.IsNullOrEmpty(data.drawerDetailText))
            {
                detailText.text = data.drawerDetailText;
                detailText.gameObject.SetActive(true);
            }
            else
            {
                detailText.text = "";
                // 若没有文本且有图片，隐藏文本物体避免占位
                if (data.drawerContentImage != null)
                {
                    detailText.gameObject.SetActive(false);
                }
            }
        }

        // 5. 跑腿订单交互栏
        if (orderActionArea != null)
        {
            orderActionArea.SetActive(data.isDeliveryOrder);
            if (data.isDeliveryOrder && orderConfirmButtonText != null)
            {
                orderConfirmButtonText.text = !string.IsNullOrEmpty(data.confirmButtonText) ? data.confirmButtonText : "确认取件";
            }
        }
    }

    /// <summary>
    /// 滑入展开
    /// </summary>
    public void Show()
    {
        Initialize();
        if (drawerRoot != null) drawerRoot.SetActive(true);

        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
        activeAnimRoutine = StartCoroutine(SlideInRoutine());
    }

    /// <summary>
    /// 滑出收回
    /// </summary>
    public void Hide(Action onComplete = null)
    {
        if (!IsVisible && !IsAnimating)
        {
            if (drawerRoot != null) drawerRoot.SetActive(false);
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

        if (cardRect != null) cardRect.anchoredPosition = hiddenPos;
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 0f;
            cardCanvasGroup.blocksRaycasts = false;
            cardCanvasGroup.interactable = false;
        }

        if (backdropMask != null)
        {
            backdropMask.alpha = 0f;
            backdropMask.blocksRaycasts = false;
            backdropMask.interactable = false;
        }

        IsVisible = false;
        IsAnimating = false;

        if (drawerRoot != null) drawerRoot.SetActive(false);
    }

    private IEnumerator SlideInRoutine()
    {
        IsAnimating = true;
        IsVisible = true;

        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.blocksRaycasts = true;
            cardCanvasGroup.interactable = true;
        }

        if (backdropMask != null)
        {
            backdropMask.blocksRaycasts = true;
            backdropMask.interactable = true;
        }

        Vector2 startPos = cardRect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideInDuration);
            float curveT = slideInCurve.Evaluate(t);

            cardRect.anchoredPosition = Vector2.LerpUnclamped(startPos, visiblePos, curveT);
            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(cardCanvasGroup.alpha, 1f, curveT);
            if (backdropMask != null) backdropMask.alpha = Mathf.Lerp(backdropMask.alpha, backdropMaxAlpha, curveT);

            yield return null;
        }

        cardRect.anchoredPosition = visiblePos;
        if (cardCanvasGroup != null) cardCanvasGroup.alpha = 1f;
        if (backdropMask != null) backdropMask.alpha = backdropMaxAlpha;

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
            if (cardCanvasGroup != null) cardCanvasGroup.alpha = Mathf.Lerp(1f, 0f, curveT);
            if (backdropMask != null) backdropMask.alpha = Mathf.Lerp(backdropMaxAlpha, 0f, curveT);

            yield return null;
        }

        cardRect.anchoredPosition = hiddenPos;
        if (cardCanvasGroup != null)
        {
            cardCanvasGroup.alpha = 0f;
            cardCanvasGroup.blocksRaycasts = false;
            cardCanvasGroup.interactable = false;
        }

        if (backdropMask != null)
        {
            backdropMask.alpha = 0f;
            backdropMask.blocksRaycasts = false;
            backdropMask.interactable = false;
        }

        IsVisible = false;
        IsAnimating = false;

        if (drawerRoot != null) drawerRoot.SetActive(false);

        onComplete?.Invoke();
    }
}
