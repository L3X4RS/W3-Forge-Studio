using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using W3Edit.CR2W;
using W3Edit.CR2W.Types;

namespace W3Forge {
  public enum EnvValueKind { Bool, Float, Int, Curve, Reference, Name, String, RawBlock }

  public sealed class EnvCurvePoint {
    public int Index {get;internal set;}
    public float Time {get;internal set;}
    public float Scalar {get;internal set;}
    public float X {get;internal set;}
    public float Y {get;internal set;}
    public float Z {get;internal set;}
    public float W {get;internal set;}
    internal CVector Entry;
    internal CVariable TimeVar,ScalarVar,VectorVar;
    public string TimeText {get{return Time.ToString("0.######",CultureInfo.InvariantCulture);}}
    public string ValueText {get{return Scalar.ToString("0.######",CultureInfo.InvariantCulture);}}
  }

  public sealed class EnvImportType {
    public string Name {get;internal set;}
    public bool IsClass {get;internal set;}
    public Dictionary<string,string> Fields {get;private set;}=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  }

  public sealed class EnvParameter {
    public string Group {get;internal set;}
    public string Path {get;internal set;}
    public EnvValueKind Kind {get;internal set;}
    public CVariable Variable {get;internal set;}
    public List<EnvCurvePoint> Points {get;internal set;}=new List<EnvCurvePoint>();
    public string FullPath {get{return String.IsNullOrEmpty(Path)?Group:Group+"."+Path;}}
    public string KindText {get{return Kind.ToString().ToUpperInvariant();}}
    public string DisplayValue {
      get {
        try {
          if(Kind==EnvValueKind.Bool)return Convert.ToBoolean(EnvDocument.GetScalar(Variable),CultureInfo.InvariantCulture)?"true":"false";
          if(Kind==EnvValueKind.Float)return Convert.ToSingle(EnvDocument.GetScalar(Variable),CultureInfo.InvariantCulture).ToString("0.######",CultureInfo.InvariantCulture);
          if(Kind==EnvValueKind.Int)return Convert.ToInt64(EnvDocument.GetScalar(Variable),CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
          if(Kind==EnvValueKind.Reference)return EnvDocument.GetReference(Variable);
          if(Kind==EnvValueKind.Name)return Convert.ToString(EnvDocument.GetNamedValue(Variable),CultureInfo.InvariantCulture);
          if(Kind==EnvValueKind.String)return Convert.ToString(EnvDocument.GetScalar(Variable),CultureInfo.InvariantCulture);
          if(Kind==EnvValueKind.RawBlock){var b=EnvDocument.GetRawBytes(Variable);return (b==null?0:b.Length)+" bytes • Next-Gen raw block";}
          if(Kind==EnvValueKind.Curve)return Points.Count+" point"+(Points.Count==1?"":"s");
        } catch {}
        return "";
      }
    }
  }

  public sealed class EnvDocument {
    readonly CR2WFile file;
    readonly CVector envParams;
    public string FilePath {get;private set;}
    public IReadOnlyList<EnvParameter> Parameters {get;private set;}
    public IReadOnlyDictionary<string,EnvImportType> ImportTypes {get;private set;}
    public int UnsupportedBlockCount {get;private set;}

    EnvDocument(string path,CR2WFile file,CVector envParams,List<EnvParameter> parameters,
      Dictionary<string,EnvImportType> imports,int unsupported) {
      FilePath=path;this.file=file;this.envParams=envParams;Parameters=parameters;ImportTypes=imports;UnsupportedBlockCount=unsupported;
    }

