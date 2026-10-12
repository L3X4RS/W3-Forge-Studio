using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using W3Edit;

namespace W3Forge {
 public sealed class DocumentSession {
   public string FilePath;
   public TabItem Tab;
   public Action Save;
   public Action Close;
   public bool Dirty;
 }
 public static class Editors {
   static readonly HashSet<string> TextExtensions=new HashSet<string>(StringComparer.OrdinalIgnoreCase){
     ".xml",".json",".csv",".txt",".ini",".ws",".yaml",".yml",".md",".bat",".cfg",".cpp",".c",".h",".hpp",".cs",".lua",".py",".obj",".mtl"
   };
   public static DocumentSession Create(string file,Action<string> log){
     var ext=Path.GetExtension(file);
     if(String.Equals(ext,".csv",StringComparison.OrdinalIgnoreCase))return StructuredEditors.Csv(file,log);
     if(String.Equals(ext,".xml",StringComparison.OrdinalIgnoreCase))return StructuredEditors.Xml(file,log);
     if(TextExtensions.Contains(ext))return StructuredEditors.Script(file,log);
     return NativeResourceEditor(file,log);
   }
   static DocumentSession TextEditor(string file,Action<string> log){
     var doc=new DocumentSession{FilePath=file};
     TextResourceFormat textFormat;
     string originalText=TextResourceIO.ReadAllText(file,out textFormat);
     bool isXml=Path.GetExtension(file).Equals(".xml",StringComparison.OrdinalIgnoreCase);
     var area=new Grid();Ui.Rows(area,Ui.Px(48),Ui.Star());
     var top=Ui.H();top.Margin=new Thickness(14,0,14,0);
     var title=Ui.Text(Path.GetFileName(file),13,"#F3F4F6");title.FontWeight=FontWeights.SemiBold;
     top.Children.Add(title);Ui.Space(top,18);
     top.Children.Add(Ui.Text("TEXT / SCRIPT EDITOR",10,"#969FAA"));
     var save=Ui.Button("SAVE",()=>doc.Save?.Invoke(),"PrimaryButton");
     save.Margin=new Thickness(20,0,0,0);top.Children.Add(save);
     Ui.Put(area,top,0);
     var edit=new TextBox{
       Text=originalText,AcceptsReturn=true,AcceptsTab=true,
       FontFamily=new System.Windows.Media.FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=13,
       VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
       TextWrapping=TextWrapping.NoWrap,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,
       Margin=new Thickness(9,0,9,9),Padding=new Thickness(15)
     };
     edit.TextChanged+=(s,e)=>doc.Dirty=true;
     Ui.Put(area,edit,1);
     doc.Save=()=>{
       var backup=file+".bak";
       if(!File.Exists(backup))File.Copy(file,backup);
       TextResourceIO.WriteAllText(file,edit.Text,textFormat,isXml);
       doc.Dirty=false;
       log?.Invoke("Saved text resource: "+file+" ["+TextResourceIO.Describe(textFormat)+"]");
     };
     doc.Close=()=>{};
     doc.Tab=new TabItem{Header=Path.GetFileName(file),Content=area,ToolTip=file};
     return doc;
   }
   static DocumentSession NativeResourceEditor(string file,Action<string> log){
     var doc=new DocumentSession{FilePath=file};
     var area=new Grid();
     Ui.Rows(area,Ui.Px(49),Ui.Star());

     var header=Ui.Box("#1A1D21");
     var headerGrid=new Grid{Margin=new Thickness(14,0,12,0)};
     Ui.Cols(headerGrid,Ui.Star(),Ui.Px(150));
     var left=Ui.H();
     var title=Ui.Text(Path.GetFileName(file),13,"#F3F4F6");title.FontWeight=FontWeights.SemiBold;
     left.Children.Add(title);Ui.Space(left,15);
     left.Children.Add(Ui.Text("NATIVE RED3 RESOURCE EDITOR",10,"#919AA6"));
     Ui.Put(headerGrid,left,0);
     header.Child=headerGrid;
     Ui.Put(area,header,0);

     var hostBorder=Ui.Box("#E8E9EB","#343941");
     hostBorder.Margin=new Thickness(8,0,8,8);
     var host=new WindowsFormsHost();
     host.HorizontalAlignment=HorizontalAlignment.Stretch;
     host.VerticalAlignment=VerticalAlignment.Stretch;

     var editor=new frmCR2WDocument();
     DisableLegacyLayoutWrite(editor,log);
     editor.TopLevel=false;
     editor.FormBorderStyle=System.Windows.Forms.FormBorderStyle.None;
     editor.Dock=System.Windows.Forms.DockStyle.Fill;
     editor.LoadFile(file);
     editor.Show();
     NativeEditorTheme.Apply(editor);
     int themePass=0;
     var themeTimer=new System.Windows.Forms.Timer{Interval=250};
     themeTimer.Tick+=(sender,args)=>{
       try{NativeEditorTheme.Apply(editor);}catch{}
       themePass++;
       if(themePass>=5){themeTimer.Stop();themeTimer.Dispose();}
     };
     themeTimer.Start();

     var save=Ui.Button("SAVE RESOURCE",()=>{
       try{
         if(File.Exists(file) && !File.Exists(file+".bak"))File.Copy(file,file+".bak");
         editor.SaveFile();
         if(Path.GetExtension(file).Equals(".env",StringComparison.OrdinalIgnoreCase)){
           try{int count=EnvCanonicalOrder.OrganizeFile(file);if(count>0)log?.Invoke("ENV canonical order on save: "+count+" vectors (original values preserved).");}
           catch(Exception ex){log?.Invoke("ENV order skipped: "+ex.Message);}
         }
         // Reopen the just-saved resource to catch invalid writes immediately.
         var verifier=new W3Edit.CR2W.CR2WFile();verifier.FileName=file;
         using(var fs=File.OpenRead(file))using(var br=new BinaryReader(fs))verifier.Read(br);
         doc.Dirty=false;
         log?.Invoke("Saved and validated native resource: "+file);
       }catch(Exception ex){MessageBox.Show(ex.Message,"Save resource",MessageBoxButton.OK,MessageBoxImage.Error);throw;}
     },"PrimaryButton");
     save.Margin=new Thickness(0,7,0,7);
     Ui.Put(headerGrid,save,0,1);

     host.Child=editor;
     hostBorder.Child=host;
     Ui.Put(area,hostBorder,1);

     doc.Save=()=>{
       if(File.Exists(file) && !File.Exists(file+".bak"))File.Copy(file,file+".bak");
       editor.SaveFile();
       if(Path.GetExtension(file).Equals(".env",StringComparison.OrdinalIgnoreCase)){
         try{int count=EnvCanonicalOrder.OrganizeFile(file);if(count>0)log?.Invoke("ENV canonical order on save: "+count+" vectors.");}
         catch(Exception ex){log?.Invoke("ENV order skipped: "+ex.Message);}
       }
       doc.Dirty=false;
       log?.Invoke("Saved native resource: "+file);
     };
     doc.Close=()=>{try{editor.Close();editor.Dispose();host.Dispose();}catch{}};
     doc.Tab=new TabItem{Header=Path.GetFileName(file),Content=area,ToolTip=file};
     log?.Invoke("Opened CR2W in the native W3Edit editor core: "+file);
     return doc;
   }

