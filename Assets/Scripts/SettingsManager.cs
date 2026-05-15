using UnityEngine;

/// <summary>
/// BGM / SFX / titreşim ayarları. PlayerPrefs ile kalıcı.
/// Ses çıkışı AudioManager üzerinden mute ile kontrol edilir.
/// </summary>
public sealed class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    const string PrefBgm = "BGM_ON";
    const string PrefSfx = "SFX_ON";
    const string PrefVibration = "VIBRATION_ON";

    [Header("Toggle UI (sahnedeki AnimatedToggle referansları)")]
    public AnimatedToggle toggleBGM;
    public AnimatedToggle toggleSFX;
    public AnimatedToggle toggleVibration;

    public bool BgmEnabled { get; private set; } = true;
    public bool SfxEnabled { get; private set; } = true;
    public bool VibrationEnabled { get; private set; } = true;

    public bool IsBgmOn => BgmEnabled;
    public bool IsSfxOn => SfxEnabled;
    public bool IsVibrationOn => VibrationEnabled;

    public static bool AllowsScreenShakeAndHaptics =>
        Instance == null || Instance.VibrationEnabled;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        BgmEnabled = PlayerPrefs.GetInt(PrefBgm, 1) == 1;
        SfxEnabled = PlayerPrefs.GetInt(PrefSfx, 1) == 1;
        VibrationEnabled = PlayerPrefs.GetInt(PrefVibration, 1) == 1;
        HapticManager.LoadEnabledFromPrefs();

        ApplyAudioFromPrefs();
        SyncToggleVisuals();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void SaveBool(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ApplyAudioFromPrefs()
    {
        if (AudioManager.Instance != null)
        {
            if (AudioManager.Instance.bgmSource != null)
                AudioManager.Instance.bgmSource.mute = !BgmEnabled;

            if (AudioManager.Instance.sfxSource != null)
                AudioManager.Instance.sfxSource.mute = !SfxEnabled;
        }

        if (BgmEnabled)
            AudioManager.Instance?.StartBGM();
    }

    void SyncToggleVisuals()
    {
        toggleBGM?.InitializeState(BgmEnabled);
        toggleSFX?.InitializeState(SfxEnabled);
        toggleVibration?.InitializeState(VibrationEnabled);
    }

    public void ToggleBGM(bool isOn)
    {
        BgmEnabled = isOn;
        SaveBool(PrefBgm, isOn);

        if (AudioManager.Instance != null && AudioManager.Instance.bgmSource != null)
            AudioManager.Instance.bgmSource.mute = !isOn;

        if (isOn)
            AudioManager.Instance?.StartBGM();
    }

    public void ToggleSFX(bool isOn)
    {
        SfxEnabled = isOn;
        SaveBool(PrefSfx, isOn);

        if (AudioManager.Instance != null && AudioManager.Instance.sfxSource != null)
            AudioManager.Instance.sfxSource.mute = !isOn;
    }

    public void ToggleVibration(bool isOn)
    {
        VibrationEnabled = isOn;
        HapticManager.SetHapticEnabled(isOn);
    }

    public static void TryHapticPulse()
    {
        if (!AllowsScreenShakeAndHaptics)
            return;

        HapticManager.Instance?.LightVibration();
    }
}
