using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Matte toggle: handle kayar; arka plan ve handle rengi animasyonlu değişir.
/// </summary>
public sealed class AnimatedToggle : MonoBehaviour, IPointerClickHandler
{
    [Header("Referanslar")]
    public RectTransform handle;
    public Image backgroundImage;
    public Image handleImage;

    [Header("Arka Plan Renkleri")]
    public Color colorOn;
    public Color colorOff;

    [Header("Handle Renkleri")]
    public Color handleColorOn;
    public Color handleColorOff;

    [Header("Handle X (anchoredPosition)")]
    public float handlePosX_On;
    public float handlePosX_Off;

    [Header("Animasyon")]
    [SerializeField, Min(0.01f)] float tweenDuration = 0.25f;

    [Header("Durum")]
    [SerializeField] bool isOn;

    [Header("Event")]
    public UnityEvent<bool> onValueChanged;

    public bool IsOn => isOn;

    /// <summary>PlayerPrefs / SettingsManager ile anında görsel senkron (animasyon yok).</summary>
    public void InitializeState(bool startingState)
    {
        isOn = startingState;
        KillTweens();

        if (handle != null)
        {
            var p = handle.anchoredPosition;
            p.x = startingState ? handlePosX_On : handlePosX_Off;
            handle.anchoredPosition = p;
        }

        if (backgroundImage != null)
            backgroundImage.color = startingState ? colorOn : colorOff;

        if (handleImage != null)
            handleImage.color = startingState ? handleColorOn : handleColorOff;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        SetIsOn(!isOn, animated: true);
    }

    public void SetIsOnWithoutNotify(bool value)
    {
        InitializeState(value);
    }

    public void SetIsOn(bool value, bool animated)
    {
        if (isOn == value && animated)
            return;

        isOn = value;
        ApplyVisual(value, animated);
        onValueChanged?.Invoke(isOn);
    }

    void ApplyInstant(bool on)
    {
        KillTweens();

        if (handle != null)
        {
            var p = handle.anchoredPosition;
            p.x = on ? handlePosX_On : handlePosX_Off;
            handle.anchoredPosition = p;
        }

        if (backgroundImage != null)
            backgroundImage.color = on ? colorOn : colorOff;

        if (handleImage != null)
            handleImage.color = on ? handleColorOn : handleColorOff;
    }

    void ApplyVisual(bool on, bool animated)
    {
        float targetHandleX = on ? handlePosX_On : handlePosX_Off;
        var targetBgColor = on ? colorOn : colorOff;
        var targetHandleColor = on ? handleColorOn : handleColorOff;

        if (!animated)
        {
            ApplyInstant(on);
            return;
        }

        KillTweens();

        if (handle != null)
            handle.DOAnchorPosX(targetHandleX, tweenDuration).SetEase(Ease.OutBack).SetUpdate(true);

        if (backgroundImage != null)
            backgroundImage.DOColor(targetBgColor, tweenDuration).SetUpdate(true);

        if (handleImage != null)
            handleImage.DOColor(targetHandleColor, tweenDuration).SetUpdate(true);
    }

    void KillTweens()
    {
        if (handle != null)
            handle.DOKill();
        if (backgroundImage != null)
            backgroundImage.DOKill();
        if (handleImage != null)
            handleImage.DOKill();
    }

    void OnDisable()
    {
        KillTweens();
    }
}
