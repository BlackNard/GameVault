# Google Images composition dependency crash — issue57

Windows event1026 identified FileNotFoundException for Microsoft.Windows.SDK.NET Version10.0.17763.10 during WebView2CompositionControl.TryInitializeD3DImage/OnApplyTemplate. This occurred asynchronously during layout, outside the search handler catch. The generic net10.0-windows target did not bring the required projection assemblies into the build.

App and App.Tests now target net10.0-windows10.0.17763.0, bringing Microsoft.Windows.SDK.NET and WinRT.Runtime through the pinned .NET SDK. The installer minimum remains10.0.19041. A scoped preflight verifies the composition projection before creating the control; expected dependency failures show an in-app fallback rather than scheduling a broken control into layout. No global exception swallowing was added.

Regression evidence: a headless exact Assembly.Load test reproduced the owner's exception before the target change, then passed afterward. A separate console probe rejects the previous broken publish, loads the new clean published graph in an isolated context, and runs the same preflight without creating a WPF control, browser, window or workspace. It is now part of scripts/verify.ps1. Installer payload checks require both projection DLLs and publisher notices. The unmodified Windows SDK license was retrieved from the exact NuGet-declared publisher URL; SHA256 is recorded and checked during packaging.

Completed local run after interruption:325Core+184App tests pass; one native encoder test remains disposable-runner only locally. Clean publish dependency probe passed. Packaging safeguards installer32/release30; final capture/CI/source review and clean delivery remain handoff gates. Owner native browser retest remains pending; never perform it on the owner PC.
