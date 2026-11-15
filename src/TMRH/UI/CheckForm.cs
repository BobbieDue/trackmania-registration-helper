using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Tmrh.Core;

namespace Tmrh.UI
{
    /// <summary>
    /// Proves which registry value each Internet Options box changes, on the player's own PC: take a snapshot,
    /// tick or untick one box in Internet Options, then compare. Read-only: it never changes anything.
    /// </summary>
    sealed class CheckForm : Form
    {
        static readonly KeyValuePair<Hive, string>[] Watched =
        {
            new KeyValuePair<Hive, string>(Hive.CurrentUser, Settings.InternetSettings),
            new KeyValuePair<Hive, string>(Hive.CurrentUser, Settings.SoftwarePublishing),
            new KeyValuePair<Hive, string>(Hive.CurrentUser, Settings.PolicyInternetSettings),
            new KeyValuePair<Hive, string>(Hive.LocalMachine, Settings.InternetSettings),
            new KeyValuePair<Hive, string>(Hive.LocalMachine, Settings.PolicyInternetSettings),
        };

        readonly IRegistryStore reg = new WindowsRegistryStore();
        readonly TextBox output = new TextBox();
        Dictionary<string, RegValue> before;

        public CheckForm()
        {
            Text = "Settings check";
            AutoScaleMode = AutoScaleMode.None;   // sizes come from Theme
            Font = Theme.F("Segoe UI", 10F);
            BackColor = Palette.Back;
            ClientSize = new Size(Theme.Px(640), Theme.Px(560));
            MinimumSize = new Size(Theme.Px(520), Theme.Px(420));
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Theme.Px(20), Theme.Px(16), Theme.Px(20), Theme.Px(16)) };
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(grid);

            var intro = new Label
            {
                Text = "This shows exactly which Windows setting each Internet Options box changes. It doesn't change anything itself.\n\n" +
                       "1.  Click Take snapshot.\n" +
                       "2.  Click Open Internet Options, tick or untick ONE box under Security, then click Apply.\n" +
                       "3.  Click Compare to see what changed. Repeat for each box, then Copy the results.",
                AutoSize = true, MaximumSize = new Size(Theme.Px(600), 0), Margin = new Padding(0, 0, 0, Theme.Px(14))
            };
            grid.Controls.Add(intro, 0, 0);
            Resize += (s, e) => intro.MaximumSize = new Size(Math.Max(Theme.Px(300), grid.ClientSize.Width - grid.Padding.Horizontal), 0);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, Theme.Px(12)) };
            var snap = Btn("Take snapshot", true);
            var open = Btn("Open Internet Options", true);
            var compare = Btn("Compare", true);
            var copy = Btn("Copy results", false);
            row.Controls.AddRange(new Control[] { snap, open, compare, copy });
            grid.Controls.Add(row, 0, 1);

            output.Dock = DockStyle.Fill;
            output.Multiline = true; output.ReadOnly = true; output.ScrollBars = ScrollBars.Vertical;
            output.Font = Theme.F("Consolas", 9.5F); output.BackColor = Color.White; output.BorderStyle = BorderStyle.FixedSingle;
            output.Margin = new Padding(0);
            grid.Controls.Add(output, 0, 2);

            snap.Click += (s, e) => { before = Snapshot(); output.Text = "Snapshot taken (" + before.Count + " values). Now change one box in Internet Options, click Apply, then Compare."; };
            open.Click += (s, e) => Process.Start("rundll32.exe", "shell32.dll,Control_RunDLL inetcpl.cpl,,6");   // tab 6 = Advanced
            compare.Click += (s, e) => Compare();
            copy.Click += (s, e) => { if (output.Text.Length > 0) Clipboard.SetText(output.Text); };
        }

        /// <summary>Buttons size themselves to their text, with even gaps, and wrap to a new line if the window is narrow.</summary>
        static Button Btn(string text, bool primary)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Theme.Px(14), Theme.Px(7), Theme.Px(14), Theme.Px(7)), Margin = new Padding(0, 0, Theme.Px(10), Theme.Px(8)),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand,
                BackColor = primary ? Palette.Blue : Palette.GreyLight, ForeColor = primary ? Palette.White : Palette.Text
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Palette.Grey;
            return b;
        }

        /// <summary>For the build's screenshot check.</summary>
        public void PreviewText(string t) { output.Text = t; }

        Dictionary<string, RegValue> Snapshot()
        {
            var d = new Dictionary<string, RegValue>();
            foreach (var w in Watched)
                foreach (var n in reg.ValueNames(w.Key, w.Value))
                {
                    var v = reg.Read(w.Key, w.Value, n);
                    d[v.Key] = v;
                }
            return d;
        }

        void Compare()
        {
            if (before == null) { output.Text = "Take a snapshot first."; return; }
            var after = Snapshot();
            var lines = new List<string>();
            foreach (var k in before.Keys.Union(after.Keys).OrderBy(x => x))
            {
                RegValue a, b;
                before.TryGetValue(k, out a); after.TryGetValue(k, out b);
                if (a != null && b != null && a.SameAs(b)) continue;
                lines.Add(k);
                lines.Add("   before: " + (a == null ? "(not set)" : a.Describe()));
                lines.Add("   after:  " + (b == null ? "(not set)" : b.Describe()));
                if (a != null && b != null && a.AsDWord.HasValue && b.AsDWord.HasValue)
                    lines.Add(string.Format("   changed bits: 0x{0:X8}", a.AsDWord.Value ^ b.AsDWord.Value));
            }
            output.Text = lines.Count == 0 ? "Nothing changed. Did you click Apply in Internet Options?" : "Windows " + Program.WindowsBuild() + "\r\n" + string.Join("\r\n", lines);
            before = after;   // ready for the next box
        }
    }
}
