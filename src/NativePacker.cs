using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LZ4;

namespace W3Forge {
  public sealed class NativePackResult {
    public string BlobBundle { get; internal set; }
    public string BuffersBundle { get; internal set; }
    public string MetadataStore { get; internal set; }
    public int MainFileCount { get; internal set; }
    public int BufferFileCount { get; internal set; }
  }

  public static class NativePacker {
    const int TocEntrySize = 320;
    const int BundleHeaderSize = 32;
    const int Alignment = 4096;
    const uint CompressionLz4 = 5;
    static readonly byte[] BundleMagic = Encoding.ASCII.GetBytes("POTATO70");
    static readonly byte[] BundleExtra = new byte[]{0x03,0x00,0x01,0x00,0x00,0x13,0x13,0x13,0x13,0x13,0x13,0x13};
    static readonly Regex BufferSuffix = new Regex(@"\.(\d+)\.buffer$",RegexOptions.IgnoreCase|RegexOptions.Compiled);

    sealed class PackedFile {
      public string DepotPath;
      public string SourcePath;
      public string TempCompressed;
      public uint RawSize;
      public uint CompressedSize;
      public uint PageOffset;
      public uint Crc32;
      public uint Compression;
      public int FileId;
      public bool IsBuffer;
      public int BufferOrdinal;
      public int BufferListIndex = -1;
      public uint StringOffset;
    }

    sealed class BundleLayout {
      public string Name;
      public uint NameOffset;
      public List<PackedFile> Files;
      public uint DataOffset;
      public uint FirstPageOffset;
      public uint VirtualBundleSize;
      public string OutputPath;
    }

    sealed class DirNode {
      public int Id;
      public int Parent;
      public uint NameOffset;
      public readonly Dictionary<string,DirNode> Children=new Dictionary<string,DirNode>(StringComparer.OrdinalIgnoreCase);
    }

    sealed class FileInit {
      public int FileId;
      public int DirId;
      public uint NameOffset;
    }

    sealed class StringTable {
      readonly MemoryStream ms=new MemoryStream();
      public uint Position { get { return checked((uint)ms.Position); } }
      public uint Add(string value) {
        if(value==null)value="";
        if(value.Any(c=>c>127))throw new InvalidDataException("REDengine depot paths must use ASCII characters: "+value);
        uint off=Position;
        var bytes=Encoding.ASCII.GetBytes(value);
        ms.Write(bytes,0,bytes.Length);
        ms.WriteByte(0);
        return off;
      }
      public byte[] ToArray(){return ms.ToArray();}
    }