    public static EnvDocument Load(string path) {
      if(String.IsNullOrWhiteSpace(path)||!File.Exists(path))throw new FileNotFoundException("ENV file not found.",path);
      var cr=new CR2WFile();cr.FileName=path;
      using(var fs=File.OpenRead(path))
      using(var br=new BinaryReader(fs))cr.Read(br);

      var envChunk=cr.chunks.FirstOrDefault(x=>String.Equals(x.Type,"CEnvironmentDefinition",StringComparison.OrdinalIgnoreCase));
      if(envChunk==null||!(envChunk.data is CVector root))
        throw new InvalidDataException("The selected resource is not a CEnvironmentDefinition ENV.");

      var ep=root.variables.OfType<CVector>().FirstOrDefault(x=>String.Equals(x.Name,"envParams",StringComparison.OrdinalIgnoreCase));
      if(ep==null)throw new InvalidDataException("ENV does not contain envParams.");

      var list=new List<EnvParameter>();
      var imports=new Dictionary<string,EnvImportType>(StringComparer.OrdinalIgnoreCase);
      AddImport(imports,"CEnvironmentDefinition",true,"envParams",ep.Type);
      int unsupported=0;
      foreach(var groupVar in ep.variables) {
        if(groupVar is CVector group) {
          AddImport(imports,ep.Type,false,group.Name,group.Type);
          WalkVector(group,group.Name,"",list,imports,ref unsupported);
        } else if(groupVar is CBytes) {
          AddImport(imports,ep.Type,false,groupVar.Name,groupVar.Type);
          list.Add(new EnvParameter{Group=groupVar.Name,Path="",Kind=EnvValueKind.RawBlock,Variable=groupVar});
        } else if(groupVar is CSoft || groupVar is CHandle) {
          AddImport(imports,ep.Type,false,groupVar.Name,groupVar.Type);
          list.Add(new EnvParameter{Group=groupVar.Name,Path="",Kind=EnvValueKind.Reference,Variable=groupVar});
        } else {
          unsupported++;
        }
      }
      return new EnvDocument(path,cr,ep,list,imports,unsupported);
    }

