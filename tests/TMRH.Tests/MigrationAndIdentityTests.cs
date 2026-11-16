using System;
using System.IO;
using System.Linq;
using System.Text;
using Tmrh.Core;
using Xunit;

namespace Tmrh.Tests
{
    /// <summary>v1.2.1: moving an older test build's saved copy, the size limit before reading, and the account (SID) check.</summary>
    public class MigrationAndIdentityTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "tmrh-mig-" + Guid.NewGuid().ToString("N"));
        string NewDir { get { return Path.Combine(root, "We Are Crazy", "TrackMania Registration Helper"); } }
        string OldFile { get { return Path.Combine(root, "We Are Crazy", "TMUF Registration Mode", "original-settings.txt"); } }
        public void Dispose() { try { Directory.Delete(root, true); } catch { } }

        const string IS = Settings.InternetSettings, SP = Settings.SoftwarePublishing;
        const string Me = "S-1-5-21-1111111111-2222222222-3333333333-1001", Other = "S-1-5-21-1111111111-2222222222-3333333333-1002";
        static RegValue Dw(string path, string name, uint v) { return RegValue.DWord(Hive.CurrentUser, path, name, 0).WithDWord(v); }
        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        static string Val(string path, string name, int v) { return "value=CurrentUser\t" + B64(path) + "\t" + B64(name) + "\tDWord\t" + v; }
        static string V1TwoEntries() { return "version=1\r\nstage=Applied\r\ncreated=2026-01-01T00:00:00.0000000Z\r\nuser=PC\\player\r\n" + Val(IS, "SecureProtocols", 0xA80) + "\r\n" + Val(IS, "CertificateRevocation", 1) + "\r\nend=1\r\n"; }
        static string V2(string extra) { return "version=2\r\nstage=Applied\r\ncreated=2026-01-01T00:00:00.0000000Z\r\nuser=PC\\player\r\n" + extra + Val(IS, "SecureProtocols", 0x2800) + "\r\n" + Val(IS, "CertificateRevocation", 1) + "\r\n" + Val(SP, "State", 0x23C00) + "\r\nend=1\r\n"; }

        BackupStore Store(string sid = Me) { return new BackupStore(NewDir, OldFile, () => sid); }
        Engine Make(MemoryRegistry r, BackupStore s) { return new Engine(r, s, null, () => 19045); }

        static MemoryRegistry RegistrationOn()
        {
            var r = new MemoryRegistry();
            r.Set(Dw(IS, "SecureProtocols", 0xA80)); r.Set(Dw(IS, "CertificateRevocation", 0)); r.Set(Dw(SP, "State", 0x23E00));
            return r;
        }

        void WriteOld(string text) { Directory.CreateDirectory(Path.GetDirectoryName(OldFile)); File.WriteAllText(OldFile, text, new UTF8Encoding(false)); }

        // ---------------------------------------------------------------- migration

        [Fact]
        public void OldCopyIsMovedAndUsed()
        {
            WriteOld(V1TwoEntries()); var bytes = File.ReadAllBytes(OldFile);
            var r = RegistrationOn(); var e = Make(r, Store());
            Assert.Equal(Mode.On, e.GetStatus().Mode);
            Assert.False(File.Exists(OldFile));
            Assert.Equal(bytes, File.ReadAllBytes(Store().FilePath));
            var d = e.Disable(null, true);
            Assert.True(d.Ok, d.Message);
            Assert.Equal(1u, r.Read(Hive.CurrentUser, IS, "CertificateRevocation").AsDWord);   // the genuine original
        }

        [Fact]
        public void ExistingNewCopyWinsAndOldIsLeftAlone()
        {
            WriteOld(V1TwoEntries()); var old = File.ReadAllBytes(OldFile);
            Directory.CreateDirectory(NewDir); File.WriteAllText(Store().FilePath, V2("sid=" + Me + "\r\n"), new UTF8Encoding(false));
            var b = Store().Load();
            Assert.Equal(2, b.Version);
            Assert.True(File.Exists(OldFile));
            Assert.Equal(old, File.ReadAllBytes(OldFile));
        }

        void AssertStuckAndNothingChanged(MemoryRegistry r, Engine e, byte[] oldBytes)
        {
            var st = e.GetStatus();
            Assert.Equal(Mode.LegacyStuck, st.Mode);
            Assert.Contains(st.BackupProblems, p => p.Contains(OldFile));
            var en = e.Enable(null);
            Assert.False(en.Ok);
            Assert.Contains(en.Details, p => p.Contains(OldFile));
            Assert.False(e.Disable(null, true).Ok);
            Assert.False(Store().FilePath != null && File.Exists(Store().FilePath));   // no new "originals" were saved
            Assert.Equal(0, r.Writes);
            Assert.Equal(oldBytes, File.ReadAllBytes(OldFile));                        // the old copy is untouched
        }

        [Fact]
        public void LockedOldCopyBlocksEverythingAndIsKept()
        {
            WriteOld(V1TwoEntries()); var bytes = File.ReadAllBytes(OldFile);
            var r = RegistrationOn(); var e = Make(r, Store());
            using (new FileStream(OldFile, FileMode.Open, FileAccess.Read, FileShare.Read))   // e.g. an antivirus scan has it open
                AssertStuckAndNothingChanged(r, e, bytes);
            Assert.Equal(Mode.On, e.GetStatus().Mode);   // once it's free, it moves and works
            Assert.False(File.Exists(OldFile));
        }

        [Fact]
        public void UncreatableNewFolderBlocksEverythingAndOldIsKept()
        {
            WriteOld(V1TwoEntries()); var bytes = File.ReadAllBytes(OldFile);
            Directory.CreateDirectory(Path.GetDirectoryName(NewDir));
            File.WriteAllText(NewDir, "a file where the folder should be");               // the new folder can't be made
            var r = RegistrationOn(); var e = Make(r, Store());
            var st = e.GetStatus();
            Assert.Equal(Mode.LegacyStuck, st.Mode);
            Assert.False(e.Enable(null).Ok);
            Assert.False(e.Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);
            Assert.Equal(bytes, File.ReadAllBytes(OldFile));
        }

        // ---------------------------------------------------------------- size checked before reading

        [Fact]
        public void HugeFileIsRejectedWithoutBeingReadAndIsKept()
        {
            Directory.CreateDirectory(NewDir);
            var f = Store().FilePath;
            using (var fs = new FileStream(f, FileMode.Create)) fs.SetLength(50L * 1024 * 1024);   // 50 MB
            var r = RegistrationOn(); var e = Make(r, Store());
            var st = e.GetStatus();
            Assert.Equal(Mode.BadBackup, st.Mode);
            Assert.Contains(st.BackupProblems, p => p.Contains("far bigger"));
            Assert.False(e.Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);
            Assert.Equal(50L * 1024 * 1024, new FileInfo(f).Length);
        }

        // ---------------------------------------------------------------- Windows account (SID)

        [Fact]
        public void NewBackupsRecordTheAccount()
        {
            var r = new MemoryRegistry(); var e = Make(r, Store());
            Assert.True(e.Enable(null).Ok);
            Assert.Contains("sid=" + Me, File.ReadAllText(Store().FilePath));
            Assert.Equal(Me, Store().Load().Sid);
            Assert.True(e.Disable(null).Ok);
        }

        [Fact]
        public void ABackupFromAnotherAccountIsRefusedAndKept()
        {
            Directory.CreateDirectory(NewDir); File.WriteAllText(Store().FilePath, V2("sid=" + Other + "\r\n"), new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(Store().FilePath);
            var r = RegistrationOn(); var e = Make(r, Store());
            var st = e.GetStatus();
            Assert.Equal(Mode.BadBackup, st.Mode);
            Assert.Contains(st.BackupProblems, p => p.Contains("different Windows account"));
            Assert.False(e.Disable(null, true).Ok);
            Assert.Equal(0, r.Writes);
            Assert.Equal(bytes, File.ReadAllBytes(Store().FilePath));
        }

        [Fact]
        public void OlderFilesWithoutAnAccountStillWork()
        {
            Directory.CreateDirectory(NewDir); File.WriteAllText(Store().FilePath, V2(""), new UTF8Encoding(false));   // written by v1.2.0
            Assert.Null(Store().Load().Sid);
        }

        [Fact]
        public void UnknownAccountIsNotHeldAgainstTheFile()
        {
            Directory.CreateDirectory(NewDir); File.WriteAllText(Store().FilePath, V2("sid=" + Other + "\r\n"), new UTF8Encoding(false));
            Assert.NotNull(Store(null).Load());   // can't tell who's signed in: the account isn't compared
        }

        [Theory]
        [InlineData("sid=not-a-sid\r\n")]
        [InlineData("sid=S-1-5-21-12a4\r\n")]
        [InlineData("sid=S-1-\r\n")]
        public void InvalidAccountIdsAreRejected(string line)
        {
            Directory.CreateDirectory(NewDir); File.WriteAllText(Store().FilePath, V2(line), new UTF8Encoding(false));
            Assert.Throws<InvalidDataException>(() => Store().Load());
        }

        [Fact]
        public void Version1FilesCannotCarryAnAccount()
        {
            Directory.CreateDirectory(NewDir);
            File.WriteAllText(Store().FilePath, V1TwoEntries().Replace("user=PC\\player\r\n", "user=PC\\player\r\nsid=" + Me + "\r\n"), new UTF8Encoding(false));
            Assert.Throws<InvalidDataException>(() => Store().Load());
        }
    
        // ---------------------------------------------------------------- 1.2.2: version and account checked after every line

        static string V1SidFirst(string sid) { return "sid=" + sid + "\r\nversion=1\r\nstage=Applied\r\ncreated=2026-01-01T00:00:00.0000000Z\r\nuser=PC\\player\r\n" + Val(IS, "SecureProtocols", 0xA80) + "\r\n" + Val(IS, "CertificateRevocation", 1) + "\r\nend=1\r\n"; }

        [Fact]
        public void Version1FileWithAccountBeforeVersionIsRejectedWithoutRegistryWrites()
        {
            Directory.CreateDirectory(NewDir);
            File.WriteAllText(Store().FilePath, V1SidFirst(Me), new UTF8Encoding(false));
            var bytes = File.ReadAllBytes(Store().FilePath);
            var bad = new System.Collections.Generic.List<string>();
            Assert.Null(BackupStore.Parse(bytes, bad));
            Assert.Contains(bad, x => x.Contains("Version 1 files don't have an account ID"));
            Assert.Throws<InvalidDataException>(() => Store().Load());
            var r = RegistrationOn(); var before = r.Copy(); var e = Make(r, Store());
            Assert.Equal(Mode.BadBackup, e.GetStatus().Mode);
            Assert.False(e.Disable(null).Ok);
            Assert.False(e.Enable(null).Ok);
            Assert.Equal(0, r.Writes);
            foreach (var k in before.Keys) Assert.True(before[k].SameAs(r.Values[k]), k);
            Assert.Equal(bytes, File.ReadAllBytes(Store().FilePath));   // kept as it was
        }

        [Fact]
        public void Version2FileWithAccountBeforeVersionStillLoads()
        {
            Directory.CreateDirectory(NewDir);
            File.WriteAllText(Store().FilePath, "sid=" + Me + "\r\n" + V2(""), new UTF8Encoding(false));
            Assert.Equal(Me, Store().Load().Sid);
        }

        [Fact]
        public void LegacyFilesWithoutAccountStillLoad()
        {
            Directory.CreateDirectory(NewDir);
            File.WriteAllText(Store().FilePath, V1TwoEntries(), new UTF8Encoding(false));
            Assert.Equal(1, Store().Load().Version);
            File.WriteAllText(Store().FilePath, V2(""), new UTF8Encoding(false));   // a v1.2.0 version 2 file: no account ID
            var b = Store().Load();
            Assert.Equal(2, b.Version);
            Assert.Null(b.Sid);
        }
}
}