    public static NativePackResult Pack(string inputRoot,string outputContent,Action<string> log) {
      if(String.IsNullOrWhiteSpace(inputRoot)||!Directory.Exists(inputRoot))
        throw new DirectoryNotFoundException("Project Bundle input folder was not found.");
      var sources=Directory.GetFiles(inputRoot,"*",SearchOption.AllDirectories)
        .Where(x=>!Project.IsIgnoredBuildFile(x)).ToArray();
      if(sources.Length==0)throw new InvalidOperationException("Project is empty. Add files from Game Browser first.");

      Directory.CreateDirectory(outputContent);
      string temp=Path.Combine(Path.GetDirectoryName(outputContent)??outputContent,".w3forge_native_pack");
      if(Directory.Exists(temp))Directory.Delete(temp,true);
      Directory.CreateDirectory(temp);

      try {
        var byDepot=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in sources) {
          var rel=RelativeDepot(file,inputRoot);
          if(byDepot.ContainsKey(rel))throw new InvalidDataException("Duplicate depot path: "+rel);
          byDepot.Add(rel,file);
        }

        var mainPaths=byDepot.Keys.Where(x=>!BufferSuffix.IsMatch(x))
          .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
        var bufferPaths=byDepot.Keys.Where(x=>BufferSuffix.IsMatch(x)).ToList();

        if(mainPaths.Count==0)throw new InvalidOperationException("Project contains only .buffer files and no main resources.");

        var main=new List<PackedFile>();
        var buffers=new List<PackedFile>();
        var bufferOwner=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

        foreach(var b in bufferPaths) {
          var m=BufferSuffix.Match(b);
          var owner=b.Substring(0,m.Index);
          if(!byDepot.ContainsKey(owner)||BufferSuffix.IsMatch(owner))
            throw new InvalidDataException("Orphan external buffer without its main resource: "+b);
          bufferOwner[b]=owner;
        }

        int fileId=1;
        int bufferListIndex=0;
        foreach(var path in mainPaths) {
          var pf=Prepare(byDepot[path],path,fileId++,false,temp,log);
          var owned=bufferPaths.Where(x=>String.Equals(bufferOwner[x],path,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>BufferNumber(x)).ThenBy(x=>x,StringComparer.OrdinalIgnoreCase).ToList();
          if(owned.Count>0) {
            pf.BufferListIndex=bufferListIndex;
            bufferListIndex += owned.Count;
          }
          main.Add(pf);
        }

        foreach(var parent in main) {
          var owned=bufferPaths.Where(x=>String.Equals(bufferOwner[x],parent.DepotPath,StringComparison.OrdinalIgnoreCase))
            .OrderBy(x=>BufferNumber(x)).ThenBy(x=>x,StringComparer.OrdinalIgnoreCase);
          foreach(var path in owned) {
            var pf=Prepare(byDepot[path],path,fileId++,true,temp,log);
            pf.BufferOrdinal=BufferNumber(path);
            buffers.Add(pf);
          }
        }

        var table=new StringTable();
        table.Add(""); // reserved string 0

        var blob=new BundleLayout{Name="blob0.bundle",Files=main,OutputPath=Path.Combine(outputContent,"blob0.bundle")};
        blob.NameOffset=table.Add(blob.Name);
        foreach(var f in main)f.StringOffset=table.Add(f.DepotPath);

        BundleLayout bufferBundle=null;
        if(buffers.Count>0) {
          bufferBundle=new BundleLayout{Name="buffers0.bundle",Files=buffers,OutputPath=Path.Combine(outputContent,"buffers0.bundle")};
          bufferBundle.NameOffset=table.Add(bufferBundle.Name);
          foreach(var f in buffers)f.StringOffset=table.Add(f.DepotPath);
        }

        uint rootNameOffset=table.Add("");
        var dirs=new List<DirNode>();
        var rootNode=new DirNode{Id=0,Parent=0,NameOffset=rootNameOffset};
        dirs.Add(rootNode);
        var fileInits=new List<FileInit>();
        var pendingFileInits=new List<Tuple<PackedFile,int,string>>();

        foreach(var f in main) {
          var parts=f.DepotPath.Split(new[]{'\\'},StringSplitOptions.RemoveEmptyEntries);
          if(parts.Length==0)throw new InvalidDataException("Invalid depot path: "+f.DepotPath);
          var cur=rootNode;
          for(int i=0;i<parts.Length-1;i++) {
            DirNode child;
            if(!cur.Children.TryGetValue(parts[i],out child)) {
              child=new DirNode{Id=dirs.Count,Parent=cur.Id,NameOffset=table.Add(parts[i])};
              cur.Children.Add(parts[i],child);
              dirs.Add(child);
            }
            cur=child;
          }
          pendingFileInits.Add(Tuple.Create(f,cur.Id,parts[parts.Length-1]));
        }

        // wcc_lite writes all directory names first, then all leaf filenames.
        // Matching that order keeps metadata.store byte-for-byte compatible.
        foreach(var pending in pendingFileInits) {
          fileInits.Add(new FileInit{
            FileId=pending.Item1.FileId,
            DirId=pending.Item2,
            NameOffset=table.Add(pending.Item3)
          });
        }

        BuildBundle(blob,false,log);
        if(bufferBundle!=null)BuildBundle(bufferBundle,true,log);

        string metadata=Path.Combine(outputContent,"metadata.store");
        WriteMetadata(metadata,table.ToArray(),main,buffers,blob,bufferBundle,dirs,fileInits);

        log?.Invoke("Native packer: "+main.Count+" resources"+(buffers.Count>0?" + "+buffers.Count+" external buffers":"")+".");
        log?.Invoke("Native packer created blob0.bundle"+(buffers.Count>0?", buffers0.bundle":"")+" and metadata.store.");

        return new NativePackResult {
          BlobBundle=blob.OutputPath,
          BuffersBundle=bufferBundle?.OutputPath,
          MetadataStore=metadata,
          MainFileCount=main.Count,
          BufferFileCount=buffers.Count
        };
      } finally {
        try{if(Directory.Exists(temp))Directory.Delete(temp,true);}catch{}
      }
    }

