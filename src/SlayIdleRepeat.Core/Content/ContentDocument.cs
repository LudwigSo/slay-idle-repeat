namespace SlayIdleRepeat.Core.Content;

/// <summary>One loaded, validated content document: its snapshot-relative path and its root value.</summary>
/// <param name="Path">Snapshot-relative path with forward slashes, e.g. <c>tuning/forge.json</c>.</param>
/// <param name="Root">The document's root value.</param>
public sealed record ContentDocument(string Path, ContentValue Root);
