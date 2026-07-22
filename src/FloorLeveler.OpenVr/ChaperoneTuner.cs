using System.Runtime.InteropServices;
using FloorLeveler.Core;

namespace FloorLeveler.OpenVr;

/// <summary>
/// IVRChaperoneSetup のラッパー。working copy への読み書きと補正の適用 (仕様 F-4, F-5) を担う。
/// いずれの setter も working copy のみを変更し、<see cref="Commit"/> を呼ぶまで反映されない。
/// 途中で失敗した場合は <see cref="Revert"/> を呼び、commit しないこと (仕様 NF-5)。
/// </summary>
public sealed class ChaperoneTuner
{
    private readonly CommitWorkingCopyDelegate _commit;
    private readonly NoArgsDelegate _revert;
    private readonly GetPlayAreaSizeDelegate _getWorkingPlayAreaSize;
    private readonly GetCollisionBoundsCountDelegate _getWorkingBoundsCount;
    private readonly GetCollisionBoundsDelegate _getWorkingBounds;
    private readonly GetMatrix34Delegate _getWorkingSeated;
    private readonly GetMatrix34Delegate _getWorkingStanding;
    private readonly SetCollisionBoundsDelegate _setWorkingBounds;
    private readonly SetMatrix34Delegate _setWorkingSeated;
    private readonly SetMatrix34Delegate _setWorkingStanding;
    private readonly ReloadFromDiskDelegate _reloadFromDisk;
    private readonly NoArgsDelegate _showPreview;
    private readonly NoArgsDelegate _hidePreview;

    internal ChaperoneTuner(IntPtr fnTablePtr)
    {
        var table = Marshal.PtrToStructure<VRChaperoneSetupFnTable>(fnTablePtr);
        _commit = Marshal.GetDelegateForFunctionPointer<CommitWorkingCopyDelegate>(table.CommitWorkingCopy);
        _revert = Marshal.GetDelegateForFunctionPointer<NoArgsDelegate>(table.RevertWorkingCopy);
        _getWorkingPlayAreaSize = Marshal.GetDelegateForFunctionPointer<GetPlayAreaSizeDelegate>(table.GetWorkingPlayAreaSize);
        _getWorkingBoundsCount = Marshal.GetDelegateForFunctionPointer<GetCollisionBoundsCountDelegate>(table.GetWorkingCollisionBoundsInfo);
        _getWorkingBounds = Marshal.GetDelegateForFunctionPointer<GetCollisionBoundsDelegate>(table.GetWorkingCollisionBoundsInfo);
        _getWorkingSeated = Marshal.GetDelegateForFunctionPointer<GetMatrix34Delegate>(table.GetWorkingSeatedZeroPoseToRawTrackingPose);
        _getWorkingStanding = Marshal.GetDelegateForFunctionPointer<GetMatrix34Delegate>(table.GetWorkingStandingZeroPoseToRawTrackingPose);
        _setWorkingBounds = Marshal.GetDelegateForFunctionPointer<SetCollisionBoundsDelegate>(table.SetWorkingCollisionBoundsInfo);
        _setWorkingSeated = Marshal.GetDelegateForFunctionPointer<SetMatrix34Delegate>(table.SetWorkingSeatedZeroPoseToRawTrackingPose);
        _setWorkingStanding = Marshal.GetDelegateForFunctionPointer<SetMatrix34Delegate>(table.SetWorkingStandingZeroPoseToRawTrackingPose);
        _reloadFromDisk = Marshal.GetDelegateForFunctionPointer<ReloadFromDiskDelegate>(table.ReloadFromDisk);
        _showPreview = Marshal.GetDelegateForFunctionPointer<NoArgsDelegate>(table.ShowWorkingSetPreview);
        _hidePreview = Marshal.GetDelegateForFunctionPointer<NoArgsDelegate>(table.HideWorkingSetPreview);
    }

    /// <summary>working copy の standing → raw 変換 (S→R 行列) を取得する。</summary>
    public RigidTransform GetWorkingStandingZeroPose()
    {
        var m = default(HmdMatrix34);
        if (!_getWorkingStanding(ref m))
        {
            throw new OpenVrException("GetWorkingStandingZeroPoseToRawTrackingPose が失敗しました。");
        }

        return m.ToRigidTransform();
    }

    /// <summary>working copy の seated → raw 変換を取得する。</summary>
    public RigidTransform GetWorkingSeatedZeroPose()
    {
        var m = default(HmdMatrix34);
        if (!_getWorkingSeated(ref m))
        {
            throw new OpenVrException("GetWorkingSeatedZeroPoseToRawTrackingPose が失敗しました。");
        }

        return m.ToRigidTransform();
    }

    public void SetWorkingStandingZeroPose(RigidTransform standingToRaw)
    {
        var m = standingToRaw.ToHmdMatrix34();
        _setWorkingStanding(ref m);
    }

    public void SetWorkingSeatedZeroPose(RigidTransform seatedToRaw)
    {
        var m = seatedToRaw.ToHmdMatrix34();
        _setWorkingSeated(ref m);
    }

