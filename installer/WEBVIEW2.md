# WebView2 Evergreen Bootstrapper

`MicrosoftEdgeWebview2Setup.exe` is Microsoft’s official Evergreen Bootstrapper.
It is embedded by `macwidget.iss` as a `dontcopy` file and is extracted to the
setup temporary directory only when the WebView2 Runtime registry key is absent.
The bootstrapper downloads the matching Runtime from Microsoft and performs a
per-user silent install, so it matches MacWidget’s non-admin installer model.

- Canonical download: `https://go.microsoft.com/fwlink/p/?LinkId=2124703`
- Downloaded: 2026-07-25
- SHA-256: `0223fa1e8d5bd5e4344fb8734e60d088e79f262c0a24444d01f240bc996f04e5`

When refreshing the file, download only from the canonical Microsoft link,
record the new SHA-256 here, and validate its Authenticode signature on Windows
before committing it.  The bootstrapper needs internet access only on devices
where the Evergreen WebView2 Runtime is not already installed.
