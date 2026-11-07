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
        PopupManager pm = GetPopupManager();
        if (pm == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] show_popup_wait: 场景中未找到 PopupManager！");
            yield break;
        }

        bool isClosed = false;
        void OnPopupClosedHandler(PopupWindowDataSO data)
        {
            isClosed = true;
        }

        pm.OnPopupClosed += OnPopupClosedHandler;
        pm.ShowById(popupId);

        // 挂起等待玩家交互关闭抽屉或确认订单
        while (!isClosed)
        {
            yield return null;
        }

        pm.OnPopupClosed -= OnPopupClosedHandler;
        Debug.Log($"💬 [YarnCommand] show_popup_wait 完成: '{popupId}'，继续推进 Yarn 对白。");
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
            if (Instance != null && Instance.jushiObject != null)
            {
                ApplyVisibility(Instance.jushiObject, visible);
            }
            else
            {
                GameObject go = FindObjectByNameOrTag("Jushi", "居士", "Character_Jushi", "FaceCaputureCanvas");
                if (go != null) ApplyVisibility(go, visible);
            }

            // 同步转头观察控制器开关（以及内部 displayImage）
            FacePerspectiveSwitcher switcher = Instance != null ? Instance.jushiFaceSwitcher : null;
            if (switcher == null) switcher = GameObject.FindObjectOfType<FacePerspectiveSwitcher>(true);
            if (switcher != null)
            {
                switcher.enabled = visible;
                if (switcher.displayImage != null)
                {
                    switcher.displayImage.enabled = visible;
                }
                Debug.Log($"💬 [Day1YarnCommands] 居士转头控制器 FacePerspectiveSwitcher: {visible}");
            }
            return;
        }

        // 2. 判断是否是老师/店长邵苇
        if (lower == "teacher" || lower == "老师" || lower == "shao" || lower == "shaowei" || lower == "shaoa")
        {
            if (Instance != null && Instance.teacherObject != null)
            {
                ApplyVisibility(Instance.teacherObject, visible);
            }
            else
            {
                GameObject go = FindObjectByNameOrTag("Teacher", "老师", "ShaoWei");
                if (go != null) ApplyVisibility(go, visible);
            }
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

    /// <summary>
    /// 播放单次音效: <<play_sfx "clip_name">>
    /// </summary>
    [YarnCommand("play_sfx")]
    public static void PlaySfx(string clipName)
    {
        Debug.Log($"💬 [YarnCommand] play_sfx: '{clipName}'");
        AudioClip clip = ResolveAudioClip(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] play_sfx: 未能找到音效 '{clipName}'！");
            return;
        }

        AudioSource source = GetSfxAudioSource();
        if (source != null)
        {
            source.PlayOneShot(clip);
        }
        else
        {
            AudioSource.PlayClipAtPoint(clip, Camera.main != null ? Camera.main.transform.position : Vector3.zero);
        }
    }

    /// <summary>
    /// 播放音效并等待结束: <<play_sfx_wait "clip_name">>
    /// </summary>
    [YarnCommand("play_sfx_wait")]
    public static IEnumerator PlaySfxWait(string clipName)
    {
        Debug.Log($"💬 [YarnCommand] play_sfx_wait: '{clipName}'");
        AudioClip clip = ResolveAudioClip(clipName);
        if (clip == null)
        {
            Debug.LogWarning($"⚠️ [Day1YarnCommands] play_sfx_wait: 未能找到音效 '{clipName}'！");
            yield break;
        }

        AudioSource source = GetSfxAudioSource();
        if (source != null)
        {
            source.clip = clip;
            source.loop = false;
            source.Play();
            yield return new WaitForSeconds(clip.length);
        }
        else
        {
            yield return new WaitForSeconds(clip.length);
        }
    }

    /// <summary>
    /// 停止音效: <<stop_sfx>>
    /// </summary>
    [YarnCommand("stop_sfx")]
    public static void StopSfx()
    {
        Debug.Log("💬 [YarnCommand] stop_sfx");
        AudioSource source = GetSfxAudioSource();
        if (source != null && source.isPlaying)
        {
            source.Stop();
        }
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
    /// 加载新场景: <<load_scene "scene_name">>
    /// </summary>
    [YarnCommand("load_scene")]
    public static void LoadScene(string sceneName)
    {
        Debug.Log($"🚀 [YarnCommand] load_scene: '{sceneName}'");
        if (SceneBlinkTransition.Instance != null)
        {
            SceneBlinkTransition.Instance.LoadSceneConcealed(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }
}
