using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using W3Edit.CR2W;
using W3Edit.CR2W.Editors;
using W3Edit.CR2W.Types;

namespace W3Forge {
 public sealed class EnvSmartChange {
   public string TargetFile,SourceFile,Path;
   public int Index;
   public float Before,Reference,After;
   public override string ToString(){return Path+" ["+Index+"]   "+Before.ToString("0.######",CultureInfo.InvariantCulture)+" -> "+After.ToString("0.######",CultureInfo.InvariantCulture)+"  (source "+Reference.ToString("0.######",CultureInfo.InvariantCulture)+")";}
 }
 public sealed class EnvSmartPlan {
   public List<EnvSmartChange> Changes=new List<EnvSmartChange>();
   public Dictionary<string,string> TargetHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   public Dictionary<string,string> SourceHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   public int Examined,Skipped,Missing;
   public HashSet<string> Groups=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   public double Blend=0.10,MaxRelative=0.04,MaxAbsolute=0.025;
 }
 public static class EnvSmartWorkflow {
   public static readonly string[] Groups={"m_globalLight","m_speedTree","m_toneMapping","m_finalColorBalance","m_ibl","m_shadows","m_water","m_bloomNew"};
   static readonly HashSet<string> Forbidden=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"m_colorGroups","m_globalFog","m_flareColorGroups"};
   static readonly string[] Risky={"balanceMap","activatedBalanceMap","colorcube","lookup","randomColorsBranches","pigment","sunColor","sunAndMoon","exposure","postScale","gamma","whitepoint","lightDir","saturation","hue","colorGroups","fog"};
   public static string HashFile(string path){
     using(var fs=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-","");
   }
   static bool SafeCurve(string group,string fullPath){
     if(Forbidden.Contains(group))return false;
     if(Risky.Any(word=>fullPath.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0))return false;
     if(group=="m_toneMapping"){
       return fullPath.EndsWith(".toeStrength",StringComparison.OrdinalIgnoreCase)
           ||fullPath.EndsWith(".linearStrength",StringComparison.OrdinalIgnoreCase)
           ||fullPath.EndsWith(".shoulderStrength",StringComparison.OrdinalIgnoreCase)
           ||fullPath.EndsWith(".rejectThreshold",StringComparison.OrdinalIgnoreCase);
     }
     // Color balance can only be edited directly in ENV Studio. Smart transfer never moves grading.
     if(group=="m_finalColorBalance")return false;
     return true;
   }
   static float Interp(List<EnvCurvePoint> points,float time){
     if(time<=points[0].Time)return points[0].Scalar;
     if(time>=points[points.Count-1].Time)return points[points.Count-1].Scalar;
     for(int i=1;i<points.Count;i++){
       var a=points[i-1];var b=points[i];
       if(time<=b.Time && time>=a.Time)return a.Scalar+(b.Scalar-a.Scalar)*(time-a.Time)/(b.Time-a.Time);
     }
     throw new InvalidDataException("Curve interpolation failed.");
   }
   static bool Valid(EnvParameter parameter){
     if(parameter==null||parameter.Kind!=EnvValueKind.Curve||parameter.Points.Count<1||parameter.Points.Count>32)return false;
     float last=-1f;
     foreach(var point in parameter.Points){
       if(float.IsNaN(point.Time)||float.IsNaN(point.Scalar)||float.IsInfinity(point.Time)||float.IsInfinity(point.Scalar))return false;
       if(point.Time<0f||point.Time>1f||point.Time<=last)return false;
       if(!(point.ScalarVar is CFloat))return false;
       last=point.Time;
     }
     return true;
   }
   public static EnvSmartPlan Preview(IEnumerable<Tuple<string,string>> pairs,IEnumerable<string> selected,double blend,double maxRelative,double maxAbsolute) {
     if(blend<=0||blend>0.30||maxRelative<=0||maxRelative>0.15||maxAbsolute<=0||maxAbsolute>0.10)throw new ArgumentOutOfRangeException("Merge limits","Use blend 0..30%, relative cap <=15% and absolute cap <=0.10.");
     var plan=new EnvSmartPlan{Blend=blend,MaxRelative=maxRelative,MaxAbsolute=maxAbsolute};
     foreach(var group in selected??Enumerable.Empty<string>())
       if(Groups.Any(x=>String.Equals(x,group,StringComparison.OrdinalIgnoreCase)))plan.Groups.Add(group);
     if(plan.Groups.Count==0)throw new InvalidOperationException("Select at least one group to preview.");
     foreach(var pair in pairs){
       string target=Path.GetFullPath(pair.Item1),source=Path.GetFullPath(pair.Item2);
       if(!File.Exists(target)||!File.Exists(source)){plan.Missing++;continue;}
       if(String.Equals(target,source,StringComparison.OrdinalIgnoreCase)){plan.Skipped++;continue;}
       if(plan.TargetHashes.ContainsKey(target))throw new InvalidDataException("Duplicate target file: "+target);
       var dst=EnvDocument.Load(target);
       var src=EnvDocument.Load(source);
       plan.Examined++;
       plan.TargetHashes.Add(target,HashFile(target));
       if(!plan.SourceHashes.ContainsKey(source))plan.SourceHashes.Add(source,HashFile(source));
       var srcGroups=src.Parameters.GroupBy(x=>x.FullPath,StringComparer.OrdinalIgnoreCase)
           .ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
       var dstGroups=dst.Parameters.GroupBy(x=>x.FullPath,StringComparer.OrdinalIgnoreCase);
       foreach(var entry in dstGroups) {
         if(entry.Count()!=1){plan.Skipped++;continue;}
         var dp=entry.First();
         if(!plan.Groups.Contains(dp.Group)||!SafeCurve(dp.Group,dp.FullPath)||!Valid(dp))continue;
         List<EnvParameter> spList;
         if(!srcGroups.TryGetValue(dp.FullPath,out spList)||spList.Count!=1||!Valid(spList[0])){plan.Skipped++;continue;}
         var sp=spList[0];
         foreach(var pt in dp.Points) {
           float from=Interp(sp.Points,pt.Time);
           if(float.IsNaN(from)||float.IsInfinity(from)||Math.Abs(from)>100f||Math.Abs(pt.Scalar)>100f){plan.Skipped++;continue;}
           if(pt.Scalar==0||Math.Sign(pt.Scalar)!=Math.Sign(from)){plan.Skipped++;continue;}
           double limit=Math.Min(maxAbsolute,Math.Abs(pt.Scalar)*maxRelative);
           float to=(float)(pt.Scalar+Math.Max(-limit,Math.Min(limit,blend*(from-pt.Scalar))));
           if(Math.Abs(to-pt.Scalar)<0.0001)continue;
           plan.Changes.Add(new EnvSmartChange{TargetFile=target,SourceFile=source,Path=dp.FullPath,Index=pt.Index,Before=pt.Scalar,Reference=from,After=to});
         }
       }
     }
     return plan;
   }
   public static List<string> MatchingPairs(string projectFiles,string sourceFiles,bool includeSpecial){
     if(!Directory.Exists(projectFiles)||!Directory.Exists(sourceFiles))throw new DirectoryNotFoundException("Choose valid project files and source files folders.");
     var result=new List<string>();
     foreach(var target in Directory.GetFiles(projectFiles,"*.env",SearchOption.AllDirectories)){
       var rel=target.Substring(projectFiles.TrimEnd('\\','/').Length).TrimStart('\\','/');
       if(!includeSpecial){
         var path=rel.ToLowerInvariant();
         if(new[]{"cutscene","interior","/wip/","\\wip\\","cave","q602","q603","overlay","_int.env","sewers"}.Any(s=>path.Contains(s)))continue;
       }
       if(File.Exists(Path.Combine(sourceFiles,rel)))result.Add(target);
     }
     return result;
   }
   public static void Apply(EnvSmartPlan plan,string backupRoot){
     if(plan==null||plan.Changes.Count==0)throw new InvalidOperationException("Nothing to apply.");
     var groups=plan.Changes.GroupBy(x=>x.TargetFile,StringComparer.OrdinalIgnoreCase).ToArray();
     // Check EVERY target before touching any file; no partial overwrite on stale previews.
     foreach(var item in plan.TargetHashes)if(!File.Exists(item.Key)||HashFile(item.Key)!=item.Value)
       throw new IOException("ENV changed after preview; rebuild preview: "+item.Key);
     foreach(var item in plan.SourceHashes)if(!File.Exists(item.Key)||HashFile(item.Key)!=item.Value)
       throw new IOException("Source ENV changed after preview; rebuild preview: "+item.Key);
     Directory.CreateDirectory(backupRoot);
     var prepared=new List<Tuple<string,string>>();
     var stagedTemps=new List<string>();
     try {
       foreach(var group in groups){
         // Prepare a temporary copy; never write a live ENV until every file is validated.
         var projected=group.Key+".w3forge_mergein_"+Guid.NewGuid().ToString("N");
         File.Copy(group.Key,projected,false);
         stagedTemps.Add(projected);
         var intermediate=EnvDocument.Load(projected);
         foreach(var change in group){
           var p=intermediate.Parameters.Single(x=>x.FullPath==change.Path);
           ((CFloat)p.Points[change.Index].ScalarVar).val=change.After;
         }
         intermediate.SaveWithBackup(Path.Combine(backupRoot,"stage_backups"),group.Key);
         var after=EnvDocument.Load(projected);
         var values=after.Parameters.GroupBy(x=>x.FullPath,StringComparer.OrdinalIgnoreCase).ToDictionary(x=>x.Key,x=>x.ToList(),StringComparer.OrdinalIgnoreCase);
         foreach(var change in group){
           var actual=values[change.Path].Single().Points[change.Index].Scalar;
           if(Math.Abs(actual-change.After)>0.00002f)throw new InvalidDataException("Staged merge failed validation "+change.Path);
         }
         prepared.Add(Tuple.Create(group.Key,projected));
       }
       var committed=new List<Tuple<string,string>>();
       try{
         foreach(var item in prepared) {
           if(HashFile(item.Item1)!=plan.TargetHashes[item.Item1])throw new IOException("ENV modified during staging.");
           var save=Path.Combine(backupRoot,Path.GetFileName(item.Item1)+"_"+Guid.NewGuid().ToString("N")+".bak");
           File.Copy(item.Item1,save,false);
           File.Copy(item.Item2,item.Item1,true);
           committed.Add(Tuple.Create(item.Item1,save));
         }
       }catch{
         foreach(var item in committed)File.Copy(item.Item2,item.Item1,true);
         throw;
       }
     }finally{
       foreach(var temp in stagedTemps)try{if(File.Exists(temp))File.Delete(temp);}catch{}
     }
   }
 }
 public static partial class EnvCanonicalOrder {
   static readonly string[] Top={"m_finalColorBalance","m_sharpen","m_ssaoNV","m_globalLight","m_speedTree","m_toneMapping","m_bloomNew","m_bloomRemasterParams","m_ibl","m_rtx","m_pt","m_globalFog","m_sky","m_depthOfField","m_colorModTransparency","m_shadows","m_water","m_colorGroups","m_flareColorGroups","m_sunAndMoonParams","m_windParams","m_gameplayEffects","m_motionBlur","m_cameraLightsSetup","m_dialogLightParams"};
   public static string ReferenceSetting {get{return Path.Combine(EnvironmentPaths.AppDataRoot,"env_vanilla_reference_folder.txt");}}
   public static string ReferenceFolder {get{
      try{return File.Exists(ReferenceSetting)?File.ReadAllText(ReferenceSetting).Trim():"";}catch{return "";}
   }}
   public static void SetReferenceFolder(string folder) {
     if(!Directory.Exists(folder))throw new DirectoryNotFoundException("Vanilla ENV reference folder does not exist.");
     File.WriteAllText(ReferenceSetting,Path.GetFullPath(folder),Encoding.UTF8);
   }
   static string Corresponding(string file){
     var root=ReferenceFolder;
     if(String.IsNullOrWhiteSpace(root)||!Directory.Exists(root))return null;
     var path=Path.GetFullPath(file).Replace('/','\\');
     int i=path.LastIndexOf("\\files\\",StringComparison.OrdinalIgnoreCase);
     string rel;
     if(i>=0)rel=path.Substring(i+"\\files\\".Length);
     else {
       i=path.IndexOf("\\environment\\",StringComparison.OrdinalIgnoreCase);
       if(i>=0)rel=path.Substring(i+1);else return null;
     }
     var candidate=Path.Combine(root,rel);
     return File.Exists(candidate)?candidate:null;
   }
   static List<IEditableVariable> Kids(IEditableVariable v){
     try{return v?.GetEditableVariables()??new List<IEditableVariable>();}catch{return new List<IEditableVariable>();}
   }
   static CVector Env(CR2WFile f){
     var c=f.chunks.FirstOrDefault(x=>String.Equals(x.Type,"CEnvironmentDefinition",StringComparison.OrdinalIgnoreCase));
     var root=c?.data as CVector;
     var e=root?.variables.OfType<CVector>().SingleOrDefault(x=>x.Name=="envParams");
     if(e==null)throw new InvalidDataException("ENV has no unambiguous envParams.");
     return e;
   }
   static CR2WFile Read(string path){
     var f=new CR2WFile();f.FileName=path;
     using(var s=File.OpenRead(path))using(var r=new BinaryReader(s))f.Read(r);
     return f;
   }
   static string K(CVariable v){return (v.Name??"")+"\u001f"+(v.Type??"");}
   static string K(IEditableVariable v){return (v.Name??"")+"\u001f"+(v.Type??"");}
   static bool Reorder(CVector node,CVector reference,bool isTop){
     var original=node.variables.ToList();
     Dictionary<string,int> order=new Dictionary<string,int>(StringComparer.Ordinal);
     if(isTop){
       for(int i=0;i<Top.Length;i++)order[Top[i]]=i;
     }else if(reference!=null){
       foreach(var c in reference.variables)if(!order.ContainsKey(K(c)))order[K(c)]=order.Count;
     }else return false;
     int rank(CVariable child){
       int n;
       if(isTop)return order.TryGetValue(child.Name??"",out n)?n:Top.Length;
       return order.TryGetValue(K(child),out n)?n:order.Count;
     }
     var sorted=original.Select((x,i)=>new {x,i}).OrderBy(x=>rank(x.x)).ThenBy(x=>x.i).Select(x=>x.x).ToList();
     if(original.SequenceEqual(sorted))return false;
     node.variables.Clear();node.variables.AddRange(sorted);
     return true;
   }
   static IEditableVariable Match(IEditableVariable v,List<IEditableVariable> all,int occurrence){
     return all.Where(x=>K(x)==K(v)).Skip(occurrence).FirstOrDefault();
   }
   static void Walk(IEditableVariable current,IEditableVariable template,bool top,ref int ordered){
     var cur=current as CVector;var rf=template as CVector;
     if(cur!=null){
       if(Reorder(cur,rf,top))ordered++;
       var refs=rf==null?new List<IEditableVariable>():rf.variables.Cast<IEditableVariable>().ToList();
       var occur=new Dictionary<string,int>(StringComparer.Ordinal);
       foreach(var x in cur.variables){
         var key=K(x);int index=0;occur.TryGetValue(key,out index);occur[key]=index+1;
         var counterpart=Match(x,refs,index);
         if(counterpart!=null)Walk(x,counterpart,false,ref ordered);
       }
       return;
     }
     var a=current as CArray;var b=template as CArray;
     if(a!=null&&b!=null){
       for(int i=0;i<a.array.Count;i++){
         var t=b.array.Count==0?null:b.array[Math.Min(i,b.array.Count-1)];
         if(t!=null)Walk(a.array[i],t,false,ref ordered);
       }
     }
   }
   public static int Organize(CR2WFile file,string sourceFile,bool forceBuiltIn=false){
     var env=Env(file);
     CVector reference=null;
     var path=forceBuiltIn?null:Corresponding(sourceFile);
     if(path!=null){try{reference=Env(Read(path));}catch{}}
     int ordered=0;
     if(reference!=null)Walk(env,reference,true,ref ordered);
     else WalkEmbedded(env,RelativeEnv(sourceFile),"envParams",true,ref ordered);
     return ordered;
   }
   public static int OrganizeFile(string file,bool forceBuiltIn=false){
     var data=Read(file);
     int changed=Organize(data,file,forceBuiltIn);
     if(changed==0)return 0;
     var backup=file+".w3forge_order_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".bak";
     var temp=file+".w3forge_order_"+Guid.NewGuid().ToString("N");
     File.Copy(file,backup,false);
     try{
       using(var stream=File.Create(temp))using(var bw=new BinaryWriter(stream))data.Write(bw);
       var check=Read(temp);Env(check);
       File.Copy(temp,file,true);
       return changed;
     }catch{File.Copy(backup,file,true);throw;}
     finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}
   }
 }
}