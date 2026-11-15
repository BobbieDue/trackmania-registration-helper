using System;
using System.IO;
using System.Linq;
using Tmrh.Core;
using Xunit;

namespace Tmrh.Tests
{
    /// <summary>
    /// Runs Enable then Disable against the real Windows registry and checks every value comes back exactly.
    /// Only runs when TMUF_E2E=1 (set on the throwaway GitHub build machine), so it never touches a developer's PC.
    /// </summary>
    public class RealRegistryTests : IDisposable
    {
        readonly string dir = Path.Combine(Path.GetTempPath(), "tmufreg-e2e-" + Guid.NewGuid().ToString("N"));
        readonly WindowsRegistryStore reg = new WindowsRegistryStore();
        static bool On { get { return Environment.GetEnvironmentVariable("TMUF_E2E") == "1"; } }
        public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

        int Build() { var v = reg.Read(Hive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber"); int b; return v.Exists && int.TryParse((string)v.Data, out b) ? b : 0; }

        void RoundTrip()
        {
            var before = Settings.Touched(reg).ToList();
            var e = new Engine(reg, new BackupStore(dir), null, Build);
            var on = e.Enable(null);
            Assert.True(on.Ok, on.Message + " " + string.Join("; ", on.Details));
            Assert.Equal(0u, reg.Read(Hive.CurrentUser, Settings.InternetSettings, Settings.CertificateRevocation).AsDWord);
            Assert.Equal(Mode.On, e.GetStatus().Mode);
            var off = e.Disable(null);
            Assert.True(off.Ok, off.Message + " " + string.Join("; ", off.Details));
            var after = Settings.Touched(reg).ToList();
            for (int i = 0; i < before.Count; i++) Assert.True(before[i].SameAs(after[i]), before[i].Key + ": " + before[i].Describe() + " vs " + after[i].Describe());
        }

        [Fact]
        public void EnableDisableOnTheRealRegistryWithExistingValues()
        {
            if (!On) return;
            var saved = Settings.Touched(reg).ToList();
            try
            {
                reg.Write(RegValue.DWord(Hive.CurrentUser, Settings.InternetSettings, Settings.SecureProtocols, 0).WithDWord(0xA80));
                reg.Write(RegValue.DWord(Hive.CurrentUser, Settings.InternetSettings, Settings.CertificateRevocation, 1));
                reg.Write(RegValue.DWord(Hive.CurrentUser, Settings.SoftwarePublishing, Settings.State, 0).WithDWord(0x23C00));
                RoundTrip();
            }
            finally { foreach (var v in saved) reg.Write(v); }
        }

        [Fact]
        public void EnableDisableOnTheRealRegistryWithValuesNotSet()
        {
            if (!On) return;
            var saved = Settings.Touched(reg).ToList();
            try
            {
                foreach (var v in saved) reg.Delete(v.Hive, v.Path, v.Name);
                RoundTrip();
                Assert.All(Settings.Touched(reg), v => Assert.False(v.Exists, v.Key + " should be gone again"));
            }
            finally { foreach (var v in saved) reg.Write(v); }
        }
    }
}
