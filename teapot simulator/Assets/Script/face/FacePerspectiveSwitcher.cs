using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceDetector;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;
using Image = UnityEngine.UI.Image;
using Color = UnityEngine.Color;
using Rect = UnityEngine.Rect;

/// <summary>
/// 人脸观察视角图片切换器 (UI Canvas Overlay 架构)
/// 核心功能：
/// 1. 调取 WebCam 摄像头并通过 MediaPipe BlazeFace 进行人脸检测与水平位置追踪；
/// 2. 运算人脸相对于屏幕中心的偏角 (左侧视角 / 正面视角 / 右侧视角)；
/// 3. 驱动 UI Image 平滑切换展示对应的视角图片；
/// 4. 包含平滑缓动滤波 (Lerp) 与滞后防抖区间 (Hysteresis)；
/// 5. 摄像头预览镜像与方向反转校正；
/// 6. 提供按键模拟模式 (方向键 ← ↓ →) 供无摄像头时离线调试。
/// </summary>
public class FacePerspectiveSwitcher : MonoBehaviour
{
    public enum ViewingPerspective
    {
        None,   // 未检测到人脸
        Left,   // 观察者偏左侧
        Center, // 观察者正视中心
        Right   // 观察者偏右侧
    }

    [Header("=== UI 显示层 (Canvas Image) ===")]
    [Tooltip("主显示 UI Image，用于展示当前视角的图片")]
    public Image displayImage;

    [Tooltip("左视态图片：当人从左侧观察屏幕时展示")]
    public Sprite leftSprite;

    [Tooltip("正视态图片：当人在正中直视屏幕时展示")]
    public Sprite centerSprite;

    [Tooltip("右视态图片：当人从右侧观察屏幕时展示")]
    public Sprite rightSprite;

    [Tooltip("丢失人脸时的候补图片 (可选，留空则保持最后一次有效视角)")]
    public Sprite fallbackSprite;

    [Tooltip("切换图片时是否使用平滑淡入淡出")]
    public bool enableFadeTransition = true;

    [Tooltip("淡入淡出时长 (秒)")]
    [Range(0.05f, 0.5f)] public float fadeDuration = 0.18f;

    [Header("=== 视角判定与方向校正 ===")]
    [Tooltip("屏幕正中死区半宽 (相对中心偏差在 ±此阈值 内均判定为正中视角)")]
    [Range(0.04f, 0.35f)] public float centerDeadzoneHalfWidth = 0.10f;

    [Tooltip("防抖滞后区间 (离开当前状态所需额外位移，防止在临界点反复抽搐)")]
    [Range(0.01f, 0.08f)] public float hysteresis = 0.03f;

    [Tooltip("人脸位置滤波平滑速度 (越大人脸跟随越快，越小越平滑防抖)")]
    [Range(2f, 30f)] public float smoothSpeed = 12f;

    [Tooltip("算法镜像反转 (将摄像头视角转化为自拍镜面视角)")]
    public bool mirrorCamera = true;

    [Tooltip("反转左右判定方向 (如果人头偏左却判定成了右，勾选此项即可瞬间翻转左右)")]
    public bool invertDirection = false;

    [Tooltip("多久执行一次人脸检测 (秒，默认 0.04s 约 25FPS)")]
    [Range(0.02f, 0.15f)] public float detectionInterval = 0.04f;

    [Header("=== 摄像头设置 ===")]
    public int requestedWebCamWidth = 640;
    public int requestedWebCamHeight = 480;
    public int requestedWebCamFPS = 30;

    [Header("=== 调试预览与键盘模拟 ===")]
    [Tooltip("是否在调试小窗口中将摄像头画面做水平镜像（像镜子一样直观）")]
    public bool mirrorPreviewWindow = true;

    [Tooltip("调试预览摄像头的 RawImage (可选，留空则不显示摄像头窗口)")]
    public RawImage debugCameraPreview;

    [Tooltip("调试状态显示文本 (可选)")]
    public Text debugStatusText;

    [Tooltip("开启后可按键盘方向键模拟视角：←(左), ↓(中), →(右), 空格(清除人脸)")]
    public bool enableKeyboardSimulation = true;

