/*using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public class SucComplete : MonoBehaviour
{
    [Header("监听目标")]
    public MonoBehaviour targetScript;
    public string boolFieldName = "isAllComplete";

    [Header("黑名单（将在完成后禁用或销毁）")]
    public List<GameObject> blacklist = new List<GameObject>();
    public bool destroyInsteadOfDisable = true;

    [Header("结尾视频")]
    public VideoPlayer endVideo;
    public RawImage endRaw;

    [Header("场景切换设置")]
    public string nextSceneName;
    public float cleanupDelay = 0.5f;

    [Header("调试")]
    public bool debugLog = true;
    public float checkInterval = 0.5f;

    private bool triggered = false;
    private FieldInfo targetField;
    private string oldSceneName;

    void Start()
    {
        oldSceneName = SceneManager.GetActiveScene().name;

        // 让 Canvas 排序层最低
        Canvas c = GetComponent<Canvas>();
        if (c != null)
        {
            c.sortingOrder = -9999;
            if (debugLog) Debug.Log($"🪞 {name} Canvas 排序层级设为最底层");
        }

        // 预加载视频首帧
        if (endVideo != null && endVideo.clip != null)
            StartCoroutine(PrepareAndPause());

        // 获取监控字段
        if (targetScript != null && !string.IsNullOrEmpty(boolFieldName))
        {
            targetField = targetScript.GetType().GetField(
                boolFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        StartCoroutine(CheckCompletion());
    }

    IEnumerator PrepareAndPause()
    {
        if (!endVideo.isPrepared)
        {
            endVideo.Prepare();
            while (!endVideo.isPrepared)
                yield return null;
        }

        if (endVideo.targetTexture && endRaw != null)
            endRaw.texture = endVideo.targetTexture;

        endRaw.enabled = true;
        endVideo.time = 0;
        endVideo.Pause();

        if (debugLog) Debug.Log("🎞️ 结尾视频已准备并停在第一帧");
    }

    IEnumerator CheckCompletion()
    {
        while (!triggered)
        {
            yield return new WaitForSeconds(checkInterval);
            if (targetField == null || targetScript == null) continue;

            object value = targetField.GetValue(targetScript);
            if (value is bool boolVal && boolVal)
            {
                triggered = true;
                if (debugLog)
                    Debug.Log($"✅ 检测到 {targetScript.name}.{boolFieldName} = true → 执行结尾逻辑");
                StartCoroutine(PlayAndTransition());
            }
        }
    }

    IEnumerator PlayAndTransition()
    {
        // Step 1: 禁用/销毁黑名单
        foreach (var obj in blacklist)
        {
            if (obj == null) continue;
            if (destroyInsteadOfDisable)
            {
                Destroy(obj);
                if (debugLog) Debug.Log($"💥 已销毁黑名单对象：{obj.name}");
            }
            else
            {
                obj.SetActive(false);
                if (debugLog) Debug.Log($"🚫 已禁用黑名单对象：{obj.name}");
            }
        }

        yield return null;

        // Step 2: 播放结尾视频
        if (endVideo == null || endVideo.clip == null)
        {
            if (debugLog) Debug.LogWarning("⚠️ 无结尾视频 → 直接切换场景");
            SceneBlinkTransition.Instance?.LoadSceneConcealed(nextSceneName);
            StartCoroutine(CleanupAllAfterSceneChange());
            yield break;
        }

        endVideo.time = 0;
        endVideo.Play();
        if (debugLog) Debug.Log($"▶️ 播放结尾视频：{endVideo.clip.name}");
        yield return new WaitForSecondsRealtime((float)endVideo.clip.length);

        // Step 3: 视频结束 → 眨眼转场
        if (debugLog) Debug.Log("🎬 结尾视频播放完毕 → 眨眼转场");
        SceneBlinkTransition.Instance?.LoadSceneConcealed(nextSceneName);
        StartCoroutine(CleanupAllAfterSceneChange());
    }

    IEnumerator CleanupAllAfterSceneChange()
    {
        // 等待新场景加载完
        yield return new WaitUntil(() => SceneManager.GetActiveScene().name == nextSceneName);
        yield return new WaitForSeconds(cleanupDelay);

        // 1️⃣ 销毁旧场景对象
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.IsValid() || s.name == nextSceneName) continue;

            var roots = s.GetRootGameObjects();
            foreach (var obj in roots)
            {
                if (obj == null) continue;
                Destroy(obj);
                if (debugLog) Debug.Log($"🧹 已销毁旧场景对象：{obj.name}");
            }
        }

        // 2️⃣ 销毁 DontDestroyOnLoad 内容
        var temp = new GameObject("TempSceneScanner");
        DontDestroyOnLoad(temp);
        Scene dontScene = temp.scene;
        Destroy(temp);

        var dontRoots = dontScene.GetRootGameObjects();
        foreach (var obj in dontRoots)
        {
            Destroy(obj);
            if (debugLog) Debug.Log($"💣 已销毁 DontDestroyOnLoad 对象：{obj.name}");
        }

        if (debugLog) Debug.Log($"✅ 已彻底清空旧场景与 DontDestroyOnLoad（当前仅保留新场景 {nextSceneName}）");
    }
}
*/

