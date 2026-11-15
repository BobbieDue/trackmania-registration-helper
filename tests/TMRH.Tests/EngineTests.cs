using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Tmrh.Core;
using Xunit;

namespace Tmrh.Tests
{
    sealed class MemoryRegistry : IRegistryStore
    {
        public readonly Dictionary<string, RegValue> Values = new Dictionary<string, RegValue>(StringComparer.OrdinalIgnoreCase);
        public int Writes;
        static string K(Hive h, string p, string n) { return h + "|" + p + "|" + n; }
        public RegValue Read(Hive h, string p, string n) { RegValue v; return Values.TryGetValue(K(h, p, n), out v) ? v : RegValue.Absent(h, p, n); }
        public void Write(RegValue v) { if (v.Hive != Hive.CurrentUser) throw new InvalidOperationException("HKLM write"); Writes++; Values[K(v.Hive, v.Path, v.Name)] = v; }
        public void Delete(Hive h, string p, string n) { if (h != Hive.CurrentUser) throw new InvalidOperationException("HKLM delete"); Writes++; Values.Remove(K(h, p, n)); }
        public string[] ValueNames(Hive h, string p) { return Values.Values.Where(v => v.Hive == h && v.Path == p).Select(v => v.Name).ToArray(); }
        public void Set(RegValue v) { Values[K(v.Hive, v.Path, v.Name)] = v; }
        public Dictionary<string, RegValue> Copy() { return new Dictionary<string, RegValue>(Values, StringComparer.OrdinalIgnoreCase); }
    }

    public class EngineTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "tmufreg-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        static RegValue Dw(string path, string name, uint v) { return RegValue.DWord(Hive.CurrentUser, path, name, 0).WithDWord(v); }
        const string IS = Settings.InternetSettings, SP = Settings.SoftwarePublishing;

        Engine Make(MemoryRegistry r, int build = 22631) { return new Engine(r, new BackupStore(dir), null, () => build); }

        static void AssertSame(Dictionary<string, RegValue> a, Dictionary<string, RegValue> b)
        {
            Assert.Equal(a.Keys.OrderBy(x => x), b.Keys.OrderBy(x => x));
            foreach (var k in a.Keys) Assert.True(a[k].SameAs(b[k]), k + ": " + a[k].Describe() + " vs " + b[k].Describe());
        }

