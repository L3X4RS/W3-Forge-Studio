using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using W3Edit.Bundles;

namespace W3Forge {
  public sealed class GameAsset {
    internal BundleItem Raw;
    public string DepotPath {get;set;}
    public string Name=>Path.GetFileName(DepotPath.Replace('\\','/'));
    public string Extension=>Path.GetExtension(DepotPath).ToLowerInvariant();
    public string SizeText=>((Raw?.Size??0)/1024.0).ToString("N1")+" KB";
    public string Bundle=>Path.GetFileName(Raw?.Bundle?.FileName??"");
    public string Compression=>Raw?.CompressionType.ToString()??"";
    public override string ToString()=>DepotPath;
  }
  public sealed class AssetFolder {
    public readonly Dictionary<string,AssetFolder> Children=new Dictionary<string,AssetFolder>(StringComparer.OrdinalIgnoreCase);
    public readonly List<GameAsset> Direct=new List<GameAsset>();
    public string Name; public string FullPath;
    public AssetFolder(string name,string full){Name=name;FullPath=full;}
  }
  public sealed class BundleIndex {
    public readonly List<GameAsset> All=new List<GameAsset>();
    public AssetFolder Root=new AssetFolder("Game content","");
    BundleManager manager;
    public int BundleCount=>manager?.Bundles.Count??0;
    public async Task ScanAsync(string gameBin,Action<string> progress) {
      await Task.Run(()=>{
        var validBin=EnvironmentPaths.ValidateGameCandidate(gameBin);
        if(validBin==null)throw new DirectoryNotFoundException("The Witcher 3 Remastered installation was not found. Select the real game bin\\x64_dx12 folder.");
        var root=EnvironmentPaths.GameRootFromBin(validBin);
        if(root==null || !Directory.Exists(Path.Combine(root,"content")))
          throw new DirectoryNotFoundException("The game's content folder was not found. W3 Forge Studio needs the normal installed game files; do not point it to a copied bin folder only.");
        progress?.Invoke("Indexing game bundles...");
        var m=new BundleManager();m.LoadAll(validBin);
        var local=new List<GameAsset>(m.Items.Count);
        var folderRoot=new AssetFolder("Game content","");
        foreach(var pair in m.Items) {
          var path=pair.Key;
          if(String.IsNullOrWhiteSpace(path)||pair.Value==null||pair.Value.Count==0)continue;
          path=path.Replace('/','\\');
          if(path.Contains("..")||Path.IsPathRooted(path))continue;
          var item=new GameAsset{DepotPath=path,Raw=pair.Value[0]};
          local.Add(item);
          var split=path.Split(new[]{'\\'},StringSplitOptions.RemoveEmptyEntries);
          var cur=folderRoot;
          string aggregate="";
          for(int i=0;i<split.Length-1;i++){
            string seg=split[i];aggregate=aggregate.Length==0?seg:aggregate+"\\"+seg;
            if(!cur.Children.TryGetValue(seg,out var next)){
              next=new AssetFolder(seg,aggregate);cur.Children.Add(seg,next);
            }
            cur=next;
          }
          cur.Direct.Add(item);
        }
        local.Sort((a,b)=>StringComparer.OrdinalIgnoreCase.Compare(a.DepotPath,b.DepotPath));
        manager=m;All.AddRange(local);Root=folderRoot;
        progress?.Invoke($"Indexed {All.Count:N0} assets in {BundleCount} bundles");
      });
    }
    public List<GameAsset> Search(string query,string extension,AssetFolder folder,int limit,out int total){
      IEnumerable<GameAsset> source;
      if(!String.IsNullOrWhiteSpace(query)){
        source=All.Where(x=>x.DepotPath.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0);
      }else if(folder!=null){
        source=folder.Direct.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase);
      }else{
        source=All;
      }

      if(!String.IsNullOrEmpty(extension))
        source=source.Where(x=>x.Extension==extension);

      var result=new List<GameAsset>(Math.Min(limit,4096));
      total=0;
      foreach(var item in source){
        total++;
        if(result.Count<limit)result.Add(item);
      }
      return result;
    }
    public void Extract(GameAsset asset,string file){
      if(asset?.Raw==null)throw new ArgumentException("Select a game asset.");
      Directory.CreateDirectory(Path.GetDirectoryName(file));
      using(var stream=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.None))asset.Raw.Extract(stream);
    }
    public string AddToProject(GameAsset asset,Project project){
      if(project==null)throw new InvalidOperationException("Create or open a project first.");
      var relative=asset.DepotPath.TrimStart('\\').Replace('/', '\\');
      if(relative.Contains("..")||relative.Contains(":")||Path.IsPathRooted(relative))throw new InvalidDataException("Unsafe asset depot path.");
      var importRoot=project.AssetImportRoot;
      var target=Path.Combine(importRoot,relative);
      Directory.CreateDirectory(Path.GetDirectoryName(target));
      if(File.Exists(target))throw new IOException("The project already contains this file: "+relative);
      Extract(asset,target);

      // REDengine resources such as W2MESH can reference external buffers.
      // When the game index exposes them, bring every matching .N.buffer into
      // the project automatically so NativePacker can rebuild buffers0.bundle.
      if(!relative.EndsWith(".buffer",StringComparison.OrdinalIgnoreCase)){
        string prefix=relative+".";
        var related=All.Where(x=>
          x.DepotPath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase) &&
          x.DepotPath.EndsWith(".buffer",StringComparison.OrdinalIgnoreCase))
          .OrderBy(x=>x.DepotPath,StringComparer.OrdinalIgnoreCase)
          .ToArray();
        foreach(var buffer in related){
          var bufferRelative=buffer.DepotPath.TrimStart('\\').Replace('/', '\\');
          var bufferTarget=Path.Combine(importRoot,bufferRelative);
          Directory.CreateDirectory(Path.GetDirectoryName(bufferTarget));
          if(!File.Exists(bufferTarget))Extract(buffer,bufferTarget);
        }
      }
      return target;
    }
  }
}