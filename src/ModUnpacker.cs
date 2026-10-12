using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LZ4;
using Doboz;
using Ionic.Zlib;

namespace W3Forge {
  public sealed class ModImportResult {
    public Project Project { get; internal set; }
    public int BundleCount { get; internal set; }
    public int ResourceCount { get; internal set; }
    public int BufferCount { get; internal set; }
    public int LooseFileCount { get; internal set; }
    public int DuplicateCount { get; internal set; }
    public bool ImportedTextureCache { get; internal set; }
  }

  sealed class RawBundleEntry {
    public string BundlePath;
    public string Name;
    public uint Size;
    public uint ZSize;
    public uint PageOffset;
    public uint Compression;
  }

  public static class ModUnpacker {
    static readonly byte[] BundleMagic=Encoding.ASCII.GetBytes("POTATO70");

    public static ModImportResult Import(string sourceFolder,string destinationParent,string projectName,string gameBin,Action<string> log) {
      if(String.IsNullOrWhiteSpace(sourceFolder)||!Directory.Exists(sourceFolder))
        throw new DirectoryNotFoundException("Select a valid Witcher 3 mod folder or its content folder.");
      if(String.IsNullOrWhiteSpace(destinationParent))
        throw new DirectoryNotFoundException("Select a destination for the unpacked project.");

      var contents=ResolveContentFolders(sourceFolder);
      var bundles=contents.SelectMany(content=>Directory.GetFiles(content,"*.bundle",SearchOption.AllDirectories))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x=>x.IndexOf("buffers",StringComparison.OrdinalIgnoreCase)>=0?1:0)
        .ThenBy(x=>x,StringComparer.OrdinalIgnoreCase)
        .ToArray();
      if(bundles.Length==0)
        throw new InvalidDataException("No REDengine .bundle files were found in the selected mod.");

      if(String.IsNullOrWhiteSpace(projectName)) {
        var sourceInfo=new DirectoryInfo(Path.GetFullPath(sourceFolder));
        projectName=sourceInfo.Name.Equals("content",StringComparison.OrdinalIgnoreCase)&&sourceInfo.Parent!=null
          ?sourceInfo.Parent.Name
          :sourceInfo.Name;
        if(String.IsNullOrWhiteSpace(projectName))projectName="ImportedMod";
      }
      projectName=SanitizeProjectName(projectName);

