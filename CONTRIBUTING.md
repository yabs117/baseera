# Contributing

Baseera is an early Windows desktop project. Small, tested improvements are welcome.

1. Describe the user-visible problem in an issue before a large change.
2. Build with a .NET 10 SDK on Windows: `./build.ps1 -Test -Smoke`.
3. Run extension tests: `node --test extension/test/*.test.js` (Node 22+).
4. Keep UI strings in all four languages and inspect Arabic RTL plus a Latin locale.
5. Explain what was actually tested. Label mocked/provider-free checks clearly.

Do not add a rule that calls a file malicious just because it is unsigned, a mod,
or references a sensitive API. Rules return evidence for human review. Add benign
counterexamples and coverage-limit tests. Never execute untrusted samples in tests.

Avoid adding dependencies unless the platform cannot reasonably provide the feature.
Never commit API keys, personal paths, scan reports, captured windows, or real malware.

Useful first contributions: accessible controls, better localized diagnostics,
additional archive parsers with resource budgets, signed release distribution,
DOM translation tests, multi-monitor overlay tests, and documented update adapters.
