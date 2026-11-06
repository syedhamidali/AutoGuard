# Contributing

1. Fork, branch from `main`, run `dotnet test` before opening a PR.
2. Keep diagnostic logic in `src/analysis` (pure, unit-tested). Never fabricate readings; a missing sensor is `null` / "Not available".
3. Any feature that writes to hardware must require explicit confirmation, keep a safety floor, and provide a restore-to-default path.
4. New dependencies: check the license and add them to `THIRD-PARTY-NOTICES.md`.
5. Hardware reports help most: open an issue with your CPU/GPU/board and which sensors showed "Not available".

By contributing you agree your work is released under the MIT License.
