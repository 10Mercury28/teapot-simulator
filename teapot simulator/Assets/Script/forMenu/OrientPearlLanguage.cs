using System;
using UnityEngine;

/// <summary>
/// Minimal immediate UI language switching for prototype pages.
/// For a production-size project, migrate strings to Unity Localization String Tables.
/// </summary>
public static class OrientPearlLanguage
{
    public const string PlayerPrefsKey = "OP.UI.Language";

    public static int Current { get; private set; } = 0; // 0 = zh-Hans; 1 = en
    public static bool IsEnglish => Current == 1;
    public static event Action LanguageChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitFromPlayerPrefs()
    {
        int saved = PlayerPrefs.GetInt(PlayerPrefsKey, 0);
        Current = saved == 1 ? 1 : 0;
    }

    public static void Select(int index)
    {
        int newValue = index == 1 ? 1 : 0;
        if (Current == newValue) return;
        Current = newValue;
        LanguageChanged?.Invoke();
    }
}
