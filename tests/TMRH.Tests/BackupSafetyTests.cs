using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Tmrh.Core;
using Xunit;

namespace Tmrh.Tests
{
    /// <summary>
    /// v1.2.0 safety work (independent audit): the saved file is checked in full before anything is restored, changes made
    /// while Registration Mode is on need confirmation, one copy at a time, and archiving never fails a good restore.
    /// </summary>
    public class BackupSafetyTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "tmrh-safety-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            try { foreach (var f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal); Directory.Delete(dir, true); } catch { }
        }

        const string IS = Settings.InternetSettings, SP = Settings.SoftwarePublishing;
        static RegValue Dw(string path, string name, uint v) { return RegValue.DWord(Hive.CurrentUser, path, name, 0).WithDWord(v); }
        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        static string Val(string hive, string path, string name, string kind, string data) { return "value=" + hive + "\t" + B64(path) + "\t" + B64(name) + "\t" + kind + "\t" + data; }
        static string SpLine(int v = 0x2800) { return Val("CurrentUser", IS, "SecureProtocols", "DWord", v.ToString()); }
        static string CrLine(int v = 1) { return Val("CurrentUser", IS, "CertificateRevocation", "DWord", v.ToString()); }
        static string StLine(int v = 0x23C00) { return Val("CurrentUser", SP, "State", "DWord", v.ToString()); }

        static string File_(string version, string stage, params string[] values)
        {
            var l = new List<string> { "# test", "version=" + version, "stage=" + stage, "created=2026-11-15T19:00:00.0000000Z", "user=PC\\player" };
            l.AddRange(values); l.Add("end=1");
            return string.Join("\r\n", l) + "\r\n";
        }

        BackupStore Store { get { return new BackupStore(dir); } }
        Engine Make(MemoryRegistry r, int build = 22631) { return new Engine(r, Store, null, () => build); }
        void WriteRaw(string text) { Directory.CreateDirectory(dir); File.WriteAllText(Store.FilePath, text, new UTF8Encoding(false)); }

        /// <summary>The registry as Registration Mode leaves it, with an unrelated value that must never change.</summary>
        static MemoryRegistry RegistrationOn()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2A80)); r.Set(Dw(IS, "CertificateRevocation", 0)); r.Set(Dw(SP, "State", 0x23E00));
            r.Set(new RegValue(Hive.CurrentUser, IS, "User Agent", RegKind.String, "unrelated"));
            return r;
        }

        // ---------------------------------------------------------------- valid files

        [Fact]
        public void V1WithTwoEntriesIsAcceptedAndRestored()
        {
            var r = RegistrationOn();
            WriteRaw(File_("1", "Applied", SpLine(0xA80), CrLine(1)));
            var e = Make(r, 19045);
            Assert.NotEqual(Mode.BadBackup, e.GetStatus().Mode);
            var res = e.Disable(null, true);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(0xA80u, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Equal(1u, r.Read(Hive.CurrentUser, IS, "CertificateRevocation").AsDWord);
            Assert.Equal(0x23E00u, r.Read(Hive.CurrentUser, SP, "State").AsDWord);   // a two-entry file never managed State
        }

        [Fact]
        public void V1WithThreeEntriesIsAcceptedAndRestored()
        {
            var r = RegistrationOn();
            WriteRaw(File_("1", "Applied", SpLine(), CrLine(), StLine()));
            var res = Make(r).Disable(null, true);
            Assert.True(res.Ok, res.Message);
            Assert.Equal(0x2800u, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Equal(0x23C00u, r.Read(Hive.CurrentUser, SP, "State").AsDWord);
        }

        [Fact]
        public void V2WithAllThreeEntriesIsAccepted()
        {
            var r = RegistrationOn();
            WriteRaw(File_("2", "Applied", SpLine(), CrLine(), StLine()));
            Assert.True(Make(r).Disable(null, true).Ok);
        }

        [Fact]
        public void NewBackupsAreVersion2WithAllThree()
        {
            var r = new MemoryRegistry(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            var text = File.ReadAllText(Store.FilePath);
            Assert.Contains("version=2", text);
            Assert.Equal(3, text.Split('\n').Count(l => l.StartsWith("value=")));
            Assert.Equal(2, Store.Load().Version);
        }

        [Fact]
        public void AVersion1FileResavedByEnableStaysValid()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0xA80)); r.Set(Dw(IS, "CertificateRevocation", 1)); r.Set(Dw(SP, "State", 0x23C00));
            WriteRaw(File_("1", "Applied", SpLine(0xA80), CrLine(1)));   // left on by an old test build, settings since reset
            var e = Make(r, 19045);
            Assert.True(e.Enable(null).Ok);
            Assert.Equal(1, Store.Load().Version);
            Assert.Equal(2, Store.Load().Originals.Count);
            Assert.True(e.Disable(null, true).Ok);
        }

        // ---------------------------------------------------------------- rejected files

        public static IEnumerable<object[]> BadFiles()
        {
            Func<string, object[]> c = t => new object[] { t };
            yield return c(File_("2", "Applied", SpLine(), CrLine()));                                    // v2 with only two entries
            yield return c(File_("1", "Applied", SpLine()));                                              // missing entry
            yield return c(File_("1", "Applied", SpLine(), StLine()));                                    // v1 two entries but not the legacy pair
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine(), CrLine()));                // duplicate
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine(), Val("CurrentUser", @"Software\Microsoft\Windows\CurrentVersion\Run", "Evil", "String", B64("C:\\evil.exe"))));
            yield return c(File_("2", "Applied", Val("CurrentUser", @"Software\Microsoft\Windows\CurrentVersion\Run", "SecureProtocols", "DWord", "1"), CrLine(), StLine()));  // right name, wrong path
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "ProxyServer", "String", B64("evil:8080")), CrLine(), StLine()));                            // right path, wrong name
            yield return c(File_("2", "Applied", Val("LocalMachine", IS, "SecureProtocols", "DWord", "2048"), CrLine(), StLine()));                                 // other hive
            yield return c(File_("2", "Applied", Val("Users", IS, "SecureProtocols", "DWord", "2048"), CrLine(), StLine()));                                        // unknown hive
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "Link", "2048"), CrLine(), StLine()));                                   // unknown type
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "1", "2048"), CrLine(), StLine()));                                      // type as a number
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "dword", "2048"), CrLine(), StLine()));                                  // wrong case
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "DWord", "abc"), CrLine(), StLine()));                                   // bad number
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "DWord", "99999999999"), CrLine(), StLine()));                           // out of range
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "Absent", "5"), CrLine(), StLine()));                                    // absent with data
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "String", B64(new string('x', 2000))), CrLine(), StLine()));             // oversized
            yield return c(File_("2", "Applied", Val("CurrentUser", IS, "SecureProtocols", "Binary", Convert.ToBase64String(new byte[5000])), CrLine(), StLine())); // oversized
            yield return c(File_("2", "Applied", "value=CurrentUser\t!!notbase64!!\t" + B64("SecureProtocols") + "\tDWord\t1", CrLine(), StLine()));                // damaged location
            yield return c(File_("2", "Applied", "value=CurrentUser\t" + B64(IS) + "\tDWord\t1", CrLine(), StLine()));                                              // wrong field count
            yield return c(File_("3", "Applied", SpLine(), CrLine(), StLine()));                                                                                    // unsupported version
            yield return c(File_("1.0", "Applied", SpLine(), CrLine(), StLine()));
            yield return c(File_("2", "Done", SpLine(), CrLine(), StLine()));                                                                                       // unknown stage
            yield return c(File_("2", "5", SpLine(), CrLine(), StLine()));                                                                                          // stage as a number
            yield return c(File_("2", "applied", SpLine(), CrLine(), StLine()));
            yield return c(File_("1", "Restored", SpLine(), CrLine(), StLine()));                                                                                   // Restored only exists in v2
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("created=2026-11-15T19:00:00.0000000Z", "created=yesterday"));              // bad date
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("user=PC\\player", "user="));                                               // empty user
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("user=PC\\player", "user=PC\\pl\u0007ayer"));                               // control character
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("version=2\r\n", ""));                                                        // no version
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("stage=Applied\r\n", ""));                                                    // no stage
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("end=1\r\n", ""));                                                            // cut off
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()) + "value=whatever\r\n");                                                             // after the end
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("stage=Applied", "stage=Applied\r\nstage=Applying"));                        // duplicate metadata
            yield return c(File_("2", "Applied", SpLine(), CrLine(), StLine()).Replace("stage=Applied", "stage=Applied\r\nhive=LocalMachine"));                     // unexpected entry
            yield return c("this is not a backup");
            yield return c("");
        }

        [Theory]
        [MemberData(nameof(BadFiles))]
        public void BadFilesAreRejectedBeforeAnyRegistryChangeAndKept(string text)
        {
            var r = RegistrationOn(); var before = r.Copy();
            WriteRaw(text);
            var bytes = File.ReadAllBytes(Store.FilePath);
            var e = Make(r);
            var st = e.GetStatus();
            Assert.Equal(Mode.BadBackup, st.Mode);
            Assert.NotEmpty(st.BackupProblems);
            var d = e.Disable(null, true);   // even with "overwrite changes" nothing may be written
            Assert.False(d.Ok);
            Assert.NotEmpty(d.Details);
            Assert.False(e.Enable(null).Ok);   // and Enable won't replace the file
            Assert.Equal(0, r.Writes);
            Assert.Equal(before.Count, r.Values.Count);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
            Assert.Equal(bytes, File.ReadAllBytes(Store.FilePath));   // the file is preserved exactly
        }

        [Fact]
        public void OversizedFileIsRejected()
        {
            var r = RegistrationOn();
            WriteRaw(File_("2", "Applied", SpLine(), CrLine(), StLine()) + "#" + new string('x', 70000) + "\r\n");
            Assert.Equal(Mode.BadBackup, Make(r).GetStatus().Mode);
            Assert.False(Make(r).Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);
        }

        [Fact]
        public void ResetToSecureSettingsIsSeparateAndLeavesTheFileAlone()
        {
            var r = RegistrationOn();
            WriteRaw(File_("2", "Applied", SpLine(), CrLine()));   // invalid
            var bytes = File.ReadAllBytes(Store.FilePath);
            var e = Make(r);
            Assert.False(e.Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);                               // nothing happens on its own
            var res = e.ResetToSecureSettings();                      // only when explicitly chosen
            Assert.True(res.Ok, res.Message);
            Assert.Equal(0x2800u, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);   // TLS 1.0/1.1 off, 1.2 + 1.3 on
            Assert.Equal(1u, r.Read(Hive.CurrentUser, IS, "CertificateRevocation").AsDWord);
            Assert.Equal(0x23C00u, r.Read(Hive.CurrentUser, SP, "State").AsDWord);
            Assert.Equal("unrelated", (string)r.Read(Hive.CurrentUser, IS, "User Agent").Data);
            Assert.Equal(bytes, File.ReadAllBytes(Store.FilePath));
        }

        [Fact]
        public void SetAsideKeepsTheDamagedFile()
        {
            WriteRaw("garbage");
            var moved = Store.SetAside();
            Assert.False(File.Exists(Store.FilePath));
            Assert.Equal("garbage", File.ReadAllText(moved));
            Assert.StartsWith("damaged-", Path.GetFileName(moved));
            Assert.Null(Store.Load());
        }

        // ---------------------------------------------------------------- policy detection

        [Theory]
        [InlineData(Hive.LocalMachine, "SecureProtocols")]
        [InlineData(Hive.CurrentUser, "SecureProtocols")]
        [InlineData(Hive.LocalMachine, "CertificateRevocation")]
        [InlineData(Hive.CurrentUser, "CertificateRevocation")]
        public void EachPolicyControlledSettingBlocksChanges(Hive hive, string name)
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2800)); r.Set(Dw(IS, "CertificateRevocation", 1)); r.Set(Dw(SP, "State", 0x23C00));
            r.Set(RegValue.DWord(hive, Settings.PolicyInternetSettings, name, 1));
            var e = Make(r);
            Assert.Equal(Mode.Managed, e.GetStatus().Mode);
            Assert.False(e.Enable(null).Ok);
            Assert.False(e.ResetToSecureSettings().Ok);
            Assert.Equal(0, r.Writes);
            Assert.Null(Store.Load());
        }

        [Fact]
        public void PublisherRevocationHasNoPolicyKeyToDetect()
        {
            // Microsoft's custom ADM template for this box writes the user's own State value directly (146432 = ticked),
            // the same value TMRH manages, so there is no separate policy location. Documented in the README.
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2800)); r.Set(Dw(IS, "CertificateRevocation", 1)); r.Set(Dw(SP, "State", 146432));
            Assert.Empty(Settings.Managed(r));
            Assert.Equal(0x23C00u, 146432u);
            Assert.Equal(0x23E00u, 146944u);   // the template's "disabled" value: exactly bit 0x200 added, as TMRH does
        }

        // ---------------------------------------------------------------- changes made while Registration Mode is on

        MemoryRegistry Typical()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2800)); r.Set(Dw(IS, "CertificateRevocation", 1)); r.Set(Dw(SP, "State", 0x23C00));
            return r;
        }

        [Fact]
        public void ChangedSettingsNeedConfirmationAndDecliningChangesNothing()
        {
            var r = Typical(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            r.Set(Dw(IS, "CertificateRevocation", 1));   // the player ticked it again
            var bytes = File.ReadAllBytes(Store.FilePath); int w = r.Writes; var now = r.Copy();
            var res = e.Disable(null);
            Assert.False(res.Ok);
            var c = Assert.Single(res.NeedsConfirmation);
            Assert.Equal("server certificate revocation checking", c.Setting);
            Assert.Equal(0u, c.Expected.AsDWord);
            Assert.Equal(1u, c.Current.AsDWord);
            // declined: registry and saved copy untouched
            Assert.Equal(w, r.Writes);
            foreach (var k in now.Keys) Assert.True(now[k].SameAs(r.Values[k]), k);
            Assert.Equal(bytes, File.ReadAllBytes(Store.FilePath));
            Assert.Equal(Stage.Applied, Store.Load().Stage);
        }

        [Fact]
        public void ConfirmingRestoresTheOriginalsExactly()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            r.Set(Dw(IS, "SecureProtocols", 0x2A88));   // something added a bit
            r.Set(Dw(SP, "State", 0x23C00));
            Assert.Equal(2, e.Disable(null).NeedsConfirmation.Count);
            var res = e.Disable(null, true);
            Assert.True(res.Ok, res.Message);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
        }

        [Fact]
        public void NoChangesMeansNoQuestion()
        {
            var r = Typical(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            var res = e.Disable(null);
            Assert.True(res.Ok);
            Assert.Empty(res.NeedsConfirmation);
        }

        // ---------------------------------------------------------------- interrupted restore

        [Fact]
        public void InterruptedRestoreOfAV1TwoEntryFileCanBeFinished()
        {
            var r = RegistrationOn();
            WriteRaw(File_("1", "Restoring", SpLine(0xA80), CrLine(1)));
            var e = Make(r, 19045);
            Assert.Equal(Mode.Interrupted, e.GetStatus().Mode);
            var res = e.Disable(null);   // no confirmation for an unfinished change: values may be half-way
            Assert.True(res.Ok, res.Message);
            Assert.Equal(0xA80u, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Equal(Mode.Off, e.GetStatus().Mode);
            Assert.False(File.Exists(Store.FilePath));
        }

        [Fact]
        public void CrashMidRestoreThenRecoveryRestoresEverything()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            e.StepHook = s => { if (s == "restored CertificateRevocation") throw new IOException("power cut"); };
            Assert.Throws<IOException>(() => e.Disable(null));
            var e2 = Make(r);
            Assert.Equal(Mode.Interrupted, e2.GetStatus().Mode);
            Assert.True(e2.Disable(null).Ok);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
        }

        // ---------------------------------------------------------------- archiving after a good restore

        [Fact]
        public void RetiredNamesNeverCollide()
        {
            Directory.CreateDirectory(dir);
            var t = new DateTime(2026, 11, 15, 19, 30, 0, 123);
            var a = Store.UniqueRetiredName(t); File.WriteAllText(a, "1");
            var b = Store.UniqueRetiredName(t); File.WriteAllText(b, "2");
            var c = Store.UniqueRetiredName(t);
            Assert.EndsWith("restored-20261115-193000-123.txt", a);
            Assert.EndsWith("-2.txt", b);
            Assert.EndsWith("-3.txt", c);
        }

        [Fact]
        public void TwoRestoresInQuickSuccessionBothSucceed()
        {
            var r = Typical(); var e = Make(r);
            for (int i = 0; i < 3; i++) { Assert.True(e.Enable(null).Ok); var d = e.Disable(null); Assert.True(d.Ok); Assert.Empty(d.Housekeeping); }
            Assert.Equal(3, Directory.GetFiles(dir, "restored-*.txt").Length);
        }

        [Fact]
        public void CleanupFailureAfterRestoreIsReportedSeparately()
        {
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 6; i++) File.WriteAllText(Path.Combine(dir, "restored-2000010" + i + "-000000-000.txt"), "old");
            var locked = Path.Combine(dir, "restored-20000100-000000-000.txt");   // the oldest: the one cleanup deletes
            File.SetAttributes(locked, FileAttributes.ReadOnly);
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            var res = e.Disable(null);
            Assert.True(res.Ok, res.Message);                           // the restore itself succeeded
            Assert.Contains(res.Housekeeping, h => h.Contains("tidied"));
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
            Assert.Equal(Mode.Off, e.GetStatus().Mode);
        }

        [Fact]
        public void ArchiveFailureAfterRestoreStillReportsSuccessAndStaysUnderstandable()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            FileStream hold = null;   // something (an antivirus scan, say) opens the file just as the restore finishes
            e.StepHook = s => { if (s == "restored State") hold = new FileStream(Store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read); };
            Result res;
            try { res = e.Disable(null); } finally { e.StepHook = null; if (hold != null) hold.Dispose(); }
            Assert.NotNull(hold);
            Assert.True(res.Ok, res.Message);
            Assert.NotEmpty(res.Housekeeping);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
            // afterwards: either nothing left to do, or the "didn't finish" state where Disable again is harmless
            var m = e.GetStatus().Mode;
            Assert.True(m == Mode.Off || m == Mode.Interrupted, m.ToString());
            if (m == Mode.Interrupted) Assert.True(e.Disable(null).Ok);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
        }

        [Fact]
        public void ARestoredMarkerIsArchivedAndNeverRestoredAgain()
        {
            var r = Typical();
            WriteRaw(File_("2", "Restored", SpLine(0x80), CrLine(0), StLine(0)));   // values that must never be written
            var e = Make(r);
            Assert.Equal(Mode.Off, e.GetStatus().Mode);
            Assert.False(File.Exists(Store.FilePath));
            Assert.False(e.Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);
        }

        // ---------------------------------------------------------------- one copy at a time

        static InstanceLock OnThread(string name, out Thread t, ManualResetEventSlim release)
        {
            InstanceLock got = null; var ready = new ManualResetEventSlim();
            t = new Thread(() => { got = InstanceLock.TryAcquire(name); ready.Set(); release.Wait(); got.Dispose(); });
            t.Start(); ready.Wait();
            return got;
        }

        [Fact]
        public void SecondCopyCannotTakeTheLock()
        {
            string name = @"Local\TMRH-test-" + Guid.NewGuid().ToString("N");
            var release = new ManualResetEventSlim(); Thread t;
            var first = OnThread(name, out t, release);
            Assert.True(first.Acquired);
            using (var second = InstanceLock.TryAcquire(name)) Assert.False(second.Acquired);
            release.Set(); t.Join();
            using (var third = InstanceLock.TryAcquire(name)) Assert.True(third.Acquired);   // free again after a normal exit
        }

        [Fact]
        public void AbandonedLockIsTakenOverSafely()
        {
            string name = @"Local\TMRH-test-" + Guid.NewGuid().ToString("N");
            var t = new Thread(() => { var m = new Mutex(false, name); m.WaitOne(); });   // "crashes" without releasing
            t.Start(); t.Join();
            using (var l = InstanceLock.TryAcquire(name))
            {
                Assert.True(l.Acquired);
                Assert.True(l.WasAbandoned);
            }
            using (var again = InstanceLock.TryAcquire(name)) Assert.True(again.Acquired);
        }
    }
}
