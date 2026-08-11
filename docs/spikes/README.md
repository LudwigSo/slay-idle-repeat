# Spike findings

Time-boxed investigations that resolve an open item from `game-design/16_DECISION_LOG.md` Part B. Each document records the verdict, the evidence behind it, and what remains unverified — so the milestone that consumes the ruling does not have to re-research it.

A spike is not a feature. Its output is a decision **plus the failures encountered on the way** — the failures are the point, because they are what CI will hit. Throwaway code lives under `spikes/`.

| Spike | Open item | Task | Date | Verdict |
|---|---|---|---|---|
| [O14 — AppLovin MAX Godot: S2S rewarded callbacks & `setUserId`](O14-applovin-max-s2s.md) | **O14** (`12` §3.3) | M0-04 | 2026-08-11 | ✅ **Proceed, no plugin patch.** `setUserId` is absent from every layer, but `show_rewarded_ad(..., custom_data)` is wired end-to-end and populates `{CUSTOM_DATA}` in the S2S postback — a better, per-impression attribution channel. `IRewardedAdPort` survives unchanged. Two caveats: the no-fill grant (`16` A5) can never be S2S-verified and is client-asserted by construction, and the plugin appears **unmaintained** since 2025-04-24 with reported Godot 4.5 iOS breakage. |
| [O23 (Android leg) — Godot 4 C#/.NET Android export via the custom build template](O23-godot-android-export.md) | **O23** (`14` §1) | M0-05a | 2026-08-11 | ✅ **Works, with caveats.** **Godot 4.7.1-stable (mono)** + JDK 17 + build-tools 36.1.0 produces a signed, .NET-bearing debug APK through the Gradle path, headlessly. No engine downgrade needed; the `net8.0` pin is confirmed. Seven failures documented, including a silent one that yields a valid APK with **zero .NET inside**. |
| O23 (iOS leg) | **O23** (`14` §1) | M0-05b | — | ⏳ Pending. No Mac and no Apple Developer account, so this leg is a written CI recipe rather than an executed export. **O23 stays open until it runs on a macOS runner.** |
