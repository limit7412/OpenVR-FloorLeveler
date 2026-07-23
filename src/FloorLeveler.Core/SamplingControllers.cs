using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>時刻付きポーズを与えると記録すべき接地点を返す自動サンプラー (仕様 F-1)。</summary>
public interface IPoseSampler
{
    /// <summary>1 フレーム分のポーズを与える。記録すべきなら接地点、そうでなければ null。</summary>
    Vector3? Feed(TimeSpan timestamp, RigidTransform devicePose);

    /// <summary>状態を初期化する。</summary>
    void Reset();
}

/// <summary>
/// 静置方式の自動サンプラー (仕様 F-1)。デバイスを床に置いて静止させると、
/// その接地点を 1 点だけ自動記録する。時刻とポーズの列を <see cref="Feed"/> に
/// 与えると、静止を検出したフレームで接地点を返す (純粋な状態機械)。
/// 一度記録したら、デバイスが離脱距離以上動くまで再記録しない (同じ静止での
/// 連続記録を防ぐ)。
/// </summary>
public sealed class StillnessSampler : IPoseSampler
{
    private readonly DeviceContactProfile _profile;
    private readonly TimeSpan _window;
    private readonly float _tolerance;
    private readonly float _releaseDistance;
    private readonly List<TimedSample> _history = [];

    private bool _armed = true;
    private Vector3? _lastEmitted;

    /// <param name="profile">接地オフセットのプロファイル。</param>
    /// <param name="window">静止判定の観測窓 (既定 500 ms)。</param>
    /// <param name="toleranceMeters">静止判定の移動量閾値 (既定 5 mm)。</param>
    /// <param name="releaseDistanceMeters">
    /// 次の記録を許可するために離脱すべき距離 (既定 5 cm)。記録後にこの距離以上
    /// 動くと再び記録可能になる。
    /// </param>
    public StillnessSampler(
        DeviceContactProfile profile,
        TimeSpan? window = null,
        float toleranceMeters = Sampling.DefaultStillnessToleranceMeters,
        float releaseDistanceMeters = 0.05f)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _window = window ?? Sampling.DefaultStillnessWindow;
        _tolerance = toleranceMeters;
        _releaseDistance = releaseDistanceMeters;
    }

    /// <summary>
    /// デバイスの時刻付きポーズを 1 フレーム分与える。静止を検出したフレームでは
    /// 接地点を返し、それ以外では null を返す。
    /// </summary>
    public Vector3? Feed(TimeSpan timestamp, RigidTransform devicePose)
    {
        var contact = _profile.ContactPoint(devicePose);
        _history.Add(new TimedSample(timestamp, contact));

        // 窓より十分古いサンプルは捨てる (窓の外側 1 点は IsStill が使うため残す)。
        var cutoff = timestamp - _window - _window;
        _history.RemoveAll(s => s.Timestamp < cutoff);

        // 記録済みの位置から離脱距離以上動いたら再武装する。
        if (!_armed && _lastEmitted is { } last && (contact - last).Length() >= _releaseDistance)
        {
            _armed = true;
        }

        if (_armed && Sampling.IsStill(_history, _window, _tolerance))
        {
            _armed = false;
            _lastEmitted = contact;
            return contact;
        }

        return null;
    }

    /// <summary>状態を初期化する (サンプリング開始時など)。</summary>
    public void Reset()
    {
        _history.Clear();
        _armed = true;
        _lastEmitted = null;
    }
}

/// <summary>
/// 連続方式の自動サンプラー (仕様 F-1)。デバイスを床に置いたまま引きずって動かすと、
/// 一定間隔ごとに接地点を記録する。持ち上げなど速度が閾値を超えるフレームは棄却する。
/// </summary>
public sealed class ContinuousSampler : IPoseSampler
{
    private readonly DeviceContactProfile _profile;
    private readonly float _maxSpeed;
    private readonly float _minSpacing;

    private TimedSample? _previous;
    private Vector3? _lastRecorded;

    /// <param name="profile">接地オフセットのプロファイル。</param>
    /// <param name="maxSpeedMetersPerSecond">これを超える速度のフレームは棄却 (既定 1.0 m/s)。</param>
    /// <param name="minSpacingMeters">記録点の最小間隔 (既定 5 cm)。点群の過密を防ぐ。</param>
    public ContinuousSampler(
        DeviceContactProfile profile,
        float maxSpeedMetersPerSecond = Sampling.DefaultMaxSpeedMetersPerSecond,
        float minSpacingMeters = 0.05f)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _maxSpeed = maxSpeedMetersPerSecond;
        _minSpacing = minSpacingMeters;
    }

    /// <summary>
    /// デバイスの時刻付きポーズを 1 フレーム分与える。記録すべきフレームでは接地点を
    /// 返し、速度超過・間隔不足のフレームでは null を返す。
    /// </summary>
    public Vector3? Feed(TimeSpan timestamp, RigidTransform devicePose)
    {
        var contact = _profile.ContactPoint(devicePose);
        var current = new TimedSample(timestamp, contact);

        // 速度超過 (持ち上げ等) のフレームは棄却する。基準を更新して次に備える。
        if (_previous is { } prev && Sampling.ExceedsSpeed(prev, current, _maxSpeed))
        {
            _previous = current;
            return null;
        }

        _previous = current;

        // 最小間隔を満たす場合のみ記録する。
        if (_lastRecorded is { } last && (contact - last).Length() < _minSpacing)
        {
            return null;
        }

        _lastRecorded = contact;
        return contact;
    }

    public void Reset()
    {
        _previous = null;
        _lastRecorded = null;
    }
}
