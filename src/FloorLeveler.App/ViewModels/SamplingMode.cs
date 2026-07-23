namespace FloorLeveler.App.ViewModels;

/// <summary>サンプリング方式 (仕様 F-1)。</summary>
public enum SamplingMode
{
    /// <summary>手動: 「記録」ボタン / Space で 1 点ずつ記録する。</summary>
    Manual,

    /// <summary>静置方式: デバイスを床に置いて静止させると自動で 1 点記録する。</summary>
    Stillness,

    /// <summary>連続方式: 床上でデバイスを引きずると一定間隔で自動記録する。</summary>
    Continuous,
}
