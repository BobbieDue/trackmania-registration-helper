# Changelog

## 1.2.2
Final release: two small fixes from the final independent review. Nothing about how you use it has changed.
- If turning Registration Mode on or off stops with an unexpected error, the message no longer says your original
  settings are saved unless a saved copy was found and checked afterwards (the error can happen before anything is
  saved). It now tells you to close the tool, open it again and check what it says, and not to assume Registration
  Mode was turned on (or that your settings were put back).
- A version 1 saved file can't contain an account ID (SID) whatever order its lines are in. Before, a damaged file
  with the account ID above the version line got past that check. Such a file is now refused and nothing is changed.
  Genuine version 1 and version 2 files, including version 2 files from 1.2.0 without an account ID, still work.
- The README now explains that saved copies from older versions have no account ID, so they can't be checked
  against your Windows account the way newer ones are.

## 1.2.1
Fixes from a second independent review.
- A saved copy left by the old test builds ("TMUF Registration Mode") is moved in when first needed. If it can't be moved (for example an antivirus scan has it open), the tool now says so, shows where the file is and what to do, and won't turn Registration Mode on or off until it's sorted. Before, it carried on as if there was no saved copy, which could have saved already-changed settings as your "originals". The old file is never changed.
- The saved settings file's size is checked before it's read, so an oversized file is rejected without being loaded.
- New saved files record which Windows account they belong to (its SID). A file from a different account is refused and kept. Files from earlier versions, which don't have this, still work. The account name in the file is only a label, because names can change.

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
