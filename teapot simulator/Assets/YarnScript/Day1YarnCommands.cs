using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Yarn.Unity;

/// <summary>
/// 集中式 Yarn Spinner 剧情指令控制器 (Day1YarnCommands)
/// 挂载在场景中（如 DialogueRunner 或全局管理器同一物体上）。
/// 
/// 核心支持的 Yarn 指令：
/// 1. 弹窗控制：
///    - <<show_popup "popup_id">>       : 异步拉起弹窗，不阻断剧本对白
///    - <<show_popup_wait "popup_id">>  : 阻断式拉起弹窗，等待玩家处理完毕（收起抽屉/确认订单）后再继续后续对话
///    - <<hide_popup>>                  : 立即关闭当前所有弹窗
/// 
/// 2. 角色立绘与 UI 显隐：
///    - <<show_character "jushi">>      : 显示居士立绘/UI，若有转头控制组件自动激活转头映射
///    - <<hide_character "jushi">>      : 隐藏居士立绘/UI，同时禁用转头映射
///    - <<show_character "teacher">>    : 显示老师立绘/UI
///    - <<hide_character "teacher">>    : 隐藏老师立绘/UI
///    - <<show_jushi>>, <<hide_jushi>>, <<show_teacher>>, <<hide_teacher>> : 便捷别名
///    - <<set_active "ObjectName" true>>: 通用物体显隐开关
/// 
/// 3. 音效与语音播放：
///    - <<play_sfx "clip_name">>        : 播放单次音效 (如 "蝉鸣", "chanming", "doorbell", "门铃")
///    - <<play_sfx_wait "clip_name">>   : 播放音效并等待音频播放结束再继续对白
///    - <<stop_sfx>>                    : 停止当前音效
///    - <<play_bgm "clip_name">>        : 循环播放背景音乐
///    - <<stop_bgm>>                    : 停止背景音乐
/// 
/// 4. 场景跳转：
///    - <<load_scene "scene_name">>     : 调用 SceneBlinkTransition 闭眼平滑转场（或标准 LoadScene）
/// </summary>
public class Day1YarnCommands : MonoBehaviour
{
    public static Day1YarnCommands Instance { get; private set; }

    [Header("=== Dialogue Runner (可选，留空自动获取) ===")]
    [Tooltip("场景中的 DialogueRunner 引用")]
    public DialogueRunner dialogueRunner;

    [Header("=== 角色立绘 / UI 对象配置 ===")]
    [Tooltip("居士的角色 GameObject / UI Image")]
    public GameObject jushiObject;

    [Tooltip("居士转头观察控制器 FacePerspectiveSwitcher（居士在场时激活，退场时停用）")]
    public FacePerspectiveSwitcher jushiFaceSwitcher;

    [Tooltip("老师的角色 GameObject / UI Image")]
    public GameObject teacherObject;

    [System.Serializable]
    public class NamedGameObject
    {
        public string name;
        public GameObject target;
    }

    [Tooltip("自定义命名对象列表，供 show_character / set_active 快速查找")]
    public List<NamedGameObject> customCharacters = new List<NamedGameObject>();

    [Header("=== 音频播放器配置 ===")]
    [Tooltip("音效专用 AudioSource (留空自动创建)")]
    public AudioSource sfxAudioSource;

    [Tooltip("背景音/环境声专用 AudioSource (留空自动创建)")]
    public AudioSource bgmAudioSource;

    [System.Serializable]
    public class NamedAudioClip
    {
        public string name;
        public AudioClip clip;
    }

    [Tooltip("预注册音效列表（支持中英文别名，如 '蝉鸣', 'chanming', 'doorbell' 等）")]
    public List<NamedAudioClip> soundEffects = new List<NamedAudioClip>();

    [Header("=== 弹窗管理器引用 (可选，默认使用 PopupManager.Instance) ===")]
    public PopupManager popupManager;

    [Header("=== 扫码枪配置 (Barcode Scanner) ===")]
    [Tooltip("扫码枪组件引用（留空会自动查找或自动添加）")]
    public BarcodeScanner barcodeScanner;