    /// <summary>
    /// working copy のコリジョン境界の全頂点 (壁 quad の配列) を取得する。
    /// 境界が設定されていない場合は空配列。取得に失敗した場合は
    /// <see cref="OpenVrException"/> を送出する (空境界と区別し、呼び出し側で revert させるため)。
    /// </summary>
    public HmdQuad[] GetWorkingCollisionBounds()
    {
        // 件数の問い合わせ (バッファ null)。OpenVR の規約上この呼び出しは false を
        // 返すため、戻り値ではなく count で判定する。
        var count = 0u;
        _getWorkingBoundsCount(IntPtr.Zero, ref count);
        if (count == 0)
        {
            return [];
        }

        var quads = new HmdQuad[count];
        if (!_getWorkingBounds(quads, ref count))
        {
            throw new OpenVrException(
                $"GetWorkingCollisionBoundsInfo が失敗しました (期待 quad 数: {count})。" +
                "境界を変換できないため補正を中止します。");
        }

        return quads;
    }

    public void SetWorkingCollisionBounds(HmdQuad[] quads)
        => _setWorkingBounds(quads, (uint)quads.Length);

    public (float SizeX, float SizeZ)? GetWorkingPlayAreaSize()
    {
        float x = 0f, z = 0f;
        return _getWorkingPlayAreaSize(ref x, ref z) ? (x, z) : null;
    }

    /// <summary>working copy を Live 設定へ反映する。false なら反映されていない。</summary>
    public bool Commit()
        => _commit(EChaperoneConfigFile.Live);

    /// <summary>working copy の変更を破棄して Live 設定へ戻す。</summary>
    public void Revert()
        => _revert();

    public void ReloadFromDisk()
        => _reloadFromDisk(EChaperoneConfigFile.Live);

    public void ShowWorkingSetPreview() => _showPreview();

    public void HideWorkingSetPreview() => _hidePreview();

    /// <summary>
    /// Core で算出した補正を working copy に適用する (仕様 F-4 手順 1〜4。commit は呼び出し側で行う)。
    /// 規約 (Core の CorrectionResult に集約): newS2R = oldS2R ∘ C⁻¹、境界頂点 v' = C(v)。
    /// </summary>
    /// <param name="correction">Core で算出した補正。</param>
    /// <param name="updateSeated">
    /// seated universe にも同一の raw 空間上の変化を適用するか (仕様 F-4 手順 3)。
    /// 本補正は standing 系の再定義であり raw 系は不変のため、理論上 seated は
    /// 変更不要のはずだが、仕様に従い既定で更新する。実機での挙動確認は M0 の検証項目。
    /// </param>
    public AppliedCorrection ApplyCorrection(CorrectionResult correction, bool updateSeated = true)
    {
        ArgumentNullException.ThrowIfNull(correction);

        var oldStanding = GetWorkingStandingZeroPose();
        var newStanding = correction.ApplyTo(oldStanding);
        SetWorkingStandingZeroPose(newStanding);

        RigidTransform? newSeated = null;
        if (updateSeated)
        {
            // raw 空間上の変化 Δ = newS2R ∘ oldS2R⁻¹ を seated にも適用する。
            var delta = RigidTransform.Compose(newStanding, oldStanding.Inverse());
            var oldSeated = GetWorkingSeatedZeroPose();
            newSeated = RigidTransform.Compose(delta, oldSeated);
            SetWorkingSeatedZeroPose(newSeated.Value);
        }

        // 境界頂点は standing 座標で保存されているため、標準規約 v' = C(v) で
        // 物理的な壁位置を維持する (仕様 F-5)。
        var oldQuads = GetWorkingCollisionBounds();
        var newQuads = new HmdQuad[oldQuads.Length];
        for (var i = 0; i < oldQuads.Length; i++)
        {
            newQuads[i] = TransformQuad(correction, oldQuads[i]);
        }

        if (newQuads.Length > 0)
        {
            SetWorkingCollisionBounds(newQuads);
        }

        return new AppliedCorrection(oldStanding, newStanding, newSeated, oldQuads.Length);
    }

    private static HmdQuad TransformQuad(CorrectionResult correction, HmdQuad quad)
        => new()
        {
            Corner0 = TransformCorner(correction, quad.Corner0),
            Corner1 = TransformCorner(correction, quad.Corner1),
            Corner2 = TransformCorner(correction, quad.Corner2),
            Corner3 = TransformCorner(correction, quad.Corner3),
        };

    private static HmdVector3 TransformCorner(CorrectionResult correction, HmdVector3 v)
    {
        var transformed = correction.StandingSpaceMap.TransformPoint(new System.Numerics.Vector3(v.X, v.Y, v.Z));
        return new HmdVector3 { X = transformed.X, Y = transformed.Y, Z = transformed.Z };
    }
}

/// <summary>working copy へ適用した補正の内容 (ログ・表示用)。</summary>
public sealed record AppliedCorrection(
    RigidTransform OldStandingToRaw,
    RigidTransform NewStandingToRaw,
    RigidTransform? NewSeatedToRaw,
    int TransformedBoundsQuadCount);
