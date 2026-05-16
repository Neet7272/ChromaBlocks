using UnityEngine;

/// <summary>
/// Titreşim: PlayerPrefs ile açık/kapalı; Android'de ms + genlik (one-shot);
/// iOS'ta ağır darbe için sistem titreşimi, hafif için tercihen sessiz (uzun buzz'dan kaçınmak için).
/// </summary>
public sealed class HapticManager : MonoBehaviour
{
    public static HapticManager Instance { get; private set; }

    public const string PrefVibrationEnabled = "VibrationEnabled";
    const string LegacyPrefVibrationOn = "VIBRATION_ON";

    const int LightDurationMs = 15;
    const int LightAmplitude = 64;

    const int HeavyDurationMs = 40;
    const int HeavyAmplitude = 220;

    /// <summary>PlayerPrefs ile senkron; <see cref="SetHapticEnabled"/> veya <see cref="LoadEnabledFromPrefs"/> günceller.</summary>
    public static bool IsHapticEnabled { get; private set; } = true;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        LoadEnabledFromPrefs();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Eski <c>VIBRATION_ON</c> varsa bir kez <c>VibrationEnabled</c>'e taşır.</summary>
    public static bool ReadVibrationEnabledPreference()
    {
        if (PlayerPrefs.HasKey(PrefVibrationEnabled))
            return PlayerPrefs.GetInt(PrefVibrationEnabled, 1) == 1;

        if (PlayerPrefs.HasKey(LegacyPrefVibrationOn))
        {
            bool on = PlayerPrefs.GetInt(LegacyPrefVibrationOn, 1) == 1;
            PlayerPrefs.SetInt(PrefVibrationEnabled, on ? 1 : 0);
            PlayerPrefs.Save();
            return on;
        }

        return true;
    }

    public static void LoadEnabledFromPrefs()
    {
        IsHapticEnabled = ReadVibrationEnabledPreference();
    }

    public static void SetHapticEnabled(bool enabled)
    {
        IsHapticEnabled = enabled;
        PlayerPrefs.SetInt(PrefVibrationEnabled, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>Blok alma / ızgaraya bırakma gibi hafif geri bildirim.</summary>
    public void PlayLightHaptic()
    {
        if (!IsHapticEnabled)
            return;

        VibrateOneShot(LightDurationMs, LightAmplitude, isHeavy: false);
    }

    /// <summary>Patlama / cascade temizliği gibi güçlü geri bildirim.</summary>
    public void PlayHeavyHaptic()
    {
        if (!IsHapticEnabled)
            return;

        VibrateOneShot(HeavyDurationMs, HeavyAmplitude, isHeavy: true);
    }

    static void VibrateOneShot(int durationMs, int amplitude, bool isHeavy)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            if (vibrator == null)
                return;

            if (!vibrator.Call<bool>("hasVibrator"))
                return;

            using var version = new AndroidJavaClass("android.os.Build$VERSION");
            var sdkInt = version.GetStatic<int>("SDK_INT");

            if (sdkInt >= 26)
            {
                using var vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
                using var effect = vibrationEffect.CallStatic<AndroidJavaObject>(
                    "createOneShot",
                    (long)durationMs,
                    Mathf.Clamp(amplitude, 1, 255));
                vibrator.Call("vibrate", effect);
            }
            else
            {
                vibrator.Call("vibrate", (long)durationMs);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HapticManager] Android titreşim başarısız: {e.Message}");
        }
#elif UNITY_IOS && !UNITY_EDITOR
        // Handheld.Vibrate() süresi OS'ta belirlenir (genelde uzun); hafif darbede kaçınıyoruz.
        if (isHeavy)
            Handheld.Vibrate();
#else
        // Editor ve diğer platformlar: sessiz
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Instance != null)
            return;

        var go = new GameObject(nameof(HapticManager));
        go.AddComponent<HapticManager>();
    }
}
