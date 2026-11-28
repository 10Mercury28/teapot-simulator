using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Yarn.Unity;

/// <summary>
/// 脑内迷宫（MindMaze）角色动画状态机控制器
/// 挂载在场景中（如 MindmapCanvas 或 Dialogue System Variant 上）。
/// 
/// 架构设计说明：
/// - 纯 MonoBehaviour，绝不继承 DialoguePresenterBase（彻底杜绝 Yarn Spinner OptionItem 冲突与 CS0534 报错）。
/// - 自动监听 LinePresenter / characterNameText 的实时对白发言者。
/// 
/// 核心功能：
/// 1. Node 参与者控制 (Involved Speakers):
///    - 通过 Yarn 指令 <<set_speakers "宝钗, 吕蒙, 柯湘">> 动态声明当前 Node 哪些角色参与对话；
///    - 未参与的角色：动画彻底冻结静止（speed = 0，停留在初始待机帧）；
///    - 参与的角色：基准态播放各自的 Idle 动画（speed = 1）。
/// 
/// 2. 谁说话谁张嘴 (Auto Talk on Line):
///    - 对白进行中，正在发言的角色自动切到 Talk 动画；
///    - 其它参与者保持在 Idle 待机倾听；
///    - 台词念完、切到玩家“我”或弹出选项分支时，发言者自动回落到 Idle 待机。
/// 
/// 3. 一键自动装配 (Auto Setup):
///    - 启动时自动查找绑定，Inspector 右键齿轮也可一键扫描装配。
/// </summary>
public class MindMazeAnimationController : MonoBehaviour
{
    public static MindMazeAnimationController Instance { get; private set; }

    [Header("=== Yarn / UI 引用 (留空自动获取) ===")]
    public DialogueRunner dialogueRunner;
    public LinePresenter linePresenter;
    public TMP_Text characterNameText;
    public CanvasGroup lineCanvasGroup;

    [System.Serializable]
    public class CharacterEntry
    {
        [Tooltip("角色唯一 ID（如 BC, VW, MBTI, courtesan, kong, LM, Mercury, KX, zodiac, XZ, mirror, bilbo）")]
        public string id;

        [Tooltip("对白中的别名匹配（逗号隔开，如 '宝钗,BC,薛宝钗'）")]
        public string aliases = "";

        [Tooltip("角色 Animator 组件")]
        public Animator animator;

        [Tooltip("待机动画状态名（如 BC_idle, enfp_idle）")]
        public string idleStateName = "";

        [Tooltip("说话发言动画状态名（如 BC_talk, enfp_talk）")]
        public string talkStateName = "";

        [Header("运行时实时状态 (只读)")]
        [SerializeField] public bool isInvolved = false;
        [SerializeField] public bool isSpeaking = false;
    }

    [Header("=== 角色动画配置列表 ===")]
    [Tooltip("场景内所有角色的动画绑定列表")]
    public List<CharacterEntry> characters = new List<CharacterEntry>();

    [Header("=== 独立测试变量模拟（单开场景时生效） ===")]
    [Tooltip("调试模拟前一天居士交易成功 ($JushiSuccess = true)，未勾选则按真实进度走")]
    public bool debugSimulateJushiSuccess = false;

    [Tooltip("调试模拟前一天与居士交心 ($LikeU = true)")]
    public bool debugSimulateLikeU = false;

    [Header("=== 换句防抖设置 ===")]
    [Tooltip("对白框换句淡出间隙的防抖时间（秒），防止同一角色连续多句说话时句间闪回 Idle")]
    public float emptySpeakerResetDelay = 0.25f;

