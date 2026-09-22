# Microsoft Rewards Windows App

The Windows application provides a native GUI for managing Microsoft Rewards account profiles.

## Account storage

Account profiles are persisted in the Windows user's local application data and protected with Windows DPAPI using the current Windows user scope.

The application imports existing `.env` account entries on first launch when present, then uses the encrypted application store for subsequent launches.

Credentials are never committed to GitHub.

## Runtime

The application starts and monitors the Rewards Control API and companion dashboard as hidden child processes. The application registers itself for Windows startup when enabled in the UI.

## Build

The CI workflow produces a Windows x64 portable package containing:

- the self-contained Windows GUI executable;
- the built Rewards bot;
- the companion dashboard;
- the Node.js runtime;
- the Chromium browser runtime.

The package is tested with the application's built-in `--self-test` command and hidden background-start check before the artifact is uploaded.

All packaged components are validated before artifact upload.
