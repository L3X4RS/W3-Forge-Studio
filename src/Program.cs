using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace W3Forge {
  public static class Program {
    [STAThread] public static void Main(string[] args) {
      // Do not remove Windows Mark-of-the-Web or antivirus metadata. Keep
      // executable security decisions under the user and Windows' control.
      AppDomain.CurrentDomain.AssemblyResolve += ResolveRuntimeAssembly;

      try {
        VerifyRuntimeFiles();
        PreloadRuntimeAssemblies();

        // The embedded legacy WinForms editor runs inside the WPF shell. Keep its
        // event exceptions under W3 Forge's handler instead of letting WinForms
        // show the old JIT crash dialog. The known legacy layout write is already
        // disabled in Editors.cs; this is a final safety net for protected installs.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (s,e) => {
          LogException(e.Exception);
          if(IsLegacyLayoutAccessError(e.Exception))return;
          MessageBox.Show(e.Exception.Message,"W3 Forge Studio",MessageBoxButton.OK,MessageBoxImage.Error);
        };

        var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
        var theme=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Theme.xaml");
        if(File.Exists(theme)){
          using(var stream=File.OpenRead(theme))
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(stream));
        }

        AppDomain.CurrentDomain.UnhandledException += (s,e) => LogException(e.ExceptionObject as Exception);
        app.DispatcherUnhandledException += (s,e) => {
          LogException(e.Exception);
          MessageBox.Show(e.Exception.Message,"W3 Forge Studio",MessageBoxButton.OK,MessageBoxImage.Error);
          e.Handled=true;
        };

        // Instantiate the main window only after the legacy W3Edit runtime has
        // been loaded. Using reflection here prevents the CLR from resolving
        // W3Edit.Bundles transitively while JIT-compiling Main on a clean PC.
        var mainType=typeof(Program).Assembly.GetType("W3Forge.MainWindow",true);
        var win=(Window)Activator.CreateInstance(mainType);
        app.Run(win);
      } catch(Exception ex) {
        LogException(ex);
        MessageBox.Show(ex.Message,"W3 Forge Studio startup",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }

    static void VerifyRuntimeFiles() {
      var appDir=AppDomain.CurrentDomain.BaseDirectory;
      var runtime=Path.Combine(appDir,"Runtime");
      var needed=new[]{
        "W3Edit.Bundles.dll", "W3Edit.CR2W.dll", "W3Edit.W3Strings.dll", "W3Edit.exe",
        "LZ4.dll", "Doboz.dll", "Ionic.Zlib.dll", "Pfim.dll", "ObjectListView.dll",
        "WeifenLuo.WinFormsUI.Docking.dll", "cr2wdocument_layout.xml"
      };
      var missing=new System.Collections.Generic.List<string>();
      foreach(var name in needed) {
        if(!File.Exists(Path.Combine(runtime,name)))missing.Add("Runtime\\"+name);
      }
      if(!File.Exists(Path.Combine(appDir,"W3ForgeStudio.exe.config")))
        missing.Add("W3ForgeStudio.exe.config");
      if(missing.Count>0)
        throw new FileNotFoundException(
          "The W3 Forge Studio installation is incomplete.\n\nMissing files:\n"+
          String.Join("\n",missing.ToArray())+
          "\n\nExtract the COMPLETE W3 Forge Studio ZIP to a new folder, "+
          "keeping the Runtime folder next to W3ForgeStudio.exe. "+
          "Do not run the executable from inside the ZIP.");
    }

    static void PreloadRuntimeAssemblies() {
      var runtime=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Runtime");

      // Load native packer / bundle dependencies first, then W3Edit libraries.
      // This makes startup deterministic on clean systems instead of relying
      // exclusively on CLR probing or AssemblyResolve timing.
      var order=new[]{
        "LZ4.dll",
        "Doboz.dll",
        "Ionic.Zlib.dll",
        "Pfim.dll",
        "W3Edit.W3Strings.dll",
        "W3Edit.CR2W.dll",
        "W3Edit.Bundles.dll",
        "WeifenLuo.WinFormsUI.Docking.dll",
        "ObjectListView.dll"
      };

      foreach(var name in order) {
        var path=Path.Combine(runtime,name);
        try {
          Assembly.LoadFrom(path);
        } catch(Exception ex) {
          throw new FileLoadException(
            "W3 Forge Studio could not load Runtime\\"+name+
            ".\n\nMake sure the complete application folder was extracted and "+
            "that Windows or antivirus software did not remove/block files inside Runtime.\n\n"+
            ((ex.HResult==unchecked((int)0x80131515) || ex.Message.IndexOf("Operation is not supported",StringComparison.OrdinalIgnoreCase)>=0)
              ? "Windows marked one or more extracted files as downloaded from the Internet. W3 Forge Studio tried to unblock them automatically, but Windows still refused the load. As a fallback, right-click the original ZIP, choose Properties > Unblock, then extract it again to a new folder.\n\n"
              : "")+
            "Technical detail: "+ex.Message,
            path,
            ex);
        }
      }
    }

    static Assembly ResolveRuntimeAssembly(object sender,ResolveEventArgs args) {
      try {
        var simple=new AssemblyName(args.Name).Name;
        var runtime=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Runtime");
        foreach(var ext in new[]{".dll",".exe"}) {
          var candidate=Path.Combine(runtime,simple+ext);
          if(File.Exists(candidate))return Assembly.LoadFrom(candidate);
        }
      } catch(Exception ex) { LogException(ex); }
      return null;
    }

    static bool IsLegacyLayoutAccessError(Exception ex) {
      if(ex==null)return false;
      var unauthorized=ex as UnauthorizedAccessException;
      if(unauthorized==null && ex.InnerException!=null)return IsLegacyLayoutAccessError(ex.InnerException);
      return unauthorized!=null && unauthorized.Message.IndexOf("cr2wdocument_layout.xml",StringComparison.OrdinalIgnoreCase)>=0;
    }

    internal static void LogException(Exception ex) {
      if(ex==null)return;
      try {
        Directory.CreateDirectory(EnvironmentPaths.AppDataRoot);
        File.AppendAllText(EnvironmentPaths.ErrorLog,DateTime.Now.ToString("s")+" "+ex+"\r\n");
      } catch {}
    }
  }
}