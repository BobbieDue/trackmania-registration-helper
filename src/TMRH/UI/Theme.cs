using System;
using System.Drawing;

namespace Tmrh.UI
{
    /// <summary>
    /// Sizes for the screen's display scaling (100%, 125%, 150%...). Layouts are built to grow with the text,
    /// and pixel sizes are scaled here once, so nothing overlaps or gets cut off at any scaling.
    /// </summary>
    static class Theme
    {
        static float dpi = -1;
        /// <summary>Extra text scale, only used by the build's screenshot check to imitate large display scaling.</summary>
        public static float FontScale = 1F;

        static float Dpi
        {
            get
            {
                if (dpi < 0)
                {
                    try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96F; }
                    catch { dpi = 1F; }
                }
                return dpi;
            }
        }

        public static int Px(int v) { return (int)Math.Round(v * Dpi * FontScale); }
        public static Font F(string family, float points) { return new Font(family, points * FontScale); }
    }
}
