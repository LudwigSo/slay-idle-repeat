// The `15` §B4 post-processing pipeline's CLI. The library half is the part that matters —
// M8-10 drives it in-process over the whole manifest — and the entry point exists because the
// other tools/ projects are Exe for the same reason: a human runs a batch by hand.
//
// M8-06 phase 1: the API surface is defined and the bodies are not. See
// tests/SlayIdleRepeat.AssetPipeline.Tests for what they must do.
Console.WriteLine(
    "AssetPipeline: not implemented yet - see doc 15 §B4 and assets/pipeline/thresholds.json.");
return 0;