    static PackedFile Prepare(string source,string depot,int fileId,bool isBuffer,string temp,Action<string> log) {
      var info=new FileInfo(source);
      if(info.Length>UInt32.MaxValue)throw new InvalidDataException("Resource is larger than 4 GiB and cannot be packed: "+depot);
      var raw=File.ReadAllBytes(source);
      uint compression=raw.Length==0?0u:CompressionLz4;
      byte[] compressed=raw.Length==0?new byte[0]:LZ4Codec.EncodeHC(raw,0,raw.Length);
      var tempFile=Path.Combine(temp,fileId.ToString("D8")+".lz4");
      File.WriteAllBytes(tempFile,compressed);
      var pf=new PackedFile{
        DepotPath=depot,
        SourcePath=source,
        TempCompressed=tempFile,
        RawSize=checked((uint)raw.Length),
        CompressedSize=checked((uint)compressed.Length),
        Crc32=Crc32(raw),
        Compression=compression,
        FileId=fileId,
        IsBuffer=isBuffer
      };
      log?.Invoke("Packed "+depot+"  "+FormatSize(raw.Length)+" -> "+FormatSize(compressed.Length));
      return pf;
    }

    static void BuildBundle(BundleLayout layout,bool isBufferBundle,Action<string> log) {
      if(layout.Files.Count==0)return;
      layout.DataOffset=checked((uint)(BundleHeaderSize+layout.Files.Count*TocEntrySize));
      uint cursor=Align(layout.DataOffset,Alignment);
      layout.FirstPageOffset=cursor;
      foreach(var f in layout.Files) {
        f.PageOffset=cursor;
        cursor=checked(cursor+f.CompressedSize);
        if(!Object.ReferenceEquals(f,layout.Files[layout.Files.Count-1]))cursor=Align(cursor,Alignment);
      }
      layout.VirtualBundleSize=Align(cursor,Alignment);
      uint tocByteCount=checked((uint)(layout.Files.Count*TocEntrySize));

      using(var fs=new FileStream(layout.OutputPath,FileMode.Create,FileAccess.Write,FileShare.None))
      using(var bw=new BinaryWriter(fs)) {
        bw.Write(BundleMagic);
        bw.Write(layout.VirtualBundleSize);
        bw.Write((uint)0);
        bw.Write(tocByteCount);
        bw.Write(BundleExtra);

        foreach(var f in layout.Files)WriteTocEntry(bw,f);

        foreach(var f in layout.Files) {
          if(fs.Position>f.PageOffset)throw new InvalidDataException("Bundle layout overlap while packing "+f.DepotPath);
          WriteZeros(bw,checked((int)(f.PageOffset-fs.Position)));
          using(var input=new FileStream(f.TempCompressed,FileMode.Open,FileAccess.Read,FileShare.Read))input.CopyTo(fs);
        }
      }
      log?.Invoke("Created "+Path.GetFileName(layout.OutputPath)+" ("+FormatSize(new FileInfo(layout.OutputPath).Length)+").");
    }

    static void WriteTocEntry(BinaryWriter bw,PackedFile f) {
      var name=Encoding.ASCII.GetBytes(f.DepotPath);
      if(name.Length>255)throw new InvalidDataException("Depot path is too long for a bundle entry: "+f.DepotPath);
      bw.Write(name);
      WriteZeros(bw,256-name.Length);
      WriteZeros(bw,16); // bundle hash field
      bw.Write((uint)0);
      bw.Write(f.RawSize);
      bw.Write(f.CompressedSize);
      bw.Write(f.PageOffset);
      bw.Write((uint)0); // date
      bw.Write((uint)0); // time
      WriteZeros(bw,16);
      bw.Write(f.Crc32);
      bw.Write(f.Compression);
    }

