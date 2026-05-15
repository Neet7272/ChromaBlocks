using UnityEngine;

/// <summary>Mobil FPS: ana menü veya doğrudan GameScene açılışında geçerli.</summary>
public static class GamePerformanceSettings
{
    static bool _applied;

    public static void Apply()
    {
        if (_applied)
            return;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        _applied = true;
    }
}
