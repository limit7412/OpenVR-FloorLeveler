using System.Numerics;
using System.Runtime.InteropServices;
using FloorLeveler.Core;
using FloorLeveler.OpenVr;

namespace FloorLeveler.OpenVr.Tests;

/// <summary>
/// ネイティブ構造体レイアウトと行列変換の検証。
/// FnTable の呼び出し自体は SteamVR 実機が必要なため M0 の手動検証項目とし、
/// ここではサイズ・フィールド数・変換の純粋部分のみを検証する。
/// </summary>
public class InteropLayoutTests
{
    [Fact]
    public void NativeStructSizes_MatchOpenVrHeader()
    {
        // openvr.h: HmdMatrix34_t = float[3][4] = 48 bytes
        Assert.Equal(48, Marshal.SizeOf<HmdMatrix34>());
        Assert.Equal(12, Marshal.SizeOf<HmdVector3>());
        Assert.Equal(8, Marshal.SizeOf<HmdVector2>());
        // HmdQuad_t = HmdVector3_t[4] = 48 bytes
        Assert.Equal(48, Marshal.SizeOf<HmdQuad>());
        // TrackedDevicePose_t = 48 + 12 + 12 + 4 (enum) + 1 + 1 → pack(4) で 80 bytes
        Assert.Equal(80, Marshal.SizeOf<TrackedDevicePose>());
    }

    [Fact]
    public void FnTableFieldCounts_MatchInterfaceVersions()
    {
        // IVRSystem_023: 47 メソッド / IVRChaperoneSetup_006: 20 メソッド
        Assert.Equal(47, typeof(VRSystemFnTable).GetFields().Length);
        Assert.Equal(20, typeof(VRChaperoneSetupFnTable).GetFields().Length);
        Assert.All(typeof(VRSystemFnTable).GetFields(), f => Assert.Equal(typeof(IntPtr), f.FieldType));
        Assert.All(typeof(VRChaperoneSetupFnTable).GetFields(), f => Assert.Equal(typeof(IntPtr), f.FieldType));
    }

    [Fact]
    public void HmdMatrix34_RoundTripsThroughRigidTransform()
    {
        var original = new RigidTransform(
            Quaternion.CreateFromYawPitchRoll(0.4f, -0.15f, 0.23f),
            new Vector3(0.5f, 1.6f, -0.9f));

        var restored = original.ToHmdMatrix34().ToRigidTransform();

        var p = new Vector3(1f, 2f, 3f);
        Assert.True((original.TransformPoint(p) - restored.TransformPoint(p)).Length() < 1e-5f);
    }

    [Fact]
    public void HmdMatrix34_TranslationIsInFourthColumn()
    {
        var t = RigidTransform.CreateTranslation(new Vector3(1f, 2f, 3f));

        var m = t.ToHmdMatrix34();

        Assert.Equal(1f, m.M03);
        Assert.Equal(2f, m.M13);
        Assert.Equal(3f, m.M23);
        // 回転部分は単位行列
        Assert.Equal(1f, m.M00, 5);
        Assert.Equal(1f, m.M11, 5);
        Assert.Equal(1f, m.M22, 5);
    }

    [Fact]
    public void ChaperoneSnapshotConversion_PreservesIdentityLayout()
    {
        // HmdMatrix34 と RigidTransform の変換で列ベクトル規約 (第4列=並進) が保たれること。
        var identity = RigidTransform.Identity.ToHmdMatrix34();

        Assert.Equal(1f, identity.M00);
        Assert.Equal(0f, identity.M01);
        Assert.Equal(0f, identity.M03);
        Assert.Equal(1f, identity.M11);
        Assert.Equal(1f, identity.M22);
    }
}
