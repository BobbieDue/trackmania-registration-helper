using System;
using System.Linq;

namespace Tmrh.Core
{
    /// <summary>The kinds of registry value the tool can save and put back exactly.</summary>
    public enum RegKind { Absent, DWord, QWord, String, ExpandString, MultiString, Binary }

    public enum Hive { CurrentUser, LocalMachine }

    /// <summary>One registry value: where it lives, whether it exists, its type and its data.</summary>
    public sealed class RegValue
    {
        public Hive Hive { get; private set; }
        public string Path { get; private set; }
        public string Name { get; private set; }
        public RegKind Kind { get; private set; }
        public object Data { get; private set; }

        public RegValue(Hive hive, string path, string name, RegKind kind, object data)
        {
            Hive = hive; Path = path; Name = name; Kind = kind; Data = kind == RegKind.Absent ? null : data;
        }

        public static RegValue Absent(Hive hive, string path, string name) { return new RegValue(hive, path, name, RegKind.Absent, null); }
        public static RegValue DWord(Hive hive, string path, string name, int value) { return new RegValue(hive, path, name, RegKind.DWord, value); }

        public bool Exists { get { return Kind != RegKind.Absent; } }
        public string Key { get { return (Hive == Hive.CurrentUser ? "HKCU" : "HKLM") + "\\" + Path + "\\" + Name; } }

        /// <summary>The DWORD as an unsigned number (how Windows shows it), or null when it isn't a DWORD.</summary>
        public uint? AsDWord { get { return Kind == RegKind.DWord ? (uint?)unchecked((uint)(int)Data) : null; } }

        public RegValue WithDWord(uint value) { return new RegValue(Hive, Path, Name, RegKind.DWord, unchecked((int)value)); }

        public bool SameAs(RegValue other)
        {
            if (other == null || Kind != other.Kind) return false;
            switch (Kind)
            {
                case RegKind.Absent: return true;
                case RegKind.DWord: return (int)Data == (int)other.Data;
                case RegKind.QWord: return (long)Data == (long)other.Data;
                case RegKind.MultiString: return ((string[])Data).SequenceEqual((string[])other.Data);
                case RegKind.Binary: return ((byte[])Data).SequenceEqual((byte[])other.Data);
                default: return string.Equals((string)Data, (string)other.Data, StringComparison.Ordinal);
            }
        }

        /// <summary>Human-readable value, e.g. "0x00000A80 (DWORD)" or "(not set)".</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case RegKind.Absent: return "(not set)";
                case RegKind.DWord: return string.Format("0x{0:X8} (DWORD)", AsDWord.Value);
                case RegKind.QWord: return string.Format("0x{0:X16} (QWORD)", (long)Data);
                case RegKind.MultiString: return string.Join(" | ", (string[])Data) + " (multi-string)";
                case RegKind.Binary: return BitConverter.ToString((byte[])Data) + " (binary)";
                default: return "\"" + Data + "\" (" + (Kind == RegKind.ExpandString ? "expandable string" : "string") + ")";
            }
        }
    }

    /// <summary>Registry access, so the logic can be tested without touching a real registry.</summary>
    public interface IRegistryStore
    {
        RegValue Read(Hive hive, string path, string name);
        void Write(RegValue value);
        void Delete(Hive hive, string path, string name);
        string[] ValueNames(Hive hive, string path);
    }
}
