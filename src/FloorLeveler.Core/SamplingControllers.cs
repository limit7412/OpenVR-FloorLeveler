using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>時刻付きポーズを与えると記録すべき接地点を返す自動サンプラー (仕様 F-1)。</summary>
public interface IPoseSampler
{
    /// <summary>1 フレーム分のポーズを与える。記録すべきなら接地点、そうでなければ null。</summary>
    Vector3? Feed(TimeSpan timestamp, RigidTransform devicePose);

    /// <summary>状態を初期化する。</summary>
    void Reset();

    /// <summary>
    /// 時間的連続性を切る (トラッキングロストなどで観測が途切れた場合)。
    /// 次の有効フレームは基準の再確立のみに使い、欠落をまたいだ速度計算や
    /// 静止判定で誤記録しないようにする。記録済みの点は保持する。
    /// </summary>
    void BreakContinuity();
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

    /// <summary>
    /// 観測が途切れた場合に履歴を破棄する。欠落をまたいだ古いサンプルで静止判定を
    /// 誤らないようにする (記録済み位置の離脱判定は維持する)。
    /// </summary>
    public void BreakContinuity() => _history.Clear();
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
    private readonly float _minMotion;
    private readonly TimeSpan _maxGap;

    private TimedSample? _previous;
    private Vector3? _lastRecorded;

    /// <param name="profile">接地オフセットのプロファイル。</param>
    /// <param name="maxSpeedMetersPerSecond">これを超える速度のフレームは棄却 (既定 1.0 m/s)。</param>
    /// <param name="minSpacingMeters">記録点の最小間隔 (既定 5 cm)。点群の過密を防ぐ。</param>
    /// <param name="minMotionMeters">
    /// 記録に必要な直前フレームからの最小移動量 (既定 5 mm)。静止しているフレームを
    /// 記録しないことで、持ち上げ後に空中で止めた姿勢などが点群に混入するのを防ぐ。
    /// </param>
    /// <param name="maxSampleGap">
    /// 直前サンプルからの時間差がこれを超えたら連続性が切れたとみなし、そのフレームを
    /// 基準確立のみに使う (既定 300 ms)。タイマー停止・スリープなどで観測が飛んだ際、
    /// 大きな dt で速度を過小評価して高速移動を記録するのを防ぐ (想定ポーリング間隔
    /// 50 ms の数倍)。
    /// </param>
    public ContinuousSampler(
        DeviceContactProfile profile,
        float maxSpeedMetersPerSecond = Sampling.DefaultMaxSpeedMetersPerSecond,
        float minSpacingMeters = 0.05f,
        float minMotionMeters = 0.005f,
        TimeSpan? maxSampleGap = null)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _maxSpeed = maxSpeedMetersPerSecond;
        _minSpacing = minSpacingMeters;
        _minMotion = minMotionMeters;
        _maxGap = maxSampleGap ?? TimeSpan.FromMilliseconds(300);
    }

    /// <summary>
    /// デバイスの時刻付きポーズを 1 フレーム分与える。記録すべきフレームでは接地点を
    /// 返し、速度超過・間隔不足・静止のフレームでは null を返す。
    /// </summary>
    public Vector3? Feed(TimeSpan timestamp, RigidTransform devicePose)
    {
        var contact = _profile.ContactPoint(devicePose);
        var current = new TimedSample(timestamp, contact);

        // 直前フレームが無い場合 (開始直後・トラッキングロスト後・速度超過の棄却後) は、
        // このフレームを基準の確立だけに使い記録しない。ドラッグの途中でない初回ポーズ
        // (手に持った高さなど) や、欠落をまたいだ大きな dt での速度過小評価による誤記録を防ぐ。
        if (_previous is not { } prev)
        {
            _previous = current;
            return null;
        }

        // タイマー停止・スリープ等で前サンプルから想定以上に時間が空いた場合は、連続性が
        // 切れたとみなしてこのフレームを基準確立のみに使う。大きな dt では持ち上げ・移動でも
        // 平均速度が閾値以下に見え、欠落中の移動先を床点として記録してしまうため。
        if (timestamp - prev.Timestamp > _maxGap)
        {
            _previous = current;
            return null;
        }

        // 速度超過 (持ち上げ等) のフレームは棄却し、連続性を切る。棄却したポーズを
        // 基準にすると、次に空中で静止した姿勢を速度 0 と誤判定して記録してしまうため、
        // 床上のドラッグが再確立する (次フレームで基準を取り直す) まで再武装しない。
        if (Sampling.ExceedsSpeed(prev, current, _maxSpeed))
        {
            _previous = null;
            return null;
        }

        _previous = current;

        // 静止しているフレーム (直前からの移動がごくわずか) は記録しない。点はドラッグ中の
        // 移動からのみ得る。これにより持ち上げ後に空中で止めた姿勢を接地点にしない。
        if ((contact - prev.Position).Length() < _minMotion)
        {
            return null;
        }

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

    /// <summary>
    /// 観測が途切れた場合に速度の連続性を切り、次の有効フレームを基準確立のみ
    /// (記録しない) にする。記録済み位置は維持し、欠落中の移動を記録しない。
    /// </summary>
    public void BreakContinuity() => _previous = null;
}
