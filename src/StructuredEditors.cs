using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml;

namespace W3Forge {
  // Purpose-built structured editors. All operations remain project-local until SAVE.
  // UTF encoding, BOM and REDengine semicolon CSV fields are preserved.
  public static class StructuredEditors {
    static string Stamp()=>DateTime.Now.ToString("yyyyMMdd_HHmmss_fff",CultureInfo.InvariantCulture);
    static string Backup(string file) {
      string path=file+".w3forge_"+Stamp()+".bak";
      File.Copy(file,path,false);
      return path;
    }
    static void SaveAtomic(string file,string value,TextResourceFormat format,bool xml) {
      string tmp=file+".w3forge_staged";
      if(File.Exists(tmp))throw new IOException("Staged document already exists: "+tmp);
      try{
        TextResourceIO.WriteAllText(tmp,value,format,xml);
        // Validate before replacing the user's original resource.
        if(xml)TextResourceIO.ValidateXmlFile(tmp);
        File.Copy(tmp,file,true);
      }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static TextBox CodeText(string text) {
      return new TextBox{
        Text=text,AcceptsReturn=true,AcceptsTab=true,FontFamily=new FontFamily("Consolas, Cascadia Mono"),
        FontSize=12.5,FontWeight=FontWeights.Normal,
        Foreground=Ui.Brush("#DCE6F3"),Background=Ui.Brush("#15191F"),
        BorderBrush=Ui.Brush("#333C46"),
        VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
        TextWrapping=TextWrapping.NoWrap,
        Margin=new Thickness(8,0,8,8),Padding=new Thickness(13,10,13,10)
      };
    }
    static StackPanel Head(string file,string label) {
      var row=Ui.H();row.Margin=new Thickness(12,5,10,5);
      var title=Ui.Text(Path.GetFileName(file),13,"#F4F6FA");
      title.FontWeight=FontWeights.SemiBold;row.Children.Add(title);
      Ui.Space(row,14);row.Children.Add(Ui.Text(label,10,"#A5B2C3"));Ui.Space(row,12);
      return row;
    }
    static void FindNext(TextBox editor,string needle,bool wrap=true){
      if(String.IsNullOrEmpty(needle))return;
      string body=editor.Text??"";
      int start=Math.Min(body.Length,editor.SelectionStart+editor.SelectionLength);
      int idx=body.IndexOf(needle,start,StringComparison.OrdinalIgnoreCase);
      if(idx<0&&wrap)idx=body.IndexOf(needle,StringComparison.OrdinalIgnoreCase);
      if(idx>=0){
        editor.Focus();editor.Select(idx,needle.Length);editor.ScrollToLine(editor.GetLineIndexFromCharacterIndex(idx));
      }
    }

    public static DocumentSession Script(string file,Action<string> log){
      var d=new DocumentSession{FilePath=file};TextResourceFormat fmt;
      string text=TextResourceIO.ReadAllText(file,out fmt);
      var root=new Grid();Ui.Rows(root,Ui.Px(52),Ui.Star(),Ui.Px(34));
      var toolbar=Head(file,"SCRIPT / SOURCE EDITOR");
      var find=new TextBox{Width=165,Margin=new Thickness(0,0,5,0),ToolTip="Find text"};
      var replace=new TextBox{Width=165,Margin=new Thickness(0,0,5,0),ToolTip="Replace selected occurrence"};
      var edit=CodeText(text);
      var status=Ui.Text("Loaded "+TextResourceIO.Describe(fmt)+" • F3 find • Ctrl+S save",10,"#9EA9B8");
      toolbar.Children.Add(Ui.Text("Find",10,"#A5B2C3"));toolbar.Children.Add(find);
      toolbar.Children.Add(Ui.Button("NEXT",()=>FindNext(edit,find.Text),"FlatButton"));
      toolbar.Children.Add(Ui.Text("Replace",10,"#A5B2C3"));toolbar.Children.Add(replace);
      toolbar.Children.Add(Ui.Button("REPLACE",()=>{
        if(edit.SelectionLength>0 && String.Equals(edit.SelectedText,find.Text,StringComparison.OrdinalIgnoreCase)){
          edit.SelectedText=replace.Text;d.Dirty=true;
        }else FindNext(edit,find.Text);
      },"FlatButton"));
      toolbar.Children.Add(Ui.Button("SAVE",()=>d.Save?.Invoke(),"PrimaryButton"));
      edit.TextChanged+=(s,e)=>{
        d.Dirty=true;
        status.Text="Edited • "+edit.LineCount+" lines • caret line "+(edit.GetLineIndexFromCharacterIndex(edit.CaretIndex)+1)+
          " • "+TextResourceIO.Describe(fmt);
      };
      edit.SelectionChanged+=(s,e)=>status.Text="Line "+(edit.GetLineIndexFromCharacterIndex(edit.CaretIndex)+1)+" / "+edit.LineCount+" • "+
        (d.Dirty?"UNSAVED":"SAVED")+" • "+TextResourceIO.Describe(fmt);
      edit.PreviewKeyDown+=(s,e)=>{
        if(e.Key==Key.F3){FindNext(edit,find.Text);e.Handled=true;}
        if(e.Key==Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){d.Save?.Invoke();e.Handled=true;}
      };
      Ui.Put(root,toolbar,0);Ui.Put(root,edit,1);
      var bottom=Ui.Box("#1C2229");bottom.Child=status;status.Margin=new Thickness(12,7,4,3);Ui.Put(root,bottom,2);
      d.Save=()=>{
        string backup=Backup(file);
        try{SaveAtomic(file,edit.Text,fmt,false);d.Dirty=false;status.Text="Saved • backup "+Path.GetFileName(backup);}
        catch(Exception ex){MessageBox.Show(ex.Message,"Save code",MessageBoxButton.OK,MessageBoxImage.Error);throw;}
        log?.Invoke("Code editor saved "+file+" backup "+backup);
      };
      d.Close=()=>{};
      d.Tab=new TabItem{Header=Path.GetFileName(file),Content=root,ToolTip=file};
      return d;
    }

    public static char GuessDelimiter(string line){
      int semi=0,comma=0,tab=0;bool quote=false;
      foreach(char c in line){
        if(c=='"'){quote=!quote;continue;}
        if(quote)continue;
        if(c==';')semi++;
        else if(c==',')comma++;
        else if(c=='\t')tab++;
      }
      if(semi>=comma&&semi>=tab&&semi>0)return ';';
      if(tab>=comma&&tab>0)return '\t';
      return ',';
    }
    // Handles escaped double quotes, CRLF/LF, quoted newlines, empty cells,
    // trailing separators and a final newline without adding extra rows.
    public static List<List<string>> ParseCsv(string text,char delimiter){
      var rows=new List<List<string>>();
      var row=new List<string>();var cell=new StringBuilder();
      bool quoted=false,ended=false,atStart=true;
      for(int i=0;i<text.Length;i++){
        char c=text[i];
        if(c=='"'){
          if(quoted&&i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}
          else if(quoted){quoted=false;}
          else if(atStart)quoted=true;
          else cell.Append(c);
          ended=false;atStart=false;continue;
        }
        if(!quoted&&c==delimiter){
          row.Add(cell.ToString());cell.Clear();atStart=true;ended=false;continue;
        }
        if(!quoted&&(c=='\r'||c=='\n')){
          if(c=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;
          row.Add(cell.ToString());cell.Clear();rows.Add(row);
          row=new List<string>();atStart=true;ended=true;continue;
        }
        cell.Append(c);atStart=false;ended=false;
      }
      if(quoted)throw new InvalidDataException("CSV has an unclosed quoted cell; open as source code to correct.");
      if(!ended){row.Add(cell.ToString());rows.Add(row);}
      return rows;
    }
    static string EscapeCell(string text,char delimiter){
      text=text??"";
      bool quote=text.IndexOfAny(new[]{delimiter,'"','\r','\n'})>=0;
      return quote?"\""+text.Replace("\"","\"\"")+"\"":text;
    }
    public static string SerializeCsv(IList<IList<string>> rows,char delimiter,string newline,bool ends){
      var sb=new StringBuilder();
      for(int i=0;i<rows.Count;i++){
        if(i>0)sb.Append(newline);
        sb.Append(String.Join(delimiter.ToString(),rows[i].Select(x=>EscapeCell(x,delimiter))));
      }
      if(ends&&rows.Count>0)sb.Append(newline);
      return sb.ToString();
    }

    static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject {
      if(parent==null)return null;
      for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){
        var child=VisualTreeHelper.GetChild(parent,i);
        if(child is T matching)return matching;
        var nested=FindVisualChild<T>(child);
        if(nested!=null)return nested;
      }
      return null;
    }

    public static DocumentSession Csv(string file,Action<string> log){
      var d=new DocumentSession{FilePath=file};TextResourceFormat fmt;
      string original=TextResourceIO.ReadAllText(file,out fmt);
      string newline=original.Contains("\r\n")?"\r\n":"\n";
      bool trailing=original.EndsWith("\n",StringComparison.Ordinal);
      var rows=ParseCsv(original,GuessDelimiter(original.Split(new[]{'\r','\n'},StringSplitOptions.None)[0]));
      if(rows.Count==0)throw new InvalidDataException("CSV empty; use source editor.");
      char delim=GuessDelimiter(original.Split(new[]{'\r','\n'},StringSplitOptions.None)[0]);
      int columns=rows.Max(x=>x.Count);
      if(columns<1||columns>400)throw new InvalidDataException("Unusual CSV column count: use source editor.");
      if(rows.Count>30000)throw new InvalidDataException("CSV too large for grid editor, use source mode.");
      var table=new DataTable("REDengine CSV");
      for(int j=0;j<columns;j++)table.Columns.Add("Column"+(j+1),typeof(string));
      for(int i=1;i<rows.Count;i++){
        var entry=table.NewRow();
        for(int j=0;j<columns;j++)entry[j]=j<rows[i].Count?rows[i][j]:"";
        table.Rows.Add(entry);
      }
      // Dedicated draggable CSV horizontal track beneath a taller editing grid.
      var layout=new Grid();Ui.Rows(layout,Ui.Px(51),Ui.Star(),Ui.Px(29),Ui.Px(28));
      var bar=Head(file,"TABLE / CSV EDITOR");
      var grid=new DataGrid{
        ItemsSource=table.DefaultView,AutoGenerateColumns=false,CanUserAddRows=true,
        CanUserDeleteRows=true,HeadersVisibility=DataGridHeadersVisibility.All,
        GridLinesVisibility=DataGridGridLinesVisibility.All,MinColumnWidth=105,
        AlternatingRowBackground=Ui.Brush("#202732"),Background=Ui.Brush("#161A21"),
        Foreground=Ui.Brush("#F2F4F7"),RowBackground=Ui.Brush("#171D24"),
        HorizontalGridLinesBrush=Ui.Brush("#313A46"),VerticalGridLinesBrush=Ui.Brush("#313A46"),
        Margin=new Thickness(6,0,6,1),SelectionMode=DataGridSelectionMode.Extended,
        HorizontalScrollBarVisibility=ScrollBarVisibility.Visible,
        VerticalScrollBarVisibility=ScrollBarVisibility.Visible,
        RowHeight=26
      };
      for(int j=0;j<columns;j++){
        var header=j<rows[0].Count?rows[0][j]:"";
        grid.Columns.Add(new DataGridTextColumn{Header=header,
          Binding=new Binding("["+(j)+"]"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},Width=new DataGridLength(Math.Max(120,header.Length*8))});
      }
      var status=Ui.Text("CSV "+table.Rows.Count+" data rows × "+columns+" columns  • delimiter "+(delim=='\t'?"TAB":delim.ToString())+" • "+TextResourceIO.Describe(fmt),10,"#96A2B4");
      Ui.Put(layout,bar,0);Ui.Put(layout,grid,1);
      var horizontalTrack=new Slider{
        Minimum=0,Maximum=0,Value=0,SmallChange=32,LargeChange=180,
        AutoToolTipPlacement=AutoToolTipPlacement.TopLeft,
        Margin=new Thickness(14,0,24,1),
        VerticalAlignment=VerticalAlignment.Center,
        ToolTip="Drag to scroll horizontally through CSV columns"
      };
      Ui.Put(layout,horizontalTrack,2);
      ScrollViewer internalScroll=null;
      grid.Loaded+=(sender,args)=>{
        internalScroll=FindVisualChild<ScrollViewer>(grid);
        if(internalScroll!=null){
          horizontalTrack.Maximum=Math.Max(0,internalScroll.ScrollableWidth);
          horizontalTrack.Value=Math.Min(horizontalTrack.Maximum,internalScroll.HorizontalOffset);
        }
      };
      grid.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler((sender,args)=>{
        if(internalScroll==null)internalScroll=FindVisualChild<ScrollViewer>(grid);
        if(internalScroll!=null){
          horizontalTrack.Maximum=Math.Max(0,internalScroll.ScrollableWidth);
          if(!horizontalTrack.IsMouseCaptureWithin){
            double offset=Math.Min(horizontalTrack.Maximum,internalScroll.HorizontalOffset);
            if(Math.Abs(horizontalTrack.Value-offset)>0.5)horizontalTrack.Value=offset;
          }
        }
      }));
      horizontalTrack.ValueChanged+=(sender,args)=>{
        if(internalScroll!=null&&Math.Abs(internalScroll.HorizontalOffset-horizontalTrack.Value)>0.5)
          internalScroll.ScrollToHorizontalOffset(horizontalTrack.Value);
      };
      grid.PreviewMouseWheel+=(sender,args)=>{
        if((Keyboard.Modifiers&ModifierKeys.Shift)==ModifierKeys.Shift && internalScroll!=null){
          internalScroll.ScrollToHorizontalOffset(Math.Max(0,internalScroll.HorizontalOffset-args.Delta/3.0));
          args.Handled=true;
        }
      };
      var footer=Ui.Box("#1C2229");footer.Child=status;status.Margin=new Thickness(12,4,4,2);Ui.Put(layout,footer,3);
      var newRow=Ui.Button("+ ROW",()=>{table.Rows.Add(table.NewRow());d.Dirty=true;},"FlatButton");
      bar.Children.Add(newRow);
      bar.Children.Add(Ui.Button("+ COLUMN",()=>{
        columns++;
        string internalName="Column"+columns;
        string displayName="new_field_"+columns;
        table.Columns.Add(internalName,typeof(string));
        rows[0].Add(displayName);
        grid.Columns.Add(new DataGridTextColumn{
          Header=displayName,
          Binding=new Binding("["+(columns-1)+"]"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.LostFocus},
          Width=new DataGridLength(135)
        });
        d.Dirty=true;
      },"FlatButton"));
      bar.Children.Add(Ui.Button("− ROW",()=>{
        var items=grid.SelectedItems.Cast<object>().OfType<DataRowView>().ToList();
        foreach(var item in items)item.Delete();
        d.Dirty=true;
      },"FlatButton"));
      bar.Children.Add(Ui.Button("SAVE CSV",()=>d.Save?.Invoke(),"PrimaryButton"));
      table.RowChanged+=(s,e)=>{d.Dirty=true;status.Text="UNSAVED • "+table.Rows.Count+" rows • "+TextResourceIO.Describe(fmt);};
      table.RowDeleted+=(s,e)=>d.Dirty=true;
      d.Save=()=>{
        grid.CommitEdit(DataGridEditingUnit.Cell,true);grid.CommitEdit(DataGridEditingUnit.Row,true);
        if(!d.Dirty){log?.Invoke("CSV unchanged: original bytes preserved, no rewrite.");return;}
        // Preserve headers and column order. New files use the input delimiter and newline.
        var output=new List<IList<string>>();
        output.Add(Enumerable.Range(0,columns).Select(j=>j<rows[0].Count?rows[0][j]:"").ToList());
        foreach(DataRow r in table.Rows)if(r.RowState!=DataRowState.Deleted){
          output.Add(Enumerable.Range(0,columns).Select(j=>Convert.ToString(r[j],CultureInfo.InvariantCulture)??"").ToList());
        }
        string content=SerializeCsv(output,delim,newline,trailing);
        string backup=Backup(file);
        try{
          SaveAtomic(file,content,fmt,false);
          var check=TextResourceIO.ReadAllText(file,out _);
          var parsed=ParseCsv(check,delim);
          if(parsed.Count!=output.Count)throw new InvalidDataException("CSV roundtrip changed row count.");
          d.Dirty=false;status.Text="CSV SAVED • backup "+Path.GetFileName(backup);
          log?.Invoke("CSV grid saved "+file+" backup "+backup);
        }catch(Exception ex){
          File.Copy(backup,file,true);
          MessageBox.Show(ex.Message,"CSV save",MessageBoxButton.OK,MessageBoxImage.Error);
          throw;
        }
      };
      d.Close=()=>{};
      d.Tab=new TabItem{Header=Path.GetFileName(file),Content=layout,ToolTip=file};
      return d;
    }

