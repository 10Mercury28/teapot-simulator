using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 弹窗系统总管理器 (PopupManager)
/// 挂载在 PopupCanvas 上，统一调度 600x200 窄横幅与 600x900 展开大抽屉。
/// 核心特性：
/// 1. ScriptableObject (PopupWindowDataSO) 完全数据驱动；
/// 2. 窄横幅弹出后开始循环播放提示音，常驻不自动消失；
/// 3. 点击窄横幅停止提示音：
///    - 若 hasExpandedContent = true：无缝展开 600x900 大抽屉，播放语音条 mp3；
///    - 若 hasExpandedContent = false：直接滑出关闭（如大众点评）；
/// 4. 支持根据 popupId 快速拉起预先配置好的 SO；
/// 5. 键盘快捷键 (T) 供运行模式下随时测试。
/// </summary>
[RequireComponent(typeof(Canvas))]
public class PopupManager : MonoBehaviour
{
    public static PopupManager Instance { get; private set; }

    [Header("=== 控制器引用 ===")]
    [Tooltip("窄横幅 UI 控制器")]
    public SlideNotificationBannerUI bannerUI;

    [Tooltip("展开大抽屉 UI 控制器")]
    public SlideDrawerUI drawerUI;

    [Header("=== 音频播放器 (AudioSource) ===")]
    [Tooltip("用于循环播放提示音/铃声的 AudioSource (若未指定会自动添加)")]
    public AudioSource loopAudioSource;

    [Tooltip("用于单次播放语音条 mp3 的 AudioSource (若未指定会自动添加)")]
    public AudioSource voiceAudioSource;

    [Tooltip("默认的全局消息循环提示音 (当 SO 中未指定 incomingLoopSfx 时使用)")]
    public AudioClip defaultIncomingLoopSfx;

    [Header("=== 默认底图与头像 ===")]
    [Tooltip("默认窄弹窗底图 (小弹窗png 600x200)")]
    public Sprite defaultBannerBg;

    [Tooltip("默认大抽屉底图 (大弹窗png 600x900)")]
    public Sprite defaultDrawerBg;

    [Tooltip("默认头像")]
    public Sprite defaultAvatar;

    [Header("=== 预注册数据列表 (Database) ===")]
    [Tooltip("预注册的 ScriptableObject 列表，可通过 ShowById('Dianping_Pat2') 直接唤出")]
    public List<PopupWindowDataSO> registeredPopups = new List<PopupWindowDataSO>();

    [Header("=== 调试功能 ===")]
    [Tooltip("是否在场景切换时跨场景保留该弹窗画布 (DontDestroyOnLoad)")]
    public bool dontDestroyOnLoad = false;

    [Tooltip("测试使用的 SO 数据资产")]
    public PopupWindowDataSO debugTestSO;

    [Tooltip("测试热键 (运行模式下按此键触发弹出)")]
    public KeyCode debugKey = KeyCode.T;

    // 事件
    public event Action<PopupWindowDataSO> OnPopupShown;
    public event Action<PopupWindowDataSO> OnBannerClicked;
    public event Action<PopupWindowDataSO> OnDrawerOpened;
    public event Action<PopupWindowDataSO> OnDrawerClosed;
    public event Action<PopupWindowDataSO> OnPopupClosed; // 无论是横幅关闭还是抽屉关闭，弹窗流程彻底结束时触发
    public event Action<PopupWindowDataSO> OnOrderConfirmed;
    public event Action<PopupWindowDataSO> OnWorkInterrupted;

    // 当前活跃数据
    public PopupWindowDataSO CurrentData { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        SetupComponents();
    }

    private void Start()
    {
        // 初始确保都处于隐藏状态
        if (bannerUI != null) bannerUI.SnapToHidden();
        if (drawerUI != null) drawerUI.SnapToHidden();
    }

    private void Update()
    {
        // 调试按键
        if (debugKey != KeyCode.None && Input.GetKeyDown(debugKey))
        {
            if (debugTestSO != null)
            {
                Show(debugTestSO);
            }
            else if (registeredPopups != null && registeredPopups.Count > 0)
            {
                Show(registeredPopups[0]);
            }
            else
            {
                Debug.LogWarning("⚠️ [PopupManager] 未配置 debugTestSO 或 registeredPopups，无法通过按键测试。");
            }
        }
    }

