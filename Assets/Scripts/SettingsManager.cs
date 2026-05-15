using UnityEngine;

/// <summary>
/// BGM / SFX / titreşim ayarları. Ses tercihleri AudioManager + PlayerPrefs üzerinden kalıcı.
/// </summary>
public sealed class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

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

    bool _isInitializing;

    public static bool AllowsScreenShakeAndHaptics =>
        Instance == null || Instance.VibrationEnabled;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        _isInitializing = true;

        if (AudioManager.Instance != null)
        {
            BgmEnabled = AudioManager.Instance.BgmEnabled;
            SfxEnabled = AudioManager.Instance.SfxEnabled;
        }
        else
        {
            BgmEnabled = PlayerPrefs.GetInt("BGM_ON", 1) == 1;
            SfxEnabled = PlayerPrefs.GetInt("SFX_ON", 1) == 1;
        }

        VibrationEnabled = PlayerPrefs.GetInt(PrefVibration, 1) == 1;
        HapticManager.LoadEnabledFromPrefs();

        SyncToggleVisuals();

        _isInitializing = false;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void SaveVibration(bool value)
    {
        PlayerPrefs.SetInt(PrefVibration, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    void SyncToggleVisuals()
    {
        toggleBGM?.InitializeState(BgmEnabled);
        toggleSFX?.InitializeState(SfxEnabled);
        toggleVibration?.InitializeState(VibrationEnabled);
    }

    public void ToggleBGM(bool isOn)
    {
        if (_isInitializing)
            return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayUiClickSfx();

        BgmEnabled = isOn;
        AudioManager.Instance?.SetBgmEnabled(isOn);
    }

    public void ToggleSFX(bool isOn)
    {
        if (_isInitializing)
            return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayUiClickSfx();

        SfxEnabled = isOn;
        AudioManager.Instance?.SetSfxEnabled(isOn);
    }

    public void ToggleVibration(bool isOn)
    {
        if (_isInitializing)
            return;

        if (AudioManager.Instance != null)
            AudioManager.Instance.PlayUiClickSfx();

        VibrationEnabled = isOn;
        SaveVibration(isOn);
        HapticManager.SetHapticEnabled(isOn);
    }

    public static void TryHapticPulse()
    {
        if (!AllowsScreenShakeAndHaptics)
            return;

        HapticManager.Instance?.LightVibration();
    }
}
