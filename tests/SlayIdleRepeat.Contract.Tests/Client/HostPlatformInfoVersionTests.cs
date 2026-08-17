using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Adapters.Platform.Host;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Client;

/// <summary>
/// The three arms of <see cref="HostPlatformInfo"/>'s build-version chain, each driven to the place
/// it answers from.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Not in the shared suite: the suite can only ask whether <c>AppVersion</c> is blank.</b>
/// That is the whole of what <c>IPlatformInfoPort</c> promises, and it is satisfied by every arm
/// here — including, until this file existed, an arm that could not run at all.
/// </para>
/// <para>
/// 🔴 <b>The defect these cases exist for was introduced by a fix.</b> The chain used to end in
/// <c>?? throw</c> over <c>typeof(HostPlatformInfo).Assembly</c>, which is never null and whose
/// <c>GetName().Version</c> is <c>0.1.0.0</c> — so the guard could not fire, the
/// <c>&lt;exception&gt;</c> tag documented an impossible state, and nothing in the tree would ever
/// have said so. Taking both assemblies as arguments is what makes the failure reachable, and
/// reaching it is what this file does.
/// </para>
/// </remarks>
public sealed class HostPlatformInfoVersionTests
{
    /// <summary>The entry assembly answers when it states a version.</summary>
    /// <remarks>
    /// Driven with this test assembly rather than the real entry assembly, which under a test host
    /// is the test platform — a real answer to "which build started this process" and the wrong
    /// subject for a case about which assembly is consulted first.
    /// </remarks>
    [Fact]
    public void The_entry_assembly_answers_first()
    {
        var entry = typeof(HostPlatformInfoVersionTests).Assembly;

        HostPlatformInfo.ReadBuildVersion(entry, typeof(HostPlatformInfo).Assembly)
            .ShouldBe(Stated(entry));
    }

    /// <summary>
    /// 🔒 This adapter's own assembly answers when there is no entry assembly — the Godot client's
    /// case, because an unmanaged host started the runtime.
    /// </summary>
    [Fact]
    public void This_assembly_answers_when_an_unmanaged_host_started_the_process()
    {
        var self = typeof(HostPlatformInfo).Assembly;

        HostPlatformInfo.ReadBuildVersion(null, self).ShouldBe(
            Stated(self),
            "Assembly.GetEntryAssembly() is null under an unmanaged host, and the shipping client "
            + "is exactly that. An arm that only ever ran under a managed entry point would be "
            + "untested on the one host this adapter has to answer for.");
    }

    /// <summary>
    /// 🔒 Build metadata is stripped: the version, not the version plus the commit it was built at.
    /// </summary>
    /// <remarks>
    /// The SDK appends the source revision by default, so this is what every real build looks like
    /// — measured on this repository's own output, where the raw attribute carries a forty-character
    /// hash after a <c>+</c>.
    /// </remarks>
    [Fact]
    public void Build_metadata_is_stripped_from_the_answer()
    {
        var self = typeof(HostPlatformInfo).Assembly;
        var raw = self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        raw.ShouldNotBeNullOrWhiteSpace("this assembly states an informational version to strip.");

        var answer = HostPlatformInfo.ReadBuildVersion(null, self);

        answer.ShouldNotContain(
            "+",
            Case.Sensitive,
            $"the raw attribute is '{raw}'. A player reads this off a settings screen and a support "
            + "agent asks for it; neither wants the commit hash, and the port asks for the version.");
        raw.ShouldStartWith(answer, Case.Sensitive, "stripping must not change the version itself.");
    }

    /// <summary>
    /// 🔒 The failure arm: nothing states a version, so the adapter says so rather than inventing
    /// one.
    /// </summary>
    /// <remarks>
    /// This is the arm that could not previously be reached. <c>AppVersion</c> has no absent state,
    /// so the alternative to failing loudly is a placeholder that every crash report then treats as
    /// a real build number — which is steering <b>S6</b>, and is worse than a crash at composition.
    /// </remarks>
    [Fact]
    public void Nothing_stating_a_version_is_a_loud_failure_rather_than_a_placeholder()
    {
        var failure = Should.Throw<InvalidOperationException>(
            () => HostPlatformInfo.ReadBuildVersion(null, null));

        failure.Message.ShouldContain("no informational version and no assembly version");
    }

    /// <summary>What an assembly states, stripped the way the adapter strips it.</summary>
    private static string Stated(Assembly assembly)
    {
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrWhiteSpace(informational))
        {
            return assembly.GetName().Version!.ToString();
        }

        var metadata = informational.IndexOf('+');

        return metadata < 0 ? informational : informational[..metadata];
    }
}
