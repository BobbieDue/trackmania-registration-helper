using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tmrh.Core
{
    public enum Mode
    {
        Off,            // original settings in place, no backup
        On,             // backup saved and the registration settings are in place
        NotNeeded,      // no backup, but the settings already allow registration
        ChangedSince,   // backup saved, but something changed the settings since Enable
        Interrupted,    // Enable or Disable stopped part-way (crash, power cut): restore is ready
        Managed,        // an organisation's policy controls these settings
        BadBackup,      // the saved settings file failed validation: nothing is restored from it
        LegacyStuck     // an older test build's saved copy exists but couldn't be moved: nothing may change until it is
    }

    /// <summary>A setting that no longer has the value Registration Mode gave it.</summary>
    public sealed class Change
    {
        public string Setting; public RegValue Expected; public RegValue Current;
        public override string ToString() { return Setting + ": Registration Mode set " + Expected.Describe() + ", it is now " + Current.Describe(); }
    }

    public sealed class Status
    {
        public Mode Mode;
        public bool Tls13Available;
        public List<string> Managed = new List<string>();
        public Backup Backup;
        public List<string> BackupProblems = new List<string>();
    }

    public sealed class Result
    {
        public bool Ok;
        public string Message;
        public List<string> Details = new List<string>();
        /// <summary>Disable stopped before changing anything because these settings changed since Enable; ask, then call again with overwrite.</summary>
        public List<Change> NeedsConfirmation = new List<Change>();
        /// <summary>Problems after a successful restore (archiving the saved copy). The registry itself is fine.</summary>
        public List<string> Housekeeping = new List<string>();
        public static Result Fail(string m, IEnumerable<string> d = null) { var r = new Result { Ok = false, Message = m }; if (d != null) r.Details.AddRange(d); return r; }
    }

    /// <summary>Tells running programs that Internet Options changed (WinINet). A no-op in tests.</summary>
    public interface ISettingsNotifier { void SettingsChanged(); }
    public sealed class NoNotify : ISettingsNotifier { public void SettingsChanged() { } }

    /// <summary>Turns Registration Mode on and off, saving and restoring the player's own settings exactly.</summary>
    public sealed class Engine
    {
        readonly IRegistryStore reg;
        readonly BackupStore store;
        readonly ISettingsNotifier notify;
        readonly Func<int> windowsBuild;

        /// <summary>Called between steps, so tests can simulate a crash part-way through.</summary>
        public Action<string> StepHook;

        public Engine(IRegistryStore registry, BackupStore backups, ISettingsNotifier notifier, Func<int> build)
        {
            reg = registry; store = backups; notify = notifier ?? new NoNotify(); windowsBuild = build;
        }

        /// <summary>WinINet supports TLS 1.3 from Windows 11 (build 22000). On Windows 10 it was only experimental.</summary>
        public bool Tls13Available { get { return windowsBuild() >= 22000; } }

        uint NeededTls { get { return Settings.Tls10 | Settings.Tls11 | Settings.Tls12 | (Tls13Available ? Settings.Tls13 : 0); } }

        /// <summary>What each touched value should become, worked out from what it is now (only our bits change).</summary>
        List<RegValue> Desired(List<RegValue> now) { return now.Select(Expected).ToList(); }

        /// <summary>The value Registration Mode gives one setting, starting from the value it had before.</summary>
        RegValue Expected(RegValue before)
        {
            if (before.Name == Settings.SecureProtocols)
            {
                uint baseProtocols = before.AsDWord ?? (reg.Read(Hive.LocalMachine, Settings.InternetSettings, Settings.SecureProtocols).AsDWord ?? 0u);
                return RegValue.DWord(Hive.CurrentUser, before.Path, before.Name, 0).WithDWord(baseProtocols | NeededTls);
            }
            if (before.Name == Settings.CertificateRevocation) return RegValue.DWord(Hive.CurrentUser, before.Path, before.Name, 0);
            uint baseState = before.AsDWord ?? Settings.PublisherDefaultState;
            return RegValue.DWord(Hive.CurrentUser, before.Path, before.Name, 0).WithDWord(baseState | Settings.PublisherNoRevocationBit);
        }

        /// <summary>Settings that differ now from what Registration Mode set (only meaningful once Enable finished).</summary>
        public List<Change> ChangesSinceEnable(Backup b)
        {
            if (b == null || b.Stage != Stage.Applied) return new List<Change>();
            return b.Originals.Select(o => new Change { Setting = Friendly(o), Expected = Expected(o), Current = reg.Read(o.Hive, o.Path, o.Name) })
                .Where(c => !c.Current.SameAs(c.Expected)).ToList();
        }

        bool AllowsRegistration(List<RegValue> now)
        {
            return now[0].AsDWord.HasValue && (now[0].AsDWord.Value & NeededTls) == NeededTls
                && now[1].AsDWord == 0u
                && now[2].AsDWord.HasValue && (now[2].AsDWord.Value & Settings.PublisherNoRevocationBit) != 0;
        }

        public Status GetStatus()
        {
            var s = new Status { Tls13Available = Tls13Available, Managed = Settings.Managed(reg) };
            try { s.Backup = store.Load(); }
            catch (InvalidDataException ex) { s.Mode = Mode.BadBackup; s.BackupProblems = BackupInvalid.Problems(ex); return s; }
            catch (LegacyBackupException ex) { s.Mode = Mode.LegacyStuck; s.BackupProblems = LegacyHelp(ex); return s; }
            if (s.Backup != null && s.Backup.Stage == Stage.Restored) { store.Retire(); s.Backup = null; }   // restored earlier; only the archiving was left
            var now = Settings.Touched(reg).ToList();
            if (s.Backup != null && s.Backup.Stage != Stage.Applied) s.Mode = Mode.Interrupted;
            else if (s.Backup != null) s.Mode = AllowsRegistration(now) ? Mode.On : Mode.ChangedSince;
            else if (s.Managed.Count > 0) s.Mode = Mode.Managed;
            else s.Mode = AllowsRegistration(now) ? Mode.NotNeeded : Mode.Off;
            return s;
        }

        public Result Enable(Action<string> progress)
        {
            progress = progress ?? (_ => { });
            var managed = Settings.Managed(reg);
            if (managed.Count > 0)
                return Result.Fail("These settings are controlled by a policy on this PC (for example a work or school PC), so this tool can't change them. Nothing was changed.", managed);

            Backup backup;
            try { backup = store.Load(); }
            catch (InvalidDataException ex) { return Result.Fail("Your saved settings file is damaged, so nothing was changed. It has been left as it is. Use the recovery options to deal with it first.", BackupInvalid.Problems(ex)); }
            catch (LegacyBackupException ex) { return Result.Fail(ex.Message, LegacyHelp(ex)); }
            if (backup != null && backup.Stage == Stage.Restored) backup = null;   // already restored: start a fresh copy
            var now = Settings.Touched(reg).ToList();
            if (backup == null)
            {
                progress("Saving your current settings...");
                backup = new Backup { Stage = Stage.Applying, Created = DateTime.UtcNow, User = Environment.UserDomainName + "\\" + Environment.UserName, Sid = store.CurrentSid, Originals = now };
                store.Save(backup);
            }
            else
            {
                progress("Your original settings are already saved, keeping them as they are...");
                backup.Stage = Stage.Applying;   // never replace the originals (a version 1 file stays version 1)
                store.Save(backup);
            }
            Step("backup-saved");

            var want = Desired(now);
            foreach (var v in want)
            {
                progress("Changing " + Friendly(v) + "...");
                reg.Write(v);
                Step("wrote " + v.Name);
            }
            notify.SettingsChanged();

            progress("Checking the changes...");
            var wrong = want.Where(v => !reg.Read(v.Hive, v.Path, v.Name).SameAs(v)).Select(v => Friendly(v) + ": wanted " + v.Describe() + ", found " + reg.Read(v.Hive, v.Path, v.Name).Describe()).ToList();
            if (wrong.Count > 0)
                return Result.Fail("Some settings didn't change as expected, so Registration Mode is not on. Your original settings are saved: click Disable to put them back.", wrong);

            backup.Stage = Stage.Applied;
            store.Save(backup);
            var r = new Result { Ok = true, Message = "Registration Mode is now on. You can start TrackMania Nations Forever or United Forever and create your account. Once your account has been created successfully, return here and disable Registration Mode to restore your original Windows settings." };
            if (!Tls13Available) r.Details.Add("TLS 1.3 isn't available in this version of Windows, so it was left as it is.");
            return r;
        }

        public Result Disable(Action<string> progress) { return Disable(progress, false); }

        /// <summary>
        /// Puts the saved settings back exactly. The whole saved file is checked first; if it fails, nothing is written
        /// and the file is kept. If any setting changed since Enable, nothing is written unless overwriteChanges is true:
        /// the result lists the changes so the player can decide.
        /// </summary>
        public Result Disable(Action<string> progress, bool overwriteChanges)
        {
            progress = progress ?? (_ => { });
            Backup backup;
            try { backup = store.Load(); }
            catch (InvalidDataException ex)
            {
                return Result.Fail("Your saved settings file failed its safety check, so nothing was restored and nothing was changed. The file has been kept as it is.", BackupInvalid.Problems(ex));
            }
            catch (LegacyBackupException ex) { return Result.Fail(ex.Message, LegacyHelp(ex)); }
            if (backup == null || backup.Stage == Stage.Restored)
                return Result.Fail("There are no saved settings to restore, so nothing was changed. (Registration Mode was not turned on by this tool on this Windows account.)");

            var changed = ChangesSinceEnable(backup);
            if (changed.Count > 0 && !overwriteChanges)
                return new Result { Ok = false, NeedsConfirmation = changed, Message = "Some settings changed after Registration Mode was turned on. Nothing has been changed yet." };

            backup.Stage = Stage.Restoring;
            store.Save(backup);
            Step("restoring");
            foreach (var o in backup.Originals)
            {
                progress("Restoring " + Friendly(o) + "...");
                if (o.Exists) reg.Write(o); else reg.Delete(o.Hive, o.Path, o.Name);
                Step("restored " + o.Name);
            }
            notify.SettingsChanged();

            progress("Checking your settings are back...");
            var wrong = backup.Originals.Where(o => !reg.Read(o.Hive, o.Path, o.Name).SameAs(o))
                .Select(o => Friendly(o) + ": should be " + o.Describe() + ", found " + reg.Read(o.Hive, o.Path, o.Name).Describe()).ToList();
            if (wrong.Count > 0)
                return Result.Fail("Some settings could not be put back. Your saved copy is kept, so you can try Disable again.", wrong);

            var r = new Result { Ok = true, Message = "Registration Mode is now off. Your original Windows Internet Options settings have been restored." };
            r.Housekeeping = store.Retire();   // never throws: the registry is already back and checked
            return r;
        }

        /// <summary>
        /// Explicit recovery when the saved file can't be trusted: puts the three boxes back to Windows' secure positions
        /// (TLS 1.0 and 1.1 off, TLS 1.2 on, 1.3 on where available, both revocation checks on), keeping any other bits.
        /// Never called automatically. The saved file is not touched.
        /// </summary>
        public Result ResetToSecureSettings()
        {
            var managed = Settings.Managed(reg);
            if (managed.Count > 0) return Result.Fail("These settings are controlled by a policy on this PC, so this tool can't change them. Nothing was changed.", managed);
            var now = Settings.Touched(reg).ToList();
            uint tls13 = Tls13Available ? Settings.Tls13 : 0;
            var want = new List<RegValue>
            {
                now[0].Kind == RegKind.DWord ? now[0].WithDWord((now[0].AsDWord.Value & ~(Settings.Tls10 | Settings.Tls11)) | Settings.Tls12 | tls13)
                                             : RegValue.DWord(Hive.CurrentUser, Settings.InternetSettings, Settings.SecureProtocols, 0).WithDWord(Settings.Tls12 | tls13),
                RegValue.DWord(Hive.CurrentUser, Settings.InternetSettings, Settings.CertificateRevocation, 1),
                now[2].Kind == RegKind.DWord ? now[2].WithDWord(now[2].AsDWord.Value & ~Settings.PublisherNoRevocationBit)
                                             : RegValue.DWord(Hive.CurrentUser, Settings.SoftwarePublishing, Settings.State, 0).WithDWord(Settings.PublisherDefaultState),
            };
            foreach (var v in want) reg.Write(v);
            notify.SettingsChanged();
            var wrong = want.Where(v => !reg.Read(v.Hive, v.Path, v.Name).SameAs(v)).Select(v => Friendly(v) + ": wanted " + v.Describe() + ", found " + reg.Read(v.Hive, v.Path, v.Name).Describe()).ToList();
            if (wrong.Count > 0) return Result.Fail("Some settings didn't change as expected.", wrong);
            return new Result { Ok = true, Message = "The three settings are back to Windows' secure positions." };
        }

        public static List<string> LegacyHelp(LegacyBackupException ex)
        {
            return new List<string>
            {
                "The older saved copy is here (left exactly as it was): " + ex.OldFile,
                "This version keeps it here: " + ex.NewFile,
                "Close anything that might be using it (an antivirus scan, an editor), then open this tool again. Or move the file to the new place yourself.",
                "Until then this tool won't turn Registration Mode on or off, so your genuine original settings can't be lost.",
            };
        }

        void Step(string what) { if (StepHook != null) StepHook(what); }

        public static string Friendly(RegValue v)
        {
            if (v.Name == Settings.SecureProtocols) return "the TLS settings";
            if (v.Name == Settings.CertificateRevocation) return "server certificate revocation checking";
            if (v.Name == Settings.State) return "publisher certificate revocation checking";
            return v.Name;
        }
    }
}
