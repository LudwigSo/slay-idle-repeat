using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Cli;

// The report is a diffed CI artefact, so it's written in the invariant culture regardless of the
// machine's — otherwise clear rates could read "62,00%" on one machine and "62.00%" on another.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Everything is in HarnessRun so that SlayIdleRepeat.Core.Tests can exercise the CLI — a top-level
// Program is internal to this assembly and unreachable from the suite.
return HarnessRun.Run(args, Console.Out);
