using System.Drawing;

namespace Tmrh.UI
{
    /// <summary>
    /// Colour-blind-safe colours (Okabe-Ito): blue and orange stay distinct for red-green colour blindness
    /// (deuteranopia, protanopia) and for blue-yellow (tritanopia) when paired with dark/light contrast.
    /// No red and no green anywhere. Every state also has its own symbol and word, so colour is never the only cue.
    /// </summary>
    static class Palette
    {
        public static readonly Color Blue = Color.FromArgb(0x00, 0x72, 0xB2);        // ON, main action
        public static readonly Color BlueDark = Color.FromArgb(0x00, 0x55, 0x86);
        public static readonly Color Orange = Color.FromArgb(0xE6, 0x9F, 0x00);      // warnings, attention
        public static readonly Color OrangeLight = Color.FromArgb(0xFD, 0xF1, 0xD6);
        public static readonly Color Grey = Color.FromArgb(0x5F, 0x5F, 0x5F);        // OFF
        public static readonly Color GreyLight = Color.FromArgb(0xEC, 0xEC, 0xEC);
        public static readonly Color Back = Color.FromArgb(0xFA, 0xFA, 0xFA);
        public static readonly Color Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public static readonly Color TextSoft = Color.FromArgb(0x4A, 0x4A, 0x4A);
        public static readonly Color White = Color.White;
    }
}