    public enum StudioDeskStartMode
    {
        AutoFromGlobalProgress = 0, // 自动按 GlobalProgressManager.CurrentDay 决定 Day 1 / Day 2
        ForceDay1 = 1,              // 调试强制启动 Day 1 (Character_Jushi)
        ForceDay2 = 2               // 调试强制启动 Day 2 (Character_Jushi_D2)
    }

    [Header("=== 柜台场景 Day 1 / Day 2 自动分流（复用 StudioDesk） ===")]
    [Tooltip("自动按全局进度决定进入 Day 1 或 Day 2 剧情；单场景调试时可切换强制 Day 1 / Day 2")]
    public StudioDeskStartMode startDayMode = StudioDeskStartMode.AutoFromGlobalProgress;

    [Tooltip("调试进入 Day 2 时，模拟前一天已交心 ($LikeU = true)")]
    public bool debugSimulateLikeUForD2 = false;

    [Tooltip("调试进入 Day 2 时，模拟 Day 1 聊过蝉/摇滚 ($D1TalkedCicada = true)")]
    public bool debugSimulateCicadaForD2 = false;

    private bool dialogueHooksBound = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        GlobalProgressManager.EnsureInstance();

        // 自动装配组件
        if (dialogueRunner == null)
        {
            dialogueRunner = FindObjectOfType<DialogueRunner>();
        }

        if (popupManager == null)
        {
            popupManager = PopupManager.Instance != null ? PopupManager.Instance : FindObjectOfType<PopupManager>();
        }

        if (sfxAudioSource == null)
        {
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.loop = false;
        }

        if (bgmAudioSource == null)
        {
            bgmAudioSource = gameObject.AddComponent<AudioSource>();
            bgmAudioSource.playOnAwake = false;
            bgmAudioSource.loop = true;
        }

        if (jushiFaceSwitcher == null && jushiObject != null)
        {
            jushiFaceSwitcher = jushiObject.GetComponentInChildren<FacePerspectiveSwitcher>(true);
        }

        if (barcodeScanner == null)
        {
            barcodeScanner = FindObjectOfType<BarcodeScanner>();
        }