    public static DocumentSession Xml(string file,Action<string> log){
      var d=new DocumentSession{FilePath=file};TextResourceFormat fmt;
      string body=TextResourceIO.ReadAllText(file,out fmt);
      var root=new Grid();Ui.Rows(root,Ui.Px(51),Ui.Star(),Ui.Px(32));
      var top=Head(file,"XML TREE + SOURCE");
      var src=CodeText(body);
      var tree=new TreeView{Background=Ui.Brush("#171C23"),Foreground=Ui.Brush("#E4EBF4"),Padding=new Thickness(8)};
      var panes=new Grid();Ui.Cols(panes,Ui.Px(360),Ui.Px(5),Ui.Star());
      Ui.Put(panes,tree,0,0);
      Ui.Put(panes,new Border{Background=Ui.Brush("#343D47")},0,1);
      Ui.Put(panes,src,0,2);
      var status=Ui.Text("Tree is a structural navigator; edit source on the right. XML validated before Save.",10,"#9FABBA");
      voidPopulate(tree,body,status);
      tree.SelectedItemChanged+=(s,e)=>{
        var item=tree.SelectedItem as TreeViewItem;
        var element=item==null?null:item.Tag as XElement;
        var info=element as IXmlLineInfo;
        if(info!=null&&info.HasLineInfo()){
          int line=Math.Max(0,info.LineNumber-1);
          src.ScrollToLine(line);
          int index=src.GetCharacterIndexFromLineIndex(line);
          src.Focus();src.Select(index,Math.Min(src.Text.Length-index,element.Name.LocalName.Length+1));
          status.Text="XML tree location: "+element.Name.LocalName+" (line "+(line+1)+")";
        }
      };
      top.Children.Add(Ui.Button("REFRESH TREE",()=>voidPopulate(tree,src.Text,status),"FlatButton"));
      top.Children.Add(Ui.Button("VALIDATE XML",()=>{
        try{TextResourceIO.ValidateXmlText(src.Text,fmt,file);status.Text="XML syntax VALID"; }
        catch(Exception ex){status.Text=ex.Message;MessageBox.Show(ex.Message,"XML validation",MessageBoxButton.OK,MessageBoxImage.Warning);}
      },"FlatButton"));
      top.Children.Add(Ui.Button("SAVE XML",()=>d.Save?.Invoke(),"PrimaryButton"));
      var footer=Ui.Box("#1C2229");footer.Child=status;status.Margin=new Thickness(12,6,4,3);
      Ui.Put(root,top,0);Ui.Put(root,panes,1);Ui.Put(root,footer,2);
      src.TextChanged+=(s,e)=>d.Dirty=true;
      d.Save=()=>{
        TextResourceIO.ValidateXmlText(src.Text,fmt,file);
        string backup=Backup(file);
        try{
          SaveAtomic(file,src.Text,fmt,true);
          d.Dirty=false;voidPopulate(tree,src.Text,status);
          log?.Invoke("XML structured editor saved "+file+" backup "+backup);
        }catch(Exception){File.Copy(backup,file,true);throw;}
      };
      d.Close=()=>{};
      d.Tab=new TabItem{Header=Path.GetFileName(file),Content=root,ToolTip=file};
      return d;
    }
    static void voidPopulate(TreeView tree,string content,TextBlock status){
      try{
        var parsed=XDocument.Parse(content,LoadOptions.PreserveWhitespace|LoadOptions.SetLineInfo);
        tree.Items.Clear();
        if(parsed.Root!=null)tree.Items.Add(TreeItem(parsed.Root,0));
        status.Text="XML tree valid • "+(parsed.Root==null?"(empty)":parsed.Root.Name.LocalName)+" • preview refreshed";
      }catch(Exception ex){status.Text="Invalid XML: "+ex.Message;}
    }
    static TreeViewItem TreeItem(XElement e,int depth){
      var attrs=e.Attributes().Take(3).Select(a=>a.Name.LocalName+"=\""+(a.Value.Length>35?a.Value.Substring(0,35)+"…":a.Value)+"\"");
      var item=new TreeViewItem{Header="<"+e.Name.LocalName+(attrs.Any()?" "+String.Join(" ",attrs):"")+">",Tag=e};
      if(depth<14) {
        foreach(var child in e.Elements().Take(250))item.Items.Add(TreeItem(child,depth+1));
        if(e.Elements().Skip(250).Any())item.Items.Add(new TreeViewItem{Header="… more nodes (not expanded)"});
      }
      return item;
    }
  }
}