    // 当前与平滑后的状态数据
    public ViewingPerspective CurrentPerspective { get; private set; } = ViewingPerspective.Center;
    public float SmoothedFaceOffsetX { get; private set; } = 0f; // -0.5 (最左) ~ +0.5 (最右)
    public bool IsFaceDetected { get; private set; } = false;

    // 内部组件与运行时
    private WebCamTexture webCamTexture;
    private TextureFrame textureFrame;
    private FaceDetector faceDetector;
    private Coroutine detectionLoopRoutine;
    private Coroutine fadeRoutine;

    private float rawTargetOffset = 0f;
    private float lastDetectionTime = 0f;
    private bool isMediaPipeReady = false;
    private bool isSimulatingWithKeyboard = false;

    private void Start()
    {
        // 校验显示组件
        if (displayImage == null)
        {
            displayImage = GetComponentInChildren<Image>();
            if (displayImage == null)
            {
                Debug.LogWarning("⚠️ [FacePerspectiveSwitcher] 未分配 DisplayImage，请在 Inspector 中拖入目标 UI Image！");
            }
        }

        // 初始展示正中图片
        ApplyPerspective(ViewingPerspective.Center, instant: true);

        // 启动摄像头与 MediaPipe
        StartCoroutine(InitializePipelineRoutine());
    }

