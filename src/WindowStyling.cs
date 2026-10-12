using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Drawing;

namespace W3Forge {
  internal static class WindowStyling {
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    public static void Apply(Window window) {
      window.Background = Ui.Brush("#0E1013");
      window.Foreground = Ui.Brush("#F2F4F7");
      window.FontFamily = new System.Windows.Media.FontFamily("Inter, Segoe UI Variable, Segoe UI");
      window.FontSize = 13;
      window.SnapsToDevicePixels = true;
      window.UseLayoutRounding = true;

      try {
        var exe=Process.GetCurrentProcess().MainModule.FileName;
        using(var icon=Icon.ExtractAssociatedIcon(exe)) {
          if(icon!=null) {
            var source=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            window.Icon=source;
          }
        }
      } catch {}

      window.SourceInitialized += delegate {
        try {
          var hwnd = new WindowInteropHelper(window).Handle;
          int enabled = 1;
          if(DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref enabled, sizeof(int));
        } catch {}
      };
    }
  }
}