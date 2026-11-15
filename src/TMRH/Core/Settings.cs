using System.Collections.Generic;

namespace Tmrh.Core
{
    /// <summary>
    /// Exactly which Internet Options boxes map to which registry values. All of them are the current user's own
    /// settings (HKEY_CURRENT_USER), which is why the tool never needs administrator rights.
    /// The "Settings check" window lets anyone confirm these on their own PC by ticking a box and comparing.
    /// </summary>
    public static class Settings
    {
        public const string InternetSettings = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        public const string SoftwarePublishing = @"Software\Microsoft\Windows\CurrentVersion\WinTrust\Trust Providers\Software Publishing";
        public const string PolicyInternetSettings = @"Software\Policies\Microsoft\Windows\CurrentVersion\Internet Settings";

        // Advanced > Security > "Use TLS 1.x": one bit each in SecureProtocols.
        public const string SecureProtocols = "SecureProtocols";
        public const uint Tls10 = 0x00000080, Tls11 = 0x00000200, Tls12 = 0x00000800, Tls13 = 0x00002000;

        // Advanced > Security > "Check for server certificate revocation": 1 = ticked, 0 = unticked, not set = ticked.
        public const string CertificateRevocation = "CertificateRevocation";

        // Advanced > Security > "Check for publisher's certificate revocation": bit 0x200 set = unticked.
        // Windows' own value when the box is ticked is 0x23C00 (used only if the value isn't there yet).
        public const string State = "State";
        public const uint PublisherNoRevocationBit = 0x00000200;
        public const uint PublisherDefaultState = 0x00023C00;

        /// <summary>The three values the tool may change, and nothing else.</summary>
        public static IEnumerable<RegValue> Touched(IRegistryStore r)
        {
            yield return r.Read(Hive.CurrentUser, InternetSettings, SecureProtocols);
            yield return r.Read(Hive.CurrentUser, InternetSettings, CertificateRevocation);
            yield return r.Read(Hive.CurrentUser, SoftwarePublishing, State);
        }

        /// <summary>Policy values that override the user's own settings (Group Policy or a management tool).</summary>
        public static List<string> Managed(IRegistryStore r)
        {
            var found = new List<string>();
            foreach (var hive in new[] { Hive.LocalMachine, Hive.CurrentUser })
                foreach (var name in new[] { SecureProtocols, CertificateRevocation })
                {
                    var v = r.Read(hive, PolicyInternetSettings, name);
                    if (v.Exists) found.Add(v.Key);
                }
            return found;
        }
    }
}
