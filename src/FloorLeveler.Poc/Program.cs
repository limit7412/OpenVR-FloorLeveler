using System.Numerics;
using System.Text.Json;
using FloorLeveler.Core;
using FloorLeveler.OpenVr;

// M0 PoC コンソール (issue #1 §11)。
// 目的: OpenVR 接続、S→R 行列の読み書き、微小回転の適用と目視確認、符号規約の実機検証。
// SteamVR 起動中の環境で実行すること。openvr_api.dll を実行ファイルと同じ場所に置く。

return args switch
{
    ["status"] => Status(),
    ["tilt", .. var rest] => Tilt(rest),
    ["level", .. var rest] => Level(rest),
    ["backup", .. var rest] => Backup(rest),
    ["restore", .. var rest] => Restore(rest),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine(
        """
        FloorLeveler M0 PoC - verification tool for SteamVR Chaperone floor correction

        Usage:
          status                            Show connection, devices, S->R matrices and bounds
          tilt --roll <deg> [--commit]      Apply a small roll rotation to the standing space
                                            (to check the sign convention by eye)
          level [--commit]                  Apply the mode A (gravity align) correction
          backup [--file <path>]            Save the current working copy as a snapshot
          restore --file <path> [--commit]  Restore a snapshot into the working copy

        Without --commit, changes stay in the working copy and the preview only, and are
        reverted on exit instead of being written to the Live configuration.
        """);
    return 1;
}

static int Status()
{
    if (!OpenVrSession.IsRuntimeInstalled())
    {
        Console.WriteLine("SteamVR runtime not found.");
        return 1;
    }

    using var session = OpenVrSession.Connect();
    Console.WriteLine("Connected to SteamVR.");

    Console.WriteLine("\n[devices]");
    foreach (var d in session.System.ListConnectedDevices())
    {
        Console.WriteLine($"  #{d.Index,2} {d.DeviceClass,-18} {d.ModelNumber} ({d.SerialNumber})");
    }

    var standing = session.ChaperoneSetup.GetWorkingStandingZeroPose();
    var seated = session.ChaperoneSetup.GetWorkingSeatedZeroPose();
    Console.WriteLine("\n[standing -> raw]");
    PrintMatrix(standing);
    Console.WriteLine("[seated -> raw]");
    PrintMatrix(seated);

    var playArea = session.ChaperoneSetup.GetWorkingPlayAreaSize();
    var bounds = session.ChaperoneSetup.GetWorkingCollisionBounds();
    var area = playArea is var (x, z) ? $"{x:F2}m x {z:F2}m" : "unavailable";
    Console.WriteLine($"\nplay area: {area}, bounds quads: {bounds.Length}");

    var gravity = Correction.ComputeGravityAlign(standing);
    Console.WriteLine($"tilt from gravity: {gravity.RotationAngleDegrees:F3} deg (axis {Format(gravity.RotationAxis)})");
    return 0;
}

static int Tilt(string[] args)
{
    var roll = ReadFloatOption(args, "--roll");
    if (roll is null)
    {
        Console.WriteLine("Specify --roll <deg> (for example: tilt --roll 0.5)");
        return 1;
    }

    // standing 空間で Z 軸まわりのロール回転 (原点中心)。
    // 符号規約: C は旧→新 standing の写像なので、+roll では旧 standing の床が
    // 新 standing で +X 側ほど高く見える想定 — 実機で目視確認し結果を仕様へ追記する。
    var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, roll.Value * MathF.PI / 180f);
    var map = new RigidTransform(rotation, Vector3.Zero);
    var correction = new CorrectionResult(
        CorrectionMode.MeasuredFloor,
        map,
        Math.Abs(roll.Value),
        Vector3.UnitZ,
        0f,
        IsNegligible: false,
        // 大きい回転は Core と同じ閾値 (10° 超) で確認を要求する。
        RequiresConfirmation: Math.Abs(roll.Value) > Correction.ConfirmationRotationDegrees);

    return ApplyAndMaybeCommit(correction, args.Contains("--commit"));
}

