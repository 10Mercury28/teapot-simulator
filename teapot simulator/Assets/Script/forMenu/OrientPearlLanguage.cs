using System;

/// <summary>
/// Minimal immediate UI language switching for prototype pages.
/// For a production-size project, migrate strings to Unity Localization String Tables.
/// </summary>
public static class OrientPearlLanguage
{
    public static int Current { get; private set; } = 0; // 0 = zh-Hans; 1 = en
    public static event Action LanguageChanged;

    public static void Select(int index)
    {
        int newValue = index == 1 ? 1 : 0;
        if (Current == newValue) return;
        Current = newValue;
        LanguageChanged?.Invoke();
    }
}
