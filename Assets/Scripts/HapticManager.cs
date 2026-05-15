using UnityEngine;

/// <summary>
/// Android'de kısa, tok titreşimler (Taptic Engine tarzı). Milisaniye bazlı one-shot.
/// </summary>
public sealed class HapticManager : MonoBehaviour
{
    public static HapticManager Instance { get; private set; }

    const string PrefVibration = "VIBRATION_ON";

    const int LightDurationMs = 18;
    const int LightAmplitude = 48;

    const int HeavyDurationMs = 45;
    const int HeavyAmplitude = 200;

    public static bool isHapticEnabled = true;

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

    public static void LoadEnabledFromPrefs()
    {
        isHapticEnabled = PlayerPrefs.GetInt(PrefVibration, 1) == 1;
    }

    public static void SetHapticEnabled(bool enabled)
    {
        isHapticEnabled = enabled;
        PlayerPrefs.SetInt(PrefVibration, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void LightVibration()
    {
        if (!isHapticEnabled)
            return;

        VibrateOneShot(LightDurationMs, LightAmplitude);
    }

    public void HeavyVibration()
    {
        if (!isHapticEnabled)
            return;

        VibrateOneShot(HeavyDurationMs, HeavyAmplitude);
    }

    static void VibrateOneShot(int durationMs, int amplitude)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            if (vibrator == null)
                return;

            if (vibrator.Call<bool>("hasVibrator") == false)
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
        // iOS: kısa darbe için hafif sistem titreşimi (süre OS tarafından belirlenir).
        if (amplitude >= HeavyAmplitude)
            Handheld.Vibrate();
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
