using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Yarn.Unity;

/// <summary>
/// 脑内迷宫（MindMaze）全流程舞台动画总控
/// 统一编排：开场边框(OS) 与 右手入场(OpenAni->OFRT->RI) 同步启动 -> OS 播完隐藏、右手进入 RI 时左手启动 hand_idle -> 开场白
/// -> 摇骰判定(Roll -> 对应T -> 对应I) -> 左手点击(hand_click -> hand_choose)
/// -> 多点位的选择特效(select) -> 对白期间保持待机 -> 结束后双手复位(DIRT->RI & hand_idle)
/// -> 离场边框(Close) 并切换下一场景。
/// </summary>
public class MindMazeStageDirector : MonoBehaviour
{
    public static MindMazeStageDirector Instance { get; private set; }

    [Header("=== Yarn / 角色控制引用 (留空自动获取) ===")]
    public DialogueRunner dialogueRunner;
    public MindMazeAnimationController animationController;
    public string openSceneNode = "MindMaze_OpenScene";

    [Header("=== 舞台动画组件 (留空自动按子物体名查找) ===")]
    public Animator openSceneAnimator;
    public Animator closeSceneAnimator;
    public Animator rightHandAnimator;
    public Animator leftHandAnimator;
    public Animator selectTemplate;

    [Header("=== 基础状态名配置 ===")]
    public string openBorderState = "OS";
    public string closeBorderState = "Close";
    public string rightOpenState = "OpenAni";
    public string rightIdleState = "RI";
    public string rightRollState = "Roll";
    public string rightResetState = "DIRT";
    public string leftIdleState = "hand_idle";
    public string leftClickState = "hand_click";
    public string leftChooseState = "hand_choose";
    public string selectPlayState = "select";

    [Header("=== 开场节奏控制 ===")]
    [Tooltip("若通过 SceneBlinkTransition 淡入进入场景，等黑屏揭开后再开始播 OS 边框动画")]
    public bool waitForSceneBlinkFadeIn = true;
    public float openingDelay = 0f;
    public bool debugLog = true;

    [Serializable]
    public class RollAnimConfig
    {
        public int rollId;
        public string note;
        public string rightTransState;
        public string rightIdleState;
        [Tooltip("本段 Roll 要播放 Select 特效的位置槽位；填 1 个就冒 1 处，填多个可同时多点冒云")]
        public List<RectTransform> selectSpots = new List<RectTransform>();
    }

    [Header("=== 5 段判定对应的右手状态与 Select 点位 ===")]
    public List<RollAnimConfig> rollConfigs = new List<RollAnimConfig>();

    private bool handsInSelectedPose = false;
    private bool hasCachedDefaultSelectRect = false;
    private Vector2 defaultSelectAnchorMin;
    private Vector2 defaultSelectAnchorMax;
    private Vector2 defaultSelectAnchoredPos;
    private Vector2 defaultSelectSizeDelta;
    private Vector2 defaultSelectPivot;
    private Quaternion defaultSelectRotation;
    private Vector3 defaultSelectScale;

    private readonly List<GameObject> activeSelectClones = new List<GameObject>();

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

        ResolveReferences();
        EnsureDefaultRollConfigs();

        // 防止 DialogueRunner 在开场边框/右手就位前抢跑弹框
        if (dialogueRunner != null)
        {
            dialogueRunner.autoStart = false;
            dialogueRunner.startNode = openSceneNode;
        }

