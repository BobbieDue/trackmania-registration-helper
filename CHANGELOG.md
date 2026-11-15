# Changelog

## 1.2.0
Safety improvements from an independent code audit. Nothing about how you use it has changed.
- The saved settings file is checked in full before anything is restored. It may only list the three settings this tool manages, for your own Windows account, each once, with sensible types and sizes, a known version and complete details. If anything is wrong, nothing is restored, nothing is changed and the file is kept. You then choose what to do: put the three boxes back to Windows' secure positions, and/or move the file aside (kept, never deleted).
- New saved files are version 2 and must list all three settings. Version 1 files from earlier builds (two or three settings) still work.
- If any of the three settings changed while Registration Mode was on, Disable shows what changed (what Registration Mode set, and what it is now) and asks before putting your originals back. Saying no changes nothing.
- Only one copy of the tool can run at a time. Opening it again brings the open window forward.
- After a successful restore, a problem archiving the saved copy is reported separately and no longer makes the restore look like it failed. Archived copies can't clash, even two restores in the same second.
- The README now explains that "Check for publisher's certificate revocation" has no Windows policy setting of its own, so policy detection covers only the TLS and server revocation settings.
- Tested automatically on Windows Server 2022 and 2025 (GitHub Actions), including turning it on and off on the real registry. Desktop Windows 10/11 testing follows the written plan in docs/DESKTOP-TESTING.md and has not been completed yet.

## 1.1.0
- Named TrackMania Registration Helper (TMRH): works for TrackMania Nations Forever and United Forever.
- App icon: C®AżΨ in the We Are Crazy server colours.
- Layouts grow with the text, so nothing overlaps at 125% or 150% display scaling.
- Numbered steps, and the button to press next is always blue.
- A saved copy from the test builds ("TMUF Registration Mode") is picked up automatically.

## 1.0.0
- First test build.
