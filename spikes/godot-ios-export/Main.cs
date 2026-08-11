using System.Runtime.InteropServices;
using Godot;

namespace SlayIdleRepeat.Spike;

/// <summary>
/// Entry point of the O23 iOS spike scene. Prints values that can only exist if
/// a real .NET runtime is executing managed code: a result computed by
/// <see cref="SpikeMath"/>, the framework description, and the process
/// architecture.
///
/// The marker line is deliberately the same shape as the Android leg's, so a
/// device run can be checked by watching the Xcode console for
/// "[O23] RESULT:" exactly as the Android leg greps logcat.
///
/// ⚠️ On iOS this must be run in a RELEASE build to be worth anything. Both of
/// the engine issues that most threaten this project -- #96072 (NativeAOT node
/// instantiation) and #121736 (the .NET-only launch crash filed against 4.7.1)
/// -- are release-only and run clean in debug.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        long yield = SpikeMath.IdleYield(baseRate: 3, seconds: 14);
        string fingerprint = SpikeMath.Fingerprint(7, 1, 9, 4);

        GD.Print("[O23] ---- Godot C#/.NET iOS spike ----");
        GD.Print($"[O23] SpikeMath.IdleYield(3, 14) = {yield}");
        GD.Print($"[O23] SpikeMath.Fingerprint(7,1,9,4) = {fingerprint}");
        GD.Print($"[O23] framework = {RuntimeInformation.FrameworkDescription}");
        GD.Print($"[O23] process arch = {RuntimeInformation.ProcessArchitecture}");
        GD.Print($"[O23] os = {RuntimeInformation.OSDescription}");
        GD.Print($"[O23] godot = {Engine.GetVersionInfo()["string"]}");

        // 168 = 3 * (2+4+6+8+10+12+14). If the managed layer were stubbed out,
        // or the trimmer removed something LINQ needs, these lines either never
        // appear or report the wrong numbers.
        GD.Print(yield == 168 && fingerprint == "9-7-4-1"
            ? "[O23] RESULT: managed code executed correctly."
            : "[O23] RESULT: FAILURE - managed code produced wrong values.");
    }
}
