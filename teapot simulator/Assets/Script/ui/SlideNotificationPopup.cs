using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// 右侧滑入式交互弹窗 (Slide-in Notification Popup)
/// 核心特性：
/// 1. 从屏幕右侧外平滑滑入到屏幕右中位置；
/// 2. 支持玩家按住向右拖拽划走 (Swipe to Dismiss)，带松手回弹与划走阈值判定；
/// 3. 支持是否自动隐藏 (autoHide 开关与 autoHideDuration 倒计时)；
/// 4. 完美兼顾 Button 点击跳转交互与拖拽手势，滑动时自动防误触点击；
/// 5. 顶层 Canvas (SortingOrder = 1500) 确保浮在所有视频和UI之上；
/// 6. 使用 TextMeshProUGUI 显示排版文字。
/// </summary>
public class SlideNotificationPopup : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler, IPointerDownHandler
{
    public static SlideNotificationPopup Instance { get; private set; }

    [Header("=== UI 组件引用 ===")]
    [Tooltip("弹窗卡片的 RectTransform (停靠在右中 Middle-Right)")]
    public RectTransform popupCard;

    [Tooltip("弹窗主配图/图标")]
    public Image iconImage;

    [Tooltip("标题文字 (TextMeshProUGUI)")]
    public TextMeshProUGUI titleText;

    [Tooltip("内容正文 (TextMeshProUGUI)")]
    public TextMeshProUGUI contentText;

    [Tooltip("卡片的 CanvasGroup (用于淡入淡出及遮罩拦截)")]
    public CanvasGroup canvasGroup;

    [Header("=== 停靠与动效参数 (屏幕右中) ===")]
    [Tooltip("展开可见时距离屏幕右边缘的边距 (像素)")]
    public float marginRight = 30f;

    [Tooltip("滑入动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideInDuration = 0.35f;

    [Tooltip("滑出动画时长 (秒)")]
    [Range(0.1f, 1f)] public float slideOutDuration = 0.25f;

    [Tooltip("滑入动画曲线")]
    public AnimationCurve slideInCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Tooltip("滑出动画曲线")]
    public AnimationCurve slideOutCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("=== 自动隐藏设置 ===")]
    [Tooltip("是否在滑入后自动倒计时隐藏 (True: n秒后自动滑走; False: 必须玩家点击或划走才消失)")]
    public bool autoHide = true;

    [Tooltip("自动隐藏等待时间 (秒)")]
    [Range(1f, 30f)] public float autoHideDuration = 5f;

    [Header("=== 划走手势判定 (Swipe to Dismiss) ===")]
    [Tooltip("向右拖拽超过卡片宽度的多少百分比算作划走成功 (0.1 ~ 0.8)")]
    [Range(0.1f, 0.8f)] public float dismissThresholdPercent = 0.35f;

    [Tooltip("拖拽判定死区 (超过此像素位移才认定为滑动，避免点击误判为拖拽)")]
    public float dragDeadzone = 12f;

    [Header("=== 点击与跳转交互 ===")]
    [Tooltip("点击弹窗后需要跳转的目标场景名 (可选，留空则不切场景)")]
    public string targetSceneName;

    [Tooltip("切场景时是否使用闭眼转场 (SceneBlinkTransition)")]
    public bool useBlinkTransition = true;

    [Tooltip("点击弹窗时触发的 UnityEvent")]
    public UnityEvent onPopupClicked;

    [Tooltip("弹窗被隐藏/划走时触发的 UnityEvent")]
    public UnityEvent onPopupDismissed;

    [Header("=== 调试功能 ===")]
    [Tooltip("启动游戏时自动弹出演示")]
    public bool showOnStart = false;

    [Tooltip("测试快捷键 (运行模式下按此键触发弹出/收回)")]
    public KeyCode debugToggleKey = KeyCode.T;

    // 状态数据
    public bool IsVisible { get; private set; } = false;
    public bool IsAnimating { get; private set; } = false;

    private Vector2 visibleAnchoredPos;
    private Vector2 hiddenAnchoredPos;

    private Coroutine activeAnimRoutine;
    private Coroutine autoHideRoutine;
    private Action runtimeClickAction;