static int Level(string[] args)
{
    using var session = OpenVrSession.Connect();
    var standing = session.ChaperoneSetup.GetWorkingStandingZeroPose();
    var correction = Correction.ComputeGravityAlign(standing);

    Console.WriteLine($"current tilt: {correction.RotationAngleDegrees:F3} deg (axis {Format(correction.RotationAxis)})");
    if (correction.IsNegligible)
    {
        Console.WriteLine("No correction needed (rotation under 0.05 deg and translation under 1 mm).");
        return 0;
    }

    return ApplyAndMaybeCommitWith(session, correction, args.Contains("--commit"));
}

static int ApplyAndMaybeCommit(CorrectionResult correction, bool commit)
{
    using var session = OpenVrSession.Connect();
    return ApplyAndMaybeCommitWith(session, correction, commit);
}

static int ApplyAndMaybeCommitWith(OpenVrSession session, CorrectionResult correction, bool commit)
{
    if (correction.RequiresConfirmation)
    {
        Console.Write($"Rotation is large ({correction.RotationAngleDegrees:F1} deg). Continue? [yes/no]: ");
        if (Console.ReadLine()?.Trim() != "yes")
        {
            Console.WriteLine("Aborted.");
            return 1;
        }
    }

    var chaperone = session.ChaperoneSetup;
    try
    {
        var applied = chaperone.ApplyCorrection(correction);
        Console.WriteLine("[standing -> raw, before]");
        PrintMatrix(applied.OldStandingToRaw);
        Console.WriteLine("[standing -> raw, after]");
        PrintMatrix(applied.NewStandingToRaw);
        Console.WriteLine($"Transformed {applied.TransformedBoundsQuadCount} bounds quad(s) to match the correction.");

        if (commit)
        {
            if (!chaperone.Commit())
            {
                chaperone.Revert();
                Console.WriteLine("CommitWorkingCopy failed; reverted.");
                return 1;
            }

            Console.WriteLine("Committed to the Live configuration. Check the floor in SteamVR.");
            return 0;
        }

        chaperone.ShowWorkingSetPreview();
        Console.WriteLine("Applied to the working copy and showing the preview (not committed).");
        Console.Write("Press Enter to revert and exit...");
        Console.ReadLine();
        chaperone.HideWorkingSetPreview();
        chaperone.Revert();
        Console.WriteLine("Reverted.");
        return 0;
    }
    catch
    {
        // 途中失敗時は中途半端な working copy を残さない (仕様 NF-5)。
        chaperone.Revert();
        throw;
    }
}

static int Backup(string[] args)
{
    var path = ReadStringOption(args, "--file") ?? DefaultBackupPath();
    using var session = OpenVrSession.Connect();

    var snapshot = ChaperoneSnapshot.Capture(session.ChaperoneSetup);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions()));
    Console.WriteLine($"Snapshot saved: {path}");
    return 0;
}

static int Restore(string[] args)
{
    var path = ReadStringOption(args, "--file");
    if (path is null || !File.Exists(path))
    {
        Console.WriteLine("Specify an existing snapshot with --file <path>.");
        return 1;
    }

    var snapshot = JsonSerializer.Deserialize<ChaperoneSnapshot>(File.ReadAllText(path), JsonOptions());
    if (snapshot is null)
    {
        Console.WriteLine("Could not read the snapshot.");
        return 1;
    }

    using var session = OpenVrSession.Connect();
    var chaperone = session.ChaperoneSetup;
    try
    {
        snapshot.Apply(chaperone);
        if (args.Contains("--commit"))
        {
            if (!chaperone.Commit())
            {
                chaperone.Revert();
                Console.WriteLine("CommitWorkingCopy failed; reverted.");
                return 1;
            }

            Console.WriteLine("Restored the snapshot to the Live configuration.");
            return 0;
        }

        // --commit なしは他コマンドと同様プレビューのみで、未コミットの復元内容を
        // working copy に残さない。
        chaperone.ShowWorkingSetPreview();
        Console.WriteLine("Restored into the working copy and showing the preview (--commit writes to Live).");
        Console.Write("Press Enter to revert and exit...");
        Console.ReadLine();
        chaperone.HideWorkingSetPreview();
        chaperone.Revert();
        Console.WriteLine("Reverted.");
        return 0;
    }
    catch
    {
        chaperone.Revert();
        throw;
    }
}

