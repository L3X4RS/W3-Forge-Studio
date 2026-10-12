using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace W3Forge {
  public static class Deployment {
    public static string OutputModFolder(Project p)=>Path.Combine(p.Output,p.ModName);
    public static string LiveModFolder(Project p,string gameBin)=>Path.Combine(EnvironmentPaths.GameRootFromBin(gameBin)??throw new DirectoryNotFoundException("Select a valid Witcher 3 installation."),"mods",p.ModName);

    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CreateHardLink(string newFileName,string existingFileName,IntPtr securityAttributes);

    public static Task<string> BuildAsync(Project p,Action<string> log)=>Task.Run(()=>Build(p,log));
    public static string Build(Project p,Action<string> log){
      if(p==null)throw new InvalidOperationException("Open a project first.");
      if(!p.HasBuildableResources)
        throw new InvalidOperationException("Project is empty. Add files from Game Assets first.");

      string staged=null;
      var input=ResolveBuildInput(p,log,out staged);

      var output=OutputModFolder(p);
      var content=Path.Combine(output,"content");
      if(Directory.Exists(output))Directory.Delete(output,true);
      Directory.CreateDirectory(content);

      try{
        log?.Invoke("Native Build started.");
        if(p.HasManagedBundleFiles||p.HasLegacyBundleFiles){
          ValidateXmlResources(input,log);
          var packResult=NativePacker.Pack(input,content,log);
          NativePacker.VerifyXmlRoundTrip(input,packResult.BlobBundle,log);
        }else{
          log?.Invoke("Script-only project: no cooked bundle required; including loose scripts.");
        }

        if(File.Exists(p.TextureCache)){
          File.Copy(p.TextureCache,Path.Combine(content,"texture.cache"),true);
          log?.Invoke("Included texture.cache.");
        }

        if(Directory.Exists(p.RawFiles) && Directory.EnumerateFiles(p.RawFiles,"*",SearchOption.AllDirectories).Any()){
          CopyFolder(p.RawFiles,output);
          log?.Invoke("Included loose/passthrough mod files.");
        }

        CheckPack(output);
        log?.Invoke("Native Build complete: "+output);
        return output;
      }finally{
        if(!String.IsNullOrWhiteSpace(staged)){
          try{if(Directory.Exists(staged))Directory.Delete(staged,true);}catch{}
        }
      }
    }

    static string ResolveBuildInput(Project p,Action<string> log,out string staged){
      staged=null;

      if(p.HasManagedBundleFiles&&!p.HasLegacyBundleFiles){
        log?.Invoke("Project layout: W3 Forge managed bundle.");
        return p.ModBundle;
      }

      if(!p.HasLegacyBundleFiles){
        if(p.HasLooseModFiles)return p.Files;
        throw new InvalidOperationException("Project has no resources or scripts to build.");
      }

      staged=Path.Combine(p.Workspace,".w3forge_build_input");
      if(Directory.Exists(staged))Directory.Delete(staged,true);
      Directory.CreateDirectory(staged);

      int legacyCount=0;
      foreach(var file in p.LegacyBundleFiles()){
        var relative=Relative(file,p.Files);
        LinkIntoBuildStage(file,Path.Combine(staged,relative));
        legacyCount++;
      }

      int managedCount=0;
      if(p.HasManagedBundleFiles){
        foreach(var file in Directory.EnumerateFiles(p.ModBundle,"*",SearchOption.AllDirectories).Where(x=>!Project.IsIgnoredBuildFile(x))){
          var relative=Relative(file,p.ModBundle);
          var target=Path.Combine(staged,relative);
          if(File.Exists(target))
            throw new InvalidDataException("The same depot path exists in both project layouts: "+relative);
          LinkIntoBuildStage(file,target);
          managedCount++;
        }
      }

      log?.Invoke("Project layout: compatible W3Edit/WolvenKit flat files ("+legacyCount+" resources"+
        (managedCount>0?", plus "+managedCount+" managed resources":"")+").");
      return staged;
    }

    static string Relative(string file,string root){
      var basePath=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
      var full=Path.GetFullPath(file);
      if(!full.StartsWith(basePath,StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Project file is outside the expected source root: "+file);
      return full.Substring(basePath.Length);
    }

    static void ValidateXmlResources(string input,Action<string> log){
      var xml=Directory.GetFiles(input,"*.xml",SearchOption.AllDirectories)
        .Where(x=>!Project.IsIgnoredBuildFile(x))
        .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)
        .ToArray();
      if(xml.Length==0)return;

      int trophies=0;
      foreach(var file in xml){
        var format=TextResourceIO.ValidateXmlFile(file);
        string rel=Relative(file,input).Replace('/','\\').ToLowerInvariant();
        if(Path.GetFileName(file).Equals("def_item_trophies.xml",StringComparison.OrdinalIgnoreCase)){
          trophies++;
          string scope=rel.StartsWith("dlc\\bob\\data\\",StringComparison.OrdinalIgnoreCase)
            ?"Blood and Wine"
            :rel.StartsWith("dlc\\ep1\\data\\",StringComparison.OrdinalIgnoreCase)
              ?"Hearts of Stone"
              :"Base game";
          string tree=rel.IndexOf("\\items_plus\\",StringComparison.OrdinalIgnoreCase)>=0?"items_plus":"items";
          log?.Invoke("TROPHY XML target: "+scope+" / "+tree+" / "+rel);
          log?.Invoke("  Note: The game contains separate items and items_plus trophy XMLs. If the tested gameplay uses the other tree, edit/include its matching definition too.");
        }
        log?.Invoke("XML valid: "+rel+" ["+TextResourceIO.Describe(format)+"]");
      }
      log?.Invoke("XML pre-build validation PASS: "+xml.Length+" file"+(xml.Length==1?"":"s")+
        (trophies>0?" ("+trophies+" trophy definition"+(trophies==1?"":"s")+")":"")+".");
    }

    static void LinkIntoBuildStage(string source,string target){
      Directory.CreateDirectory(Path.GetDirectoryName(target));
      if(File.Exists(target))throw new IOException("Duplicate build resource: "+target);
      if(!CreateHardLink(target,source,IntPtr.Zero))File.Copy(source,target,false);
    }


    public static Task<string> InstallAsync(Project p,string gameBin,Action<string> log)=>Task.Run(()=>Install(p,gameBin,log));
    public static string Install(Project p,string gameBin,Action<string> log){
      if(Process.GetProcessesByName("witcher3").Length>0)throw new InvalidOperationException("Close The Witcher 3 before installing mods.");
      string src=OutputModFolder(p);CheckPack(src);
      string dest=LiveModFolder(p,gameBin);
      string mods=Path.GetDirectoryName(dest);
      Directory.CreateDirectory(mods);
      string staging=Path.Combine(mods,p.ModName+".w3forge_stage");
      string prev=Path.Combine(mods,p.ModName+".w3forge_previous");
      if(Directory.Exists(staging)||Directory.Exists(prev))
        throw new IOException("A previous installation was interrupted. Inspect .w3forge_stage/.w3forge_previous in mods before retrying.");
      CopyFolder(src,staging);
      CheckPack(staging);
      bool hadOld=Directory.Exists(dest);
      string backup=null;
      if(hadOld) {
        backup=Path.Combine(p.Workspace,"backups",p.ModName+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(Path.GetDirectoryName(backup));
        CopyFolder(dest,backup);
        log?.Invoke("Backup saved: "+backup);
      }
      try {
        if(hadOld)Directory.Move(dest,prev);
        Directory.Move(staging,dest);
        CheckPack(dest);
      }catch{
        if(Directory.Exists(dest))Directory.Delete(dest,true);
        if(Directory.Exists(prev))Directory.Move(prev,dest);
        if(Directory.Exists(staging))Directory.Delete(staging,true);
        throw;
      }
      if(Directory.Exists(prev)) Directory.Delete(prev,true);
      log?.Invoke("Installed in the game: "+dest);
      return dest;
    }

    public static Task<bool> UninstallAsync(Project p,string gameBin,Action<string> log)=>Task.Run(()=>Uninstall(p,gameBin,log));
    public static bool Uninstall(Project p,string gameBin,Action<string> log){
      if(Process.GetProcessesByName("witcher3").Length>0)throw new InvalidOperationException("Close The Witcher 3 before uninstalling.");
      var dest=LiveModFolder(p,gameBin);
      if(!Directory.Exists(dest))return false;
      var backup=Path.Combine(p.Workspace,"backups",p.ModName+"_removed_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));
      Directory.CreateDirectory(Path.GetDirectoryName(backup));
      CopyFolder(dest,backup);
      Directory.Delete(dest,true);
      log?.Invoke("Uninstalled; backup saved at "+backup);
      return true;
    }

    public static void CheckPack(string folder){
      var content=Path.Combine(folder,"content");
      bool meta=File.Exists(Path.Combine(content,"metadata.store"));
      bool bundle=File.Exists(Path.Combine(content,"blob0.bundle"));
      if(meta!=bundle)throw new InvalidDataException("Incomplete cooked bundle: metadata.store and blob0.bundle must both exist.");
      if(!meta){
        var scripts=Path.Combine(content,"scripts");
        if(!Directory.Exists(scripts)||!Directory.EnumerateFiles(scripts,"*.ws",SearchOption.AllDirectories).Any())
          throw new InvalidDataException("No cooked resources and no installable WitcherScript .ws files.");
        return;
      }
      if(new FileInfo(Path.Combine(content,"metadata.store")).Length<32)throw new InvalidDataException("metadata.store is invalid");
      if(new FileInfo(Path.Combine(content,"blob0.bundle")).Length<32)throw new InvalidDataException("blob0.bundle is invalid");
    }

    public static void CopyFolder(string src,string dest){
      Directory.CreateDirectory(dest);
      foreach(var f in Directory.GetFiles(src)){
        if(Project.IsIgnoredBuildFile(f))continue;
        File.Copy(f,Path.Combine(dest,Path.GetFileName(f)),true);
      }
      foreach(var d in Directory.GetDirectories(src))CopyFolder(d,Path.Combine(dest,Path.GetFileName(d)));
    }
  }
}