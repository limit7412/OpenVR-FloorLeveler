using System.Numerics;

namespace FloorLeveler.Core;

/// <summary>
/// デバイス接地オフセットプロファイル (仕様 F-1)。
/// <see cref="ContactOffset"/> はデバイスローカル座標での接地点であり、
/// 記録したポーズに適用して床上の接地点を求める。
/// </summary>
public sealed record DeviceContactProfile(string Name, Vector3 ContactOffset)
{
    /// <summary>デバイスのポーズ (standing 空間) から床への接地点を算出する。</summary>
    public Vector3 ContactPoint(RigidTransform devicePose)
        => devicePose.TransformPoint(ContactOffset);
}

/// <summary>
/// 内蔵の既定プロファイル。
/// 既定値はいずれも暫定 (デバイス原点 = 接地点とみなす) であり、実機での計測 (M0)
/// を経て更新する。UI からユーザーが数値を上書きできることが仕様上の要件。
/// </summary>
public static class BuiltInDeviceProfiles
{
    public static DeviceContactProfile ViveTracker30 { get; } = new("Vive Tracker 3.0", Vector3.Zero);
    public static DeviceContactProfile TundraTracker { get; } = new("Tundra Tracker", Vector3.Zero);
    public static DeviceContactProfile IndexController { get; } = new("Index コントローラー", Vector3.Zero);
    public static DeviceContactProfile ViveWand { get; } = new("Vive ワンド", Vector3.Zero);

    public static IReadOnlyList<DeviceContactProfile> All { get; } =
        [ViveTracker30, TundraTracker, IndexController, ViveWand];
}
