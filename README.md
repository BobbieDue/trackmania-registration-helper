# TrackMania Registration Helper (TMRH)

**Can't create a new account in TrackMania Nations Forever or TrackMania United Forever? This fixes it, then puts
your Windows settings back.**

Both games still create accounts through Windows' old internet settings (the ones in **Internet Options**). Modern
Windows has made those settings stricter, so the old registration often fails on a fresh PC. TMRH switches on the few
settings the old system needs, only while you create your account, and then restores your own settings exactly.

Made for returning players by the [We Are C®AżΨ](https://wearecrazy.co.uk/) TrackMania community.

## How to use it

1. Download `TrackMania-Registration-Helper.exe` from the [latest release](../../releases/latest) or the
   [We Are C®AżΨ downloads page](https://wearecrazy.co.uk/downloads.html). There's nothing to install.
2. Open it and click **Enable Registration Mode**.
3. Start TrackMania Nations Forever or United Forever and create your account.
4. Come back to TMRH and click **Disable Registration Mode**. Your original settings are back.

You only need it once, to create the account. Playing the game afterwards needs nothing special.

Windows may say "Windows protected your PC" because the program isn't code-signed. Click **More info**, then
**Run anyway**. You can check the file first (see [Checking a download](#checking-a-download)).

## What it changes

Only these three settings in **Internet Options > Advanced > Security**, and only for your own Windows account:

| Internet Options box | While ON | Where Windows keeps it (your account only) |
|---|---|---|
| Use TLS 1.0, 1.1, 1.2 (and 1.3 on Windows 11) | ticked | `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\SecureProtocols` (bits 0x80, 0x200, 0x800, 0x2000 added; other bits left alone) |
| Check for server certificate revocation | unticked | `HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings\CertificateRevocation` = 0 |
| Check for publisher's certificate revocation | unticked | `HKCU\Software\Microsoft\Windows\CurrentVersion\WinTrust\Trust Providers\Software Publishing\State` (bit 0x200 added) |

It never touches Windows Defender, the firewall, your browsers or anything else.

**Security:** while Registration Mode is ON, Windows doesn't check whether certificates have been revoked, so you're
less protected against fake or withdrawn certificates. Turn it OFF as soon as your account exists.

## How it keeps your settings safe

- **Saved first:** before changing anything it saves each value (including ones that weren't set at all) to
  `%LocalAppData%\We Are Crazy\TrackMania Registration Helper\original-settings.txt`.
- **Saved once:** pressing Enable again never replaces that copy.
- **Put back exactly:** Disable restores each value with its original type and data, removes any value that wasn't
  there before, reads them all back, and only says it worked if they match.
- **Crash-safe:** the saved copy is swapped in whole, never half-written. If Windows shuts down part-way, opening TMRH
  again offers to put your settings back.
- **Never guesses:** with no saved copy, Disable changes nothing.
- **Respects policies:** if a work or school policy controls these settings, it says so and changes nothing.
- **Your account only:** it runs as you, never as administrator. All three settings belong to your own Windows
  account; asking for admin rights could change a different admin account's settings instead of yours.

**Settings check** (link at the bottom of the window) lets you confirm the table above on your own PC: take a
snapshot, tick or untick one box in Internet Options and click Apply, then Compare. It only reads, never changes.

## Colours

Colour-blind friendly: the Okabe-Ito palette (blue for ON and the next button to press, orange for warnings, grey for
OFF), no red or green, and every state also has its own symbol and words.

## Checking a download

Each release lists the file's SHA-256, also in `SHA256SUMS.txt`. In PowerShell:

```powershell
Get-FileHash .\TrackMania-Registration-Helper.exe -Algorithm SHA256
```

Every release is built by GitHub Actions from this source on two different versions of Windows, and is only
published if both builds come out byte-for-byte identical.

## Building it yourself

On Windows, with the [.NET 8 SDK](https://dotnet.microsoft.com/download) (only needed to build; players need nothing):

```
dotnet test tests/TMRH.Tests -c Release
dotnet build src/TMRH -c Release -o out
```

The program is `out\TrackMania-Registration-Helper.exe`. It targets .NET Framework 4.8, which is part of Windows 10
(supported versions) and Windows 11. The tests include turning it on and off on the real registry; that part only
runs when `TMUF_E2E=1` is set (the GitHub build machines set it), so running the tests won't change your PC.

To publish a new version: raise `<Version>` in `src/TMRH/TMRH.csproj`, add a `## <version>` section to
`CHANGELOG.md`, and push to `main`. The build publishes the release (tag `v<version>`) with that changelog section as
its notes, and the We Are C®AżΨ website picks it up within 30 minutes.

## Source layout

- `src/TMRH/Core/`: registry access, the saved copy, Enable/Disable and the checks
- `src/TMRH/UI/`: the window, colours and the Settings check
- `tests/`: unit tests (in-memory registry) and the real-registry round trip
- `tools/make_icon.py`: draws the icon

## Licence

MIT. See [LICENSE](LICENSE).
