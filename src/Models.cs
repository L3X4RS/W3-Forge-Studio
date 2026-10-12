using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace W3Forge {
  public sealed class Project {
    public string FilePath {get;private set;}
    public string Name {get;private set;}
    public string GameBin {get;set;}
    public string Root {get{return Path.Combine(Path.GetDirectoryName(FilePath),Name);}}
    public string Files {get{return Path.Combine(Root,"files");}}
    public string ModBundle {get{return Path.Combine(Files,"Mod","Bundle");}}
    public string TextureCache {get{return Path.Combine(Files,"Mod","TextureCache","texture.cache");}}
    public string RawFiles {get{return Path.Combine(Files,"Raw");}}
    public string LooseFiles {get{return Path.Combine(Files,"Loose");}}
    public string Workspace {get{return Path.Combine(Root,"workspace");}}
    public string Output {get{return Path.Combine(Workspace,"build");}}
    public string ModName { get {string x=new string(Name.Where(c=>char.IsLetterOrDigit(c)||c=='_').ToArray());return x.StartsWith("mod",StringComparison.OrdinalIgnoreCase)?x:"mod"+x;} }

    public static bool IsIgnoredBuildFile(string file){
      if(String.IsNullOrWhiteSpace(file))return true;
      var name=Path.GetFileName(file);
      if(name.EndsWith(".bak",StringComparison.OrdinalIgnoreCase))return true;
      if(name.IndexOf(".w3forge_",StringComparison.OrdinalIgnoreCase)>=0)return true;
      if(name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase))return true;
      return false;
    }
    static bool HasFiles(string root){
      try{return Directory.Exists(root)&&Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).Any(x=>!IsIgnoredBuildFile(x));}
      catch{return false;}
    }
    static bool IsUnder(string file,string root){
      try{
        var f=Path.GetFullPath(file);
        var r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        return f.StartsWith(r,StringComparison.OrdinalIgnoreCase);
      }catch{return false;}
    }
    public IEnumerable<string> LegacyBundleFiles(){
      if(!Directory.Exists(Files))yield break;
      foreach(var file in Directory.EnumerateFiles(Files,"*",SearchOption.AllDirectories)){
        if(IsUnder(file,Path.Combine(Files,"Mod")))continue;
        if(IsUnder(file,RawFiles))continue;
        if(IsUnder(file,LooseFiles))continue;
        if(IsIgnoredBuildFile(file))continue;
        yield return file;
      }
    }
    public bool HasManagedBundleFiles { get { return HasFiles(ModBundle); } }
    public bool HasLegacyBundleFiles {
      get {
        try{using(var e=LegacyBundleFiles().GetEnumerator())return e.MoveNext();}
        catch{return false;}
      }
    }
    public bool HasLooseModFiles { get { return HasFiles(RawFiles); } }
    public bool HasBuildableResources { get { return HasManagedBundleFiles||HasLegacyBundleFiles||HasLooseModFiles; } }
    public bool UsesLegacyFlatLayout { get { return HasLegacyBundleFiles; } }
    public string AssetImportRoot {
      get {
        if(HasLegacyBundleFiles)return Files;
        if(HasManagedBundleFiles)return ModBundle;
        return Files;
      }
    }

    static Project(){ }
    public static Project Create(string parent,string name,string gameBin) {
      if(String.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name=="."||name=="..")throw new ArgumentException("Project name is invalid.");
      Directory.CreateDirectory(parent);
      var file=Path.Combine(parent,name+".w3modproj");
      if(File.Exists(file)) throw new IOException("A project with this name already exists.");
      var p=new Project{FilePath=file,Name=name,GameBin=gameBin};
      p.EnsureFolders();p.Save(); return p;
    }
    public static Project Open(string file){
      var doc=XDocument.Load(file);
      if(doc.Root==null || doc.Root.Name.LocalName!="W3Mod")throw new InvalidDataException("Not a W3Edit/WolvenKit .w3modproj.");
      Func<string,string> get=k=>doc.Root.Elements().FirstOrDefault(x=>x.Name.LocalName==k)?.Value??"";
      var n=get("Name"); if(String.IsNullOrWhiteSpace(n))n=Path.GetFileNameWithoutExtension(file);
      if(n.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new InvalidDataException("Project name is invalid.");
      var p=new Project{FilePath=Path.GetFullPath(file),Name=n,GameBin=get("GameBin")};
      p.EnsureFolders();
      return p;
    }
    public void Save(){
      var doc=File.Exists(FilePath)?XDocument.Load(FilePath):new XDocument(new XElement("W3Mod"));
      var root=doc.Root??new XElement("W3Mod");
      if(doc.Root==null)doc.Add(root);
      Action<string,string> put=(k,val)=>{var el=root.Element(k);if(el==null)root.Add(new XElement(k,val??""));else el.Value=val??"";};
      put("Name",Name);put("version","1.1");put("GameBin",GameBin);
      doc.Save(FilePath);
    }
    public void EnsureFolders(){
      foreach(var dir in new[]{Files,ModBundle,Path.Combine(Files,"Mod","TextureCache"),Path.Combine(Files,"DLC"),RawFiles,LooseFiles,Workspace,Output})Directory.CreateDirectory(dir);
    }
  }

  public static class EnvironmentPaths {
    public static string AppDataRoot {
      get {
        var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"W3 Forge Studio");
        Directory.CreateDirectory(root);
        return root;
      }
    }
    public static string SettingsFile { get { return Path.Combine(AppDataRoot,"preferences.ini"); } }
    public static string ErrorLog { get { return Path.Combine(AppDataRoot,"errors.log"); } }
    public static string CacheRoot {
      get {
        var p=Path.Combine(AppDataRoot,"Cache");
        Directory.CreateDirectory(p);
        return p;
      }
    }

    public static string GameRootFromBin(string input){
      if(String.IsNullOrWhiteSpace(input))return null;
      try{
        var path=Path.GetFullPath(input.Trim().Trim('"'));
        if(File.Exists(path)&&Path.GetFileName(path).Equals("witcher3.exe",StringComparison.OrdinalIgnoreCase))path=Path.GetDirectoryName(path);
        var folder=new DirectoryInfo(path);
        if(folder.Name.Equals("x64_dx12",StringComparison.OrdinalIgnoreCase)||folder.Name.Equals("x64",StringComparison.OrdinalIgnoreCase)){
          return folder.Parent?.Parent?.FullName;
        }
        if(File.Exists(Path.Combine(path,"bin","x64_dx12","witcher3.exe"))||File.Exists(Path.Combine(path,"bin","x64","witcher3.exe")))return path;
      }catch{}
      return null;
    }

    public static string ValidateGameCandidate(string candidate){
      if(String.IsNullOrWhiteSpace(candidate))return null;
      try{
        candidate=Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')).Replace('/','\\');
        if(File.Exists(candidate)&&Path.GetFileName(candidate).Equals("witcher3.exe",StringComparison.OrdinalIgnoreCase))
          candidate=Path.GetDirectoryName(candidate);

        if(Directory.Exists(candidate)){
          if(File.Exists(Path.Combine(candidate,"witcher3.exe"))){
            var n=Path.GetFileName(candidate);
            if(n.Equals("x64_dx12",StringComparison.OrdinalIgnoreCase)||n.Equals("x64",StringComparison.OrdinalIgnoreCase))
              return Path.GetFullPath(candidate);
          }

          var dx12=Path.Combine(candidate,"bin","x64_dx12");
          if(File.Exists(Path.Combine(dx12,"witcher3.exe")))return dx12;
          var x64=Path.Combine(candidate,"bin","x64");
          if(File.Exists(Path.Combine(x64,"witcher3.exe")))return x64;
        }
      }catch{}
      return null;
    }

    static string RegValue(RegistryHive hive,RegistryView view,string key,string name){
      try{
        using(var baseKey=RegistryKey.OpenBaseKey(hive,view))
        using(var k=baseKey.OpenSubKey(key))
          return k?.GetValue(name) as string;
      }catch{return null;}
    }

    static IEnumerable<string> SteamRoots(){
      var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      Action<string> add=p=>{
        if(String.IsNullOrWhiteSpace(p))return;
        try{
          p=Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')).Replace('/','\\');
          if(Directory.Exists(p))seen.Add(Path.GetFullPath(p));
        }catch{}
      };

      foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
        add(RegValue(RegistryHive.CurrentUser,view,@"SOFTWARE\Valve\Steam","SteamPath"));
        add(RegValue(RegistryHive.LocalMachine,view,@"SOFTWARE\Valve\Steam","InstallPath"));
      }
      add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
      add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam"));

      foreach(var steam in seen.ToArray()){
        var vdf=Path.Combine(steam,"steamapps","libraryfolders.vdf");
        if(!File.Exists(vdf))continue;
        try{
          var text=File.ReadAllText(vdf);
          foreach(Match m in Regex.Matches(text,"\"path\"\\s*\"([^\"]+)\"",RegexOptions.IgnoreCase))
            add(m.Groups[1].Value.Replace(@"\\",@"\"));
          foreach(Match m in Regex.Matches(text,"(?m)^\\s*\"\\d+\"\\s*\"([A-Za-z]:[^\"]+)\"\\s*$"))
            add(m.Groups[1].Value.Replace(@"\\",@"\"));
        }catch{}
      }
      return seen;
    }

    static IEnumerable<string> GogRoots(){
      var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
      foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
        try{
          using(var baseKey=RegistryKey.OpenBaseKey(hive,view))
          using(var games=baseKey.OpenSubKey(@"SOFTWARE\GOG.com\Games")){
            if(games==null)continue;
            foreach(var sub in games.GetSubKeyNames()){
              using(var g=games.OpenSubKey(sub)){
                var name=Convert.ToString(g?.GetValue("gameName")??g?.GetValue("name")??"");
                var path=Convert.ToString(g?.GetValue("path")??g?.GetValue("PATH")??"");
                if(name.IndexOf("Witcher 3",StringComparison.OrdinalIgnoreCase)>=0&&!String.IsNullOrWhiteSpace(path))roots.Add(path);
              }
            }
          }
        }catch{}
      }
      return roots;
    }

    static IEnumerable<string> UninstallInstallLocations(string displayContains){
      var result=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
      foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}){
        try{
          using(var baseKey=RegistryKey.OpenBaseKey(hive,view))
          using(var uninstall=baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")){
            if(uninstall==null)continue;
            foreach(var sub in uninstall.GetSubKeyNames()){
              try{
                using(var k=uninstall.OpenSubKey(sub)){
                  var name=Convert.ToString(k?.GetValue("DisplayName")??"");
                  if(name.IndexOf(displayContains,StringComparison.OrdinalIgnoreCase)<0)continue;
                  var location=Convert.ToString(k?.GetValue("InstallLocation")??"").Trim().Trim('"');
                  if(!String.IsNullOrWhiteSpace(location))result.Add(location);
                }
              }catch{}
            }
          }
        }catch{}
      }
      return result;
    }

    public static string FindGame(){
      var candidates=new List<string>();

      foreach(var steam in SteamRoots())
        candidates.Add(Path.Combine(steam,"steamapps","common","The Witcher 3"));

      candidates.AddRange(GogRoots());
      candidates.AddRange(UninstallInstallLocations("Witcher 3").Where(x=>x.IndexOf("Mod Tools",StringComparison.OrdinalIgnoreCase)<0));

      foreach(var drive in DriveInfo.GetDrives()){
        try{
          if(!drive.IsReady)continue;
          var r=drive.RootDirectory.FullName;
          candidates.Add(Path.Combine(r,"SteamLibrary","steamapps","common","The Witcher 3"));
          candidates.Add(Path.Combine(r,"GOG Games","The Witcher 3 Wild Hunt"));
          candidates.Add(Path.Combine(r,"GOG Games","The Witcher 3 Wild Hunt GOTY"));
          candidates.Add(Path.Combine(r,"Games","The Witcher 3"));
        }catch{}
      }

      foreach(var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)){
        var valid=ValidateGameCandidate(candidate);
        if(valid!=null)return valid;
      }
      return null;
    }

  }

  public sealed class Preferences {
    public string GameBin {get;set;}=EnvironmentPaths.FindGame()??"";
    public string LastProject {get;set;}="";
    public static Preferences Load(){
      var p=new Preferences();
      if(File.Exists(EnvironmentPaths.SettingsFile)){
        foreach(var line in File.ReadAllLines(EnvironmentPaths.SettingsFile)){
          int i=line.IndexOf('=');if(i<1)continue;
          var key=line.Substring(0,i);var value=line.Substring(i+1);
          if(key=="GameBin")p.GameBin=value;
          if(key=="LastProject")p.LastProject=value;
        }
      }

      if(EnvironmentPaths.ValidateGameCandidate(p.GameBin)==null)p.GameBin=EnvironmentPaths.FindGame()??"";
      return p;
    }
    public void Save(){
      Directory.CreateDirectory(EnvironmentPaths.AppDataRoot);
      File.WriteAllLines(EnvironmentPaths.SettingsFile,new[]{"GameBin="+GameBin,"LastProject="+LastProject});
    }
  }
}