using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Kalıcı tekil ses yöneticisi. Tüm kısa SFX, DDOL <see cref="sfxSource"/> üzerinden PlayOneShot ile çalar;
/// blok/shape Destroy edilse bile ses kesilmez.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    const string PrefBgm = "BGM_ON";
    const string PrefSfx = "SFX_ON";

    const string BgmChildName = "BGM_Source";
    const string SfxChildName = "SFX_Source";

    [Header("Kaynaklar (boş bırakılabilir — runtime'da child oluşturulur)")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;

    [Header("Klipler")]
    public AudioClip bgmClip;
    public AudioClip pickUpClip;
    public AudioClip placeClip;
    public AudioClip clearClip;
    public AudioClip uiClickClip;

    [Header("SFX")]
    [SerializeField, Range(0f, 1f)] float sfxVolume = 1f;

    public bool BgmEnabled { get; private set; } = true;
    public bool SfxEnabled { get; private set; } = true;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            GamePerformanceSettings.Apply();
            EnsurePersistentSources();
            LoadAndApplyPreferences();
            StartBGM();
            return;
        }

        if (Instance != this)
            Destroy(gameObject);
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (Instance != this)
            return;

        EnsurePersistentSources();
        ApplyMuteState();
        if (BgmEnabled)
            StartBGM();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus || Instance != this)
            return;

        EnsurePersistentSources();
        ApplyMuteState();
    }

    void LoadAndApplyPreferences()
    {
        BgmEnabled = PlayerPrefs.GetInt(PrefBgm, 1) == 1;
        SfxEnabled = PlayerPrefs.GetInt(PrefSfx, 1) == 1;
        ApplyMuteState();
    }

    void ApplyMuteState()
    {
        if (bgmSource != null)
            bgmSource.mute = !BgmEnabled;

        if (sfxSource != null)
            sfxSource.mute = !SfxEnabled;
    }

    static void SavePreference(string key, bool enabled)
    {
        PlayerPrefs.SetInt(key, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetBgmEnabled(bool enabled, bool save = true)
    {
        BgmEnabled = enabled;
        if (save)
            SavePreference(PrefBgm, enabled);

        ApplyMuteState();

        if (enabled)
            StartBGM();
    }

    public void SetSfxEnabled(bool enabled, bool save = true)
    {
        SfxEnabled = enabled;
        if (save)
            SavePreference(PrefSfx, enabled);

        ApplyMuteState();
    }

    static bool IsValidChildSource(AudioSource source, Transform root)
    {
        return source != null && source.transform.IsChildOf(root);
    }

    void EnsurePersistentSources()
    {
        bgmSource = EnsureChildSource(
            bgmSource,
            BgmChildName,
            loop: true,
            defaultClip: bgmClip,
            onClipResolved: clip => { if (bgmClip == null) bgmClip = clip; });

        sfxSource = EnsureChildSource(
            sfxSource,
            SfxChildName,
            loop: false,
            defaultClip: null,
            onClipResolved: null);

        if (bgmSource != null)
        {
            bgmSource.playOnAwake = false;
            bgmSource.volume = 1f;
            bgmSource.spatialBlend = 0f;
        }

        if (sfxSource != null)
        {
            ApplySfxSourceDefaults(sfxSource);
        }
    }

    static void ApplySfxSourceDefaults(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
        source.bypassListenerEffects = true;
        source.volume = 1f;
    }

    AudioSource EnsureChildSource(
        AudioSource inspectorSource,
        string childName,
        bool loop,
        AudioClip defaultClip,
        System.Action<AudioClip> onClipResolved)
    {
        if (IsValidChildSource(inspectorSource, transform))
        {
            inspectorSource.loop = loop;
            inspectorSource.playOnAwake = false;
            if (!loop)
                ApplySfxSourceDefaults(inspectorSource);
            return inspectorSource;
        }

        var savedTime = 0f;
        var wasPlaying = false;
        var savedMute = false;
        var savedVolume = 1f;
        AudioClip savedClip = defaultClip;

        if (inspectorSource != null)
        {
            savedClip ??= inspectorSource.clip;
            wasPlaying = inspectorSource.isPlaying;
            savedTime = inspectorSource.time;
            savedMute = inspectorSource.mute;
            savedVolume = inspectorSource.volume;

            inspectorSource.Stop();
            inspectorSource.playOnAwake = false;
        }

        if (savedClip != null)
            onClipResolved?.Invoke(savedClip);

        var childTransform = transform.Find(childName);
        AudioSource child;
        if (childTransform != null)
        {
            child = childTransform.GetComponent<AudioSource>();
            if (child == null)
                child = childTransform.gameObject.AddComponent<AudioSource>();
        }
        else
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            child = go.AddComponent<AudioSource>();
        }

        child.loop = loop;
        child.playOnAwake = false;
        child.mute = savedMute;
        child.volume = savedVolume;
        child.spatialBlend = 0f;

        if (!loop)
            ApplySfxSourceDefaults(child);

        if (loop && bgmClip != null)
            child.clip = bgmClip;
        else if (savedClip != null && child.clip == null)
            child.clip = savedClip;

        if (loop && wasPlaying && child.clip != null && !child.mute)
        {
            child.time = Mathf.Clamp(savedTime, 0f, child.clip.length - 0.01f);
            if (!child.isPlaying)
                child.Play();
        }

        return child;
    }

    void EnsureSfxSourceReady()
    {
        if (IsValidChildSource(sfxSource, transform))
        {
            ApplySfxSourceDefaults(sfxSource);
            return;
        }

        EnsurePersistentSources();
    }

    /// <summary>BGM zaten çalıyorsa dokunmaz.</summary>
    public void StartBGM()
    {
        if (!IsValidChildSource(bgmSource, transform))
            EnsurePersistentSources();

        if (bgmSource == null || bgmClip == null || !BgmEnabled)
            return;

        if (bgmSource.isPlaying)
            return;

        if (bgmSource.clip == null)
            bgmSource.clip = bgmClip;

        bgmSource.mute = false;
        bgmSource.Play();
    }

    /// <summary>Merkezi kısa efekt — yalnızca DDOL SFX_Source PlayOneShot.</summary>
    public void PlaySFX(AudioClip clip, float volumeScale = 1f)
    {
        if (clip == null || !SfxEnabled)
            return;

        EnsureSfxSourceReady();

        if (!IsValidChildSource(sfxSource, transform))
            return;

        if (sfxSource.mute)
            return;

        var vol = Mathf.Clamp01(sfxVolume) * Mathf.Clamp01(volumeScale);
        sfxSource.PlayOneShot(clip, vol);
    }

    public void PlayPickUpSfx() => PlaySFX(pickUpClip != null ? pickUpClip : uiClickClip);

    public void PlayPlaceSfx() => PlaySFX(placeClip != null ? placeClip : uiClickClip);

    public void PlayClearSfx() => PlaySFX(clearClip != null ? clearClip : uiClickClip);

    public void PlayUiClickSfx() => PlaySFX(uiClickClip);

    /// <summary>Ayar toggle vb.: SFX kapalı olsa bile son tıklama sesi (PlayOneShot).</summary>
    public void PlayUiClickSfxForced()
    {
        if (uiClickClip == null)
            return;

        EnsureSfxSourceReady();

        if (!IsValidChildSource(sfxSource, transform))
            return;

        var wasMuted = sfxSource.mute;
        sfxSource.mute = false;
        var vol = Mathf.Clamp01(sfxVolume);
        sfxSource.PlayOneShot(uiClickClip, vol);
        sfxSource.mute = wasMuted;
    }

    public static void PlayPlaceSfxSafe() => Instance?.PlayPlaceSfx();

    public static void PlayClearSfxSafe() => Instance?.PlayClearSfx();

    public static void PlayPickUpSfxSafe() => Instance?.PlayPickUpSfx();
}
