using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace W3Forge {
  public enum W3ConsoleKey {
    Tilde = 0xC0,
    Apostrophe = 0xDE,
    F2 = 0x71
  }

  public sealed class LiveEnvBridgeResult {
    public string ModFolder {get;internal set;}
    public string ScriptFile {get;internal set;}
    public int EnvFilesScanned {get;internal set;}
    public int ParametersGenerated {get;internal set;}
    public bool UsedExistingSharedImports {get;internal set;}
  }

  public static class LiveEnvBridge {
    const string BridgeModName="modW3ForgeLiveEnvBridge";

    public static string GameRoot(string gameBin) {
      return EnvironmentPaths.GameRootFromBin(gameBin);
    }

    public static string BridgeFolder(string gameBin) {
      var root=GameRoot(gameBin);
      return root==null?null:Path.Combine(root,"mods",BridgeModName);
    }

    public static bool IsInstalled(string gameBin) {
      var root=BridgeFolder(gameBin);
      return !String.IsNullOrWhiteSpace(root)&&File.Exists(Path.Combine(root,"content","scripts","local","w3forge_live_env.ws"));
    }

    public static LiveEnvBridgeResult Install(Project project,string gameBin,Action<string> log) {
      if(project==null)throw new InvalidOperationException("Open a project before installing the Live ENV bridge.");
      var root=GameRoot(gameBin);
      if(String.IsNullOrWhiteSpace(root)||!Directory.Exists(root))throw new DirectoryNotFoundException("Select a valid Witcher 3 installation.");
      if(Process.GetProcessesByName("witcher3").Length>0)
        throw new InvalidOperationException("Close The Witcher 3 before installing or regenerating the Live ENV bridge.");

      var envFiles=Directory.Exists(project.Files)
        ?Directory.GetFiles(project.Files,"*.env",SearchOption.AllDirectories)
          .Where(x=>x.IndexOf(Path.DirectorySeparatorChar+"Raw"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)<0)
          .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray()
        :new string[0];
      if(envFiles.Length==0)throw new InvalidOperationException("The current project does not contain any .env resources.");

      var parameters=new Dictionary<string,EnvParameter>(StringComparer.OrdinalIgnoreCase);
      var imports=new Dictionary<string,EnvImportType>(StringComparer.OrdinalIgnoreCase);
      int loaded=0;
      foreach(var file in envFiles) {
        try {
          var doc=EnvDocument.Load(file);
          loaded++;
          foreach(var p in doc.Parameters) {
            if(p.Kind==EnvValueKind.Int)continue; // live bridge only emits types proven safe in WitcherScript.
            string key=p.Group+"|"+p.Path+"|"+p.Kind;
            if(!parameters.ContainsKey(key))parameters[key]=p;
          }
          MergeImports(imports,doc.ImportTypes);
        } catch(Exception ex) {
          log?.Invoke("Live ENV schema skipped "+file+": "+ex.Message);
        }
      }
      if(loaded==0||parameters.Count==0)throw new InvalidDataException("No editable ENV schema could be generated from this project.");

      bool external=HasSharedEnvironmentImports(root);
      string source=GenerateScript(parameters.Values.ToList(),imports,!external);

      string mod=Path.Combine(root,"mods",BridgeModName);
      string script=Path.Combine(mod,"content","scripts","local","w3forge_live_env.ws");
      Directory.CreateDirectory(Path.GetDirectoryName(script));
      if(File.Exists(script))File.Copy(script,script+".previous",true);
      File.WriteAllText(script,source,new UTF8Encoding(false));

      File.WriteAllText(Path.Combine(mod,"W3ForgeLiveEnv.txt"),
        "W3 Forge Studio Live ENV Bridge\r\n"+
        "Generated: "+DateTime.Now.ToString("s")+"\r\n"+
        "ENV files scanned: "+loaded+"\r\n"+
        "Live parameters: "+parameters.Count+"\r\n"+
        "Imports: "+(external?"existing Shared Imports":"W3 Forge generated minimal imports")+"\r\n",
        Encoding.UTF8);

      EnsureDebugConsole(root,log);
      log?.Invoke("Live ENV bridge generated: "+script);
      log?.Invoke("Live ENV schema: "+loaded+" ENV files, "+parameters.Count+" live parameters.");

      return new LiveEnvBridgeResult{
        ModFolder=mod,ScriptFile=script,EnvFilesScanned=loaded,
        ParametersGenerated=parameters.Count,UsedExistingSharedImports=external
      };
    }

    // Validates/generates the complete bridge in the project workspace without
    // writing to game mods, enabling the console, or executing WitcherScript.
    public static string ExportPreview(Project project,Action<string> log) {
      if(project==null)throw new InvalidOperationException("Open a project first.");
      if(!Directory.Exists(project.Files))throw new DirectoryNotFoundException("Project files are missing.");
      var envFiles=Directory.GetFiles(project.Files,"*.env",SearchOption.AllDirectories)
        .Where(x=>x.IndexOf(Path.DirectorySeparatorChar+"Raw"+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)<0)
        .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
      if(envFiles.Length==0)throw new InvalidOperationException("No ENV files found in the project.");
      var parameters=new Dictionary<string,EnvParameter>(StringComparer.OrdinalIgnoreCase);
      var imports=new Dictionary<string,EnvImportType>(StringComparer.OrdinalIgnoreCase);
      int loaded=0;
      foreach(var envFile in envFiles) {
        try {
          var doc=EnvDocument.Load(envFile);
          loaded++;
          foreach(var p in doc.Parameters) {
            if(p.Kind==EnvValueKind.Int)continue;
            string key=p.Group+"|"+p.Path+"|"+p.Kind;
            if(!parameters.ContainsKey(key))parameters[key]=p;
          }
          MergeImports(imports,doc.ImportTypes);
        } catch(Exception ex){log?.Invoke("Offline bridge skipped "+envFile+": "+ex.Message);}
      }
      if(loaded==0||parameters.Count==0)throw new InvalidDataException("No compatible ENV parameters.");
      string output=Path.Combine(project.Workspace,"live_env_lab","w3forge_live_env_preview.ws");
      Directory.CreateDirectory(Path.GetDirectoryName(output));
      File.WriteAllText(output,GenerateScript(parameters.Values.ToList(),imports,true),new UTF8Encoding(false));
      log?.Invoke("OFFLINE WitcherScript preview only: "+output+" ("+loaded+" ENVs, "+parameters.Count+" parameters).");
      return output;
    }

    public static void Uninstall(string gameBin,Action<string> log) {
      var folder=BridgeFolder(gameBin);
      if(String.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))return;
      if(Process.GetProcessesByName("witcher3").Length>0)
        throw new InvalidOperationException("Close The Witcher 3 before removing the Live ENV bridge.");
      Directory.Delete(folder,true);
      log?.Invoke("Live ENV bridge removed: "+folder);
    }

    static void MergeImports(Dictionary<string,EnvImportType> target,IReadOnlyDictionary<string,EnvImportType> source) {
      foreach(var kv in source) {
        if(!target.TryGetValue(kv.Key,out var dest)) {
          dest=new EnvImportType{Name=kv.Value.Name,IsClass=kv.Value.IsClass};
          target[kv.Key]=dest;
        }
        if(kv.Value.IsClass)dest.IsClass=true;
        foreach(var f in kv.Value.Fields)
          if(!dest.Fields.ContainsKey(f.Key))dest.Fields[f.Key]=f.Value;
      }
    }

    static bool HasSharedEnvironmentImports(string gameRoot) {
      string shared=Path.Combine(gameRoot,"mods","modSharedImports");
      if(Directory.Exists(shared))return true;
      // Also accept merged/shared baseline imports when present.
      string mods=Path.Combine(gameRoot,"mods");
      if(!Directory.Exists(mods))return false;
      foreach(var file in Directory.GetFiles(mods,"*.ws",SearchOption.AllDirectories)) {
        if(file.IndexOf(BridgeModName,StringComparison.OrdinalIgnoreCase)>=0)continue;
        try {
          var text=File.ReadAllText(file);
          if(Regex.IsMatch(text,@"import\s+class\s+CEnvironmentDefinition\b",RegexOptions.IgnoreCase) &&
             Regex.IsMatch(text,@"import\s+var\s+envParams\b",RegexOptions.IgnoreCase))
            return true;
        } catch {}
      }
      return false;
    }

    static string GenerateScript(List<EnvParameter> parameters,Dictionary<string,EnvImportType> imports,bool includeImports) {
      var sb=new StringBuilder();
      sb.AppendLine("// W3 Forge Studio Live ENV Bridge");
      sb.AppendLine("// Generated from the active project's own REDengine ENV schema.");
      sb.AppendLine("// Do not edit while The Witcher 3 is running.");
      sb.AppendLine();

      if(includeImports) {
        foreach(var def in OrderImports(imports)) {
          if(def.Name=="Vector")continue;
          if(def.IsClass)
            sb.AppendLine("import class "+def.Name+(def.Name=="CEnvironmentDefinition"?" extends CResource":"")+" {");
          else
            sb.AppendLine("import struct "+def.Name+" {");
          foreach(var field in def.Fields.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)) {
            if(!ValidIdentifier(field.Key)||!ValidType(field.Value))continue;
            sb.AppendLine("    import var "+field.Key+" : "+NormalizeScriptType(field.Value)+";");
          }
          sb.AppendLine("}");
          sb.AppendLine();
        }
      }

      sb.AppendLine("function W3Forge_ActivateEnv(env : CEnvironmentDefinition) {");
      sb.AppendLine("    if (env) { theGame.SetEnvironmentID(ActivateEnvironmentDefinition(env, 100000, 1.0, 0.0)); }");
      sb.AppendLine("}");
      sb.AppendLine();
      sb.AppendLine("exec function w3forge_env_load(path : string) {");
      sb.AppendLine("    var env : CEnvironmentDefinition;");
      sb.AppendLine("    env = (CEnvironmentDefinition)LoadResource(path, true);");
      sb.AppendLine("    W3Forge_ActivateEnv(env);");
      sb.AppendLine("}");
      sb.AppendLine();

      AppendBoolFunction(sb,parameters.Where(x=>x.Kind==EnvValueKind.Bool));
      AppendFloatFunction(sb,parameters.Where(x=>x.Kind==EnvValueKind.Float));
      AppendCurveFunction(sb,parameters.Where(x=>x.Kind==EnvValueKind.Curve));

      sb.AppendLine("exec function w3forge_env_time(hour : float) { ForceFakeEnvTime(hour); }");
      sb.AppendLine("exec function w3forge_env_time_release() { DisableFakeEnvTime(); }");
      return sb.ToString();
    }

    static IEnumerable<EnvImportType> OrderImports(Dictionary<string,EnvImportType> imports) {
      // Leaf curve structs first is easier to inspect; WitcherScript accepts forward references.
      string[] preferred={"SCurveDataEntry","SSimpleCurve","CAreaEnvironmentParams","CEnvironmentDefinition"};
      return imports.Values.OrderBy(x=>{
        int i=Array.IndexOf(preferred,x.Name);
        return i<0?1:0;
      }).ThenBy(x=>x.Name=="CEnvironmentDefinition"?1:0)
        .ThenBy(x=>x.Name,StringComparer.OrdinalIgnoreCase);
    }

    static void AppendBoolFunction(StringBuilder sb,IEnumerable<EnvParameter> source) {
      var list=SafeParameters(source).ToList();
      sb.AppendLine("exec function w3forge_env_bool(path : string, group : string, id : string, value : bool) {");
      sb.AppendLine("    var env : CEnvironmentDefinition;");
      sb.AppendLine("    env = (CEnvironmentDefinition)LoadResource(path, true);");
      sb.AppendLine("    if (!env) { return; }");
      AppendSwitch(sb,list,(p,indent)=>{
        sb.AppendLine(indent+Expression(p)+" = value; break;");
      });
      sb.AppendLine("    W3Forge_ActivateEnv(env);");
      sb.AppendLine("}");
      sb.AppendLine();
    }

    static void AppendFloatFunction(StringBuilder sb,IEnumerable<EnvParameter> source) {
      var list=SafeParameters(source).ToList();
      sb.AppendLine("exec function w3forge_env_float(path : string, group : string, id : string, value : float) {");
      sb.AppendLine("    var env : CEnvironmentDefinition;");
      sb.AppendLine("    env = (CEnvironmentDefinition)LoadResource(path, true);");
      sb.AppendLine("    if (!env) { return; }");
      AppendSwitch(sb,list,(p,indent)=>{
        sb.AppendLine(indent+Expression(p)+" = value; break;");
      });
      sb.AppendLine("    W3Forge_ActivateEnv(env);");
      sb.AppendLine("}");
      sb.AppendLine();
    }

    static void AppendCurveFunction(StringBuilder sb,IEnumerable<EnvParameter> source) {
      var list=SafeParameters(source).ToList();
      sb.AppendLine("exec function w3forge_env_curve(path : string, group : string, id : string, point : int, scalar : float, x : float, y : float, z : float, w : float) {");
      sb.AppendLine("    var env : CEnvironmentDefinition;");
      sb.AppendLine("    var values : array<SCurveDataEntry>;");
      sb.AppendLine("    var cp : SCurveDataEntry;");
      sb.AppendLine("    env = (CEnvironmentDefinition)LoadResource(path, true);");
      sb.AppendLine("    if (!env) { return; }");
      AppendSwitch(sb,list,(p,indent)=>{
        string e=Expression(p)+".dataCurveValues";
        sb.AppendLine(indent+"values = "+e+";");
        sb.AppendLine(indent+"if (point >= 0 && point < values.Size()) {");
        sb.AppendLine(indent+"    cp = values[point];");
        sb.AppendLine(indent+"    cp.lue = scalar;");
        sb.AppendLine(indent+"    cp.ntrolPoint.X = x; cp.ntrolPoint.Y = y; cp.ntrolPoint.Z = z; cp.ntrolPoint.W = w;");
        sb.AppendLine(indent+"    values[point] = cp;");
        sb.AppendLine(indent+"    "+e+" = values;");
        sb.AppendLine(indent+"}");
        sb.AppendLine(indent+"break;");
      });
      sb.AppendLine("    W3Forge_ActivateEnv(env);");
      sb.AppendLine("}");
      sb.AppendLine();
    }

    static void AppendSwitch(StringBuilder sb,List<EnvParameter> list,Action<EnvParameter,string> body) {
      sb.AppendLine("    switch (group) {");
      foreach(var group in list.GroupBy(x=>x.Group).OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)) {
        sb.AppendLine("        case \""+group.Key+"\":");
        sb.AppendLine("            switch (id) {");
        foreach(var p in group.OrderBy(x=>x.Path,StringComparer.OrdinalIgnoreCase)) {
          sb.AppendLine("                case \""+p.Path+"\":");
          body(p,"                    ");
        }
        sb.AppendLine("            }");
        sb.AppendLine("            break;");
      }
      sb.AppendLine("    }");
    }

    static IEnumerable<EnvParameter> SafeParameters(IEnumerable<EnvParameter> source) {
      foreach(var p in source) {
        if(!ValidIdentifier(p.Group))continue;
        if(p.Path.Split('.').All(ValidIdentifier))yield return p;
      }
    }

    static string Expression(EnvParameter p) {
      return "env.envParams."+p.Group+"."+p.Path;
    }

    static bool ValidIdentifier(string value) {
      return !String.IsNullOrWhiteSpace(value)&&Regex.IsMatch(value,@"^[A-Za-z_][A-Za-z0-9_]*$");
    }
    static bool ValidType(string value) {
      return !String.IsNullOrWhiteSpace(value)&&Regex.IsMatch(value,@"^[A-Za-z_][A-Za-z0-9_]*(?:<[^>]+>)?$");
    }
    static string NormalizeScriptType(string type) {
      if(type.Equals("Bool",StringComparison.OrdinalIgnoreCase))return "bool";
      if(type.Equals("Float",StringComparison.OrdinalIgnoreCase))return "float";
      if(type.StartsWith("Uint",StringComparison.OrdinalIgnoreCase)||type.StartsWith("Int",StringComparison.OrdinalIgnoreCase))return "int";
      return type;
    }

    public static void EnsureDebugConsole(string gameRoot,Action<string> log) {
      string ini=Path.Combine(gameRoot,"bin","config","base","general.ini");
      if(!File.Exists(ini))throw new FileNotFoundException("Witcher 3 general.ini was not found.",ini);
      var lines=File.ReadAllLines(ini).ToList();
      int idx=lines.FindIndex(x=>Regex.IsMatch(x,@"^\s*DBGConsoleOn\s*=",RegexOptions.IgnoreCase));
      if(idx>=0) {
        if(!Regex.IsMatch(lines[idx],@"=\s*true\s*$",RegexOptions.IgnoreCase)) {
          File.Copy(ini,ini+".w3forge_backup",true);
          lines[idx]="DBGConsoleOn=true";
          File.WriteAllLines(ini,lines.ToArray());
          log?.Invoke("Enabled Witcher 3 debug console in general.ini.");
        }
      } else {
        File.Copy(ini,ini+".w3forge_backup",true);
        int general=lines.FindIndex(x=>x.Trim().Equals("[General]",StringComparison.OrdinalIgnoreCase));
        if(general<0){lines.Add("[General]");lines.Add("DBGConsoleOn=true");}
        else lines.Insert(general+1,"DBGConsoleOn=true");
        File.WriteAllLines(ini,lines.ToArray());
        log?.Invoke("Enabled Witcher 3 debug console in general.ini.");
      }
    }

    public static string CommandFor(Project project,string envFile,EnvParameter parameter,int point=-1) {
      if(project==null||parameter==null)throw new InvalidOperationException("Select an ENV parameter first.");
      string depot=EnvDocument.DepotPath(project,envFile);
      if(String.IsNullOrWhiteSpace(depot))throw new InvalidOperationException("ENV must be inside the current project.");
      string q=Quote(depot),g=Quote(parameter.Group),id=Quote(parameter.Path);
      if(parameter.Kind==EnvValueKind.Bool) {
        bool v=Convert.ToBoolean(EnvDocument.GetScalar(parameter.Variable),CultureInfo.InvariantCulture);
        return "w3forge_env_bool("+q+","+g+","+id+","+(v?"true":"false")+")";
      }
      if(parameter.Kind==EnvValueKind.Float) {
        float v=Convert.ToSingle(EnvDocument.GetScalar(parameter.Variable),CultureInfo.InvariantCulture);
        return "w3forge_env_float("+q+","+g+","+id+","+F(v)+")";
      }
      if(parameter.Kind==EnvValueKind.Curve) {
        if(point<0||point>=parameter.Points.Count)throw new InvalidOperationException("Select a curve point first.");
        var p=parameter.Points[point];
        return "w3forge_env_curve("+q+","+g+","+id+","+point+","+F(p.Scalar)+","+F(p.X)+","+F(p.Y)+","+F(p.Z)+","+F(p.W)+")";
      }
      throw new NotSupportedException("This ENV value type is not supported by the live bridge.");
    }

    public static string LoadCommand(Project project,string envFile) {
      var depot=EnvDocument.DepotPath(project,envFile);
      if(String.IsNullOrWhiteSpace(depot))throw new InvalidOperationException("ENV must be inside the current project.");
      return "w3forge_env_load("+Quote(depot)+")";
    }

    static string F(float v)=>v.ToString("0.########",CultureInfo.InvariantCulture);
    static string Quote(string s)=>"\""+(s??"").Replace("\"","\\\"")+"\"";

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
    [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,INPUT[] inputs,int size);

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public INPUTUNION data; }

    [StructLayout(LayoutKind.Explicit)]
    struct INPUTUNION {
      [FieldOffset(0)] public MOUSEINPUT mi;
      [FieldOffset(0)] public KEYBDINPUT ki;
      [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT {
      public int dx;
      public int dy;
      public uint mouseData;
      public uint dwFlags;
      public uint time;
      public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT {
      public ushort wVk;
      public ushort wScan;
      public uint dwFlags;
      public uint time;
      public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT {
      public uint uMsg;
      public ushort wParamL;
      public ushort wParamH;
    }

    const uint KEYEVENTF_KEYUP=0x0002;
    const uint KEYEVENTF_UNICODE=0x0004;
    const byte VK_RETURN=0x0D;

    public static void SendConsoleCommand(string command,W3ConsoleKey key,bool closeConsole=true) {
      if(String.IsNullOrWhiteSpace(command))return;
      var game=Process.GetProcessesByName("witcher3").FirstOrDefault(x=>x.MainWindowHandle!=IntPtr.Zero);
      if(game==null)throw new InvalidOperationException("The Witcher 3 is not running or its game window is not ready.");

      SetForegroundWindow(game.MainWindowHandle);
      Thread.Sleep(120);
      Tap((byte)key);
      Thread.Sleep(180);
      SendUnicode(command);
      Thread.Sleep(80);
      Tap(VK_RETURN);
      Thread.Sleep(120);
      if(closeConsole)Tap((byte)key);
    }

    static void Tap(byte key) {
      keybd_event(key,0,0,UIntPtr.Zero);
      Thread.Sleep(30);
      keybd_event(key,0,KEYEVENTF_KEYUP,UIntPtr.Zero);
    }

    static void SendUnicode(string text) {
      foreach(char c in text) {
        var down=new INPUT{type=1,data=new INPUTUNION{ki=new KEYBDINPUT{wVk=0,wScan=c,dwFlags=KEYEVENTF_UNICODE,time=0,dwExtraInfo=UIntPtr.Zero}}};
        var up=new INPUT{type=1,data=new INPUTUNION{ki=new KEYBDINPUT{wVk=0,wScan=c,dwFlags=KEYEVENTF_UNICODE|KEYEVENTF_KEYUP,time=0,dwExtraInfo=UIntPtr.Zero}}};
        var a=new[]{down,up};
        if(SendInput(2,a,Marshal.SizeOf(typeof(INPUT)))!=2)
          throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not send text to the Witcher 3 console.");
      }
    }
  }
}