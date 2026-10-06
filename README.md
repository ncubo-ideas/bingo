# Bingo

A bingo app for in-person events. The organizer, the people selling cards and the
players all use their phones in the same room, and the phones talk to each other
directly: the game keeps going without an internet connection. Players without the
app can join from a browser with the web player.

The app is bilingual (English and Spanish). It follows the phone's language and lets
each person switch from the cover screen.

## Repository layout

| Folder | What it holds |
| --- | --- |
| `App/BingoApp` | The Android app (.NET MAUI): organizer, collaborator and player screens. |
| `App/BingoWeb` | The web player: the same player pages running on .NET WebAssembly in the browser. |
| `Engine/BingoEngine` | The bingo rules: cards, draws, winning patterns, rounds. |
| `Engine/Village` | The event around the game: sales, collaborators, prizes, raffles. |
| `Test/` | Unit tests for the engine, the village, the app's texts and end-to-end script tests. |
| `Docker/` | Builds the Android APK inside a container, with nothing installed on the machine. |

`Bingo.slnx` opens everything in Visual Studio.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download), plus the .NET 9 runtime: the
  engine, the tests and the web player target .NET 9
- For the Android app: Java 17 (for example the Microsoft Build of OpenJDK), the MAUI
  Android workload and the Android SDK

  ```bash
  dotnet workload install maui-android
  ```

  ```bash
  dotnet build App/BingoApp/BingoApp.csproj -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=True
  ```

- For the web player: [Node.js](https://nodejs.org) and PowerShell (Windows PowerShell 5.1
  or PowerShell 7 on any system)
- Optional: [Docker](https://www.docker.com) to build the APK without any of the above

## Run the tests

The unit tests need only the .NET SDK:

```bash
dotnet test Test/BingoEngineTests/BingoEngineTests.csproj
```

```bash
dotnet test Test/VillageTests/VillageTests.csproj
```

```bash
dotnet test Test/BingoEngineDslTests/BingoEngineDslTests.csproj
```

```bash
dotnet test Test/BingoAppTests/BingoAppTests.csproj --filter "FullyQualifiedName!~Integration"
```

`Test/BingoAppTests/Integration` drives the real app on Android emulators. Those tests
skip themselves until you create `Test/BingoAppTests/appsettings.local.json` with the
same shape as `appsettings.json`: the path to `adb`, the serials of up to three
emulators and the APK to install.

## Run the app

Start an Android emulator (or plug in a phone with USB debugging), then:

```bash
dotnet build App/BingoApp/BingoApp.csproj -t:Run -f net10.0-android
```

Choose **Organizer** to create a bingo. Its invitation codes and QR codes bring the
collaborators and the players in.

## Run the web player

From a PowerShell prompt at the repository root:

```powershell
./App/BingoWeb/build.ps1 -Serve
```

This brings the player pages and texts from `App/BingoApp`, builds the WebAssembly
player and serves it at <http://localhost:18160>. Requests to `/api` go to an organizer
at `127.0.0.1:18080`. To play against the app on an emulator, open it as **Organizer**
(that is when it starts listening, on port 8080) and forward the port:

```bash
adb forward tcp:18080 tcp:8080
```

## Build the APK with Docker

From a PowerShell prompt at the repository root:

```powershell
./Docker/build.ps1
```

Without PowerShell, the same build is:

```bash
docker compose -f Docker/docker-compose.yml --profile build run --rm apk
```

The APK lands in `Docker/out/bingo.apk`. The first run takes about ten minutes while it
builds the image; later runs take one or two. The repository does not ship a signing
key, so the APK is signed with a throwaway debug key.

## Configuration

All optional. The app reads these from its environment:

| Variable | Effect |
| --- | --- |
| `BINGO_IROH_RELAY` | `1` lets phones reach each other through public relay servers when they are not on the same network. Off by default: phones use direct addresses only. Build with `-p:BingoRelay=1` to bake it into the APK. |
| `BINGO_TIMEOUT` | Seconds a phone waits for another before giving up. Default `15`. |
| `BINGO_PUBLIC_URL` | The address other phones should use for the organizer when it runs behind a proxy or a tunnel. |

The web player build reads one:

| Variable | Effect |
| --- | --- |
| `BINGO_IROH_BROWSER` | Folder of a local `Ncubo.Puppeteer.Adapters.Iroh.Browser` checkout. With it the web player also connects over iroh; without it, over HTTP only. |

## Third-party components

- `App/BingoApp/localfeed` holds builds of the `Ncubo.Puppeteer` packages, the event
  sourcing and messaging library the app runs on. They are property of NCubo Ideas, are
  included only so the repository builds, and are **not** covered by this repository's
  license.
- jsQR (`App/BingoWeb/wwwroot/jsQR.js`) is distributed under its own license, included
  next to it.
- The fonts under `wwwroot/fonts` and `Resources/Fonts` keep their own licenses.

## License

The code in this repository is released under the [MIT License](LICENSE), except for
the third-party components listed above.