    static void WriteMetadata(string path,byte[] stringTable,List<PackedFile> main,List<PackedFile> buffers,
      BundleLayout blob,BundleLayout bufferBundle,List<DirNode> dirs,List<FileInit> fileInits) {

      uint maxBundle=0,maxMemory=0;
      foreach(var f in main.Concat(buffers)) {
        if(f.CompressedSize>maxBundle)maxBundle=f.CompressedSize;
        if(f.RawSize>maxMemory)maxMemory=f.RawSize;
      }

      using(var fs=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
      using(var bw=new BinaryWriter(fs)) {
        bw.Write(new byte[]{0x03,0x56,0x54,0x4D});
        bw.Write((int)6);
        bw.Write(maxBundle);
        bw.Write(maxMemory);
        WriteVlq(bw,stringTable.Length);
        bw.Write(stringTable);

        WriteVlq(bw,1+main.Count+buffers.Count);
        for(int i=0;i<8;i++)bw.Write((uint)0);
        foreach(var f in main) {
          bw.Write(f.StringOffset); bw.Write((uint)0); bw.Write(f.CompressedSize); bw.Write(f.RawSize);
          bw.Write((uint)f.FileId); bw.Write(f.Compression);
          bw.Write(f.BufferListIndex>=0?(uint)f.BufferListIndex:0u);
          bw.Write(f.BufferListIndex>=0?1u:0u);
        }
        foreach(var f in buffers) {
          bw.Write(f.StringOffset); bw.Write((uint)0); bw.Write(f.CompressedSize); bw.Write(f.RawSize);
          bw.Write((uint)f.FileId); bw.Write(f.Compression); bw.Write((uint)0); bw.Write((uint)0);
        }

        WriteVlq(bw,1+main.Count+buffers.Count);
        for(int i=0;i<5;i++)bw.Write((uint)0);
        foreach(var f in main)WriteFileEntry(bw,f,1);
        foreach(var f in buffers)WriteFileEntry(bw,f,2);

        WriteVlq(bw,bufferBundle==null?2:3);
        for(int i=0;i<6;i++)bw.Write((uint)0);
        bw.Write(blob.NameOffset);
        bw.Write((uint)1);
        bw.Write((uint)main.Count);
        bw.Write(blob.VirtualBundleSize-blob.DataOffset);
        bw.Write(blob.DataOffset);
        bw.Write((uint)0);
        if(bufferBundle!=null) {
          bw.Write(bufferBundle.NameOffset);
          bw.Write((uint)(main.Count+1));
          bw.Write((uint)buffers.Count);
          bw.Write(bufferBundle.FirstPageOffset-bufferBundle.DataOffset);
          bw.Write(bufferBundle.DataOffset);
          bw.Write((uint)0);
        }

        WriteVlq(bw,buffers.Count);
        foreach(var f in buffers)bw.Write((uint)f.FileId);

        WriteVlq(bw,dirs.Count);
        foreach(var d in dirs) { bw.Write((int)d.NameOffset); bw.Write(d.Parent); }

        WriteVlq(bw,fileInits.Count);
        foreach(var fi in fileInits) { bw.Write(fi.FileId); bw.Write(fi.DirId); bw.Write((int)fi.NameOffset); }

        var hashes=main.Select(f=>new KeyValuePair<ulong,int>(Fnv1A64(f.DepotPath),f.FileId))
          .OrderBy(x=>x.Key).ToList();
        WriteVlq(bw,hashes.Count);
        foreach(var h in hashes) {
          bw.Write(unchecked((long)h.Key));
          bw.Write((long)h.Value);
        }
      }
    }

    static void WriteFileEntry(BinaryWriter bw,PackedFile f,uint bundleId) {
      bw.Write((uint)f.FileId); bw.Write(bundleId); bw.Write(f.PageOffset); bw.Write(f.CompressedSize); bw.Write((uint)0);
    }

    static string RelativeDepot(string file,string root) {
      string full=Path.GetFullPath(file);
      string basePath=Path.GetFullPath(root);
      if(!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))basePath+=Path.DirectorySeparatorChar;
      var rel=new Uri(basePath).MakeRelativeUri(new Uri(full)).ToString();
      rel=Uri.UnescapeDataString(rel).Replace('/','\\').TrimStart('\\').ToLowerInvariant();
      if(rel.Contains("..")||rel.Contains(":")||Path.IsPathRooted(rel))throw new InvalidDataException("Unsafe depot path: "+rel);
      return rel;
    }

    static int BufferNumber(string path) {
      var m=BufferSuffix.Match(path);
      if(!m.Success)return Int32.MaxValue;
      int n;
      return Int32.TryParse(m.Groups[1].Value,out n)?n:Int32.MaxValue;
    }

    static uint Align(uint value,int boundary) {
      uint b=(uint)boundary;
      if(value==0)return 0;
      uint r=value%b;
      return r==0?value:checked(value+(b-r));
    }

    static void WriteZeros(BinaryWriter bw,int count) {
      if(count<=0)return;
      byte[] zeros=new byte[Math.Min(count,8192)];
      while(count>0) {
        int n=Math.Min(count,zeros.Length);
        bw.Write(zeros,0,n);
        count-=n;
      }
    }

    static void WriteVlq(BinaryWriter bw,int value) {
      if(value<0)throw new ArgumentOutOfRangeException("value");
      if(value==0){bw.Write((byte)0x80);return;}
      byte b=(byte)(value&0x3F);
      value>>=6;
      bool cont=value!=0;
      if(cont)b|=0x40;
      bw.Write(b);
      while(cont) {
        b=(byte)(value&0x7F);
        value>>=7;
        cont=value!=0;
        if(cont)b|=0x80;
        bw.Write(b);
      }
    }

