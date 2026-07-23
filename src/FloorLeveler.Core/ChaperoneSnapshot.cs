using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>
/// Chaperone 設定のスナップショット (仕様 F-6)。OpenVR の型に依存しない
/// シリアライズ可能な表現で、standing / seated の S→R 行列、コリジョン境界頂点、
/// プレイエリアサイズを保持する。行列は行優先 3x4 (HmdMatrix34_t 相当) で格納する。
/// </summary>
/// <param name="StandingToRaw">standing → raw 変換 (3 行 × 4 列)。</param>
/// <param name="SeatedToRaw">seated → raw 変換 (3 行 × 4 列)。</param>
/// <param name="BoundsQuads">コリジョン境界。[壁][頂点 0..3][x,y,z]。境界なしは空配列。</param>
/// <param name="PlayAreaSize">プレイエリアの [X, Z] サイズ (メートル)。取得不能時は null。</param>
public sealed record ChaperoneSnapshot(
    float[][] StandingToRaw,
    float[][] SeatedToRaw,
    float[][][] BoundsQuads,
    float[]? PlayAreaSize)
{
    public RigidTransform Standing() => FromRows(StandingToRaw);

    public RigidTransform Seated() => FromRows(SeatedToRaw);

    /// <summary>境界頂点を <see cref="Vector3"/> の配列 (壁ごとに 4 頂点) として返す。</summary>
    public Vector3[][] BoundsAsVectors()
        => BoundsQuads
            .Select(quad => quad.Select(c => new Vector3(c[0], c[1], c[2])).ToArray())
            .ToArray();

    public static ChaperoneSnapshot Create(
        RigidTransform standing,
        RigidTransform seated,
        IReadOnlyList<Vector3[]> boundsQuads,
        (float X, float Z)? playAreaSize)
        => new(
            ToRows(standing),
            ToRows(seated),
            boundsQuads.Select(quad => quad.Select(v => new[] { v.X, v.Y, v.Z }).ToArray()).ToArray(),
            playAreaSize is var (x, z) ? [x, z] : null);

    public static float[][] ToRows(RigidTransform t)
    {
        var m = t.ToRowMajor3x4();
        return
        [
            [m[0, 0], m[0, 1], m[0, 2], m[0, 3]],
            [m[1, 0], m[1, 1], m[1, 2], m[1, 3]],
            [m[2, 0], m[2, 1], m[2, 2], m[2, 3]],
        ];
    }

    public static RigidTransform FromRows(float[][] rows)
    {
        if (rows is not { Length: 3 } || rows.Any(r => r is not { Length: 4 }))
        {
            throw new ArgumentException("3x4 行列が必要です。", nameof(rows));
        }

        var m = new float[3, 4];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 4; c++)
            {
                m[r, c] = rows[r][c];
            }
        }

        return RigidTransform.FromRowMajor3x4(m);
    }
}
