using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace W3Forge {
  public sealed class LiveEnvPane : Grid {
    readonly Func<Project> getProject;
    readonly Func<string> getGameBin;
    readonly Action<string> log;
    readonly Action<string> status;

    TextBox search;
    ListView parameters,points;
    TextBlock state,selectedName,bridgeState;
    TextBox valueBox,timeBox,xBox,yBox,zBox,wBox,hourBox,valueBoxCurve;
    CheckBox boolBox;
    ComboBox consoleKey;
    StackPanel scalarEditor,curveEditor;
    Button save,apply,applyLive,loadLive,installBridge,removeBridge,copyLive,liveLabToggle,exportPreview;
    StackPanel liveLab;
    bool liveLabEnabled;

    List<EnvParameter> visible=new List<EnvParameter>();
    EnvDocument document;
    EnvParameter selected;
    bool dirty;

    public LiveEnvPane(Func<Project> getProject,Func<string> getGameBin,Action<string> log,Action<string> status) {
      this.getProject=getProject;this.getGameBin=getGameBin;this.log=log;this.status=status;
      Build();
    }

    void Build() {
      Background=Ui.Brush("#191C20");
      Ui.Rows(this,Ui.Px(62),Ui.Star(),GridLength.Auto);

      var top=new Grid{Margin=new Thickness(15,9,15,8)};
      Ui.Cols(top,Ui.Px(105),Ui.Star());
      var title=Ui.Text("ENV STUDIO",10,"#9EA7B3");title.FontWeight=FontWeights.Bold;Ui.Put(top,title,0);
      bridgeState=Ui.Text("Binary ENV editor  •  curves, names and resource/LUT references",11,"#8F98A4");Ui.Put(top,bridgeState,0,1);
      // Live ENV Lab requires a deliberate opt-in. The bridge is experimental:
      // offline command generation can be verified, but game-side execution
      // must be validated by the user on Witcher 3 Next-Gen.
      consoleKey=new ComboBox{Width=134,Margin=new Thickness(6,0,10,0)};
      consoleKey.Items.Add("Apostrophe (BR)");consoleKey.Items.Add("Tilde");consoleKey.Items.Add("F2");consoleKey.SelectedIndex=0;
      installBridge=Ui.Button("INSTALL BRIDGE",InstallBridge,"ToolbarButton");
      removeBridge=Ui.Button("REMOVE BRIDGE",RemoveBridge,"FlatButton");
      Ui.Put(this,top,0);

      var body=new Grid();
      // ENV Studio is contextual to the currently selected/open ENV tab.
      // It intentionally has NO project-wide ENV browser of its own.
      Ui.Cols(body,Ui.Star(),Ui.Px(4),Ui.Px(390));

      var center=new Grid();Ui.Rows(center,Ui.Px(52),Ui.Star());
      var find=new Grid{Margin=new Thickness(12,8,12,7)};
      Ui.Cols(find,Ui.Star(),Ui.Px(160));
      search=new TextBox{
        FontSize=12,Margin=new Thickness(0,0,10,0),
        Background=Ui.Brush("#171B20"),Foreground=Ui.Brush("#F4F6F8"),
        BorderBrush=Ui.Brush("#353D47")
      };
      search.ToolTip="Search group or ENV parameter.";
      search.TextChanged+=(s,e)=>RefreshParameters();
      Ui.Put(find,search,0,0);
      state=Ui.Text("Open a project with ENV resources",10.5,"#88919D");
      state.HorizontalAlignment=HorizontalAlignment.Right;Ui.Put(find,state,0,1);
      Ui.Put(center,find,0);

      parameters=new ListView{
        Margin=new Thickness(8,0,8,8),SelectionMode=SelectionMode.Single,
        Background=Ui.Brush("#13161A"),Foreground=Ui.Brush("#E6EAF0"),
        BorderBrush=Ui.Brush("#303741"),BorderThickness=new Thickness(1)
      };
      var pv=new GridView();
      pv.Columns.Add(new GridViewColumn{Header="GROUP",DisplayMemberBinding=new Binding("Group"),Width=145});
      pv.Columns.Add(new GridViewColumn{Header="PARAMETER",DisplayMemberBinding=new Binding("Path"),Width=300});
      pv.Columns.Add(new GridViewColumn{Header="TYPE",DisplayMemberBinding=new Binding("KindText"),Width=92});
      pv.Columns.Add(new GridViewColumn{Header="VALUE / RESOURCE REFERENCE",DisplayMemberBinding=new Binding("DisplayValue"),Width=340});
      parameters.View=pv;
      parameters.SelectionChanged+=(s,e)=>SelectParameter();
      Ui.Put(center,parameters,1);
      Ui.Put(body,center,0,0);

      Ui.Put(body,new Border{Background=Ui.Brush("#383D45")},0,1);
      Ui.Put(body,BuildEditor(),0,2);
      Ui.Put(this,body,1);

      var footer=Ui.Box("#20242A");
      var actions=Ui.H();actions.Margin=new Thickness(13,0,13,0);
      save=Ui.Button("SAVE ENV",SaveEnv,"PrimaryButton");save.IsEnabled=false;
      actions.Children.Add(save);
      actions.Children.Add(Ui.Button("SMART TRANSFER / ORDER",OpenSmartWorkflow,"ToolbarButton"));
      actions.Children.Add(Ui.Button("RELOAD FROM DISK",ReloadEnv,"ToolbarButton"));
      liveLabToggle=Ui.Button("LIVE ENV LAB  [EXPERIMENTAL]",ToggleLiveLab,"ToolbarButton");
      // The unverified console bridge MUST NOT be exposed in public builds.
      // Only the safely validated JSON -> ENV importer remains visible.
      // actions.Children.Add(liveLabToggle);
      // Former in-game JSON import is deprecated; JSON ↔ RAW lives in project context menu.
      actions.Children.Add(Ui.Button("COPY ENV PATH",CopyEnvPath,"FlatButton"));
      actions.Children.Add(Ui.Separator(true));
      actions.Children.Add(Ui.Text("SAVE ENV always creates a timestamped backup and validates the serialized CR2W before replacing the file.",10,"#7F8996"));
      loadLive=Ui.Button("LOAD ENV LIVE",LoadLive,"ToolbarButton");
      applyLive=Ui.Button("SEND SELECTED PARAMETER",ApplyLive,"ToolbarButton");
      copyLive=Ui.Button("COPY LIVE COMMAND",CopyLiveCommand,"FlatButton");
      hourBox=new TextBox{Text="17.0",Width=64,Margin=new Thickness(6,0,10,0),
        Background=Ui.Brush("#171B20"),Foreground=Ui.Brush("#F4F6F8")};
      liveLab=Ui.V();
      liveLab.Visibility=Visibility.Collapsed;
      liveLab.Margin=new Thickness(13,3,13,9);
      var warning=Ui.Text("EXPERIMENTAL WITCHERSCRIPT BRIDGE — not yet game-validated. Uses the in-game debug console. Commands are unconfirmed until you see the effect. SAVE ENV persists edits only to project files; build/install the mod separately.",10,"#E5B17A");
      warning.TextWrapping=TextWrapping.Wrap;
      liveLab.Children.Add(warning);
      var labRow=Ui.H();labRow.Margin=new Thickness(0,6,0,4);
      exportPreview=Ui.Button("EXPORT BRIDGE SCRIPT (OFFLINE)",ExportBridgePreview,"ToolbarButton");
      labRow.Children.Add(exportPreview);labRow.Children.Add(installBridge);labRow.Children.Add(removeBridge);
      labRow.Children.Add(Ui.Separator(true));
      labRow.Children.Add(loadLive);labRow.Children.Add(applyLive);labRow.Children.Add(copyLive);
      liveLab.Children.Add(labRow);
      var labTime=Ui.H();
      labTime.Children.Add(Ui.Text("Console key:",10,"#9CA6B5"));labTime.Children.Add(consoleKey);
      labTime.Children.Add(Ui.Text("Game hour (0-24):",10,"#9CA6B5"));labTime.Children.Add(hourBox);
      labTime.Children.Add(Ui.Button("FREEZE TIME",FreezeTime,"ToolbarButton"));
      labTime.Children.Add(Ui.Button("RELEASE TIME",ReleaseTime,"FlatButton"));
      labTime.Children.Add(Ui.Text("A bridge install modifies game script files and general.ini only after confirmation; restart game to compile scripts.",10,"#8F98A4"));
      liveLab.Children.Add(labTime);
      var footerStack=Ui.V();footerStack.Children.Add(actions);footerStack.Children.Add(liveLab);
      footer.Child=footerStack;Ui.Put(this,footer,2);

      ClearContext();
    }

    Border BuildEditor() {
      var wrap=Ui.Box("#171A1E");
      var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
      var stack=Ui.V();stack.Margin=new Thickness(16,14,16,14);
      selectedName=Ui.Text("Select an ENV parameter",12,"#E9EDF2");
      selectedName.FontWeight=FontWeights.SemiBold;selectedName.TextWrapping=TextWrapping.Wrap;
      stack.Children.Add(selectedName);
      stack.Children.Add(Ui.Separator());

      scalarEditor=Ui.V();
      scalarEditor.Children.Add(Ui.Text("VALUE",9,"#7F8996"));
      valueBox=new TextBox{
        Margin=new Thickness(0,6,0,8),
        FontFamily=new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=12,
        Background=Ui.Brush("#171B20"),Foreground=Ui.Brush("#F4F6F8"),
        BorderBrush=Ui.Brush("#353D47")
      };
      scalarEditor.Children.Add(valueBox);
      var refActions=Ui.H();
      var pasteRef=Ui.Button("PASTE REFERENCE",PasteReference,"FlatButton");
      var copyRef=Ui.Button("COPY REFERENCE",CopyReference,"FlatButton");
      var browseRef=Ui.Button("BROWSE XBM",BrowseReference,"FlatButton");
      refActions.Children.Add(pasteRef);refActions.Children.Add(copyRef);refActions.Children.Add(browseRef);
      refActions.Tag="REFERENCE_ACTIONS";
      scalarEditor.Children.Add(refActions);
      var rawActions=Ui.H();
      rawActions.Children.Add(Ui.Button("IMPORT SAME BLOCK FROM ENV",ImportRawBlock,"ToolbarButton"));
      rawActions.Children.Add(Ui.Button("EXPORT RAW BLOCK",ExportRawBlock,"FlatButton"));
      rawActions.Tag="RAW_ACTIONS";
      scalarEditor.Children.Add(rawActions);
      boolBox=new CheckBox{Content="Enabled / true",Margin=new Thickness(0,5,0,9),Foreground=Ui.Brush("#DCE1E8")};
      scalarEditor.Children.Add(boolBox);
      apply=Ui.Button("APPLY TO ENV",()=>ApplyLocal(true),"ToolbarButton");scalarEditor.Children.Add(apply);
      stack.Children.Add(scalarEditor);

      curveEditor=Ui.V();
      curveEditor.Children.Add(Ui.Text("CURVE POINTS",9,"#7F8996"));
      points=new ListView{
        Height=170,Margin=new Thickness(0,6,0,10),SelectionMode=SelectionMode.Single,
        Background=Ui.Brush("#13161A"),Foreground=Ui.Brush("#E6EAF0"),
        BorderBrush=Ui.Brush("#303741"),BorderThickness=new Thickness(1)
      };
      var cv=new GridView();
      cv.Columns.Add(new GridViewColumn{Header="#",DisplayMemberBinding=new Binding("Index"),Width=34});
      cv.Columns.Add(new GridViewColumn{Header="TIME",DisplayMemberBinding=new Binding("TimeText"),Width=82});
      cv.Columns.Add(new GridViewColumn{Header="SCALAR",DisplayMemberBinding=new Binding("ValueText"),Width=95});
      points.View=cv;points.SelectionChanged+=(s,e)=>LoadPoint();
      curveEditor.Children.Add(points);

      var grid=new Grid();Ui.Cols(grid,Ui.Star(),Ui.Star());Ui.Rows(grid,Ui.Px(52),Ui.Px(52),Ui.Px(52));
      timeBox=Field(grid,"Time",0,0);
      valueBoxCurve=Field(grid,"Scalar",0,1);
      xBox=Field(grid,"X / R",1,0);yBox=Field(grid,"Y / G",1,1);
      zBox=Field(grid,"Z / B",2,0);wBox=Field(grid,"W / A",2,1);
      curveEditor.Children.Add(grid);
      curveEditor.Children.Add(Ui.Button("APPLY CURVE POINT",()=>ApplyLocal(true),"ToolbarButton"));
      stack.Children.Add(curveEditor);

      var note=Ui.Text("Edits stay in memory until SAVE ENV. Resource/LUT references accept REDengine depot paths (for example environment\\definitions\\...\\lut.xbm). Every save creates a backup and validates the ENV before commit.",10,"#7F8996");
      note.Margin=new Thickness(0,18,0,0);note.TextWrapping=TextWrapping.Wrap;note.TextTrimming=TextTrimming.None;
      stack.Children.Add(note);
      scroll.Content=stack;wrap.Child=scroll;
      scalarEditor.Visibility=curveEditor.Visibility=Visibility.Collapsed;
      return wrap;
    }

    TextBox Field(Grid grid,string label,int row,int col) {
      var host=Ui.V();host.Margin=new Thickness(col==0?0:5,0,col==0?5:0,4);
      host.Children.Add(Ui.Text(label,9,"#7F8996"));
      var box=new TextBox{
        Margin=new Thickness(0,4,0,0),
        FontFamily=new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),FontSize=11.5,
        Background=Ui.Brush("#171B20"),Foreground=Ui.Brush("#F4F6F8"),
        BorderBrush=Ui.Brush("#353D47")
      };
      host.Children.Add(box);Ui.Put(grid,host,row,col);return box;
    }

    public string CurrentFile { get { return document==null?null:document.FilePath; } }
    public bool HasDirtyChanges { get { return dirty; } }

    public void NotifyProjectChanged() {
      if(document!=null && !File.Exists(document.FilePath))ClearContext();
      UpdateActions();
    }

    public void SetPendingContext(string file) {
      if(String.IsNullOrWhiteSpace(file)) {
        bridgeState.Text="Select an open .env document to enable ENV Studio.";
        return;
      }
      bridgeState.Text="Selected ENV  •  "+Path.GetFileName(file)+"  •  click ENV STUDIO to edit this file";
      bridgeState.Foreground=Ui.Brush("#AEB7C2");
    }

    public void ClearContext() {
      document=null;selected=null;dirty=false;
      visible.Clear();
      if(parameters!=null)parameters.ItemsSource=null;
      if(points!=null)points.ItemsSource=null;
      if(scalarEditor!=null)scalarEditor.Visibility=Visibility.Collapsed;
      if(curveEditor!=null)curveEditor.Visibility=Visibility.Collapsed;
      if(selectedName!=null)selectedName.Text="Select an ENV parameter";
      if(state!=null)state.Text="ENV Studio follows the currently selected open .env tab";
      if(bridgeState!=null)bridgeState.Text="Select an open .env document to enable ENV Studio.";
      UpdateActions();
    }

    public bool OpenFile(string file) {
      if(String.IsNullOrWhiteSpace(file))return false;
      string full;
      try{full=Path.GetFullPath(file);}catch{return false;}
      if(!File.Exists(full)||!String.Equals(Path.GetExtension(full),".env",StringComparison.OrdinalIgnoreCase))return false;

      if(document!=null && String.Equals(Path.GetFullPath(document.FilePath),full,StringComparison.OrdinalIgnoreCase)) {
        bridgeState.Text="Editing selected ENV  •  "+Path.GetFileName(full);
        UpdateActions();
        return true;
      }

      if(dirty&&document!=null) {
        var r=MessageBox.Show(
          "ENV Studio has unsaved changes in:\n"+Path.GetFileName(document.FilePath)+
          "\n\nSave them before switching to:\n"+Path.GetFileName(full)+" ?",
          "Switch ENV Studio context",MessageBoxButton.YesNoCancel,MessageBoxImage.Warning);
        if(r==MessageBoxResult.Cancel)return false;
        if(r==MessageBoxResult.Yes) {
          SaveEnv();
          if(dirty)return false;
        }
      }

      try {
        document=EnvDocument.Load(full);dirty=false;selected=null;
        RefreshParameters();
        state.Text=document.Parameters.Count+" editable  •  "+document.UnsupportedBlockCount+" unsupported";
        bridgeState.Text="Editing selected ENV  •  "+Path.GetFileName(full);
        bridgeState.Foreground=Ui.Brush("#AEB7C2");
        status?.Invoke("ENV STUDIO  •  "+Path.GetFileName(full)+"  •  "+document.Parameters.Count+" parameters");
        log?.Invoke("ENV Studio contextual load: "+full+" ("+document.Parameters.Count+" editable, "+document.UnsupportedBlockCount+" unsupported)");
        UpdateActions();
        return true;
      } catch(Exception ex) {
        Program.LogException(ex);MessageBox.Show(ex.Message,"Open ENV",MessageBoxButton.OK,MessageBoxImage.Error);
        return false;
      }
    }

    void RefreshParameters() {
      if(document==null){parameters.ItemsSource=null;return;}
      string q=(search.Text??"").Trim();
      visible=document.Parameters.Where(x=>String.IsNullOrWhiteSpace(q)||
        x.FullPath.IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0).ToList();
      parameters.ItemsSource=null;parameters.ItemsSource=visible;
      state.Text=visible.Count==document.Parameters.Count
        ?document.Parameters.Count+" parameters"
        :visible.Count+" / "+document.Parameters.Count+" parameters";
    }

    void SelectParameter() {
      selected=parameters.SelectedItem as EnvParameter;
      points.ItemsSource=null;
      scalarEditor.Visibility=curveEditor.Visibility=Visibility.Collapsed;
      if(selected==null){selectedName.Text="Select an ENV parameter";UpdateActions();return;}
      selectedName.Text=selected.FullPath+"  •  "+selected.KindText;
      if(selected.Kind==EnvValueKind.Curve) {
        curveEditor.Visibility=Visibility.Visible;
        points.ItemsSource=selected.Points;
        if(selected.Points.Count>0)points.SelectedIndex=0;
      } else {
        scalarEditor.Visibility=Visibility.Visible;
        bool isBool=selected.Kind==EnvValueKind.Bool;
        bool isRef=selected.Kind==EnvValueKind.Reference;
        bool isRaw=selected.Kind==EnvValueKind.RawBlock;
        boolBox.Visibility=isBool?Visibility.Visible:Visibility.Collapsed;
        valueBox.Visibility=isBool?Visibility.Collapsed:Visibility.Visible;
        valueBox.IsReadOnly=isRaw;
        apply.Visibility=isRaw?Visibility.Collapsed:Visibility.Visible;
        foreach(var child in scalarEditor.Children.OfType<FrameworkElement>()) {
          if(Object.Equals(child.Tag,"REFERENCE_ACTIONS"))child.Visibility=isRef?Visibility.Visible:Visibility.Collapsed;
          if(Object.Equals(child.Tag,"RAW_ACTIONS"))child.Visibility=isRaw?Visibility.Visible:Visibility.Collapsed;
        }
        if(isBool)boolBox.IsChecked=Convert.ToBoolean(EnvDocument.GetScalar(selected.Variable),CultureInfo.InvariantCulture);
        else if(isRef)valueBox.Text=EnvDocument.GetReference(selected.Variable);
        else if(isRaw)valueBox.Text=selected.DisplayValue;
        else if(selected.Kind==EnvValueKind.Name)valueBox.Text=Convert.ToString(EnvDocument.GetNamedValue(selected.Variable),CultureInfo.InvariantCulture);
        else valueBox.Text=Convert.ToString(EnvDocument.GetScalar(selected.Variable),CultureInfo.InvariantCulture);
      }
      UpdateActions();
    }

    void LoadPoint() {
      var p=points.SelectedItem as EnvCurvePoint;
      if(p==null)return;
      timeBox.Text=F(p.Time);valueBoxCurve.Text=F(p.Scalar);
      xBox.Text=F(p.X);yBox.Text=F(p.Y);zBox.Text=F(p.Z);wBox.Text=F(p.W);
    }

    bool ApplyLocal(bool message) {
      if(document==null||selected==null)return false;
      try {
        if(selected.Kind==EnvValueKind.RawBlock) {
          if(message)status?.Invoke("RAW Next-Gen blocks are replaced only through IMPORT SAME BLOCK FROM ENV.");
          return true;
        }
        if(selected.Kind==EnvValueKind.Bool)document.SetBool(selected,boolBox.IsChecked==true);
        else if(selected.Kind==EnvValueKind.Float)document.SetFloat(selected,Parse(valueBox.Text));
        else if(selected.Kind==EnvValueKind.Int)document.SetInt(selected,(long)Parse(valueBox.Text));
        else if(selected.Kind==EnvValueKind.Reference)document.SetReference(selected,valueBox.Text);
        else if(selected.Kind==EnvValueKind.Name)document.SetName(selected,valueBox.Text);
        else if(selected.Kind==EnvValueKind.String)document.SetString(selected,valueBox.Text);
        else if(selected.Kind==EnvValueKind.Curve) {
          var cp=points.SelectedItem as EnvCurvePoint;
          if(cp==null)throw new InvalidOperationException("Select a curve point first.");
          document.SetCurvePoint(selected,cp.Index,Parse(timeBox.Text),Parse(valueBoxCurve.Text),
            Parse(xBox.Text),Parse(yBox.Text),Parse(zBox.Text),Parse(wBox.Text));
        }
        dirty=true;RefreshParameters();
        if(message)status?.Invoke("ENV value changed in memory. Use SAVE ENV to commit.");
        UpdateActions();return true;
      } catch(Exception ex) {
        if(message)MessageBox.Show(ex.Message,"Edit ENV",MessageBoxButton.OK,MessageBoxImage.Warning);
        return false;
      }
    }

    void PasteReference() {
      if(selected==null||selected.Kind!=EnvValueKind.Reference)return;
      try {
        string text=System.Windows.Clipboard.ContainsText()?System.Windows.Clipboard.GetText():"";
        if(String.IsNullOrWhiteSpace(text))return;
        text=text.Trim().Trim('"');
        var p=getProject();
        if(Path.IsPathRooted(text)&&File.Exists(text)&&p!=null) {
          string depot=EnvDocument.DepotPath(p,text);
          if(!String.IsNullOrWhiteSpace(depot))text=depot;
        }
        valueBox.Text=text.Replace('/','\\');
        status?.Invoke("Reference pasted. Apply to ENV, then Save ENV.");
      } catch(Exception ex){MessageBox.Show(ex.Message,"Paste reference",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }

    void CopyReference() {
      if(selected==null||selected.Kind!=EnvValueKind.Reference)return;
      try{if(!String.IsNullOrWhiteSpace(valueBox.Text))System.Windows.Clipboard.SetText(valueBox.Text.Trim());}catch{}
    }

    void BrowseReference() {
      if(selected==null||selected.Kind!=EnvValueKind.Reference)return;
      var p=getProject();
      var dlg=new Microsoft.Win32.OpenFileDialog{Title="Select project XBM / LUT",Filter="REDengine textures (*.xbm)|*.xbm|All files|*.*"};
      if(p!=null&&Directory.Exists(p.Files))dlg.InitialDirectory=p.Files;
      if(dlg.ShowDialog()!=true)return;
      string depot=p==null?null:EnvDocument.DepotPath(p,dlg.FileName);
      if(String.IsNullOrWhiteSpace(depot)) {
        MessageBox.Show("Choose a texture inside the current project so Forge can store a valid REDengine depot path.","ENV reference",MessageBoxButton.OK,MessageBoxImage.Warning);
        return;
      }
      valueBox.Text=depot;
      status?.Invoke("Selected ENV resource reference: "+depot);
    }

    public void SaveCurrent(){SaveEnv();}

    void OpenSmartWorkflow(){
      if(document==null)return;
      if(dirty){
        MessageBox.Show("Save or discard pending ENV Studio edits before using Smart Transfer / Order.",
          "W3 Forge ENV",MessageBoxButton.OK,MessageBoxImage.Warning);
        return;
      }
      var current=document.FilePath;
      var window=new EnvSmartWindow(current,getProject(),log,()=>{
        try{document=EnvDocument.Load(current);dirty=false;selected=null;RefreshParameters();UpdateActions();}
        catch(Exception ex){log?.Invoke("ENV changed on disk; reload manually: "+ex.Message);}
      });
      window.Owner=Window.GetWindow(this);
      window.ShowDialog();
    }

    void ReloadEnv() {
      if(document==null)return;
      try {
        if(dirty&&MessageBox.Show("Discard unsaved ENV edits and reload from disk?","ENV Studio",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        string file=document.FilePath;
        document=EnvDocument.Load(file);dirty=false;selected=null;
        RefreshParameters();state.Text=document.Parameters.Count+" editable  •  "+document.UnsupportedBlockCount+" unsupported";
        status?.Invoke("ENV reloaded from disk: "+Path.GetFileName(file));
        UpdateActions();
      } catch(Exception ex){Program.LogException(ex);MessageBox.Show(ex.Message,"Reload ENV",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void CopyEnvPath() {
      if(document==null)return;
      try{System.Windows.Clipboard.SetText(document.FilePath);}catch{}
      status?.Invoke("ENV path copied.");
    }

    void ImportRawBlock() {
      if(document==null||selected==null||selected.Kind!=EnvValueKind.RawBlock)return;
      var dlg=new Microsoft.Win32.OpenFileDialog{Title="Import the same Next-Gen ENV block from another ENV",Filter="Witcher 3 environment (*.env)|*.env|All files|*.*"};
      if(dlg.ShowDialog()!=true)return;
      try {
        var source=EnvDocument.Load(dlg.FileName);
        var sourceParam=source.Parameters.FirstOrDefault(x=>String.Equals(x.FullPath,selected.FullPath,StringComparison.OrdinalIgnoreCase)&&x.Kind==EnvValueKind.RawBlock);
        if(sourceParam==null)throw new InvalidDataException("The selected ENV does not contain the same raw block: "+selected.FullPath);
        var bytes=EnvDocument.GetRawBytes(sourceParam.Variable);
        if(bytes==null||bytes.Length==0)throw new InvalidDataException("The source raw block is empty.");
        document.SetRawBytes(selected,bytes);
        dirty=true;
        valueBox.Text=selected.DisplayValue;
        RefreshParameters();
        status?.Invoke("Imported raw Next-Gen block: "+selected.FullPath+"  •  "+bytes.Length+" bytes");
        log?.Invoke("ENV raw block imported from "+dlg.FileName+": "+selected.FullPath+" ("+bytes.Length+" bytes)");
        UpdateActions();
      } catch(Exception ex){Program.LogException(ex);MessageBox.Show(ex.Message,"Import ENV block",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void ExportRawBlock() {
      if(selected==null||selected.Kind!=EnvValueKind.RawBlock)return;
      try {
        var bytes=EnvDocument.GetRawBytes(selected.Variable);
        if(bytes==null)throw new InvalidDataException("Raw block is unavailable.");
        var dlg=new Microsoft.Win32.SaveFileDialog{Title="Export raw Next-Gen ENV block",Filter="Binary block (*.bin)|*.bin|All files|*.*",FileName=selected.Path.Replace('.','_')+".bin"};
        if(dlg.ShowDialog()!=true)return;
        File.WriteAllBytes(dlg.FileName,bytes);
        status?.Invoke("Raw ENV block exported: "+dlg.FileName);
      } catch(Exception ex){Program.LogException(ex);MessageBox.Show(ex.Message,"Export ENV block",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void SaveEnv() {
      if(document==null)return;
      try {
        ApplyLocal(false);
        var p=getProject();
        string backupRoot=p!=null
          ? Path.Combine(p.Workspace,"backups","env")
          : Path.Combine(EnvironmentPaths.AppDataRoot,"Backups","ENV");
        string backup=document.SaveWithBackup(backupRoot);
        var file=document.FilePath;
        document=EnvDocument.Load(file);dirty=false;
        RefreshParameters();
        log?.Invoke("ENV saved and validated: "+file);
        log?.Invoke("ENV backup: "+backup);
        status?.Invoke("ENV SAVED  •  backup retained");
        MessageBox.Show("ENV saved and validated.\n\nBackup:\n"+backup,"Live ENV Studio",MessageBoxButton.OK,MessageBoxImage.Information);
      } catch(Exception ex) {
        Program.LogException(ex);MessageBox.Show(ex.Message,"Save ENV",MessageBoxButton.OK,MessageBoxImage.Error);
      }
      UpdateActions();
    }

    void ToggleLiveLab() {
      liveLabEnabled=!liveLabEnabled;
      liveLab.Visibility=liveLabEnabled?Visibility.Visible:Visibility.Collapsed;
      liveLabToggle.Content=liveLabEnabled?"HIDE LIVE ENV LAB":"LIVE ENV LAB  [EXPERIMENTAL]";
      UpdateBridgeState();
    }

    void CopyLiveCommand() {
      if(document==null||selected==null)return;
      if(!ApplyLocal(true))return;
      try {
        int point=-1;
        var cp=points.SelectedItem as EnvCurvePoint;
        if(selected.Kind==EnvValueKind.Curve&&cp!=null)point=cp.Index;
        string cmd=LiveEnvBridge.CommandFor(getProject(),document.FilePath,selected,point);
        Clipboard.SetText(cmd);
        status?.Invoke("Live ENV command COPIED — not sent to the game.");
      } catch(Exception ex) {
        Program.LogException(ex);MessageBox.Show(ex.Message,"Copy Live Command",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }

    void ExportBridgePreview() {
      try {
        string result=LiveEnvBridge.ExportPreview(getProject(),log);
        status?.Invoke("Experimental bridge script exported without modifying the game.");
        MessageBox.Show("OFFLINE BRIDGE PREVIEW EXPORTED\n\n"+result+
          "\n\nThis is WitcherScript source only. It has not been compiled by The Witcher 3 "+
          "and no runtime communication is assumed.",
          "Live ENV Bridge — Offline Preview",MessageBoxButton.OK,MessageBoxImage.Information);
      } catch(Exception ex) {
        Program.LogException(ex);
        MessageBox.Show(ex.Message,"Export Bridge Preview",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }

    void InstallBridge() {
      try {
        var answer=MessageBox.Show(
          "EXPERIMENTAL Live ENV Bridge (NOT validated in a running game).\n\n"+
          "This action writes a WitcherScript mod to the game's mods folder, "+
          "may enable DBGConsoleOn in general.ini (backed up), and requires a game restart.\n\n"+
          "The legacy EnvUi mod was inspected, not installed or copied. "+
          "WitcherScript compatibility with your Next-Gen build is NOT assured.\n\n"+
          "Install the bridge anyway?",
          "W3 Forge — Experimental Bridge",MessageBoxButton.YesNo,MessageBoxImage.Warning);
        if(answer!=MessageBoxResult.Yes)return;
        var result=LiveEnvBridge.Install(getProject(),getGameBin(),log);
        UpdateBridgeState();
        status?.Invoke("LIVE ENV BRIDGE READY  •  restart the game to compile it");
        MessageBox.Show(
          "Live ENV bridge generated successfully.\n\n"+
          "ENV files scanned: "+result.EnvFilesScanned+"\n"+
          "Live parameters: "+result.ParametersGenerated+"\n"+
          "Imports: "+(result.UsedExistingSharedImports?"existing Shared Imports":"standalone W3 Forge minimal imports")+"\n\n"+
          "Start/restart The Witcher 3 before using Apply Live.",
          "Live ENV Bridge",MessageBoxButton.OK,MessageBoxImage.Information);
      } catch(Exception ex) {
        Program.LogException(ex);MessageBox.Show(ex.Message,"Install Live ENV Bridge",MessageBoxButton.OK,MessageBoxImage.Error);
      }
    }

    void RemoveBridge() {
      try {LiveEnvBridge.Uninstall(getGameBin(),log);UpdateBridgeState();status?.Invoke("Live ENV bridge removed.");}
      catch(Exception ex){MessageBox.Show(ex.Message,"Remove Live ENV Bridge",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void LoadLive() {
      if(document==null)return;
      try {
        string cmd=LiveEnvBridge.LoadCommand(getProject(),document.FilePath);
        LiveEnvBridge.SendConsoleCommand(cmd,SelectedKey());
        status?.Invoke("ENV LOAD COMMAND SENT (not game-confirmed): "+Path.GetFileName(document.FilePath));
        log?.Invoke("Live ENV command: "+cmd);
      } catch(Exception ex){Program.LogException(ex);MessageBox.Show(ex.Message,"Load ENV Live",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void ApplyLive() {
      if(document==null||selected==null)return;
      if(!ApplyLocal(true))return;
      try {
        int point=-1;
        var cp=points.SelectedItem as EnvCurvePoint;
        if(selected.Kind==EnvValueKind.Curve&&cp!=null)point=cp.Index;
        string cmd=LiveEnvBridge.CommandFor(getProject(),document.FilePath,selected,point);
        LiveEnvBridge.SendConsoleCommand(cmd,SelectedKey());
        status?.Invoke("LIVE ENV COMMAND SENT (not game-confirmed)  •  "+selected.FullPath);
        log?.Invoke("Live ENV command: "+cmd);
      } catch(Exception ex){Program.LogException(ex);MessageBox.Show(ex.Message,"Apply ENV Live",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void FreezeTime() {
      try {
        float hour=Parse(hourBox.Text);
        if(hour<0||hour>24)throw new ArgumentOutOfRangeException("hour","Game hour must be between 0 and 24.");
        LiveEnvBridge.SendConsoleCommand("w3forge_env_time("+F(hour)+")",SelectedKey());
        status?.Invoke("Freeze-time command sent (unconfirmed), requested hour "+F(hour));
      } catch(Exception ex){MessageBox.Show(ex.Message,"Freeze ENV time",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    void ReleaseTime() {
      try{LiveEnvBridge.SendConsoleCommand("w3forge_env_time_release()",SelectedKey());status?.Invoke("Release-time command sent (unconfirmed).");}
      catch(Exception ex){MessageBox.Show(ex.Message,"Release ENV time",MessageBoxButton.OK,MessageBoxImage.Error);}
    }

    W3ConsoleKey SelectedKey() {
      if(consoleKey.SelectedIndex==1)return W3ConsoleKey.Tilde;
      if(consoleKey.SelectedIndex==2)return W3ConsoleKey.F2;
      return W3ConsoleKey.Apostrophe;
    }

    void UpdateBridgeState() {
      bool installed=LiveEnvBridge.IsInstalled(getGameBin());
      bridgeState.Text=liveLabEnabled ? "LIVE ENV LAB  •  "+(installed?"Bridge scripts present; runtime NOT VERIFIED":"Bridge not installed; offline command preview available") : "Binary ENV editor  •  curves, names and resource/LUT references";
      bridgeState.Foreground=Ui.Brush("#8F98A4");
      removeBridge.IsEnabled=installed;
      UpdateActions();
    }

    void UpdateActions() {
      bool hasDoc=document!=null;
      bool bridge=LiveEnvBridge.IsInstalled(getGameBin());
      save.IsEnabled=hasDoc;
      loadLive.IsEnabled=hasDoc&&bridge;
      applyLive.IsEnabled=hasDoc&&selected!=null&&(selected.Kind==EnvValueKind.Bool||selected.Kind==EnvValueKind.Float||selected.Kind==EnvValueKind.Curve)&&bridge;
      copyLive.IsEnabled=hasDoc&&selected!=null&&(selected.Kind==EnvValueKind.Bool||selected.Kind==EnvValueKind.Float||selected.Kind==EnvValueKind.Curve);
      installBridge.IsEnabled=getProject()!=null&&!bridge;
      exportPreview.IsEnabled=getProject()!=null;
      removeBridge.IsEnabled=bridge;
    }

    static float Parse(string s) {
      float v;
      if(float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out v))return v;
      if(float.TryParse(s,NumberStyles.Float,CultureInfo.CurrentCulture,out v))return v;
      throw new FormatException("Invalid numeric value: "+s);
    }
    static string F(float v){return v.ToString("0.######",CultureInfo.InvariantCulture);}
  }
}