static string DefaultBackupPath()
    => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FloorLeveler", "backups",
        $"{DateTime.Now:yyyyMMdd-HHmmss}.json");

static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

static void PrintMatrix(RigidTransform t)
{
    var m = t.ToRowMajor3x4();
    for (var r = 0; r < 3; r++)
    {
        Console.WriteLine($"  [{m[r, 0],9:F5} {m[r, 1],9:F5} {m[r, 2],9:F5} | {m[r, 3],9:F5}]");
    }
}

static string Format(Vector3 v) => $"({v.X:F3}, {v.Y:F3}, {v.Z:F3})";

static float? ReadFloatOption(string[] args, string name)
{
    // NaN / Infinity は TryParse を通るが、無効な行列の commit につながるため拒否する。
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length
        && float.TryParse(args[i + 1], out var value)
        && float.IsFinite(value)
        ? value
        : null;
}

static string? ReadStringOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>Chaperone 設定のスナップショット (仕様 F-6 の下地)。</summary>
/// <param name="PlayAreaSize">プレイエリアの X/Z サイズ (メートル)。取得できなかった場合は null。</param>
sealed record ChaperoneSnapshot(
    float[][] StandingToRaw,
    float[][] SeatedToRaw,
    float[][][] BoundsQuads,
    float[]? PlayAreaSize = null)
{
    public static ChaperoneSnapshot Capture(ChaperoneTuner chaperone)
        => new(
            ToJagged(chaperone.GetWorkingStandingZeroPose()),
            ToJagged(chaperone.GetWorkingSeatedZeroPose()),
            chaperone.GetWorkingCollisionBounds()
                .Select(q => new[]
                {
                    ToArray(q.Corner0), ToArray(q.Corner1), ToArray(q.Corner2), ToArray(q.Corner3),
                })
                .ToArray(),
            chaperone.GetWorkingPlayAreaSize() is var (x, z) ? [x, z] : null);

    public void Apply(ChaperoneTuner chaperone)
    {
        chaperone.SetWorkingStandingZeroPose(FromJagged(StandingToRaw));
        chaperone.SetWorkingSeatedZeroPose(FromJagged(SeatedToRaw));

        // 空 (境界未設定) も正当な状態として保存されているため、
        // 0 件でも setter に渡して既存の境界をクリアする。
        chaperone.SetWorkingCollisionBounds(BoundsQuads
            .Select(q => new HmdQuad
            {
                Corner0 = ToVector(q[0]),
                Corner1 = ToVector(q[1]),
                Corner2 = ToVector(q[2]),
                Corner3 = ToVector(q[3]),
            })
            .ToArray());

        if (PlayAreaSize is [var sizeX, var sizeZ])
        {
            chaperone.SetWorkingPlayAreaSize(sizeX, sizeZ);
        }
    }

    private static float[][] ToJagged(RigidTransform t)
    {
        var m = t.ToRowMajor3x4();
        return
        [
            [m[0, 0], m[0, 1], m[0, 2], m[0, 3]],
            [m[1, 0], m[1, 1], m[1, 2], m[1, 3]],
            [m[2, 0], m[2, 1], m[2, 2], m[2, 3]],
        ];
    }

    private static RigidTransform FromJagged(float[][] rows)
    {
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

    private static float[] ToArray(HmdVector3 v) => [v.X, v.Y, v.Z];

    private static HmdVector3 ToVector(float[] a) => new() { X = a[0], Y = a[1], Z = a[2] };
}
