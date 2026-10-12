using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LZ4;
using Doboz;
using Ionic.Zlib;

namespace W3Forge {
  public sealed class CompiledModAsset {
    internal string BundlePath;
    internal uint ZSize,PageOffset,CompressionId;
    public string DepotPath { get; internal set; }
    public string Extension { get { return Path.GetExtension(DepotPath ?? "").ToLowerInvariant(); } }
    public string BundleName { get; internal set; }
    public long Size { get; internal set; }
    public string Compression { get; internal set; }
    public string SizeText {
      get {
        if(Size < 1024) return Size+" B";
        if(Size < 1024L*1024L) return (Size/1024.0).ToString("N1")+" KB";
        return (Size/1024.0/1024.0).ToString("N1")+" MB";
      }
    }
  }

  public sealed class ModPackageImportResult {
    public int CookedImported;
    public int LooseImported;
    public int Skipped;
    public bool TextureCacheCopied;
  }

  sealed class ModLooseFile {
    public string Source,RelativePackage,RelativeContent;
    public bool PrimaryContent;
  }

  public sealed class CompiledModPackage {
    static readonly byte[] BundleMagic=Encoding.ASCII.GetBytes("POTATO70");
    public string RootPath { get; private set; }
    public string DisplayName { get; private set; }
    public List<CompiledModAsset> Assets { get; private set; }=new List<CompiledModAsset>();
    readonly List<ModLooseFile> loose=new List<ModLooseFile>();