    private void Update()
    {
        // 1. 同步预览小窗口镜像
        if (debugCameraPreview != null)
        {
            Vector3 s = debugCameraPreview.rectTransform.localScale;
            float desiredScaleX = mirrorPreviewWindow ? -Mathf.Abs(s.x) : Mathf.Abs(s.x);
            if (!Mathf.Approximately(s.x, desiredScaleX))
            {
                s.x = desiredScaleX;
                debugCameraPreview.rectTransform.localScale = s;
            }
        }

        // 2. 键盘模拟输入 (方便日常调试，不插摄像头也能随时测试)
        if (enableKeyboardSimulation)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow))
            {
                SimulateFaceOffset(-0.25f);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                SimulateFaceOffset(0f);
            }
            else if (Input.GetKeyDown(KeyCode.RightArrow))
            {
                SimulateFaceOffset(0.25f);
            }
            else if (Input.GetKeyDown(KeyCode.Space))
            {
                isSimulatingWithKeyboard = false;
                IsFaceDetected = false;
                if (fallbackSprite != null)
                {
                    ApplyPerspective(ViewingPerspective.None, instant: false);
                }
            }
        }

        // 3. 针对人脸相对屏幕中心偏离值的平滑滤波 (Lerp)
        if (IsFaceDetected)
        {
            SmoothedFaceOffsetX = Mathf.Lerp(SmoothedFaceOffsetX, rawTargetOffset, Time.deltaTime * smoothSpeed);
            UpdatePerspectiveFromOffset(SmoothedFaceOffsetX);
        }

        // 4. 调试信息刷新
        UpdateDebugUI();
    }

    private void SimulateFaceOffset(float offset)
    {
        isSimulatingWithKeyboard = true;
        IsFaceDetected = true;
        rawTargetOffset = offset;
        SmoothedFaceOffsetX = offset;
        UpdatePerspectiveFromOffset(SmoothedFaceOffsetX);
        Debug.Log($"🕹️ [FacePerspectiveSwitcher] 键盘模拟视角偏移: {offset:F2} -> {CurrentPerspective}");
    }

    /// <summary>
    /// 根据当前偏移量与滞后区间计算视角状态
    /// </summary>
    private void UpdatePerspectiveFromOffset(float offset)
    {
        ViewingPerspective newPerspective = CurrentPerspective;

        float leftThreshold = -centerDeadzoneHalfWidth;
        float rightThreshold = centerDeadzoneHalfWidth;

        // 加入滞后区间 (Hysteresis) 确保临界点不抖动
        if (CurrentPerspective == ViewingPerspective.Left)
        {
            if (offset > leftThreshold + hysteresis)
            {
                newPerspective = (offset > rightThreshold) ? ViewingPerspective.Right : ViewingPerspective.Center;
            }
        }
        else if (CurrentPerspective == ViewingPerspective.Right)
        {
            if (offset < rightThreshold - hysteresis)
            {
                newPerspective = (offset < leftThreshold) ? ViewingPerspective.Left : ViewingPerspective.Center;
            }
        }
        else // 当前为 Center 或 None
        {
            if (offset < leftThreshold - hysteresis)
            {
                newPerspective = ViewingPerspective.Left;
            }
            else if (offset > rightThreshold + hysteresis)
            {
                newPerspective = ViewingPerspective.Right;
            }
            else
            {
                newPerspective = ViewingPerspective.Center;
            }
        }

        if (newPerspective != CurrentPerspective)
        {
            ApplyPerspective(newPerspective, instant: !enableFadeTransition);
        }
    }

    /// <summary>
    /// 应用并切换当前视角的图片
    /// </summary>
    private void ApplyPerspective(ViewingPerspective perspective, bool instant)
    {
        CurrentPerspective = perspective;

        Sprite targetSprite = null;
        switch (perspective)
        {
            case ViewingPerspective.Left:
                targetSprite = leftSprite != null ? leftSprite : centerSprite;
                break;
            case ViewingPerspective.Center:
                targetSprite = centerSprite;
                break;
            case ViewingPerspective.Right:
                targetSprite = rightSprite != null ? rightSprite : centerSprite;
                break;
            case ViewingPerspective.None:
                targetSprite = fallbackSprite != null ? fallbackSprite : centerSprite;
                break;
        }

        if (displayImage == null || targetSprite == null) return;
        if (displayImage.sprite == targetSprite) return; // 已经展示该图片，无需重复切换

        Debug.Log($"🖼️ [FacePerspectiveSwitcher] 切换视角图片: {perspective} (Sprite: {targetSprite.name})");

        if (instant || !gameObject.activeInHierarchy)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            displayImage.sprite = targetSprite;
            Color c = displayImage.color;
            c.a = 1f;
            displayImage.color = c;
        }
        else
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(CrossFadeSpriteRoutine(targetSprite, fadeDuration));
        }
    }

    private IEnumerator CrossFadeSpriteRoutine(Sprite newSprite, float duration)
    {
        if (displayImage == null) yield break;

        // 平滑淡出
        float halfDuration = duration * 0.5f;
        float t = 0f;
        Color initialColor = displayImage.color;

        while (t < halfDuration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0.2f, t / halfDuration);
            initialColor.a = alpha;
            displayImage.color = initialColor;
            yield return null;
        }

        // 换图
        displayImage.sprite = newSprite;

        // 平滑淡入
        t = 0f;
        while (t < halfDuration)
        {
            t += Time.deltaTime;
            float alpha = Mathf.Lerp(0.2f, 1f, t / halfDuration);
            initialColor.a = alpha;
            displayImage.color = initialColor;
            yield return null;
        }

        initialColor.a = 1f;
        displayImage.color = initialColor;
    }

    // ============================================================
    // MediaPipe 与 WebCam 初始化流程
    // ============================================================

    private IEnumerator InitializePipelineRoutine()
    {
        // 1. 检查摄像头硬件设备
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogWarning("⚠️ [FacePerspectiveSwitcher] 未检测到摄像头设备，已自动降级为【键盘方向键模拟测试模式】。");
            yield break;
        }

        var device = WebCamTexture.devices[0];
        webCamTexture = new WebCamTexture(device.name, requestedWebCamWidth, requestedWebCamHeight, requestedWebCamFPS);
        webCamTexture.Play();

        // 等待 WebCam 数据有效 (Mac / Win 初始需等待几帧)
        float timeout = 4f;
        while (webCamTexture.width <= 16 && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (webCamTexture.width <= 16)
        {
            Debug.LogWarning("⚠️ [FacePerspectiveSwitcher] 摄像头获取超时，转为键盘模拟模式。");
            yield break;
        }

        // 绑定调试预览画面
        if (debugCameraPreview != null)
        {
            debugCameraPreview.texture = webCamTexture;
        }

        // 使用 MediaPipe 专用的 TextureFrame 解决垂直翻转与像素格式问题
        textureFrame = new TextureFrame(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32);

        // 2. 初始化 MediaPipe 模型资源加载器
        IResourceManager resourceManager;
#if UNITY_EDITOR
        resourceManager = new LocalResourceManager();
#else
        resourceManager = new StreamingAssetsResourceManager();
#endif
        yield return resourceManager.PrepareAssetAsync("blaze_face_short_range.bytes");

        // 3. 构建 FaceDetector 配置
        FaceDetectorOptions options = null;
        try
        {
            var baseOptions = new BaseOptions(
                BaseOptions.Delegate.CPU, 
                modelAssetPath: "blaze_face_short_range.bytes"
            );

            options = new FaceDetectorOptions(
                baseOptions,
                runningMode: RunningMode.IMAGE,
                minDetectionConfidence: 0.5f,
                minSuppressionThreshold: 0.3f,
                numFaces: 1
            );

            faceDetector = FaceDetector.CreateFromOptions(options);
            isMediaPipeReady = (faceDetector != null);
            Debug.Log("✅ [FacePerspectiveSwitcher] MediaPipe 人脸检测器初始化就绪！");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ [FacePerspectiveSwitcher] MediaPipe 初始化异常: {ex.Message}，已降级为键盘模拟模式。");
            yield break;
        }

        // 4. 开启异步人脸检测循环
        detectionLoopRoutine = StartCoroutine(FaceDetectionLoop());
    }

    private IEnumerator FaceDetectionLoop()
    {
        while (isMediaPipeReady && webCamTexture != null && webCamTexture.isPlaying)
        {
            if (Time.unscaledTime - lastDetectionTime >= detectionInterval)
            {
                lastDetectionTime = Time.unscaledTime;
                ProcessFrame();
            }
            yield return null;
        }
    }

    private void ProcessFrame()
    {
        if (webCamTexture == null || !webCamTexture.isPlaying || faceDetector == null || textureFrame == null) return;

        try
        {
            // 通过 TextureFrame 读取摄像头图像（自动校正垂直翻转与缩放）
            bool vFlip = webCamTexture.videoVerticallyMirrored;
            textureFrame.ReadTextureOnCPU(webCamTexture, flipHorizontally: false, flipVertically: vFlip);

            // 构建 MediaPipe 图像并检测
            using var mpImage = textureFrame.BuildCPUImage();
            var imgOptions = new ImageProcessingOptions(rotationDegrees: (int)webCamTexture.videoRotationAngle);
            FaceDetectionResult result = faceDetector.Detect(mpImage, imgOptions);

            if (result.detections != null && result.detections.Count > 0)
            {
                var primaryDetection = result.detections[0];
                var box = primaryDetection.boundingBox;

                // 计算人脸中心在图像宽度上的百分比 (0.0 ~ 1.0)
                float faceCenterPixelX = (box.left + box.right) * 0.5f;
                float normalizedFaceX = Mathf.Clamp01(faceCenterPixelX / textureFrame.width);

                // 镜像反转 (自拍模式将相机视野转换为镜面视野)
                if (mirrorCamera)
                {
                    normalizedFaceX = 1f - normalizedFaceX;
                }

                // 左右反转校正
                if (invertDirection)
                {
                    normalizedFaceX = 1f - normalizedFaceX;
                }

                // 相对中心偏差: -0.5 (最左) ~ 0 (居中) ~ +0.5 (最右)
                rawTargetOffset = normalizedFaceX - 0.5f;
                IsFaceDetected = true;
                isSimulatingWithKeyboard = false;
            }
            else
            {
                // 本帧未检出人脸 (不立即判定为 None，保持现有 offset)
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FacePerspectiveSwitcher] 人脸检测单帧异常: {ex.Message}");
        }
    }

    private void UpdateDebugUI()
    {
        if (debugStatusText != null)
        {
            string status = IsFaceDetected ? $"偏移={rawTargetOffset:+0.00;-0.00;0.00} (平滑={SmoothedFaceOffsetX:+0.00;-0.00;0.00})" : "未识别";
            debugStatusText.text = $"<b>视角: {CurrentPerspective}</b> | {status}\n[快捷键: ←左 | ↓中 | →右]";
        }
    }

    private void OnDestroy()
    {
        if (detectionLoopRoutine != null) StopCoroutine(detectionLoopRoutine);
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (faceDetector != null)
        {
            faceDetector.Close();
            faceDetector = null;
        }

        if (webCamTexture != null)
        {
            webCamTexture.Stop();
            webCamTexture = null;
        }

        if (textureFrame != null)
        {
            textureFrame.Dispose();
            textureFrame = null;
        }
    }
}
