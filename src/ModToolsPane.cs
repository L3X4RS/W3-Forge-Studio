using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace W3Forge {
  public sealed class ModToolsPane : Grid {
    readonly Func<Project> getProject;
    readonly Action refreshProject;
    readonly Action<Project> openProject;
    readonly Action<string> log;
    readonly Action<string> status;

    TextBox pathBox,searchBox;
    TextBlock state;
    ListView list;
    Button scan,extractSelected,extractAll,importProject,unpackProject;
    CompiledModPackage package;
    List<CompiledModAsset> visible=new List<CompiledModAsset>();

    public ModToolsPane(Func<Project> getProject,Action refreshProject,Action<Project> openProject,Action<string> log,Action<string> status) {
      this.getProject=getProject;
      this.refreshProject=refreshProject;
      this.openProject=openProject;
      this.log=log;
      this.status=status;
      Build();
    }

    void Build() {
      Background=Ui.Brush("#191C20");
      Ui.Rows(this,Ui.Px(62),Ui.Px(50),Ui.Star(),Ui.Px(60));

      var source=new Grid{Margin=new Thickness(15,10,15,8)};
      Ui.Cols(source,Ui.Px(115),Ui.Star(),Ui.Px(105),Ui.Px(100));
      var label=Ui.Text("MOD / PACKAGE",10,"#9EA7B3");label.FontWeight=FontWeights.Bold;Ui.Put(source,label,0);
      pathBox=new TextBox{Margin=new Thickness(4,0,10,0),FontFamily=new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=11.5};
      pathBox.ToolTip="Choose an installed or extracted Witcher 3 mod folder. You can also select its content folder.";
      Ui.Put(source,pathBox,0,1);
      Ui.Put(source,Ui.Button("Browse",Browse),0,2);
      scan=Ui.Button("SCAN MOD",async()=>await ScanAsync(),"PrimaryButton");Ui.Put(source,scan,0,3);
      Ui.Put(this,source,0);

      var find=new Grid{Margin=new Thickness(15,0,15,8)};
      Ui.Cols(find,Ui.Star(),Ui.Px(280));
      searchBox=new TextBox{FontSize=12.5,Margin=new Thickness(0,0,12,0)};
      searchBox.ToolTip="Filter the indexed mod by depot path or file name.";
      searchBox.TextChanged+=(s,e)=>RefreshList();
      Ui.Put(find,searchBox,0,0);
      state=Ui.Text("Select a compiled mod folder and click Scan Mod",11,"#929BA7");
      state.HorizontalAlignment=HorizontalAlignment.Right;Ui.Put(find,state,0,1);
      Ui.Put(this,find,1);

      list=new ListView{SelectionMode=SelectionMode.Extended,Margin=new Thickness(15,0,15,8)};
      var view=new GridView{AllowsColumnReorder=true};
      view.Columns.Add(new GridViewColumn{Header="DEPOT RESOURCE PATH",DisplayMemberBinding=new Binding("DepotPath"),Width=575});
      view.Columns.Add(new GridViewColumn{Header="TYPE",DisplayMemberBinding=new Binding("Extension"),Width=80});
      view.Columns.Add(new GridViewColumn{Header="SIZE",DisplayMemberBinding=new Binding("SizeText"),Width=95});
      view.Columns.Add(new GridViewColumn{Header="BUNDLE",DisplayMemberBinding=new Binding("BundleName"),Width=145});
      view.Columns.Add(new GridViewColumn{Header="COMPRESSION",DisplayMemberBinding=new Binding("Compression"),Width=105});
      list.View=view;
      list.SelectionChanged+=(s,e)=>UpdateActions();
      Ui.Put(this,list,2);

      var footer=Ui.Box("#20242A");
      var actions=Ui.H();actions.Margin=new Thickness(13,0,13,0);
      extractSelected=Ui.Button("EXTRACT SELECTED",ExtractSelected,"ToolbarButton");
      extractAll=Ui.Button("EXTRACT ALL",ExtractAll,"ToolbarButton");
      unpackProject=Ui.Button("UNPACK AS PROJECT",UnpackAsProject,"ToolbarButton");
      importProject=Ui.Button("IMPORT TO PROJECT",ImportToProject,"PrimaryButton");
      extractSelected.IsEnabled=extractAll.IsEnabled=unpackProject.IsEnabled=importProject.IsEnabled=false;
      actions.Children.Add(extractSelected);
      actions.Children.Add(extractAll);
      actions.Children.Add(unpackProject);
      actions.Children.Add(Ui.Separator(true));
      actions.Children.Add(importProject);
      actions.Children.Add(Ui.Text("Browse, unbundle and convert compiled mods into editable W3 Forge projects",10,"#7F8996"));
      footer.Child=actions;Ui.Put(this,footer,3);
    }

    void Browse() {
      using(var dlg=new System.Windows.Forms.FolderBrowserDialog()) {
        dlg.Description="Select an installed or extracted Witcher 3 mod folder";
        if(Directory.Exists(pathBox.Text))dlg.SelectedPath=pathBox.Text;
        if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK)pathBox.Text=dlg.SelectedPath;
      }
    }

    async Task ScanAsync() {
      if(String.IsNullOrWhiteSpace(pathBox.Text))return;
      scan.IsEnabled=false;
      state.Text="Scanning...";
      status?.Invoke("Indexing compiled mod...");
      try {
        var p=pathBox.Text.Trim();
        package=await Task.Run(()=>CompiledModPackage.Open(p,log));
        RefreshList();
        state.Text=package.Assets.Count.ToString("N0")+" bundled resources";
        status?.Invoke("MOD READY  •  "+package.Assets.Count.ToString("N0")+" bundled resources indexed");
        log?.Invoke("Opened compiled mod package: "+package.RootPath);
      } catch(Exception ex) {
        Program.LogException(ex);
        package=null;visible.Clear();list.ItemsSource=null;
        state.Text="Scan failed";
        MessageBox.Show(ex.Message,"Open compiled mod",MessageBoxButton.OK,MessageBoxImage.Error);
        status?.Invoke("Compiled mod scan failed");
      } finally {
        scan.IsEnabled=true;UpdateActions();
      }
    }

    void RefreshList() {
      if(package==null){visible.Clear();list.ItemsSource=null;UpdateActions();return;}
      var q=(searchBox.Text??"").Trim();
      visible=(String.IsNullOrWhiteSpace(q)
        ?package.Assets
        :package.Assets.Where(x=>x.DepotPath.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToList())
        .Take(10000).ToList();
      list.ItemsSource=visible;
      state.Text=(visible.Count==package.Assets.Count
        ?package.Assets.Count.ToString("N0")+" bundled resources"
        :visible.Count.ToString("N0")+" / "+package.Assets.Count.ToString("N0")+" resources");
      UpdateActions();
    }

    void UpdateActions() {
      bool ready=package!=null;
      extractAll.IsEnabled=ready;
      extractSelected.IsEnabled=ready&&list.SelectedItems.Count>0;
      unpackProject.IsEnabled=ready&&package.Assets.Count>0;
      importProject.IsEnabled=ready&&getProject()!=null;
      importProject.ToolTip=getProject()==null?"Open or create a project first.":"Extract the compiled mod resources into the current editable project.";
    }

    void ExtractSelected() {
      if(package==null||list.SelectedItems.Count==0)return;
      using(var dlg=new System.Windows.Forms.FolderBrowserDialog()) {
        dlg.Description="Select destination for the selected compiled resources";
        if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        try {
          package.ExtractAssets(list.SelectedItems.Cast<CompiledModAsset>().ToArray(),dlg.SelectedPath,log);
          status?.Invoke("Selected mod resources extracted.");
        } catch(Exception ex) {
          Program.LogException(ex);MessageBox.Show(ex.Message,"Extract resources",MessageBoxButton.OK,MessageBoxImage.Error);
        }
      }
    }

    void ExtractAll() {
      if(package==null)return;
      using(var dlg=new System.Windows.Forms.FolderBrowserDialog()) {
        dlg.Description="Select destination for the fully unpacked mod";
        if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        var dest=Path.Combine(dlg.SelectedPath,SafeName(package.DisplayName)+"_UNPACKED");
        try {
          package.ExtractComplete(dest,log);
          status?.Invoke("Compiled mod unpacked to "+dest);
          MessageBox.Show("Mod unpacked successfully:\n"+dest,"W3 Forge Studio",MessageBoxButton.OK,MessageBoxImage.Information);
        } catch(Exception ex) {
          Program.LogException(ex);MessageBox.Show(ex.Message,"Unpack mod",MessageBoxButton.OK,MessageBoxImage.Error);
        }
      }
    }

    void UnpackAsProject() {
      if(package==null||package.Assets.Count==0)return;
      using(var dlg=new System.Windows.Forms.FolderBrowserDialog()) {
        dlg.Description="Choose where W3 Forge Studio should create the editable project";
        if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;

        var prompt=new TextPrompt("Unpack compiled mod","PROJECT NAME",SafeName(package.DisplayName));
        prompt.Owner=Window.GetWindow(this);
        if(prompt.ShowDialog()!=true)return;

        try {
          string gameBin=getProject()!=null?getProject().GameBin:null;
          if(String.IsNullOrWhiteSpace(gameBin)) {
            var main=Window.GetWindow(this) as MainWindow;
          }
          var result=ModUnpacker.Import(package.RootPath,dlg.SelectedPath,prompt.Value,
            String.IsNullOrWhiteSpace(gameBin)?"":gameBin,log);
          openProject?.Invoke(result.Project);
          status?.Invoke("UNPACKED PROJECT READY  •  "+result.ResourceCount+" resources");
          MessageBox.Show(
            "Compiled mod converted into an editable W3 Forge project.\n\n"+
            "Bundles: "+result.BundleCount+"\n"+
            "Resources: "+result.ResourceCount+"\n"+
            "External buffers: "+result.BufferCount+"\n"+
            "Loose files: "+result.LooseFileCount+"\n"+
            "texture.cache: "+(result.ImportedTextureCache?"yes":"no"),
            "Unpack as project",MessageBoxButton.OK,MessageBoxImage.Information);
        } catch(Exception ex) {
          Program.LogException(ex);
          MessageBox.Show(ex.Message,"Unpack as project",MessageBoxButton.OK,MessageBoxImage.Error);
        }
      }
    }

    void ImportToProject() {
      var project=getProject();
      if(package==null||project==null)return;
      try {
        var result=package.ImportToProject(project,false,log);
        refreshProject?.Invoke();
        status?.Invoke("Imported "+result.CookedImported+" cooked resources into "+project.Name);
        MessageBox.Show(
          "Compiled mod imported into the current project.\n\n"+
          "Cooked resources: "+result.CookedImported+"\n"+
          "Loose files: "+result.LooseImported+"\n"+
          "texture.cache: "+(result.TextureCacheCopied?"yes":"no")+"\n"+
          "Skipped existing files: "+result.Skipped,
          "Import compiled mod",MessageBoxButton.OK,MessageBoxImage.Information);
      } catch(Exception ex) {
        Program.LogException(ex);MessageBox.Show(ex.Message,"Import compiled mod",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }

    static string SafeName(string s) {
      if(String.IsNullOrWhiteSpace(s))return "MOD";
      return new string(s.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
    }

    public void NotifyProjectChanged(){UpdateActions();}
  }
}