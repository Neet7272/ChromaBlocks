using UnityEngine;

/// <summary>
/// Kalıcı tekil ses yöneticisi. AudioSource'lar yalnızca bu objenin child'ı olarak yaşar;
/// sahneye bağlı "Menu Audio Source" gibi referanslar otomatik taşınır.
/// </summary>
[DefaultExecutionOrder(-200)]
public sealed class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    const string BgmChildName = "BGM_Source";
    const string SfxChildName = "SFX_Source";

    [Header("Kaynaklar (boş bırakılabilir — runtime'da child oluşturulur)")]
    public AudioSource bgmSource;
    public AudioSource sfxSource;

    [Header("Klipler")]
    public AudioClip bgmClip;
    public AudioClip placeClip;
    public AudioClip clearClip;
    public AudioClip uiClickClip;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            EnsurePersistentSources();
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

            // Sahne objesi yok olmadan önce durdur; müzik child kaynağa taşınacak.
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

        if (bgmSource == null || bgmClip == null)
            return;

        if (bgmSource.isPlaying)
            return;

        if (bgmSource.clip == null)
            bgmSource.clip = bgmClip;

        if (!bgmSource.mute)
            bgmSource.Play();
    }

    public void PlaySFX(AudioClip clip)
    {
        if (clip == null)
            return;

        if (sfxSource == null)
            EnsurePersistentSources();

        if (sfxSource == null || sfxSource.mute)
            return;

        sfxSource.PlayOneShot(clip);
    }
}