   static void DisableLegacyLayoutWrite(frmCR2WDocument editor,Action<string> log){
     // The legacy W3Edit control persists cr2wdocument_layout.xml beside the
     // host executable when FormClosed fires. That is not writable when W3 Forge
     // Studio is installed under Program Files, producing UnauthorizedAccessException.
     // W3 Forge ships a stable embedded layout, so disable only that legacy close
     // persistence handler when W3Edit is hosted inside our process.
     try{
       var mi=editor.GetType().GetMethod("frmCR2WDocument_FormClosed",BindingFlags.Instance|BindingFlags.NonPublic);
       if(mi==null)return;
       var handler=(System.Windows.Forms.FormClosedEventHandler)Delegate.CreateDelegate(typeof(System.Windows.Forms.FormClosedEventHandler),editor,mi,false);
       if(handler!=null){
         editor.FormClosed-=handler;
       }
       // W3Edit.Configuration also has a legacy finalizer that writes
       // <host>_config.xml beside the executable. W3 Forge does not use that
       // legacy configuration, so suppress only its finalizer in hosted mode.
       var legacyConfig=MainController.Get().Configuration;
       if(legacyConfig!=null)GC.SuppressFinalize(legacyConfig);
       log?.Invoke("Embedded W3Edit layout/config persistence disabled: protected installs remain read-only.");
     }catch(Exception ex){
       Program.LogException(ex);
     }
   }
 }
}