    public static CompiledModPackage Open(string input,Action<string> log) {
      if(String.IsNullOrWhiteSpace(input))throw new ArgumentException("Select a Witcher 3 mod folder.");
      input=Path.GetFullPath(input.Trim().Trim('"'));
      if(File.Exists(input))input=Path.GetDirectoryName(input);
      if(!Directory.Exists(input))throw new DirectoryNotFoundException("Mod folder was not found: "+input);
      var di=new DirectoryInfo(input);
      if(di.Name.Equals("content",StringComparison.OrdinalIgnoreCase)&&di.Parent!=null)input=di.Parent.FullName;

      var p=new CompiledModPackage {
        RootPath=input,
        DisplayName=Path.GetFileName(input.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar))
      };
      var map=new Dictionary<string,CompiledModAsset>(StringComparer.OrdinalIgnoreCase);
      foreach(var bundle in Directory.EnumerateFiles(input,"*.bundle",SearchOption.AllDirectories)
        .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)) {
        foreach(var a in ReadBundle(bundle))map[a.DepotPath]=a;
      }
      p.Assets=map.Values.OrderBy(x=>x.DepotPath,StringComparer.OrdinalIgnoreCase).ToList();
      p.ScanLoose();
      if(p.Assets.Count==0&&p.loose.Count==0)throw new InvalidDataException("No REDengine resources or loose mod files were found.");
      log?.Invoke("Compiled mod indexed: "+p.Assets.Count.ToString("N0")+" bundled resources, "+p.loose.Count.ToString("N0")+" loose files.");
      return p;
    }

    static List<CompiledModAsset> ReadBundle(string path) {
      var list=new List<CompiledModAsset>();
      using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
      using(var br=new BinaryReader(fs)) {
        if(fs.Length<32||!br.ReadBytes(8).SequenceEqual(BundleMagic))
          throw new InvalidDataException("Invalid POTATO70 bundle: "+path);
        br.ReadUInt32();br.ReadUInt32();
        uint tocBytes=br.ReadUInt32();
        if(tocBytes%320!=0||32L+tocBytes>fs.Length)throw new InvalidDataException("Invalid bundle TOC: "+path);
        fs.Position=32;
        int count=checked((int)(tocBytes/320));
        for(int i=0;i<count;i++) {
          var nameBytes=br.ReadBytes(256);
          int zero=Array.IndexOf(nameBytes,(byte)0);if(zero<0)zero=nameBytes.Length;
          string depot=SafeDepot(Encoding.Default.GetString(nameBytes,0,zero));
          br.ReadBytes(16);br.ReadUInt32();
          uint size=br.ReadUInt32(),zsize=br.ReadUInt32(),page=br.ReadUInt32();
          br.ReadUInt32();br.ReadUInt32();br.ReadBytes(16);br.ReadUInt32();
          uint compression=br.ReadUInt32();
          if(String.IsNullOrWhiteSpace(depot))continue;
          list.Add(new CompiledModAsset {
            BundlePath=path,DepotPath=depot,BundleName=Path.GetFileName(path),
            Size=size,ZSize=zsize,PageOffset=page,CompressionId=compression,
            Compression=CompressionName(compression)
          });
        }
      }
      return list;
    }

    static string CompressionName(uint c) {
      switch(c){case 0:return "None";case 1:return "Zlib";case 2:return "Snappy";case 3:return "Doboz";case 4:case 5:return "LZ4";default:return "Unknown "+c;}
    }

    void ScanLoose() {
      loose.Clear();
      foreach(var file in Directory.EnumerateFiles(RootPath,"*",SearchOption.AllDirectories)) {
        string name=Path.GetFileName(file),ext=Path.GetExtension(file);
        if(ext.Equals(".bundle",StringComparison.OrdinalIgnoreCase)||name.Equals("metadata.store",StringComparison.OrdinalIgnoreCase))continue;
        var content=FindContentAncestor(file);if(content==null)continue;
        loose.Add(new ModLooseFile {
          Source=file,RelativePackage=Relative(file,RootPath),RelativeContent=Relative(file,content.FullName),
          PrimaryContent=content.Parent!=null&&PathsEqual(content.Parent.FullName,RootPath)
        });
      }
    }

    public void ExtractAssets(IEnumerable<CompiledModAsset> selected,string destination,Action<string> log) {
      Directory.CreateDirectory(destination);
      int count=0;
      foreach(var a in selected??Enumerable.Empty<CompiledModAsset>()) {
        if(a==null)continue;
        string target=Path.Combine(destination,SafeDepot(a.DepotPath));
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using(var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None))ExtractOne(a,output);
        count++;
      }
      log?.Invoke("Extracted "+count.ToString("N0")+" bundled resources.");
    }

    public void ExtractComplete(string destination,Action<string> log) {
      Directory.CreateDirectory(destination);
      ExtractAssets(Assets,Path.Combine(destination,"Cooked"),log);
      int copied=0;
      foreach(var f in loose) {
        string target=Path.Combine(destination,"Loose",f.RelativePackage);
        Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(f.Source,target,true);copied++;
      }
      log?.Invoke("Copied "+copied.ToString("N0")+" loose/runtime files.");
    }

    public ModPackageImportResult ImportToProject(Project project,bool overwrite,Action<string> log) {
      if(project==null)throw new InvalidOperationException("Open or create a project first.");
      var r=new ModPackageImportResult();
      foreach(var a in Assets) {
        string target=Path.Combine(project.AssetImportRoot,SafeDepot(a.DepotPath));
        if(File.Exists(target)&&!overwrite){r.Skipped++;continue;}
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        using(var output=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None))ExtractOne(a,output);
        r.CookedImported++;
      }
      foreach(var f in loose.Where(x=>x.PrimaryContent)) {
        string name=Path.GetFileName(f.Source);
        if(name.Equals("texture.cache",StringComparison.OrdinalIgnoreCase)) {
          if(File.Exists(project.TextureCache)&&!overwrite){r.Skipped++;continue;}
          Directory.CreateDirectory(Path.GetDirectoryName(project.TextureCache));File.Copy(f.Source,project.TextureCache,true);r.TextureCacheCopied=true;continue;
        }
        string ext=Path.GetExtension(f.Source).ToLowerInvariant();
        if(ext==".ws"||ext==".w3strings"||ext==".xml"||ext==".settings") {
          string target=Path.Combine(project.RawFiles,"content",f.RelativeContent);
          if(File.Exists(target)&&!overwrite){r.Skipped++;continue;}
          Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(f.Source,target,true);r.LooseImported++;
        }
      }
      log?.Invoke("Imported compiled mod: "+r.CookedImported+" cooked, "+r.LooseImported+" loose"+
        (r.TextureCacheCopied?", texture.cache":"")+", "+r.Skipped+" skipped.");
      return r;
    }

    static void ExtractOne(CompiledModAsset a,Stream output) {
      using(var fs=new FileStream(a.BundlePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
        if((long)a.PageOffset+a.ZSize>fs.Length)throw new InvalidDataException("Bundle entry points outside the file: "+a.DepotPath);
        fs.Position=a.PageOffset;
        switch(a.CompressionId) {
          case 0:CopyExact(fs,output,a.ZSize);break;
          case 1:using(var lim=new LimitedReadStream(fs,a.ZSize))using(var z=new ZlibStream(lim,CompressionMode.Decompress,true))z.CopyTo(output);break;
          case 3:{var p=ReadExact(fs,a.ZSize);var raw=DobozCodec.Decode(p,0,p.Length);output.Write(raw,0,raw.Length);break;}
          case 4:
          case 5:{var p=ReadExact(fs,a.ZSize);var raw=LZ4Codec.Decode(p,0,p.Length,checked((int)a.Size));if(raw==null)throw new InvalidDataException("LZ4 decompression failed: "+a.DepotPath);output.Write(raw,0,raw.Length);break;}
          case 2:throw new NotSupportedException("Snappy-compressed bundle entries are not yet supported: "+a.DepotPath);
          default:throw new NotSupportedException("Unknown bundle compression type "+a.CompressionId+": "+a.DepotPath);
        }
      }
      if(output.CanSeek&&a.Size!=0&&output.Position!=a.Size)throw new InvalidDataException("Unpacked size mismatch for "+a.DepotPath+".");
    }

    static byte[] ReadExact(Stream s,uint n){var b=new byte[n];int p=0;while(p<b.Length){int r=s.Read(b,p,b.Length-p);if(r<=0)throw new EndOfStreamException();p+=r;}return b;}
    static void CopyExact(Stream i,Stream o,uint n){var b=new byte[81920];long left=n;while(left>0){int r=i.Read(b,0,(int)Math.Min(b.Length,left));if(r<=0)throw new EndOfStreamException();o.Write(b,0,r);left-=r;}}

    static DirectoryInfo FindContentAncestor(string file){var d=new FileInfo(file).Directory;while(d!=null){if(d.Name.Equals("content",StringComparison.OrdinalIgnoreCase))return d;d=d.Parent;}return null;}
    static string SafeDepot(string v){v=(v??"").Replace('/','\\').TrimStart('\\');if(String.IsNullOrWhiteSpace(v))return "";if(v.Contains("..")||v.Contains(":")||Path.IsPathRooted(v))throw new InvalidDataException("Unsafe depot path: "+v);return v;}
    static string Relative(string f,string r){r=Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;f=Path.GetFullPath(f);if(!f.StartsWith(r,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("File outside mod root.");return f.Substring(r.Length);}
    static bool PathsEqual(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);}

    sealed class LimitedReadStream:Stream {
      readonly Stream inner;long left;
      public LimitedReadStream(Stream s,long n){inner=s;left=n;}
      public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>left;public override long Position{get=>0;set=>throw new NotSupportedException();}
      public override int Read(byte[] b,int o,int c){if(left<=0)return 0;c=(int)Math.Min(c,left);int n=inner.Read(b,o,c);left-=n;return n;}
      public override void Flush(){}public override long Seek(long o,SeekOrigin so)=>throw new NotSupportedException();public override void SetLength(long v)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
  }
}