using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SucComplete : MonoBehaviour
{
    [Header("监听目标")]
    public MonoBehaviour targetScript;
    public string boolFieldName = "isAllComplete";

    [Header("黑名单（将在完成后禁用或销毁）")]
    public List<GameObject> blacklist = new List<GameObject>();
    public bool destroyInsteadOfDisable = true;

    [Header("结尾视频设置")]
    public bool useEndVideo = true; // ✅ 新增：控制是否使用结尾视频
    public VideoPlayer endVideo;
    public RawImage endRaw;

    [Header("场景切换设置")]
    public string nextSceneName;
    public float cleanupDelay = 0.5f;

    [Header("调试")]
    public bool debugLog = true;
    public float checkInterval = 0.5f;

    private bool triggered = false;
    private FieldInfo targetField;
    private string oldSceneName;

    void Start()
    {
        oldSceneName = SceneManager.GetActiveScene().name;

        // 让 Canvas 排序层最低
        Canvas c = GetComponent<Canvas>();
        if (c != null)
        {
            c.sortingOrder = -9999;
            if (debugLog) Debug.Log($"🪞 {name} Canvas 排序层级设为最底层");
        }

        // ✅ 仅当使用结尾视频时预加载
        if (useEndVideo && endVideo != null && endVideo.clip != null)
            StartCoroutine(PrepareAndPause());

        // 获取监控字段
        if (targetScript != null && !string.IsNullOrEmpty(boolFieldName))
        {
            targetField = targetScript.GetType().GetField(
                boolFieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        StartCoroutine(CheckCompletion());
    }

    IEnumerator PrepareAndPause()
    {
        if (!endVideo.isPrepared)
        {
            endVideo.Prepare();
            while (!endVideo.isPrepared)
                yield return null;
        }

        if (endVideo.targetTexture && endRaw != null)
            endRaw.texture = endVideo.targetTexture;

        endRaw.enabled = true;
        endVideo.time = 0;
        endVideo.Pause();

        if (debugLog) Debug.Log("🎞️ 结尾视频已准备并停在第一帧");
    }

    IEnumerator CheckCompletion()
    {
        while (!triggered)
        {
            yield return new WaitForSeconds(checkInterval);
            if (targetField == null || targetScript == null) continue;

            object value = targetField.GetValue(targetScript);
            if (value is bool boolVal && boolVal)
            {
                triggered = true;
                if (debugLog)
                    Debug.Log($"✅ 检测到 {targetScript.name}.{boolFieldName} = true → 执行结尾逻辑");
                StartCoroutine(PlayAndTransition());
            }
        }
    }

    IEnumerator PlayAndTransition()
    {
        // Step 1️⃣: 禁用/销毁黑名单
        foreach (var obj in blacklist)
        {
            if (obj == null) continue;
            if (destroyInsteadOfDisable)
            {
                Destroy(obj);
                if (debugLog) Debug.Log($"💥 已销毁黑名单对象：{obj.name}");
            }
            else
            {
                obj.SetActive(false);
                if (debugLog) Debug.Log($"🚫 已禁用黑名单对象：{obj.name}");
            }
        }

        yield return null;

        // Step 2️⃣: 播放结尾视频（仅当启用）
        if (useEndVideo && endVideo != null && endVideo.clip != null)
        {
            endVideo.time = 0;
            endVideo.Play();
            if (debugLog) Debug.Log($"▶️ 播放结尾视频：{endVideo.clip.name}");
            yield return new WaitUntil(() => !endVideo.isPlaying); // ✅ 播完立即切换
        }
        else
        {
            if (debugLog) Debug.Log("⚠️ 未启用结尾视频 → 直接跳转");
        }

        // Step 3️⃣: 视频结束 → 立即眨眼转场（无延迟）
        if (debugLog) Debug.Log("🎬 视频播放完毕（或无视频）→ 立刻跳转");
        SceneBlinkTransition.Instance?.LoadSceneConcealed(nextSceneName);
        StartCoroutine(CleanupAllAfterSceneChange());
    }

    IEnumerator CleanupAllAfterSceneChange()
    {
        yield return new WaitUntil(() => SceneManager.GetActiveScene().name == nextSceneName);
        yield return new WaitForSeconds(cleanupDelay);

        // 清理旧场景
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (!s.IsValid() || s.name == nextSceneName) continue;

            var roots = s.GetRootGameObjects();
            foreach (var obj in roots)
            {
                if (obj == null) continue;
                Destroy(obj);
                if (debugLog) Debug.Log($"🧹 已销毁旧场景对象：{obj.name}");
            }
        }

        // 清理 DontDestroyOnLoad
        var temp = new GameObject("TempSceneScanner");
        DontDestroyOnLoad(temp);
        Scene dontScene = temp.scene;
        Destroy(temp);

        var dontRoots = dontScene.GetRootGameObjects();
        foreach (var obj in dontRoots)
        {
            Destroy(obj);
            if (debugLog) Debug.Log($"💣 已销毁 DontDestroyOnLoad 对象：{obj.name}");
        }

        if (debugLog)
            Debug.Log($"✅ 已彻底清空旧场景与 DontDestroyOnLoad（当前仅保留新场景 {nextSceneName}）");
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(SucComplete))]
public class SucCompleteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty("targetScript"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("boolFieldName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blacklist"), true);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("destroyInsteadOfDisable"));

        // ✅ 仅当 useEndVideo 勾选时显示视频槽
        SerializedProperty useEndVideoProp = serializedObject.FindProperty("useEndVideo");
        EditorGUILayout.PropertyField(useEndVideoProp);

        if (useEndVideoProp.boolValue)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("endVideo"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("endRaw"));
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty("nextSceneName"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("cleanupDelay"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("debugLog"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("checkInterval"));

        serializedObject.ApplyModifiedProperties();
    }
}
#endif