    static void WalkVector(CVector vector,string group,string prefix,List<EnvParameter> list,
      Dictionary<string,EnvImportType> imports,ref int unsupported) {
      foreach(var child in vector.variables) {
        string path=String.IsNullOrEmpty(prefix)?child.Name:prefix+"."+child.Name;
        if(child is CBool) {
          AddImport(imports,vector.Type,false,child.Name,"bool");
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Bool,Variable=child});
          continue;
        }
        if(child is CFloat) {
          AddImport(imports,vector.Type,false,child.Name,"float");
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Float,Variable=child});
          continue;
        }
        if(child is CSoft || child is CHandle) {
          AddImport(imports,vector.Type,false,child.Name,child.Type);
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Reference,Variable=child});
          continue;
        }
        if(child is CName) {
          AddImport(imports,vector.Type,false,child.Name,child.Type);
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Name,Variable=child});
          continue;
        }
        if(child is CBytes) {
          AddImport(imports,vector.Type,false,child.Name,child.Type);
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.RawBlock,Variable=child});
          continue;
        }
        if(child is CString) {
          AddImport(imports,vector.Type,false,child.Name,"string");
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.String,Variable=child});
          continue;
        }
        if(child is CVector nested) {
          AddImport(imports,vector.Type,false,child.Name,nested.Type);
          if(String.Equals(child.Type,"SSimpleCurve",StringComparison.OrdinalIgnoreCase)) {
            var p=ParseCurve(group,path,nested,imports);
            if(p!=null)list.Add(p); else unsupported++;
          } else {
            WalkVector(nested,group,path,list,imports,ref unsupported);
          }
          continue;
        }

        var val=FindValProperty(child);
        if(val!=null&&IsIntegral(val.PropertyType)) {
          AddImport(imports,vector.Type,false,child.Name,ScriptScalarType(val.PropertyType));
          list.Add(new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Int,Variable=child});
        } else {
          // Arrays only have direct meaning inside SSimpleCurve for the ENV studio.
          unsupported++;
        }
      }
    }

    static EnvParameter ParseCurve(string group,string path,CVector curve,Dictionary<string,EnvImportType> imports) {
      var array=curve.variables.OfType<CArray>().FirstOrDefault(x=>String.Equals(x.Name,"dataCurveValues",StringComparison.OrdinalIgnoreCase));
      if(array==null)return null;
      AddImport(imports,curve.Type,false,"dataCurveValues","array<SCurveDataEntry>");
      AddImport(imports,"SCurveDataEntry",false,"me","float");
      AddImport(imports,"SCurveDataEntry",false,"ntrolPoint","Vector");
      AddImport(imports,"SCurveDataEntry",false,"lue","float");
      var result=new EnvParameter{Group=group,Path=path,Kind=EnvValueKind.Curve,Variable=curve};
      for(int i=0;i<array.array.Count;i++) {
        var entry=array.array[i] as CVector;
        if(entry==null)continue;
        var time=entry.variables.FirstOrDefault(x=>String.Equals(x.Name,"me",StringComparison.OrdinalIgnoreCase));
        var scalar=entry.variables.FirstOrDefault(x=>String.Equals(x.Name,"lue",StringComparison.OrdinalIgnoreCase));
        var vec=entry.variables.OfType<CVector>().FirstOrDefault(x=>String.Equals(x.Name,"ntrolPoint",StringComparison.OrdinalIgnoreCase));
        result.Points.Add(new EnvCurvePoint{
          Index=i,Entry=entry,TimeVar=time,ScalarVar=scalar,VectorVar=vec,
          Time=ToFloat(GetScalar(time)),Scalar=ToFloat(GetScalar(scalar)),
          X=VectorValue(vec,"X"),Y=VectorValue(vec,"Y"),Z=VectorValue(vec,"Z"),W=VectorValue(vec,"W")
        });
      }
      return result;
    }

    static float VectorValue(CVector vector,string name) {
      if(vector==null)return 0f;
      return ToFloat(GetScalar(vector.variables.FirstOrDefault(x=>String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase))));
    }

    public void SetBool(EnvParameter p,bool value) {
      RequireKind(p,EnvValueKind.Bool);SetScalar(p.Variable,value);
    }
    public void SetFloat(EnvParameter p,float value) {
      RequireKind(p,EnvValueKind.Float);SetScalar(p.Variable,value);
    }
    public void SetInt(EnvParameter p,long value) {
      RequireKind(p,EnvValueKind.Int);SetScalar(p.Variable,value);
    }
    public void SetReference(EnvParameter p,string value) {
      RequireKind(p,EnvValueKind.Reference);
      if(value==null)value="";
      value=value.Trim().Replace('/','\\');
      while(value.StartsWith("\\"))value=value.Substring(1);
      if(value.Contains(":")||value.Contains(".."))throw new InvalidDataException("Reference must be a REDengine depot path, not an absolute filesystem path.");
      var hp=p.Variable.GetType().GetProperty("Handle",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      if(hp==null||!hp.CanWrite)throw new InvalidDataException("This resource reference is not editable.");
      hp.SetValue(p.Variable,value,null);
    }
    public void SetName(EnvParameter p,string value) {
      RequireKind(p,EnvValueKind.Name);
      var vp=p.Variable.GetType().GetProperty("Value",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      if(vp==null||!vp.CanWrite)throw new InvalidDataException("This name value is not editable.");
      vp.SetValue(p.Variable,value??"",null);
    }
    public void SetString(EnvParameter p,string value) {
      RequireKind(p,EnvValueKind.String);SetScalar(p.Variable,value??"");
    }
    public void SetRawBytes(EnvParameter p,byte[] bytes) {
      RequireKind(p,EnvValueKind.RawBlock);
      var bp=p.Variable.GetType().GetProperty("Bytes",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      if(bp==null||!bp.CanWrite)throw new InvalidDataException("This raw Next-Gen block is not writable.");
      bp.SetValue(p.Variable,bytes==null?new byte[0]:(byte[])bytes.Clone(),null);
    }
    public void SetCurvePoint(EnvParameter p,int point,float time,float scalar,float x,float y,float z,float w) {
      RequireKind(p,EnvValueKind.Curve);
      if(point<0||point>=p.Points.Count)throw new ArgumentOutOfRangeException("point");
      var cp=p.Points[point];
      SetScalar(cp.TimeVar,time);SetScalar(cp.ScalarVar,scalar);
      SetVector(cp.VectorVar,"X",x);SetVector(cp.VectorVar,"Y",y);SetVector(cp.VectorVar,"Z",z);SetVector(cp.VectorVar,"W",w);
      cp.Time=time;cp.Scalar=scalar;cp.X=x;cp.Y=y;cp.Z=z;cp.W=w;
    }

    static void SetVector(CVariable vector,string name,float value) {
      if(!(vector is CVector cv))return;
      SetScalar(cv.variables.FirstOrDefault(x=>String.Equals(x.Name,name,StringComparison.OrdinalIgnoreCase)),value);
    }
    static void RequireKind(EnvParameter p,EnvValueKind kind) {
      if(p==null||p.Kind!=kind)throw new InvalidOperationException("ENV parameter type mismatch.");
    }

    public string SaveWithBackup(string backupDirectory=null,string canonicalOriginalPath=null) {
      string stamp=DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
      string backup;
      if(String.IsNullOrWhiteSpace(backupDirectory)){
        backup=FilePath+".w3forge_"+stamp+".bak";
      } else {
        Directory.CreateDirectory(backupDirectory);
        backup=Path.Combine(backupDirectory,Path.GetFileName(FilePath)+".w3forge_"+stamp+".bak");
      }
      string temp=FilePath+".w3forge_write";
      File.Copy(FilePath,backup,false);
      try {
        // Canonicalize variable order only; this never replaces values or adds/removes fields.
        EnvCanonicalOrder.Organize(file,canonicalOriginalPath??FilePath);
        using(var fs=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None))
        using(var bw=new BinaryWriter(fs))file.Write(bw);

        var verify=new CR2WFile();verify.FileName=temp;
        using(var fs=File.OpenRead(temp))using(var br=new BinaryReader(fs))verify.Read(br);
        var env=verify.chunks.FirstOrDefault(x=>String.Equals(x.Type,"CEnvironmentDefinition",StringComparison.OrdinalIgnoreCase));
        if(env==null)throw new InvalidDataException("ENV validation failed after serialization.");

        File.Copy(temp,FilePath,true);
        return backup;
      } catch {
        if(File.Exists(backup))File.Copy(backup,FilePath,true);
        throw;
      } finally {
        try{if(File.Exists(temp))File.Delete(temp);}catch{}
      }
    }

    public static object GetScalar(CVariable variable) {
      if(variable==null)return null;
      var p=FindValProperty(variable);
      return p==null?null:p.GetValue(variable,null);
    }

    public static string GetReference(CVariable variable) {
      if(variable==null)return "";
      var p=variable.GetType().GetProperty("Handle",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      try{return p==null?"":Convert.ToString(p.GetValue(variable,null),CultureInfo.InvariantCulture)??"";}catch{return "";}
    }

    public static object GetNamedValue(CVariable variable) {
      if(variable==null)return null;
      var p=variable.GetType().GetProperty("Value",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      try{return p==null?null:p.GetValue(variable,null);}catch{return null;}
    }

    public static byte[] GetRawBytes(CVariable variable) {
      if(variable==null)return null;
      var p=variable.GetType().GetProperty("Bytes",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
      try{
        var b=p==null?null:p.GetValue(variable,null) as byte[];
        return b==null?null:(byte[])b.Clone();
      }catch{return null;}
    }

    static PropertyInfo FindValProperty(CVariable variable) {
      if(variable==null)return null;
      return variable.GetType().GetProperty("val",BindingFlags.Public|BindingFlags.Instance|BindingFlags.IgnoreCase);
    }

    static bool IsIntegral(Type t) {
      return t==typeof(int)||t==typeof(uint)||t==typeof(short)||t==typeof(ushort)||t==typeof(long)||t==typeof(ulong)||t==typeof(byte)||t==typeof(sbyte);
    }

    static string ScriptScalarType(Type t) {
      if(t==typeof(bool))return "bool";
      if(t==typeof(float)||t==typeof(double))return "float";
      if(t==typeof(uint)||t==typeof(ushort)||t==typeof(ulong))return "int";
      return "int";
    }

    static void AddImport(Dictionary<string,EnvImportType> imports,string type,bool isClass,string field,string fieldType) {
      if(String.IsNullOrWhiteSpace(type)||String.IsNullOrWhiteSpace(field)||String.IsNullOrWhiteSpace(fieldType))return;
      if(type=="Vector")return;
      if(!imports.TryGetValue(type,out var def)) {
        def=new EnvImportType{Name=type,IsClass=isClass};
        imports[type]=def;
      }
      if(isClass)def.IsClass=true;
      if(!def.Fields.ContainsKey(field))def.Fields[field]=fieldType;
    }

    static void SetScalar(CVariable variable,object value) {
      if(variable==null)throw new InvalidDataException("ENV scalar value is missing.");
      var p=FindValProperty(variable);
      if(p==null||!p.CanWrite)throw new InvalidDataException("ENV value is not editable: "+variable.Name);
      object converted;
      if(p.PropertyType==typeof(bool))converted=Convert.ToBoolean(value,CultureInfo.InvariantCulture);
      else if(p.PropertyType==typeof(float))converted=Convert.ToSingle(value,CultureInfo.InvariantCulture);
      else if(p.PropertyType==typeof(double))converted=Convert.ToDouble(value,CultureInfo.InvariantCulture);
      else converted=Convert.ChangeType(value,p.PropertyType,CultureInfo.InvariantCulture);
      p.SetValue(variable,converted,null);
    }

    static float ToFloat(object value) {
      try{return value==null?0f:Convert.ToSingle(value,CultureInfo.InvariantCulture);}catch{return 0f;}
    }

    public static string DepotPath(Project project,string file) {
      if(project==null||String.IsNullOrWhiteSpace(file))return null;
      var full=Path.GetFullPath(file);
      string[] roots={project.ModBundle,project.Files};
      foreach(var root in roots) {
        if(String.IsNullOrWhiteSpace(root)||!Directory.Exists(root))continue;
        var r=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
        if(full.StartsWith(r,StringComparison.OrdinalIgnoreCase))
          return full.Substring(r.Length).Replace('/','\\');
      }
      return null;
    }
  }
}