    private void SetupComponents()
    {
        // 自动查找 Banner 和 Drawer
        if (bannerUI == null) bannerUI = GetComponentInChildren<SlideNotificationBannerUI>(true);
        if (drawerUI == null) drawerUI = GetComponentInChildren<SlideDrawerUI>(true);

        // 设置音频播放器
        if (loopAudioSource == null)
        {
            loopAudioSource = gameObject.AddComponent<AudioSource>();
            loopAudioSource.playOnAwake = false;
            loopAudioSource.loop = true;
        }

        if (voiceAudioSource == null)
        {
            voiceAudioSource = gameObject.AddComponent<AudioSource>();
            voiceAudioSource.playOnAwake = false;
            voiceAudioSource.loop = false;
        }

        // 绑定 Banner 事件
        if (bannerUI != null)
        {
            bannerUI.OnClicked += HandleBannerClicked;
            bannerUI.OnDismissedBySwipe += HandleBannerDismissedBySwipe;
        }

        // 绑定 Drawer 事件
        if (drawerUI != null)
        {
            drawerUI.OnCloseRequested += HandleDrawerCloseRequested;
            drawerUI.OnOrderConfirmed += HandleOrderConfirmed;
        }
    }

    // ============================================================
    // 公共 API
    // ============================================================

    /// <summary>
    /// 根据 ScriptableObject 数据弹出窗口
    /// </summary>
    public void Show(PopupWindowDataSO data)
    {
        if (data == null)
        {
            Debug.LogError("❌ [PopupManager] Show 传入的 PopupWindowDataSO 为空！");
            return;
        }

        CurrentData = data;
        Debug.Log($"🔔 [PopupManager] 弹出窗口: {data.popupId} | 标题: {data.bannerTitle}");

        // 1. 若配置了打断干活判 Fail，通知外部
        if (data.interruptWorkAndFail)
        {
            Debug.Log($"⚠️ [PopupManager] 弹窗触发打断操作 (Fail 判定): {data.popupId}");
            OnWorkInterrupted?.Invoke(data);
        }

        // 2. 填充 Banner UI
        if (bannerUI != null)
        {
            bannerUI.BindData(data, defaultBannerBg, defaultAvatar);
            bannerUI.Show();
        }

        // 3. 播放循环提示音
        AudioClip clipToPlay = data.incomingLoopSfx != null ? data.incomingLoopSfx : defaultIncomingLoopSfx;
        if (clipToPlay != null && loopAudioSource != null)
        {
            loopAudioSource.clip = clipToPlay;
            loopAudioSource.loop = true;
            loopAudioSource.Play();
        }

        OnPopupShown?.Invoke(data);
    }

