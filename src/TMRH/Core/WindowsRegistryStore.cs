using System;
using Microsoft.Win32;

namespace Tmrh.Core
{
    /// <summary>The real Windows registry. Only the current user's settings are ever written.</summary>
    public sealed class WindowsRegistryStore : IRegistryStore
    {
        static RegistryKey Root(Hive hive)
        {
            // Always the 64-bit view on 64-bit Windows, so the values match what Internet Options shows.
            var h = hive == Hive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            return RegistryKey.OpenBaseKey(h, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        public RegValue Read(Hive hive, string path, string name)
        {
            using (var root = Root(hive))
            using (var key = root.OpenSubKey(path, false))
            {
                if (key == null) return RegValue.Absent(hive, path, name);
                object data = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (data == null) return RegValue.Absent(hive, path, name);
                return new RegValue(hive, path, name, FromWin(key.GetValueKind(name)), data);
            }
        }

        public void Write(RegValue value)
        {
            if (value.Hive != Hive.CurrentUser) throw new InvalidOperationException("This tool only changes your own (current user) settings.");
            if (!value.Exists) { Delete(value.Hive, value.Path, value.Name); return; }
            using (var root = Root(value.Hive))
            using (var key = root.CreateSubKey(value.Path, true))
            {
                key.SetValue(value.Name, value.Data, ToWin(value.Kind));
                key.Flush();
            }
        }

        public void Delete(Hive hive, string path, string name)
        {
            if (hive != Hive.CurrentUser) throw new InvalidOperationException("This tool only changes your own (current user) settings.");
            using (var root = Root(hive))
            using (var key = root.OpenSubKey(path, true))
            {
                if (key == null) return;
                key.DeleteValue(name, false);
                key.Flush();
            }
        }

        public string[] ValueNames(Hive hive, string path)
        {
            using (var root = Root(hive))
            using (var key = root.OpenSubKey(path, false))
                return key == null ? new string[0] : key.GetValueNames();
        }

        static RegKind FromWin(RegistryValueKind k)
        {
            switch (k)
            {
                case RegistryValueKind.DWord: return RegKind.DWord;
                case RegistryValueKind.QWord: return RegKind.QWord;
                case RegistryValueKind.String: return RegKind.String;
                case RegistryValueKind.ExpandString: return RegKind.ExpandString;
                case RegistryValueKind.MultiString: return RegKind.MultiString;
                default: return RegKind.Binary;
            }
        }

        static RegistryValueKind ToWin(RegKind k)
        {
            switch (k)
            {
                case RegKind.DWord: return RegistryValueKind.DWord;
                case RegKind.QWord: return RegistryValueKind.QWord;
                case RegKind.String: return RegistryValueKind.String;
                case RegKind.ExpandString: return RegistryValueKind.ExpandString;
                case RegKind.MultiString: return RegistryValueKind.MultiString;
                default: return RegistryValueKind.Binary;
            }
        }
    }
}
