using UnityEngine;
using DG.Tweening;

/// <summary>
/// Kamera screenshake; bitişte orijinal local pozisyona kesin sıfırlama (drift yok).
/// </summary>
public sealed class CameraManager : MonoBehaviour
{
    [SerializeField] Camera targetCamera;

    [Header("Hafif (yerleştirme)")]
    [SerializeField, Min(0.01f)] float lightDuration = 0.1f;
    [SerializeField, Min(0f)] float lightStrength = 0.1f;
    [SerializeField, Min(1)] int lightVibrato = 8;

    [Header("Şiddetli (2x2 / lazer)")]
    [SerializeField, Min(0.01f)] float strongDuration = 0.3f;
    [SerializeField, Min(0f)] float strongStrength = 0.4f;
    [SerializeField, Min(1)] int strongVibrato = 10;

    Transform _camTransform;
    Vector3 _anchorLocalPosition;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        _camTransform = targetCamera != null ? targetCamera.transform : null;
        if (_camTransform != null)
            _anchorLocalPosition = _camTransform.localPosition;
    }

    void OnDestroy()
    {
        if (_camTransform != null)
            _camTransform.DOKill();
    }

    /// <summary>Oyun sırasında kamera rig'i taşınırsa çağır (isteğe bağlı).</summary>
    public void RebaseAnchor()
    {
        if (_camTransform != null)
            _anchorLocalPosition = _camTransform.localPosition;
    }

    public void ShakeLight()
    {
        PlayShake(lightDuration, lightStrength, lightVibrato);
    }

    public void ShakeStrong()
    {
        PlayShake(strongDuration, strongStrength, strongVibrato);
    }

    void PlayShake(float duration, float strength, int vibrato, bool haptic = false)
    {
        if (!SettingsManager.AllowsScreenShakeAndHaptics)
            return;

        if (_camTransform == null)
            return;

        _camTransform.DOKill();
        _camTransform.localPosition = _anchorLocalPosition;

        _camTransform
            .DOShakePosition(duration, strength, vibrato, 90f, false, true)
            .SetUpdate(true)
            .OnKill(SnapToAnchor)
            .OnComplete(SnapToAnchor);

        if (haptic)
            SettingsManager.TryHapticPulse();
    }

    void SnapToAnchor()
    {
        if (_camTransform != null)
            _camTransform.localPosition = _anchorLocalPosition;
    }
}
