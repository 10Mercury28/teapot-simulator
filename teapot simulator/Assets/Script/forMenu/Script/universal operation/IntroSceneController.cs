using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;

public class IntroSceneController : MonoBehaviour
{
    [Header("Video Settings")]
    public VideoPlayer videoPlayer;
    public RawImage rawImage;

    [Header("UI")]
    public Button playButton;

    [Header("Skip Settings")]
    [Tooltip(
        "不勾选：点击按钮 → 播放视频 → SceneBootstrapper负责后续流程。\n" +
        "勾选：点击按钮 → 不播放视频 → 直接模拟“视频播放结束”，交给SceneBootstrapper负责后续流程。"
    )]
    public bool skipVideo = false;

    [Header("Scene Bootstrapper")]
    [Tooltip("拖入当前场景中负责正常视频结束后转场的 SceneBootstrapper。留空时会自动寻找。")]
    public SceneBootstrapper sceneBootstrapper;

    private bool hasStarted = false;


    // ============================================================
    // START
    // ============================================================

    void Start()
    {
        // --------------------------------------------------------
        // VideoPlayer 检查
        // --------------------------------------------------------

        if (videoPlayer == null)
        {
            Debug.LogError(
                "❌ IntroSceneController：VideoPlayer 未绑定！"
            );
            return;
        }

        if (rawImage == null)
        {
            Debug.LogError(
                "❌ IntroSceneController：RawImage 未绑定！"
            );
            return;
        }


        // --------------------------------------------------------
        // 自动寻找 SceneBootstrapper
        // --------------------------------------------------------

        if (sceneBootstrapper == null)
        {
            sceneBootstrapper =
                FindObjectOfType<SceneBootstrapper>();
        }

        if (sceneBootstrapper == null)
        {
            Debug.LogError(
                "❌ IntroSceneController：找不到 SceneBootstrapper！\n" +
                "请在 Inspector 的 Scene Bootstrapper 栏里手动拖入。"
            );
        }


        // --------------------------------------------------------
        // Video 设置
        // --------------------------------------------------------

        videoPlayer.playOnAwake = false;

        // 注意：
        // IntroSceneController 不再监听 loopPointReached！
        //
        // 视频自然结束后的转场仍然完全交给
        // SceneBootstrapper 自己原来的 OnVideoEnd。
        //
        // 这样不会出现两个脚本同时跳转的问题。

        videoPlayer.prepareCompleted += OnVideoPrepared;

        videoPlayer.Prepare();


        // --------------------------------------------------------
        // Button
        // --------------------------------------------------------

        if (playButton != null)
        {
            playButton.onClick.RemoveListener(OnPlayClicked);
            playButton.onClick.AddListener(OnPlayClicked);
        }
        else
        {
            Debug.LogError(
                "❌ IntroSceneController：Play Button 未绑定！"
            );
        }


        Debug.Log(
            $"🎬 IntroSceneController 初始化完成 | Skip Video = {skipVideo}"
        );
    }


    // ============================================================
    // VIDEO PREPARED
    // ============================================================

    void OnVideoPrepared(VideoPlayer vp)
    {
        if (rawImage != null)
        {
            rawImage.texture = vp.texture;
        }

        // 如果还没有点击按钮，就停在第一帧
        if (!hasStarted)
        {
            vp.frame = 0;
            vp.Pause();

            Debug.Log(
                "🎞 Intro 视频准备完成，停在第一帧。"
            );
        }
    }


    // ============================================================
    // BUTTON
    // ============================================================

    void OnPlayClicked()
    {
        if (hasStarted)
            return;

        hasStarted = true;


        // 防止重复点击
        if (playButton != null)
        {
            playButton.interactable = false;
            playButton.gameObject.SetActive(false);
        }


        // ========================================================
        // SKIP VIDEO
        // ========================================================

        if (skipVideo)
        {
            Debug.Log(
                "⏭ Skip Video = TRUE"
            );

            Debug.Log(
                "⏭ 不播放 Intro 视频，直接进入视频结束后的正常流程。"
            );


            // 确保视频不会继续播放
            if (videoPlayer != null)
            {
                videoPlayer.Pause();
            }


            TriggerNormalVideoEndFlow();

            return;
        }


        // ========================================================
        // NORMAL
        // ========================================================

        Debug.Log(
            "▶️ Skip Video = FALSE → 正常播放 Intro 视频。"
        );


        // 如果视频已经 Prepare 好
        if (videoPlayer.isPrepared)
        {
            videoPlayer.time = 0;
            videoPlayer.Play();
        }

        // 如果还没 Prepare 完
        else
        {
            Debug.Log(
                "⏳ 视频尚未 Prepare 完成，等待后再播放。"
            );

            videoPlayer.prepareCompleted += PlayAfterPrepared;

            videoPlayer.Prepare();
        }
    }


    // ============================================================
    // WAIT FOR PREPARE THEN PLAY
    // ============================================================

    void PlayAfterPrepared(VideoPlayer vp)
    {
        vp.prepareCompleted -= PlayAfterPrepared;

        if (skipVideo)
            return;

        vp.time = 0;
        vp.Play();

        Debug.Log(
            "▶️ Video Prepare 完成 → 开始播放 Intro。"
        );
    }


    // ============================================================
    // SKIP → 模拟视频已经正常播放结束
    // ============================================================

    void TriggerNormalVideoEndFlow()
    {
        if (sceneBootstrapper == null)
        {
            sceneBootstrapper =
                FindObjectOfType<SceneBootstrapper>();
        }


        if (sceneBootstrapper == null)
        {
            Debug.LogError(
                "❌ Skip 失败：SceneBootstrapper 不存在！"
            );

            return;
        }


        Debug.Log(
            "🔁 Skip → 将控制权交给 SceneBootstrapper.OnVideoEnd()"
        );


        /*
         * 关键：
         *
         * 不自己 LoadScene。
         *
         * 直接调用 SceneBootstrapper 原来处理
         * “视频播放结束”的同一个方法。
         *
         * SendMessage 可以调用 private OnVideoEnd(VideoPlayer)
         * 所以不需要修改 SceneBootstrapper。
         */

        sceneBootstrapper.SendMessage(
            "OnVideoEnd",
            videoPlayer,
            SendMessageOptions.RequireReceiver
        );
    }


    // ============================================================
    // CLEANUP
    // ============================================================

    void OnDestroy()
    {
        if (playButton != null)
        {
            playButton.onClick.RemoveListener(
                OnPlayClicked
            );
        }

        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -=
                OnVideoPrepared;

            videoPlayer.prepareCompleted -=
                PlayAfterPrepared;
        }
    }
}