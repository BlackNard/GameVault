# Automatic administrator-required game launch

Owner request: clicking Play handles administrator-required executables without changing Windows compatibility settings or requiring the player to start GameVault manually as administrator. Windows' approval prompt remains required.

## Design

- Keep the existing direct/job launch for normal games. Only Windows ERROR_ELEVATION_REQUIRED (740) triggers an elevated helper using ShellExecute runas; unrelated errors never request elevation.
- Reuse GameVault.exe in a dedicated helper mode before workspace/data initialization. It launches exactly one requested executable in the existing no-kill process group, then transfers read/query/synchronize process and job handles to the normal parent using DuplicateHandle. The helper exits; the normal app retains lifetime observation.
- Exchange the one launch request, handle response, and ownership acknowledgment through a random, explicit-user-SID-restricted, first-instance local-only named pipe. Verify both peer process IDs. Bound messages and connection/handshake timeouts. No shell interpolation of game paths, no registry changes, no stored credentials, and no termination flags.
- Use limited process handles for later observation so elevated descendants can be queried/waited without requesting all-access rights. Cancellation of approval reports a friendly result and leaves Not started unchanged. All native failures include Windows' error number/message.
- If transport fails, dispose observation handles and leave any launched game alive. Helper mode never opens profile/settings storage. A local same-account UAC approval is supported; different-account credentials may fail current-user pipe authentication with a clear diagnostic.

## Execution

- [x] Add failing tests for 740-only fallback, normal path, unrelated error, and cancelled approval.
- [x] Implement limited-handle observation, one-shot helper/pipe transfer, and helper app entry point.
- [x] Test same-account helper transport without elevation or desktop windows: duplicate handles remain usable after helper exit, closing observation leaves fixture alive, correct working folder, and rejected peer/invalid request.
- [ ] Focused review, full locked verification, updated PR #23/Windows CI, and a separate corrected development package. Owner checks the actual UAC prompt and RE2; assistant does not launch real games or trigger UAC.

Native references: Microsoft docs for [runas](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.verb?view=net-10.0), [pipe options](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.pipeoptions?view=net-10.0), [server PID](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid), [client PID](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid).

## Verification ledger

- Observed two expected policy test failures before implementing 740 fallback and cancelled-approval handling; normal/unrelated-error cases stayed green.
- The .NET CurrentUserOnly flag also checks elevation level/owner SID, which conflicts with this medium-to-high helper. BrokerPipe instead uses a protected DACL granting the actual WindowsIdentity.User SID, rejects remote clients, and reserves the first pipe instance. Both peer PID checks remain required.
- One focused review found no material correctness/security issues in the revised pipe, helper startup isolation, restricted handle transfer, and no-kill disposal. An interrupted final confirmation may retain two read-only observation handles until the parent exits; ownership rules avoid closing potentially reused handles.
- Final scripts/verify.ps1 passed locked restore, Release build with 0 warnings/errors, all 87 automated tests, and self-contained win-x64 publish. Tests cover native handle transfer, helper exit, child tracking, detach-without-kill, invalid message/request, mismatched helper peer, and exact native-error preservation.
- Actual Windows UAC and RE2 launch remain owner-owned manual checks. No UAC prompt, real game, or desktop UI was launched by the assistant.
