# Desktop Windows test plan

The GitHub builds run on **Windows Server 2022 and 2025**. They prove the code builds identically and that turning
Registration Mode on and off restores the real registry exactly. They do **not** prove how the Internet Options
window looks or behaves on a desktop PC, or that TrackMania registration actually works. That is what this plan is for.

**Status: one result so far (Windows 11, reported by the We Are Crazy admin, see the table). Everything else below has not been run yet.** Results are added to the table at the bottom
as they are done. Nothing is marked as tested until someone has actually run it on that version of Windows.

## Machines

| Windows | Build | Scaling to test |
|---|---|---|
| Windows 10 22H2 | 19045 | 100%, 125%, 150% |
| Windows 11 23H2 | 22631 | 100%, 125%, 150% |
| Windows 11 24H2 or later | 26100+ | 100%, 125%, 150% |

A clean virtual machine or a spare Windows account is ideal. TMRH only changes the signed-in account's settings.

## 1. Confirm the registry mappings with the Settings check

For each machine, open TMRH, click **Settings check**, then **Take snapshot**. In Internet Options > Advanced >
Security change **one** box and click **Apply**, then click **Compare** in TMRH. Record the value that changed:

| Box | Expected value | Expected change |
|---|---|---|
| Use TLS 1.0 | `SecureProtocols` | bit 0x80 |
| Use TLS 1.1 | `SecureProtocols` | bit 0x200 |
| Use TLS 1.2 | `SecureProtocols` | bit 0x800 |
| Use TLS 1.3 (Windows 11) | `SecureProtocols` | bit 0x2000 |
| Check for server certificate revocation | `CertificateRevocation` | 1 ticked, 0 unticked |
| Check for publisher's certificate revocation | `...\WinTrust\Trust Providers\Software Publishing\State` | bit 0x200 set when unticked |

## 2. Registry starting states

Run the on/off cycle (section 3) from each of these starting states (back up with `reg export` first):

1. Windows defaults (fresh account).
2. All three values present with typical values.
3. Values missing: delete `SecureProtocols` and `CertificateRevocation` under HKCU Internet Settings.
4. A non-default type, e.g. `CertificateRevocation` as a string `"1"`.
5. A policy set: `HKLM\Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings\CertificateRevocation` = 1.
   Expected: TMRH says the settings are controlled by your organisation and changes nothing.

## 3. On/off cycle

1. Note the Internet Options boxes (screenshot), and `reg export` the two keys.
2. **Enable**: check the boxes now show TLS 1.0/1.1/1.2 (and 1.3 on Windows 11) ticked and both revocation boxes unticked.
3. With Registration Mode on, untick one box in Internet Options, then **Disable**: TMRH must list that change and ask.
   Click **No**: nothing changes. Click **Disable** again and **Yes**: originals restored.
4. Compare the boxes and a fresh `reg export` with step 1: they must be identical.
5. Open TMRH twice: the second launch must bring the first window forward.
6. Edit `%LocalAppData%\We Are Crazy\TrackMania Registration Helper\original-settings.txt` while on (change a path):
   TMRH must refuse to restore, keep the file, and offer the two recovery choices.

## 4. The real test: creating a TrackMania account

On at least one Windows 10 and one Windows 11 machine, with TrackMania Nations Forever or United Forever installed:

1. Without TMRH: try to create a new account and record what happens.
2. Enable TMRH, create the account, record the result.
3. Disable TMRH and check the account still logs in.

## Results

| Date | Windows | Scaling | Section | Result | Tester |
|---|---|---|---|---|---|
| 2026-11-15 | Windows 11 (build not recorded) | not recorded | 4 (v1.2.0) | Reported working: "it works on windows 11". Details (build, whether a new account was created, Disable afterwards) not yet recorded | We Are Crazy admin |
| | Windows 10 22H2 | | all | not yet run | |