        BindDialogueHooks();
        ConfigureStudioDeskEntry();
    }

    private void Start()
    {
        SyncStateToYarn();
    }

    private void OnDestroy()
    {
        if (dialogueHooksBound && dialogueRunner != null)
        {
            dialogueRunner.onDialogueStart?.RemoveListener(SyncStateToYarn);
            dialogueRunner.onNodeComplete?.RemoveListener(OnNodeCompletedCaptureVars);
            dialogueRunner.onDialogueComplete?.RemoveListener(OnDialogueCompletedCaptureVars);
            dialogueHooksBound = false;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void BindDialogueHooks()
    {
        if (dialogueHooksBound || dialogueRunner == null) return;

        dialogueRunner.onDialogueStart?.AddListener(SyncStateToYarn);
        dialogueRunner.onNodeComplete?.AddListener(OnNodeCompletedCaptureVars);
        dialogueRunner.onDialogueComplete?.AddListener(OnDialogueCompletedCaptureVars);
        dialogueHooksBound = true;
    }

    private void ConfigureStudioDeskEntry()
    {
        if (dialogueRunner == null) return;

        string activeScene = SceneManager.GetActiveScene().name;
        bool isStudioDesk = activeScene.IndexOf("StudioDesk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            dialogueRunner.startNode == "Character_Jushi" ||
                            dialogueRunner.startNode == "Character_Jushi_D2";

        if (!isStudioDesk) return;

        int targetDay = GlobalProgressManager.CurrentDay;
        if (startDayMode == StudioDeskStartMode.ForceDay1)
        {
            targetDay = 1;
            GlobalProgressManager.CurrentDay = 1;
        }
        else if (startDayMode == StudioDeskStartMode.ForceDay2)
        {
            targetDay = 2;
            GlobalProgressManager.CurrentDay = 2;
        }

        if (debugSimulateLikeUForD2) GlobalProgressManager.IsLikeU = true;
        if (debugSimulateCicadaForD2) GlobalProgressManager.TalkedCicada = true;

        string targetNode = targetDay >= 2 ? "Character_Jushi_D2" : "Character_Jushi";
        dialogueRunner.startNode = targetNode;
        Debug.Log($"🏪 [Day1YarnCommands] 柜台场景自动分流：Day={targetDay}, startNode='{targetNode}', LikeU={GlobalProgressManager.IsLikeU}, TalkedCicada={GlobalProgressManager.TalkedCicada}");
    }

    private void SyncStateToYarn()
    {
        GlobalProgressManager.SyncGlobalVarsToYarn(dialogueRunner);
    }

    private void OnNodeCompletedCaptureVars(string _)
    {
        GlobalProgressManager.CaptureYarnVarsToGlobal(dialogueRunner);
    }

    private void OnDialogueCompletedCaptureVars()
    {
        GlobalProgressManager.CaptureYarnVarsToGlobal(dialogueRunner);
    }

    // ============================================================
    // 1. 弹窗控制指令 (Popup Commands)
    // ============================================================

    /// <summary>
    /// 非阻塞拉起弹窗: <<show_popup "popup_id">>
    /// </summary>
    [YarnCommand("show_popup")]
    public static void ShowPopup(string popupId)
    {
        Debug.Log($"💬 [YarnCommand] show_popup: '{popupId}'");
        if (popupId.IndexOf("HouJ", StringComparison.OrdinalIgnoreCase) >= 0 || popupId.IndexOf("LikeU", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            GlobalProgressManager.IsLikeU = true;
            Debug.Log("🤝 [Day1YarnCommands] 检测到居士交心弹窗，全局设置 IsLikeU = true！");
        }
        PopupManager pm = GetPopupManager();
        if (pm != null)
        {
            pm.ShowById(popupId);
        }
        else
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] show_popup: 场景中未找到 PopupManager！");
        }
    }

    /// <summary>
    /// 阻塞式拉起弹窗并等待关闭: <<show_popup_wait "popup_id">>
    /// </summary>
    [YarnCommand("show_popup_wait")]
    public static IEnumerator ShowPopupWait(string popupId)
    {
        Debug.Log($"💬 [YarnCommand] show_popup_wait: '{popupId}'");
        if (popupId.IndexOf("HouJ", StringComparison.OrdinalIgnoreCase) >= 0 || popupId.IndexOf("LikeU", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            GlobalProgressManager.IsLikeU = true;
            Debug.Log("🤝 [Day1YarnCommands] 检测到居士交心弹窗，全局设置 IsLikeU = true！");
        }
        PopupManager pm = GetPopupManager();
        if (pm == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] show_popup_wait: 场景中未找到 PopupManager！");
            yield break;
        }

        bool isClosed = false;
        bool confirmedWithSceneTransition = false;
        void OnPopupClosedHandler(PopupWindowDataSO data)
        {
            isClosed = true;
        }
        void OnOrderConfirmedHandler(PopupWindowDataSO data)
        {
            if (data != null && !string.IsNullOrEmpty(data.targetSceneOnConfirm))
            {
                confirmedWithSceneTransition = true;
            }
        }

        pm.OnPopupClosed += OnPopupClosedHandler;
        pm.OnOrderConfirmed += OnOrderConfirmedHandler;
        pm.ShowById(popupId);

        // 挂起等待玩家交互关闭抽屉或确认订单
        while (!isClosed)
        {
            yield return null;
        }

        pm.OnPopupClosed -= OnPopupClosedHandler;
        pm.OnOrderConfirmed -= OnOrderConfirmedHandler;

        if (confirmedWithSceneTransition)
        {
            DialogueRunner runner = GetDialogueRunner();
            if (runner != null && runner.VariableStorage != null)
            {
                runner.VariableStorage.SetValue("$D2Finished", true);
            }
        }
        Debug.Log($"💬 [YarnCommand] show_popup_wait 完成: '{popupId}' (orderTransition={confirmedWithSceneTransition})，继续推进 Yarn 对白。");
    }

    /// <summary>
    /// 设置居士交心状态: <<set_like_u true>>
    /// </summary>
    [YarnCommand("set_like_u")]
    public static void SetLikeU(bool likeU = true)
    {
        GlobalProgressManager.IsLikeU = likeU;
        Debug.Log($"🤝 [YarnCommand] set_like_u: {likeU}");
    }

    /// <summary>
    /// 关闭所有弹窗: <<hide_popup>>
    /// </summary>
    [YarnCommand("hide_popup")]
    public static void HidePopup()
    {
        Debug.Log("💬 [YarnCommand] hide_popup");
        PopupManager pm = GetPopupManager();
        if (pm != null)
        {
            pm.CloseAll();
        }
    }

    private static PopupManager GetPopupManager()
    {
        if (Instance != null && Instance.popupManager != null)
        {
            return Instance.popupManager;
        }
        return PopupManager.Instance != null ? PopupManager.Instance : GameObject.FindObjectOfType<PopupManager>();
    }

    // ============================================================
    // 2. 角色立绘与 UI 显隐指令 (Character / Sprite Commands)
    // ============================================================

    /// <summary>
    /// 显示角色: <<show_character "name">>
    /// </summary>
    [YarnCommand("show_character")]
    public static void ShowCharacter(string characterName)
    {
        SetCharacterVisibility(characterName, true);
    }

    /// <summary>
    /// 隐藏角色: <<hide_character "name">>
    /// </summary>
    [YarnCommand("hide_character")]
    public static void HideCharacter(string characterName)
    {
        SetCharacterVisibility(characterName, false);
    }

    [YarnCommand("show_jushi")]
    public static void ShowJushi() => ShowCharacter("jushi");

    [YarnCommand("hide_jushi")]
    public static void HideJushi() => HideCharacter("jushi");

    [YarnCommand("show_teacher")]
    public static void ShowTeacher() => ShowCharacter("teacher");

    [YarnCommand("hide_teacher")]
    public static void HideTeacher() => HideCharacter("teacher");

    /// <summary>
    /// 通用激活/停用物体: <<set_active "ObjectName" true/false>>
    /// </summary>
    [YarnCommand("set_active")]
    public static void SetActiveObject(string objectName, bool active)
    {
        SetCharacterVisibility(objectName, active);
    }

    private static void SetCharacterVisibility(string name, bool visible)
    {
        Debug.Log($"💬 [YarnCommand] SetCharacterVisibility: '{name}' -> {visible}");
        string lower = name.Trim().ToLowerInvariant();

        // 1. 判断是否是居士
        if (lower == "jushi" || lower == "居士" || lower == "houj" || lower == "houjushi" || lower == "character_jushi")
        {
            GameObject target = (Instance != null && Instance.jushiObject != null)
                ? Instance.jushiObject
                : FindObjectByNameOrTag("Jushi", "居士", "Character_Jushi", "FaceCaputureCanvas");
            if (target != null) ApplyVisibility(target, visible);

            FacePerspectiveSwitcher switcher = Instance != null ? Instance.jushiFaceSwitcher : null;
            if (switcher == null && target != null) switcher = target.GetComponentInChildren<FacePerspectiveSwitcher>(true);
            if (switcher == null) switcher = GameObject.FindObjectOfType<FacePerspectiveSwitcher>(true);
            SyncFaceSwitcherVisibility(switcher, visible, "居士");
            return;
        }

        // 2. 判断是否是老师/店长邵苇
        if (lower == "teacher" || lower == "老师" || lower == "shao" || lower == "shaowei" || lower == "shaoa")
        {
            GameObject target = (Instance != null && Instance.teacherObject != null)
                ? Instance.teacherObject
                : FindObjectByNameOrTag("Teacher", "老师", "ShaoWei", "Shao");
            if (target != null) ApplyVisibility(target, visible);

            FacePerspectiveSwitcher switcher = target != null ? target.GetComponentInChildren<FacePerspectiveSwitcher>(true) : null;
            SyncFaceSwitcherVisibility(switcher, visible, "老师");
            return;
        }

        // 3. 从预配置列表中匹配
        if (Instance != null && Instance.customCharacters != null)
        {
            NamedGameObject custom = Instance.customCharacters.Find(c => c != null && !string.IsNullOrEmpty(c.name) &&
                c.name.Trim().Equals(name, StringComparison.OrdinalIgnoreCase));
            if (custom != null && custom.target != null)
            {
                ApplyVisibility(custom.target, visible);
                return;
            }
        }

        // 4. 场景通用模糊查找
        GameObject sceneObj = GameObject.Find(name);
        if (sceneObj != null)
        {
            ApplyVisibility(sceneObj, visible);
        }
        else
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] 未在场景中找到名为 '{name}' 的对象以设置显隐！");
        }
    }

    private static void SyncFaceSwitcherVisibility(FacePerspectiveSwitcher switcher, bool visible, string characterLabel)
    {
        if (switcher == null) return;

        switcher.enabled = visible;
        if (switcher.displayImage != null)
        {
            switcher.displayImage.enabled = visible;
        }
        Debug.Log($"💬 [Day1YarnCommands] {characterLabel}转头控制器 FacePerspectiveSwitcher: {visible}");
    }

    /// <summary>
    /// 统一显隐辅助方法：同时兼容 GameObject、UI Image、SpriteRenderer 与 CanvasGroup
    /// </summary>
    private static void ApplyVisibility(GameObject target, bool visible)
    {
        if (target == null) return;

        // 1. 设置 GameObject 节点显隐
        target.SetActive(visible);

        // 2. 若挂有 CanvasGroup，同步透明度与交互
        CanvasGroup cg = target.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = visible ? 1f : 0f;
            cg.blocksRaycasts = visible;
            cg.interactable = visible;
        }

        // 3. 若节点为 UI Image，同步 enabled
        Image img = target.GetComponent<Image>();
        if (img != null)
        {
            img.enabled = visible;
        }

        // 4. 若节点为场景 2D Sprite (SpriteRenderer)，同步 enabled
        SpriteRenderer sr = target.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            sr.enabled = visible;
        }
    }

    private static GameObject FindObjectByNameOrTag(params string[] aliases)
    {
        foreach (string alias in aliases)
        {
            GameObject found = GameObject.Find(alias);
            if (found != null) return found;
        }

        // 在所有根节点中查找（含未激活）
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] rootObjects = activeScene.GetRootGameObjects();
        foreach (GameObject root in rootObjects)
        {
            foreach (string alias in aliases)
            {
                if (root.name.IndexOf(alias, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return root;
                }
                Transform child = FindDeepChild(root.transform, alias);
                if (child != null) return child.gameObject;
            }
        }
        return null;
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }

    // ============================================================
    // 3. 音效与音频播放指令 (Audio / SFX Commands)
    // ============================================================

    private Coroutine currentSfxFadeRoutine;

    /// <summary>
    /// 播放单次音效: 
    /// <<play_sfx "clip_name">>
    /// <<play_sfx "clip_name" 3.5>> (播放 3.5 秒后自动淡出并停止)
    /// <<play_sfx "clip_name" 3.5 1.0>> (播放 3.5 秒，其中最后 1.0 秒平滑淡出)
    /// </summary>
    [YarnCommand("play_sfx")]
    public static void PlaySfx(string clipName, float duration = 0f, float fadeOutTime = 0.5f)
    {
        Debug.Log($"💬 [YarnCommand] play_sfx: '{clipName}' (duration={duration}s, fadeOut={fadeOutTime}s)");
        AudioClip clip = ResolveAudioClip(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] play_sfx: 未能找到音效 '{clipName}'！");
            return;
        }

        AudioSource source = GetSfxAudioSource();
        if (source == null)
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
            return;
        }

        if (Instance != null)
        {
            Instance.StartPlaySfx(source, clip, duration, fadeOutTime);
        }
        else
        {
            if (duration > 0)
            {
                source.clip = clip;
                source.volume = 1f;
                source.Play();
            }
            else
            {
                source.PlayOneShot(clip);
            }
        }
    }

    /// <summary>
    /// 播放音效并等待结束: 
    /// <<play_sfx_wait "clip_name">>
    /// <<play_sfx_wait "clip_name" 3.0>> (播放 3.0 秒，暂停对白 3.0 秒后继续)
    /// </summary>
    [YarnCommand("play_sfx_wait")]
    public static IEnumerator PlaySfxWait(string clipName, float duration = 0f, float fadeOutTime = 0.5f)
    {
        Debug.Log($"💬 [YarnCommand] play_sfx_wait: '{clipName}' (duration={duration}s)");
        AudioClip clip = ResolveAudioClip(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] play_sfx_wait: 未能找到音效 '{clipName}'！");
            yield break;
        }

        AudioSource source = GetSfxAudioSource();
        float effectiveDuration = duration > 0 ? duration : clip.length;

        if (source != null)
        {
            if (Instance != null)
            {
                Instance.StartPlaySfx(source, clip, effectiveDuration, fadeOutTime);
            }
            else
            {
                source.clip = clip;
                source.volume = 1f;
                source.Play();
            }
            yield return new WaitForSeconds(effectiveDuration);
        }
        else
        {
            yield return new WaitForSeconds(effectiveDuration);
        }
    }

    /// <summary>
    /// 停止音效: 
    /// <<stop_sfx>>
    /// <<stop_sfx 0.5>> (在 0.5 秒内平滑淡出并停止)
    /// </summary>
    [YarnCommand("stop_sfx")]
    public static void StopSfx(float fadeOutTime = 0.3f)
    {
        Debug.Log($"💬 [YarnCommand] stop_sfx (fadeOut={fadeOutTime}s)");
        AudioSource source = GetSfxAudioSource();
        if (source != null && source.isPlaying)
        {
            if (Instance != null && fadeOutTime > 0)
            {
                Instance.StartStopSfx(source, fadeOutTime);
            }
            else
            {
                source.Stop();
            }
        }
    }

    private void StartPlaySfx(AudioSource source, AudioClip clip, float duration, float fadeOutTime)
    {
        if (currentSfxFadeRoutine != null)
        {
            StopCoroutine(currentSfxFadeRoutine);
            currentSfxFadeRoutine = null;
        }

        source.clip = clip;
        source.volume = 1f;
        source.loop = false;
        source.Play();

        if (duration > 0)
        {
            currentSfxFadeRoutine = StartCoroutine(FadeOutSfxRoutine(source, duration, fadeOutTime));
        }
    }

    private void StartStopSfx(AudioSource source, float fadeOutTime)
    {
        if (currentSfxFadeRoutine != null)
        {
            StopCoroutine(currentSfxFadeRoutine);
            currentSfxFadeRoutine = null;
        }

        if (fadeOutTime <= 0)
        {
            source.Stop();
        }
        else
        {
            currentSfxFadeRoutine = StartCoroutine(FadeOutSfxRoutine(source, fadeOutTime, fadeOutTime));
        }
    }

    private IEnumerator FadeOutSfxRoutine(AudioSource source, float duration, float fadeOutTime)
    {
        float playDuration = Mathf.Max(0.05f, duration - fadeOutTime);
        yield return new WaitForSeconds(playDuration);

        float initialVolume = source.volume;
        float elapsed = 0f;
        while (elapsed < fadeOutTime && source != null)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(initialVolume, 0f, elapsed / fadeOutTime);
            yield return null;
        }

        if (source != null)
        {
            source.Stop();
            source.volume = initialVolume;
        }
        currentSfxFadeRoutine = null;
    }

    /// <summary>
    /// 循环播放 BGM/环境音: <<play_bgm "clip_name">>
    /// </summary>
    [YarnCommand("play_bgm")]
    public static void PlayBgm(string clipName)
    {
        Debug.Log($"💬 [YarnCommand] play_bgm: '{clipName}'");
        AudioClip clip = ResolveAudioClip(clipName);
        if (clip == null) return;

        AudioSource source = GetBgmAudioSource();
        if (source != null)
        {
            source.clip = clip;
            source.loop = true;
            source.Play();
        }
    }

    /// <summary>
    /// 停止 BGM: <<stop_bgm>>
    /// </summary>
    [YarnCommand("stop_bgm")]
    public static void StopBgm()
    {
        Debug.Log("💬 [YarnCommand] stop_bgm");
        AudioSource source = GetBgmAudioSource();
        if (source != null && source.isPlaying)
        {
            source.Stop();
        }
    }

    private static AudioClip ResolveAudioClip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string cleanName = name.Replace(".mp3", "").Replace(".wav", "").Replace(".ogg", "").Trim();

        // 1. 在 Inspector 注册的列表中精确或别名匹配
        if (Instance != null && Instance.soundEffects != null)
        {
            NamedAudioClip found = Instance.soundEffects.Find(s => s != null && s.clip != null &&
                (string.Equals(s.name, cleanName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(s.clip.name, cleanName, StringComparison.OrdinalIgnoreCase)));
            if (found != null && found.clip != null) return found.clip;

            // 别名匹配: 蝉鸣
            if (cleanName.Equals("蝉鸣", StringComparison.OrdinalIgnoreCase) || cleanName.Equals("chanming", StringComparison.OrdinalIgnoreCase) || cleanName.Equals("cicada", StringComparison.OrdinalIgnoreCase))
            {
                NamedAudioClip cicada = Instance.soundEffects.Find(s => s != null && s.clip != null &&
                    (s.name.Contains("蝉") || s.name.IndexOf("chan", StringComparison.OrdinalIgnoreCase) >= 0 || s.clip.name.Contains("蝉")));
                if (cicada != null) return cicada.clip;
            }

            // 别名匹配: 门铃
            if (cleanName.Equals("门铃", StringComparison.OrdinalIgnoreCase) || cleanName.Equals("doorbell", StringComparison.OrdinalIgnoreCase) || cleanName.Equals("ring", StringComparison.OrdinalIgnoreCase))
            {
                NamedAudioClip bell = Instance.soundEffects.Find(s => s != null && s.clip != null &&
                    (s.name.Contains("门铃") || s.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0 || s.name.IndexOf("ring", StringComparison.OrdinalIgnoreCase) >= 0));
                if (bell != null) return bell.clip;
            }
        }

        // 2. 从 Resources 中查找
        AudioClip resClip = Resources.Load<AudioClip>(cleanName) ??
                            Resources.Load<AudioClip>("Audio/" + cleanName) ??
                            Resources.Load<AudioClip>("SFX/" + cleanName);
        if (resClip != null) return resClip;

        // 3. 从已载入的内存 AudioClip 资产中按名称匹配
        AudioClip[] allClips = Resources.FindObjectsOfTypeAll<AudioClip>();
        foreach (AudioClip ac in allClips)
        {
            if (ac == null) continue;
            if (string.Equals(ac.name, cleanName, StringComparison.OrdinalIgnoreCase) ||
                ac.name.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ac;
            }
        }

        return null;
    }

    private static AudioSource GetSfxAudioSource()
    {
        if (Instance != null && Instance.sfxAudioSource != null) return Instance.sfxAudioSource;
        return GameObject.FindObjectOfType<AudioSource>();
    }

    private static AudioSource GetBgmAudioSource()
    {
        if (Instance != null && Instance.bgmAudioSource != null) return Instance.bgmAudioSource;
        return null;
    }

    // ============================================================
    // 4. 场景跳转与闭眼转场 (Scene Transition Commands)
    // ============================================================

    /// <summary>
    /// 加载新场景（支持可选黑屏字卡文字与天数设置）:
    /// <<load_scene "Mindmaze" "入夜">>
    /// <<load_scene "chooseToolBasedOnCut" "Day 2" 2>>
    /// </summary>
    [YarnCommand("load_scene")]
    public static IEnumerator LoadScene(string sceneName, string blackScreenText = "", int nextDay = 0)
    {
        DialogueRunner runner = GetDialogueRunner();
        if (runner != null)
        {
            GlobalProgressManager.CaptureYarnVarsToGlobal(runner);
        }

        Debug.Log($"🚀 [YarnCommand] load_scene: '{sceneName}' (blackText='{blackScreenText}', nextDay={nextDay})");
        GlobalProgressManager global = GlobalProgressManager.EnsureInstance();
        yield return global.TransitionToSceneWaitForCover(sceneName, blackScreenText, nextDay);
    }

    // ============================================================
    // 5. 扫码枪与条码比对指令 (Barcode Commands)
    // ============================================================

    /// <summary>
    /// 扫码枪等待指令: <<d2_scan "03-GongChun">> 或 <<scan_barcode "03-GongChun">>
    /// 挂起 Yarn 对白，直到收到扫码枪输入（或无硬件时按键盘数字键 1 模拟）
    /// 并将比对结果写入 Yarn 变量 $D2ScanMatched
    /// </summary>
    [YarnCommand("d2_scan")]
    public static IEnumerator D2Scan(string expectedBarcode)
    {
        yield return ScanBarcodeInternal(expectedBarcode);
    }

    [YarnCommand("scan_barcode")]
    public static IEnumerator ScanBarcode(string expectedBarcode)
    {
        yield return ScanBarcodeInternal(expectedBarcode);
    }

    private static readonly (KeyCode primary, KeyCode secondary, string barcode, string label)[] DebugScanShortcuts =
    {
        (KeyCode.Alpha1, KeyCode.Keypad1, "03-GongChun",   "供春壶"),
        (KeyCode.Alpha2, KeyCode.Keypad2, "09-H2zhuangjj", "梅桩壶"),
        (KeyCode.F1,     KeyCode.None,    "03-GongChun",   "供春壶"),
        (KeyCode.F2,     KeyCode.None,    "09-H2zhuangjj", "梅桩壶"),
        (KeyCode.F3,     KeyCode.Keypad3, "09-H1FoShouJ",  "佛手提梁壶"),
        (KeyCode.F4,     KeyCode.Keypad4, "05-HeHuanHu",   "合欢壶"),
        (KeyCode.F5,     KeyCode.Keypad5, "04-FangXingHj", "方片壶"),
        (KeyCode.F9,     KeyCode.None,    "WRONG-TEST",    "错误条码测试"),
    };

    private static IEnumerator ScanBarcodeInternal(string expectedBarcode)
    {
        Debug.Log($"📷 [YarnCommand] 开始等待扫码，期望条码: '{expectedBarcode}'（无扫码枪调试：F1=供春, F2=梅桩, F3=佛手, F4=合欢, F5=方片, F9=错码; 兼容数字键1/2）");

        BarcodeScanner scanner = GetBarcodeScanner();
        DialogueRunner runner = GetDialogueRunner();

        if (runner == null)
        {
            Debug.LogError("❌ [Day1YarnCommands] 未在场景中找到 DialogueRunner，无法写入扫码结果！");
            yield break;
        }

        bool received = false;
        string scannedValue = null;

        void OnScan(string value)
        {
            if (!received)
            {
                scannedValue = value;
                received = true;
            }
        }

        runner.VariableStorage.SetValue("$D2ScanMatched", false);

        if (scanner != null)
        {
            scanner.OnBarcodeScanned.AddListener(OnScan);
        }

        try
        {
            // 挂起等待扫码枪输入（扫码枪会在末尾发送回车），或通过功能键/小键盘快速模拟测试（避免抢占 0/3/4/5/9 条码字符）
            while (!received)
            {
                for (int i = 0; i < DebugScanShortcuts.Length; i++)
                {
                    var s = DebugScanShortcuts[i];
                    if (Input.GetKeyDown(s.primary) || (s.secondary != KeyCode.None && Input.GetKeyDown(s.secondary)))
                    {
                        scannedValue = s.barcode;
                        received = true;
                        Debug.Log($"⌨️ [Day1YarnCommands] 调试快捷键模拟扫码 ({s.label}): '{scannedValue}'");
                        break;
                    }
                }

                yield return null;
            }

            // 比较条码（Trim 去除首尾空白，不区分大小写）
            string cleanScanned = scannedValue != null ? scannedValue.Trim() : "";
            string cleanExpected = expectedBarcode != null ? expectedBarcode.Trim() : "";
            bool matches = string.Equals(cleanScanned, cleanExpected, StringComparison.OrdinalIgnoreCase);

            runner.VariableStorage.SetValue("$D2ScanMatched", matches);
            Debug.Log($"🎯 [Day1YarnCommands] 扫码判定完成: 收到='{cleanScanned}', 期望='{cleanExpected}', 结果={matches}");
        }
        finally
        {
            if (scanner != null)
            {
                scanner.OnBarcodeScanned.RemoveListener(OnScan);
            }
        }
    }

    private static BarcodeScanner GetBarcodeScanner()
    {
        if (Instance != null && Instance.barcodeScanner != null) return Instance.barcodeScanner;
        BarcodeScanner scanner = GameObject.FindObjectOfType<BarcodeScanner>();
        if (scanner == null)
        {
            GameObject host = Instance != null ? Instance.gameObject : new GameObject("BarcodeScanner_Auto");
            scanner = host.AddComponent<BarcodeScanner>();
        }
        return scanner;
    }

    private static DialogueRunner GetDialogueRunner()
    {
        if (Instance != null && Instance.dialogueRunner != null) return Instance.dialogueRunner;
        return GameObject.FindObjectOfType<DialogueRunner>();
    }
}