      var p=Project.Create(destinationParent,projectName,gameBin);
      int resourceCount=0,bufferCount=0,duplicates=0;
      var written=new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      try {
        log?.Invoke("Unpack Mod started: "+Path.GetFullPath(sourceFolder));
        log?.Invoke("Detected "+contents.Length+" content package"+(contents.Length==1?"":"s")+" and "+bundles.Length+" bundle"+(bundles.Length==1?"":"s")+".");
        foreach(var bundlePath in bundles) {
          var entries=ReadBundle(bundlePath);
          log?.Invoke("Reading "+Path.GetFileName(bundlePath)+" ("+entries.Count+" TOC entries)...");
          foreach(var entry in entries) {
            var relative=SafeDepotPath(entry.Name);
            if(!written.Add(relative)) {
              duplicates++;
              log?.Invoke("Duplicate depot path skipped (first definition kept): "+relative);
              continue;
            }
            var target=Path.Combine(p.Files,relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using(var fs=new FileStream(target,FileMode.Create,FileAccess.Write,FileShare.None))
              ExtractEntry(entry,fs);
            resourceCount++;
            if(relative.EndsWith(".buffer",StringComparison.OrdinalIgnoreCase))bufferCount++;
          }
        }

        bool rootCache=false;
        int loose=0;
        var sourceRoot=Path.GetFullPath(sourceFolder);
        if(new DirectoryInfo(sourceRoot).Name.Equals("content",StringComparison.OrdinalIgnoreCase))
          sourceRoot=new DirectoryInfo(sourceRoot).Parent?.FullName??sourceRoot;

        foreach(var content in contents) {
          var packageRel=RelativeDirectory(content,sourceRoot);
          if(String.IsNullOrWhiteSpace(packageRel))packageRel="content";

          var texture=Path.Combine(content,"texture.cache");
          if(File.Exists(texture)) {
            if(!rootCache) {
              Directory.CreateDirectory(Path.GetDirectoryName(p.TextureCache));
              File.Copy(texture,p.TextureCache,true);
              rootCache=true;
              log?.Invoke("Imported texture.cache from "+packageRel+".");
            }
            var rawCache=Path.Combine(p.RawFiles,"packages",packageRel,"texture.cache");
            Directory.CreateDirectory(Path.GetDirectoryName(rawCache));
            File.Copy(texture,rawCache,true);
          }

          foreach(var file in Directory.GetFiles(content,"*",SearchOption.AllDirectories)) {
            string ext=Path.GetExtension(file);
            if(ext.Equals(".bundle",StringComparison.OrdinalIgnoreCase))continue;
            var rel=Relative(file,content);
            if(rel.Equals("metadata.store",StringComparison.OrdinalIgnoreCase))continue;
            if(rel.Equals("texture.cache",StringComparison.OrdinalIgnoreCase))continue;

            var rawTarget=Path.Combine(p.RawFiles,"packages",packageRel,rel);
            Directory.CreateDirectory(Path.GetDirectoryName(rawTarget));
            File.Copy(file,rawTarget,true);
            loose++;
          }
        }

        p.Save();
        log?.Invoke("Unpack Mod complete: "+resourceCount+" resources"+
          (bufferCount>0?" ("+bufferCount+" external buffers)":"")+
          (duplicates>0?", "+duplicates+" duplicate definitions skipped":"")+
          (loose>0?", "+loose+" loose files preserved":"")+".");
        return new ModImportResult {
          Project=p,
          BundleCount=bundles.Length,
          ResourceCount=resourceCount,
          BufferCount=bufferCount,
          LooseFileCount=loose,
          DuplicateCount=duplicates,
          ImportedTextureCache=rootCache
        };
      } catch {
        try {
          if(File.Exists(p.FilePath))File.Delete(p.FilePath);
          if(Directory.Exists(p.Root))Directory.Delete(p.Root,true);
        } catch {}
        throw;
      }
    }

    static List<RawBundleEntry> ReadBundle(string path) {
      var result=new List<RawBundleEntry>();
      using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
      using(var br=new BinaryReader(fs)) {
        var magic=br.ReadBytes(8);
        if(!magic.SequenceEqual(BundleMagic))throw new InvalidDataException("Invalid POTATO70 bundle: "+path);
        br.ReadUInt32(); // virtual size
        br.ReadUInt32(); // dummy
        uint tocBytes=br.ReadUInt32();
        if(tocBytes%320!=0||32L+tocBytes>fs.Length)
          throw new InvalidDataException("Invalid bundle TOC: "+path);
        fs.Position=0x20;
        int count=checked((int)(tocBytes/320));
        for(int i=0;i<count;i++) {
          var nameBytes=br.ReadBytes(256);
          int zero=Array.IndexOf(nameBytes,(byte)0);
          if(zero<0)zero=nameBytes.Length;
          string name=Encoding.Default.GetString(nameBytes,0,zero);
          br.ReadBytes(16); // hash
          br.ReadUInt32(); // empty
          uint size=br.ReadUInt32();
          uint zsize=br.ReadUInt32();
          uint page=br.ReadUInt32();
          br.ReadUInt32(); // date
          br.ReadUInt32(); // time
          br.ReadBytes(16);
          br.ReadUInt32(); // crc
          uint compression=br.ReadUInt32();
          result.Add(new RawBundleEntry {
            BundlePath=path,Name=name,Size=size,ZSize=zsize,PageOffset=page,Compression=compression
          });
        }
      }
      return result;
    }