    // 拖拽相关
    private bool isDragging = false;
    private bool dragExceededDeadzone = false;
    private Vector2 pointerDownPos;
    private Vector2 dragStartCardPos;
    private float cardWidth = 360f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning($"⚠️ [SlideNotificationPopup] 场景中存在重复实例，销毁旧对象: {gameObject.name}");
        }

        InitializePositions();
    }

    private void Start()
    {
        // 确保启动时处于屏幕外隐藏状态
        SnapToHidden();

        if (showOnStart)
        {
            Show();
        }
    }

    private void Update()
    {
        // 快捷键调试
        if (debugToggleKey != KeyCode.None && Input.GetKeyDown(debugToggleKey))
        {
            if (IsVisible) Hide();
            else Show();
        }
    }

    /// <summary>
    /// 初始化计算右中对齐的可见位置与屏幕外隐藏位置
    /// </summary>
    public void InitializePositions()
    {
        if (popupCard == null)
        {
            popupCard = GetComponent<RectTransform>();
        }

        if (canvasGroup == null)
        {
            canvasGroup = popupCard.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = popupCard.gameObject.AddComponent<CanvasGroup>();
            }
        }

        // 统一设为 Middle-Right 锚点
        popupCard.anchorMin = new Vector2(1f, 0.5f);
        popupCard.anchorMax = new Vector2(1f, 0.5f);
        popupCard.pivot = new Vector2(1f, 0.5f);

        cardWidth = Mathf.Max(popupCard.rect.width, 100f);

        // 可见停靠位置：贴紧屏幕右边缘，留出 marginRight 间距
        visibleAnchoredPos = new Vector2(-marginRight, 0f);

        // 隐藏位置：完全缩进屏幕右侧外 + 60 像素缓冲
        hiddenAnchoredPos = new Vector2(cardWidth + 60f, 0f);
    }

    private void SnapToHidden()
    {
        InitializePositions();
        popupCard.anchoredPosition = hiddenAnchoredPos;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
        IsVisible = false;
        IsAnimating = false;
    }

    // ============================================================
    // 公共 API：唤出与收起
    // ============================================================

    /// <summary>
    /// 唤出弹窗并更新图文内容
    /// </summary>
    public void Show(
        Sprite image = null, 
        string title = null, 
        string content = null, 
        bool? overrideAutoHide = null, 
        float? overrideDuration = null, 
        Action onClick = null, 
        string sceneToLoad = null)
    {
        // 1. 设置内容
        if (image != null && iconImage != null)
        {
            iconImage.sprite = image;
            iconImage.gameObject.SetActive(true);
        }

        if (title != null && titleText != null)
        {
            titleText.text = title;
        }

        if (content != null && contentText != null)
        {
            contentText.text = content;
        }

        if (overrideAutoHide.HasValue)
        {
            autoHide = overrideAutoHide.Value;
        }

        if (overrideDuration.HasValue)
        {
            autoHideDuration = overrideDuration.Value;
        }

        runtimeClickAction = onClick;

        if (!string.IsNullOrEmpty(sceneToLoad))
        {
            targetSceneName = sceneToLoad;
        }

        // 2. 动画滑入
        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
        if (autoHideRoutine != null) StopCoroutine(autoHideRoutine);

        activeAnimRoutine = StartCoroutine(SlideInRoutine());
    }

    /// <summary>
    /// 收回并滑出屏幕右侧
    /// </summary>
    public void Hide()
    {
        if (!IsVisible && !IsAnimating) return;

        if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
        if (autoHideRoutine != null) StopCoroutine(autoHideRoutine);

        activeAnimRoutine = StartCoroutine(SlideOutRoutine());
    }

    // ============================================================
    // 滑入 / 滑出协程
    // ============================================================

    private IEnumerator SlideInRoutine()
    {
        IsAnimating = true;
        IsVisible = true;

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        Vector2 startPos = popupCard.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideInDuration);
            float curveT = slideInCurve.Evaluate(t);

            popupCard.anchoredPosition = Vector2.LerpUnclamped(startPos, visibleAnchoredPos, curveT);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, curveT);
            }

            yield return null;
        }

        popupCard.anchoredPosition = visibleAnchoredPos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        IsAnimating = false;

        // 启动自动隐藏倒计时
        if (autoHide)
        {
            autoHideRoutine = StartCoroutine(AutoHideCountdownRoutine());
        }
    }

    private IEnumerator SlideOutRoutine()
    {
        IsAnimating = true;

        Vector2 startPos = popupCard.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < slideOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / slideOutDuration);
            float curveT = slideOutCurve.Evaluate(t);

            popupCard.anchoredPosition = Vector2.LerpUnclamped(startPos, hiddenAnchoredPos, curveT);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, curveT);
            }

            yield return null;
        }

        popupCard.anchoredPosition = hiddenAnchoredPos;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        IsVisible = false;
        IsAnimating = false;

        onPopupDismissed?.Invoke();
    }

    private IEnumerator AutoHideCountdownRoutine()
    {
        float timer = autoHideDuration;
        while (timer > 0f)
        {
            // 如果玩家正在按住拖拽，暂停倒计时
            if (!isDragging)
            {
                timer -= Time.unscaledDeltaTime;
            }
            yield return null;
        }

        Hide();
    }

    // ============================================================
    // 拖拽手势交互 (Swipe to Dismiss 向右划走)
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
        dragStartCardPos = popupCard.anchoredPosition;

        // 暂停自动隐藏倒计时
        if (autoHideRoutine != null)
        {
            StopCoroutine(autoHideRoutine);
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging) return;

        float deltaX = eventData.position.x - pointerDownPos.x;

        if (Mathf.Abs(deltaX) > dragDeadzone)
        {
            dragExceededDeadzone = true;
        }

        // 核心约束：仅允许向右划出屏幕（不能反向往左拉入屏幕正中）
        float allowedX = Mathf.Max(visibleAnchoredPos.x, dragStartCardPos.x + deltaX);
        popupCard.anchoredPosition = new Vector2(allowedX, visibleAnchoredPos.y);

        // 拖得越远，略微淡化卡片透明度提供反馈
        if (canvasGroup != null)
        {
            float dragProgress = Mathf.Clamp01((allowedX - visibleAnchoredPos.x) / (cardWidth * 0.8f));
            canvasGroup.alpha = Mathf.Lerp(1f, 0.4f, dragProgress);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!isDragging) return;
        isDragging = false;

        float draggedDistanceX = popupCard.anchoredPosition.x - visibleAnchoredPos.x;
        float thresholdDistance = cardWidth * dismissThresholdPercent;

        // 向右滑动的速度较快，或者滑出距离超过阈值 -> 判定划走成功
        bool shouldDismiss = draggedDistanceX >= thresholdDistance || eventData.delta.x > 8f;

        if (shouldDismiss)
        {
            Hide();
        }
        else
        {
            // 未达到划走阈值：弹回居中停靠位置
            if (activeAnimRoutine != null) StopCoroutine(activeAnimRoutine);
            activeAnimRoutine = StartCoroutine(SpringBackRoutine());
        }
    }

    private IEnumerator SpringBackRoutine()
    {
        IsAnimating = true;
        Vector2 startPos = popupCard.anchoredPosition;
        float elapsed = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float curveT = slideInCurve.Evaluate(t);

            popupCard.anchoredPosition = Vector2.LerpUnclamped(startPos, visibleAnchoredPos, curveT);
            if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, 1f, curveT);
            yield return null;
        }

        popupCard.anchoredPosition = visibleAnchoredPos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        IsAnimating = false;

        // 恢复自动隐藏倒计时
        if (autoHide)
        {
            if (autoHideRoutine != null) StopCoroutine(autoHideRoutine);
            autoHideRoutine = StartCoroutine(AutoHideCountdownRoutine());
        }
    }

    // ============================================================
    // 点击与跳转处理 (防误触)
    // ============================================================

    public void OnPointerClick(PointerEventData eventData)
    {
        // 如果发生了有效拖拽划动，屏蔽点击，防止误触发跳转
        if (dragExceededDeadzone)
        {
            return;
        }

        Debug.Log("🎯 [SlideNotificationPopup] 弹窗被点击触发！");

        // 1. 触发外部绑定的 UnityEvent
        onPopupClicked?.Invoke();

        // 2. 触发代码传入的 Action 回调
        runtimeClickAction?.Invoke();

        // 3. 执行目标场景跳转
        if (!string.IsNullOrEmpty(targetSceneName))
        {
            ExecuteSceneJump(targetSceneName);
        }
    }

    private void ExecuteSceneJump(string sceneName)
    {
        Debug.Log($"🚀 [SlideNotificationPopup] 正在跳转到场景: '{sceneName}'");

        if (useBlinkTransition && SceneBlinkTransition.Instance != null)
        {
            SceneBlinkTransition.Instance.LoadSceneConcealed(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