        MemoryRegistry Typical()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2800));            // Windows 11: TLS 1.2 + 1.3
            r.Set(Dw(IS, "CertificateRevocation", 1));
            r.Set(Dw(SP, "State", 0x23C00));
            r.Set(new RegValue(Hive.CurrentUser, IS, "User Agent", RegKind.String, "unrelated"));   // must never change
            return r;
        }

        [Fact]
        public void EnableThenDisableRestoresExactly()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.Equal(Mode.Off, e.GetStatus().Mode);
            Assert.True(e.Enable(null).Ok);
            Assert.Equal(Mode.On, e.GetStatus().Mode);
            Assert.Equal(0x2800u | 0x80 | 0x200 | 0x800 | 0x2000, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Equal(0u, r.Read(Hive.CurrentUser, IS, "CertificateRevocation").AsDWord);
            Assert.Equal(0x23E00u, r.Read(Hive.CurrentUser, SP, "State").AsDWord);
            Assert.True(e.Disable(null).Ok);
            AssertSame(before, r.Values);
            Assert.Equal(Mode.Off, e.GetStatus().Mode);
        }

        [Fact]
        public void ValuesThatWereNotSetAreRemovedAgain()
        {
            var r = new MemoryRegistry(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            Assert.True(r.Read(Hive.CurrentUser, SP, "State").Exists);
            Assert.True(e.Disable(null).Ok);
            Assert.Empty(r.Values);
        }

        [Fact]
        public void OriginalTypeIsKeptOnRestore()
        {
            var r = Typical();
            r.Set(new RegValue(Hive.CurrentUser, IS, "CertificateRevocation", RegKind.String, "1"));   // odd but possible
            var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            Assert.True(e.Disable(null).Ok);
            AssertSame(before, r.Values);
        }

        [Fact]
        public void EnableTwiceNeverReplacesTheOriginals()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            r.Set(Dw(IS, "CertificateRevocation", 1));   // something else changed it back
            Assert.Equal(Mode.ChangedSince, e.GetStatus().Mode);
            Assert.True(e.Enable(null).Ok);
            Assert.True(e.Disable(null).Ok);
            AssertSame(before, r.Values);
        }

        [Fact]
        public void CrashWhileEnablingCanBeUndone()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            e.StepHook = s => { if (s == "wrote SecureProtocols") throw new IOException("power cut"); };
            Assert.Throws<IOException>(() => e.Enable(null));
            e.StepHook = null;
            var e2 = Make(r);   // the app opened again
            Assert.Equal(Mode.Interrupted, e2.GetStatus().Mode);
            Assert.True(e2.Disable(null).Ok);
            AssertSame(before, r.Values);
        }

        [Fact]
        public void CrashWhileRestoringCanBeFinished()
        {
            var r = Typical(); var before = r.Copy(); var e = Make(r);
            Assert.True(e.Enable(null).Ok);
            e.StepHook = s => { if (s == "restored SecureProtocols") throw new IOException("crash"); };
            Assert.Throws<IOException>(() => e.Disable(null));
            var e2 = Make(r);
            Assert.Equal(Mode.Interrupted, e2.GetStatus().Mode);
            Assert.True(e2.Disable(null).Ok);
            AssertSame(before, r.Values);
        }

        [Fact]
        public void DisableWithoutBackupChangesNothing()
        {
            var r = Typical(); r.Set(Dw(IS, "CertificateRevocation", 0)); var before = r.Copy(); var e = Make(r);
            var res = e.Disable(null);
            Assert.False(res.Ok);
            Assert.Equal(0, r.Writes);
            AssertSame(before, r.Values);
        }

        [Fact]
        public void PolicyBlocksEnableWithoutChangingAnything()
        {
            var r = Typical();
            r.Set(RegValue.DWord(Hive.LocalMachine, Settings.PolicyInternetSettings, "SecureProtocols", 0xA80));
            var e = Make(r);
            Assert.Equal(Mode.Managed, e.GetStatus().Mode);
            Assert.False(e.Enable(null).Ok);
            Assert.Equal(0, r.Writes);
            Assert.Null(new BackupStore(dir).Load());
        }

        [Fact]
        public void Tls13OnlyWhereWindowsSupportsIt()
        {
            var r10 = Typical(); r10.Set(Dw(IS, "SecureProtocols", 0xA80));
            var e10 = Make(r10, 19045);   // Windows 10 22H2
            var res = e10.Enable(null);
            Assert.True(res.Ok);
            Assert.Equal(0xA80u, r10.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Contains(res.Details, d => d.Contains("TLS 1.3"));
            var r11 = Typical(); r11.Set(Dw(IS, "SecureProtocols", 0xA80));
            Assert.True(Make(r11, 22631).Enable(null).Ok);
            Assert.Equal(0x2A80u, r11.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
        }

        [Fact]
        public void OnlyTheNeededBitsAreAdded()
        {
            var r = Typical(); r.Set(Dw(IS, "SecureProtocols", 0x20 | 0x800));   // SSL 3 stays as the player had it
            Assert.True(Make(r, 19045).Enable(null).Ok);
            Assert.Equal(0x20u | 0x80 | 0x200 | 0x800, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
            Assert.Equal("unrelated", (string)r.Read(Hive.CurrentUser, IS, "User Agent").Data);
        }

        [Fact]
        public void MachineDefaultIsUsedWhenTheUserHasNone()
        {
            var r = new MemoryRegistry();
            r.Set(RegValue.DWord(Hive.LocalMachine, IS, "SecureProtocols", 0).WithDWord(0x2800));
            Assert.True(Make(r).Enable(null).Ok);
            Assert.Equal(0x2A80u, r.Read(Hive.CurrentUser, IS, "SecureProtocols").AsDWord);
        }

        [Fact]
        public void AlreadyFineNeedsNothing()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0x2A80)); r.Set(Dw(IS, "CertificateRevocation", 0)); r.Set(Dw(SP, "State", 0x23E00));
            var e = Make(r);
            Assert.Equal(Mode.NotNeeded, e.GetStatus().Mode);
        }

        [Fact]
        public void BackupFileRoundTripsEveryKind()
        {
            // Every value type round-trips exactly. Since v1.2.0 a saved file may only name the three managed settings,
            // so each type is carried by one of them in turn (arbitrary names are now rejected: see BackupSafetyTests).
            var kinds = new[]
            {
                RegValue.Absent(Hive.CurrentUser, IS, "SecureProtocols"),
                Dw(IS, "SecureProtocols", 0xFFFFFFFF),
                new RegValue(Hive.CurrentUser, IS, "SecureProtocols", RegKind.QWord, -5L),
                new RegValue(Hive.CurrentUser, IS, "SecureProtocols", RegKind.String, "Ünset value"),
                new RegValue(Hive.CurrentUser, IS, "SecureProtocols", RegKind.ExpandString, "%TEMP%\\a\tb"),
                new RegValue(Hive.CurrentUser, IS, "SecureProtocols", RegKind.MultiString, new[] { "a", "", "c,d" }),
                new RegValue(Hive.CurrentUser, IS, "SecureProtocols", RegKind.Binary, new byte[] { 0, 1, 255 }),
            };
            var store = new BackupStore(dir);
            foreach (var k in kinds)
            {
                var b = new Backup { Stage = Stage.Applied, Created = DateTime.UtcNow, User = "PC\\Jöbbie" };
                b.Originals.Add(k);
                b.Originals.Add(new RegValue(Hive.CurrentUser, IS, "CertificateRevocation", RegKind.String, "1"));
                b.Originals.Add(RegValue.Absent(Hive.CurrentUser, SP, "State"));
                store.Save(b);
                var l = store.Load();
                Assert.Equal(Stage.Applied, l.Stage);
                Assert.Equal(b.User, l.User);
                for (int i = 0; i < b.Originals.Count; i++) Assert.True(b.Originals[i].SameAs(l.Originals[i]), k.Kind + " " + b.Originals[i].Name);
            }
        }

        [Fact]
        public void HalfWrittenBackupIsNeverTrusted()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(new BackupStore(dir).FilePath, "version=1\nstage=Applied\n");
            Assert.Throws<InvalidDataException>(() => new BackupStore(dir).Load());
        }
    }
}
