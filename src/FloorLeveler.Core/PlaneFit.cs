using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>床平面の推定結果 (仕様 F-2)。座標系は standing 空間。</summary>
/// <param name="Normal">単位法線。<c>Normal.Y &gt; 0</c> に正規化済み。</param>
/// <param name="Centroid">点群の重心 (平面上の代表点)。</param>
/// <param name="TiltAngleDegrees">standing の up (0,1,0) からの傾き角 (度)。</param>
/// <param name="TiltAzimuthDegrees">
/// 傾きの方位 (度)。床が低くなる方向 (下り方向) の XZ 平面上の向きを
/// atan2(x, z) で表す (+Z 方向が 0°、+X 方向が 90°)。傾きが無い場合は 0。
/// </param>
/// <param name="RmsResidual">平面からの符号なし距離の RMS (メートル)。</param>
/// <param name="MaxResidual">平面からの符号なし距離の最大値 (メートル)。</param>
public sealed record PlaneFitResult(
    Vector3 Normal,
    Vector3 Centroid,
    float TiltAngleDegrees,
    float TiltAzimuthDegrees,
    float RmsResidual,
    float MaxResidual,
    int PointCount);

/// <summary>PCA (主成分分析) による最小二乗平面フィット (仕様 F-2)。</summary>
public static class PlaneFit
{
    public const int MinimumPoints = 3;

    /// <summary>
    /// 点群に最小二乗平面をフィットする。重心を差し引いた点群の 3x3 共分散行列を
    /// 固有値分解し、最小固有値に対応する固有ベクトルを法線とする。
    /// </summary>
    /// <exception cref="ArgumentException">点数が 3 未満、または点群が退化 (一直線上) している場合。</exception>
    public static PlaneFitResult Fit(IReadOnlyList<Vector3> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count < MinimumPoints)
        {
            throw new ArgumentException($"平面フィットには {MinimumPoints} 点以上が必要です。", nameof(points));
        }

        // 精度確保のため共分散と固有値分解は double で行う。
        double cx = 0, cy = 0, cz = 0;
        foreach (var p in points)
        {
            cx += p.X;
            cy += p.Y;
            cz += p.Z;
        }

        var n = points.Count;
        cx /= n;
        cy /= n;
        cz /= n;

        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            var dx = p.X - cx;
            var dy = p.Y - cy;
            var dz = p.Z - cz;
            xx += dx * dx;
            xy += dx * dy;
            xz += dx * dz;
            yy += dy * dy;
            yz += dy * dz;
            zz += dz * dz;
        }

        var cov = new double[3, 3]
        {
            { xx, xy, xz },
            { xy, yy, yz },
            { xz, yz, zz },
        };

        var (eigenValues, eigenVectors) = SymmetricEigen3x3.Decompose(cov);

        // 最小固有値に対応する固有ベクトルが法線。
        var minIndex = 0;
        if (eigenValues[1] < eigenValues[minIndex]) minIndex = 1;
        if (eigenValues[2] < eigenValues[minIndex]) minIndex = 2;

        // 残り 2 つの固有値が共に小さい場合、点群は一直線上にあり平面が定まらない。
        var midValue = eigenValues.Where((_, i) => i != minIndex).Min();
        if (midValue <= 1e-12)
        {
            throw new ArgumentException("点群が退化しており平面を決定できません。", nameof(points));
        }

        var normal = new Vector3(
            (float)eigenVectors[0, minIndex],
            (float)eigenVectors[1, minIndex],
            (float)eigenVectors[2, minIndex]);
        normal = Vector3.Normalize(normal);
        if (normal.Y < 0)
        {
            normal = -normal;
        }

        var centroid = new Vector3((float)cx, (float)cy, (float)cz);

        double sumSq = 0, max = 0;
        foreach (var p in points)
        {
            var d = Math.Abs(Vector3.Dot(p - centroid, normal));
            sumSq += (double)d * d;
            max = Math.Max(max, d);
        }

        var rms = (float)Math.Sqrt(sumSq / n);

        var tiltRadians = MathF.Acos(Math.Clamp(normal.Y, -1f, 1f));
        var tiltDegrees = tiltRadians * (180f / MathF.PI);

        // 下り方向 = 法線の水平成分の向き (法線が +X に倒れていれば +X 側が低い)。
        var horizontal = new Vector2(normal.X, normal.Z);
        var azimuth = horizontal.LengthSquared() < 1e-12f
            ? 0f
            : MathF.Atan2(normal.X, normal.Z) * (180f / MathF.PI);

        return new PlaneFitResult(normal, centroid, tiltDegrees, azimuth, rms, (float)max, n);
    }
}

/// <summary>対称 3x3 行列のヤコビ法による固有値分解。</summary>
internal static class SymmetricEigen3x3
{
    /// <returns>固有値 (順不同) と、列が対応する固有ベクトルの 3x3 行列。</returns>
    public static (double[] Values, double[,] Vectors) Decompose(double[,] a)
    {
        var m = (double[,])a.Clone();
        var v = new double[3, 3];
        for (var i = 0; i < 3; i++)
        {
            v[i, i] = 1.0;
        }

        for (var sweep = 0; sweep < 64; sweep++)
        {
            var off = Math.Abs(m[0, 1]) + Math.Abs(m[0, 2]) + Math.Abs(m[1, 2]);
            if (off < 1e-15)
            {
                break;
            }

            for (var p = 0; p < 2; p++)
            {
                for (var q = p + 1; q < 3; q++)
                {
                    Rotate(m, v, p, q);
                }
            }
        }

        return (new[] { m[0, 0], m[1, 1], m[2, 2] }, v);
    }

    private static void Rotate(double[,] m, double[,] v, int p, int q)
    {
        if (Math.Abs(m[p, q]) < 1e-18)
        {
            return;
        }

        var theta = (m[q, q] - m[p, p]) / (2.0 * m[p, q]);
        var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
        if (theta == 0.0)
        {
            t = 1.0;
        }

        var c = 1.0 / Math.Sqrt(t * t + 1.0);
        var s = t * c;

        for (var k = 0; k < 3; k++)
        {
            var mkp = m[k, p];
            var mkq = m[k, q];
            m[k, p] = c * mkp - s * mkq;
            m[k, q] = s * mkp + c * mkq;
        }

        for (var k = 0; k < 3; k++)
        {
            var mpk = m[p, k];
            var mqk = m[q, k];
            m[p, k] = c * mpk - s * mqk;
            m[q, k] = s * mpk + c * mqk;
        }

        for (var k = 0; k < 3; k++)
        {
            var vkp = v[k, p];
            var vkq = v[k, q];
            v[k, p] = c * vkp - s * vkq;
            v[k, q] = s * vkp + c * vkq;
        }
    }
}
