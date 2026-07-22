using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>補正モード (仕様 F-3)。</summary>
public enum CorrectionMode
{
    /// <summary>モード A — 重力水平化: 仮想床を raw tracking の up 方向に戻す。</summary>
    GravityAlign,

    /// <summary>モード B — 実測床面合わせ: 仮想床を実測した物理床平面に一致させる。</summary>
    MeasuredFloor,
}

/// <summary>
/// 算出された補正。
/// <see cref="StandingSpaceMap"/> は「旧 standing 座標 → 新 standing 座標」の写像 C であり、
/// 以下の規約で各データへ適用する (符号・合成順序の規約はここに集約する):
/// <list type="bullet">
/// <item>S→R 行列: newS2R = oldS2R ∘ C⁻¹ (<see cref="ApplyTo"/>)。
/// raw 空間に固定された物理点 p_raw = oldS2R(p) は newS2R⁻¹(p_raw) = C(p) となり、
/// 旧 standing 座標 p の物体は新 standing 座標 C(p) に現れる。</item>
/// <item>Chaperone 境界頂点 (standing 座標で保存): v' = C(v) (仕様 F-5)。
/// 物理的な壁位置 (raw 座標) を維持したまま新 standing 座標へ写す。</item>
/// </list>
/// </summary>
/// <param name="Mode">算出に用いたモード。</param>
/// <param name="StandingSpaceMap">旧 standing 座標 → 新 standing 座標の剛体変換 C。</param>
/// <param name="RotationAngleDegrees">回転量 (度、非負)。</param>
/// <param name="RotationAxis">回転軸 (standing 空間の単位ベクトル)。回転が微小な場合は Zero。</param>
/// <param name="HeightChangeMeters">基準点 (床面合わせでは点群重心) における高さ変化 (メートル)。</param>
/// <param name="IsNegligible">補正不要 (回転 0.05° 未満かつ並進 1 mm 未満) かどうか。</param>
/// <param name="RequiresConfirmation">回転が 10° を超えており適用前に確認を要するかどうか。</param>
public sealed record CorrectionResult(
    CorrectionMode Mode,
    RigidTransform StandingSpaceMap,
    float RotationAngleDegrees,
    Vector3 RotationAxis,
    float HeightChangeMeters,
    bool IsNegligible,
    bool RequiresConfirmation)
{
    /// <summary>補正を standing → raw 行列へ合成する: newS2R = oldS2R ∘ C⁻¹。</summary>
    public RigidTransform ApplyTo(RigidTransform standingToRaw)
        => RigidTransform.Compose(standingToRaw, StandingSpaceMap.Inverse());
}

/// <summary>補正変換の算出 (仕様 F-3)。純粋関数のみで構成する。</summary>
public static class Correction
{
    /// <summary>これ未満の回転は「補正不要」とみなす (度)。</summary>
    public const float NegligibleRotationDegrees = 0.05f;

    /// <summary>これ未満の並進は「補正不要」とみなす (メートル)。</summary>
    public const float NegligibleTranslationMeters = 0.001f;

    /// <summary>これを超える回転は誤サンプリングの可能性が高く確認を要する (度)。</summary>
    public const float ConfirmationRotationDegrees = 10f;

    /// <summary>
    /// モード A — 重力水平化。standing の up 軸が raw の up 軸 (重力方向) と一致するよう
    /// 回転補正を算出する。回転中心は standing 原点。床サンプリング結果
    /// (<paramref name="floor"/>) があれば、その重心が Y=0 になる高さ合わせも行う。
    /// </summary>
    /// <param name="standingToRaw">現在の standing → raw 変換 (S→R 行列)。</param>
    /// <param name="floor">任意。実測床面の推定結果 (高さ合わせに利用)。</param>
    public static CorrectionResult ComputeGravityAlign(RigidTransform standingToRaw, PlaneFitResult? floor = null)
    {
        // raw の up (重力方向とほぼ整列) を standing 座標で表すと、
        // 傾きが無ければ (0,1,0) に一致する。これを standing up へ写す回転が補正。
        var gravityUpInStanding = standingToRaw.Inverse().TransformVector(Vector3.UnitY);
        var rotation = RigidTransform.FromToRotation(gravityUpInStanding, Vector3.UnitY);

        var map = new RigidTransform(rotation, Vector3.Zero); // 回転中心 = standing 原点

        var heightChange = 0f;
        if (floor is not null)
        {
            // 回転後の床重心が Y=0 になるよう並進する。
            heightChange = -map.TransformPoint(floor.Centroid).Y;
            map = RigidTransform.Compose(RigidTransform.CreateTranslation(new Vector3(0f, heightChange, 0f)), map);
        }

        return Build(CorrectionMode.GravityAlign, map, heightChange);
    }

    /// <summary>
    /// モード B — 実測床面合わせ。推定法線 n を standing の up (0,1,0) に写す回転を
    /// 点群重心を中心に適用し、さらに重心の Y 座標が 0 になるよう並進する。
    /// </summary>
    public static CorrectionResult ComputeFloorAlign(PlaneFitResult floor)
    {
        ArgumentNullException.ThrowIfNull(floor);

        var rotation = RigidTransform.FromToRotation(floor.Normal, Vector3.UnitY);
        var rotate = RigidTransform.CreateRotationAboutPoint(rotation, floor.Centroid);

        // 重心は回転中心なので回転では動かない。Y=0 へ下ろす並進を加える。
        var heightChange = -floor.Centroid.Y;
        var map = RigidTransform.Compose(
            RigidTransform.CreateTranslation(new Vector3(0f, heightChange, 0f)),
            rotate);

        return Build(CorrectionMode.MeasuredFloor, map, heightChange);
    }

    private static CorrectionResult Build(CorrectionMode mode, RigidTransform map, float heightChange)
    {
        var (axis, angleRadians) = ToAxisAngle(map.Rotation);
        var angleDegrees = angleRadians * (180f / MathF.PI);

        var isNegligible = angleDegrees < NegligibleRotationDegrees
            && Math.Abs(heightChange) < NegligibleTranslationMeters;
        var requiresConfirmation = angleDegrees > ConfirmationRotationDegrees;

        return new CorrectionResult(mode, map, angleDegrees, axis, heightChange, isNegligible, requiresConfirmation);
    }

    private static (Vector3 Axis, float AngleRadians) ToAxisAngle(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        if (q.W < 0f)
        {
            q = -q; // 最短回転で表す
        }

        var angle = 2f * MathF.Acos(Math.Clamp(q.W, -1f, 1f));
        var s = MathF.Sqrt(Math.Max(0f, 1f - q.W * q.W));
        if (s < 1e-6f)
        {
            return (Vector3.Zero, angle);
        }

        return (new Vector3(q.X / s, q.Y / s, q.Z / s), angle);
    }
}

/// <summary>Chaperone 境界頂点の整合維持 (仕様 F-5)。</summary>
public static class BoundaryTransform
{
    /// <summary>
    /// standing 座標で保存された境界頂点群に補正の standing 空間写像 C を適用し、
    /// 物理的な壁位置 (raw 座標) を維持した新 standing 座標の頂点群を返す。
    /// </summary>
    public static Vector3[] Transform(CorrectionResult correction, IReadOnlyList<Vector3> vertices)
    {
        ArgumentNullException.ThrowIfNull(correction);
        ArgumentNullException.ThrowIfNull(vertices);

        var result = new Vector3[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            result[i] = correction.StandingSpaceMap.TransformPoint(vertices[i]);
        }

        return result;
    }
}
