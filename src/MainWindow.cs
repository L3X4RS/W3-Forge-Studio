using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace W3Forge {
 public sealed class MainWindow : Window {
  Preferences prefs;
  Project project;
  BundleIndex index;
  AssetFolder folder;
  GameAsset asset;
  List<GameAsset> visible=new List<GameAsset>();
  string extensionFilter="";
  bool loading,working;
  string pendingCut;
  string pendingCopy;

  Grid root;
  TreeView projectTree,folderTree;
  ListView assetList;
  TextBox search,gamePath,console;
  TextBlock hitCount,assetState,projectTitle,installState,info,footerStatus,searchPlaceholder,textureStatus;
  TabControl docs;
  Button addSelected,buildInstall,buildOnly,installOnly,scanButton;
  Border outputArea,propertyArea,texturePreviewHost;
  System.Windows.Controls.Image texturePreview;
  StackPanel textureActions;
  TexturePreviewData currentTexture;
  string currentTextureName;
  ModToolsPane modToolsPane;
  LiveEnvPane liveEnvPane;
  TabItem envStudioTab;
  string envStudioContextFile;
  TabItem envStudioSourceTab;
  bool handlingDocumentSelection;
  DispatcherTimer searchDelay;
  List<DocumentSession> sessions=new List<DocumentSession>();
  List<Button> filterButtons=new List<Button>();
  TabItem browserTab;
  string projectTreeFocusAfterRefresh;

  public MainWindow(){
    prefs=Preferences.Load();
    Title="W3 Forge Studio";
    Width=1600;Height=940;MinWidth=1280;MinHeight=760;
    WindowStartupLocation=WindowStartupLocation.CenterScreen;
    WindowState=WindowState.Maximized;
    WindowStyling.Apply(this);
    try {
      var iconPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"W3ForgeStudio.ico");
      if(File.Exists(iconPath)) Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath,UriKind.Absolute));
    } catch {}
    SetResourceReference(BackgroundProperty,"Bg");
    Content=BuildShell();
    searchDelay=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(270)};
    searchDelay.Tick+=(s,e)=>{searchDelay.Stop();RefreshAssets();};
    Loaded+=async(s,e)=>{
      if(!String.IsNullOrWhiteSpace(prefs.LastProject)&&File.Exists(prefs.LastProject)){
        try{OpenProject(prefs.LastProject);}catch(Exception ex){Log("Previous project: "+ex.Message);}
      }
      if(Directory.Exists(prefs.GameBin))await ScanGameAsync();
    };
    PreviewKeyDown+=MainKeys;
    Closing+=(s,e)=>{
      prefs.Save();
      if(sessions.Any(x=>x.Dirty)){
        var r=MessageBox.Show("Some files have unsaved edits. Exit anyway?","Unsaved changes",MessageBoxButton.YesNo,MessageBoxImage.Warning);
        if(r!=MessageBoxResult.Yes){e.Cancel=true;return;}
      }
      if(!e.Cancel){
        foreach(var session in sessions.ToArray())try{session.Close?.Invoke();}catch{}
      }
    };
  }

  UIElement BuildShell(){
    root=new Grid();
    Ui.Rows(root,Ui.Px(76),Ui.Px(61),Ui.Star(),Ui.Px(29));
    var brand=Ui.Box("#15171B");Ui.Put(root,brand,0);
    var bar=new Grid{Margin=new Thickness(23,0,24,0)};
    Ui.Cols(bar,Ui.Px(5),Ui.Px(14),Ui.Px(360),Ui.Star(),Ui.Px(180));
    Ui.Put(bar,Ui.Box("#D34249",radius:2),0,0);
    var brandGroup=Ui.H();
    brandGroup.VerticalAlignment=VerticalAlignment.Center;
    var titleGroup=Ui.V();titleGroup.VerticalAlignment=VerticalAlignment.Center;
    var word=Ui.Text("W3 FORGE STUDIO",20,"#F6F7F9");word.FontWeight=FontWeights.SemiBold;
    titleGroup.Children.Add(word);
    var sub=Ui.Text("REDENGINE 3  /  THE WITCHER 3 REMASTERED",9.5,"#7F8996");sub.Margin=new Thickness(1,4,0,0);
    titleGroup.Children.Add(sub);
    brandGroup.Children.Add(titleGroup);
    Ui.Put(bar,brandGroup,0,2);
    projectTitle=Ui.Text("NO PROJECT OPEN",11,"#A7AEB7");
    projectTitle.HorizontalAlignment=HorizontalAlignment.Right;
    Ui.Put(bar,projectTitle,0,3);
    installState=Ui.Text("NOT INSTALLED",10,"#C0A2A3");
    installState.FontWeight=FontWeights.Bold;
    installState.HorizontalAlignment=HorizontalAlignment.Right;
    Ui.Put(bar,installState,0,4);
    brand.Child=bar;

    var command=Ui.Box("#1C1F24","#30353C");
    var commands=new DockPanel{LastChildFill=false,Margin=new Thickness(20,8,12,8)};
    var left=Ui.H();
    left.Children.Add(Ui.Button("New project",CreateProject,"FlatButton"));
    left.Children.Add(Ui.Button("Open project",ChooseProject,"FlatButton"));
    left.Children.Add(Ui.Button("Open file",ChooseFileToOpen,"FlatButton"));
    left.Children.Add(Ui.Button("New script",CreateWitcherScript,"FlatButton"));
    left.Children.Add(Ui.Button("JSON ↔ RAW",ChooseRoundtripFile,"ToolbarButton"));
    left.Children.Add(Ui.Button("Unpack mod",UnpackMod,"FlatButton"));
    left.Children.Add(Ui.Separator(true));
    left.Children.Add(Ui.Button("Save",SaveCurrent,"FlatButton"));
    left.Children.Add(Ui.Button("Save all",SaveAll,"FlatButton"));
    left.Children.Add(Ui.Separator(true));
    scanButton=Ui.Button("Scan game",async()=>await ScanGameAsync(),"ToolbarButton");
    left.Children.Add(scanButton);
    left.Children.Add(Ui.Separator(true));
    buildOnly=Ui.Button("Build",async()=>await RunBuildAsync(false),"ToolbarButton");
    buildOnly.IsEnabled=false;
    buildOnly.ToolTip="Add at least one resource to the project before building.";
    left.Children.Add(buildOnly);

    installOnly=Ui.Button("INSTALL",async()=>await InstallBuiltAsync(),"ToolbarButton");
    installOnly.IsEnabled=false;
    installOnly.ToolTip="Build the project first, then install the generated mod.";
    left.Children.Add(installOnly);

    buildInstall=Ui.Button("BUILD & INSTALL",async()=>await RunBuildAsync(true),"PrimaryButton");
    buildInstall.Padding=new Thickness(17,9,17,9);
    buildInstall.IsEnabled=false;
    buildInstall.ToolTip="Add at least one resource to the project before building.";
    left.Children.Add(buildInstall);
    DockPanel.SetDock(left,Dock.Left);commands.Children.Add(left);
    var right=Ui.H();right.HorizontalAlignment=HorizontalAlignment.Right;
    right.Children.Add(Ui.Button("RUN GAME",LaunchGame,"ToolbarButton"));
    right.Children.Add(Ui.Button("Uninstall",async()=>await UninstallAsync(),"FlatButton"));
    right.Children.Add(Ui.Button("Settings",Settings,"FlatButton"));
    DockPanel.SetDock(right,Dock.Right);commands.Children.Add(right);
    command.Child=commands;Ui.Put(root,command,1);

    var workGrid=new Grid();
    Ui.Cols(workGrid,Ui.Px(284),Ui.Px(4),Ui.Star(),Ui.Px(4),Ui.Px(264));
    var sidebar=BuildProjectSidebar();Ui.Put(workGrid,sidebar,0,0);
    var split1=new GridSplitter{HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Ui.Brush("#2B3139")};
    Grid.SetColumn(split1,1);workGrid.Children.Add(split1);
    var middle=BuildMainWorkspace();Ui.Put(workGrid,middle,0,2);
    var split2=new GridSplitter{HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Ui.Brush("#2B3139")};
    Grid.SetColumn(split2,3);workGrid.Children.Add(split2);
    propertyArea=BuildInspector();Ui.Put(workGrid,propertyArea,0,4);
    Ui.Put(root,workGrid,2);

    var footer=Ui.Box("#15171A");
    var dock=new DockPanel{LastChildFill=true,Margin=new Thickness(15,0,15,0)};
    footerStatus=Ui.Text("READY   •   Open or create a project, or search the game assets",11,"#A7AEB7");
    var tips=Ui.Text("W3 FORGE  /  COMMUNITY MODDING TOOL",10,"#6F7680");
    DockPanel.SetDock(tips,Dock.Right);dock.Children.Add(tips);dock.Children.Add(footerStatus);
    footer.Child=dock;Ui.Put(root,footer,3);
    return root;
  }

  Border BuildProjectSidebar(){
    var bg=Ui.Box("#171A1F");
    var panel=new Grid();
    Ui.Rows(panel,Ui.Px(54),Ui.Px(42),Ui.Star(),Ui.Px(36));
    var heading=new Grid{Margin=new Thickness(16,0,12,0)};
    Ui.Cols(heading,Ui.Star(),Ui.Px(78));
    var text=Ui.Text("PROJECT EXPLORER",10,"#9EA7B3");text.FontWeight=FontWeights.Bold;Ui.Put(heading,text,0);
    var reload=Ui.Button("Refresh",RefreshProjectTree,"FlatButton");Ui.Put(heading,reload,0,1);
    Ui.Put(panel,heading,0);
    var actions=Ui.H();actions.Margin=new Thickness(12,0,12,0);
    actions.Children.Add(Ui.Button("Add file",AddExternalFiles,"FlatButton"));
    actions.Children.Add(Ui.Button("New folder",NewProjectFolder,"FlatButton"));
    actions.Children.Add(Ui.Button("Paste",PasteProjectItem,"FlatButton"));
    Ui.Put(panel,actions,1);
    projectTree=new TreeView{Margin=new Thickness(10,5,6,8)};
    ScrollViewer.SetHorizontalScrollBarVisibility(projectTree,ScrollBarVisibility.Disabled);
    projectTree.SelectedItemChanged+=(s,e)=>{
      var item=projectTree.SelectedItem as TreeViewItem;
      var path=item?.Tag as string;
      if(path!=null){asset=null;ShowFileDetails(path);}
    };
    projectTree.MouseDoubleClick+=(s,e)=>{
      var p=(projectTree.SelectedItem as TreeViewItem)?.Tag as string;
      if(File.Exists(p))OpenDocument(p);
    };
    projectTree.PreviewKeyDown+=(s,e)=>{
      if(e.Key==Key.F2){RenameProjectItem();e.Handled=true;}
      else if(e.Key==Key.Delete){DeleteProjectItem();e.Handled=true;}
      else if(e.KeyboardDevice.Modifiers==ModifierKeys.Control){
        if(e.Key==Key.C){CopyProjectItem(false);e.Handled=true;}
        if(e.Key==Key.X){CopyProjectItem(true);e.Handled=true;}
        if(e.Key==Key.V){PasteProjectItem();e.Handled=true;}
      }
    };
    projectTree.AllowDrop=true;
    projectTree.Drop+=(s,e)=>{
      if(project==null||!e.Data.GetDataPresent(DataFormats.FileDrop))return;
      var paths=e.Data.GetData(DataFormats.FileDrop) as string[];
      if(paths==null)return;
      var dest=SelectedProjectFolder();
      foreach(var path in paths)try{CopyIntoProject(path,dest);}catch(Exception ex){Log("Drop failed: "+ex.Message);}
      RefreshProjectTree();
    };
    projectTree.DragOver+=(s,e)=>{if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effects=DragDropEffects.Copy;e.Handled=true;};
    var context=new ContextMenu();
    foreach(var pair in new[]{("Export to editable JSON",new Action(ExportSelectedJson)),
      ("Compile JSON to original RAW",new Action(CompileSelectedJson)),
      ("Open",new Action(OpenSelectedProjectFile)),
      ("Open in native CR2W editor",new Action(OpenSelectedProjectFileNative)),
      ("New folder",new Action(NewProjectFolder)),("Add files...",new Action(AddExternalFiles)),
      ("Copy",new Action(()=>CopyProjectItem(false))),("Cut",new Action(()=>CopyProjectItem(true))),
      ("Paste",new Action(PasteProjectItem)),("Rename",new Action(RenameProjectItem)),
      ("Delete",new Action(DeleteProjectItem)),("Show in Explorer",new Action(ShowInExplorer))}){
      var menu=new MenuItem{Header=pair.Item1};
      menu.Click+=(s,e)=>pair.Item2();
      context.Items.Add(menu);
    }
    projectTree.ContextMenu=context;
    projectTree.PreviewMouseRightButtonDown+=(s,e)=>{
      var current=e.OriginalSource as DependencyObject;
      while(current!=null && !(current is TreeViewItem))current=VisualTreeHelper.GetParent(current);
      if(current is TreeViewItem node)node.IsSelected=true;
    };
    Ui.Put(panel,projectTree,2);
    var footer=Ui.Box("#20242A");
    var inf=Ui.Text("Drag & drop  •  Ctrl+C / Ctrl+V",10,"#737D89");
    inf.Margin=new Thickness(14,0,14,0);footer.Child=inf;Ui.Put(panel,footer,3);
    bg.Child=panel;return bg;
  }

  void DocumentTabSelectionChanged(object sender,SelectionChangedEventArgs e) {
    if(handlingDocumentSelection || !Object.ReferenceEquals(e.Source,docs))return;
    var tab=docs.SelectedItem as TabItem;
    if(tab==null)return;

    if(Object.ReferenceEquals(tab,envStudioTab)) {
      if(String.IsNullOrWhiteSpace(envStudioContextFile) || envStudioSourceTab==null) {
        handlingDocumentSelection=true;
        docs.SelectedItem=envStudioSourceTab??browserTab;
        handlingDocumentSelection=false;
        return;
      }
      if(!liveEnvPane.OpenFile(envStudioContextFile)) {
        handlingDocumentSelection=true;
        docs.SelectedItem=envStudioSourceTab;
        handlingDocumentSelection=false;
      }
      return;
    }

    var session=sessions.FirstOrDefault(x=>Object.ReferenceEquals(x.Tab,tab));
    if(session!=null && String.Equals(Path.GetExtension(session.FilePath),".env",StringComparison.OrdinalIgnoreCase)) {
      envStudioContextFile=session.FilePath;
      envStudioSourceTab=tab;
      envStudioTab.IsEnabled=true;
      envStudioTab.ToolTip="Edit selected ENV: "+Path.GetFileName(session.FilePath);
      liveEnvPane.SetPendingContext(session.FilePath);
      return;
    }

    envStudioContextFile=null;
    envStudioSourceTab=null;
    envStudioTab.IsEnabled=false;
    envStudioTab.ToolTip="Select an open .env document first.";
  }

  Grid BuildMainWorkspace(){
    var middle=new Grid();
    Ui.Rows(middle,Ui.Star(),Ui.Px(27));
    docs=new TabControl{Margin=new Thickness(0),Background=Ui.Brush("#191C20")};
    browserTab=new TabItem{Header="GAME ASSETS",Content=BuildBrowser()};
    docs.Items.Add(browserTab);
    modToolsPane=new ModToolsPane(()=>project,RefreshProjectTree,p=>UseProject(p),Log,SetStatus);
    docs.Items.Add(new TabItem{Header="MOD TOOLS",Content=modToolsPane});
    liveEnvPane=new LiveEnvPane(()=>project,()=>gamePath.Text.Trim(),Log,SetStatus);
    envStudioTab=new TabItem{
      Header="ENV STUDIO",
      Content=liveEnvPane,
      IsEnabled=false,
      ToolTip="Select an open .env document first."
    };
    docs.Items.Add(envStudioTab);
    docs.SelectionChanged+=DocumentTabSelectionChanged;
    docs.SelectedIndex=0;
    Ui.Put(middle,docs,0);
    var outputHeader=Ui.Box("#24282D");
    var outputControl=Ui.H();
    var logToggle=Ui.Button("⌄  OUTPUT",ToggleOutput,"FlatButton");
    outputControl.Children.Add(logToggle);
    outputControl.Children.Add(Ui.Text("Build logs & actions",10,"#808893"));
    outputHeader.Child=outputControl;Ui.Put(middle,outputHeader,1);
    // Output is initially hidden; reveal it only when needed.
    outputArea=Ui.Box("#111317");
    console=new TextBox{IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
      HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,FontFamily=new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=11,
      Background=Ui.Brush("#111317"),BorderThickness=new Thickness(0),Padding=new Thickness(15)};
    outputArea.Child=console;Grid.SetRow(outputArea,2);
    middle.RowDefinitions.Add(new RowDefinition{Height=Ui.Px(0)});
    middle.Children.Add(outputArea);
    return middle;
  }

  Grid BuildBrowser(){
    var page=new Grid{Background=Ui.Brush("#191C20")};
    Ui.Rows(page,Ui.Px(58),Ui.Px(88),Ui.Star(),Ui.Px(58));
    var source=new Grid{Margin=new Thickness(15,9,15,7)};
    Ui.Cols(source,Ui.Px(92),Ui.Star(),Ui.Px(110));
    var label=Ui.Text("GAME SOURCE",10,"#9EA7B3");label.FontWeight=FontWeights.Bold;Ui.Put(source,label,0);
    gamePath=new TextBox{Text=prefs.GameBin,Margin=new Thickness(5,1,10,1),FontFamily=new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=11.5};
    Ui.Put(source,gamePath,0,1);
    var browse=Ui.Button("Browse",BrowseGame);Ui.Put(source,browse,0,2);
    Ui.Put(page,source,0);

    var searchLayout=new Grid{Margin=new Thickness(15,0,15,8)};
    Ui.Cols(searchLayout,Ui.Star(),Ui.Px(190));
    Ui.Rows(searchLayout,Ui.Px(38),Ui.Px(34));
    var searchHost=new Grid{Margin=new Thickness(0,0,12,2)};
    search=new TextBox{FontSize=13,Margin=new Thickness(0)};
    search.ToolTip="Search any depot path, file name or extension.";
    searchPlaceholder=Ui.Text("Search game resources by name or depot path...",12,"#69737F");
    searchPlaceholder.Margin=new Thickness(12,0,0,0);
    searchPlaceholder.IsHitTestVisible=false;
    searchHost.Children.Add(search);
    searchHost.Children.Add(searchPlaceholder);
    search.TextChanged+=(s,e)=>{
      searchPlaceholder.Visibility=String.IsNullOrWhiteSpace(search.Text)?Visibility.Visible:Visibility.Collapsed;
      searchDelay?.Stop();searchDelay?.Start();
    };
    Ui.Put(searchLayout,searchHost,0,0);
    hitCount=Ui.Text("Scan the game to browse assets",11,"#A2AAB4");
    hitCount.HorizontalAlignment=HorizontalAlignment.Right;Ui.Put(searchLayout,hitCount,0,1);
    var filters=Ui.H();
    filterButtons.Clear();
    foreach(var pair in new[]{("ALL",""),("ENV",".env"),("MESH",".w2mesh"),("TEXTURE",".xbm"),("ENTITY",".w2ent"),("WORLD",".w2w")}){
      string ext=pair.Item2;
      var button=Ui.Button(pair.Item1,()=>SetAssetFilter(ext),"FlatButton");
      button.Tag=ext;
      button.FontSize=10;
      button.Padding=new Thickness(10,3,10,3);
      button.MinHeight=26;
      button.Height=26;
      button.Margin=new Thickness(0,4,8,4);
      filterButtons.Add(button);
      filters.Children.Add(button);
    }
    UpdateFilterButtons();
    Ui.Put(searchLayout,filters,1,0,1,2);Ui.Put(page,searchLayout,1);

    var body=new Grid();
    Ui.Cols(body,Ui.Px(252),Ui.Px(4),Ui.Star());
    var foldersWrap=Ui.Box("#1B1F24");
    var folderGrid=new Grid();
    Ui.Rows(folderGrid,Ui.Px(37),Ui.Star());
    var folderHeader=Ui.Text("GAME DEPOT",10,"#8E98A5");
    folderHeader.FontWeight=FontWeights.Bold;
    folderHeader.Margin=new Thickness(13,0,13,0);
    Ui.Put(folderGrid,folderHeader,0);
    folderTree=new TreeView{Margin=new Thickness(6,2,4,5)};
    ScrollViewer.SetHorizontalScrollBarVisibility(folderTree,ScrollBarVisibility.Disabled);
    folderTree.SelectedItemChanged+=(s,e)=>{
      if(folderTree.SelectedItem is TreeViewItem item && item.Tag is AssetFolder f){
        folder=f;RefreshAssets();
      }
    };
    Ui.Put(folderGrid,folderTree,1);foldersWrap.Child=folderGrid;Ui.Put(body,foldersWrap,0);
    var split=new Border{Width=4,Background=Ui.Brush("#383D45")};Ui.Put(body,split,0,1);
    assetList=new ListView{SelectionMode=SelectionMode.Extended,Margin=new Thickness(2,0,7,0)};
    var view=new GridView{AllowsColumnReorder=true};
    view.Columns.Add(new GridViewColumn{Header="DEPOT RESOURCE PATH",CellTemplate=AssetCellTemplate("DepotPath",true),Width=470});
    view.Columns.Add(new GridViewColumn{Header="TYPE",CellTemplate=AssetCellTemplate("Extension",false),Width=74});
    view.Columns.Add(new GridViewColumn{Header="SIZE",CellTemplate=AssetCellTemplate("SizeText",false),Width=86});
    view.Columns.Add(new GridViewColumn{Header="BUNDLE",CellTemplate=AssetCellTemplate("Bundle",true),Width=128});
    assetList.View=view;
    VirtualizingStackPanel.SetIsVirtualizing(assetList,true);
    VirtualizingStackPanel.SetVirtualizationMode(assetList,VirtualizationMode.Recycling);
    assetList.SelectionChanged+=(s,e)=>{
      asset=assetList.SelectedItem as GameAsset;
      ShowAssetDetails();
      addSelected.IsEnabled=project!=null && assetList.SelectedItems.Count>0;
    };
    assetList.MouseDoubleClick+=(s,e)=>OpenSelectedAsset();
    assetList.ContextMenu=MakeAssetMenu();
    Ui.Put(body,assetList,0,2);
    Ui.Put(page,body,2);

    var actionBar=Ui.Box("#20242A");
    var action=Ui.H();action.Margin=new Thickness(12,0,12,0);
    addSelected=Ui.Button("ADD TO PROJECT",AddAssetsToProject,"PrimaryButton");
    addSelected.IsEnabled=false;
    action.Children.Add(addSelected);
    action.Children.Add(Ui.Button("Extract...",ExtractAssets));
    action.Children.Add(Ui.Button("Open resource",OpenSelectedAsset));
    action.Children.Add(Ui.Button("Copy depot path",CopyAssetPath,"FlatButton"));
    actionBar.Child=action;Ui.Put(page,actionBar,3);
    return page;
  }

  DataTemplate AssetCellTemplate(string property,bool mono){
    var factory=new FrameworkElementFactory(typeof(TextBlock));
    factory.SetBinding(TextBlock.TextProperty,new Binding(property));
    factory.SetBinding(TextBlock.ToolTipProperty,new Binding(property));
    factory.SetValue(TextBlock.VerticalAlignmentProperty,VerticalAlignment.Center);
    factory.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
    factory.SetValue(TextBlock.MarginProperty,new Thickness(9,0,9,0));
    factory.SetValue(TextBlock.FontFamilyProperty,new FontFamily(
      mono?"JetBrains Mono, Cascadia Mono, Consolas":"Inter, Segoe UI Variable, Segoe UI"));
    factory.SetValue(TextBlock.FontSizeProperty,mono?11.25:11.75);
    return new DataTemplate{VisualTree=factory};
  }

  ContextMenu MakeAssetMenu(){
    var menu=new ContextMenu{
      Background=Ui.Brush("#171B20"),
      Foreground=Ui.Brush("#EEF1F5"),
      BorderBrush=Ui.Brush("#3A424D"),
      BorderThickness=new Thickness(1),
      Padding=new Thickness(5),
      FontFamily=new FontFamily("Inter, Segoe UI Variable, Segoe UI")
    };

    var add=new MenuItem{Header="Add to project"};
    var extract=new MenuItem{Header="Extract selected..."};
    var exportPng=new MenuItem{Header="Export selected XBM as PNG..."};
    var exportTga=new MenuItem{Header="Export selected XBM as TGA..."};
    var exportDds=new MenuItem{Header="Export selected XBM as DDS..."};
    var open=new MenuItem{Header="Open resource"};
    var copy=new MenuItem{Header="Copy depot path"};

    add.Click+=(s,e)=>AddAssetsToProject();
    extract.Click+=(s,e)=>ExtractAssets();
    exportPng.Click+=(s,e)=>BatchExportTextures(".png");
    exportTga.Click+=(s,e)=>BatchExportTextures(".tga");
    exportDds.Click+=(s,e)=>BatchExportTextures(".dds");
    open.Click+=(s,e)=>OpenSelectedAsset();
    copy.Click+=(s,e)=>CopyAssetPath();

    menu.Items.Add(add);
    menu.Items.Add(extract);
    menu.Items.Add(new Separator());
    menu.Items.Add(exportPng);
    menu.Items.Add(exportTga);
    menu.Items.Add(exportDds);
    menu.Items.Add(new Separator());
    menu.Items.Add(open);
    menu.Items.Add(new Separator());
    menu.Items.Add(copy);

    menu.Opened+=(s,e)=>{
      bool selected=assetList!=null && assetList.SelectedItems.Count>0;
      add.IsEnabled=selected && project!=null;
      extract.IsEnabled=selected;
      bool hasTextures=selected&&assetList.SelectedItems.Cast<GameAsset>().Any(x=>x.Extension==".xbm");
      exportPng.IsEnabled=hasTextures;
      exportTga.IsEnabled=hasTextures;
      exportDds.IsEnabled=hasTextures;
      open.IsEnabled=selected && assetList.SelectedItems.Count==1;
      copy.IsEnabled=selected && assetList.SelectedItems.Count==1;
      add.ToolTip=project==null?"Open or create a project first.":null;
    };
    return menu;
  }

  Border BuildInspector(){
    var bg=Ui.Box("#171A1E");
    var main=new Grid();Ui.Rows(main,Ui.Px(54),Ui.Px(1),Ui.Star(),Ui.Px(78));
    var heading=Ui.Text("INSPECTOR",10,"#9EA7B3");heading.FontWeight=FontWeights.Bold;heading.Margin=new Thickness(20,0,20,0);
    Ui.Put(main,heading,0);
    Ui.Put(main,Ui.Box("#383C44"),1);

    var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    var inspect=Ui.V();

    texturePreview=new System.Windows.Controls.Image{
      Stretch=Stretch.Uniform,
      SnapsToDevicePixels=true,
      MaxHeight=280,
      Margin=new Thickness(8)
    };
    texturePreviewHost=Ui.Box("#101318","#343A43",radius:2);
    texturePreviewHost.Height=260;
    texturePreviewHost.Margin=new Thickness(14,14,14,4);
    texturePreviewHost.Child=texturePreview;
    texturePreviewHost.Visibility=Visibility.Collapsed;
    inspect.Children.Add(texturePreviewHost);

    textureStatus=Ui.Text("",10.5,"#8E98A5");
    textureStatus.Margin=new Thickness(16,6,16,3);
    textureStatus.TextWrapping=TextWrapping.Wrap;
    textureStatus.Visibility=Visibility.Collapsed;
    inspect.Children.Add(textureStatus);

    textureActions=Ui.H();
    textureActions.Margin=new Thickness(12,5,12,3);
    textureActions.Visibility=Visibility.Collapsed;
    var png=Ui.Button("PNG",()=>ExportCurrentTexture(".png"),"ToolbarButton");
    var tga=Ui.Button("TGA",()=>ExportCurrentTexture(".tga"),"ToolbarButton");
    var dds=Ui.Button("DDS",()=>ExportCurrentTexture(".dds"),"ToolbarButton");
    var lab=Ui.Button("TEXTURE LAB",OpenTextureLab,"FlatButton");
    textureActions.Children.Add(png);textureActions.Children.Add(tga);textureActions.Children.Add(dds);textureActions.Children.Add(lab);
    inspect.Children.Add(textureActions);

    info=Ui.Text("Select an asset or a file to inspect its details.",13,"#9299A4");
    info.Margin=new Thickness(20);
    info.TextWrapping=TextWrapping.Wrap;info.VerticalAlignment=VerticalAlignment.Top;info.TextTrimming=TextTrimming.None;
    inspect.Children.Add(info);
    scroll.Content=inspect;Ui.Put(main,scroll,2);

    var note=Ui.Box("#171B20","#292F37");
    var targetCopy=Ui.V();
    targetCopy.Margin=new Thickness(18,10,18,10);
    targetCopy.VerticalAlignment=VerticalAlignment.Center;
    var targetLabel=Ui.Text("TARGET GAME",9,"#7F8996");targetLabel.FontWeight=FontWeights.SemiBold;
    targetCopy.Children.Add(targetLabel);
    var targetName=Ui.Text("The Witcher 3 Remastered",11,"#C9CFD7");targetName.Margin=new Thickness(0,5,0,0);
    targetCopy.Children.Add(targetName);
    note.Child=targetCopy;Ui.Put(main,note,3);
    bg.Child=main;return bg;
  }

  void ShowAssetDetails(){
    ResetTexturePreview();
    if(asset==null){info.Text="Select a game asset to inspect its details.";return;}
    info.Text="GAME RESOURCE\n\n"+asset.Name+"\n\n"+
      "DEPOT PATH\n"+asset.DepotPath+"\n\n"+
      "TYPE\n"+(asset.Extension.ToUpperInvariant())+"\n\n"+
      "UNCOMPRESSED\n"+asset.SizeText+"\n\n"+
      "COMPRESSION\n"+asset.Compression+"\n\n"+
      "SOURCE BUNDLE\n"+asset.Bundle+"\n\n"+
      (asset.Extension==".xbm"?"Texture preview loads automatically above.":"Double-click to inspect or select Add to Project to extract the original asset into your mod workspace.");
    if(asset.Extension==".xbm")LoadTexturePreview(asset);
  }

  void ShowFileDetails(string file){
    ResetTexturePreview();
    if(!File.Exists(file)&&!Directory.Exists(file))return;
    bool dir=Directory.Exists(file);
    info.Text=(dir?"PROJECT DIRECTORY":"PROJECT FILE")+"\n\n"+Path.GetFileName(file)+
      "\n\nLOCATION\n"+file+
      (!dir?"\n\nSIZE\n"+(new FileInfo(file).Length/1024.0).ToString("N1")+" KB":"")+
      (!dir&&Path.GetExtension(file).Equals(".xbm",StringComparison.OrdinalIgnoreCase)
        ?"\n\nTexture preview loads automatically above."
        :"\n\nDouble-click to open. Right-click for project file operations.");
    if(!dir&&Path.GetExtension(file).Equals(".xbm",StringComparison.OrdinalIgnoreCase))
      LoadTexturePreview(file);
  }

  void ResetTexturePreview(){
    currentTexture=null;
    currentTextureName=null;
    if(texturePreview!=null)texturePreview.Source=null;
    if(texturePreviewHost!=null)texturePreviewHost.Visibility=Visibility.Collapsed;
    if(textureStatus!=null){textureStatus.Text="";textureStatus.Visibility=Visibility.Collapsed;}
    if(textureActions!=null)textureActions.Visibility=Visibility.Collapsed;
  }

  async void LoadTexturePreview(GameAsset row){
    if(row==null||index==null)return;
    var bin=gamePath.Text.Trim();
    var projectCache=project!=null&&File.Exists(project.TextureCache)?project.TextureCache:null;
    texturePreviewHost.Visibility=Visibility.Visible;
    textureStatus.Visibility=Visibility.Visible;
    textureStatus.Text="Loading texture preview...";
    var expected=row;
    try{
      var cacheRoot=Path.Combine(EnvironmentPaths.CacheRoot,"textures");
      var relative=row.DepotPath.Replace('/','\\').TrimStart('\\');
      if(relative.Contains("..")||relative.Contains(":")||Path.IsPathRooted(relative))
        throw new InvalidDataException("Unsafe texture depot path.");
      var local=Path.Combine(cacheRoot,relative);
      var data=await Task.Run(()=>{
        Directory.CreateDirectory(Path.GetDirectoryName(local));
        index.Extract(row,local);
        return TextureTools.Load(local,row.DepotPath,bin,projectCache);
      });
      if(asset!=expected)return;
      ApplyTexturePreview(data,row.Name);
    }catch(Exception ex){
      if(asset!=expected)return;
      Program.LogException(ex);
      texturePreviewHost.Visibility=Visibility.Collapsed;
      textureStatus.Visibility=Visibility.Visible;
      textureStatus.Text="Texture preview unavailable: "+ex.Message;
    }
  }

  async void LoadTexturePreview(string file){
    if(!File.Exists(file))return;
    string selected=file;
    string depot=ProjectDepotPath(file);
    string bin=gamePath.Text.Trim();
    string projectCache=project!=null&&File.Exists(project.TextureCache)?project.TextureCache:null;
    texturePreviewHost.Visibility=Visibility.Visible;
    textureStatus.Visibility=Visibility.Visible;
    textureStatus.Text="Loading texture preview...";
    try{
      var data=await Task.Run(()=>TextureTools.Load(file,depot,bin,projectCache));
      if(!String.Equals(SelectedProjectPath(),selected,StringComparison.OrdinalIgnoreCase))return;
      ApplyTexturePreview(data,Path.GetFileName(file));
    }catch(Exception ex){
      if(!String.Equals(SelectedProjectPath(),selected,StringComparison.OrdinalIgnoreCase))return;
      Program.LogException(ex);
      texturePreviewHost.Visibility=Visibility.Collapsed;
      textureStatus.Visibility=Visibility.Visible;
      textureStatus.Text="Texture preview unavailable: "+ex.Message;
    }
  }

  string ProjectDepotPath(string file){
    if(project==null)return Path.GetFileName(file);
    string rel=RelativeUnder(file,project.ModBundle);
    if(rel!=null)return rel;
    rel=RelativeUnder(file,project.Files);
    if(rel!=null){
      if(rel.StartsWith("Mod\\Bundle\\",StringComparison.OrdinalIgnoreCase))
        return rel.Substring("Mod\\Bundle\\".Length);
      return rel;
    }
    return Path.GetFileName(file);
  }

  string RelativeUnder(string file,string rootPath){
    try{
      if(String.IsNullOrWhiteSpace(rootPath)||!Directory.Exists(rootPath))return null;
      string rootFull=Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
      string full=Path.GetFullPath(file);
      if(full.StartsWith(rootFull,StringComparison.OrdinalIgnoreCase))return full.Substring(rootFull.Length).Replace('/','\\');
    }catch{}
    return null;
  }

  void ApplyTexturePreview(TexturePreviewData data,string name){
    currentTexture=data;
    currentTextureName=name;
    texturePreview.Source=data.Image;
    texturePreviewHost.Visibility=Visibility.Visible;
    textureStatus.Visibility=Visibility.Visible;
    textureActions.Visibility=Visibility.Visible;
    var t=data.Info;
    textureStatus.Text=t.Width+" × "+t.Height+"  •  "+t.DdsFormat+
      "\n"+(String.IsNullOrWhiteSpace(t.TextureGroup)?"":t.TextureGroup+"  •  ")+t.SourceLabel+
      "\nMipmaps: "+t.MipCount+(t.FullResolution?"  •  Full cache data":"  •  Resident mip fallback");
  }

  void ExportCurrentTexture(string ext){
    if(currentTexture==null)return;
    ExportTexture(currentTexture,currentTextureName,ext);
  }

  void ExportTexture(TexturePreviewData data,string name,string ext){
    if(data==null)return;
    string label=ext==".png"?"PNG Image|*.png":ext==".tga"?"Targa Image|*.tga":"DirectDraw Surface|*.dds";
    var dlg=new SaveFileDialog{
      Filter=label,
      FileName=Path.GetFileNameWithoutExtension(name??"texture")+ext,
      AddExtension=true,
      DefaultExt=ext.TrimStart('.')
    };
    if(dlg.ShowDialog()!=true)return;
    try{
      TextureTools.Export(data,dlg.FileName,ext);
      SetStatus("Texture exported: "+dlg.FileName);
      Log("Texture export "+ext.ToUpperInvariant()+": "+dlg.FileName);
    }catch(Exception ex){
      Program.LogException(ex);
      MessageBox.Show(ex.Message,"Texture export",MessageBoxButton.OK,MessageBoxImage.Error);
    }
  }

  void OpenTextureLab(){
    if(currentTexture==null)return;
    var data=currentTexture;
    var name=currentTextureName??"texture";

    var tab=new TabItem();
    var header=Ui.H();
    header.Children.Add(Ui.Text("TEXTURE LAB  •  "+name,11.5,"#D7DCE3"));
    var close=Ui.Button("×",()=>docs.Items.Remove(tab),"FlatButton");
    close.Margin=new Thickness(7,0,0,0);close.Padding=new Thickness(5,0,5,0);
    header.Children.Add(close);
    tab.Header=header;

    var page=new Grid{Background=Ui.Brush("#12151A")};
    Ui.Rows(page,Ui.Px(58),Ui.Star(),Ui.Px(44));

    var top=new DockPanel{Margin=new Thickness(15,8,15,8)};
    var meta=Ui.Text(data.Info.Width+" × "+data.Info.Height+"   "+data.Info.DdsFormat+"   "+data.Info.SourceLabel,11,"#AAB3BE");
    meta.VerticalAlignment=VerticalAlignment.Center;
    DockPanel.SetDock(meta,Dock.Left);top.Children.Add(meta);
    var buttons=Ui.H();buttons.HorizontalAlignment=HorizontalAlignment.Right;
    buttons.Children.Add(Ui.Button("EXPORT PNG",()=>ExportTexture(data,name,".png"),"ToolbarButton"));
    buttons.Children.Add(Ui.Button("EXPORT TGA",()=>ExportTexture(data,name,".tga"),"ToolbarButton"));
    buttons.Children.Add(Ui.Button("EXPORT DDS",()=>ExportTexture(data,name,".dds"),"ToolbarButton"));
    DockPanel.SetDock(buttons,Dock.Right);top.Children.Add(buttons);
    Ui.Put(page,top,0);

    var imageBorder=Ui.Box("#0B0D10","#303640");
    imageBorder.Margin=new Thickness(15,0,15,10);
    var image=new System.Windows.Controls.Image{Source=data.Image,Stretch=Stretch.Uniform,SnapsToDevicePixels=true,Margin=new Thickness(10)};
    imageBorder.Child=image;Ui.Put(page,imageBorder,1);

    var foot=Ui.Text(
      (String.IsNullOrWhiteSpace(data.Info.TextureGroup)?"":data.Info.TextureGroup+"  •  ")+
      "Mipmaps "+data.Info.MipCount+
      (String.IsNullOrWhiteSpace(data.Info.CacheFile)?"":"  •  "+data.Info.CacheFile),
      10,"#78828E");
    foot.Margin=new Thickness(18,0,18,0);foot.TextTrimming=TextTrimming.CharacterEllipsis;
    Ui.Put(page,foot,2);

    tab.Content=page;
    docs.Items.Add(tab);
    docs.SelectedItem=tab;
  }

  async Task ScanGameAsync(){
    if(loading)return;
    string bin=gamePath.Text.Trim();
    string rootGame=EnvironmentPaths.GameRootFromBin(bin);
    if(rootGame==null||!Directory.Exists(bin)){
      MessageBox.Show("Choose the Witcher 3 bin\\x64_dx12 directory.","Game location");
      return;
    }
    loading=true;scanButton.IsEnabled=false;hitCount.Text="Scanning...";SetStatus("Scanning Witcher 3 Remastered game bundles...");
    try{
      var newIndex=new BundleIndex();
      await newIndex.ScanAsync(bin,x=>Dispatcher.BeginInvoke(new Action(()=>SetStatus(x))));
      index=newIndex;
      folder=null;PopulateGameFolders();
      RefreshAssets();
      prefs.GameBin=bin;prefs.Save();
      Log($"Indexed {index.All.Count:N0} resources across {index.BundleCount} bundles.");
      SetStatus("GAME ASSETS READY  •  "+index.All.Count.ToString("N0")+" resources indexed");
    }catch(Exception ex){
      Program.LogException(ex);
      Log("Game scan failed: "+ex.Message);
      string message=ex.Message;
      if(ex is FileNotFoundException || ex is FileLoadException || ex is BadImageFormatException)
        message+="\n\nPlease extract the complete W3 Forge Studio archive, including Runtime and W3ForgeStudio.exe.config, into a fresh folder.";
      MessageBox.Show(message,"Scanning game",MessageBoxButton.OK,MessageBoxImage.Error);
      SetStatus("Scan failed");
    }
    finally{loading=false;scanButton.IsEnabled=true;}
  }
  void PopulateGameFolders(){
    folderTree.Items.Clear();
    if(index==null)return;
    var rootItem=FolderItem(index.Root);
    folderTree.Items.Add(rootItem);
    rootItem.IsExpanded=true;rootItem.IsSelected=true;
  }
  TreeViewItem FolderItem(AssetFolder f){
    var item=new TreeViewItem{Header=(f.FullPath==""?"All game assets":f.Name),Tag=f,ToolTip=(f.FullPath==""?"Game content":f.FullPath)};
    if(f.Children.Count>0){
      item.Items.Add(new TreeViewItem{Header="..."});
      item.Expanded+=(s,e)=>{
        if(!Object.ReferenceEquals(e.OriginalSource,item))return;
        if(item.Items.Count==1 && item.Items[0] is TreeViewItem placeholder && placeholder.Tag==null){
          item.Items.Clear();
          foreach(var child in f.Children.Values.OrderBy(x=>x.Name,StringComparer.OrdinalIgnoreCase))item.Items.Add(FolderItem(child));
        }
      };
    }
    return item;
  }
  void RefreshAssets(){
    if(index==null)return;
    string query=search.Text.Trim();
    int total;
    // Empty root displays a manageable preview, while selected folders show direct contents.
    var inFolder=String.IsNullOrEmpty(query)&&folder!=null&&folder.FullPath!=""?folder:null;
    visible=index.Search(query,extensionFilter,inFolder,5000,out total);
    assetList.ItemsSource=visible;
    string filterLabel=String.IsNullOrEmpty(extensionFilter)?"ALL":extensionFilter.TrimStart('.').ToUpperInvariant();
    hitCount.Text=(visible.Count<total
      ?"Showing "+visible.Count.ToString("N0")+" of "+total.ToString("N0")
      :total.ToString("N0")+" resources")+"  •  "+filterLabel;
    assetState=hitCount;
    asset=null;addSelected.IsEnabled=false;
  }
  void AddAssetsToProject(){
    if(project==null){MessageBox.Show("Open or create a project first.","Add to project");return;}
    var selected=assetList.SelectedItems.Cast<GameAsset>().ToArray();
    if(selected.Length==0)return;
    int added=0;
    foreach(var item in selected){
      try{index.AddToProject(item,project);added++;}
      catch(Exception ex){Log("Unable to add "+item.DepotPath+": "+ex.Message);}
    }
    RefreshProjectTree();
    Log("Added "+added+" native game resources to "+project.Name);
    SetStatus(added+" resources added to project.");
  }
  void ExtractAssets(){
    if(index==null||assetList.SelectedItems.Count==0)return;
    using(var dlg=new System.Windows.Forms.FolderBrowserDialog()){
      dlg.Description="Select extraction destination";
      if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
      foreach(GameAsset row in assetList.SelectedItems){
        try{
          var name=row.DepotPath.Replace('/','\\');
          if(name.Contains("..")||Path.IsPathRooted(name))continue;
          index.Extract(row,Path.Combine(dlg.SelectedPath,name));
        }catch(Exception ex){Log("Extract failed: "+ex.Message);}
      }
      SetStatus("Extraction completed.");
    }
  }
  async void BatchExportTextures(string ext){
    if(index==null||assetList==null)return;
    var selected=assetList.SelectedItems.Cast<GameAsset>().Where(x=>x.Extension==".xbm").ToArray();
    if(selected.Length==0)return;

    string destination=null;
    using(var dlg=new System.Windows.Forms.FolderBrowserDialog()){
      dlg.Description="Select destination for "+ext.TrimStart('.').ToUpperInvariant()+" texture export";
      if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
      destination=dlg.SelectedPath;
    }

    string bin=gamePath.Text.Trim();
    string projectCache=project!=null&&File.Exists(project.TextureCache)?project.TextureCache:null;
    SetStatus("Exporting "+selected.Length+" texture"+(selected.Length==1?"":"s")+" as "+ext.TrimStart('.').ToUpperInvariant()+"...");
    int ok=0,failed=0;

    await Task.Run(()=>{
      foreach(var row in selected){
        try{
          var relative=row.DepotPath.Replace('/','\\').TrimStart('\\');
          if(relative.Contains("..")||relative.Contains(":")||Path.IsPathRooted(relative))
            throw new InvalidDataException("Unsafe texture depot path: "+row.DepotPath);

          var cacheFile=Path.Combine(EnvironmentPaths.CacheRoot,"textures",relative);
          Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
          index.Extract(row,cacheFile);

          var data=TextureTools.Load(cacheFile,row.DepotPath,bin,projectCache);
          var relOut=Path.ChangeExtension(relative,ext);
          var target=Path.Combine(destination,relOut);
          Directory.CreateDirectory(Path.GetDirectoryName(target));
          TextureTools.Export(data,target,ext);
          ok++;
        }catch(Exception ex){
          failed++;
          Log("Texture export failed for "+row.DepotPath+": "+ex.Message);
          Program.LogException(ex);
        }
      }
    });

    SetStatus("Texture export complete: "+ok+" succeeded"+(failed>0?", "+failed+" failed":"")+".");
    if(failed>0)
      MessageBox.Show(ok+" texture(s) exported successfully.\n"+failed+" failed. See Output for details.","Batch texture export",MessageBoxButton.OK,MessageBoxImage.Warning);
    else
      MessageBox.Show(ok+" texture(s) exported successfully to:\n"+destination,"Batch texture export",MessageBoxButton.OK,MessageBoxImage.Information);
  }

  void CopyAssetPath(){if(asset!=null)System.Windows.Clipboard.SetText(asset.DepotPath);}
  void OpenSelectedAsset(){
    if(asset==null||index==null)return;
    try{
      var dir=Path.Combine(EnvironmentPaths.CacheRoot,"preview");
      var filename=asset.DepotPath.Replace('/','\\');
      if(filename.Contains("..")||Path.IsPathRooted(filename))return;
      var file=Path.Combine(dir,filename);
      index.Extract(asset,file);
      OpenDocument(file);
    }catch(Exception ex){MessageBox.Show("Could not open this resource:\n"+ex.Message);}
  }

  void OpenDocument(string file,bool forceNative=false){
    // IMPORTANT: opening an ENV must NEVER jump to ENV Studio automatically.
    // Default Open / double-click always uses the normal/native resource editor.
    // ENV Studio is an explicit opt-in action only.
    var found=sessions.FirstOrDefault(x=>x.FilePath.Equals(file,StringComparison.OrdinalIgnoreCase));
    if(found!=null){docs.SelectedItem=found.Tab;return;}
    try{
      var document=Editors.Create(file,Log);
      var header=Ui.H();
      string sourceTag="FILE";
      if(project!=null && file.StartsWith(project.Files,StringComparison.OrdinalIgnoreCase))sourceTag="PROJECT";
      else {
        string previewRoot=Path.Combine(EnvironmentPaths.CacheRoot,"preview");
        if(file.StartsWith(previewRoot,StringComparison.OrdinalIgnoreCase))sourceTag="GAME";
      }
      header.Children.Add(Ui.Text(Path.GetFileName(file)+"  •  "+sourceTag,12));
      var close=Ui.Button("×",()=>{
        if(document.Dirty&&MessageBox.Show("Discard unsaved changes?","Close resource",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;
        try{document.Close?.Invoke();}catch{}
        docs.Items.Remove(document.Tab);sessions.Remove(document);docs.SelectedIndex=0;
      },"FlatButton");
      close.Margin=new Thickness(7,0,0,0);close.FontSize=14;close.Padding=new Thickness(5,0,5,0);
      header.Children.Add(close);document.Tab.Header=header;
      docs.Items.Add(document.Tab);sessions.Add(document);docs.SelectedItem=document.Tab;
      SetStatus("Opened "+file);
    }catch(Exception ex){Log("Open resource error: "+ex);MessageBox.Show(ex.Message,"Resource editor",MessageBoxButton.OK,MessageBoxImage.Warning);}
  }

  async void UnpackMod(){
    if(working)return;
    string source=null;
    using(var dlg=new System.Windows.Forms.FolderBrowserDialog()){
      dlg.Description="Select a Witcher 3 mod folder or its content folder";
      var gameRoot=EnvironmentPaths.GameRootFromBin(gamePath.Text.Trim());
      var mods=gameRoot==null?null:Path.Combine(gameRoot,"mods");
      if(mods!=null&&Directory.Exists(mods))dlg.SelectedPath=mods;
      if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
      source=dlg.SelectedPath;
    }

    string defaultName=Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
    if(defaultName.Equals("content",StringComparison.OrdinalIgnoreCase))
      defaultName=Path.GetFileName(Path.GetDirectoryName(source));
    if(defaultName.StartsWith("mod",StringComparison.OrdinalIgnoreCase)&&defaultName.Length>3)
      defaultName=defaultName.Substring(3);
    var namePrompt=new TextPrompt("Unpack mod","Project name",defaultName){Owner=this};
    if(namePrompt.ShowDialog()!=true||String.IsNullOrWhiteSpace(namePrompt.Value))return;

    string destination=null;
    using(var dlg=new System.Windows.Forms.FolderBrowserDialog()){
      dlg.Description="Choose where to create the editable W3 Forge project";
      var suggested=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"W3 Forge Projects");
      try{Directory.CreateDirectory(suggested);dlg.SelectedPath=suggested;}catch{}
      if(dlg.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
      destination=dlg.SelectedPath;
    }

    // Capture all WPF-owned values on the UI thread before entering Task.Run.
    // Accessing namePrompt.Value or gamePath.Text from the worker thread throws
    // "The calling thread cannot access this object because a different thread owns it."
    string projectName=namePrompt.Value.Trim();
    string unpackGameBin=gamePath.Text.Trim();

    working=true;UpdateBuildState();ExpandOutput();
    SetStatus("Unpacking mod into an editable project...");
    try{
      var result=await Task.Run(()=>ModUnpacker.Import(source,destination,projectName,unpackGameBin,Log));
      UseProject(result.Project);
      SetStatus("MOD UNPACKED  •  "+result.ResourceCount+" resources ready to edit");
      Log("Imported mod project: "+result.Project.FilePath);
      MessageBox.Show(
        "Mod unpacked successfully.\n\n"+
        "Resources: "+result.ResourceCount+
        (result.BufferCount>0?"\nExternal buffers: "+result.BufferCount:"")+
        (result.LooseFileCount>0?"\nLoose files preserved: "+result.LooseFileCount:"")+
        (result.ImportedTextureCache?"\nTexture cache: imported":"")+
        "\n\nProject:\n"+result.Project.FilePath,
        "Unpack Mod",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
    }catch(Exception ex){
      Program.LogException(ex);
      Log("Unpack Mod failed: "+ex.Message);
      SetStatus("Unpack Mod failed");
      MessageBox.Show(ex.Message,"Unpack Mod",MessageBoxButton.OK,MessageBoxImage.Error);
    }finally{
      working=false;UpdateBuildState();
    }
  }

  void CreateProject(){
    var d=new ProjectDialog(prefs){Owner=this};
    if(d.ShowDialog()!=true)return;
    try{
      var p=Project.Create(d.Folder,d.ProjectName,d.GameBin);
      UseProject(p);
      Log("Project created: "+p.FilePath);
    }catch(Exception ex){MessageBox.Show(ex.Message,"Create project",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  void LaunchGame(){
    try{
      var bin=EnvironmentPaths.ValidateGameCandidate(gamePath.Text.Trim());
      if(String.IsNullOrWhiteSpace(bin))throw new DirectoryNotFoundException("Configure a valid Witcher 3 installation in Settings before running.");
      if(Process.GetProcessesByName("witcher3").Length>0){
        SetStatus("The Witcher 3 is already running.");return;
      }
      var exe=Path.Combine(bin,"witcher3.exe");
      if(!File.Exists(exe))throw new FileNotFoundException("The Witcher 3 executable was not found.",exe);
      Process.Start(new ProcessStartInfo{
        FileName=exe,WorkingDirectory=bin,UseShellExecute=true
      });
      SetStatus("Started The Witcher 3: "+exe);
      Log("Run game: "+exe);
    }catch(Exception ex){
      Log("Run game failed: "+ex.Message);
      MessageBox.Show(ex.Message,"Run The Witcher 3",MessageBoxButton.OK,MessageBoxImage.Error);
    }
  }
  void CreateWitcherScript(){
    if(project==null){MessageBox.Show("Open or create a project before adding scripts.","New WitcherScript");return;}
    var dialog=new TextPrompt("New WitcherScript (.ws)","Script filename","my_mod_script.ws"){Owner=this};
    if(dialog.ShowDialog()!=true)return;
    string name=dialog.Value.Trim();
    if(!name.EndsWith(".ws",StringComparison.OrdinalIgnoreCase))name+=".ws";
    if(Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0){
      MessageBox.Show("Use a filename without slashes, e.g. my_mod_script.ws.","New WitcherScript");return;
    }
    var directory=Path.Combine(project.RawFiles,"content","scripts","local","w3forge");
    try{
      Directory.CreateDirectory(directory);
      var output=Path.Combine(directory,name);
      if(File.Exists(output))throw new IOException("Script already exists: "+output);
      string fn="w3forge_"+new string(Path.GetFileNameWithoutExtension(name).ToLowerInvariant().Select(c=>((c>='a'&&c<='z')||(c>='0'&&c<='9'))?c:'_').ToArray());
      var body="// WitcherScript (.ws) for The Witcher 3 Next-Gen.\r\n"+
        "// Installed as loose content/scripts/local/w3forge/"+name+".\r\n"+
        "// Create functions carefully: duplicate global names can fail script compilation.\r\n\r\n"+
        "exec function "+fn+"()\r\n{\r\n    LogChannel('W3FORGE', \""+fn+" loaded\");\r\n}\r\n";
      File.WriteAllText(output,body,new System.Text.UTF8Encoding(false));
      projectTreeFocusAfterRefresh=output;
      RefreshProjectTree();
      OpenDocument(output);
      Log("Created WitcherScript for loose-file build: "+output);
    }catch(Exception ex){MessageBox.Show(ex.Message,"Create WitcherScript",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  void ChooseProject(){
    var dlg=new OpenFileDialog{Title="Open Witcher 3 project (.w3modproj)",Filter="Witcher 3 Mod Project|*.w3modproj",RestoreDirectory=true,CheckFileExists=true};
    // Open Project uses only the project directory, never the last Open File location.
    if(!String.IsNullOrWhiteSpace(prefs.LastProject)){
      var folder=Path.GetDirectoryName(prefs.LastProject);
      if(Directory.Exists(folder))dlg.InitialDirectory=folder;
      if(File.Exists(prefs.LastProject))dlg.FileName=Path.GetFileName(prefs.LastProject);
    }
    if(dlg.ShowDialog()==true)try{OpenProject(dlg.FileName);}catch(Exception ex){MessageBox.Show(ex.Message); }
  }
  void ChooseFileToOpen(){
    var dlg=new OpenFileDialog{
      Title="Open Witcher 3 resource",
      Multiselect=false,
      Filter="Witcher 3 resources and source|*.env;*.xbm;*.w2mesh;*.w2ent;*.w2w;*.w2l;*.w2anims;*.csv;*.xml;*.ws;*.txt;*.ini;*.json;*.cpp;*.cs;*.c;*.h;*.hpp;*.lua;*.obj;*.mtl|All files|*.*",
      RestoreDirectory=true
    };
    if(dlg.ShowDialog()==true)OpenDocument(dlg.FileName);
  }
  void OpenProject(string path){UseProject(Project.Open(path));Log("Opened project: "+path);}
  void UseProject(Project p){
    project=p;prefs.LastProject=p.FilePath;
    if(!String.IsNullOrWhiteSpace(p.GameBin)){gamePath.Text=p.GameBin;prefs.GameBin=p.GameBin;}
    prefs.Save();
    projectTitle.Text="PROJECT  /  "+p.Name.ToUpperInvariant();
    RefreshProjectTree();UpdateInstallState();
    addSelected.IsEnabled=assetList!=null && assetList.SelectedItems.Count>0;
    modToolsPane?.NotifyProjectChanged();
    liveEnvPane?.NotifyProjectChanged();
    SetStatus("Project ready: "+p.Name);
  }
  static void RememberExpanded(ItemsControl parent,HashSet<string> expanded){
    foreach(var child in parent.Items){
      var node=child as TreeViewItem;
      if(node==null)continue;
      var path=node.Tag as string;
      if(node.IsExpanded && path!=null)expanded.Add(path);
      RememberExpanded(node,expanded);
    }
  }
  static TreeViewItem FindProjectTreeNode(ItemsControl parent,string fullPath){
    foreach(var child in parent.Items){
      var node=child as TreeViewItem;
      if(node==null)continue;
      if(String.Equals(node.Tag as string,fullPath,StringComparison.OrdinalIgnoreCase))return node;
      var nested=FindProjectTreeNode(node,fullPath);
      if(nested!=null)return nested;
    }
    return null;
  }
  void RefreshProjectTree(){
    // Preserve all open explorer branches and the selected folder across
    // delete/backup/rename/build. Never return to Engine unprompted.
    var expanded=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    RememberExpanded(projectTree,expanded);
    string restore=projectTreeFocusAfterRefresh??SelectedProjectPath();
    projectTreeFocusAfterRefresh=null;
    projectTree.Items.Clear();
    if(project==null){UpdateBuildState();return;}
    var rootItem=new TreeViewItem{Header=project.Name,Tag=project.Files,FontWeight=FontWeights.SemiBold,ToolTip=project.FilePath};
    projectTree.Items.Add(rootItem);
    PopulateProjectNodes(rootItem,project.Files,0,expanded);
    rootItem.IsExpanded=true;
    if(!String.IsNullOrWhiteSpace(restore)){
      string test=restore;
      TreeViewItem target=null;
      while(!String.IsNullOrWhiteSpace(test)){
        target=FindProjectTreeNode(projectTree,test);
        if(target!=null)break;
        test=Path.GetDirectoryName(test);
      }
      if(target!=null){
        for(var parent=target.Parent as TreeViewItem;parent!=null;parent=parent.Parent as TreeViewItem)parent.IsExpanded=true;
        target.IsSelected=true;
      }
    }
    UpdateBuildState();
    modToolsPane?.NotifyProjectChanged();
    liveEnvPane?.NotifyProjectChanged();
  }
  void PopulateProjectNodes(TreeViewItem parent,string path,int depth,HashSet<string> expanded){
    if(depth>8)return;
    try{
      foreach(var dir in Directory.GetDirectories(path).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase)){
        var node=new TreeViewItem{Header=Path.GetFileName(dir),Tag=dir,ToolTip=dir};
        parent.Items.Add(node);
        PopulateProjectNodes(node,dir,depth+1,expanded);
        node.IsExpanded=expanded.Contains(dir) || depth<1;
      }
      foreach(var file in Directory.GetFiles(path).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        parent.Items.Add(new TreeViewItem{Header=Path.GetFileName(file),Tag=file,ToolTip=file});
    }catch(Exception ex){Log("Project tree: "+ex.Message);}
  }
  string SelectedProjectPath()=>((projectTree.SelectedItem as TreeViewItem)?.Tag as string);
  string SelectedProjectFolder(){
    if(project==null)return null;
    var path=SelectedProjectPath();
    if(Directory.Exists(path))return path;
    if(File.Exists(path))return Path.GetDirectoryName(path);
    return project.AssetImportRoot;
  }
  string JsonRawFolder(){return project==null?null:project.RawFiles;}
  string JsonFilesFolder(){return project==null?null:project.Files;}
  string JsonBackupFolder(){
    return project==null?Path.Combine(EnvironmentPaths.AppDataRoot,"backups","json_roundtrip"):
      Path.Combine(project.Workspace,"backups","json_roundtrip");
  }
  void ExportSelectedJson(){
    var path=SelectedProjectPath();
    if(File.Exists(path))ExportJson(path);
    else MessageBox.Show("Select a file to convert to JSON in the project tree.","JSON ↔ RAW");
  }
  void CompileSelectedJson(){
    var path=SelectedProjectPath();
    if(File.Exists(path))CompileJson(path);
    else MessageBox.Show("Select the original file or its JSON sidecar.","JSON ↔ RAW");
  }
  void ChooseRoundtripFile(){
    var dlg=new OpenFileDialog{
      Title="Choose a RAW resource to export or its .json to compile",
      Filter="Resources and JSON|*.json;*.env;*.w2w;*.w2ent;*.w2mesh;*.w2mi;*.w2mg;*.w2rig;*.w2anims;*.w2cutscene;*.w2scene;*.ws;*.xml;*.csv;*.ini;*.txt;*.yaml;*.yml;*.md;*.bat;*.cfg|All files|*.*"
    };
    if(project!=null && Directory.Exists(project.RawFiles))dlg.InitialDirectory=project.RawFiles;
    if(dlg.ShowDialog()!=true)return;
    if(dlg.FileName.EndsWith(".json",StringComparison.OrdinalIgnoreCase))CompileJson(dlg.FileName);
    else ExportJson(dlg.FileName);
  }
  async void ExportJson(string path){
    if(!RoundtripJson.CanExport(path)){
      MessageBox.Show("This resource type has no supported JSON roundtrip adapter. Only verified text and CR2W resource formats are enabled.","JSON export",MessageBoxButton.OK,MessageBoxImage.Warning);
      return;
    }
    if(sessions.Any(d=>String.Equals(d.FilePath,path,StringComparison.OrdinalIgnoreCase)&&d.Dirty)){
      MessageBox.Show("Save changes in the resource editor before exporting JSON.","Unsaved edits",MessageBoxButton.OK,MessageBoxImage.Warning);
      return;
    }
    string output=RoundtripJson.JsonPath(path,JsonFilesFolder(),JsonRawFolder());
    if(File.Exists(output)){
      MessageBox.Show("An editable JSON already exists. To protect your edits it was not overwritten:\n"+output,
        "JSON exists",MessageBoxButton.OK,MessageBoxImage.Information);return;
    }
    try{
      SetStatus("Exporting editable JSON: "+Path.GetFileName(path));
      var result=await Task.Run(()=>RoundtripJson.Export(path,output));
      Log("JSON roundtrip EXPORT: "+result.Report+"\n"+result.JsonPath);
      SetStatus("JSON EXPORT OK — "+result.Fields+" typed fields");
      RefreshProjectTree();
      MessageBox.Show(result.Report+"\n\nJSON saved:\n"+result.JsonPath+
        "\n\nEdit only supported values; the original RAW file remains untouched. "+
        "After editing, use the JSON's context menu to compile back.", "JSON export",
        MessageBoxButton.OK,MessageBoxImage.Information);
    }catch(Exception ex){Program.LogException(ex);Log("JSON EXPORT ERROR: "+ex);
      SetStatus("JSON export failed");MessageBox.Show(ex.Message,"JSON export rejected",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  async void CompileJson(string selected){
    bool isJson=selected.EndsWith(".json",StringComparison.OrdinalIgnoreCase);
    string json=isJson?selected:RoundtripJson.JsonPath(selected,JsonFilesFolder(),JsonRawFolder());
    string original=isJson?RoundtripJson.SourcePath(selected,JsonFilesFolder(),JsonRawFolder()):selected;
    if(!File.Exists(original)||!File.Exists(json)){
      MessageBox.Show("Both original RAW file and JSON sidecar are required.\nRAW: "+original+"\nJSON: "+json,
        "JSON compile",MessageBoxButton.OK,MessageBoxImage.Warning);return;
    }
    if(sessions.Any(d=>d.Dirty&&
      (String.Equals(d.FilePath,original,StringComparison.OrdinalIgnoreCase)||
       String.Equals(d.FilePath,json,StringComparison.OrdinalIgnoreCase)))){
      MessageBox.Show("Save and close unsaved edits in the original or JSON editor before compiling.",
        "Unsaved edits",MessageBoxButton.OK,MessageBoxImage.Warning);return;
    }
    try{
      SetStatus("Validating JSON against original RAW...");
      string backups=JsonBackupFolder();
      var preview=await Task.Run(()=>RoundtripJson.Import(original,json,backups,false));
      if(preview.Changed==0){
        SetStatus("JSON already matches original RAW");MessageBox.Show("No changes to compile. The original remains byte-identical.",
          "JSON compile",MessageBoxButton.OK,MessageBoxImage.Information);return;
      }
      if(MessageBox.Show(preview.Report+"\n\nSource: "+original+
        "\n\nCreate a backup and compile these changes into the ORIGINAL RAW file? "+
        "Unrepresented binary blocks will be copied from the original.",
        "Confirm JSON → original",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes){
        SetStatus("JSON compile canceled; original untouched");return;
      }
      SetStatus("Compiling and validating CR2W/RAW conversion...");
      var result=await Task.Run(()=>RoundtripJson.Import(original,json,backups,true));
      Log("JSON roundtrip IMPORT: "+result.Report);
      SetStatus("JSON COMPILED — "+result.Changed+" changes; backup created");
      MessageBox.Show(result.Report+"\n\nThe original is ready for the normal project BUILD operation.",
        "Roundtrip successful",MessageBoxButton.OK,MessageBoxImage.Information);
      RefreshProjectTree();
    }catch(Exception ex){Program.LogException(ex);Log("JSON COMPILE ERROR: "+ex);
      SetStatus("JSON compile failed; check original SHA");MessageBox.Show(ex.Message,
        "JSON compile rejected",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  void OpenSelectedProjectFile(){var file=SelectedProjectPath();if(File.Exists(file))OpenDocument(file,true);}
  void OpenSelectedProjectFileNative(){var file=SelectedProjectPath();if(File.Exists(file))OpenDocument(file,true);}
  void AddExternalFiles(){
    if(project==null){MessageBox.Show("Open a project first.");return;}
    var dlg=new OpenFileDialog{Multiselect=true,Filter="All resource files|*.*"};
    if(dlg.ShowDialog()==true){
      foreach(var file in dlg.FileNames){
        try{CopyIntoProject(file,SelectedProjectFolder());}catch(Exception ex){Log("Add file: "+ex.Message);}
      }
      RefreshProjectTree();
    }
  }
  void CopyIntoProject(string src,string folder){
    if(!Directory.Exists(folder))Directory.CreateDirectory(folder);
    var dest=Path.Combine(folder,Path.GetFileName(src.TrimEnd('\\')));
    if(Directory.Exists(src)){
      if(Directory.Exists(dest))throw new IOException("Folder already exists: "+dest);
      Deployment.CopyFolder(src,dest);
    }else{
      if(File.Exists(dest))throw new IOException("File already exists: "+dest);
      File.Copy(src,dest);
    }
    Log("Added project file: "+dest);
  }
  void NewProjectFolder(){
    if(project==null)return;
    var d=new TextPrompt("New folder","Folder name","NewFolder"){Owner=this};
    if(d.ShowDialog()!=true||d.Value.Length==0)return;
    try{Directory.CreateDirectory(Path.Combine(SelectedProjectFolder(),d.Value));RefreshProjectTree();}
    catch(Exception ex){MessageBox.Show(ex.Message);}
  }
  void CopyProjectItem(bool cut){
    var p=SelectedProjectPath();if(String.IsNullOrWhiteSpace(p))return;
    pendingCopy=p;pendingCut=cut?p:null;
    var sc=new StringCollection();sc.Add(p);
    try{System.Windows.Forms.Clipboard.SetFileDropList(sc);}catch{}
    SetStatus((cut?"Cut: ":"Copied: ")+p);
  }
  void PasteProjectItem(){
    if(project==null)return;
    var dest=SelectedProjectFolder();
    var source=pendingCopy;
    if(String.IsNullOrWhiteSpace(source)){
      try{
        var files=System.Windows.Forms.Clipboard.GetFileDropList();
        if(files.Count>0)source=files[0];
      }catch{}
    }
    if(String.IsNullOrWhiteSpace(source))return;
    try{
      if(pendingCut!=null&&source==pendingCut){
        var target=Path.Combine(dest,Path.GetFileName(source.TrimEnd('\\')));
        if(File.Exists(target)||Directory.Exists(target))throw new IOException("Destination already exists.");
        if(Directory.Exists(source))Directory.Move(source,target);else File.Move(source,target);
        pendingCopy=null;pendingCut=null;
      }else CopyIntoProject(source,dest);
      RefreshProjectTree();
    }catch(Exception ex){MessageBox.Show(ex.Message,"Paste",MessageBoxButton.OK,MessageBoxImage.Error);}
  }
  void RenameProjectItem(){
    var path=SelectedProjectPath();if(String.IsNullOrWhiteSpace(path)||project==null)return;
    if(path.Equals(project.Files,StringComparison.OrdinalIgnoreCase))return;
    var d=new TextPrompt("Rename resource","New filename",Path.GetFileName(path)){Owner=this};
    if(d.ShowDialog()!=true||d.Value.Length==0)return;
    try{
      var target=Path.Combine(Path.GetDirectoryName(path),d.Value);
      if(File.Exists(target)||Directory.Exists(target))throw new IOException("Destination exists.");
      if(Directory.Exists(path))Directory.Move(path,target);else File.Move(path,target);
      projectTreeFocusAfterRefresh=target;
      RefreshProjectTree();Log("Renamed "+path+" -> "+target);
    }catch(Exception ex){MessageBox.Show(ex.Message);}
  }
  void DeleteProjectItem(){
    var path=SelectedProjectPath();
    if(String.IsNullOrWhiteSpace(path)||project==null||path.Equals(project.Files,StringComparison.OrdinalIgnoreCase))return;
    if(MessageBox.Show("Delete this project item?\n"+path,"Confirm delete",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
    try{
      if(File.Exists(path))File.Delete(path);
      else if(Directory.Exists(path))Directory.Delete(path,true);
      projectTreeFocusAfterRefresh=Path.GetDirectoryName(path);
      RefreshProjectTree();
    }catch(Exception ex){MessageBox.Show(ex.Message);}

  }
  void ShowInExplorer(){
    var path=SelectedProjectPath();
    if(String.IsNullOrWhiteSpace(path))return;
    Process.Start("explorer.exe",File.Exists(path)?"/select,\""+path+"\"":"\""+path+"\"");
  }
  void SaveCurrent(){
    var tab=docs?.SelectedItem as TabItem;
    if(Object.ReferenceEquals(tab,envStudioTab) && liveEnvPane!=null){
      try{
        liveEnvPane.SaveCurrent();
        project?.Save();
      }catch(Exception ex){
        Program.LogException(ex);
        MessageBox.Show(ex.Message,"Save ENV",MessageBoxButton.OK,MessageBoxImage.Error);
      }
      return;
    }
    var session=sessions.FirstOrDefault(x=>Object.ReferenceEquals(x.Tab,tab));
    if(session==null){
      project?.Save();
      SetStatus("No editable file is selected.");
      return;
    }
    try{
      session.Save?.Invoke();
      project?.Save();
      SetStatus("SAVED  •  "+Path.GetFileName(session.FilePath));
      Log("Saved current file: "+session.FilePath);
    }catch(Exception ex){
      Program.LogException(ex);
      MessageBox.Show(ex.Message,"Save resource",MessageBoxButton.OK,MessageBoxImage.Error);
    }
  }
  void SaveAll(){
    int saved=0;
    foreach(var session in sessions.ToArray()){
      try{
        session.Save?.Invoke();
        saved++;
      }catch(Exception ex){
        Program.LogException(ex);
        MessageBox.Show(ex.Message,"Save resource",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }
    project?.Save();
    SetStatus("SAVE ALL  •  "+saved+" file"+(saved==1?"":"s"));
    Log("Save all completed: "+saved+" open files.");
  }

  bool HasBuildableResources(){
    return project!=null&&project.HasBuildableResources;
  }

  bool HasBuiltPackage(){
    if(project==null)return false;
    try{
      var content=Path.Combine(Deployment.OutputModFolder(project),"content");
      return Directory.Exists(content)
        && File.Exists(Path.Combine(content,"blob0.bundle"))
        && File.Exists(Path.Combine(content,"metadata.store"));
    }catch{return false;}
  }

  void UpdateBuildState(){
    if(buildOnly==null||buildInstall==null||installOnly==null)return;
    bool hasProject=project!=null;
    bool hasResources=HasBuildableResources();
    bool hasBuiltPackage=HasBuiltPackage();

    bool canBuild=hasProject&&hasResources&&!working;
    bool canInstall=hasProject&&hasBuiltPackage&&!working;

    buildOnly.IsEnabled=canBuild;
    buildInstall.IsEnabled=canBuild;
    installOnly.IsEnabled=canInstall;

    string buildTip=!hasProject
      ?"Open or create a project before building."
      :!hasResources
        ?"Add at least one resource to the project before building."
        :working
          ?"Build in progress..."
          :"Build the current project with the native REDengine packer.";

    string installTip=!hasProject
      ?"Open or create a project first."
      :!hasBuiltPackage
        ?"No built package found. Build the project first."
        :working
          ?"Another operation is in progress..."
          :"Install the existing built package without rebuilding it.";

    buildOnly.ToolTip=buildTip;
    buildInstall.ToolTip=buildTip;
    installOnly.ToolTip=installTip;
  }

  async Task RunBuildAsync(bool install){
    if(working)return;
    if(project==null){
      SetStatus("Open or create a project before building.");
      return;
    }
    if(!HasBuildableResources()){
      docs.SelectedItem=browserTab;
      SetStatus("Project is empty. Select game assets and choose Add to Project before building.");
      UpdateBuildState();
      return;
    }

    working=true;UpdateBuildState();
    ExpandOutput();SetStatus(install?"Building and installing...":"Building...");
    try{
      SaveAll();
      await Deployment.BuildAsync(project,Log);
      if(install){
        var dest=await Deployment.InstallAsync(project,gamePath.Text.Trim(),Log);
        SetStatus("INSTALLED SUCCESSFULLY: "+dest);
        MessageBox.Show("Build installed successfully:\n"+dest,"W3 Forge Studio",MessageBoxButton.OK,MessageBoxImage.Information);
      }else SetStatus("BUILD COMPLETE: "+Deployment.OutputModFolder(project));
      UpdateInstallState();
    }catch(Exception ex){
      Program.LogException(ex);
      Log("Build failed: "+ex.Message);
      SetStatus("Build/install failed");
      MessageBox.Show(ex.Message,install?"Build & Install":"Build",MessageBoxButton.OK,MessageBoxImage.Error);
    }
    finally{working=false;UpdateBuildState();}
  }
  async Task InstallBuiltAsync(){
    if(working)return;
    if(project==null){
      SetStatus("Open or create a project before installing.");
      return;
    }
    if(!HasBuiltPackage()){
      SetStatus("No built package found. Build the project first.");
      UpdateBuildState();
      return;
    }

    working=true;
    UpdateBuildState();
    ExpandOutput();
    SetStatus("Installing existing build...");
    try{
      var dest=await Deployment.InstallAsync(project,gamePath.Text.Trim(),Log);
      SetStatus("INSTALLED SUCCESSFULLY: "+dest);
      UpdateInstallState();
      MessageBox.Show("Mod installed successfully:\n"+dest,"W3 Forge Studio",MessageBoxButton.OK,MessageBoxImage.Information);
    }catch(Exception ex){
      Program.LogException(ex);
      Log("Install failed: "+ex.Message);
      SetStatus("Install failed");
      MessageBox.Show(ex.Message,"Install",MessageBoxButton.OK,MessageBoxImage.Error);
    }finally{
      working=false;
      UpdateBuildState();
    }
  }

  async Task UninstallAsync(){
    if(project==null){MessageBox.Show("Open a project first.");return;}
    var path=Deployment.LiveModFolder(project,gamePath.Text.Trim());
    if(MessageBox.Show("Remove this project's mod from the game?\n\n"+path+"\n\nA backup will be saved first.","Uninstall",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
    try{
      ExpandOutput();
      bool removed=await Deployment.UninstallAsync(project,gamePath.Text.Trim(),Log);
      SetStatus(removed?"Mod uninstalled; backup retained":"No installed mod found.");
      UpdateInstallState();
    }catch(Exception ex){MessageBox.Show(ex.Message);}
  }
  void UpdateInstallState(){
    bool installed=false;
    if(project!=null)try{installed=Directory.Exists(Deployment.LiveModFolder(project,gamePath.Text.Trim()));}catch{}
    installState.Text=project==null?"NO PROJECT":installed?"●  INSTALLED":"○  NOT INSTALLED";
    installState.Foreground=Ui.Brush(installed?"#65C693":"#B9A6A8");
  }
  void SetAssetFilter(string ext){
    extensionFilter=ext??"";
    UpdateFilterButtons();
    RefreshAssets();
  }
  void UpdateFilterButtons(){
    foreach(var button in filterButtons){
      string ext=button.Tag as string??"";
      bool active=String.Equals(ext,extensionFilter,StringComparison.OrdinalIgnoreCase);
      button.Background=Ui.Brush(active?"#3B2429":"#00000000");
      button.BorderBrush=Ui.Brush(active?"#9B3841":"#00000000");
      button.Foreground=Ui.Brush(active?"#FFFFFF":"#A6AFBA");
    }
  }

  void Settings(){
    var d=new SettingsDialog(prefs){Owner=this};
    if(d.ShowDialog()==true){
      if(d.GameBin.Length>0){gamePath.Text=d.GameBin;prefs.GameBin=d.GameBin;}
      prefs.Save();
      if(project!=null){
        project.GameBin=prefs.GameBin;
        project.Save();
      }
      SetStatus("Tool paths updated.");
      UpdateInstallState();
    }
  }
  void BrowseGame(){
    using(var dlg=new System.Windows.Forms.FolderBrowserDialog()){
      dlg.Description="Choose The Witcher 3 bin\\x64_dx12 folder";
      if(Directory.Exists(gamePath.Text))dlg.SelectedPath=gamePath.Text;
      if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){gamePath.Text=dlg.SelectedPath;prefs.GameBin=dlg.SelectedPath;prefs.Save();}
    }
  }
  void ToggleOutput(){
    var height=docs.Parent as Grid;
    if(height==null)return;
    var mid=(Grid)docs.Parent;
    mid.RowDefinitions[2].Height=mid.RowDefinitions[2].Height.Value==0?Ui.Px(155):Ui.Px(0);
  }
  void ExpandOutput(){
    var mid=docs.Parent as Grid;
    if(mid!=null)mid.RowDefinitions[2].Height=Ui.Px(155);
  }
  void SetStatus(string message){if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>SetStatus(message)));return;}footerStatus.Text=message;}
  void Log(string message){
    if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Log(message)));return;}
    if(console.Text.Length>200000)console.Text=console.Text.Substring(console.Text.Length-130000);
    console.AppendText("["+DateTime.Now.ToString("HH:mm:ss")+"] "+message+"\n");
    console.ScrollToEnd();
  }
  System.Windows.Controls.Image LoadBrandImage(string name,double width,double height){
    try{
      string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets",name);
      if(!File.Exists(path))return null;
      var bmp=new System.Windows.Media.Imaging.BitmapImage();
      bmp.BeginInit();bmp.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;bmp.UriSource=new Uri(path,UriKind.Absolute);bmp.EndInit();bmp.Freeze();
      return new System.Windows.Controls.Image{Source=bmp,Width=width,Height=height,Stretch=Stretch.Uniform,SnapsToDevicePixels=true};
    }catch(Exception ex){Log("Brand asset: "+ex.Message);return null;}
  }

  void MainKeys(object sender,KeyEventArgs e){
    if((Keyboard.Modifiers&ModifierKeys.Control)==ModifierKeys.Control){
      if(e.Key==Key.S){
        if((Keyboard.Modifiers&ModifierKeys.Shift)==ModifierKeys.Shift)SaveAll();
        else SaveCurrent();
        e.Handled=true;
      }
      if(e.Key==Key.O){ChooseFileToOpen();e.Handled=true;}
      if(e.Key==Key.B){_ = RunBuildAsync(false);e.Handled=true;}
      if(e.Key==Key.F){search.Focus();search.SelectAll();e.Handled=true;}
    }
  }
 }
}