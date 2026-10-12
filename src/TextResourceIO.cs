using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace W3Forge {
  public sealed class TextResourceFormat {
    public Encoding Encoding {get;internal set;}
    public byte[] Preamble {get;internal set;}
    public string Name {get;internal set;}
    public bool HasBom {get{return Preamble!=null&&Preamble.Length>0;}}
  }

  public static class TextResourceIO {
    static readonly Regex XmlEncoding=new Regex(
      @"<\?xml[^>]*\bencoding\s*=\s*[""'](?<enc>[^""']+)[""']",
      RegexOptions.IgnoreCase|RegexOptions.Compiled);

    public static string ReadAllText(string file,out TextResourceFormat format) {
      if(String.IsNullOrWhiteSpace(file))throw new ArgumentNullException("file");
      var bytes=File.ReadAllBytes(file);
      format=Detect(bytes);
      int skip=format.Preamble==null?0:format.Preamble.Length;
      return format.Encoding.GetString(bytes,skip,bytes.Length-skip);
    }

    public static void WriteAllText(string file,string text,TextResourceFormat format,bool validateXml) {
      if(format==null)throw new ArgumentNullException("format");
      if(text==null)text="";
      if(validateXml)ValidateXmlText(text,format,file);
      var body=format.Encoding.GetBytes(text);
      var preamble=format.Preamble??new byte[0];
      using(var fs=new FileStream(file,FileMode.Create,FileAccess.Write,FileShare.None)) {
        if(preamble.Length>0)fs.Write(preamble,0,preamble.Length);
        fs.Write(body,0,body.Length);
      }
    }

    public static TextResourceFormat ValidateXmlFile(string file) {
      TextResourceFormat format;
      var text=ReadAllText(file,out format);
      ValidateXmlText(text,format,file);
      return format;
    }

    public static void ValidateXmlText(string text,TextResourceFormat format,string file) {
      try {
        XDocument.Parse(text,LoadOptions.PreserveWhitespace);
      } catch(Exception ex) {
        throw new InvalidDataException("Invalid XML"+(String.IsNullOrWhiteSpace(file)?"":" in "+file)+": "+ex.Message,ex);
      }

      var m=XmlEncoding.Match(text??"");
      if(!m.Success)return;
      string declared=m.Groups["enc"].Value.Trim();
      if(declared.Length==0)return;
      Encoding declaredEncoding;
      try {declaredEncoding=Encoding.GetEncoding(declared);}
      catch(Exception ex) {
        throw new InvalidDataException("Unsupported XML encoding declaration '"+declared+"'"+(String.IsNullOrWhiteSpace(file)?"":" in "+file)+".",ex);
      }
      if(!SameEncodingFamily(declaredEncoding,format.Encoding)) {
        throw new InvalidDataException(
          "XML encoding declaration is '"+declared+"' but the file is being saved as "+format.Name+
          ". Change the declaration or restore the original encoding before saving.");
      }
    }

    public static string Describe(TextResourceFormat format) {
      if(format==null)return "unknown";
      return format.Name+(format.HasBom?" + BOM":" (no BOM)");
    }

    static TextResourceFormat Detect(byte[] bytes) {
      if(bytes==null)bytes=new byte[0];

      if(Starts(bytes,new byte[]{0xEF,0xBB,0xBF}))
        return F(new UTF8Encoding(false,true),new byte[]{0xEF,0xBB,0xBF},"UTF-8");
      if(Starts(bytes,new byte[]{0xFF,0xFE,0x00,0x00}))
        return F(new UTF32Encoding(false,false,true),new byte[]{0xFF,0xFE,0x00,0x00},"UTF-32 LE");
      if(Starts(bytes,new byte[]{0x00,0x00,0xFE,0xFF}))
        return F(new UTF32Encoding(true,false,true),new byte[]{0x00,0x00,0xFE,0xFF},"UTF-32 BE");
      if(Starts(bytes,new byte[]{0xFF,0xFE}))
        return F(new UnicodeEncoding(false,false,true),new byte[]{0xFF,0xFE},"UTF-16 LE");
      if(Starts(bytes,new byte[]{0xFE,0xFF}))
        return F(new UnicodeEncoding(true,false,true),new byte[]{0xFE,0xFF},"UTF-16 BE");

      // REDengine text resources are normally UTF-8 when no BOM is present.
      // Keep BOM-less files BOM-less instead of silently changing their byte format.
      return F(new UTF8Encoding(false,true),new byte[0],"UTF-8");
    }

    static TextResourceFormat F(Encoding e,byte[] preamble,string name) {
      return new TextResourceFormat{Encoding=e,Preamble=preamble,Name=name};
    }

    static bool Starts(byte[] data,byte[] prefix) {
      if(data.Length<prefix.Length)return false;
      for(int i=0;i<prefix.Length;i++)if(data[i]!=prefix[i])return false;
      return true;
    }

    static bool SameEncodingFamily(Encoding a,Encoding b) {
      if(a==null||b==null)return false;
      string x=a.WebName.ToLowerInvariant();
      string y=b.WebName.ToLowerInvariant();
      if(x==y)return true;
      if(IsUtf8(x)&&IsUtf8(y))return true;
      if(IsUtf16(x)&&IsUtf16(y))return true;
      if(IsUtf32(x)&&IsUtf32(y))return true;
      return false;
    }
    static bool IsUtf8(string x){return x=="utf-8"||x=="utf8";}
    static bool IsUtf16(string x){return x.StartsWith("utf-16")||x=="unicode"||x=="utf-16le"||x=="utf-16be";}
    static bool IsUtf32(string x){return x.StartsWith("utf-32")||x=="utf-32le"||x=="utf-32be";}
  }
}