using FloorLeveler.Core;

namespace FloorLeveler.OpenVr;

/// <summary>
/// <see cref="HmdMatrix34"/> と Core の <see cref="RigidTransform"/> の相互変換。
/// OpenVR の型への依存はこの境界に閉じ込め、Core には持ち込まない (仕様 §7)。
/// </summary>
public static class HmdMatrixConversion
{
    public static RigidTransform ToRigidTransform(this HmdMatrix34 m)
        => RigidTransform.FromRowMajor3x4(new[,]
        {
            { m.M00, m.M01, m.M02, m.M03 },
            { m.M10, m.M11, m.M12, m.M13 },
            { m.M20, m.M21, m.M22, m.M23 },
        });

    public static HmdMatrix34 ToHmdMatrix34(this RigidTransform t)
    {
        var m = t.ToRowMajor3x4();
        return new HmdMatrix34
        {
            M00 = m[0, 0], M01 = m[0, 1], M02 = m[0, 2], M03 = m[0, 3],
            M10 = m[1, 0], M11 = m[1, 1], M12 = m[1, 2], M13 = m[1, 3],
            M20 = m[2, 0], M21 = m[2, 1], M22 = m[2, 2], M23 = m[2, 3],
        };
    }
}
