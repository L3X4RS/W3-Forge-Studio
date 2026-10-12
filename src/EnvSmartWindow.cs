using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace W3Forge {
 public sealed class EnvSmartWindow : Window {
   readonly Project project;
   readonly string currentFile;
   readonly Action<string> log;
   readonly Action onChanged;
   readonly Dictionary<string,CheckBox> groupBoxes=new Dictionary<string,CheckBox>(StringComparer.OrdinalIgnoreCase);
   TextBox sourceFile,sourceRoot,referenceRoot,details;
   CheckBox batch,special;
   TextBlock summary;
   Button apply;
   EnvSmartPlan preview;
   public EnvSmartWindow(string file,Project project,Action<string> log,Action onChanged) {
     this.project=project;currentFile=Path.GetFullPath(file);this.log=log;this.onChanged=onChanged;
     Title="W3 Forge • ENV Smart Transfer & Canonical Order";
     Width=1020;Height=790;MinWidth=810;MinHeight=650;
     WindowStartupLocation=WindowStartupLocation.CenterOwner;
     Background=Ui.Brush("#171A1E");
     Foreground=Ui.Brush("#E3E8EE");
     Content=Build();
   }
   static TextBlock Label(string s){return new TextBlock{Text=s,Foreground=Ui.Brush("#AEB7C2"),Margin=new Thickness(4,7,6,6),FontSize=12,TextWrapping=TextWrapping.Wrap};}
   static TextBox ReadOnlyBox(string value){return new TextBox{
       Text=value,IsReadOnly=true,Foreground=Ui.Brush("#D9E2EC"),Background=Ui.Brush("#20262D"),
       BorderBrush=Ui.Brush("#39434E"),Margin=new Thickness(3,4,5,4),Padding=new Thickness(7),FontSize=11
   };}
   static Button ButtonFor(string name,Action action){var b=Ui.Button(name,action,"ToolbarButton");b.Margin=new Thickness(3,3,6,3);return b;}
   UIElement Build(){
     var page=new Grid{Margin=new Thickness(18)};
     page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
     page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
     page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
     page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
     page.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
     page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
     var intro=new StackPanel();
     intro.Children.Add(new TextBlock{Text="ENV SMART WORKFLOW",FontWeight=FontWeights.Bold,FontSize=20,Foreground=Ui.Brush("#F5F7F9")});
     intro.Children.Add(Label("Target: "+currentFile));
     intro.Children.Add(Label("Transfer numerical curve adjustments only. Never replace ENV blocks, LUTs, timestamps, colors, AgX, exposure, fog, or m_colorGroups. Preview is required before writing."));
     Grid.SetRow(intro,0);page.Children.Add(intro);

     var source=new StackPanel();
     var fileRow=new Grid();fileRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
     fileRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
     source.Children.Add(Label("SOURCE ENV (single file)"));
     sourceFile=ReadOnlyBox("");
     fileRow.Children.Add(sourceFile);
     var browseFile=ButtonFor("CHOOSE .ENV",()=>{
       var dlg=new OpenFileDialog{Title="Select source ENV (for example, STLM)",Filter="Witcher 3 Environment|*.env"};
       if(dlg.ShowDialog()==true){sourceFile.Text=dlg.FileName;InvalidatePreview();}
     });
     Grid.SetColumn(browseFile,1);fileRow.Children.Add(browseFile);source.Children.Add(fileRow);
     source.Children.Add(Label("SOURCE FILES FOLDER (batch: same relative ENV paths as current project)"));
     var dirRow=new Grid();dirRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});dirRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
     sourceRoot=ReadOnlyBox("");
     dirRow.Children.Add(sourceRoot);
     var folder=ButtonFor("CHOOSE FOLDER",()=>{
       using(var dlg=new System.Windows.Forms.FolderBrowserDialog{Description="Choose source mod 'files' folder",ShowNewFolderButton=false}){
         if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){sourceRoot.Text=dlg.SelectedPath;InvalidatePreview();}
       }
     });
     Grid.SetColumn(folder,1);dirRow.Children.Add(folder);source.Children.Add(dirRow);
     var options=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(3,7,3,0)};
     batch=new CheckBox{Content="Batch: all matching ENV files in project",Margin=new Thickness(2,4,25,5),Foreground=Ui.Brush("#F1F4F6")};
     special=new CheckBox{Content="Also include interiors / cutscenes / mission environments (advanced)",Margin=new Thickness(2,4,2,5),Foreground=Ui.Brush("#F1F4F6"),IsChecked=false};
     batch.Checked+=(s,e)=>InvalidatePreview();batch.Unchecked+=(s,e)=>InvalidatePreview();
     special.Checked+=(s,e)=>InvalidatePreview();special.Unchecked+=(s,e)=>InvalidatePreview();
     options.Children.Add(batch);options.Children.Add(special);source.Children.Add(options);
     Grid.SetRow(source,1);page.Children.Add(source);

     var controls=new StackPanel{Margin=new Thickness(0,8,0,4)};
     controls.Children.Add(Label("SELECT GROUPS FOR NUMBER-ONLY TRANSFER (default excludes grading and tonemapping)"));
     var groups=new WrapPanel();
     foreach(var group in EnvSmartWorkflow.Groups){
       var chk=new CheckBox{Content=group,Margin=new Thickness(7,5,18,6),Foreground=Ui.Brush("#CED9E3"),
         IsChecked=group=="m_globalLight"||group=="m_speedTree"};
       if(group=="m_finalColorBalance"){chk.IsChecked=false;chk.IsEnabled=false;chk.ToolTip="Protected. Adjust LD4 color grading explicitly in ENV Studio; no automatic color transfer.";}
       chk.Checked+=(s,e)=>InvalidatePreview();chk.Unchecked+=(s,e)=>InvalidatePreview();
       groupBoxes[group]=chk;groups.Children.Add(chk);
     }
     controls.Children.Add(groups);
     controls.Children.Add(Label("Safety: 10% blend toward reference at LD4 original curve times; maximum delta 4% relative or 0.025 absolute. No new curve nodes."));
     var buttons=new WrapPanel();
     buttons.Children.Add(ButtonFor("PREVIEW DIFFERENCES",Preview));
     apply=ButtonFor("APPLY PREVIEW + BACKUP",Apply);apply.IsEnabled=false;buttons.Children.Add(apply);
     buttons.Children.Add(ButtonFor("ORGANIZE TARGET ENV NOW",OrganizeTarget));
     controls.Children.Add(buttons);
     Grid.SetRow(controls,2);page.Children.Add(controls);

     var order=new StackPanel{Margin=new Thickness(0,6,0,10)};
     order.Children.Add(Label("CANONICAL INTERNAL ORDER  •  Built into Forge: no extracted Vanilla required. An optional Vanilla folder can override the built-in structural order. Neither mode copies parameter values or references."));
     var refRow=new Grid();refRow.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});refRow.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
     referenceRoot=ReadOnlyBox(String.IsNullOrWhiteSpace(EnvCanonicalOrder.ReferenceFolder)?"BUILT-IN VANILLA ORDER ACTIVE (no extracted files required)":EnvCanonicalOrder.ReferenceFolder);refRow.Children.Add(referenceRoot);
     var choose=ButtonFor("OPTIONAL VANILLA OVERRIDE",()=>{
       using(var dlg=new System.Windows.Forms.FolderBrowserDialog{Description="Choose the root of extracted Vanilla ENV files (same structure as project/files)",ShowNewFolderButton=false}){
         if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){
           try{EnvCanonicalOrder.SetReferenceFolder(dlg.SelectedPath);referenceRoot.Text=EnvCanonicalOrder.ReferenceFolder;
             MessageBox.Show("Canonical order configured. Future ENV Studio and native ENV saves automatically preserve this schema order where a matching Vanilla ENV exists.","Forge",MessageBoxButton.OK,MessageBoxImage.Information);}
           catch(Exception ex){MessageBox.Show(ex.Message,"Vanilla reference",MessageBoxButton.OK,MessageBoxImage.Error);}
         }
       }
     });
     Grid.SetColumn(choose,1);refRow.Children.Add(choose);order.Children.Add(refRow);
     Grid.SetRow(order,3);page.Children.Add(order);

     details=new TextBox{Text="Press PREVIEW DIFFERENCES to compare your target ENV(s) with a source. No source files are modified.",IsReadOnly=true,
       TextWrapping=TextWrapping.NoWrap,AcceptsReturn=true,FontFamily=new FontFamily("Consolas"),FontSize=11,
       Background=Ui.Brush("#101318"),Foreground=Ui.Brush("#CFD5DE"),BorderBrush=Ui.Brush("#353C45"),
       Padding=new Thickness(12),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};
     Grid.SetRow(details,4);page.Children.Add(details);
     summary=Label("Nothing has been written.");
     Grid.SetRow(summary,5);page.Children.Add(summary);
     return page;
   }
   void InvalidatePreview(){preview=null;if(apply!=null)apply.IsEnabled=false;}
   void Preview(){
     try{
       var chosen=groupBoxes.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray();
       var pairs=new List<Tuple<string,string>>();
       if(batch.IsChecked==true){
         if(project==null)throw new InvalidOperationException("Open a Forge project to use batch merge.");
         var files=EnvSmartWorkflow.MatchingPairs(project.Files,sourceRoot.Text,special.IsChecked==true);
         foreach(var target in files){
           var rel=target.Substring(project.Files.TrimEnd('\\','/').Length).TrimStart('\\','/');
           pairs.Add(Tuple.Create(target,Path.Combine(sourceRoot.Text,rel)));
         }
       }else{
         if(!File.Exists(sourceFile.Text))throw new FileNotFoundException("Select a source ENV first.");
         pairs.Add(Tuple.Create(currentFile,sourceFile.Text));
       }
       if(pairs.Count==0)throw new InvalidOperationException("No matching ENV file paths found.");
       preview=EnvSmartWorkflow.Preview(pairs,chosen,0.10,0.04,0.025);
       var lines=new List<string>();
       lines.Add("MATCHED FILES: "+preview.Examined+"  MISSING: "+preview.Missing+"  SKIPPED: "+preview.Skipped+"  CHANGES: "+preview.Changes.Count);
       lines.Add("IMPORTANT: no values are copied directly. Only safe scalar curve points are blended.");
       lines.Add("");
       foreach(var file in preview.Changes.GroupBy(x=>x.TargetFile,StringComparer.OrdinalIgnoreCase)){
         lines.Add("FILE: "+file.Key);
         foreach(var row in file)lines.Add("  "+row.ToString());
       }
       if(preview.Changes.Count>350)lines.Add("Full preview: "+preview.Changes.Count+" changes; scroll to inspect each.");
       details.Text=String.Join(Environment.NewLine,lines);
       apply.IsEnabled=preview.Changes.Count>0;
       summary.Text="Preview ready: "+preview.Changes.Count+" scalar values in "+preview.Changes.Select(x=>x.TargetFile).Distinct(StringComparer.OrdinalIgnoreCase).Count()+" ENV files.";
     }catch(Exception ex){preview=null;apply.IsEnabled=false;MessageBox.Show(ex.Message,"Smart ENV preview",MessageBoxButton.OK,MessageBoxImage.Warning);}
   }
   void Apply(){
     if(preview==null||preview.Changes.Count==0)return;
     var count=preview.Changes.Select(x=>x.TargetFile).Distinct(StringComparer.OrdinalIgnoreCase).Count();
     if(MessageBox.Show("Apply "+preview.Changes.Count+" scalar value adjustments across "+count+" ENV files?\n\nA preview and timestamped backups are mandatory. LUTs, color groups, fog and curve times are protected.","Confirm ENV transfer",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
     try{
       string backup=project==null?Path.Combine(EnvironmentPaths.AppDataRoot,"Backups","SmartENV",DateTime.Now.ToString("yyyyMMdd_HHmmss")):Path.Combine(project.Workspace,"backups","smart_env",DateTime.Now.ToString("yyyyMMdd_HHmmss"));
       EnvSmartWorkflow.Apply(preview,backup);
       var done=preview.Changes.Count;preview=null;apply.IsEnabled=false;
       log?.Invoke("SMART ENV TRANSFER: "+done+" safe numeric curve points; backups: "+backup);
       onChanged?.Invoke();
       summary.Text="Applied "+done+" scalar points. Full backups: "+backup;
       MessageBox.Show("Smart ENV transfer completed and validated.\n\nBackups:\n"+backup,"W3 Forge",MessageBoxButton.OK,MessageBoxImage.Information);
     }catch(Exception ex){MessageBox.Show(ex.Message,"ENV transfer failed / rollback",MessageBoxButton.OK,MessageBoxImage.Error);}
   }
   void OrganizeTarget(){
     try{
       if(MessageBox.Show("Reorder existing ENV variables to the native canonical layout?\n\nThe Vanilla reference only controls their position. No values, LUTs, nodes or groups are replaced. A backup will be made if anything changes.","Canonical ENV order",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
       int count=EnvCanonicalOrder.OrganizeFile(currentFile);
       log?.Invoke("Canonical ENV order: "+currentFile+"; reordered vectors="+count);
       onChanged?.Invoke();
       MessageBox.Show(count==0?"Already in canonical order or no matching template for internal blocks.":"Organized "+count+" ENV vectors. Your values and LUTs were preserved. A timestamped backup was kept.", "ENV canonical order",MessageBoxButton.OK,MessageBoxImage.Information);
     }catch(Exception ex){MessageBox.Show(ex.Message,"ENV canonical order",MessageBoxButton.OK,MessageBoxImage.Error);}
   }
 }
}