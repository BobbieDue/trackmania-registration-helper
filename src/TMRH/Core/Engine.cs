using System;
using System.Collections.Generic;
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
        Managed         // an organisation's policy controls these settings
    }

    public sealed class Status
    {
        public Mode Mode;
        public bool Tls13Available;
        public List<string> Managed = new List<string>();
        public Backup Backup;
    }

    public sealed class Result
    {
        public bool Ok;
        public string Message;
        public List<string> Details = new List<string>();
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
        List<RegValue> Desired(List<RegValue> now)
        {
            var sp = now[0];
            uint baseProtocols = sp.AsDWord ?? (reg.Read(Hive.LocalMachine, Settings.InternetSettings, Settings.SecureProtocols).AsDWord ?? 0u);
            var cr = now[1];
            var st = now[2];
            uint baseState = st.AsDWord ?? Settings.PublisherDefaultState;
            return new List<RegValue>
            {
                RegValue.DWord(Hive.CurrentUser, sp.Path, sp.Name, 0).WithDWord(baseProtocols | NeededTls),
                RegValue.DWord(Hive.CurrentUser, cr.Path, cr.Name, 0),
                RegValue.DWord(Hive.CurrentUser, st.Path, st.Name, 0).WithDWord(baseState | Settings.PublisherNoRevocationBit),
            };
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
            s.Backup = store.Load();
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

            var backup = store.Load();
            var now = Settings.Touched(reg).ToList();
            if (backup == null)
            {
                progress("Saving your current settings...");
                backup = new Backup { Stage = Stage.Applying, Created = DateTime.UtcNow, User = Environment.UserDomainName + "\\" + Environment.UserName, Originals = now };
                store.Save(backup);
            }
            else
            {
                progress("Your original settings are already saved, keeping them as they are...");
                backup.Stage = Stage.Applying;   // never replace the originals
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

        public Result Disable(Action<string> progress)
        {
            progress = progress ?? (_ => { });
            var backup = store.Load();
            if (backup == null)
                return Result.Fail("There are no saved settings to restore, so nothing was changed. (Registration Mode was not turned on by this tool on this Windows account.)");

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

            store.Retire();
            return new Result { Ok = true, Message = "Registration Mode is now off. Your original Windows Internet Options settings have been restored." };
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