    static ulong Fnv1A64(string value) {
      ulong hash=0xCBF29CE484222325UL;
      unchecked {
        foreach(byte b in Encoding.ASCII.GetBytes(value)) {
          hash^=b;
          hash*=0x100000001B3UL;
        }
      }
      return hash;
    }

    static uint Crc32(byte[] data) {
      uint crc=0xFFFFFFFFu;
      foreach(byte b in data) {
        crc^=b;
        for(int k=0;k<8;k++)crc=(crc&1)!=0?(crc>>1)^0xEDB88320u:crc>>1;
      }
      return ~crc;
    }

    public static void VerifyXmlRoundTrip(string inputRoot,string blobBundle,Action<string> log) {
      if(String.IsNullOrWhiteSpace(inputRoot)||!Directory.Exists(inputRoot))
        throw new DirectoryNotFoundException("XML verification input root was not found.");
      if(String.IsNullOrWhiteSpace(blobBundle)||!File.Exists(blobBundle))
        throw new FileNotFoundException("blob0.bundle was not found for XML verification.",blobBundle);

      var expected=Directory.GetFiles(inputRoot,"*.xml",SearchOption.AllDirectories)
        .Where(x=>!Project.IsIgnoredBuildFile(x))
        .ToDictionary(x=>RelativeDepot(x,inputRoot),x=>x,StringComparer.OrdinalIgnoreCase);
      if(expected.Count==0)return;

      var verified=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      using(var fs=new FileStream(blobBundle,FileMode.Open,FileAccess.Read,FileShare.Read))
      using(var br=new BinaryReader(fs)) {
        var magic=br.ReadBytes(8);
        if(!magic.SequenceEqual(BundleMagic))throw new InvalidDataException("Invalid generated blob0.bundle magic.");
        br.ReadUInt32();br.ReadUInt32();
        uint tocBytes=br.ReadUInt32();
        if(tocBytes%TocEntrySize!=0||32L+tocBytes>fs.Length)
          throw new InvalidDataException("Invalid generated blob0.bundle TOC.");
        fs.Position=BundleHeaderSize;
        int count=checked((int)(tocBytes/TocEntrySize));

        for(int i=0;i<count;i++) {
          var nameBytes=br.ReadBytes(256);
          int zero=Array.IndexOf(nameBytes,(byte)0);
          if(zero<0)zero=nameBytes.Length;
          string depot=Encoding.ASCII.GetString(nameBytes,0,zero);
          br.ReadBytes(16);
          br.ReadUInt32();
          uint rawSize=br.ReadUInt32();
          uint packedSize=br.ReadUInt32();
          uint pageOffset=br.ReadUInt32();
          br.ReadUInt32();br.ReadUInt32();
          br.ReadBytes(16);
          br.ReadUInt32();
          uint compression=br.ReadUInt32();

          string source;
          if(!expected.TryGetValue(depot,out source))continue;

          long tocPos=fs.Position;
          if((long)pageOffset+packedSize>fs.Length)
            throw new InvalidDataException("Generated XML bundle entry points outside blob0.bundle: "+depot);
          fs.Position=pageOffset;
          byte[] packed=br.ReadBytes(checked((int)packedSize));
          byte[] raw;
          if(compression==0)raw=packed;
          else if(compression==CompressionLz4) {
            raw=LZ4Codec.Decode(packed,0,packed.Length,checked((int)rawSize));
            if(raw==null)throw new InvalidDataException("Generated XML LZ4 decode failed: "+depot);
          } else {
            throw new InvalidDataException("Generated XML uses unexpected compression "+compression+": "+depot);
          }
          var original=File.ReadAllBytes(source);
          if(raw.Length!=original.Length||!raw.SequenceEqual(original))
            throw new InvalidDataException("XML bundle round-trip mismatch: "+depot);
          verified.Add(depot);
          fs.Position=tocPos;
        }
      }

      var missing=expected.Keys.Where(x=>!verified.Contains(x)).ToArray();
      if(missing.Length>0)
        throw new InvalidDataException("Generated bundle is missing XML resource(s): "+String.Join(", ",missing));
      log?.Invoke("XML bundle verification PASS: "+verified.Count+" XML resource"+(verified.Count==1?"":"s")+" preserved byte-for-byte.");
    }

    static string FormatSize(long bytes) {
      if(bytes>=1024*1024)return (bytes/1048576.0).ToString("N2")+" MB";
      if(bytes>=1024)return (bytes/1024.0).ToString("N1")+" KB";
      return bytes+" B";
    }
  }
}