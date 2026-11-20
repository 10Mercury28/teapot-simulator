using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// PatUIController
/// - 管理单个拍泥 Module 的目标线框 (Frame) 与动态手势引导动画 (StartImage / Visual Cue)
/// - 去除多层叠加 Peel Stack 表现，仅保留常驻 Frame 与持续播放的动态 Visual Cue
/// - 点击时提供生动的 Q 弹缩放 (Punch Scale) 与高光闪烁 (Hit Flash) 反馈
/// - 状态驱动（仅显示当前正在交互的模块）：
///     * Inactive: 完全隐藏 (alpha = 0，Frame 与 Visual Cue 均隐藏)
///     * Waiting / Transition: 当前模块的 Frame 与 Visual Cue 暗淡提示 (dimAlpha)
///     * Main Active: 当前模块的 Frame 与 Visual Cue 完全高亮 (brightAlpha = 1.0)，手势持续播放引导
///     * Complete: UI 完全隐藏 (alpha = 0)
/// - 所有图形组件默认 raycastTarget = false，严禁拦截鼠标点击
/// </summary>
public class PatUIController : MonoBehaviour
{
    [Header("=== Canvas Group ===")]
    public CanvasGroup uiGroup;

    [Header("=== 目标线框 (Frame) ===")]
    [Tooltip("拍泥区域目标线框 (frame.png)")]
    public Image frameImage;

    [Header("=== 动态引导动画 (Visual Cue / StartImage) ===")]
    [Tooltip("手部拍打动态引导图像")]
    public Image startImage;
    [Tooltip("手部拍打动画控制器 (若未手动指定将自动获取)")]
    public Animator startAnimator;

    [Header("=== Q 弹缩放 (Punch Scale) ===")]
    [Tooltip("是否启用点击时的 Q 弹缩放效果 (Punch Scale)")]
    public bool enablePunchScale = true;
    [Tooltip("弹性缩放倍率 (默认 1.18x，小于 1 为微缩回弹，大于 1 为放大回弹)")]
    [Range(0.5f, 2f)] public float punchScaleMultiplier = 1.18f;
    [Tooltip("弹性缩放持续时长 (秒)")]
    [Range(0.02f, 0.5f)] public float punchDuration = 0.12f;

    [Header("=== 高光微闪 (Hit Flash) ===")]
    [Tooltip("是否启用点击受击高光微闪 (Hit Flash)")]
    public bool enableHitFlash = true;
    [Tooltip("受击高光颜色 (可调颜色与透明度)")]
    public Color hitFlashColor = new Color(1f, 1f, 0.85f, 1f);
    [Tooltip("受击亮度倍率 (<1 变暗，=1 原亮度，>1 增亮高光)")]
    [Range(0f, 3f)] public float hitFlashBrightness = 1.4f;
    [Tooltip("高光微闪持续时长 (秒)")]
    [Range(0.01f, 0.5f)] public float hitFlashDuration = 0.06f;

    [Header("=== 额外点击特效图 (可选) ===")]
    [Tooltip("可选的额外受击贴图显示 (如点击特效/水花等)")]
    public Image clickFeedbackImage;
    public Sprite clickSprite;

    [Header("=== 透明度设置 ===")]
    [Range(0f, 1f)] public float dimAlpha = 0.2f;
    [Range(0f, 1f)] public float brightAlpha = 1.0f;

    [Header("=== 旧叠层兼容字段 (已废弃，保留序列化避免报错) ===")]
    [HideInInspector] public Transform stackRoot;
    [HideInInspector] public List<Sprite> stackSprites = new List<Sprite>();

    // 内部状态
    private List<Image> spawnedImages = new List<Image>();
    private Vector3 originalStartScale = Vector3.one;
    private Vector3 originalFrameScale = Vector3.one;
    private Color originalFrameColor = Color.white;
    private Color originalStartColor = Color.white;
    private bool scalesRecorded = false;

    private Material frameFlashMat;
    private Material startFlashMat;

    private Coroutine punchCoroutine;
    private Coroutine flashCoroutine;
    private Coroutine clickFlashCoroutine;

    private void Awake()
    {
        AutoBindComponents();
        EnsureNoRaycast();
        RecordOriginalState();
        SetInactive();
    }

    private void OnDisable()
    {
        punchCoroutine = null;
        flashCoroutine = null;
        clickFlashCoroutine = null;

        if (scalesRecorded)
        {
            ResetScales();
            ResetColors();
        }
    }

    private void OnDestroy()
    {
        if (frameImage != null && frameFlashMat != null && frameImage.material == frameFlashMat)
        {
            frameImage.material = null;
        }
        if (startImage != null && startFlashMat != null && startImage.material == startFlashMat)
        {
            startImage.material = null;
        }

        if (frameFlashMat != null)
        {
            Destroy(frameFlashMat);
        }
        if (startFlashMat != null && startFlashMat != frameFlashMat)
        {
            Destroy(startFlashMat);
        }

        frameFlashMat = null;
        startFlashMat = null;
    }

