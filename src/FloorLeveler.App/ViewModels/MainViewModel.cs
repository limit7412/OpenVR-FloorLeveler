using System.Collections.ObjectModel;
using System.Numerics;
using FloorLeveler.App.Localization;
using FloorLeveler.App.Services;
using FloorLeveler.Core;

namespace FloorLeveler.App.ViewModels;

/// <summary>
/// メインウィンドウのビューモデル (仕様 §6, §7)。
/// 一方向データフロー: サンプル記録 → Core で推定 → 補正算出 → プレビュー → 適用。
/// OpenVR へのアクセスは <see cref="ISessionGateway"/> の背後に隠し、
/// ゲートウェイ生成関数を注入することで単体テスト可能にする。
/// </summary>
public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly Func<ISessionGateway> _gatewayFactory;
    private readonly List<Vector3> _points = [];

    private ISessionGateway? _gateway;
    private AppliedCorrectionInfo? _lastApplied;
    private CorrectionResult? _pendingCorrection;

    private bool _isConnected;

    // ステータスと補正サマリは「文字列」ではなく「言語を受け取って文字列を作る関数」で
    // 持つ。言語を切り替えたときに、表示中のメッセージも作り直せるようにするため。
    private Func<Strings, string> _status = s => s.StatusNotConnected;
    private Func<Strings, string> _correctionSummary = _ => string.Empty;

    private GatewayDevice? _selectedDevice;
    private CorrectionMode _mode = CorrectionMode.GravityAlign;
    private bool _useRansac;
    private FloorEstimate? _estimate;
    private FloorPlot _topPlot = FloorProjection.TopDown([], TopAxisLabels(Strings.For(AppLanguage.Japanese)));
    private FloorPlot _sidePlot = FloorProjection.Side([], null, SideAxisLabels(Strings.For(AppLanguage.Japanese)));
    private bool _isPreviewing;
    private bool _largeCorrectionAcknowledged;
    private SamplingMode _samplingMode = SamplingMode.Manual;
    private IPoseSampler? _sampler;
    private BackupListItem? _selectedBackup;
    private AppLanguage _language;

    private readonly AppSettings _initialSettings;
    private readonly BackupService _backupService;
    private readonly RotatingLogWriter? _log;
    private readonly Func<DateTime> _clock;

    public MainViewModel()
        : this(OpenVrGateway.Connect, AppSettings.Load(), new BackupService(), new RotatingLogWriter())
    {
    }

    public MainViewModel(
        Func<ISessionGateway> gatewayFactory,
        AppSettings? settings = null,
        BackupService? backupService = null,
        RotatingLogWriter? log = null,
        Func<DateTime>? clock = null)
    {
        _gatewayFactory = gatewayFactory;
        _initialSettings = settings ?? new AppSettings();
        _backupService = backupService ?? new BackupService();
        _log = log;
        _clock = clock ?? (() => DateTime.Now);
        _useRansac = _initialSettings.UseRansac;
        _language = _initialSettings.Language;

        ConnectCommand = new RelayCommand(Connect, () => !IsConnected);
        RecordPointCommand = new RelayCommand(RecordPoint, () => IsConnected && SelectedDevice is not null && IsManualSampling);
        ClearPointsCommand = new RelayCommand(ClearPoints, () => _points.Count > 0);
        PreviewCommand = new RelayCommand(Preview, () => CanApply);
        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        CancelPreviewCommand = new RelayCommand(CancelPreview, () => IsPreviewing);
        UndoCommand = new RelayCommand(Undo, () => _lastApplied is not null && !IsPreviewing);
        BackupCommand = new RelayCommand(Backup, () => IsConnected && !IsPreviewing);
        RestoreLatestCommand = new RelayCommand(RestoreLatest, () => IsConnected && !IsPreviewing && _backupService.LatestRestorable() is not null);
        RefreshBackupsCommand = new RelayCommand(RefreshBackups);
        RestoreSelectedCommand = new RelayCommand(
            RestoreSelected, () => IsConnected && !IsPreviewing && SelectedBackup is not null);
    }

    public ObservableCollection<GatewayDevice> Devices { get; } = [];

    public RelayCommand ConnectCommand { get; }

    public RelayCommand RecordPointCommand { get; }

    public RelayCommand ClearPointsCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand CancelPreviewCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand BackupCommand { get; }

    public RelayCommand RestoreLatestCommand { get; }

    public RelayCommand RefreshBackupsCommand { get; }

    public RelayCommand RestoreSelectedCommand { get; }

    /// <summary>
    /// 保存済みバックアップの一覧 (新しい順、仕様 F-6 の「任意時点への復元」)。
    /// 明示的に選んで復元する用途のため、自動退避 (Auto) も種別を添えて含める
    /// (最新の自動選択では除外するが、ユーザーが指定するなら選べてよい)。
    /// </summary>
    public ObservableCollection<BackupListItem> Backups { get; } = [];

    /// <summary>一覧で選択中のバックアップ。</summary>
    public BackupListItem? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                RestoreSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>現在の言語の UI 文字列。XAML はここ経由で束縛する (仕様 §6)。</summary>
    public Strings L => Strings.For(_language);

    /// <summary>UI の表示言語。切り替えると表示中の文言もその場で作り直す。</summary>
    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                OnLanguageChanged();
            }
        }
    }

    // ラジオ/コンボ用の個別バインディング。
    public bool IsJapanese
    {
        get => _language == AppLanguage.Japanese;
        set { if (value) Language = AppLanguage.Japanese; }
    }

    public bool IsEnglish
    {
        get => _language == AppLanguage.English;
        set { if (value) Language = AppLanguage.English; }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public string StatusMessage => _status(L);

    /// <summary>ステータス文言を差し替える (言語ではなく「作り方」を保持する)。</summary>
    private void SetStatus(Func<Strings, string> text)
    {
        _status = text;
        OnPropertyChanged(nameof(StatusMessage));
    }

    public GatewayDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                RecordPointCommand.RaiseCanExecuteChanged();
                RebuildSampler(); // デバイスが変わると接地プロファイルも変わる
            }
        }
    }

    /// <summary>サンプリング方式 (仕様 F-1)。</summary>
    public SamplingMode SamplingMode
    {
        get => _samplingMode;
        set
        {
            if (SetProperty(ref _samplingMode, value))
            {
                RebuildSampler();
                OnPropertyChanged(nameof(IsManualSampling));
                OnPropertyChanged(nameof(IsAutoSampling));
                RecordPointCommand.RaiseCanExecuteChanged();
                SetStatus(value switch
                {
                    SamplingMode.Stillness => s => s.StatusStillnessMode,
                    SamplingMode.Continuous => s => s.StatusContinuousMode,
                    _ => s => s.StatusManualMode,
                });
            }
        }
    }

    public bool IsManualSampling => _samplingMode == SamplingMode.Manual;

    public bool IsAutoSampling => _samplingMode != SamplingMode.Manual;

    // ラジオボタン用の個別バインディング。
    public bool IsStillnessSampling
    {
        get => _samplingMode == SamplingMode.Stillness;
        set { if (value) SamplingMode = SamplingMode.Stillness; }
    }

    public bool IsContinuousSampling
    {
        get => _samplingMode == SamplingMode.Continuous;
        set { if (value) SamplingMode = SamplingMode.Continuous; }
    }

    public bool IsManualSamplingSelected
    {
        get => _samplingMode == SamplingMode.Manual;
        set { if (value) SamplingMode = SamplingMode.Manual; }
    }

    /// <summary>true=モード B (実測床面合わせ)、false=モード A (重力水平化)。</summary>
    public bool UseMeasuredFloorMode
    {
        get => _mode == CorrectionMode.MeasuredFloor;
        set
        {
            var mode = value ? CorrectionMode.MeasuredFloor : CorrectionMode.GravityAlign;
            if (SetProperty(ref _mode, mode))
            {
                OnPropertyChanged(nameof(UseMeasuredFloorMode));
                Recompute();
            }
        }
    }

    public bool UseRansac
    {
        get => _useRansac;
        set
        {
            if (SetProperty(ref _useRansac, value))
            {
                Recompute();
            }
        }
    }

    public int PointCount => _points.Count;

    /// <summary>記録点数のラベル (語順が言語で変わるため書式ごとリソースに置く)。</summary>
    public string PointCountText => L.PointCountLabel(_points.Count);

    /// <summary>広がりのラベル。</summary>
    public string SpreadLabelText => L.SpreadLabel(SpreadText);

    /// <summary>俯瞰ビュー (XZ 平面) の投影データ (仕様 F-4)。</summary>
    public FloorPlot TopPlot
    {
        get => _topPlot;
        private set => SetProperty(ref _topPlot, value);
    }

    /// <summary>側面ビュー (傾き方向の断面) の投影データ (仕様 F-4)。</summary>
    public FloorPlot SidePlot
    {
        get => _sidePlot;
        private set => SetProperty(ref _sidePlot, value);
    }

    public string SpreadText => _estimate is null
        ? "-"
        : $"{_estimate.Quality.SpreadMeters * 100f:F0} cm";

    public string TiltText => _estimate?.Plane is { } p
        ? L.TiltValue(p.TiltAngleDegrees, p.TiltAzimuthDegrees)
        : "-";

    public string ResidualText => _estimate?.Plane is { } p
        ? L.ResidualValue(p.RmsResidual * 1000f, p.MaxResidual * 1000f)
        : "-";

    public string CorrectionSummary => _correctionSummary(L);

    public bool IsPreviewing
    {
        get => _isPreviewing;
        private set
        {
            if (SetProperty(ref _isPreviewing, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>10° 超の補正 (仕様 F-3 の確認要求) に対するユーザーの確認状態。</summary>
    public bool LargeCorrectionAcknowledged
    {
        get => _largeCorrectionAcknowledged;
        set
        {
            if (SetProperty(ref _largeCorrectionAcknowledged, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>補正が 10° を超えており適用前に確認チェックが必要かどうか。</summary>
    public bool NeedsConfirmation => _pendingCorrection is { RequiresConfirmation: true };

    /// <summary>
    /// 推定・モードから算出した補正が適用可能か。10° 超の補正は
    /// <see cref="LargeCorrectionAcknowledged"/> をチェックするまで適用できない。
    /// </summary>
    public bool CanApply => _pendingCorrection is { IsNegligible: false } c
        && (!c.RequiresConfirmation || LargeCorrectionAcknowledged);

    /// <summary>モード B のみサンプリングが必須 (モード A は S→R 行列だけで算出可能)。</summary>
    public bool IsSamplingRequired => _mode == CorrectionMode.MeasuredFloor;

    private void Connect()
    {
        try
        {
            _gateway = _gatewayFactory();

            // ChaperoneSetup が実際に読めることを確認してから接続成立とする。
            // (後続の再計算は失敗を握りつぶすため、ここで明示的に検証する。)
            _ = _gateway.GetStandingZeroPose();

            IsConnected = true;
            RefreshDevices();
            Recompute();

            // 初回接続時に現在の設定を自動退避しておく (仕様 F-6)。
            TryAutoBackup();
            RefreshBackups(); // 既存の退避も含めて一覧を初期化する

            _log?.Log("接続しました。");
            SetStatus(s => s.StatusConnected);
        }
        catch (Exception ex)
        {
            // 初期化成功後の ChaperoneSetup 呼び出しなどで失敗する環境もあるため、
            // 接続シーケンス全体を捕捉して未接続状態へ戻す (仕様 §9)。
            _gateway?.Dispose();
            _gateway = null;
            IsConnected = false;

            // dll が無いケースだけは OpenVR のメッセージより具体的な案内を出す。
            SetStatus(ex is SessionUnavailableException { Reason: SessionFailure.NativeLibraryMissing }
                ? s => s.StatusNativeLibraryMissing
                : s => s.StatusConnectFailed(ErrorText.Describe(ex, s)));
        }
    }

    /// <summary>床サンプリングに使えるデバイス種別か (仕様 F-1: コントローラー / トラッカー)。</summary>
    private static bool IsSamplingDevice(GatewayDevice device)
        => device.Kind is nameof(FloorLeveler.OpenVr.ETrackedDeviceClass.Controller)
            or nameof(FloorLeveler.OpenVr.ETrackedDeviceClass.GenericTracker);

    private void RefreshDevices()
    {
        Devices.Clear();
        if (_gateway is null)
        {
            return;
        }

        // HMD やベースステーションは床に置けないため対象から除外する。
        foreach (var device in _gateway.ListDevices().Where(IsSamplingDevice))
        {
            Devices.Add(device);
        }

        SelectedDevice = Devices.FirstOrDefault();
    }

    private void RecordPoint()
    {
        if (_gateway is null || SelectedDevice is null)
        {
            return;
        }

        var pose = _gateway.GetDevicePose(SelectedDevice.Index);
        if (pose is null)
        {
            SetStatus(s => s.StatusNoValidPose);
            return;
        }

        // デバイス原点ではなく接地点を記録する (仕様 F-1 の接地オフセット)。
        var profile = ContactProfileFor(SelectedDevice);
        _points.Add(profile.ContactPoint(pose.Value));
        var recorded = _points.Count;
        SetStatus(s => s.StatusSampleRecorded(recorded));
        Recompute();
    }

    /// <summary>デバイス種別に対応する内蔵接地プロファイルを返す (F-1)。</summary>
    private static DeviceContactProfile ContactProfileFor(GatewayDevice device)
        => device.Kind switch
        {
            nameof(FloorLeveler.OpenVr.ETrackedDeviceClass.GenericTracker) => BuiltInDeviceProfiles.ViveTracker30,
            nameof(FloorLeveler.OpenVr.ETrackedDeviceClass.Controller) => BuiltInDeviceProfiles.IndexController,
            _ => new DeviceContactProfile(device.Kind, System.Numerics.Vector3.Zero),
        };

    /// <summary>選択デバイス・方式に応じた自動サンプラーを (再)生成する。</summary>
    private void RebuildSampler()
    {
        _sampler = (_samplingMode, SelectedDevice) switch
        {
            (SamplingMode.Stillness, { } d) => new StillnessSampler(ContactProfileFor(d)),
            (SamplingMode.Continuous, { } d) => new ContinuousSampler(ContactProfileFor(d)),
            _ => null,
        };
    }

    /// <summary>
    /// 自動サンプリングの 1 フレーム分の処理 (仕様 F-1)。Shell 側のタイマーが
    /// 一定間隔で呼び出す。現在のデバイスポーズをサンプラーに与え、記録すべき
    /// フレームなら点群に追加して再計算する。
    /// </summary>
    public void PollSample()
    {
        if (_gateway is null || _sampler is null || SelectedDevice is null
            || _samplingMode == SamplingMode.Manual)
        {
            return;
        }

        // プレビュー中は自動記録しない (記録→再計算で未確定プレビューが破棄されるのを防ぐ)。
        // 観測を止める間は連続性を切り、プレビュー中の移動をまたいだ大きな dt で速度を
        // 過小評価して復帰後に誤記録するのを防ぐ (トラッキングロスト時と同じ扱い)。
        if (IsPreviewing)
        {
            _sampler.BreakContinuity();
            return;
        }

        var pose = _gateway.GetDevicePose(SelectedDevice.Index);
        if (pose is null)
        {
            // トラッキングロスト中は連続性を切り、復帰後の誤記録を防ぐ。
            _sampler.BreakContinuity();
            return;
        }

        var timestamp = new TimeSpan(_clock().Ticks);
        if (_sampler.Feed(timestamp, pose.Value) is { } contact)
        {
            _points.Add(contact);
            var recorded = _points.Count;
            SetStatus(s => s.StatusAutoRecorded(recorded));
            Recompute();
        }
    }

    private void ClearPoints()
    {
        _points.Clear();
        _sampler?.Reset(); // 自動サンプラーの再武装・間隔状態も初期化する
        SetStatus(s => s.StatusPointsCleared);
        Recompute();
    }

    /// <summary>現在の点群・モード・設定から推定と補正を再計算する (純粋部分は Core)。</summary>
    private void Recompute()
    {
        // プレビュー中に入力が変わった場合はまずプレビューを破棄する。
        // working copy (補正適用済み) を基準に再計算すると補正の基準がずれるため。
        DiscardPreview();

        _estimate = FloorEstimation.Estimate(_points, _useRansac, _initialSettings.RansacThresholdMeters);
        _pendingCorrection = TryComputeCorrection();

        // 点群・推定平面を 2D ビューへ投影し直す (仕様 F-4)。
        TopPlot = FloorProjection.TopDown(_points, TopAxisLabels(L));
        SidePlot = FloorProjection.Side(_points, _estimate?.Plane, SideAxisLabels(L));

        // 補正が変わったら確認状態はリセットする (別の大補正を無確認で通さない)。
        _largeCorrectionAcknowledged = false;
        OnPropertyChanged(nameof(LargeCorrectionAcknowledged));

        _correctionSummary = _pendingCorrection switch
        {
            null => s => s.StatusCorrectionUnavailable,
            { IsNegligible: true } => s => s.StatusCorrectionNegligible,
            { } c => s => s.CorrectionSummaryValue(c.RotationAngleDegrees, c.HeightChangeMeters * 1000f)
                + (c.RequiresConfirmation ? s.CorrectionNeedsConfirmationSuffix : string.Empty),
        };
        OnPropertyChanged(nameof(CorrectionSummary));

        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(PointCountText));
        OnPropertyChanged(nameof(SpreadText));
        OnPropertyChanged(nameof(SpreadLabelText));
        OnPropertyChanged(nameof(TiltText));
        OnPropertyChanged(nameof(ResidualText));
        OnPropertyChanged(nameof(IsSamplingRequired));
        OnPropertyChanged(nameof(NeedsConfirmation));
        RaiseCommandStates();
    }

    private CorrectionResult? TryComputeCorrection()
    {
        try
        {
            return TryComputeCorrectionCore();
        }
        catch (Exception ex)
        {
            // ChaperoneSetup の読み取り失敗などは「補正を算出できません」に落とす。
            SetStatus(s => s.StatusCorrectionComputeFailed(ErrorText.Describe(ex, s)));
            return null;
        }
    }

    private CorrectionResult? TryComputeCorrectionCore()
    {
        if (_gateway is null)
        {
            return null;
        }

        if (_mode == CorrectionMode.GravityAlign)
        {
            // モード A はサンプリング不要。床サンプルは品質要件 (点数・広がり) を
            // 満たす場合のみ高さ合わせに使う。品質不足の点群 (机上の誤記録など) で
            // 高さを動かさないため。
            var standing = _gateway.GetStandingZeroPose();
            var floor = _estimate is { CanCorrect: true } e ? e.Plane : null;
            return Correction.ComputeGravityAlign(standing, floor);
        }

        // モード B は推定平面が必須。
        return _estimate is { CanCorrect: true, Plane: { } plane }
            ? Correction.ComputeFloorAlign(plane)
            : null;
    }

    /// <summary>
    /// プレビュー中の working copy を破棄して commit 済み状態に戻す。
    /// Preview / Apply はいずれも呼び出し前にこれを通すことで、プレビューで
    /// 適用済みの補正に重ねて再適用してしまう二重適用を防ぐ。
    /// </summary>
    private void DiscardPreview()
    {
        if (_gateway is null || !IsPreviewing)
        {
            return;
        }

        _gateway.HidePreview();
        _gateway.Revert();
        IsPreviewing = false;
    }

    private void Preview()
    {
        if (_gateway is null || _pendingCorrection is null || !CanApply)
        {
            return;
        }

        try
        {
            DiscardPreview();
            _gateway.ApplyCorrection(_pendingCorrection);
            _gateway.ShowPreview();
            IsPreviewing = true;
            SetStatus(s => s.StatusPreviewing);
        }
        catch (Exception ex)
        {
            _gateway.Revert();
            IsPreviewing = false;
            SetStatus(s => s.StatusPreviewFailed(ErrorText.Describe(ex, s)));
        }
    }

    private void CancelPreview()
    {
        if (_gateway is null)
        {
            return;
        }

        DiscardPreview();
        SetStatus(s => s.StatusPreviewDiscarded);
    }

    private void Apply()
    {
        if (_gateway is null || _pendingCorrection is null || !CanApply)
        {
            return;
        }

        try
        {
            // プレビューで working copy に入れた補正は必ず破棄してから
            // 改めて 1 回だけ適用する (二重適用の防止)。
            DiscardPreview();

            // 適用前の状態をファイルに退避しておく (仕様 F-6)。commit 後にアプリが
            // 落ちてメモリ上のアンドゥ履歴を失っても、適用前へ復旧できるようにする。
            // この退避に失敗した場合はファイル復旧手段を確保できないため適用を中止する。
            if (!TrySaveBackup(BackupKind.PreApply))
            {
                SetStatus(s => s.StatusBackupBeforeApplyFailed);
                return;
            }

            var correction = _pendingCorrection;
            var applied = _gateway.ApplyCorrection(correction);
            if (!_gateway.Commit())
            {
                _gateway.Revert();
                SetStatus(s => s.StatusApplyFailedReverted);
                return;
            }

            _lastApplied = applied;

            // 適用操作は変更前後の行列値を記録する (仕様 NF-4)。
            _log?.Log(
                $"補正を適用 (モード {correction.Mode}, 回転 {correction.RotationAngleDegrees:F3}°): " +
                $"S→R {FormatMatrix(applied.OldStandingToRaw)} → {FormatMatrix(applied.NewStandingToRaw)}");

            // 適用済みの補正を保留したままにしない (二度押しで再合成される問題の防止)。
            // サンプル点は新しい standing 座標へ写し、新姿勢を基準に再計算する。
            for (var i = 0; i < _points.Count; i++)
            {
                _points[i] = correction.StandingSpaceMap.TransformPoint(_points[i]);
            }

            // standing 座標系が変わったため、旧座標の履歴を持つサンプラーを初期化する。
            _sampler?.Reset();
            Recompute();
            SetStatus(s => s.StatusApplied);
            UndoCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            // 途中失敗時は中途半端な状態を commit しない (仕様 NF-5)。
            _gateway.Revert();
            IsPreviewing = false;
            SetStatus(s => s.StatusApplyFailed(ErrorText.Describe(ex, s)));
        }
    }

    private void Undo()
    {
        if (_gateway is null || _lastApplied is null)
        {
            return;
        }

        try
        {
            // 直前の適用を打ち消す補正 (新→旧 standing の写像) を合成して適用する。
            var inverseMap = RigidTransform.Compose(
                _lastApplied.OldStandingToRaw.Inverse(),
                _lastApplied.NewStandingToRaw);
            var undo = new CorrectionResult(
                CorrectionMode.GravityAlign,
                inverseMap,
                0f,
                Vector3.Zero,
                0f,
                IsNegligible: false,
                RequiresConfirmation: false);

            _gateway.ApplyCorrection(undo);
            if (!_gateway.Commit())
            {
                _gateway.Revert();
                SetStatus(s => s.StatusUndoFailed);
                return;
            }

            _lastApplied = null;
            _log?.Log("直前の補正を元に戻しました。");

            // standing 座標系が元に戻ったため、サンプル点も元の座標へ戻して再計算する。
            for (var i = 0; i < _points.Count; i++)
            {
                _points[i] = inverseMap.TransformPoint(_points[i]);
            }

            _sampler?.Reset();
            Recompute();
            SetStatus(s => s.StatusUndone);
            UndoCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _gateway.Revert();
            SetStatus(s => s.StatusUndoFailedWithReason(ErrorText.Describe(ex, s)));
        }
    }

    private void Backup()
    {
        if (_gateway is null)
        {
            return;
        }

        try
        {
            var path = _backupService.Save(_gateway.CaptureSnapshot(), _clock(), BackupKind.Manual);
            _log?.Log($"バックアップを保存: {path}");
            var fileName = Path.GetFileName(path);
            SetStatus(s => s.StatusBackupSaved(fileName));
            RestoreLatestCommand.RaiseCanExecuteChanged();
            RefreshBackups();
        }
        catch (Exception ex)
        {
            SetStatus(s => s.StatusBackupFailed(ErrorText.Describe(ex, s)));
        }
    }

    private void RestoreLatest()
    {
        if (_gateway is null)
        {
            return;
        }

        // 接続時の自動退避は復元対象から除外する (悪い補正後の再接続で自動退避が
        // 最新になり、正常な適用前状態へ戻れなくなるのを防ぐ)。
        var candidates = _backupService.RestorableCandidates();
        if (candidates.Count == 0)
        {
            SetStatus(s => s.StatusNoRestorableBackup);
            return;
        }

        // 新しい順に試し、復元できない候補 (保存中断・不完全コピー・手動編集による
        // 形状不正、または OpenVR が Live 反映を拒否する値) は飛ばして次の有効な
        // 候補へ進む。
        var skipped = 0;
        foreach (var entry in candidates)
        {
            if (!TryRestore(entry))
            {
                skipped++;
                continue;
            }

            AfterRestore(entry, skipped);
            return;
        }

        SetStatus(s => s.StatusAllRestoresFailed);
    }

    /// <summary>一覧で選択したバックアップへ復元する (仕様 F-6 の任意時点への復元)。</summary>
    private void RestoreSelected()
    {
        if (_gateway is null || SelectedBackup is not { } item)
        {
            return;
        }

        // 明示的に指定された 1 件のみを試し、失敗しても他候補へは進まない
        // (ユーザーが選んだ時点と異なる状態を黙って復元しないため)。
        if (!TryRestore(item.Entry))
        {
            // 表示名ごと作り直す。生成済みの文字列を掴むと、失敗後に言語を切り替えたとき
            // 種別名だけ旧言語のまま残る。
            var entry = item.Entry;
            SetStatus(s => s.StatusRestoreFailed(BackupListItem.Create(entry, s).DisplayName));
            return;
        }

        AfterRestore(item.Entry, skipped: 0);
    }

    /// <summary>
    /// バックアップ 1 件を working copy へ書き戻して commit する。読み込み・形状検証・
    /// 書き戻し・commit のいずれで失敗しても revert し、false を返す (仕様 NF-5)。
    /// </summary>
    private bool TryRestore(BackupEntry entry)
    {
        if (_gateway is null)
        {
            return false;
        }

        try
        {
            var snapshot = _backupService.Load(entry.Path);
            snapshot.Validate(); // 形状不正 (行列・境界・プレイエリア) はここで例外
            _gateway.RestoreSnapshot(snapshot);
        }
        catch
        {
            TryRevert();
            return false;
        }

        try
        {
            if (_gateway.Commit())
            {
                return true;
            }
        }
        catch
        {
            // commit 自体が例外を投げた場合 (セッション断など) も候補固有の失敗として
            // 扱う。ここで捕捉しないと revert されないまま UI へ例外が伝播し、選択した
            // スナップショットが working copy に残ってしまう (仕様 NF-5)。
            TryRevert();
            return false;
        }

        // Live 反映が拒否された場合も候補固有の失敗として扱う。
        TryRevert();
        return false;
    }

    /// <summary>復元成功後の状態初期化と通知。</summary>
    /// <param name="skipped">この復元までに読み飛ばした候補数 (0 なら表示しない)。</param>
    private void AfterRestore(BackupEntry entry, int skipped)
    {
        // 復元により standing 座標系が不連続に変わるため、旧座標系で記録した
        // サンプル点・保留補正・アンドゥ履歴をすべて破棄して再計算する
        // (Apply/Undo と異なり復元前後の姿勢を関係づける単一の変換が無いため、
        // 点群は写像ではなくクリアする)。
        _points.Clear();
        _sampler?.Reset(); // 復元で standing 座標系が変わったためサンプラーも初期化
        _lastApplied = null;
        _log?.Log($"バックアップを復元: {entry.Path}" + (skipped > 0 ? $" ({skipped} 件をスキップ)" : string.Empty));
        Recompute();
        var timestamp = entry.Timestamp;
        SetStatus(skipped > 0
            ? s => s.StatusRestoredWithSkips(timestamp, skipped)
            : s => s.StatusRestored(timestamp));
        UndoCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// バックアップ一覧を読み直す。選択は同じファイルがあれば維持する
    /// (退避直後の再読込で選択が飛ばないようにするため)。
    /// </summary>
    private void RefreshBackups()
    {
        var previous = SelectedBackup?.Entry.Path;
        Backups.Clear();
        foreach (var entry in _backupService.List())
        {
            Backups.Add(BackupListItem.Create(entry, L));
        }

        SelectedBackup = Backups.FirstOrDefault(e => e.Entry.Path == previous);
        RestoreSelectedCommand.RaiseCanExecuteChanged();
    }

    private void TryRevert()
    {
        try
        {
            _gateway?.Revert();
        }
        catch
        {
            // 破損候補スキップ時の後始末失敗は無視する。
        }
    }

    private void TryAutoBackup()
        => TrySaveBackup(BackupKind.Auto);

    /// <summary>バックアップを保存し、成功したかを返す。</summary>
    private bool TrySaveBackup(BackupKind kind)
    {
        if (_gateway is null)
        {
            return false;
        }

        try
        {
            _backupService.Save(_gateway.CaptureSnapshot(), _clock(), kind);
            // 退避直後は復元ボタンを有効化できる (接続時の RaiseCommandStates は
            // この保存より前に走るため、ここで明示的に再評価する)。
            RestoreLatestCommand.RaiseCanExecuteChanged();
            RefreshBackups();
            return true;
        }
        catch
        {
            // Auto の失敗は接続を妨げない。PreApply の失敗は呼び出し側で適用を中止する。
            return false;
        }
    }

    /// <summary>俯瞰ビューの軸ラベル。</summary>
    private static PlotAxisLabels TopAxisLabels(Strings strings)
        => new(strings.PlotAxisX, strings.PlotAxisZ);

    /// <summary>側面ビューの軸ラベル。</summary>
    private static PlotAxisLabels SideAxisLabels(Strings strings)
        => new(strings.PlotAxisTiltDirection, strings.PlotAxisHeight);

    private static string FormatMatrix(RigidTransform t)
    {
        var m = t.ToRowMajor3x4();
        return $"[{m[0, 0]:F4},{m[0, 1]:F4},{m[0, 2]:F4},{m[0, 3]:F4}; "
            + $"{m[1, 0]:F4},{m[1, 1]:F4},{m[1, 2]:F4},{m[1, 3]:F4}; "
            + $"{m[2, 0]:F4},{m[2, 1]:F4},{m[2, 2]:F4},{m[2, 3]:F4}]";
    }

    private void RaiseCommandStates()
    {
        ConnectCommand.RaiseCanExecuteChanged();
        RecordPointCommand.RaiseCanExecuteChanged();
        ClearPointsCommand.RaiseCanExecuteChanged();
        PreviewCommand.RaiseCanExecuteChanged();
        ApplyCommand.RaiseCanExecuteChanged();
        CancelPreviewCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        BackupCommand.RaiseCanExecuteChanged();
        RestoreLatestCommand.RaiseCanExecuteChanged();
        RestoreSelectedCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanApply));
    }

    /// <summary>
    /// 言語が変わったので、言語に依存する表示をすべて作り直す。
    /// 文言を関数で保持しているため、表示中のステータスもその場で切り替わる。
    /// </summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(L));
        OnPropertyChanged(nameof(IsJapanese));
        OnPropertyChanged(nameof(IsEnglish));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(CorrectionSummary));
        OnPropertyChanged(nameof(TiltText));
        OnPropertyChanged(nameof(ResidualText));
        OnPropertyChanged(nameof(PointCountText));
        OnPropertyChanged(nameof(SpreadLabelText));

        // 一覧のラベルとプロットの軸ラベルは生成済みの文字列なので、
        // 作り直さないと旧言語のまま残る。
        TopPlot = FloorProjection.TopDown(_points, TopAxisLabels(L));
        SidePlot = FloorProjection.Side(_points, _estimate?.Plane, SideAxisLabels(L));
        RefreshBackups();
    }

    /// <summary>現在の UI 状態を反映した設定を返す (終了時の保存用、仕様 F-7)。</summary>
    public AppSettings SnapshotSettings(double windowWidth, double windowHeight)
        => _initialSettings with
        {
            UseRansac = _useRansac,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
            Language = _language,
        };

    public void Dispose()
    {
        // 未確定のプレビューを working copy に残したまま終了しない。
        try
        {
            DiscardPreview();
        }
        catch
        {
            // 終了処理のため失敗は無視する。
        }

        _gateway?.Dispose();
        _gateway = null;
    }
}
