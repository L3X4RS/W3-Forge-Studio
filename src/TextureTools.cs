using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ionic.Zlib;
using Pfim;
using W3Edit.CR2W;
using W3Edit.CR2W.Types;

namespace W3Forge {
  public sealed class TextureInfo {
    public string DepotPath {get;internal set;}
    public int Width {get;internal set;}
    public int Height {get;internal set;}
    public int MipCount {get;internal set;}
    public int ResidentMipIndex {get;internal set;}
    public uint TextureCacheKey {get;internal set;}
    public string Compression {get;internal set;}
    public string TextureGroup {get;internal set;}
    public string DdsFormat {get;internal set;}
    public bool FullResolution {get;internal set;}
    public string CacheFile {get;internal set;}
    public string SourceLabel {get{return FullResolution?"Full resolution / texture.cache":"Resident mip preview";}}
  }

  public sealed class TexturePreviewData {
    public TextureInfo Info {get;internal set;}
    public BitmapSource Image {get;internal set;}
    public byte[] DdsBytes {get;internal set;}
  }

  enum RedTextureFormat { Rgba8,BC1,BC2,BC3,BC4,BC5,BC7 }

  sealed class XbmDescriptor {
    public string DepotPath;
    public int Width,Height,MipCount,ResidentMipIndex;
    public uint TextureCacheKey;
    public string Compression,TextureGroup;
    public RedTextureFormat Format;
    public readonly List<Tuple<uint,uint,uint>> Mips=new List<Tuple<uint,uint,uint>>();
    public byte[] ResidentBytes;
  }

  sealed class TextureCacheEntry {
    public string Name,CacheFile;
    public uint Hash,CompressedSize,UncompressedSize,BaseAlignment;
    public long PageOffset;
    public ushort Width,Height,MipCount,SliceCount;
    public int MipOffsetIndex,NumMipOffsets;
    public ushort Version7Flags;
    public byte Type1,Type2,IsCube,Unknown;
  }

  sealed class TextureCacheIndex {
    public string FilePath;
    public uint Version;
    public uint[] ChunkOffsets=new uint[0];
    public readonly Dictionary<uint,List<TextureCacheEntry>> ByHash=new Dictionary<uint,List<TextureCacheEntry>>();
    public readonly Dictionary<string,TextureCacheEntry> ByName=new Dictionary<string,TextureCacheEntry>(StringComparer.OrdinalIgnoreCase);
  }

  public static class TextureTools {
    const uint TextureCacheMagic=1415070536; // "HCXT" little-endian
    static readonly object CacheLock=new object();
    static readonly Dictionary<string,TextureCacheIndex> CacheIndexes=new Dictionary<string,TextureCacheIndex>(StringComparer.OrdinalIgnoreCase);

    public static TexturePreviewData Load(string xbmPath,string depotPath,string gameBin,string projectTextureCache) {
      var xbm=ReadXbm(xbmPath,depotPath);
      byte[] dds=null;
      TextureCacheEntry entry=null;

      try{entry=FindCacheEntry(xbm,gameBin,projectTextureCache);}catch{entry=null;}
      if(entry!=null&&entry.IsCube==0) {
        try{dds=ExtractFullDds(entry);}catch{dds=null;}
      }

      bool full=dds!=null;
      if(dds==null)dds=BuildResidentDds(xbm);
      if(dds==null||dds.Length<128)
        throw new InvalidDataException("The XBM has no decodable resident mip and no matching full-resolution texture cache entry.");

      var image=DecodeDds(dds);
      int width=xbm.Width,height=xbm.Height,mips=Math.Max(1,xbm.MipCount);
      RedTextureFormat fmt=xbm.Format;
      if(full) {
        width=entry.Width;height=entry.Height;mips=Math.Max(1,(int)entry.MipCount);fmt=FormatFromCacheType(entry.Type1);
      } else if(xbm.Mips.Count>0) {
        int i=Math.Min(Math.Max(0,xbm.ResidentMipIndex),xbm.Mips.Count-1);
        width=(int)xbm.Mips[i].Item1;height=(int)xbm.Mips[i].Item2;mips=Math.Max(1,xbm.MipCount-i);
      }

      return new TexturePreviewData {
        Info=new TextureInfo {
          DepotPath=depotPath,
          Width=width,Height=height,MipCount=mips,
          ResidentMipIndex=xbm.ResidentMipIndex,
          TextureCacheKey=xbm.TextureCacheKey,
          Compression=xbm.Compression,
          TextureGroup=xbm.TextureGroup,
          DdsFormat=FormatLabel(fmt),
          FullResolution=full,
          CacheFile=full?entry.CacheFile:null
        },
        Image=image,
        DdsBytes=dds
      };
    }

