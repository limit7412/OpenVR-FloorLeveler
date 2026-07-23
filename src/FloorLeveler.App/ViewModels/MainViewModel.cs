using System.Collections.ObjectModel;
using System.Numerics;
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
    private string _statusMessage = "未接続です。SteamVR を起動して「接続」を押してください。";
    private GatewayDevice? _selectedDevice;
    private CorrectionMode _mode = CorrectionMode.GravityAlign;
    private bool _useRansac;
    private FloorEstimate? _estimate;
    private string _correctionSummary = string.Empty;
    private bool _isPreviewing;
    private bool _largeCorrectionAcknowledged;

    public MainViewModel()
        : this(OpenVrGateway.Connect)
    {
    }

    public MainViewModel(Func<ISessionGateway> gatewayFactory)
    {
        _gatewayFactory = gatewayFactory;

        ConnectCommand = new RelayCommand(Connect, () => !IsConnected);
        RecordPointCommand = new RelayCommand(RecordPoint, () => IsConnected && SelectedDevice is not null);
        ClearPointsCommand = new RelayCommand(ClearPoints, () => _points.Count > 0);
        PreviewCommand = new RelayCommand(Preview, () => CanApply);
        ApplyCommand = new RelayCommand(Apply, () => CanApply);
        CancelPreviewCommand = new RelayCommand(CancelPreview, () => IsPreviewing);
        UndoCommand = new RelayCommand(Undo, () => _lastApplied is not null && !IsPreviewing);
    }

    public ObservableCollection<GatewayDevice> Devices { get; } = [];

    public RelayCommand ConnectCommand { get; }

    public RelayCommand RecordPointCommand { get; }

    public RelayCommand ClearPointsCommand { get; }

    public RelayCommand PreviewCommand { get; }

    public RelayCommand ApplyCommand { get; }

    public RelayCommand CancelPreviewCommand { get; }

    public RelayCommand UndoCommand { get; }

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public GatewayDevice? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetProperty(ref _selectedDevice, value))
            {
                RecordPointCommand.RaiseCanExecuteChanged();
            }
        }
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

    public string SpreadText => _estimate is null
        ? "-"
        : $"{_estimate.Quality.SpreadMeters * 100f:F0} cm";

    public string TiltText => _estimate?.Plane is { } p
        ? $"{p.TiltAngleDegrees:F2}° (方位 {p.TiltAzimuthDegrees:F0}°)"
        : "-";

    public string ResidualText => _estimate?.Plane is { } p
        ? $"RMS {p.RmsResidual * 1000f:F1} mm / 最大 {p.MaxResidual * 1000f:F1} mm"
        : "-";

    public string CorrectionSummary
    {
        get => _correctionSummary;
        private set => SetProperty(ref _correctionSummary, value);
    }

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
            IsConnected = true;
            RefreshDevices();
            Recompute();
            StatusMessage = "SteamVR に接続しました。";
        }
        catch (SessionUnavailableException ex)
        {
            IsConnected = false;
            StatusMessage = $"接続に失敗しました: {ex.Message}";
        }
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        if (_gateway is null)
        {
            return;
        }

        foreach (var device in _gateway.ListDevices())
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
            StatusMessage = "トラッキングが有効なサンプルを取得できませんでした。";
            return;
        }

        // デバイス原点ではなく接地点を記録する (仕様 F-1 の接地オフセット)。
        var profile = ContactProfileFor(SelectedDevice);
        _points.Add(profile.ContactPoint(pose.Value));
        StatusMessage = $"サンプルを記録しました (計 {_points.Count} 点)。";
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

    private void ClearPoints()
    {
        _points.Clear();
        StatusMessage = "サンプルをクリアしました。";
        Recompute();
    }

    /// <summary>現在の点群・モード・設定から推定と補正を再計算する (純粋部分は Core)。</summary>
    private void Recompute()
    {
        _estimate = FloorEstimation.Estimate(_points, _useRansac);
        _pendingCorrection = TryComputeCorrection();

        // 補正が変わったら確認状態はリセットする (別の大補正を無確認で通さない)。
        _largeCorrectionAcknowledged = false;
        OnPropertyChanged(nameof(LargeCorrectionAcknowledged));

        CorrectionSummary = _pendingCorrection switch
        {
            null => "補正を算出できません。",
            { IsNegligible: true } => "補正不要です (回転 0.05° 未満かつ並進 1 mm 未満)。",
            { } c => $"回転 {c.RotationAngleDegrees:F2}°、高さ変化 {c.HeightChangeMeters * 1000f:F1} mm"
                + (c.RequiresConfirmation ? " (10° 超: 適用前に確認が必要)" : string.Empty),
        };

        OnPropertyChanged(nameof(PointCount));
        OnPropertyChanged(nameof(SpreadText));
        OnPropertyChanged(nameof(TiltText));
        OnPropertyChanged(nameof(ResidualText));
        OnPropertyChanged(nameof(IsSamplingRequired));
        OnPropertyChanged(nameof(NeedsConfirmation));
        RaiseCommandStates();
    }

    private CorrectionResult? TryComputeCorrection()
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
        if (_gateway is null || _pendingCorrection is null)
        {
            return;
        }

        try
        {
            DiscardPreview();
            _gateway.ApplyCorrection(_pendingCorrection);
            _gateway.ShowPreview();
            IsPreviewing = true;
            StatusMessage = "プレビュー中です。「適用」で確定、「プレビュー破棄」で破棄します。";
        }
        catch (Exception ex)
        {
            _gateway.Revert();
            IsPreviewing = false;
            StatusMessage = $"プレビューに失敗しました: {ex.Message}";
        }
    }

    private void CancelPreview()
    {
        if (_gateway is null)
        {
            return;
        }

        DiscardPreview();
        StatusMessage = "プレビューを破棄しました。";
    }

    private void Apply()
    {
        if (_gateway is null || _pendingCorrection is null)
        {
            return;
        }

        try
        {
            // プレビューで working copy に入れた補正は必ず破棄してから
            // 改めて 1 回だけ適用する (二重適用の防止)。
            DiscardPreview();

            var applied = _gateway.ApplyCorrection(_pendingCorrection);
            if (!_gateway.Commit())
            {
                _gateway.Revert();
                StatusMessage = "適用に失敗したため元に戻しました。";
                return;
            }

            _lastApplied = applied;
            StatusMessage = "補正を適用しました。";
            UndoCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            // 途中失敗時は中途半端な状態を commit しない (仕様 NF-5)。
            _gateway.Revert();
            IsPreviewing = false;
            StatusMessage = $"適用に失敗しました: {ex.Message}";
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
                StatusMessage = "元に戻す操作に失敗しました。";
                return;
            }

            _lastApplied = null;
            StatusMessage = "直前の補正を元に戻しました。";
            UndoCommand.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            _gateway.Revert();
            StatusMessage = $"元に戻す操作に失敗しました: {ex.Message}";
        }
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
        OnPropertyChanged(nameof(CanApply));
    }

    public void Dispose()
    {
        _gateway?.Dispose();
        _gateway = null;
    }
}
