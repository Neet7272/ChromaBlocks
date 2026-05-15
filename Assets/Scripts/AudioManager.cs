using UnityEngine;

/// <summary>
/// Kalıcı tekil ses yöneticisi. AudioSource'lar yalnızca bu objenin child'ı olarak yaşar;
/// sahneye bağlı "Menu Audio Source" gibi referanslar otomatik taşınır.
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

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
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

        bgmSource.playOnAwake = false;
        sfxSource.playOnAwake = false;
        bgmSource.volume = 1f;
        sfxSource.volume = 1f;
    }

    AudioSource EnsureChildSource(
        AudioSource inspectorSource,
        string childName,
        bool loop,
        AudioClip defaultClip,
        System.Action<AudioClip> onClipResolved)
    {
        if (inspectorSource != null && inspectorSource.transform.IsChildOf(transform))
        {
            inspectorSource.loop = loop;
            inspectorSource.playOnAwake = false;
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

    /// <summary>BGM zaten çalıyorsa dokunmaz.</summary>
    public void StartBGM()
    {
        if (bgmSource == null)
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

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null || !SfxEnabled)
            return;

        if (sfxSource == null)
            EnsurePersistentSources();

        if (sfxSource == null)
            return;

        if (sfxSource.mute)
            return;

        sfxSource.PlayOneShot(clip);
    }

    public void PlayPickUpSfx()
    {
        PlaySFX(pickUpClip != null ? pickUpClip : uiClickClip);
    }

    public void PlayPlaceSfx()
    {
        PlaySFX(placeClip);
    }

    public void PlayClearSfx()
    {
        PlaySFX(clearClip);
    }

    public void PlayUiClickSfx()
    {
        PlaySFX(uiClickClip);
    }
}
