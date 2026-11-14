using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 弹窗数据 ScriptableObject
/// 用于在 Unity Project 视图中右键创建并配置各种弹窗（如大众点评通知、老师通知、居士消息、美团跑腿订单等）。
/// </summary>
[CreateAssetMenu(fileName = "PopupData_", menuName = "Teapot/Popup Data", order = 1)]
public class PopupWindowDataSO : ScriptableObject
{
    [Header("=== 基础标识 (ID) ===")]
    [Tooltip("弹窗的唯一标识字符串，如 'Dianping_Pat2', 'Teacher_Pat4', 'Meituan_Order', 'Teacher_D2'")]
    public string popupId;

    [Header("=== 窄窗口配置 (Banner 600x200) ===")]
    [Tooltip("窄窗口标题，如：大众点评、老师、侯居士")]
    public string bannerTitle = "新消息通知";

    [Tooltip("窄窗口摘要文字内容")]
    [TextArea(2, 4)]
    public string bannerSummary;

    [Tooltip("角色/来源头像 (150x150)，如：老师微信、大众点评、居士等")]
    public Sprite avatarSprite;

    [Tooltip("窄窗口背景图 (600x200)，若为空则使用 PopupManager 默认的小弹窗背景图")]
    public Sprite bannerBackground;

    [Header("=== 展开大窗口配置 (Drawer 600x900) ===")]
    [Tooltip("是否有大消息？\n【True】: 玩家点击窄窗口后，展开 600x900 大抽屉窗口\n【False】: 玩家点击窄窗口后，直接关闭消失（如大众点评通知）")]
    public bool hasExpandedContent = false;

    [Tooltip("大抽屉窗口顶部的标题，若留空则自动沿用 bannerTitle")]
    public string drawerTitle;

    [Tooltip("大抽屉背景底图 (600x900)，若为空则使用 PopupManager 默认的大弹窗背景图")]
    public Sprite drawerBackground;

    [Tooltip("大抽屉主体图片展示 (如 550 宽的语音条转文字图、居士消息图、跑腿订单图等)")]
    public Sprite drawerContentImage;

    [Tooltip("大抽屉文字详情 (可选，可作为正文说明或补充信息)")]
    [TextArea(3, 8)]
    public string drawerDetailText;

    [Header("=== 音频配置 ===")]
    [Tooltip("窄窗口弹出时开始循环播放的提示音 (mp3/wav)，直到玩家点击窄窗口才停止")]
    public AudioClip incomingLoopSfx;

    [Tooltip("展开大抽屉时播放的语音条音频 (mp3/wav)")]
    public AudioClip voiceClip;

    [Header("=== 跑腿订单与确认交互 ===")]
    [Tooltip("是否为跑腿订单？若是，大抽屉底部将显示'确认'按钮")]
    public bool isDeliveryOrder = false;

    [Tooltip("确认按钮文字")]
    public string confirmButtonText = "确认取件";

    [Tooltip("点击确认后跳转的目标场景名 (可选，留空则不切场景，仅触发 OnOrderConfirmed 事件)")]
    public string targetSceneOnConfirm;

    [Header("=== 特殊业务标志 ===")]
    [Tooltip("若弹出此消息时玩家正在进行交互干活，是否打断并判 Fail (对应 Pat 第4个东西时弹出老师消息)")]
    public bool interruptWorkAndFail = false;
}