    /// <summary>
    /// 根据预注册的 popupId 或资产名查找弹窗数据
    /// </summary>
    public PopupWindowDataSO FindPopupData(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        // 1. 精确匹配 popupId
        PopupWindowDataSO found = registeredPopups.Find(p => p != null && string.Equals(p.popupId, id, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;

        // 2. 精确匹配 asset 资产名
        found = registeredPopups.Find(p => p != null && string.Equals(p.name, id, StringComparison.OrdinalIgnoreCase));
        if (found != null) return found;

        // 3. 包含模糊匹配 (例如 "Dianping" 匹配 "Day1_Dianping_Pat2")
        found = registeredPopups.Find(p => p != null && 
            ((p.popupId != null && p.popupId.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) ||
             (p.name != null && p.name.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0)));
        if (found != null) return found;

        // 4. 从内存/已加载资产中查找 (防止策划未手动拖入 registeredPopups)
        PopupWindowDataSO[] allLoaded = Resources.FindObjectsOfTypeAll<PopupWindowDataSO>();
        foreach (var p in allLoaded)
        {
            if (p == null) continue;
            if (string.Equals(p.popupId, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.name, id, StringComparison.OrdinalIgnoreCase) ||
                (p.popupId != null && p.popupId.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (p.name != null && p.name.IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据预注册的 popupId 或资产名称弹出窗口
    /// </summary>
    public void ShowById(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        PopupWindowDataSO found = FindPopupData(id);
        if (found != null)
        {
            Show(found);
        }
        else
        {
            Debug.LogWarning($"⚠️ [PopupManager] 未能找到 ID/名称匹配 '{id}' 的弹窗数据！");
        }
    }

    /// <summary>
    /// 关闭所有弹窗并停止声音
    /// </summary>
    public void CloseAll()
    {
        StopAllSounds();

        if (bannerUI != null) bannerUI.Hide();
        if (drawerUI != null) drawerUI.Hide();

        CurrentData = null;
    }

    // ============================================================
    // 内部交互响应流程
    // ============================================================

    private void HandleBannerClicked()
    {
        // 1. 停止循环提示音
        StopLoopSound();

        if (CurrentData == null) return;
        OnBannerClicked?.Invoke(CurrentData);

        // 2. 判断是否展开大抽屉
        if (CurrentData.hasExpandedContent && drawerUI != null)
        {
            // 有大消息 -> 收起窄横幅，滑入大抽屉
            bannerUI.Hide();

            drawerUI.BindData(CurrentData, defaultDrawerBg);
            drawerUI.Show();

            // 播放语音音频
            if (CurrentData.voiceClip != null && voiceAudioSource != null)
            {
                voiceAudioSource.clip = CurrentData.voiceClip;
                voiceAudioSource.Play();
            }

            OnDrawerOpened?.Invoke(CurrentData);
        }
        else
        {
            // 无大消息 (如大众点评) -> 直接滑出关闭
            PopupWindowDataSO closedData = CurrentData;
            bannerUI.Hide();
            CurrentData = null;
            OnPopupClosed?.Invoke(closedData);
        }
    }

    private void HandleBannerDismissedBySwipe()
    {
        // 玩家手势划走
        StopLoopSound();
        PopupWindowDataSO closedData = CurrentData;
        CurrentData = null;
        OnPopupClosed?.Invoke(closedData);
    }

    private void HandleDrawerCloseRequested()
    {
        StopVoiceSound();

        PopupWindowDataSO closedData = CurrentData;
        if (drawerUI != null)
        {
            drawerUI.Hide();
        }

        OnDrawerClosed?.Invoke(closedData);
        OnPopupClosed?.Invoke(closedData);
        CurrentData = null;
    }

    private void HandleOrderConfirmed()
    {
        StopVoiceSound();

        PopupWindowDataSO confirmedData = CurrentData;
        OnOrderConfirmed?.Invoke(confirmedData);

        // 如果配置了跳转场景
        if (confirmedData != null && !string.IsNullOrEmpty(confirmedData.targetSceneOnConfirm))
        {
            Debug.Log($"🚀 [PopupManager] 订单确认，跳转场景: {confirmedData.targetSceneOnConfirm}");
            if (SceneBlinkTransition.Instance != null)
            {
                SceneBlinkTransition.Instance.LoadSceneConcealed(confirmedData.targetSceneOnConfirm);
            }
            else
            {
                SceneManager.LoadScene(confirmedData.targetSceneOnConfirm);
            }
        }

        if (drawerUI != null)
        {
            drawerUI.Hide();
        }

        CurrentData = null;
    }

    private void StopLoopSound()
    {
        if (loopAudioSource != null && loopAudioSource.isPlaying)
        {
            loopAudioSource.Stop();
        }
    }

    private void StopVoiceSound()
    {
        if (voiceAudioSource != null && voiceAudioSource.isPlaying)
        {
            voiceAudioSource.Stop();
        }
    }

    private void StopAllSounds()
    {
        StopLoopSound();
        StopVoiceSound();
    }

    private void OnDestroy()
    {
        if (bannerUI != null)
        {
            bannerUI.OnClicked -= HandleBannerClicked;
            bannerUI.OnDismissedBySwipe -= HandleBannerDismissedBySwipe;
        }

        if (drawerUI != null)
        {
            drawerUI.OnCloseRequested -= HandleDrawerCloseRequested;
            drawerUI.OnOrderConfirmed -= HandleOrderConfirmed;
        }
    }
}
