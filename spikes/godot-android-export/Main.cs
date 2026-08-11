using System.Runtime.InteropServices;
using Godot;

namespace SlayIdleRepeat.Spike;

/// <summary>
/// Entry point of the O23 spike scene. Prints values that can only exist if a
/// real .NET runtime is running: a result computed by <see cref="SpikeMath"/>,
/// the framework description, and the process architecture.
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        long yield = SpikeMath.IdleYield(baseRate: 3, seconds: 14);
        string fingerprint = SpikeMath.Fingerprint(7, 1, 9, 4);

        GD.Print("[O23] ---- Godot C#/.NET Android spike ----");
        GD.Print($"[O23] SpikeMath.IdleYield(3, 14) = {yield}");
        GD.Print($"[O23] SpikeMath.Fingerprint(7,1,9,4) = {fingerprint}");
        GD.Print($"[O23] framework = {RuntimeInformation.FrameworkDescription}");
        GD.Print($"[O23] process arch = {RuntimeInformation.ProcessArchitecture}");
        GD.Print($"[O23] os = {RuntimeInformation.OSDescription}");
        GD.Print($"[O23] godot = {Engine.GetVersionInfo()["string"]}");

        // 168 = 3 * (2+4+6+8+10+12+14). If the managed layer were stubbed out
        // these lines would simply never appear.
        GD.Print(yield == 168 && fingerprint == "9-7-4-1"
            ? "[O23] RESULT: managed code executed correctly."
            : "[O23] RESULT: FAILURE - managed code produced wrong values.");
    }
}
