using System.Numerics;
using FloorLeveler.Core;

namespace FloorLeveler.OpenVr;

/// <summary>
/// <see cref="ChaperoneTuner"/> の working copy と <see cref="ChaperoneSnapshot"/> の
/// 相互変換 (仕様 F-6)。HmdQuad ↔ Vector3 の変換をこの境界に閉じ込める。
/// </summary>
public static class ChaperoneSnapshotIo
{
    /// <summary>現在の working copy からスナップショットを取得する。</summary>
    public static ChaperoneSnapshot Capture(ChaperoneTuner chaperone)
    {
        ArgumentNullException.ThrowIfNull(chaperone);

        var bounds = chaperone.GetWorkingCollisionBounds()
            .Select(q => new[]
            {
                ToVector(q.Corner0),
                ToVector(q.Corner1),
                ToVector(q.Corner2),
                ToVector(q.Corner3),
            })
            .ToArray();

        return ChaperoneSnapshot.Create(
            chaperone.GetWorkingStandingZeroPose(),
            chaperone.GetWorkingSeatedZeroPose(),
            bounds,
            chaperone.GetWorkingPlayAreaSize());
    }

    /// <summary>
    /// スナップショットを working copy へ書き戻す (commit はしない)。
    /// 境界は空でも書き戻して既存境界をクリアする。
    /// </summary>
    public static void Restore(ChaperoneTuner chaperone, ChaperoneSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(chaperone);
        ArgumentNullException.ThrowIfNull(snapshot);

        // 形状不正なスナップショットを working copy へ部分的に書き込まないよう、
        // 書き込み前に検証する。
        snapshot.Validate();

        chaperone.SetWorkingStandingZeroPose(snapshot.Standing());
        chaperone.SetWorkingSeatedZeroPose(snapshot.Seated());

        var quads = snapshot.BoundsAsVectors()
            .Select(q => new HmdQuad
            {
                Corner0 = ToHmd(q[0]),
                Corner1 = ToHmd(q[1]),
                Corner2 = ToHmd(q[2]),
                Corner3 = ToHmd(q[3]),
            })
            .ToArray();
        chaperone.SetWorkingCollisionBounds(quads);

        if (snapshot.PlayAreaSize is [var sizeX, var sizeZ])
        {
            chaperone.SetWorkingPlayAreaSize(sizeX, sizeZ);
        }
    }

    private static Vector3 ToVector(HmdVector3 v) => new(v.X, v.Y, v.Z);

    private static HmdVector3 ToHmd(Vector3 v) => new() { X = v.X, Y = v.Y, Z = v.Z };
}
