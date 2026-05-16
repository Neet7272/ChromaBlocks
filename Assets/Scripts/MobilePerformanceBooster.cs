using UnityEngine;

/// <summary>
/// İlk açılışta bir kez motor ayarları: çözünürlük üst sınırı, FPS/vSync, mobilde fizik simülasyonu kapatma.
/// <see cref="RuntimeInitializeOnLoadMethod"/> ile Main Menu’den önce de çalışır.
/// </summary>
[DefaultExecutionOrder(-5000)]
public sealed class MobilePerformanceBooster : MonoBehaviour
{
    static MobilePerformanceBooster _instance;
    static bool _applied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (_applied)
            return;
        if (_instance != null)
            return;

        var go = new GameObject(nameof(MobilePerformanceBooster));
        DontDestroyOnLoad(go);
        go.AddComponent<MobilePerformanceBooster>();
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        ApplyOnce();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    /// <summary>Harici (GamePerformanceSettings) çağrıları için idempotent giriş.</summary>
    public static void ApplyOnce()
    {
        if (_applied)
            return;

        _applied = true;

#if UNITY_ANDROID || UNITY_IOS
        if (!Application.isEditor)
            ApplyResolutionCapMobile();
#endif

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = GetPreferredTargetFrameRate();

#if UNITY_ANDROID || UNITY_IOS
        if (!Application.isEditor)
            DisablePhysicsSimulationIfUnused();
#endif
    }

#if UNITY_ANDROID || UNITY_IOS
    static void ApplyResolutionCapMobile()
    {
        int w = Screen.width;
        int h = Screen.height;
        if (w <= 1 || h <= 1)
            return;

        int longEdge = Mathf.Max(w, h);
        const int maxLongEdge = 1920;

        float scale = 1f;
        if (longEdge > maxLongEdge)
            scale = maxLongEdge / (float)longEdge;
        if (h > 2400)
            scale = Mathf.Min(scale, 0.7f);

        if (scale >= 0.999f)
            return;

        int newW = Mathf.Clamp(Mathf.RoundToInt(w * scale), 360, 8192);
        int newH = Mathf.Clamp(Mathf.RoundToInt(h * scale), 360, 8192);

        if (newW == w && newH == h)
            return;

        Screen.SetResolution(newW, newH, true);
    }
#endif

    static int GetPreferredTargetFrameRate()
    {
#if UNITY_ANDROID || UNITY_IOS
        try
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz < 1d)
                hz = 60d;
            int rounded = Mathf.RoundToInt((float)hz);
            if (rounded >= 120)
                return 120;
        }
        catch
        {
            // Bazı cihaz / sürücülerde oran okunamaz
        }
#endif
        return 60;
    }

#if UNITY_ANDROID || UNITY_IOS
    /// <summary>
    /// Projede Rigidbody kullanılmıyorsa simülasyon adımını kapatır; statik collider + raycast etkilenmez.
    /// </summary>
    static void DisablePhysicsSimulationIfUnused()
    {
        Physics.simulationMode = SimulationMode.Script;
        Physics2D.simulationMode = SimulationMode2D.Script;
    }
#endif
}
