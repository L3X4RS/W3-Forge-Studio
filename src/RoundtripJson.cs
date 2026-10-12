using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using W3Edit.CR2W;
using W3Edit.CR2W.Types;

namespace W3Forge {
  public sealed class RoundtripResult {
    public string Source,JsonPath,Backup,Report;
    public int Fields,Changed;
  }
  public static class RoundtripJson {
    const string Schema="w3forge.roundtrip.v1";
    static readonly HashSet<string> TextExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
      ".ws",".xml",".csv",".ini",".txt",".yaml",".yml",".md",".bat",".cfg",".redscript"
    };
    static readonly HashSet<string> BinaryExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
      ".env",".w2w",".w2ent",".w2mesh",".w2mi",".w2mg",".w2rig",".w2anims",".w2cutscene",
      ".w2scene",".w2l",".w2fnt",".w2beh",".w2p",".w2quest",".w2phase",".w2comm",
      ".w2journal",".w2def",".w2dset",".w2store",".w2action",".w2effects",".w2ter"
    };
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=100*1024*1024,RecursionLimit=60};}
    public static bool CanExport(string path){var ext=Path.GetExtension(path);return TextExtensions.Contains(ext)||BinaryExtensions.Contains(ext);}
    public static string Sha(string p){using(var hash=SHA256.Create())using(var s=File.OpenRead(p))return BitConverter.ToString(hash.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
    static Dictionary<string,object> Obj(object o){var d=o as Dictionary<string,object>;if(d==null)throw new InvalidDataException("Invalid JSON object.");return d;}
    static string Str(Dictionary<string,object> d,string key){object o;if(!d.TryGetValue(key,out o)||!(o is string))throw new InvalidDataException("Missing JSON string: "+key);return (string)o;}
    static object[] Arr(Dictionary<string,object> d,string key){object o;if(!d.TryGetValue(key,out o)||!(o is object[]))throw new InvalidDataException("Missing JSON array: "+key);return (object[])o;}
    static void CheckSource(Dictionary<string,object> d,string source){
      if(Str(d,"schema")!=Schema)throw new InvalidDataException("Unsupported roundtrip schema.");
      if(Str(d,"sourceName")!=Path.GetFileName(source)||Str(d,"extension")!=Path.GetExtension(source).ToLowerInvariant())
        throw new InvalidDataException("Source filename or extension does not match JSON.");
      if(!String.Equals(Str(d,"sourceSha256"),Sha(source),StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Base SHA256 changed since JSON export. Original preserved.");
    }
    public static string JsonPath(string source,string files,string raw){
      var p=Path.GetFullPath(source);
      if(files!=null&&raw!=null){
        string root=Path.GetFullPath(files).TrimEnd('\\')+"\\";
        string rawRoot=Path.GetFullPath(raw).TrimEnd('\\')+"\\";
        if(p.StartsWith(root,StringComparison.OrdinalIgnoreCase)&&!p.StartsWith(rawRoot,StringComparison.OrdinalIgnoreCase))
          return Path.Combine(raw,"JSON_Roundtrip",p.Substring(root.Length)+".json");
      }
      return p+".json";
    }
    public static string SourcePath(string json,string files,string raw){
      string p=Path.GetFullPath(json);
      if(!p.EndsWith(".json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Expected .json sidecar.");
      if(files!=null&&raw!=null){
        string root=Path.GetFullPath(Path.Combine(raw,"JSON_Roundtrip")).TrimEnd('\\')+"\\";
        if(p.StartsWith(root,StringComparison.OrdinalIgnoreCase)){
          string target=Path.GetFullPath(Path.Combine(files,p.Substring(root.Length,p.Length-root.Length-5)));
          if(!target.StartsWith(Path.GetFullPath(files).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("JSON cannot escape the project folder.");
          return target;
        }
      }
      return p.Substring(0,p.Length-5);
    }
    sealed class Field {
      public int Chunk;
      public string Locator,Path,Type;
      public CVariable Var;
      public object Original;
    }
    static string SafeType(CVariable v){
      if(v is CFloat)return "float";
      if(v is CBool)return "bool";
      if(v is CInt32)return "int32";
      if(v is CUInt32)return "uint32";
      if(v is CUInt16)return "uint16";
      if(v is CUInt8)return "uint8";
      return null;
    }
    static PropertyInfo Property(CVariable v){return v.GetType().GetProperty("val",BindingFlags.Instance|BindingFlags.Public|BindingFlags.IgnoreCase);}
    static object Value(CVariable v){var p=Property(v);return p==null?null:p.GetValue(v,null);}
    static void Visit(CVariable v,int chunk,string locator,string name,List<Field> output,int depth){
      if(depth>64)throw new InvalidDataException("Excessively nested CR2W.");
      string type=SafeType(v);
      if(type!=null){
        var scalar=Value(v);
        if(scalar!=null){
          if(type=="float"&&(Single.IsNaN(Convert.ToSingle(scalar))||Single.IsInfinity(Convert.ToSingle(scalar))))return;
          output.Add(new Field{Chunk=chunk,Locator=locator,Path=name,Type=type,Var=v,Original=scalar});
        }
      }else if(v is CVector){
        var children=((CVector)v).variables;
        for(int i=0;i<children.Count;i++)Visit(children[i],chunk,locator+"/"+i,name+"."+children[i].Name,output,depth+1);
      }else if(v is CArray){
        var children=((CArray)v).array;
        for(int i=0;i<children.Count;i++){
          var child=children[i] as CVariable;
          if(child!=null)Visit(child,chunk,locator+"/a"+i,name+"["+i+"]",output,depth+1);
        }
      }
    }
    static CR2WFile Read(string path){
      var f=new CR2WFile();f.FileName=path;
      using(var s=File.OpenRead(path))using(var br=new BinaryReader(s))f.Read(br);
      if(f.chunks.Count==0)throw new InvalidDataException("Not a readable CR2W resource.");
      return f;
    }
    static List<Field> Fields(CR2WFile f){
      var result=new List<Field>();
      for(int i=0;i<f.chunks.Count;i++){
        var v=f.chunks[i].data as CVariable;
        if(v!=null)Visit(v,i,"r",f.chunks[i].Type+" #"+(i+1),result,0);
      }
      return result;
    }
    static bool Equal(object a,object b,string type){
      if(a==null)return false;
      try{
        if(type=="bool")return a is bool && Convert.ToBoolean(a)==Convert.ToBoolean(b);
        if(a is string||a is bool)return false;
        if(type=="float")return Convert.ToSingle(a,CultureInfo.InvariantCulture)==Convert.ToSingle(b,CultureInfo.InvariantCulture);
        return Convert.ToDecimal(a,CultureInfo.InvariantCulture)==Convert.ToDecimal(b,CultureInfo.InvariantCulture);
      }catch{return false;}
    }
    static void Set(Field f,object raw){
      var p=Property(f.Var);
      if(p==null||!p.CanWrite)throw new InvalidDataException("Nonwritable field "+f.Path);
      object value;
      if(f.Type=="bool"){
        if(!(raw is bool))throw new InvalidDataException("Expected bool at "+f.Path);
        value=(bool)raw;
      }else{
        if(raw==null||raw is string||raw is bool)throw new InvalidDataException("Expected number at "+f.Path);
        double x=Convert.ToDouble(raw,CultureInfo.InvariantCulture);
        if(double.IsNaN(x)||double.IsInfinity(x))throw new InvalidDataException("NaN not allowed.");
        if(f.Type=="float"){if(Math.Abs(x)>float.MaxValue)throw new InvalidDataException("Float out of range.");value=(float)x;}
        else{
          double lo=f.Type=="int32"?int.MinValue:0;
          double hi=f.Type=="uint8"?byte.MaxValue:f.Type=="uint16"?ushort.MaxValue:
            f.Type=="uint32"?uint.MaxValue:int.MaxValue;
          if(x!=Math.Floor(x)||x<lo||x>hi)throw new InvalidDataException("Integer overflow at "+f.Path);
          value=Convert.ChangeType(x,p.PropertyType,CultureInfo.InvariantCulture);
        }
      }
      p.SetValue(f.Var,value,null);
    }
    static Dictionary<string,object> Envelope(string source){
      return new Dictionary<string,object>{
        {"schema",Schema},{"sourceName",Path.GetFileName(source)},
        {"extension",Path.GetExtension(source).ToLowerInvariant()},{"sourceSha256",Sha(source)},
        {"note","Edit only the values. All unknown CR2W binary data remain in the RAW source. SHA256 required."}
      };
    }
    public static RoundtripResult Export(string source,string json){
      if(!File.Exists(source))throw new FileNotFoundException("RAW file missing.",source);
      if(!CanExport(source))throw new InvalidDataException("No safe JSON adapter for this file type.");
      if(File.Exists(json))throw new IOException("JSON already exists: "+json+". Existing edits will not be overwritten.");
      var d=Envelope(source);
      int count=0;string type;
      if(TextExtensions.Contains(Path.GetExtension(source))){
        TextResourceFormat fmt;string txt=TextResourceIO.ReadAllText(source,out fmt);
        if(Path.GetExtension(source).Equals(".xml",StringComparison.OrdinalIgnoreCase))TextResourceIO.ValidateXmlText(txt,fmt,source);
        d["encoding"]=fmt.Name;d["bom"]=fmt.HasBom;
        if(Path.GetExtension(source).Equals(".csv",StringComparison.OrdinalIgnoreCase)){
          char sep=txt.Contains(";")?';':txt.Contains("\t")?'\t':',';
          var rows=StructuredEditors.ParseCsv(txt,sep);
          type="csv";d["delimiter"]=sep.ToString();d["rows"]=rows;
          d["lineEnd"]=txt.Contains("\r\n")?"\r\n":"\n";
          d["endsWithNewline"]=txt.EndsWith("\n",StringComparison.Ordinal);
          count=rows.Sum(x=>x.Count);
        }else{type="text";d["content"]=txt;count=1;}
      }else{
        var native=Read(source);var fields=Fields(native);
        type="cr2w";d["version"]=native.FileVersion;d["chunks"]=native.chunks.Count;
        d["fields"]=fields.Select(x=>(object)new Dictionary<string,object>{
          {"chunk",x.Chunk},{"locator",x.Locator},{"path",x.Path},{"type",x.Type},
          {"original",x.Original},{"value",x.Original}
        }).ToArray();
        count=fields.Count;
      }
      d["format"]=type;
      Directory.CreateDirectory(Path.GetDirectoryName(json));
      string temp=json+".w3forge_tmp";
      if(File.Exists(temp))throw new IOException("Temporary JSON already exists.");
      try{
        File.WriteAllText(temp,Json().Serialize(d),new UTF8Encoding(false));
        CheckSource(Obj(Json().DeserializeObject(File.ReadAllText(temp,Encoding.UTF8))),source);
        File.Move(temp,json);
      }finally{if(File.Exists(temp))File.Delete(temp);}
      return new RoundtripResult{Source=source,JsonPath=json,Fields=count,Report=type+" => JSON; "+count+" editable fields"};
    }
    public static RoundtripResult Import(string source,string json,string backups,bool commit){
      if(!File.Exists(source)||!File.Exists(json))throw new FileNotFoundException("Missing RAW source or JSON.");
      if(!CanExport(source))throw new InvalidDataException("Unsupported JSON compiler for this format.");
      if(new FileInfo(json).Length>99L*1024*1024)throw new InvalidDataException("JSON too large.");
      var d=Obj(Json().DeserializeObject(File.ReadAllText(json,Encoding.UTF8)));
      CheckSource(d,source);
      string type=Str(d,"format"),tmp=source+".w3forge_json_stage";
      if(File.Exists(tmp))throw new IOException("Staged file already exists.");
      int changes=0,all=0;
      try{
        if(type=="text"){
          TextResourceFormat encoding;
          string old=TextResourceIO.ReadAllText(source,out encoding),edit=Str(d,"content");
          all=1;if(old!=edit){
            TextResourceIO.WriteAllText(tmp,edit,encoding,Path.GetExtension(source).Equals(".xml",StringComparison.OrdinalIgnoreCase));
            changes=1;
          }
        }else if(type=="csv"){
          TextResourceFormat encoding;
          var old=TextResourceIO.ReadAllText(source,out encoding);
          string sep=Str(d,"delimiter");
          if(sep.Length!=1||";,\t".IndexOf(sep[0])<0)throw new InvalidDataException("Unsupported CSV separator.");
          var original=StructuredEditors.ParseCsv(old,sep[0]);
          var updated=Arr(d,"rows");
          if(updated.Length!=original.Count)throw new InvalidDataException("CSV shape changed; edit columns with built-in CSV grid.");
          var rows=new List<IList<string>>();
          for(int i=0;i<updated.Length;i++){
            var ar=updated[i] as object[];
            if(ar==null||ar.Length!=original[i].Count)throw new InvalidDataException("CSV columns changed; use CSV grid.");
            var cells=new List<string>();
            for(int j=0;j<ar.Length;j++){
              if(!(ar[j] is string))throw new InvalidDataException("CSV cells must be strings.");
              cells.Add((string)ar[j]);all++;
              if(cells[j]!=original[i][j])changes++;
            }
            rows.Add(cells);
          }
          if(changes>0){
            string newline=Str(d,"lineEnd");
            if(newline!="\r\n"&&newline!="\n")throw new InvalidDataException("Invalid lineEnding");
            bool ends=d.ContainsKey("endsWithNewline") && d["endsWithNewline"] is bool && (bool)d["endsWithNewline"];
            TextResourceIO.WriteAllText(tmp,StructuredEditors.SerializeCsv(rows,sep[0],newline,ends),encoding,false);
          }
        }else if(type=="cr2w"){
          var cr=Read(source);var fields=Fields(cr);var rows=Arr(d,"fields");
          if(Convert.ToInt32(d["version"])!=cr.FileVersion||Convert.ToInt32(d["chunks"])!=cr.chunks.Count
             ||rows.Length!=fields.Count)throw new InvalidDataException("CR2W schema mismatch; no file modified.");
          all=fields.Count;
          for(int i=0;i<rows.Length;i++){
            var f=fields[i];var row=Obj(rows[i]);
            if(Convert.ToInt32(row["chunk"])!=f.Chunk||Str(row,"locator")!=f.Locator ||
               Str(row,"path")!=f.Path||Str(row,"type")!=f.Type || !Equal(row["original"],f.Original,f.Type))
              throw new InvalidDataException("CR2W field identity/old value mismatch at "+i);
            if(!row.ContainsKey("value"))throw new InvalidDataException("JSON field value missing at "+i);
            if(!Equal(row["value"],f.Original,f.Type)){Set(f,row["value"]);changes++;}
          }
          if(changes>0){
            using(var fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new BinaryWriter(fs))cr.Write(writer);
            var check=Read(tmp);var again=Fields(check);
            if(check.FileVersion!=cr.FileVersion||check.chunks.Count!=cr.chunks.Count||again.Count!=fields.Count)
              throw new InvalidDataException("CR2W roundtrip structure changed, original untouched.");
            for(int i=0;i<fields.Count;i++)
              if(again[i].Locator!=fields[i].Locator||again[i].Path!=fields[i].Path ||
                 !Equal(again[i].Original,Value(fields[i].Var),fields[i].Type))
                throw new InvalidDataException("CR2W post-save field mismatch at "+fields[i].Path);
          }
        }else throw new InvalidDataException("Unrecognized roundtrip format.");
        if(changes>0&&!File.Exists(tmp))throw new InvalidDataException("No output was generated.");
        if(!commit||changes==0)return new RoundtripResult{Source=source,JsonPath=json,Fields=all,Changed=changes,
          Report="PREVIEW: "+changes+"/"+all+" "+type+" fields modified (original untouched)"};
        CheckSource(d,source);
        Directory.CreateDirectory(backups);
        string backup=Path.Combine(backups,Path.GetFileName(source)+"."+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".bak");
        if(File.Exists(backup))throw new IOException("Backup name already exists.");
        File.Copy(source,backup,false);
        string jsonBackup=Path.Combine(backups,Path.GetFileName(json)+"."+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".bak");
        if(File.Exists(jsonBackup))throw new IOException("JSON backup name already exists.");
        File.Copy(json,jsonBackup,false);
        string rebased=json+".w3forge_rebase";
        if(File.Exists(rebased))throw new IOException("JSON rebase temp already exists.");
        try{
          File.Copy(tmp,source,true);
          // Refresh the sidecar after a successful compile. Next user edit can
          // compile again against NEW base SHA, without discarding their JSON.
          var updated=Export(source,rebased);
          File.Copy(rebased,json,true);
          CheckSource(Obj(Json().DeserializeObject(File.ReadAllText(json,Encoding.UTF8))),source);
        }catch{
          File.Copy(backup,source,true);
          File.Copy(jsonBackup,json,true);
          throw;
        }finally{if(File.Exists(rebased))File.Delete(rebased);}
        return new RoundtripResult{Source=source,JsonPath=json,Fields=all,Changed=changes,Backup=backup,
          Report="COMPILED "+changes+"/"+all+" "+type+" edits. Original and JSON now synchronized. RAW backup: "+backup+"; JSON backup: "+jsonBackup};
      }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
  }
}