    public static void Export(TexturePreviewData data,string outputPath,string extension) {
      if(data==null||data.Image==null)throw new InvalidOperationException("No texture preview is loaded.");
      extension=(extension??Path.GetExtension(outputPath)??"").Trim().ToLowerInvariant();
      if(!extension.StartsWith("."))extension="."+extension;
      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))??".");

      switch(extension) {
        case ".dds":
          File.WriteAllBytes(outputPath,data.DdsBytes);
          return;
        case ".png":
          using(var fs=new FileStream(outputPath,FileMode.Create,FileAccess.Write,FileShare.None)) {
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(data.Image));
            encoder.Save(fs);
          }
          return;
        case ".tga":
          WriteTga(data.Image,outputPath);
          return;
        default:
          throw new NotSupportedException("Supported texture exports are PNG, TGA and DDS.");
      }
    }

    static XbmDescriptor ReadXbm(string path,string depotPath) {
      if(!File.Exists(path))throw new FileNotFoundException("XBM file was not found.",path);
      CR2WFile file;
      using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
      using(var br=new BinaryReader(fs))file=new CR2WFile(br);

      var chunk=file.chunks.FirstOrDefault(x=>String.Equals(x.Type,"CBitmapTexture",StringComparison.Ordinal));
      if(chunk==null)throw new InvalidDataException("This resource does not contain a CBitmapTexture chunk.");
      var vector=chunk.data as CVector;
      if(vector==null)throw new InvalidDataException("The XBM texture properties could not be read.");

      var result=new XbmDescriptor {
        DepotPath=depotPath??Path.GetFileName(path),
        Width=(int)GetUInt(vector,"width"),
        Height=(int)GetUInt(vector,"height"),
        ResidentMipIndex=(int)GetUInt(vector,"residentMipIndex"),
        TextureCacheKey=GetUInt(vector,"textureCacheKey"),
        Compression=GetName(vector,"compression"),
        TextureGroup=GetName(vector,"textureGroup")
      };
      result.Format=FormatFromCompression(result.Compression);

      var raw=chunk.unknownBytes==null?null:chunk.unknownBytes.Bytes;
      if(raw==null||raw.Length<16)throw new InvalidDataException("The XBM does not contain buffered mip data.");

      int pos=0;
      ReadU32(raw,ref pos);
      uint count=ReadU32(raw,ref pos);
      if(count==0||count>64)throw new InvalidDataException("The XBM mip table is invalid.");
      result.MipCount=(int)count;

      for(int i=0;i<count;i++) {
        if(pos+12>raw.Length)throw new InvalidDataException("The XBM mip table is truncated.");
        result.Mips.Add(Tuple.Create(ReadU32(raw,ref pos),ReadU32(raw,ref pos),ReadU32(raw,ref pos)));
      }
      if(pos+8>raw.Length)throw new InvalidDataException("The XBM resident mip header is truncated.");
      uint residentSize=ReadU32(raw,ref pos);
      ReadU32(raw,ref pos);
      if(residentSize>raw.Length-pos)throw new InvalidDataException("The XBM resident mip data is truncated.");
      result.ResidentBytes=new byte[residentSize];
      Buffer.BlockCopy(raw,pos,result.ResidentBytes,0,(int)residentSize);
      if(result.ResidentMipIndex<0||result.ResidentMipIndex>=result.Mips.Count)
        result.ResidentMipIndex=Math.Max(0,result.Mips.Count-1);
      return result;
    }

    static uint GetUInt(CVector v,string name) {
      var x=v.variables.FirstOrDefault(y=>String.Equals(y.Name,name,StringComparison.OrdinalIgnoreCase));
      if(x is CUInt32 u32)return u32.val;
      if(x is CUInt8 u8)return u8.val;
      if(x==null)return 0;
      var prop=x.GetType().GetProperty("val");
      return prop==null?0:Convert.ToUInt32(prop.GetValue(x,null));
    }

    static string GetName(CVector v,string name) {
      var x=v.variables.FirstOrDefault(y=>String.Equals(y.Name,name,StringComparison.OrdinalIgnoreCase)) as CName;
      return x==null?"":x.Value;
    }

    static RedTextureFormat FormatFromCompression(string compression) {
      switch((compression??"").Trim()) {
        case "TCM_DXTNoAlpha":
        case "TCM_Normals":return RedTextureFormat.BC1;
        case "TCM_DXTAlpha":
        case "TCM_NormalsHigh":
        case "TCM_NormalsGloss":
        case "TCM_QualityColor":return RedTextureFormat.BC3;
        case "TCM_QualityR":return RedTextureFormat.BC4;
        case "TCM_QualityRG":return RedTextureFormat.BC5;
        case "TCM_None":
        case "TCM_DXTAlphaLinear":
        case "TCM_RGBE":return RedTextureFormat.Rgba8;
        default:return RedTextureFormat.Rgba8;
      }
    }

    static RedTextureFormat FormatFromCacheType(byte type1) {
      switch(type1) {
        case 0x00:
        case 0xFD:return RedTextureFormat.Rgba8;
        case 0x07:return RedTextureFormat.BC1;
        case 0x0D:return RedTextureFormat.BC2;
        case 0x08:return RedTextureFormat.BC3;
        case 0x0E:return RedTextureFormat.BC4;
        case 0x0F:return RedTextureFormat.BC5;
        case 0x0A:return RedTextureFormat.BC7;
        default:throw new NotSupportedException("Unsupported REDengine texture format 0x"+type1.ToString("X2")+".");
      }
    }

    static string FormatLabel(RedTextureFormat f) {
      switch(f) {
        case RedTextureFormat.Rgba8:return "RGBA8";
        case RedTextureFormat.BC1:return "BC1 / DXT1";
        case RedTextureFormat.BC2:return "BC2 / DXT3";
        case RedTextureFormat.BC3:return "BC3 / DXT5";
        case RedTextureFormat.BC4:return "BC4";
        case RedTextureFormat.BC5:return "BC5";
        case RedTextureFormat.BC7:return "BC7";
        default:return f.ToString();
      }
    }

    static TextureCacheEntry FindCacheEntry(XbmDescriptor xbm,string gameBin,string projectTextureCache) {
      var caches=new List<string>();
      if(!String.IsNullOrWhiteSpace(projectTextureCache)&&File.Exists(projectTextureCache))caches.Add(projectTextureCache);

      string root=EnvironmentPaths.GameRootFromBin(gameBin);
      if(root!=null) {
        string content=Path.Combine(root,"content");
        if(Directory.Exists(content)) {
          foreach(var d in Directory.GetDirectories(content).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)) {
            string n=Path.GetFileName(d);
            if(n.StartsWith("content",StringComparison.OrdinalIgnoreCase)||n.StartsWith("patch",StringComparison.OrdinalIgnoreCase))
              caches.AddRange(Directory.GetFiles(d,"texture.cache",SearchOption.AllDirectories));
          }
        }
        string dlc=Path.Combine(root,"DLC");
        if(!Directory.Exists(dlc))dlc=Path.Combine(root,"dlc");
        if(Directory.Exists(dlc))caches.AddRange(Directory.GetFiles(dlc,"texture.cache",SearchOption.AllDirectories));
      }

      string depot=(xbm.DepotPath??"").Replace('/','\\');
      TextureCacheEntry fallback=null;
      foreach(var cache in caches.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Reverse()) {
        TextureCacheIndex idx;
        try{idx=GetCacheIndex(cache);}catch{continue;}
        TextureCacheEntry exact;
        if(idx.ByName.TryGetValue(depot,out exact))return exact;
        List<TextureCacheEntry> matches;
        if(xbm.TextureCacheKey!=0&&idx.ByHash.TryGetValue(xbm.TextureCacheKey,out matches)) {
          exact=matches.FirstOrDefault(x=>String.Equals(x.Name,depot,StringComparison.OrdinalIgnoreCase));
          if(exact!=null)return exact;
          if(fallback==null)fallback=matches.FirstOrDefault();
        }
      }
      return fallback;
    }

    static TextureCacheIndex GetCacheIndex(string path) {
      path=Path.GetFullPath(path);
      lock(CacheLock) {
        TextureCacheIndex existing;
        if(CacheIndexes.TryGetValue(path,out existing))return existing;
      }
      var read=ReadCacheIndex(path);
      lock(CacheLock)CacheIndexes[path]=read;
      return read;
    }

    static TextureCacheIndex ReadCacheIndex(string path) {
      var idx=new TextureCacheIndex{FilePath=path};
      using(var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
      using(var br=new BinaryReader(fs)) {
        if(fs.Length<64)throw new InvalidDataException("Texture cache is too small.");
        fs.Seek(-32,SeekOrigin.End);
        br.ReadUInt64();
        br.ReadUInt32();
        uint count=br.ReadUInt32();
        uint stringSize=br.ReadUInt32();
        uint mipCount=br.ReadUInt32();
        uint magic=br.ReadUInt32();
        uint version=br.ReadUInt32();
        idx.Version=version;
        if(magic!=TextureCacheMagic)throw new InvalidDataException("Not a REDengine texture.cache.");
        if(count>2000000)throw new InvalidDataException("Texture cache entry count is unreasonable.");

        long tableStart=fs.Length-(32L+(long)count*52L+stringSize+(long)mipCount*4L);
        if(tableStart<0)throw new InvalidDataException("Texture cache tables are invalid.");

        fs.Position=tableStart;
        idx.ChunkOffsets=new uint[mipCount];
        for(int i=0;i<idx.ChunkOffsets.Length;i++)idx.ChunkOffsets[i]=br.ReadUInt32();

        long namesBase=fs.Position;
        var names=new Dictionary<uint,string>();
        for(int i=0;i<count;i++) {
          uint rel=checked((uint)(fs.Position-namesBase));
          names[rel]=ReadCString(br);
        }

        fs.Position=fs.Length-32L-(long)count*52L;
        for(int i=0;i<count;i++) {
          uint hash=br.ReadUInt32();
          uint nameOffset=br.ReadUInt32();
          string name;
          if(!names.TryGetValue(nameOffset,out name))name="";
          var e=new TextureCacheEntry {
            Name=name.Replace('/','\\'),CacheFile=path,Hash=hash
          };
          e.PageOffset=br.ReadUInt32();
          e.CompressedSize=br.ReadUInt32();
          e.UncompressedSize=br.ReadUInt32();
          e.BaseAlignment=br.ReadUInt32();
          e.Width=br.ReadUInt16();e.Height=br.ReadUInt16();
          e.MipCount=br.ReadUInt16();e.SliceCount=br.ReadUInt16();
          e.MipOffsetIndex=br.ReadInt32();
          if(version>=7) {
            e.NumMipOffsets=br.ReadUInt16();
            e.Version7Flags=br.ReadUInt16();
          } else e.NumMipOffsets=br.ReadInt32();
          br.ReadInt64();
          e.Type1=br.ReadByte();e.Type2=br.ReadByte();e.IsCube=br.ReadByte();e.Unknown=br.ReadByte();
          if(e.NumMipOffsets<0||e.NumMipOffsets>64)e.NumMipOffsets=Math.Max(0,Math.Min((int)e.MipCount-1,16));

          idx.ByName[e.Name]=e;
          List<TextureCacheEntry> list;
          if(!idx.ByHash.TryGetValue(e.Hash,out list)){list=new List<TextureCacheEntry>();idx.ByHash[e.Hash]=list;}
          list.Add(e);
        }
      }
      return idx;
    }

    static byte[] ExtractFullDds(TextureCacheEntry e) {
      var fmt=FormatFromCacheType(e.Type1);
      var idx=GetCacheIndex(e.CacheFile);
      using(var output=new MemoryStream()) {
        WriteDdsHeader(output,e.Width,e.Height,Math.Max(1,(int)e.MipCount),fmt,e.IsCube!=0,Math.Max(1,(int)e.SliceCount));
        using(var fs=new FileStream(e.CacheFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
        using(var br=new BinaryReader(fs)) {
          long start=e.PageOffset*4096L;
          ReadTextureChunk(br,start,output);
          for(int j=0;j<e.NumMipOffsets;j++) {
            long n=(long)e.MipOffsetIndex+j;
            if(n<0||n>=idx.ChunkOffsets.Length)
              throw new InvalidDataException("Texture cache mip offset index is outside the offset table.");
            ReadTextureChunk(br,start+idx.ChunkOffsets[n],output);
          }
        }
        return output.ToArray();
      }
    }

    static void ReadTextureChunk(BinaryReader br,long pos,Stream output) {
      var fs=br.BaseStream;
      if(pos<0||pos+9>fs.Length)throw new InvalidDataException("Texture cache chunk offset is invalid.");
      fs.Position=pos;
      uint zsize=br.ReadUInt32();
      uint size=br.ReadUInt32();
      br.ReadByte();
      if(zsize==0||zsize>Int32.MaxValue||pos+9L+zsize>fs.Length)
        throw new InvalidDataException("Texture cache mip block is invalid.");
      using(var limited=new LimitedReadStream(fs,zsize))
      using(var z=new ZlibStream(limited,CompressionMode.Decompress,true))
      using(var temp=new MemoryStream()) {
        z.CopyTo(temp);
        if(size!=0&&temp.Length!=size)
          throw new InvalidDataException("Texture cache mip block decompressed to an unexpected size.");
        temp.Position=0;
        temp.CopyTo(output);
      }
    }

    static byte[] BuildResidentDds(XbmDescriptor xbm) {
      if(xbm.ResidentBytes==null||xbm.ResidentBytes.Length==0)return null;
      int index=xbm.Mips.Count==0?0:Math.Min(Math.Max(0,xbm.ResidentMipIndex),xbm.Mips.Count-1);
      uint w=xbm.Mips.Count==0?(uint)Math.Max(1,xbm.Width):xbm.Mips[index].Item1;
      uint h=xbm.Mips.Count==0?(uint)Math.Max(1,xbm.Height):xbm.Mips[index].Item2;
      int mips=Math.Max(1,xbm.MipCount-index);
      using(var ms=new MemoryStream()) {
        WriteDdsHeader(ms,w,h,mips,xbm.Format,false,1);
        ms.Write(xbm.ResidentBytes,0,xbm.ResidentBytes.Length);
        return ms.ToArray();
      }
    }

    static BitmapSource DecodeDds(byte[] dds) {
      using(var ms=new MemoryStream(dds,false))
      using(var img=Pfim.Pfim.FromStream(ms)) {
        PixelFormat format;
        switch(img.Format) {
          case ImageFormat.Rgb24:format=PixelFormats.Bgr24;break;
          case ImageFormat.Rgba32:format=PixelFormats.Bgra32;break;
          case ImageFormat.Rgb8:format=PixelFormats.Gray8;break;
          case ImageFormat.R5g5b5:format=PixelFormats.Bgr555;break;
          case ImageFormat.R5g6b5:format=PixelFormats.Bgr565;break;
          case ImageFormat.R5g5b5a1:format=PixelFormats.Bgr555;break;
          default:throw new NotSupportedException("DDS decoder returned unsupported format "+img.Format+".");
        }
        int bytes=Math.Min(img.DataLen,img.Stride*img.Height);
        var copy=new byte[bytes];
        Buffer.BlockCopy(img.Data,0,copy,0,bytes);
        var bmp=BitmapSource.Create(img.Width,img.Height,96,96,format,null,copy,img.Stride);
        bmp.Freeze();
        return bmp;
      }
    }

    static void WriteTga(BitmapSource source,string path) {
      BitmapSource image=source;
      if(image.Format!=PixelFormats.Bgra32) {
        var converted=new FormatConvertedBitmap();
        converted.BeginInit();converted.Source=source;converted.DestinationFormat=PixelFormats.Bgra32;converted.EndInit();converted.Freeze();
        image=converted;
      }
      if(image.PixelWidth>UInt16.MaxValue||image.PixelHeight>UInt16.MaxValue)throw new InvalidDataException("Texture is too large for classic TGA.");
      int stride=image.PixelWidth*4;
      var pixels=new byte[stride*image.PixelHeight];
      image.CopyPixels(pixels,stride,0);
      using(var fs=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
      using(var bw=new BinaryWriter(fs)) {
        bw.Write((byte)0);bw.Write((byte)0);bw.Write((byte)2);
        bw.Write((ushort)0);bw.Write((ushort)0);bw.Write((byte)0);
        bw.Write((ushort)0);bw.Write((ushort)0);
        bw.Write((ushort)image.PixelWidth);bw.Write((ushort)image.PixelHeight);
        bw.Write((byte)32);bw.Write((byte)0x28);
        bw.Write(pixels);
      }
    }

    static void WriteDdsHeader(Stream stream,uint width,uint height,int mipCount,RedTextureFormat fmt,bool cube,int slices) {
      using(var bw=new BinaryWriter(stream,Encoding.ASCII,true)) {
        bw.Write(0x20534444u);bw.Write(124u);
        uint flags=0x00001007u;
        bool compressed=fmt!=RedTextureFormat.Rgba8;
        flags|=compressed?0x00080000u:0x00000008u;
        if(mipCount>1)flags|=0x00020000u;
        bw.Write(flags);bw.Write(height);bw.Write(width);
        bw.Write(compressed?CalculateTopLevelSize(width,height,fmt):width*4u);
        bw.Write(0u);bw.Write((uint)Math.Max(1,mipCount));
        for(int i=0;i<11;i++)bw.Write(0u);

        bw.Write(32u);
        uint pf=0,four=0,bpp=0,r=0,g=0,b=0,a=0;
        bool dx10=fmt==RedTextureFormat.BC7;
        switch(fmt) {
          case RedTextureFormat.Rgba8:pf=0x41;bpp=32;r=0x00ff0000;g=0x0000ff00;b=0x000000ff;a=0xff000000;break;
          case RedTextureFormat.BC1:pf=4;four=FourCC("DXT1");break;
          case RedTextureFormat.BC2:pf=4;four=FourCC("DXT3");break;
          case RedTextureFormat.BC3:pf=4;four=FourCC("DXT5");break;
          case RedTextureFormat.BC4:pf=4;four=FourCC("BC4U");break;
          case RedTextureFormat.BC5:pf=4;four=FourCC("BC5U");break;
          case RedTextureFormat.BC7:pf=4;four=FourCC("DX10");break;
        }
        bw.Write(pf);bw.Write(four);bw.Write(bpp);bw.Write(r);bw.Write(g);bw.Write(b);bw.Write(a);
        uint caps=0x1000u;if(mipCount>1)caps|=0x400008u;
        bw.Write(caps);bw.Write(cube?0x0000FE00u:0u);bw.Write(0u);bw.Write(0u);bw.Write(0u);
        if(dx10) {
          bw.Write(98u); // DXGI_FORMAT_BC7_UNORM
          bw.Write(3u);bw.Write(cube?4u:0u);bw.Write((uint)Math.Max(1,slices));bw.Write(0u);
        }
      }
    }

    static uint CalculateTopLevelSize(uint w,uint h,RedTextureFormat f) {
      if(f==RedTextureFormat.Rgba8)return w*h*4;
      uint block=(f==RedTextureFormat.BC1||f==RedTextureFormat.BC4)?8u:16u;
      return Math.Max(1u,(w+3)/4)*Math.Max(1u,(h+3)/4)*block;
    }

    static uint FourCC(string s) {
      var b=Encoding.ASCII.GetBytes(s);
      return (uint)(b[0]|(b[1]<<8)|(b[2]<<16)|(b[3]<<24));
    }

    static uint ReadU32(byte[] data,ref int pos){uint v=BitConverter.ToUInt32(data,pos);pos+=4;return v;}

    static string ReadCString(BinaryReader br) {
      var bytes=new List<byte>(96);
      while(true) {
        byte b=br.ReadByte();if(b==0)break;bytes.Add(b);
        if(bytes.Count>32768)throw new InvalidDataException("Texture cache string is too long.");
      }
      return Encoding.Default.GetString(bytes.ToArray());
    }

    sealed class LimitedReadStream:Stream {
      readonly Stream inner; long remaining;
      public LimitedReadStream(Stream inner,long length){this.inner=inner;remaining=length;}
      public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
      public override long Length=>remaining;public override long Position{get=>0;set=>throw new NotSupportedException();}
      public override int Read(byte[] buffer,int offset,int count){if(remaining<=0)return 0;count=(int)Math.Min(count,remaining);int n=inner.Read(buffer,offset,count);remaining-=n;return n;}
      public override void Flush(){}public override long Seek(long o,SeekOrigin so)=>throw new NotSupportedException();
      public override void SetLength(long v)=>throw new NotSupportedException();
      public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();
    }
  }
}