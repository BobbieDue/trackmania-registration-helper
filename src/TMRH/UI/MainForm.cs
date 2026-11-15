using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tmrh.Core;

namespace Tmrh.UI
{
    sealed class MainForm : Form
    {
        const string Website = "https://wearecrazy.co.uk/";
        const string SourceUrl = "https://github.com/BobbieDue/trackmania-registration-helper";
        readonly Engine engine;
        readonly TableLayoutPanel statusBox = new TableLayoutPanel();
        readonly Label statusSymbol = new Label(), statusWord = new Label(), statusText = new Label();
        readonly Button enableBtn = new Button(), disableBtn = new Button();
        readonly Label progress = new Label();
        bool busy;
        Status last;

        public MainForm(Engine engine)
        {
            this.engine = engine;
            Text = "TrackMania Registration Helper (TMRH)";
            AutoScaleMode = AutoScaleMode.None;   // sizes come from Theme
            Font = Theme.F("Segoe UI", 10F);
            BackColor = Palette.Back;
            ForeColor = Palette.Text;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            int W = Theme.Px(520);

            var page = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0), Padding = new Padding(0) };
            Controls.Add(page);

            // header band
            var head = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Palette.Blue, Margin = new Padding(0), Padding = new Padding(Theme.Px(24), Theme.Px(12), Theme.Px(24), Theme.Px(12)), MinimumSize = new Size(W + Theme.Px(48), 0) };
            head.Controls.Add(new Label { Text = "TrackMania Registration Helper", Font = Theme.F("Segoe UI Semibold", 18F), ForeColor = Palette.White, AutoSize = true, Margin = new Padding(0) });
            head.Controls.Add(new Label { Text = "TMRH \u00B7 for TrackMania Nations Forever and United Forever \u00B7 from the We Are C\u00AEA\u017C\u03A8 community", ForeColor = Color.FromArgb(0xDC, 0xEC, 0xF7), AutoSize = true, MaximumSize = new Size(W, 0), Margin = new Padding(Theme.Px(2), Theme.Px(2), 0, 0) });
            page.Controls.Add(head);

            var col = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0), Padding = new Padding(Theme.Px(24), Theme.Px(12), Theme.Px(24), Theme.Px(14)) };
            page.Controls.Add(col);

            col.Controls.Add(Para("Having trouble creating a new TrackMania Nations Forever or United Forever account? This helper temporarily adjusts the Windows security settings the old TrackMania registration system needs. Once your account has been created, simply turn Registration Mode OFF to restore your original settings.", W));

            // status card: symbol + word + sentence, so it never depends on colour alone
            statusBox.ColumnCount = 2; statusBox.RowCount = 2;
            statusBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusBox.AutoSize = true; statusBox.MinimumSize = new Size(W, 0); statusBox.MaximumSize = new Size(W, 0);
            statusBox.Padding = new Padding(Theme.Px(14), Theme.Px(10), Theme.Px(14), Theme.Px(10));
            statusBox.Margin = new Padding(0, Theme.Px(12), 0, Theme.Px(10));
            statusSymbol.Font = Theme.F("Segoe UI Symbol", 24F); statusSymbol.AutoSize = true; statusSymbol.Margin = new Padding(0, 0, Theme.Px(14), 0);
            statusWord.Font = Theme.F("Segoe UI Semibold", 14F); statusWord.AutoSize = true; statusWord.Margin = new Padding(0);
            statusText.AutoSize = true; statusText.MaximumSize = new Size(W - Theme.Px(100), 0); statusText.Margin = new Padding(Theme.Px(1), Theme.Px(4), 0, 0);
            statusBox.Controls.Add(statusSymbol, 0, 0); statusBox.SetRowSpan(statusSymbol, 2);
            statusBox.Controls.Add(statusWord, 1, 0);
            statusBox.Controls.Add(statusText, 1, 1);
            statusBox.Paint += (s, e) => { using (var p = new Pen(statusBox.Tag is Color ? (Color)statusBox.Tag : Palette.Grey, Theme.Px(2))) e.Graphics.DrawRectangle(p, 1, 1, statusBox.Width - 3, statusBox.Height - 3); };
            col.Controls.Add(statusBox);

            // the three steps: numbered badges, short words
            var steps = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, MinimumSize = new Size(W, 0), MaximumSize = new Size(W, 0), Margin = new Padding(0, 0, 0, Theme.Px(12)) };
            for (int i = 0; i < 3; i++) steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3F));
            string[] st = { "Turn it on here", "Create your account in TrackMania", "Come back and turn it off" };
            for (int i = 0; i < 3; i++)
            {
                var cell = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                cell.Controls.Add(new Label { Text = (i + 1).ToString(), Font = Theme.F("Segoe UI Semibold", 9F), BackColor = Palette.Blue, ForeColor = Palette.White, AutoSize = false, Size = new Size(Theme.Px(22), Theme.Px(22)), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 0, Theme.Px(8), 0) });
                cell.Controls.Add(new Label { Text = st[i], AutoSize = true, MaximumSize = new Size(W / 3 - Theme.Px(36), 0), ForeColor = Palette.TextSoft, Margin = new Padding(0, Theme.Px(2), 0, 0) });
                steps.Controls.Add(cell, i, 0);
            }
            col.Controls.Add(steps);

            Style(enableBtn, "\u25CF  Enable Registration Mode", Palette.Blue, Palette.White, W);
            Style(disableBtn, "\u25CB  Disable Registration Mode", Palette.BlueDark, Palette.White, W);   // the button to press next is always blue
            enableBtn.Click += async (s, e) => await Run(true);
            disableBtn.Click += async (s, e) => await Run(false);
            col.Controls.Add(enableBtn);
            col.Controls.Add(disableBtn);

            progress.AutoSize = true; progress.MaximumSize = new Size(W, 0); progress.ForeColor = Palette.TextSoft; progress.Margin = new Padding(0, 0, 0, Theme.Px(10)); progress.Visible = false;
            progress.TextChanged += (s, e) => progress.Visible = progress.Text.Length > 0;
            col.Controls.Add(progress);

            // warning card: orange bar + symbol + words
            var warn = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, MinimumSize = new Size(W, 0), MaximumSize = new Size(W, 0), BackColor = Palette.OrangeLight, Margin = new Padding(0, Theme.Px(4), 0, Theme.Px(10)), Padding = new Padding(0) };
            warn.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Px(6)));
            warn.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            warn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            warn.Controls.Add(new Panel { BackColor = Palette.Orange, Dock = DockStyle.Fill, Margin = new Padding(0) }, 0, 0);
            warn.Controls.Add(new Label { Text = "\u26A0", Font = Theme.F("Segoe UI Symbol", 15F), AutoSize = true, Margin = new Padding(Theme.Px(12), Theme.Px(8), Theme.Px(6), Theme.Px(8)) }, 1, 0);
            warn.Controls.Add(new Label
            {
                Text = "While Registration Mode is ON, Windows does not check whether website and software certificates have been revoked, so you are less protected against fake or withdrawn certificates. Turn it OFF as soon as your account is created.",
                AutoSize = true, MaximumSize = new Size(W - Theme.Px(60), 0), Margin = new Padding(0, Theme.Px(8), Theme.Px(12), Theme.Px(8))
            }, 2, 0);
            col.Controls.Add(warn);

            var links = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = true, MaximumSize = new Size(W, 0) };
            links.Controls.Add(Link("wearecrazy.co.uk", () => Process.Start(Website)));
            links.Controls.Add(Link("What does it change?", ShowDetails));
            links.Controls.Add(Link("Settings check", () => new CheckForm().ShowDialog(this)));
            var ver = Application.ProductVersion.Split('+')[0];
            links.Controls.Add(Link("v" + ver + " \u00B7 source code", () => Process.Start(SourceUrl)));
            col.Controls.Add(links);

            Shown += (s, e) => RefreshStatus(true);
        }

        static Label Para(string text, int w) { return new Label { Text = text, MaximumSize = new Size(w, 0), AutoSize = true, Margin = new Padding(0) }; }

        static void Style(Button b, string text, Color back, Color fore, int w)
        {
            b.Text = text; b.AutoSize = true; b.MinimumSize = new Size(w, 0); b.MaximumSize = new Size(w, 0);
            b.Padding = new Padding(0, Theme.Px(7), 0, Theme.Px(7)); b.Margin = new Padding(0, 0, 0, Theme.Px(8));
            b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0;
            b.BackColor = back; b.ForeColor = fore; b.Font = Theme.F("Segoe UI Semibold", 12F);
            b.Tag = back; b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
            b.EnabledChanged += (s, e) => { b.BackColor = b.Enabled ? (Color)b.Tag : Palette.GreyLight; b.ForeColor = b.Enabled ? fore : Palette.TextSoft; };
        }

        LinkLabel Link(string text, Action a)
        {
            var l = new LinkLabel { Text = text, AutoSize = true, LinkColor = Palette.BlueDark, ActiveLinkColor = Palette.Blue, Margin = new Padding(0, 0, Theme.Px(22), Theme.Px(4)) };
            l.LinkClicked += (s, e) => { try { a(); } catch (Exception ex) { MessageBox.Show(ex.Message, Text); } };
            return l;
        }

        /// <summary>For the build's screenshot check: show a state without reading the registry.</summary>
        public void Preview(Mode m)
        {
            switch (m)
            {
                case Mode.On: SetStatus("\u25CF", "Registration Mode is ON", "Start TrackMania and create your account, then come back and turn it OFF.", Palette.Blue, Palette.White, Palette.BlueDark); enableBtn.Enabled = false; disableBtn.Enabled = true; break;
                case Mode.Interrupted: SetStatus("\u25B2", "Last change didn't finish", "Windows or this tool stopped part-way. Click Disable to put your original settings back.", Palette.OrangeLight, Palette.Text, Palette.Orange); enableBtn.Enabled = false; disableBtn.Enabled = true; break;
                case Mode.BadBackup: SetStatus("\u25B2", "Saved settings file is damaged", "It failed its safety check, so nothing will be restored from it. Click Disable to see your options.", Palette.OrangeLight, Palette.Text, Palette.Orange); enableBtn.Enabled = false; disableBtn.Enabled = true; break;
                default: SetStatus("\u25CB", "Registration Mode is OFF", "Your normal Windows settings are in place.", Palette.GreyLight, Palette.Text, Palette.Grey); enableBtn.Enabled = true; disableBtn.Enabled = false; break;
            }
        }

        void SetStatus(string symbol, string word, string sentence, Color fill, Color text, Color border)
        {
            statusBox.BackColor = fill; statusBox.Tag = border;
            statusSymbol.Text = symbol; statusWord.Text = word; statusText.Text = sentence;
            statusSymbol.ForeColor = statusWord.ForeColor = statusText.ForeColor = text;
            statusBox.Invalidate();
            statusBox.AccessibleName = word + ". " + sentence;
        }

        void RefreshStatus(bool offerRecovery)
        {
            Status s;
            try { s = engine.GetStatus(); }
            catch (Exception ex) { SetStatus("▲", "CAN'T READ SETTINGS", ex.Message, Palette.OrangeLight, Palette.Text, Palette.Orange); return; }
            last = s;
            switch (s.Mode)
            {
                case Mode.On:
                    SetStatus("●", "Registration Mode is ON", "Start TrackMania and create your account, then come back and turn it OFF.", Palette.Blue, Palette.White, Palette.BlueDark); break;
                case Mode.Off:
                    SetStatus("○", "Registration Mode is OFF", "Your normal Windows settings are in place.", Palette.GreyLight, Palette.Text, Palette.Grey); break;
                case Mode.NotNeeded:
                    SetStatus("✓", "Already OK for registration", "Your settings already allow TrackMania registration, so you don't need to turn anything on.", Palette.GreyLight, Palette.Text, Palette.Blue); break;
                case Mode.ChangedSince:
                    SetStatus("▲", "ON, but settings were changed", "Something changed these settings after Registration Mode was turned on. Disable still puts your originals back.", Palette.OrangeLight, Palette.Text, Palette.Orange); break;
                case Mode.Interrupted:
                    SetStatus("▲", "Last change didn't finish", "Windows or this tool stopped part-way. Click Disable to put your original settings back.", Palette.OrangeLight, Palette.Text, Palette.Orange); break;
                case Mode.Managed:
                    SetStatus("▲", "Controlled by your organisation", "A policy on this PC controls these settings, so this tool can't change them.", Palette.OrangeLight, Palette.Text, Palette.Orange); break;
                case Mode.BadBackup:
                    SetStatus("\u25B2", "Saved settings file is damaged", "It failed its safety check, so nothing will be restored from it. Click Disable to see your options.", Palette.OrangeLight, Palette.Text, Palette.Orange); break;
            }
            enableBtn.Enabled = !busy && (s.Mode == Mode.Off || s.Mode == Mode.ChangedSince);
            disableBtn.Enabled = !busy && (s.Backup != null || s.Mode == Mode.BadBackup);
            if (offerRecovery && s.Mode == Mode.BadBackup) { BeginInvoke(new Action(Recovery)); return; }
            if (offerRecovery && s.Mode == Mode.Interrupted &&
                MessageBox.Show(this, "The last change to Registration Mode didn't finish (for example Windows restarted part-way).\n\nPut your original settings back now?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                BeginInvoke(new Action(async () => await Run(false, true)));
        }

        async Task Run(bool enable, bool skipConfirm = false, bool overwriteChanges = false)
        {
            if (busy) return;
            if (!enable && last != null && last.Mode == Mode.BadBackup) { Recovery(); return; }
            if (enable && !skipConfirm)
            {
                var msg = "Registration Mode changes these Windows Internet Options settings for your Windows account only:\n\n" +
                          "  • Use TLS 1.0, 1.1 and 1.2" + (engine.Tls13Available ? " and 1.3" : "") + ": turned on\n" +
                          "  • Check for server certificate revocation: turned off\n" +
                          "  • Check for publisher's certificate revocation: turned off\n\n" +
                          "Your current settings are saved first, and Disable puts them back exactly.\n\n" +
                          "While it's on, Windows won't check whether certificates have been revoked, which makes you less safe from fake or withdrawn certificates. Turn it off as soon as your account is created.\n\nTurn Registration Mode on?";
                if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            busy = true; enableBtn.Enabled = disableBtn.Enabled = false; UseWaitCursor = true;
            Result r;
            try
            {
                Action<string> p = t => BeginInvoke(new Action(() => progress.Text = t));
                r = await Task.Run(() => enable ? engine.Enable(p) : engine.Disable(p, overwriteChanges));
            }
            catch (Exception ex)
            {
                r = Result.Fail((enable ? "Turning Registration Mode on" : "Restoring your settings") + " stopped with an error: " + ex.Message +
                    "\n\nYour original settings are saved. Open this tool again or click Disable to put them back.");
            }
            busy = false; UseWaitCursor = false;
            if (!enable && r.NeedsConfirmation.Count > 0)
            {
                progress.Text = "Waiting for you to decide.";
                var q = "These settings were changed after Registration Mode was turned on:\n\n" +
                        string.Join("\n", r.NeedsConfirmation.Select(c => "• " + c.Setting + "\n     Registration Mode set: " + c.Expected.Describe() + "\n     It is now: " + c.Current.Describe())) +
                        "\n\nDisable will overwrite these changes and put back your original settings exactly as they were before Registration Mode.\n\nRestore your original settings?";
                if (MessageBox.Show(this, q, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes) { await Run(false, true, true); return; }
                progress.Text = "Nothing was changed.";
                RefreshStatus(false);
                MessageBox.Show(this, "Nothing was changed. Your settings and your saved copy are as they were.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            progress.Text = r.Ok ? (enable ? "Done: Registration Mode is on." : "Done: your settings are restored.") : "Not done, see the message.";
            RefreshStatus(false);
            MessageBox.Show(this, r.Message + (r.Details.Count > 0 ? "\n\n" + string.Join("\n", r.Details.Select(d => "• " + d)) : ""),
                Text, MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (r.Housekeeping.Count > 0)   // separate from the result: the settings themselves are fine
                MessageBox.Show(this, "Your settings were restored correctly. One tidy-up step didn't work:\n\n" + string.Join("\n", r.Housekeeping.Select(d => "• " + d)),
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// The saved settings file failed its safety check. Nothing is restored from it. Every way forward is the
        /// player's explicit choice: reset the three boxes to Windows' secure positions, and/or move the file aside (kept).
        /// </summary>
        void Recovery()
        {
            var probs = (last != null ? last.BackupProblems : new List<string>()).Take(6).ToList();
            var msg = "Your saved settings file failed its safety check, so this tool won't restore anything from it. It hasn't changed any settings and the file has been left as it is.\n\n" +
                      (probs.Count > 0 ? "What's wrong:\n" + string.Join("\n", probs.Select(p => "• " + p)) + "\n\n" : "") +
                      "The file is in:\n" + BackupStore.DefaultDirectory + "\n\n" +
                      "You can put the three Internet Options boxes back to Windows' secure positions now (TLS 1.0 and 1.1 off, TLS 1.2 on" + (engine.Tls13Available ? ", TLS 1.3 on" : "") + ", both certificate revocation checks on). Other settings aren't touched.\n\nDo that now?";
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                Result r;
                try { r = engine.ResetToSecureSettings(); } catch (Exception ex) { r = Result.Fail("That didn't work: " + ex.Message); }
                MessageBox.Show(this, r.Message + (r.Details.Count > 0 ? "\n\n" + string.Join("\n", r.Details.Select(d => "• " + d)) : ""), Text, MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
            if (MessageBox.Show(this, "Move the damaged file aside so this tool can be used again? It is kept in the same folder (renamed to damaged-...txt), never deleted.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { var f = new BackupStore(BackupStore.DefaultDirectory).SetAside(); MessageBox.Show(this, "Moved to:\n" + f, Text, MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { MessageBox.Show(this, "It couldn't be moved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            }
            RefreshStatus(false);
        }

        void ShowDetails()
        {
            var lines = new[]
            {
                "Registration Mode changes three of your own Windows Internet Options settings (Internet Options > Advanced > Security). It never needs administrator rights and never changes anything for other Windows accounts.",
                "",
                "• Use TLS 1.0 / 1.1 / 1.2 / 1.3: turned on (1.3 only on Windows 11, where it exists).",
                "   HKCU\\" + Settings.InternetSettings + "\\SecureProtocols (only these bits are added)",
                "• Check for server certificate revocation: turned off.",
                "   HKCU\\" + Settings.InternetSettings + "\\CertificateRevocation = 0",
                "• Check for publisher's certificate revocation: turned off.",
                "   HKCU\\" + Settings.SoftwarePublishing + "\\State (bit 0x200 added)",
                "",
                "Before changing anything it saves the current values (including ones that weren't set at all) to:",
                "   " + BackupStore.DefaultDirectory,
                "Disable puts every value back exactly, removes any value that wasn't there before, and checks the result.",
                "",
                "It does not touch Windows Defender, the firewall, your browsers or any other setting. The source code is published so anyone can check this."
            };
            MessageBox.Show(this, string.Join("\n", lines), "What does it change?", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
