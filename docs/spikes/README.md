# Spike findings

Time-boxed investigations that resolve an open item from `game-design/16_DECISION_LOG.md` Part D. Each document records the verdict, the evidence behind it, and what remains unverified — so the milestone that consumes the ruling does not have to re-research it.

| Spike | Open item | Milestone | Date | Verdict |
|---|---|---|---|---|
| [O14 — AppLovin MAX Godot: S2S rewarded callbacks & `setUserId`](O14-applovin-max-s2s.md) | **O14** (`12` §3.3) | M0 / M0-04 | 2026-08-11 | ✅ **Proceed, no plugin patch.** `setUserId` is absent from every layer, but `show_rewarded_ad(..., custom_data)` is wired end-to-end and populates `{CUSTOM_DATA}` in the S2S postback — a better, per-impression attribution channel. `IRewardedAdPort` survives unchanged. Two caveats: the no-fill grant (`16` A5) can never be S2S-verified and is client-asserted by construction, and the plugin appears **unmaintained** since 2025-04-24 with reported Godot 4.5 iOS breakage. |
