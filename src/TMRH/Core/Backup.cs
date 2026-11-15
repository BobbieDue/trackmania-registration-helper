using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tmrh.Core
{
    public enum Stage { Applying, Applied, Restoring }

    /// <summary>The player's original settings, saved once before anything changes.</summary>
    public sealed class Backup
    {
        public Stage Stage { get; set; }
        public DateTime Created { get; set; }
        public string User { get; set; }
        public List<RegValue> Originals { get; set; }

        public Backup() { Originals = new List<RegValue>(); }
    }

    /// <summary>
    /// Keeps the backup in the player's own %LocalAppData%. Every save writes a temporary file and then swaps it in,
    /// so a crash or power cut leaves either the old file or the new one, never half a file.
    /// </summary>
    public sealed class BackupStore
    {
        readonly string dir;
        public BackupStore(string directory) { dir = directory; }

        public static string DefaultDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "We Are Crazy", "TrackMania Registration Helper"); }
        }

        public string FilePath { get { return Path.Combine(dir, "original-settings.txt"); } }

        /// <summary>
        /// Test builds were called "TMUF Registration Mode" and saved to a folder of that name. If a saved copy is
        /// still there (Registration Mode left on), move it so Disable can still put those settings back.
        /// </summary>
        public static void MoveFromOldFolder()
        {
            string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "We Are Crazy", "TMUF Registration Mode", "original-settings.txt");
            var now = new BackupStore(DefaultDirectory);
            if (!File.Exists(old) || File.Exists(now.FilePath)) return;
            Directory.CreateDirectory(DefaultDirectory);
            File.Move(old, now.FilePath);
        }

        public Backup Load()
        {
            if (!File.Exists(FilePath)) return null;
            var b = new Backup();
            bool sawEnd = false;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                switch (k)
                {
                    case "stage": b.Stage = (Stage)Enum.Parse(typeof(Stage), v); break;
                    case "created": b.Created = DateTime.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                    case "user": b.User = v; break;
                    case "value": b.Originals.Add(Decode(v)); break;
                    case "end": sawEnd = true; break;
                }
            }
            if (!sawEnd || b.Originals.Count == 0) throw new InvalidDataException("The saved settings file is incomplete: " + FilePath);
            return b;
        }

        public void Save(Backup b)
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("# TrackMania Registration Helper: your original Windows Internet Options settings. Do not edit.");
            sb.AppendLine("version=1");
            sb.AppendLine("stage=" + b.Stage);
            sb.AppendLine("created=" + b.Created.ToString("o", CultureInfo.InvariantCulture));
            sb.AppendLine("user=" + b.User);
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

        /// <summary>After a successful restore the backup is kept (renamed) for reference, never reused.</summary>
        public void Retire()
        {
            if (!File.Exists(FilePath)) return;
            string done = Path.Combine(dir, "restored-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
            File.Move(FilePath, done);
            foreach (var old in Directory.GetFiles(dir, "restored-*.txt").OrderByDescending(f => f).Skip(5)) File.Delete(old);
        }

        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        static string UnB64(string s) { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }

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

        static RegValue Decode(string s)
        {
            var p = s.Split('\t');
            if (p.Length != 5) throw new InvalidDataException("Bad line in the saved settings file.");
            var hive = (Hive)Enum.Parse(typeof(Hive), p[0]);
            var kind = (RegKind)Enum.Parse(typeof(RegKind), p[3]);
            object data;
            switch (kind)
            {
                case RegKind.Absent: data = null; break;
                case RegKind.DWord: data = int.Parse(p[4], CultureInfo.InvariantCulture); break;
                case RegKind.QWord: data = long.Parse(p[4], CultureInfo.InvariantCulture); break;
                case RegKind.MultiString: data = p[4].Length == 0 ? new string[0] : p[4].Split(',').Select(UnB64).ToArray(); break;
                case RegKind.Binary: data = Convert.FromBase64String(p[4]); break;
                default: data = UnB64(p[4]); break;
            }
            return new RegValue(hive, UnB64(p[1]), UnB64(p[2]), kind, data);
        }
    }
}