    static void ExtractEntry(RawBundleEntry e,Stream output) {
      using(var fs=new FileStream(e.BundlePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
        if((long)e.PageOffset+e.ZSize>fs.Length)
          throw new InvalidDataException("Bundle entry points outside the file: "+e.Name);
        fs.Position=e.PageOffset;
        switch(e.Compression) {
          case 0:
            CopyExact(fs,output,e.ZSize);
            break;
          case 1:
            using(var limited=new LimitedReadStream(fs,e.ZSize))
            using(var z=new ZlibStream(limited,CompressionMode.Decompress,true))z.CopyTo(output);
            break;
          case 3: {
            var packed=ReadExact(fs,e.ZSize);
            var raw=DobozCodec.Decode(packed,0,packed.Length);
            output.Write(raw,0,raw.Length);
            break;
          }
          case 4:
          case 5: {
            var packed=ReadExact(fs,e.ZSize);
            var raw=LZ4Codec.Decode(packed,0,packed.Length,checked((int)e.Size));
            if(raw==null)throw new InvalidDataException("LZ4 decompression failed: "+e.Name);
            output.Write(raw,0,raw.Length);
            break;
          }
          case 2:
            throw new NotSupportedException("This bundle uses Snappy compression (type 2), which is not supported by the current native unpacker: "+e.Name);
          default:
            throw new NotSupportedException("Unknown bundle compression type "+e.Compression+": "+e.Name);
        }
      }
      if(output.CanSeek && e.Size!=0 && output.Position!=e.Size)
        throw new InvalidDataException("Unpacked size mismatch for "+e.Name+": expected "+e.Size+", got "+output.Position+".");
    }

    static byte[] ReadExact(Stream s,uint count) {
      var b=new byte[count];
      int pos=0;
      while(pos<b.Length) {
        int n=s.Read(b,pos,b.Length-pos);
        if(n<=0)throw new EndOfStreamException();
        pos+=n;
      }
      return b;
    }

    static void CopyExact(Stream input,Stream output,uint count) {
      var buf=new byte[81920];
      long remaining=count;
      while(remaining>0) {
        int n=input.Read(buf,0,(int)Math.Min(buf.Length,remaining));
        if(n<=0)throw new EndOfStreamException();
        output.Write(buf,0,n);remaining-=n;
      }
    }

    static string[] ResolveContentFolders(string source) {
      source=Path.GetFullPath(source);
      if(Directory.GetFiles(source,"*.bundle",SearchOption.TopDirectoryOnly).Length>0)
        return new[]{source};

      var direct=Path.Combine(source,"content");
      if(Directory.Exists(direct) &&
         Directory.GetFiles(direct,"*.bundle",SearchOption.AllDirectories).Length>0)
        return new[]{direct};

      var candidates=Directory.GetDirectories(source,"content",SearchOption.AllDirectories)
        .Where(x=>Directory.GetFiles(x,"*.bundle",SearchOption.AllDirectories).Length>0)
        .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)
        .ToArray();
      if(candidates.Length>0)return candidates;
      throw new InvalidDataException("The selected folder does not contain a bundled Witcher 3 mod.");
    }

    static string RelativeDirectory(string folder,string root) {
      try {
        var r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        var f=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(f.StartsWith(r,StringComparison.OrdinalIgnoreCase))return f.Substring(r.Length);
      } catch {}
      return Path.GetFileName(folder);
    }

    static string SafeDepotPath(string value) {
      value=(value??"").Replace('/','\\').TrimStart('\\');
      if(String.IsNullOrWhiteSpace(value)||value.Contains("..")||value.Contains(":")||Path.IsPathRooted(value))
        throw new InvalidDataException("Unsafe or invalid depot path in bundle: "+value);
      return value;
    }

    static string Relative(string file,string root) {
      var r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
      var f=Path.GetFullPath(file);
      if(!f.StartsWith(r,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("File is outside content root.");
      return f.Substring(r.Length);
    }

    static string SanitizeProjectName(string value) {
      var invalid=Path.GetInvalidFileNameChars();
      var chars=value.Where(c=>!invalid.Contains(c)).ToArray();
      var s=new string(chars).Trim();
      if(String.IsNullOrWhiteSpace(s))s="ImportedMod";
      return s;
    }

    sealed class LimitedReadStream : Stream {
      readonly Stream inner; long remaining;
      public LimitedReadStream(Stream inner,long length){this.inner=inner;remaining=length;}
      public override bool CanRead=>true; public override bool CanSeek=>false; public override bool CanWrite=>false;
      public override long Length=>remaining; public override long Position{get=>0;set=>throw new NotSupportedException();}
      public override int Read(byte[] buffer,int offset,int count){
        if(remaining<=0)return 0;count=(int)Math.Min(count,remaining);
        int n=inner.Read(buffer,offset,count);remaining-=n;return n;
      }
      public override void Flush(){} public override long Seek(long o,SeekOrigin so)=>throw new NotSupportedException();
      public override void SetLength(long v)=>throw new NotSupportedException();
      public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
  }
}