    /// <summary>
    /// 自动在子节点中搜寻并关联缺失的 UI 引用
    /// </summary>
    public void AutoBindComponents()
    {
        if (uiGroup == null)
        {
            uiGroup = GetComponentInChildren<CanvasGroup>(true);
        }

        // 查找 startImage 和 animator
        if (startImage == null)
        {
            Transform startTrans = transform.Find("Ui_M/StartImage") ?? transform.Find("StartImage");
            if (startTrans != null) startImage = startTrans.GetComponent<Image>();
        }

        if (startAnimator == null && startImage != null)
        {
            startAnimator = startImage.GetComponent<Animator>();
        }

        // 查找 frameImage：优先查找 Frame，若未找到但 clickFeedbackImage 引用存在（历史上指向 frame.png 的物体），则绑定
        if (frameImage == null)
        {
            Transform frameTrans = transform.Find("Ui_M/Frame") ?? transform.Find("Frame");
            if (frameTrans != null)
            {
                frameImage = frameTrans.GetComponent<Image>();
            }
            else if (clickFeedbackImage != null)
            {
                frameImage = clickFeedbackImage;
            }
            else
            {
                Transform clickTrans = transform.Find("Ui_M/ClickFeedback") ?? transform.Find("ClickFeedback");
                if (clickTrans != null) frameImage = clickTrans.GetComponent<Image>();
            }
        }
    }

    private void RecordOriginalState()
    {
        if (scalesRecorded) return;

        if (startImage != null)
        {
            originalStartScale = startImage.rectTransform.localScale;
            // 确保颜色基础 alpha 为 1.0，由 CanvasGroup 统领透明度
            Color c = startImage.color;
            c.a = 1f;
            startImage.color = c;
            originalStartColor = c;
        }

        if (frameImage != null)
        {
            originalFrameScale = frameImage.rectTransform.localScale;
            Color c = frameImage.color;
            c.a = 1f;
            frameImage.color = c;
            originalFrameColor = c;
        }

        scalesRecorded = true;
    }

    // ===================== 状态控制 =====================

    /// <summary>
    /// 未激活待机状态：完全隐藏（仅展示当前正在交互的模块）
    /// </summary>
    public void SetInactive()
    {
        HideUI();
    }

    /// <summary>
    /// 准备/过渡状态 (Dim)：Frame 与 Visual Cue 均暗淡提示
    /// </summary>
    public void ShowStart()
    {
        RecordOriginalState();
        ClearStack();

        if (frameImage != null)
        {
            frameImage.enabled = true;
            frameImage.raycastTarget = false;
        }

        if (startImage != null)
        {
            startImage.enabled = true;
            startImage.raycastTarget = false;
            if (startAnimator != null && !startAnimator.enabled)
            {
                startAnimator.enabled = true;
            }
        }

        SetAlpha(dimAlpha);
    }

    /// <summary>
    /// 主拍打阶段开始：Frame 与 Visual Cue 高亮显示，手势持续播放引导
    /// （已废弃生成多层 stack，保留接口签名以完全兼容 PatModuleUI）
    /// </summary>
    public void BuildStack(int count)
    {
        RecordOriginalState();
        ClearStack();

        if (frameImage != null)
        {
            frameImage.enabled = true;
            frameImage.raycastTarget = false;
        }

        // 核心修改：保持 Visual Cue 持续显示与播放，绝不关闭！
        if (startImage != null)
        {
            startImage.enabled = true;
            startImage.raycastTarget = false;
            if (startAnimator != null && !startAnimator.enabled)
            {
                startAnimator.enabled = true;
            }
        }

        SetAlpha(brightAlpha);
    }

