using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tmrh.Core
{
    /// <summary>Restored = the settings were put back and checked, but the file couldn't be archived yet (version 2 only).</summary>
    public enum Stage { Applying, Applied, Restoring, Restored }

    /// <summary>
    /// The saved settings file failed validation: an InvalidDataException carrying the list of problems found.
    /// Nothing has been restored from it and the file is left as it is.
    /// </summary>
    /// <summary>
    /// A saved copy from the old test builds ("TMUF Registration Mode") exists but couldn't be moved to the current folder.
    /// The tool must not carry on as if there were no saved copy: it would save already-changed settings as the "originals".
    /// The old file is left exactly where it is.
    /// </summary>
    public sealed class LegacyBackupException : IOException
    {
        public string OldFile { get; private set; }
        public string NewFile { get; private set; }
        public LegacyBackupException(string oldFile, string newFile, Exception why)
            : base("Your original settings were saved by an older test version of this tool, and that saved copy couldn't be moved to where this version keeps it (" + why.Message + "). Nothing has been changed.", why)
        { OldFile = oldFile; NewFile = newFile; }
    }

    public static class BackupInvalid
    {
        const string Key = "TMRH.Problems";
        public static InvalidDataException Make(string file, List<string> problems)
        {
            var ex = new InvalidDataException("The saved settings file can't be trusted, so nothing was restored from it: " + file + (problems.Count > 0 ? " (" + problems[0] + ")" : ""));
            ex.Data[Key] = problems.ToArray();
            return ex;
        }
        public static List<string> Problems(InvalidDataException ex)
        {
            var p = ex.Data[Key] as string[];
            return p != null ? p.ToList() : new List<string> { ex.Message };
        }
    }

    /// <summary>The player's original settings, saved once before anything changes.</summary>
    public sealed class Backup
    {
        public const int CurrentVersion = 2;
        public int Version { get; set; }
        public Stage Stage { get; set; }
        public DateTime Created { get; set; }
        /// <summary>Descriptive only: the account name when it was saved (account names can change).</summary>
        public string User { get; set; }
        /// <summary>The Windows account (SID) the settings belong to. Written by v1.2.1 and later; older files don't have it.</summary>
        public string Sid { get; set; }
        public List<RegValue> Originals { get; set; }

        public Backup() { Originals = new List<RegValue>(); Version = CurrentVersion; }
    }

    /// <summary>
    /// Keeps the backup in the player's own %LocalAppData%. Every save writes a temporary file and then swaps it in,
    /// so a crash or power cut leaves either the old file or the new one, never half a file.
    /// </summary>
    public sealed class BackupStore
    {
        readonly string dir, legacy;
        readonly Func<string> currentSid;

        /// <param name="legacyFile">Where the old test builds kept their saved copy (moved here on first use), or null.</param>
        /// <param name="sid">The signed-in Windows account's SID, or null when it can't be told (then it isn't checked).</param>
        public BackupStore(string directory, string legacyFile = null, Func<string> sid = null)
        {
            dir = directory; legacy = legacyFile; currentSid = sid ?? (() => null);
        }

        public string CurrentSid { get { try { return currentSid(); } catch { return null; } } }

        public static string DefaultLegacyFile
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "We Are Crazy", "TMUF Registration Mode", "original-settings.txt"); }
        }

        public static string WindowsSid()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent()) return id.User == null ? null : id.User.Value;
        }

        public static string DefaultDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "We Are Crazy", "TrackMania Registration Helper"); }
        }

        public string FilePath { get { return Path.Combine(dir, "original-settings.txt"); } }

        /// <summary>
        /// If there's no saved copy here but the old test builds left one, move it here (on the same disk this is a
        /// single rename: it either happens or the old file stays exactly as it was). If the move fails, throw:
        /// carrying on as if there were no saved copy could save already-changed settings as the originals.
        /// </summary>
        void MigrateLegacy()
        {
            if (legacy == null || File.Exists(FilePath) || !File.Exists(legacy)) return;
            try
            {
                Directory.CreateDirectory(dir);
                File.Move(legacy, FilePath);
            }
            catch (Exception ex) { throw new LegacyBackupException(legacy, FilePath, ex); }
        }

        const int MaxFileBytes = 64 * 1024, MaxText = 1024, MaxItems = 32, MaxUser = 256;

        /// <summary>
        /// Reads and checks the whole file before anything uses it. Returns null when there is no file. Throws
        /// an InvalidDataException from BackupInvalid (and leaves the file alone) if anything is wrong: only the settings this tool manages,
        /// for the current user, each once, with sensible types and sizes, a known version, stage and complete metadata.
        /// </summary>
        public Backup Load()
        {
            MigrateLegacy();
            if (!File.Exists(FilePath)) return null;
            var bad = new List<string>();
            Backup b = null;
            try
            {
                var raw = ReadBounded(FilePath);   // the size is checked before anything is read into memory
                if (raw == null) bad.Add("The file is far bigger than a saved settings file can be.");
                else b = Parse(raw, bad);
            }
            catch (Exception ex) { bad.Add("Couldn't read it: " + ex.Message); b = null; }
            if (b != null && b.Sid != null)
            {
                string me = CurrentSid;
                if (me != null && !string.Equals(me, b.Sid, StringComparison.OrdinalIgnoreCase))
                    bad.Add("It belongs to a different Windows account, so it won't be restored into this one.");
            }
            if (bad.Count > 0 || b == null) throw BackupInvalid.Make(FilePath, bad);
            return b;
        }

        /// <summary>The file's bytes, or null if it's bigger than a saved settings file can be (nothing big is ever read).</summary>
        static byte[] ReadBounded(string path)
        {
            if (new FileInfo(path).Length > MaxFileBytes) return null;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var buf = new byte[MaxFileBytes + 1];
                int n = 0, got;
                while (n < buf.Length && (got = fs.Read(buf, n, buf.Length - n)) > 0) n += got;
                if (n > MaxFileBytes) return null;   // it grew after the size check
                var raw = new byte[n];
                Buffer.BlockCopy(buf, 0, raw, 0, n);
                return raw;
            }
        }

        /// <summary>Checks a backup file's contents. Every problem found is added to bad; null if there were any.</summary>
        public static Backup Parse(byte[] raw, List<string> bad)
        {
            if (raw.Length > MaxFileBytes) { bad.Add("The file is far bigger than a saved settings file can be."); return null; }
            string text;
            try { text = new UTF8Encoding(false, true).GetString(raw); }
            catch (Exception) { bad.Add("The file isn't valid text."); return null; }
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            var b = new Backup { Version = 0 };
            var seen = new HashSet<string>();
            bool sawEnd = false, sawStage = false, sawCreated = false;
            int n = 0;
            foreach (var line0 in text.Split('\n'))
            {
                n++;
                string line = line0.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                if (sawEnd) { bad.Add("Line " + n + ": something after the end marker."); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) { bad.Add("Line " + n + ": not a setting line."); continue; }
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                if (k != "value" && !seen.Add(k)) { bad.Add("Line " + n + ": \"" + k + "\" appears twice."); continue; }
                switch (k)
                {
                    case "version":
                        if (v == "1") b.Version = 1; else if (v == "2") b.Version = 2;
                        else bad.Add("Unsupported version \"" + Short(v) + "\".");
                        break;
                    case "stage":
                        Stage st;
                        if (Enum.TryParse(v, false, out st) && Enum.IsDefined(typeof(Stage), st) && st.ToString() == v) { b.Stage = st; sawStage = true; }
                        else bad.Add("Unknown stage \"" + Short(v) + "\".");
                        break;
                    case "created":
                        DateTime t;
                        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out t)) { b.Created = t; sawCreated = true; }
                        else bad.Add("The date it was saved isn't valid.");
                        break;
                    case "sid":   // v2 only, optional (files from v1.2.0 don't have it)
                        if (b.Version == 1) bad.Add("Version 1 files don't have an account ID.");
                        else if (v.Length < 5 || v.Length > 184 || !v.StartsWith("S-1-", StringComparison.Ordinal) || v.Substring(4).Any(ch => !(char.IsDigit(ch) && ch < 128) && ch != '-'))
                            bad.Add("The Windows account ID in it isn't valid.");
                        else b.Sid = v;
                        break;
                    case "user":
                        if (v.Length == 0 || v.Length > MaxUser || v.Any(char.IsControl)) bad.Add("The Windows account name in it isn't valid.");
                        else b.User = v;
                        break;
                    case "value":
                        var rv = Decode(v, n, bad);
                        if (rv != null) b.Originals.Add(rv);
                        break;
                    case "end":
                        if (v != "1") bad.Add("The end marker isn't valid.");
                        sawEnd = true;
                        break;
                    default:
                        bad.Add("Line " + n + ": unexpected entry \"" + Short(k) + "\".");
                        break;
                }
            }
            if (b.Version == 0 && !seen.Contains("version")) bad.Add("It has no version.");
            if (!sawStage && !seen.Contains("stage")) bad.Add("It has no stage.");
            if (!sawCreated && !seen.Contains("created")) bad.Add("It has no date.");
            if (b.User == null && !seen.Contains("user")) bad.Add("It has no Windows account name.");
            if (!sawEnd) bad.Add("It is incomplete (no end marker), for example it was cut off part-way.");
            if (b.Version == 1 && b.Stage == Stage.Restored) bad.Add("Version 1 files can't be in the Restored stage.");

            var names = b.Originals.Select(o => o.Name).ToList();
            foreach (var d in names.GroupBy(x => x, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                bad.Add("The setting " + d.Key + " is listed more than once.");
            bool hasSp = names.Contains(Settings.SecureProtocols), hasCr = names.Contains(Settings.CertificateRevocation), hasSt = names.Contains(Settings.State);
            if (b.Version == 2 && !(names.Count == 3 && hasSp && hasCr && hasSt))
                bad.Add("A version 2 file must list exactly the three managed settings.");
            if (b.Version == 1 && !((names.Count == 2 && hasSp && hasCr) || (names.Count == 3 && hasSp && hasCr && hasSt)))
                bad.Add("A version 1 file must list the TLS and server revocation settings, and optionally the publisher one.");
            return bad.Count == 0 ? b : null;
        }

        static string Short(string s) { s = s ?? ""; return s.Length > 40 ? s.Substring(0, 40) + "..." : s; }

        public void Save(Backup b)
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("# TrackMania Registration Helper: your original Windows Internet Options settings. Do not edit.");
            sb.AppendLine("version=" + b.Version);   // a version 1 file being re-saved stays version 1
            sb.AppendLine("stage=" + b.Stage);
            sb.AppendLine("created=" + b.Created.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine("user=" + b.User);
            if (b.Sid != null && b.Version >= 2) sb.AppendLine("sid=" + b.Sid);
            foreach (var v in b.Originals) sb.AppendLine("value=" + Encode(v));
            sb.AppendLine("end=1");
            string tmp = FilePath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);   // on the disk before we rely on it
            }
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }

        /// <summary>
        /// After a successful, checked restore the backup is kept (renamed) for reference, never reused. Never throws:
        /// the registry is already back, so any problem here is reported separately as housekeeping. If the file can't
        /// be renamed, it is marked Restored so the tool doesn't think a restore is still needed.
        /// </summary>
        public List<string> Retire()
        {
            var notes = new List<string>();
            if (!File.Exists(FilePath)) return notes;
            try { File.Move(FilePath, UniqueRetiredName(DateTime.Now)); }
            catch (Exception ex)
            {
                notes.Add("Your saved copy couldn't be archived (" + ex.Message + ").");
                try
                {
                    var b = Load();
                    if (b != null && b.Version >= 2 && b.Stage != Stage.Restored)
                    {
                        b.Stage = Stage.Restored; Save(b);
                        notes.Add("It has been marked as already restored, so it won't be used again.");
                    }
                    else if (b != null && b.Stage != Stage.Restored)
                        notes.Add("If the tool says the last change didn't finish, Disable again is safe: it puts back the same settings.");
                }
                catch (Exception ex2) { notes.Add("It also couldn't be marked as restored (" + ex2.Message + "). If the tool says the last change didn't finish, Disable again is safe: it puts back the same settings."); }
                return notes;
            }
            try { foreach (var old in Directory.GetFiles(dir, "restored-*.txt").OrderByDescending(f => f, StringComparer.Ordinal).Skip(5)) File.Delete(old); }
            catch (Exception ex) { notes.Add("Old archived copies couldn't be tidied up (" + ex.Message + "). This doesn't affect your settings."); }
            return notes;
        }

        /// <summary>restored-yyyyMMdd-HHmmss-fff.txt, with -2, -3... added if that name is taken, so two restores never clash.</summary>
        public string UniqueRetiredName(DateTime when)
        {
            string stem = Path.Combine(dir, "restored-" + when.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            string f = stem + ".txt";
            for (int i = 2; File.Exists(f); i++) f = i < 1000 ? stem + "-" + i + ".txt" : stem + "-" + Guid.NewGuid().ToString("N") + ".txt";
            return f;
        }

        /// <summary>Moves a damaged file aside (kept, never deleted) so the tool can be used again. Returns the new name.</summary>
        public string SetAside()
        {
            string f = Path.Combine(dir, "damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".txt");
            for (int i = 2; File.Exists(f); i++) f = Path.Combine(dir, "damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "-" + i + ".txt");
            File.Move(FilePath, f);
            return f;
        }

        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }

        static string Encode(RegValue v)
        {
            string data;
            switch (v.Kind)
            {
                case RegKind.Absent: data = ""; break;
                case RegKind.DWord: data = ((int)v.Data).ToString(CultureInfo.InvariantCulture); break;
                case RegKind.QWord: data = ((long)v.Data).ToString(CultureInfo.InvariantCulture); break;
                case RegKind.MultiString: data = string.Join(",", ((string[])v.Data).Select(B64)); break;
                case RegKind.Binary: data = Convert.ToBase64String((byte[])v.Data); break;
                default: data = B64((string)v.Data); break;
            }
            return string.Join("\t", new[] { v.Hive.ToString(), B64(v.Path), B64(v.Name), v.Kind.ToString(), data });
        }

        static readonly string[][] Allowed =
        {
            new[] { Settings.InternetSettings, Settings.SecureProtocols },
            new[] { Settings.InternetSettings, Settings.CertificateRevocation },
            new[] { Settings.SoftwarePublishing, Settings.State },
        };

        static string StrictB64(string s)
        {
            var bytes = Convert.FromBase64String(s);   // FormatException for anything that isn't base64
            return new UTF8Encoding(false, true).GetString(bytes);
        }

        /// <summary>One saved value, checked strictly. Adds problems to bad and returns null if it isn't acceptable.</summary>
        static RegValue Decode(string s, int line, List<string> bad)
        {
            string at = "Line " + line + ": ";
            var p = s.Split('\t');
            if (p.Length != 5) { bad.Add(at + "a saved value isn't in the right format."); return null; }
            if (p[0] != "CurrentUser") { bad.Add(at + "a saved value isn't for your own account (" + Short(p[0]) + ")."); return null; }
            string path, name;
            try { path = StrictB64(p[1]); name = StrictB64(p[2]); }
            catch (Exception) { bad.Add(at + "a saved value's location is damaged."); return null; }
            var allowed = Allowed.FirstOrDefault(a => string.Equals(a[0], path, StringComparison.OrdinalIgnoreCase) && string.Equals(a[1], name, StringComparison.OrdinalIgnoreCase));
            if (allowed == null) { bad.Add(at + "it lists a setting this tool doesn't manage (" + Short(path) + "\\" + Short(name) + ")."); return null; }
            RegKind kind;
            if (!Enum.TryParse(p[3], false, out kind) || !Enum.IsDefined(typeof(RegKind), kind) || kind.ToString() != p[3]) { bad.Add(at + "unknown value type \"" + Short(p[3]) + "\"."); return null; }
            object data;
            try
            {
                switch (kind)
                {
                    case RegKind.Absent:
                        if (p[4].Length != 0) throw new FormatException();
                        data = null; break;
                    case RegKind.DWord: data = int.Parse(p[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); break;
                    case RegKind.QWord: data = long.Parse(p[4], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture); break;
                    case RegKind.MultiString:
                        var items = p[4].Length == 0 ? new string[0] : p[4].Split(',').Select(StrictB64).ToArray();
                        if (items.Length > MaxItems || items.Any(x => x.Length > MaxText)) throw new OverflowException();
                        data = items; break;
                    case RegKind.Binary:
                        var bytes = Convert.FromBase64String(p[4]);
                        if (bytes.Length > MaxText) throw new OverflowException();
                        data = bytes; break;
                    default:
                        var str = StrictB64(p[4]);
                        if (str.Length > MaxText) throw new OverflowException();
                        data = str; break;
                }
            }
            catch (OverflowException) { bad.Add(at + "a saved value is too big or out of range."); return null; }
            catch (Exception) { bad.Add(at + "a saved value's data is damaged."); return null; }
            return new RegValue(Hive.CurrentUser, allowed[0], allowed[1], kind, data);
        }
    }
}