        PrepareInitialStageState();
    }

    private void Start()
    {
        StartCoroutine(PlayOpeningSequenceRoutine());
    }

    private void OnDestroy()
    {
        CleanupSelectClones();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void ResolveReferences()
    {
        if (dialogueRunner == null)
        {
            dialogueRunner = GetComponent<DialogueRunner>() ?? FindObjectOfType<DialogueRunner>(true);
        }

        if (animationController == null)
        {
            animationController = GetComponent<MindMazeAnimationController>() ?? FindObjectOfType<MindMazeAnimationController>(true);
        }

        if (openSceneAnimator == null) openSceneAnimator = FindChildAnimator("Open scene");
        if (closeSceneAnimator == null) closeSceneAnimator = FindChildAnimator("endscene");
        if (rightHandAnimator == null) rightHandAnimator = FindChildAnimator("Right");
        if (leftHandAnimator == null) leftHandAnimator = FindChildAnimator("Left");
        if (selectTemplate == null) selectTemplate = FindChildAnimator("select");
    }

    private Animator FindChildAnimator(string targetName)
    {
        Transform match = FindDeepChildIgnoreCase(transform, targetName);
        if (match == null && transform.root != transform)
        {
            match = FindDeepChildIgnoreCase(transform.root, targetName);
        }
        return match != null ? match.GetComponent<Animator>() : null;
    }

    private static Transform FindDeepChildIgnoreCase(Transform parent, string targetName)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (string.Equals(child.name.Trim(), targetName, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
            Transform found = FindDeepChildIgnoreCase(child, targetName);
            if (found != null) return found;
        }
        return null;
    }

    private void EnsureDefaultRollConfigs()
    {
        if (rollConfigs == null)
        {
            rollConfigs = new List<RollAnimConfig>();
        }

        if (rollConfigs.Count > 0) return;

        rollConfigs.Add(new RollAnimConfig
        {
            rollId = 1,
            note = "MindMaze_1 (5点)",
            rightTransState = "5T",
            rightIdleState = "5I"
        });
        rollConfigs.Add(new RollAnimConfig
        {
            rollId = 2,
            note = "MindMaze_2 (3点)",
            rightTransState = "3T",
            rightIdleState = "3I"
        });
        rollConfigs.Add(new RollAnimConfig
        {
            rollId = 3,
            note = "MindMaze_3 (6点)",
            rightTransState = "6T",
            rightIdleState = "6I"
        });
        rollConfigs.Add(new RollAnimConfig
        {
            rollId = 4,
            note = "MindMaze_4 (11点)",
            rightTransState = "11T",
            rightIdleState = "11I"
        });
        rollConfigs.Add(new RollAnimConfig
        {
            rollId = 5,
            note = "MindMaze_5 (失败线占位-暂用11)",
            rightTransState = "11T",
            rightIdleState = "11I"
        });
    }

    private void PrepareInitialStageState()
    {
        if (closeSceneAnimator != null) closeSceneAnimator.gameObject.SetActive(false);

        if (selectTemplate != null)
        {
            CacheDefaultSelectRect();
            Image selectImg = selectTemplate.GetComponent<Image>();
            if (selectImg != null) selectImg.raycastTarget = false;
            selectTemplate.gameObject.SetActive(false);
        }

        if (rightHandAnimator != null) rightHandAnimator.gameObject.SetActive(false);
        if (leftHandAnimator != null) leftHandAnimator.gameObject.SetActive(false);

        if (openSceneAnimator != null)
        {
            openSceneAnimator.gameObject.SetActive(true);
            openSceneAnimator.speed = 0f;
            openSceneAnimator.Play(openBorderState, 0, 0f);
            openSceneAnimator.Update(0f);
        }
    }

    private IEnumerator PlayOpeningSequenceRoutine()
    {
        if (waitForSceneBlinkFadeIn && SceneBlinkTransition.Instance != null)
        {
            float blinkTimeout = 3.0f;
            while (SceneBlinkTransition.Instance != null &&
                   SceneBlinkTransition.Instance.IsTransitioning &&
                   blinkTimeout > 0f)
            {
                blinkTimeout -= Time.unscaledDeltaTime;
                yield return null;
            }
        }

        if (openingDelay > 0f)
        {
            yield return new WaitForSeconds(openingDelay);
        }

        // 1. 开场边框 OS 与 右手入场 (OpenAni -> OFRT -> RI) 同帧启动；OS 播完后隐藏
        bool openBorderDone = (openSceneAnimator == null);
        if (openSceneAnimator != null)
        {
            StartCoroutine(PlayAndHideOpenBorderRoutine(() => openBorderDone = true));
        }

        if (rightHandAnimator != null)
        {
            rightHandAnimator.gameObject.SetActive(true);
            rightHandAnimator.speed = 1f;
            rightHandAnimator.Play(rightOpenState, 0, 0f);
            rightHandAnimator.Update(0f);

            if (debugLog) Debug.Log("✋ [MindMazeStage] 播放开场边框 OS 的同时启动右手 OpenAni -> OFRT -> RI...");
            yield return WaitForAnimatorEnterState(rightHandAnimator, rightIdleState, 4.5f, null);
        }

        // 2. 右手正式进入 RI 时，左手开始播放 hand_idle (loop)
        if (leftHandAnimator != null)
        {
            leftHandAnimator.gameObject.SetActive(true);
            leftHandAnimator.speed = 1f;
            leftHandAnimator.Play(leftIdleState, 0, 0f);
            leftHandAnimator.Update(0f);
            if (debugLog) Debug.Log("🤚 [MindMazeStage] 右手已进入 RI，左手启动 hand_idle");
        }

        // 确保开场边框 OS 彻底播完并隐藏
        while (!openBorderDone)
        {
            yield return null;
        }

        // 3. 启动 Yarn 开场白
        if (dialogueRunner != null && !dialogueRunner.IsDialogueRunning)
        {
            GlobalProgressManager.EnsureInstance();
            if (animationController != null)
            {
                if (animationController.debugSimulateJushiSuccess) GlobalProgressManager.JushiSuccess = true;
                if (animationController.debugSimulateLikeU) GlobalProgressManager.IsLikeU = true;
            }
            GlobalProgressManager.SyncGlobalVarsToYarn(dialogueRunner);

            if (debugLog) Debug.Log($"💬 [MindMazeStage] 开场就位，启动 Yarn 节点: {openSceneNode}");
            dialogueRunner.StartDialogue(openSceneNode).Forget();
        }
    }

    private IEnumerator PlayAndHideOpenBorderRoutine(Action onComplete)
    {
        if (openSceneAnimator != null)
        {
            openSceneAnimator.gameObject.SetActive(true);
            openSceneAnimator.speed = 1f;
            openSceneAnimator.Play(openBorderState, 0, 0f);
            openSceneAnimator.Update(0f);

            if (debugLog) Debug.Log("🎬 [MindMazeStage] 播放开场边框 OS...");
            yield return WaitForAnimatorStateFinished(openSceneAnimator, openBorderState, 3.0f);
            openSceneAnimator.gameObject.SetActive(false);
        }

        onComplete?.Invoke();
    }

    // ============================================================
    // Yarn 阻塞式舞台控制指令
    // ============================================================

    /// <summary>
    /// 播放指定段落的 Roll 抽签与选定动画（Roll -> xT -> xI + hand_click -> hand_choose + select）
    /// </summary>
    [YarnCommand("mindmaze_roll_anim")]
    public static IEnumerator YarnRollAnim(int rollId)
    {
        MindMazeStageDirector director = Instance != null ? Instance : FindObjectOfType<MindMazeStageDirector>(true);
        if (director != null)
        {
            yield return director.PlayRollSequenceRoutine(rollId);
        }
        else
        {
            Debug.LogWarning($"⚠️ [MindMazeStage] 未找到 MindMazeStageDirector，跳过 mindmaze_roll_anim({rollId})");
        }
    }

    /// <summary>
    /// 本段对白结束后，右手播 DIRT -> RI（loop），左手回到 hand_idle（loop）
    /// </summary>
    [YarnCommand("mindmaze_reset_hands")]
    public static IEnumerator YarnResetHands()
    {
        MindMazeStageDirector director = Instance != null ? Instance : FindObjectOfType<MindMazeStageDirector>(true);
        if (director != null)
        {
            yield return director.ResetHandsToIdleRoutine();
        }
    }

    /// <summary>
    /// 播放场景结束离场边框动画 Close，等播完后再交由后续 load_scene 切场
    /// </summary>
    [YarnCommand("mindmaze_close_scene")]
    public static IEnumerator YarnCloseScene()
    {
        MindMazeStageDirector director = Instance != null ? Instance : FindObjectOfType<MindMazeStageDirector>(true);
        if (director != null)
        {
            yield return director.PlayCloseSceneRoutine();
        }
    }

    // ============================================================
    // 核心动画编排协程
    // ============================================================

    public IEnumerator PlayRollSequenceRoutine(int rollId)
    {
        RollAnimConfig cfg = GetRollConfig(rollId);
        if (cfg == null)
        {
            Debug.LogWarning($"⚠️ [MindMazeStage] 未配置 rollId={rollId} 的动画参数，跳过本次 Roll 表现");
            yield break;
        }

        if (debugLog) Debug.Log($"🎲 [MindMazeStage] 开始 Roll {rollId} ({cfg.note}): Roll -> {cfg.rightTransState} -> {cfg.rightIdleState}");

        if (leftHandAnimator != null)
        {
            leftHandAnimator.gameObject.SetActive(true);
            leftHandAnimator.speed = 1f;
        }

        // 1. 右手播放 Roll -> 对应 T -> 自动转入对应 I (loop)
        if (rightHandAnimator != null)
        {
            rightHandAnimator.gameObject.SetActive(true);
            rightHandAnimator.speed = 1f;

            rightHandAnimator.Play(rightRollState, 0, 0f);
            rightHandAnimator.Update(0f);
            yield return WaitForAnimatorStateFinished(rightHandAnimator, rightRollState, 2.5f);

            if (!string.IsNullOrEmpty(cfg.rightTransState))
            {
                rightHandAnimator.Play(cfg.rightTransState, 0, 0f);
                rightHandAnimator.Update(0f);
                yield return WaitForAnimatorEnterState(rightHandAnimator, cfg.rightIdleState, 2.5f, cfg.rightTransState);
            }
            else if (!string.IsNullOrEmpty(cfg.rightIdleState))
            {
                rightHandAnimator.Play(cfg.rightIdleState, 0, 0f);
                rightHandAnimator.Update(0f);
            }
        }

        handsInSelectedPose = true;

        // 2. 右手落定各自 T -> I 后，左手播放 hand_click，播完转 hand_choose (loop)
        if (leftHandAnimator != null)
        {
            leftHandAnimator.Play(leftClickState, 0, 0f);
            leftHandAnimator.Update(0f);
            yield return WaitForAnimatorStateFinished(leftHandAnimator, leftClickState, 2.0f);

            leftHandAnimator.Play(leftChooseState, 0, 0f);
            leftHandAnimator.Update(0f);
        }

        // 3. hand_click 结束同一刻：按配置的槽位（单点或多点）播放 select 特效，播完隐藏
        yield return PlaySelectAtSpotsRoutine(cfg.selectSpots);

        if (debugLog) Debug.Log($"✨ [MindMazeStage] Roll {rollId} 特效播完，双手保持在 {cfg.rightIdleState} + {leftChooseState} 进入对白");
    }

    private IEnumerator PlaySelectAtSpotsRoutine(List<RectTransform> rawSpots)
    {
        if (selectTemplate == null) yield break;

        CacheDefaultSelectRect();
        CleanupSelectClones();

        List<RectTransform> validSpots = new List<RectTransform>();
        if (rawSpots != null)
        {
            for (int i = 0; i < rawSpots.Count; i++)
            {
                if (rawSpots[i] != null) validSpots.Add(rawSpots[i]);
            }
        }

        RectTransform templateRect = selectTemplate.GetComponent<RectTransform>();

        if (validSpots.Count == 0)
        {
            RestoreDefaultSelectRect(templateRect);
        }
        else
        {
            ApplySpotTransform(templateRect, validSpots[0]);
        }

        Image templateImg = selectTemplate.GetComponent<Image>();
        if (templateImg != null) templateImg.raycastTarget = false;

        selectTemplate.gameObject.SetActive(true);
        selectTemplate.speed = 1f;
        selectTemplate.Play(selectPlayState, 0, 0f);
        selectTemplate.Update(0f);

        // 若同一段需要多处同时冒云，按其余点位临时克隆播放并在结束后销毁
        for (int i = 1; i < validSpots.Count; i++)
        {
            GameObject cloneObj = Instantiate(selectTemplate.gameObject, selectTemplate.transform.parent);
            cloneObj.name = $"{selectTemplate.name}_Runtime_{i}";
            cloneObj.transform.SetSiblingIndex(selectTemplate.transform.GetSiblingIndex() + i);

            RectTransform cloneRect = cloneObj.GetComponent<RectTransform>();
            ApplySpotTransform(cloneRect, validSpots[i]);

            Image cloneImg = cloneObj.GetComponent<Image>();
            if (cloneImg != null) cloneImg.raycastTarget = false;

            Animator cloneAnim = cloneObj.GetComponent<Animator>();
            if (cloneAnim != null)
            {
                cloneAnim.speed = 1f;
                cloneAnim.Play(selectPlayState, 0, 0f);
                cloneAnim.Update(0f);
            }

            activeSelectClones.Add(cloneObj);
        }

        yield return WaitForAnimatorStateFinished(selectTemplate, selectPlayState, 2.5f);

        selectTemplate.gameObject.SetActive(false);
        CleanupSelectClones();
    }

    public IEnumerator ResetHandsToIdleRoutine()
    {
        if (!handsInSelectedPose) yield break;
        handsInSelectedPose = false;

        if (debugLog) Debug.Log("🔄 [MindMazeStage] 对白结束，右手 DIRT -> RI，左手回到 hand_idle");

        if (rightHandAnimator != null)
        {
            rightHandAnimator.gameObject.SetActive(true);
            rightHandAnimator.speed = 1f;
            rightHandAnimator.Play(rightResetState, 0, 0f);
            rightHandAnimator.Update(0f);
            yield return WaitForAnimatorEnterState(rightHandAnimator, rightIdleState, 2.0f, rightResetState);
        }

        if (leftHandAnimator != null)
        {
            leftHandAnimator.gameObject.SetActive(true);
            leftHandAnimator.speed = 1f;
            leftHandAnimator.Play(leftIdleState, 0, 0f);
            leftHandAnimator.Update(0f);
        }
    }

    public IEnumerator PlayCloseSceneRoutine()
    {
        if (selectTemplate != null) selectTemplate.gameObject.SetActive(false);
        CleanupSelectClones();

        if (closeSceneAnimator == null) yield break;

        if (debugLog) Debug.Log("🎬 [MindMazeStage] 播放离场边框 Close...");
        closeSceneAnimator.gameObject.SetActive(true);
        closeSceneAnimator.speed = 1f;
        closeSceneAnimator.Play(closeBorderState, 0, 0f);
        closeSceneAnimator.Update(0f);

        yield return WaitForAnimatorStateFinished(closeSceneAnimator, closeBorderState, 3.0f);
    }

    // ============================================================
    // Animator 精准等待工具
    // ============================================================

    private static IEnumerator WaitForAnimatorStateFinished(Animator anim, string stateName, float maxWait)
    {
        if (anim == null || string.IsNullOrEmpty(stateName)) yield break;

        bool entered = false;
        float elapsed = 0f;

        while (elapsed < maxWait)
        {
            if (anim == null || !anim.gameObject.activeInHierarchy) yield break;

            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            bool inTransition = anim.IsInTransition(0);
            bool isTarget = info.IsName(stateName);

            if (isTarget)
            {
                entered = true;
                if (!inTransition && info.normalizedTime >= 1f)
                {
                    yield break;
                }
            }
            else if (entered && !inTransition)
            {
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private static IEnumerator WaitForAnimatorEnterState(Animator anim, string targetStateName, float maxWait, string fallbackPrevState)
    {
        if (anim == null || string.IsNullOrEmpty(targetStateName)) yield break;

        float elapsed = 0f;

        while (elapsed < maxWait)
        {
            if (anim == null || !anim.gameObject.activeInHierarchy) yield break;

            AnimatorStateInfo info = anim.GetCurrentAnimatorStateInfo(0);
            bool inTransition = anim.IsInTransition(0);

            if (!inTransition && info.IsName(targetStateName))
            {
                yield break;
            }

            // 兜底：若前一个 T 状态没配过渡箭头却已播完，自动接管切到 I 状态
            if (!string.IsNullOrEmpty(fallbackPrevState) &&
                info.IsName(fallbackPrevState) &&
                !inTransition &&
                info.normalizedTime >= 1f)
            {
                anim.Play(targetStateName, 0, 0f);
                anim.Update(0f);
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (anim != null)
        {
            anim.Play(targetStateName, 0, 0f);
            anim.Update(0f);
        }
    }

    // ============================================================
    // RectTransform 点位对齐与辅助工具
    // ============================================================

    private RollAnimConfig GetRollConfig(int rollId)
    {
        if (rollConfigs == null) return null;

        for (int i = 0; i < rollConfigs.Count; i++)
        {
            if (rollConfigs[i] != null && rollConfigs[i].rollId == rollId)
            {
                return rollConfigs[i];
            }
        }

        return rollConfigs.Count > 0 ? rollConfigs[0] : null;
    }

    private void CacheDefaultSelectRect()
    {
        if (hasCachedDefaultSelectRect || selectTemplate == null) return;
        RectTransform rt = selectTemplate.GetComponent<RectTransform>();
        if (rt == null) return;

        defaultSelectAnchorMin = rt.anchorMin;
        defaultSelectAnchorMax = rt.anchorMax;
        defaultSelectAnchoredPos = rt.anchoredPosition;
        defaultSelectSizeDelta = rt.sizeDelta;
        defaultSelectPivot = rt.pivot;
        defaultSelectRotation = rt.localRotation;
        defaultSelectScale = rt.localScale;
        hasCachedDefaultSelectRect = true;
    }

    private void RestoreDefaultSelectRect(RectTransform rt)
    {
        if (!hasCachedDefaultSelectRect || rt == null) return;
        rt.anchorMin = defaultSelectAnchorMin;
        rt.anchorMax = defaultSelectAnchorMax;
        rt.anchoredPosition = defaultSelectAnchoredPos;
        rt.sizeDelta = defaultSelectSizeDelta;
        rt.pivot = defaultSelectPivot;
        rt.localRotation = defaultSelectRotation;
        rt.localScale = defaultSelectScale;
    }

    private static bool IsFullStretchRect(RectTransform rt)
    {
        if (rt == null) return false;
        return rt.anchorMin == Vector2.zero &&
               rt.anchorMax == Vector2.one &&
               rt.offsetMin == Vector2.zero &&
               rt.offsetMax == Vector2.zero;
    }

    private static void ApplySpotTransform(RectTransform target, RectTransform spot)
    {
        if (target == null || spot == null) return;

        RectTransform spotParentRect = spot.parent as RectTransform;
        bool sameBasis = spot.parent == target.parent ||
                         (spot.parent != null && spot.parent.parent == target.parent && IsFullStretchRect(spotParentRect));

        if (sameBasis)
        {
            target.anchorMin = spot.anchorMin;
            target.anchorMax = spot.anchorMax;
            target.pivot = spot.pivot;
            target.anchoredPosition = spot.anchoredPosition;
            target.sizeDelta = spot.sizeDelta;
            target.localRotation = spot.localRotation;
            target.localScale = spot.localScale;
            return;
        }

        RectTransform targetParent = target.parent as RectTransform;
        if (targetParent == null) return;

        Vector3[] worldCorners = new Vector3[4];
        spot.GetWorldCorners(worldCorners);

        Vector3 localMin = targetParent.InverseTransformPoint(worldCorners[0]);
        Vector3 localMax = targetParent.InverseTransformPoint(worldCorners[2]);
        Vector3 localCenter = 0.5f * (localMin + localMax);

        target.anchorMin = new Vector2(0.5f, 0.5f);
        target.anchorMax = new Vector2(0.5f, 0.5f);
        target.pivot = new Vector2(0.5f, 0.5f);
        target.localPosition = new Vector3(localCenter.x, localCenter.y, 0f);
        target.sizeDelta = new Vector2(Mathf.Abs(localMax.x - localMin.x), Mathf.Abs(localMax.y - localMin.y));
        target.localRotation = spot.localRotation;
        target.localScale = Vector3.one;
    }

    private void CleanupSelectClones()
    {
        for (int i = 0; i < activeSelectClones.Count; i++)
        {
            if (activeSelectClones[i] != null)
            {
                Destroy(activeSelectClones[i]);
            }
        }
        activeSelectClones.Clear();
    }

    /// <summary>
    /// 在 Scene 视图拖好 SelectSpot_1~5 的位置与大小后，点击 Inspector 右键菜单执行此项，
    /// 会自动将像素偏移烘焙进百分比 Anchor（offset 清零），保证任意分辨率与 Retina 屏都不会飘位。
    /// </summary>
    [ContextMenu("将所有 SelectSpot 转为百分比锚点 (防不同分辨率飘位)")]
    public void ConvertSelectSpotsToNormalizedAnchors()
    {
        if (rollConfigs == null) return;

        int convertedCount = 0;
        for (int i = 0; i < rollConfigs.Count; i++)
        {
            RollAnimConfig cfg = rollConfigs[i];
            if (cfg == null || cfg.selectSpots == null) continue;

            for (int j = 0; j < cfg.selectSpots.Count; j++)
            {
                RectTransform spot = cfg.selectSpots[j];
                if (spot == null) continue;

                RectTransform parentRect = spot.parent as RectTransform;
                if (parentRect == null) continue;

                Vector2 parentSize = parentRect.rect.size;
                if (parentSize.x <= 0.001f || parentSize.y <= 0.001f) continue;

#if UNITY_EDITOR
                UnityEditor.Undo.RecordObject(spot, "Normalize SelectSpot Anchors");
#endif
                Vector2 minPos = new Vector2(spot.anchorMin.x * parentSize.x, spot.anchorMin.y * parentSize.y) + spot.offsetMin;
                Vector2 maxPos = new Vector2(spot.anchorMax.x * parentSize.x, spot.anchorMax.y * parentSize.y) + spot.offsetMax;

                spot.anchorMin = new Vector2(minPos.x / parentSize.x, minPos.y / parentSize.y);
                spot.anchorMax = new Vector2(maxPos.x / parentSize.x, maxPos.y / parentSize.y);
                spot.offsetMin = Vector2.zero;
                spot.offsetMax = Vector2.zero;
                convertedCount++;
            }
        }

        Debug.Log($"📐 [MindMazeStage] 已将 {convertedCount} 个 SelectSpot 烘焙为纯百分比锚点！");
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (rollConfigs == null) return;

        Gizmos.color = new Color(0.2f, 0.95f, 0.95f, 0.9f);
        Vector3[] corners = new Vector3[4];

        for (int i = 0; i < rollConfigs.Count; i++)
        {
            RollAnimConfig cfg = rollConfigs[i];
            if (cfg == null || cfg.selectSpots == null) continue;

            for (int j = 0; j < cfg.selectSpots.Count; j++)
            {
                RectTransform spot = cfg.selectSpots[j];
                if (spot == null) continue;

                spot.GetWorldCorners(corners);
                Gizmos.DrawLine(corners[0], corners[1]);
                Gizmos.DrawLine(corners[1], corners[2]);
                Gizmos.DrawLine(corners[2], corners[3]);
                Gizmos.DrawLine(corners[3], corners[0]);
            }
        }
    }
#endif
}
