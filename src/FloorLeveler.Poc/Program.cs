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
        FloorLeveler M0 PoC — SteamVR Chaperone 床補正の検証ツール

        使い方:
          status                     接続状態・デバイス一覧・S→R 行列・境界情報を表示
          tilt --roll <度> [--commit]   standing 空間に微小ロール回転を適用 (符号規約の目視確認用)
          level [--commit]           モード A (重力水平化) の補正を適用
          backup [--file <パス>]      現在の working copy をスナップショット保存
          restore --file <パス> [--commit]  スナップショットを working copy へ復元

        --commit を付けない場合、変更は working copy とプレビュー表示のみで、
        終了時に revert して Live 設定には反映しない (仕様 NF-5)。
        """);
    return 1;
}

static int Status()
{
    if (!OpenVrSession.IsRuntimeInstalled())
    {
        Console.WriteLine("SteamVR ランタイムが見つかりません。");
        return 1;
    }

    using var session = OpenVrSession.Connect();
    Console.WriteLine("SteamVR に接続しました。");

    Console.WriteLine("\n[デバイス]");
    foreach (var d in session.System.ListConnectedDevices())
    {
        Console.WriteLine($"  #{d.Index,2} {d.DeviceClass,-18} {d.ModelNumber} ({d.SerialNumber})");
    }

    var standing = session.ChaperoneSetup.GetWorkingStandingZeroPose();
    var seated = session.ChaperoneSetup.GetWorkingSeatedZeroPose();
    Console.WriteLine("\n[standing → raw]");
    PrintMatrix(standing);
    Console.WriteLine("[seated → raw]");
    PrintMatrix(seated);

    var playArea = session.ChaperoneSetup.GetWorkingPlayAreaSize();
    var bounds = session.ChaperoneSetup.GetWorkingCollisionBounds();
    Console.WriteLine($"\nプレイエリア: {(playArea is var (x, z) ? $"{x:F2}m x {z:F2}m" : "取得失敗")}, 境界 quad 数: {bounds.Length}");

    var gravity = Correction.ComputeGravityAlign(standing);
    Console.WriteLine($"重力水平からの傾き: {gravity.RotationAngleDegrees:F3}° (軸 {Format(gravity.RotationAxis)})");
    return 0;
}

static int Tilt(string[] args)
{
    var roll = ReadFloatOption(args, "--roll");
    if (roll is null)
    {
        Console.WriteLine("--roll <度> を指定してください (例: tilt --roll 0.5)");
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
        RequiresConfirmation: false);

    return ApplyAndMaybeCommit(correction, args.Contains("--commit"));
}

static int Level(string[] args)
{
    using var session = OpenVrSession.Connect();
    var standing = session.ChaperoneSetup.GetWorkingStandingZeroPose();
    var correction = Correction.ComputeGravityAlign(standing);

    Console.WriteLine($"現在の傾き: {correction.RotationAngleDegrees:F3}° (軸 {Format(correction.RotationAxis)})");
    if (correction.IsNegligible)
    {
        Console.WriteLine("補正不要です (回転 0.05° 未満かつ並進 1 mm 未満)。");
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
        Console.Write($"回転が {correction.RotationAngleDegrees:F1}° と大きいです。続行しますか? [yes/no]: ");
        if (Console.ReadLine()?.Trim() != "yes")
        {
            Console.WriteLine("中止しました。");
            return 1;
        }
    }

    var chaperone = session.ChaperoneSetup;
    try
    {
        var applied = chaperone.ApplyCorrection(correction);
        Console.WriteLine("[適用前 standing → raw]");
        PrintMatrix(applied.OldStandingToRaw);
        Console.WriteLine("[適用後 standing → raw]");
        PrintMatrix(applied.NewStandingToRaw);
        Console.WriteLine($"境界 quad {applied.TransformedBoundsQuadCount} 枚を補正に合わせて変換しました。");

        if (commit)
        {
            if (!chaperone.Commit())
            {
                chaperone.Revert();
                Console.WriteLine("CommitWorkingCopy が失敗したため revert しました。");
                return 1;
            }

            Console.WriteLine("Live 設定へ commit しました。SteamVR 上で床の変化を確認してください。");
            return 0;
        }

        chaperone.ShowWorkingSetPreview();
        Console.WriteLine("working copy に適用しプレビューを表示中です (commit はしていません)。");
        Console.Write("Enter で revert して終了します...");
        Console.ReadLine();
        chaperone.HideWorkingSetPreview();
        chaperone.Revert();
        Console.WriteLine("revert しました。");
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
    Console.WriteLine($"スナップショットを保存しました: {path}");
    return 0;
}

static int Restore(string[] args)
{
    var path = ReadStringOption(args, "--file");
    if (path is null || !File.Exists(path))
    {
        Console.WriteLine("--file <パス> に存在するスナップショットを指定してください。");
        return 1;
    }

    var snapshot = JsonSerializer.Deserialize<ChaperoneSnapshot>(File.ReadAllText(path), JsonOptions());
    if (snapshot is null)
    {
        Console.WriteLine("スナップショットの読み込みに失敗しました。");
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
                Console.WriteLine("CommitWorkingCopy が失敗したため revert しました。");
                return 1;
            }

            Console.WriteLine("スナップショットを Live 設定へ復元しました。");
        }
        else
        {
            Console.WriteLine("working copy へ復元しました (--commit で Live に反映)。");
        }

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
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && float.TryParse(args[i + 1], out var value) ? value : null;
}

static string? ReadStringOption(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

/// <summary>Chaperone 設定のスナップショット (仕様 F-6 の下地)。</summary>
sealed record ChaperoneSnapshot(
    float[][] StandingToRaw,
    float[][] SeatedToRaw,
    float[][][] BoundsQuads)
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
                .ToArray());

    public void Apply(ChaperoneTuner chaperone)
    {
        chaperone.SetWorkingStandingZeroPose(FromJagged(StandingToRaw));
        chaperone.SetWorkingSeatedZeroPose(FromJagged(SeatedToRaw));
        if (BoundsQuads.Length > 0)
        {
            chaperone.SetWorkingCollisionBounds(BoundsQuads
                .Select(q => new HmdQuad
                {
                    Corner0 = ToVector(q[0]),
                    Corner1 = ToVector(q[1]),
                    Corner2 = ToVector(q[2]),
                    Corner3 = ToVector(q[3]),
                })
                .ToArray());
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
