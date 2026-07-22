using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>
/// 剛体変換 (回転 + 並進)。OpenVR と同じ列ベクトル規約 (p' = R * p + t) で定義する。
/// OpenVR の <c>HmdMatrix34_t</c> (行優先 3x4) との相互変換は
/// <see cref="FromRowMajor3x4"/> / <see cref="ToRowMajor3x4"/> で行い、
/// Core 内部では OpenVR の型に依存しない。
/// </summary>
public readonly struct RigidTransform : IEquatable<RigidTransform>
{
    public Quaternion Rotation { get; }
    public Vector3 Translation { get; }

    public RigidTransform(Quaternion rotation, Vector3 translation)
    {
        Rotation = Quaternion.Normalize(rotation);
        Translation = translation;
    }

    public static RigidTransform Identity { get; } = new(Quaternion.Identity, Vector3.Zero);

    /// <summary>点を変換する: p' = R * p + t</summary>
    public Vector3 TransformPoint(Vector3 point)
        => Vector3.Transform(point, Rotation) + Translation;

    /// <summary>方向ベクトルを変換する (並進は適用しない)。</summary>
    public Vector3 TransformVector(Vector3 vector)
        => Vector3.Transform(vector, Rotation);

    /// <summary>逆変換: p = R⁻¹ * (p' - t)</summary>
    public RigidTransform Inverse()
    {
        var invRot = Quaternion.Conjugate(Rotation);
        return new RigidTransform(invRot, Vector3.Transform(-Translation, invRot));
    }

    /// <summary>
    /// 合成。結果は result(p) = outer(inner(p)) となる (inner を先に適用)。
    /// </summary>
    public static RigidTransform Compose(RigidTransform outer, RigidTransform inner)
        => new(
            Quaternion.Normalize(outer.Rotation * inner.Rotation),
            outer.TransformPoint(inner.Translation));

    /// <summary>指定点を中心とする回転: p' = q * (p - center) + center</summary>
    public static RigidTransform CreateRotationAboutPoint(Quaternion rotation, Vector3 center)
        => new(rotation, center - Vector3.Transform(center, rotation));

    public static RigidTransform CreateTranslation(Vector3 translation)
        => new(Quaternion.Identity, translation);

    /// <summary>
    /// 単位ベクトル from を単位ベクトル to に写す最小回転を返す。
    /// 平行 (回転不要) と反平行 (180° 回転) を含めて安定に扱う。
    /// </summary>
    public static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from);
        to = Vector3.Normalize(to);

        var dot = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (dot > 1f - 1e-7f)
        {
            return Quaternion.Identity;
        }

        if (dot < -1f + 1e-7f)
        {
            // 反平行: from に直交する任意の軸で 180° 回転する。
            var axis = Vector3.Cross(from, Vector3.UnitX);
            if (axis.LengthSquared() < 1e-10f)
            {
                axis = Vector3.Cross(from, Vector3.UnitY);
            }

            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }

        var rotationAxis = Vector3.Normalize(Vector3.Cross(from, to));
        var angle = MathF.Acos(dot);
        return Quaternion.CreateFromAxisAngle(rotationAxis, angle);
    }

    /// <summary>
    /// 行優先 3x4 行列 (HmdMatrix34_t 相当、m[row, col]、第 4 列が並進) から生成する。
    /// </summary>
    public static RigidTransform FromRowMajor3x4(float[,] m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.GetLength(0) != 3 || m.GetLength(1) != 4)
        {
            throw new ArgumentException("3x4 行列が必要です。", nameof(m));
        }

        // System.Numerics.Matrix4x4 は行ベクトル規約 (v' = v * M) のため、
        // 列ベクトル規約の回転行列 R は転置して格納する。
        var net = new Matrix4x4(
            m[0, 0], m[1, 0], m[2, 0], 0f,
            m[0, 1], m[1, 1], m[2, 1], 0f,
            m[0, 2], m[1, 2], m[2, 2], 0f,
            0f, 0f, 0f, 1f);

        var rotation = Quaternion.CreateFromRotationMatrix(net);
        var translation = new Vector3(m[0, 3], m[1, 3], m[2, 3]);
        return new RigidTransform(rotation, translation);
    }

    /// <summary>行優先 3x4 行列 (HmdMatrix34_t 相当) へ変換する。</summary>
    public float[,] ToRowMajor3x4()
    {
        var net = Matrix4x4.CreateFromQuaternion(Rotation);
        return new[,]
        {
            { net.M11, net.M21, net.M31, Translation.X },
            { net.M12, net.M22, net.M32, Translation.Y },
            { net.M13, net.M23, net.M33, Translation.Z },
        };
    }

    public bool Equals(RigidTransform other)
        => Rotation.Equals(other.Rotation) && Translation.Equals(other.Translation);

    public override bool Equals(object? obj) => obj is RigidTransform other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Rotation, Translation);

    public override string ToString() => $"R={Rotation} t={Translation}";
}
