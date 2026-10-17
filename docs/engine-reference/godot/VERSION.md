# Godot — Version Reference

Last verified: 2026-10-15

| Field | Value |
|-------|-------|
| **Engine Version** | 4.7.1 |
| **Project Pinned** | 2026-10-15 |
| **Installed at pin time** | NOT DETERMINED — `godot`, `godot4`, `godot-mono` not on PATH; `dotnet --version` failed to run. Absence of the engine is not established. |
| **Last Docs Verified** | 2026-10-15 |
| **LLM Knowledge Cutoff** | May 2025 (training data likely covers up to ~4.3) |
| **Risk Level** | HIGH — 4.7.1 is beyond LLM training data (4.4, 4.5, 4.6, 4.7 are all unseen) |
| **Language** | C# (.NET 8+) |

## Release timeline (post-cutoff)

Source: https://godotengine.org/download/archive/ (fetched 2026-10-15)

| Version | Released |
|---------|----------|
| 4.4 | 2025-03-03 |
| 4.4.1 | 2025-03-26 |
| 4.5 | 2025-09-15 |
| 4.5.1 | 2025-10-15 |
| 4.5.2 | 2026-03-19 |
| 4.6 | 2026-01-26 |
| 4.6.1 | 2026-02-16 |
| 4.6.2 | 2026-04-01 |
| 4.6.3 | 2026-05-20 |
| 4.7 | 2026-06-18 |
| **4.7.1 (pinned)** | 2026-07-14 |
| 4.7.2 | 2026-08-18 (newer patch than the pin — consider moving the pin) |

## Sources for this reference set

- https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.4.html
- https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.5.html
- https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.6.html
- https://docs.godotengine.org/en/stable/tutorials/migrating/upgrading_to_godot_4.7.html

## Not sourced (do not fill from training data)

- NOT SOURCEABLE — high-level multiplayer API behaviour (`MultiplayerAPI`, ENet peer, RPC) for 4.4–4.7: not described in the upgrade guides above beyond the RPC rename listed in breaking-changes.md. Spec chapter 13 §3 relies on this API; verify against the class reference before writing netcode.
- NOT SOURCEABLE — .NET SDK minimum for 4.6/4.7 desktop builds: the 4.4→4.5 page states Android requires .NET 9 and other platforms "still use .NET 8 as the minimum"; nothing for 4.6/4.7 was found.
- NOT SOURCEABLE — 4.7.1 patch-release changes: the upgrade guides cover minor versions only; no 4.7→4.7.1 notes were fetched.
- NOT SOURCEABLE — 4.7 dedicated-server / headless export specifics.