    private readonly List<CharacterEntry> currentSpeakers = new List<CharacterEntry>();
    private Canvas lineCanvas;
    private string lastDetectedSpeakerName = "";
    private float emptySpeakerTimer = 0f;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(this);
            return;
        }

        GlobalProgressManager.EnsureInstance();

        // 自动解析依赖组件
        ResolveDependencies();

        // 智能防呆：若从预制体继承的 startNode 仍是 Day 2 的 JushiD2_GiftMenu，自动切到脑内迷宫起点 MindMaze_OpenScene
        if (dialogueRunner != null && (string.IsNullOrEmpty(dialogueRunner.startNode) || dialogueRunner.startNode == "JushiD2_GiftMenu"))
        {
            dialogueRunner.startNode = "MindMaze_OpenScene";
            Debug.Log("🎭 [MindMazeAnimationController] 自动将 DialogueRunner.startNode 设定为 MindMaze_OpenScene。");
        }

        // 若列表为空，自动从场景/子物体扫描装配
        if (characters == null || characters.Count == 0)
        {
            AutoSetupCharacters();
        }
        EnsureBuiltInAliases();

        // 初始让所有角色全部静止（等待首个 Node 指令唤醒参与者）
        FreezeAllCharacters();
    }

    private void Start()
    {
        SyncVarsToYarn();
    }

    private void OnEnable()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.onDialogueStart?.AddListener(SyncVarsToYarn);
            dialogueRunner.onNodeStart?.AddListener(OnNodeStart);
            dialogueRunner.onDialogueComplete?.AddListener(OnDialogueComplete);
        }
    }

    private void OnDisable()
    {
        if (dialogueRunner != null)
        {
            dialogueRunner.onDialogueStart?.RemoveListener(SyncVarsToYarn);
            dialogueRunner.onNodeStart?.RemoveListener(OnNodeStart);
            dialogueRunner.onDialogueComplete?.RemoveListener(OnDialogueComplete);
        }
    }

    private void SyncVarsToYarn()
    {
        if (debugSimulateJushiSuccess) GlobalProgressManager.JushiSuccess = true;
        if (debugSimulateLikeU) GlobalProgressManager.IsLikeU = true;
        GlobalProgressManager.SyncGlobalVarsToYarn(dialogueRunner);
    }

    private void ResolveDependencies()
    {
        if (dialogueRunner == null)
        {
            dialogueRunner = GetComponent<DialogueRunner>() ?? FindObjectOfType<DialogueRunner>();
        }

        if (linePresenter == null)
        {
            linePresenter = GetComponent<LinePresenter>() ?? FindObjectOfType<LinePresenter>();
        }

        if (linePresenter != null)
        {
            if (characterNameText == null)
            {
                characterNameText = linePresenter.characterNameText;
            }
            if (lineCanvasGroup == null)
            {
                lineCanvasGroup = linePresenter.canvasGroup;
            }
        }

        // 兜底查找 Character Name 文本组件
        if (characterNameText == null)
        {
            GameObject nameObj = GameObject.Find("Character Name");
            if (nameObj != null)
            {
                characterNameText = nameObj.GetComponent<TMP_Text>();
            }
        }

        if (lineCanvas == null)
        {
            if (lineCanvasGroup != null) lineCanvas = lineCanvasGroup.GetComponentInParent<Canvas>();
            else if (linePresenter != null) lineCanvas = linePresenter.GetComponentInParent<Canvas>();
        }
    }

    // ============================================================
    // 1. 每帧侦测当前台词发言者（自动触发 Talk / 回落 Idle）
    // ============================================================

    private void Update()
    {
        string detectedSpeaker = GetActiveSpeakerName();

        if (string.IsNullOrEmpty(detectedSpeaker))
        {
            if (!string.IsNullOrEmpty(lastDetectedSpeakerName) || currentSpeakers.Count > 0)
            {
                emptySpeakerTimer += Time.unscaledDeltaTime;
                if (emptySpeakerTimer >= emptySpeakerResetDelay)
                {
                    emptySpeakerTimer = 0f;
                    lastDetectedSpeakerName = "";
                    HandleSpeakerChanged("");
                }
            }
            return;
        }

        emptySpeakerTimer = 0f;

        if (detectedSpeaker != lastDetectedSpeakerName)
        {
            lastDetectedSpeakerName = detectedSpeaker;
            HandleSpeakerChanged(detectedSpeaker);
        }
    }

    private string GetActiveSpeakerName()
    {
        if (lineCanvas != null && !lineCanvas.enabled)
        {
            return "";
        }

        // 检查对白框是否正在显示
        bool isLineVisible = true;
        if (lineCanvasGroup != null)
        {
            isLineVisible = lineCanvasGroup.alpha > 0.05f;
        }
        else if (linePresenter != null)
        {
            isLineVisible = linePresenter.gameObject.activeInHierarchy;
        }

        if (!isLineVisible)
        {
            return "";
        }

        // 检查发言人名字
        if (characterNameText != null)
        {
            bool nameActive = true;
            if (linePresenter != null && linePresenter.characterNameContainer != null)
            {
                nameActive = linePresenter.characterNameContainer.activeInHierarchy;
            }
            else
            {
                nameActive = characterNameText.gameObject.activeInHierarchy;
            }

            if (nameActive && !string.IsNullOrWhiteSpace(characterNameText.text))
            {
                return characterNameText.text.Trim();
            }
        }

        return "";
    }

    private static bool IsPlayerSpeakerName(string name)
    {
        string normalized = NormalizeSpeakerToken(name);
        return normalized == "我" || normalized == "玩家" || normalized == "店主";
    }

    private void RevertCharacterAfterSpeaking(CharacterEntry entry)
    {
        if (entry == null) return;
        SetCharacterState(entry, entry.isInvolved ? CharacterAnimState.Idle : CharacterAnimState.Frozen);
    }

    private void HandleSpeakerChanged(string speakerName)
    {
        if (string.IsNullOrEmpty(speakerName))
        {
            // 此时无台词（如选项显示中、打字结束淡出、或旁白无说话人）
            ResetCurrentSpeakerToIdle();
            return;
        }

        // 过滤玩家自己或不需要做动画的角色
        if (IsPlayerSpeakerName(speakerName))
        {
            ResetCurrentSpeakerToIdle();
            return;
        }

        List<CharacterEntry> matchedSpeakers = new List<CharacterEntry>();
        string[] tokens = speakerName.Split(new char[] { '、', ',', '，' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawToken in tokens)
        {
            string token = rawToken.Trim();
            if (string.IsNullOrEmpty(token) || IsPlayerSpeakerName(token))
                continue;

            CharacterEntry matched = FindCharacter(token);
            if (matched != null && !matchedSpeakers.Contains(matched))
            {
                matchedSpeakers.Add(matched);
            }
        }

        if (matchedSpeakers.Count == 0)
        {
            // 未在角色映射表中（例如未知旁白），之前发言人回落 Idle
            ResetCurrentSpeakerToIdle();
            return;
        }

        // 上一批中不再继续发言的角色回落 Idle（或 Frozen）
        for (int i = 0; i < currentSpeakers.Count; i++)
        {
            CharacterEntry prev = currentSpeakers[i];
            if (prev != null && !matchedSpeakers.Contains(prev))
            {
                RevertCharacterAfterSpeaking(prev);
            }
        }

        currentSpeakers.Clear();
        currentSpeakers.AddRange(matchedSpeakers);

        // 发言人必须切到 Talk 动画
        for (int i = 0; i < currentSpeakers.Count; i++)
        {
            CharacterEntry entry = currentSpeakers[i];
            entry.isInvolved = true;
            SetCharacterState(entry, CharacterAnimState.Talk);
            Debug.Log($"🗣️ [MindMaze] 角色发言: '{entry.id}' -> 播放 Talk ('{entry.talkStateName}')");
        }
    }

    private void ResetCurrentSpeakerToIdle()
    {
        for (int i = 0; i < currentSpeakers.Count; i++)
        {
            RevertCharacterAfterSpeaking(currentSpeakers[i]);
        }
        currentSpeakers.Clear();
    }

    private void OnNodeStart(string nodeName)
    {
        // 节点切换时，清除当前发言状态
        emptySpeakerTimer = 0f;
        ResetCurrentSpeakerToIdle();
        lastDetectedSpeakerName = "";
    }

    private void OnDialogueComplete()
    {
        // 对话彻底结束
        emptySpeakerTimer = 0f;
        ResetCurrentSpeakerToIdle();
        lastDetectedSpeakerName = "";
    }

    // ============================================================
    // 2. Yarn 指令：设置当前 Node 涉及的角色
    // ============================================================

    /// <summary>
    /// Yarn 指令：设置当前 Node 涉及的角色（逗号、顿号或空格分隔）
    /// 例如：
    /// <<set_speakers "宝钗, 吕蒙, 柯湘">>
    /// <<set_speakers "MBTI, 伍尔夫, 宝钗">>
    /// <<set_speakers "all">>     (全员唤醒 Idle)
    /// <<set_speakers "none">>    (全员冻结静止)
    /// </summary>
    [YarnCommand("set_speakers")]
    public static void SetSpeakers(string speakerListStr = "")
    {
        if (Instance == null)
        {
            Instance = FindObjectOfType<MindMazeAnimationController>();
        }

        if (Instance != null)
        {
            Instance.ApplyInvolvedSpeakers(speakerListStr);
        }
        else
        {
            Debug.LogWarning("⚠️ [MindMazeAnimationController] 场景中未找到 MindMazeAnimationController 实例！");
        }
    }

    /// <summary>
    /// 便捷别名：<<set_node_speakers "BC, VW">>
    /// </summary>
    [YarnCommand("set_node_speakers")]
    public static void SetNodeSpeakers(string speakerListStr = "")
    {
        SetSpeakers(speakerListStr);
    }

    public void ApplyInvolvedSpeakers(string speakerListStr)
    {
        Debug.Log($"🎭 [MindMaze] 设置当前 Node 涉及角色: '{speakerListStr}'");

        emptySpeakerTimer = 0f;
        lastDetectedSpeakerName = "";
        currentSpeakers.Clear();

        string raw = string.IsNullOrEmpty(speakerListStr) ? "" : speakerListStr.Trim().ToLowerInvariant();
        bool isAll = raw == "all" || raw == "全部" || raw == "所有人";
        bool isNone = string.IsNullOrEmpty(raw) || raw == "none" || raw == "无" || raw == "没人";

        HashSet<string> targetTokens = new HashSet<string>();
        if (!isAll && !isNone)
        {
            char[] splitChars = new char[] { ',', '，', '、', ' ', ';', '；', '|' };
            string[] tokens = raw.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
            foreach (string t in tokens)
            {
                targetTokens.Add(t.Trim());
            }
        }

        foreach (var c in characters)
        {
            if (c == null || c.animator == null) continue;

            bool shouldBeInvolved = false;
            if (isAll)
            {
                shouldBeInvolved = true;
            }
            else if (isNone)
            {
                shouldBeInvolved = false;
            }
            else
            {
                shouldBeInvolved = MatchTokens(c, targetTokens);
            }

            c.isInvolved = shouldBeInvolved;

            if (shouldBeInvolved)
            {
                // 参与的角色播放待机 Idle（微动）
                SetCharacterState(c, CharacterAnimState.Idle);
            }
            else
            {
                // 未参与的角色彻底冻结（不播 Idle）
                SetCharacterState(c, CharacterAnimState.Frozen);
            }
        }
    }

    private bool MatchTokens(CharacterEntry entry, HashSet<string> tokens)
    {
        if (tokens.Contains(entry.id.ToLowerInvariant())) return true;

        if (!string.IsNullOrEmpty(entry.aliases))
        {
            string[] aliasList = entry.aliases.Split(new char[] { ',', '，', '、', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string a in aliasList)
            {
                if (tokens.Contains(a.Trim().ToLowerInvariant())) return true;
            }
        }
        return false;
    }

    // ============================================================
    // 3. 动画状态切换核心逻辑
    // ============================================================

    public enum CharacterAnimState
    {
        Frozen, // 冻结静止（停在第 0 帧，speed = 0）
        Idle,   // 待机呼吸微动（speed = 1，循环 Idle）
        Talk    // 正在发言说话（speed = 1，循环 Talk）
    }

    private static void PlayStateIfNeeded(Animator anim, string stateName)
    {
        if (string.IsNullOrEmpty(stateName)) return;

        AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
        if (anim.IsInTransition(0) || !info.IsName(stateName))
        {
            anim.Play(stateName, 0);
        }
    }

    public void SetCharacterState(CharacterEntry entry, CharacterAnimState state)
    {
        if (entry == null || entry.animator == null) return;
        Animator anim = entry.animator;

        switch (state)
        {
            case CharacterAnimState.Frozen:
                entry.isSpeaking = false;
                anim.speed = 0f;
                if (!string.IsNullOrEmpty(entry.idleStateName))
                {
                    anim.Play(entry.idleStateName, 0, 0f);
                }
                break;

            case CharacterAnimState.Idle:
                entry.isSpeaking = false;
                anim.speed = 1f;
                PlayStateIfNeeded(anim, entry.idleStateName);
                break;

            case CharacterAnimState.Talk:
                entry.isSpeaking = true;
                anim.speed = 1f;
                string talkState = !string.IsNullOrEmpty(entry.talkStateName) ? entry.talkStateName : entry.idleStateName;
                PlayStateIfNeeded(anim, talkState);
                break;
        }
    }

    /// <summary>
    /// Yarn 指令：全部角色彻底冻结
    /// </summary>
    [YarnCommand("freeze_all")]
    public static void FreezeAll()
    {
        if (Instance == null) Instance = FindObjectOfType<MindMazeAnimationController>();
        if (Instance != null) Instance.FreezeAllCharacters();
    }

    public void FreezeAllCharacters()
    {
        emptySpeakerTimer = 0f;
        lastDetectedSpeakerName = "";
        currentSpeakers.Clear();

        foreach (var c in characters)
        {
            if (c != null)
            {
                c.isInvolved = false;
                SetCharacterState(c, CharacterAnimState.Frozen);
            }
        }
    }

    // ============================================================
    // 4. 便捷查找与一键自动装配
    // ============================================================

    private static string NormalizeSpeakerToken(string value)
    {
        return string.IsNullOrEmpty(value)
            ? ""
            : value.Trim().Trim('“', '”', '"', '‘', '’', '\'').Trim().ToLowerInvariant();
    }

    public CharacterEntry FindCharacter(string searchKey)
    {
        if (string.IsNullOrEmpty(searchKey)) return null;
        string clean = searchKey.Trim().ToLowerInvariant();
        string stripped = NormalizeSpeakerToken(searchKey);

        foreach (var c in characters)
        {
            if (c == null) continue;
            string idLower = (c.id ?? "").Trim().ToLowerInvariant();
            string idStripped = NormalizeSpeakerToken(c.id);
            if (idLower == clean || (!string.IsNullOrEmpty(stripped) && idStripped == stripped))
                return c;

            if (!string.IsNullOrEmpty(c.aliases))
            {
                string[] aliasList = c.aliases.Split(new char[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string a in aliasList)
                {
                    string aliasLower = a.Trim().ToLowerInvariant();
                    string aliasStripped = NormalizeSpeakerToken(a);
                    if (aliasLower == clean || (!string.IsNullOrEmpty(stripped) && aliasStripped == stripped))
                        return c;
                }
            }
        }
        return null;
    }

    private static readonly (string id, string aliases, string childName, string idle, string talk)[] BuiltInCharacterCatalog =
    {
        ("BC", "BC,宝钗,薛宝钗,BaoChai,Baochai,Xue Baochai", "BC", "BC_idle", "BC_talk"),
        ("VW", "VW,伍尔夫,弗吉尼亚,Virginia,Virginia Woolf,Woolf", "VW", "VW_idle", "VW_talk"),
        ("courtesan", "courtesan,名妓,C,Courtesan,Late Ming Courtesan,Oiran", "courtesan", "C_idle", "C_talk"),
        ("MBTI", "MBTI,mbti,enfp,ENFP", "MBTIPanel", "enfp_idle", "enfp_talk"),
        ("kong", "kong,孔子,金猴,Confucius,Golden Monkey,Monkey", "kong", "kong idle", "kong idle"),
        ("LM", "LM,吕蒙,Lu Meng,Lü Meng,Lv Meng,Lumeng", "LM", "LM_idle", "LM_talk"),
        ("Mercury", "Mercury,MercuryPanel,水星,赫尔墨斯,“赫尔墨斯”,Hermes", "MercuryPanel", "Mercury_Idle", "Mercury_Talk"),
        ("KX", "KX,柯湘,康熙,Ke Xiang,Kangxi,Kexiang", "KX", "KX_idle", "KX_talk"),
        ("zodiac", "zodiac,生肖,Zodiac,Chinese Zodiac", "zodiac", "zodiac_idle", "zodiac_talk"),
        ("XZ", "XZ,星座,Constellation,Horoscope,Star Sign,Astrology", "星座", "XZ_idle", "XZ_talk"),
        ("mirror", "mirror,镜中人,“镜中人”,镜子,奥斯卡,镜子中的“奥斯卡”,The Mirror,Person in the Mirror,The Person in the Mirror,Mirror Person,Man in the Mirror,Woman in the Mirror,Oscar,Oscar in the Mirror", "mirror", "Mirror_Idle", "Mirror_Talk"),
        ("bilbo", "bilbo,比尔博,“比尔博”,Bilbo,Bilbo Baggins", "bilbo", "Bilbo_idle", "Bilbo_talk")
    };

    private void EnsureBuiltInAliases()
    {
        if (characters == null || characters.Count == 0) return;

        foreach (var entry in characters)
        {
            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
            string entryId = entry.id.Trim();

            for (int i = 0; i < BuiltInCharacterCatalog.Length; i++)
            {
                if (!string.Equals(BuiltInCharacterCatalog[i].id, entryId, StringComparison.OrdinalIgnoreCase))
                    continue;

                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(entry.aliases))
                {
                    foreach (var token in entry.aliases.Split(new char[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string trimmed = token.Trim();
                        if (!string.IsNullOrEmpty(trimmed)) existing.Add(trimmed);
                    }
                }

                var merged = new List<string>(existing);
                foreach (var builtInToken in BuiltInCharacterCatalog[i].aliases.Split(','))
                {
                    string trimmed = builtInToken.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && existing.Add(trimmed))
                    {
                        merged.Add(trimmed);
                    }
                }

                entry.aliases = string.Join(",", merged);
                break;
            }
        }
    }

    /// <summary>
    /// 一键扫描并自动填充场景内角色的 Animator 和对应状态名
    /// 可在 Inspector 组件右上角齿轮点击 "Auto Setup Characters From Hierarchy" 触发
    /// </summary>
    [ContextMenu("Auto Setup Characters From Hierarchy")]
    public void AutoSetupCharacters()
    {
        characters.Clear();

        // 预定义工程中已有的知名角色映射表（同时覆盖中/英文别名）
        var defaultCatalog = BuiltInCharacterCatalog;

        foreach (var item in defaultCatalog)
        {
            GameObject targetObj = GameObject.Find(item.childName);
            if (targetObj == null)
            {
                Transform child = FindDeepChild(transform, item.childName);
                if (child != null) targetObj = child.gameObject;
            }

            // 针对带引号命名的特别查找（如 "bilbo"）
            if (targetObj == null)
            {
                Transform child = FindDeepChildLoose(transform.root, item.childName);
                if (child != null) targetObj = child.gameObject;
            }

            if (targetObj != null)
            {
                Animator anim = targetObj.GetComponent<Animator>() ?? targetObj.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    CharacterEntry entry = new CharacterEntry
                    {
                        id = item.id,
                        aliases = item.aliases,
                        animator = anim,
                        idleStateName = item.idle,
                        talkStateName = item.talk
                    };
                    characters.Add(entry);
                }
            }
        }

        Debug.Log($"🎉 [MindMazeAnimationController] 自动装配完成，共找到并绑定了 {characters.Count} 位角色的 Animator。");
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

    private static Transform FindDeepChildLoose(Transform parent, string cleanName)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            string stripped = child.name.Replace("\"", "").Trim();
            if (stripped.IndexOf(cleanName, StringComparison.OrdinalIgnoreCase) >= 0)
                return child;
            Transform result = FindDeepChildLoose(child, cleanName);
            if (result != null) return result;
        }
        return null;
    }
}
