# Spikes

Time-boxed investigations that answer an open question from
`game-design/16_DECISION_LOG.md`. Each spike produces a written verdict here;
throwaway code lives under `spikes/`.

A spike is not a feature. Its output is a decision plus the failures encountered
on the way — the failures are the point, because they are what CI will hit.

| Question | Spike | Task | Verdict |
|---|---|---|---|
| O23 (Android leg) | [Godot 4 C#/.NET Android export via the custom build template](O23-godot-android-export.md) | M0-05a | **Works, with caveats.** Godot **4.7.1-stable (mono)** + JDK 17 + build-tools 36.1.0 produces a signed, .NET-bearing debug APK through the Gradle path. No engine downgrade needed. `net8.0` pin confirmed. iOS still open — see M0-05b. |