    /// <summary>
    /// 每次有效拍中时调用：触发受击弹性缩放 (Punch Scale) 与高光闪烁 (Hit Flash)
    /// </summary>
    public void OnHit()
    {
        if (!isActiveAndEnabled) return;

        RecordOriginalState();

        // 1. 弹性受击缩放动效
        if (enablePunchScale)
        {
            if (punchCoroutine != null) StopCoroutine(punchCoroutine);
            punchCoroutine = StartCoroutine(PunchScaleRoutine());
        }

        // 2. 受击高光闪烁
        if (enableHitFlash)
        {
            if (flashCoroutine != null) StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(HitFlashRoutine());
        }

        // 3. 额外独立特效图闪烁（若配置且不与 frameImage 冲突）
        if (clickFeedbackImage != null && clickFeedbackImage != frameImage && clickSprite != null)
        {
            if (clickFlashCoroutine != null) StopCoroutine(clickFlashCoroutine);
            clickFlashCoroutine = StartCoroutine(FlashClickFeedback());
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
    /// Module 完成时调用：UI 彻底隐藏并释放
    /// </summary>
    public void HideOnComplete()
    {
        HideUI();
    }

    private void HideUI()
    {
        if (punchCoroutine != null)
        {
            StopCoroutine(punchCoroutine);
            punchCoroutine = null;
        }

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        if (clickFlashCoroutine != null)
        {
            StopCoroutine(clickFlashCoroutine);
            clickFlashCoroutine = null;
        }

        if (scalesRecorded)
        {
            ResetScales();
            ResetColors();
        }

        ClearStack();
        SetAlpha(0f);

        if (frameImage != null) frameImage.enabled = false;
        if (startImage != null) startImage.enabled = false;
        if (clickFeedbackImage != null) clickFeedbackImage.enabled = false;
    }

    private void SetAlpha(float a)
    {
        if (!uiGroup) return;
        uiGroup.alpha = a;
        uiGroup.blocksRaycasts = false;
        uiGroup.interactable = false;
    }

    // ===================== 动画动效协程 =====================

    private IEnumerator PunchScaleRoutine()
    {
        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            // 0 -> 1 -> 0 弹性曲线 (正弦曲线)
            float factor = 1f + Mathf.Sin(t * Mathf.PI) * (punchScaleMultiplier - 1f);

            if (startImage != null)
                startImage.rectTransform.localScale = originalStartScale * factor;
            if (frameImage != null)
                frameImage.rectTransform.localScale = originalFrameScale * factor;

            yield return null;
        }

        ResetScales();
        punchCoroutine = null;
    }

    private IEnumerator HitFlashRoutine()
    {
        Color appliedColor = new Color(
            hitFlashColor.r * hitFlashBrightness,
            hitFlashColor.g * hitFlashBrightness,
            hitFlashColor.b * hitFlashBrightness,
            hitFlashColor.a
        );

        ApplyFlashColor(appliedColor);

        yield return new WaitForSeconds(hitFlashDuration);

        ResetColors();
        flashCoroutine = null;
    }

    private IEnumerator FlashClickFeedback()
    {
        if (!clickFeedbackImage || !clickSprite) yield break;

        clickFeedbackImage.sprite = clickSprite;
        clickFeedbackImage.enabled = true;
        clickFeedbackImage.raycastTarget = false;

        yield return new WaitForSeconds(0.12f);

        clickFeedbackImage.enabled = false;
        clickFlashCoroutine = null;
    }

    private Material CreateFlashMaterialInstance(Image img)
    {
        if (img == null) return null;
        Material baseMat = img.material;
        if (baseMat == null || !baseMat.HasProperty("_Color")) return null;

        Material matInstance = new Material(baseMat)
        {
            hideFlags = HideFlags.DontSave
        };
        img.material = matInstance;
        return matInstance;
    }

    private void EnsureFlashMaterials()
    {
        if (frameImage != null && frameFlashMat == null)
        {
            frameFlashMat = CreateFlashMaterialInstance(frameImage);
        }

        if (startImage != null && startFlashMat == null)
        {
            if (startImage == frameImage && frameFlashMat != null)
            {
                startFlashMat = frameFlashMat;
            }
            else
            {
                startFlashMat = CreateFlashMaterialInstance(startImage);
            }
        }
    }

    private void ApplyFlashColor(Color appliedColor)
    {
        EnsureFlashMaterials();
        ApplyColorToImage(frameImage, frameFlashMat, appliedColor);
        ApplyColorToImage(startImage, startFlashMat, appliedColor);
    }

    private void ApplyColorToImage(Image img, Material flashMat, Color color)
    {
        if (img == null) return;
        if (flashMat != null)
        {
            flashMat.color = color;
        }
        else
        {
            img.color = color;
        }
    }

    private void ResetScales()
    {
        if (startImage != null) startImage.rectTransform.localScale = originalStartScale;
        if (frameImage != null) frameImage.rectTransform.localScale = originalFrameScale;
    }

    private void ResetColors()
    {
        if (frameFlashMat != null) frameFlashMat.color = Color.white;
        if (startFlashMat != null) startFlashMat.color = Color.white;

        if (frameImage != null) frameImage.color = originalFrameColor;
        if (startImage != null) startImage.color = originalStartColor;
    }

    private void ClearStack()
    {
        foreach (var img in spawnedImages)
        {
            if (img != null) Destroy(img.gameObject);
        }
        spawnedImages.Clear();
    }

    private void EnsureNoRaycast()
    {
        if (uiGroup != null)
        {
            uiGroup.blocksRaycasts = false;
            uiGroup.interactable = false;
        }

        if (startImage != null) startImage.raycastTarget = false;
        if (frameImage != null) frameImage.raycastTarget = false;
        if (clickFeedbackImage != null) clickFeedbackImage.raycastTarget = false;
    }
}
