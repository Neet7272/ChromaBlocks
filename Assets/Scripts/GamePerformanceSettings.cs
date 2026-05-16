using UnityEngine;

/// <summary>Mobil FPS: ana menü veya GameScene açılışında geçerli. Gerçek ayarlar <see cref="MobilePerformanceBooster"/>.</summary>
public static class GamePerformanceSettings
{
    public static void Apply()
    {
        MobilePerformanceBooster.ApplyOnce();
    }
}
