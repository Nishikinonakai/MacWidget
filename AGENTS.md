# MacWidget contributor guidance

## Scope

- MacWidget is a native Windows WPF/WebView2 application targeting `net10.0-windows10.0.19041.0`.
- Runtime layouts and preferences under `%LOCALAPPDATA%\\MacWidget` are per-user state, not source-controlled project configuration.
- Do not commit `bin/`, `obj/`, `publish/`, `udf/`, logs, IDE state, installers, WebView user data, or credentials.

## Local development

- Requires the .NET 10 SDK. On macOS/Linux cross-compilation, keep Windows targeting enabled.
- Build check:

  ```bash
  dotnet build src/WidgetProto -c Release -p:EnableWindowsTargeting=true
  ```

- Release artifact:

  ```bash
  dotnet publish src/WidgetProto -c Release -r win-x64 --self-contained true -o publish
  ```

- The installed application and its `%LOCALAPPDATA%` state are separate from this Git worktree. Do not treat deployment folders as source-sync targets.

## Dual-machine workflow

- GitHub `origin/main` is the shared source of truth. Before edits run `git fetch origin`, inspect `git status -sb`, and update a clean checkout with `git pull --ff-only`.
- Commit and push a coherent, verified change before continuing the same branch on the other machine. For genuine parallel work, use a named feature branch and merge deliberately.
- Before any remote deployment or release operation, run `git fetch origin && git status` and resolve divergence or local changes first.
- Keep host addresses, access tokens, user-specific install paths, and personal credentials out of the repository and out of prompts/logs.

## Windows validation

- Preserve the active user's desktop state. Run installed-app smoke checks only during an approved idle window.
- Validate the optional MacDesk link only when both applications are intentionally installed and running on the test machine.
