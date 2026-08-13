using System.Globalization;
using SlayIdleRepeat.BalanceHarness.Cli;

// 🔒 The report is a data artefact — a nightly CI job diffs it and a design decision is made off it —
// so it is written in the invariant culture regardless of the machine's. This checkout's own build
// output is German ("Bestanden!", "Fehler:"); a report whose clear rates read "62,00%" on one machine
// and "62.00%" on another is not diffable, and a decimal comma inside a comma-separated table is worse
// than not diffable.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Everything is in HarnessRun so that SlayIdleRepeat.Core.Tests can exercise the CLI — a top-level
// Program is internal to this assembly and unreachable from the suite.
return HarnessRun.Run(args, Console.Out);
