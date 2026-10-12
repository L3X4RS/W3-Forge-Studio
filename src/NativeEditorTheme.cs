using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace W3Forge {
  internal static class NativeEditorTheme {
    static readonly Color Bg = Color.FromArgb(24,27,31);
    static readonly Color Surface = Color.FromArgb(30,34,39);
    static readonly Color Surface2 = Color.FromArgb(36,41,47);
    static readonly Color Header = Color.FromArgb(39,44,51);
    static readonly Color Border = Color.FromArgb(56,62,70);
    static readonly Color Text = Color.FromArgb(226,230,235);
    static readonly Color Muted = Color.FromArgb(166,174,184);
    static readonly Color Selected = Color.FromArgb(104,38,45);
    static readonly Color SelectedInactive = Color.FromArgb(71,42,47);

    public static void Apply(Control root) {
      if(root==null)return;
      ApplyControl(root);
      foreach(Control child in root.Controls)Apply(child);
    }

    static void ApplyControl(Control c) {
      try {
        c.BackColor = Bg;
        c.ForeColor = Text;
        if(!(c.Font?.Name=="Inter"))
          c.Font = new Font("Inter", Math.Max(8.5f,c.Font?.Size??9f), c.Font?.Style??FontStyle.Regular);

        var typeName=c.GetType().FullName??"";
        if(typeName.IndexOf("DockPaneCaption",StringComparison.OrdinalIgnoreCase)>=0 ||
           typeName.IndexOf("DockPaneStrip",StringComparison.OrdinalIgnoreCase)>=0 ||
           typeName.IndexOf("AutoHideStrip",StringComparison.OrdinalIgnoreCase)>=0) {
          c.BackColor=Surface2;c.ForeColor=Text;
        } else if(typeName.IndexOf("DockWindow",StringComparison.OrdinalIgnoreCase)>=0 ||
                  typeName.IndexOf("DockPanel",StringComparison.OrdinalIgnoreCase)>=0) {
          c.BackColor=Bg;c.ForeColor=Text;
        }

        if(c is ListView lv) {
          lv.BackColor=Surface;
          lv.ForeColor=Text;
          lv.FullRowSelect=true;
          lv.GridLines=false;
          SetOptional(c,"UseAlternatingBackColors",false);
          SetOptional(c,"AlternateRowBackColor",Surface);
          SetOptional(c,"UseCustomSelectionColors",true);
          SetOptional(c,"HighlightBackgroundColor",Selected);
          SetOptional(c,"HighlightForegroundColor",Color.White);
          SetOptional(c,"UnfocusedHighlightBackgroundColor",SelectedInactive);
          SetOptional(c,"UnfocusedHighlightForegroundColor",Color.White);
          SetOptional(c,"HeaderUsesThemes",false);
          ThemeHeader(c);
          foreach(ListViewItem item in lv.Items) {
            item.BackColor=Surface;
            item.ForeColor=Text;
            foreach(ListViewItem.ListViewSubItem sub in item.SubItems){sub.BackColor=Surface;sub.ForeColor=Text;}
          }
          lv.Layout += delegate {
            try {
              foreach(ListViewItem item in lv.Items) {
                if(!item.Selected) {
                  item.BackColor=Surface;item.ForeColor=Text;
                  foreach(ListViewItem.ListViewSubItem sub in item.SubItems){sub.BackColor=Surface;sub.ForeColor=Text;}
                }
              }
            } catch {}
          };
        }

        if(c is TreeView tv) {
          tv.BackColor=Surface;tv.ForeColor=Text;tv.LineColor=Border;
        }
        if(c is TextBoxBase tb) {
          tb.BackColor=Surface;tb.ForeColor=Text;tb.BorderStyle=BorderStyle.FixedSingle;
        }
        if(c is Label label) label.ForeColor=Text;
        if(c is Splitter) c.BackColor=Border;
      } catch {}
    }

    static void ThemeHeader(Control c) {
      try {
        var p=c.GetType().GetProperty("HeaderFormatStyle",BindingFlags.Public|BindingFlags.Instance);
        var style=p?.GetValue(c,null);if(style==null)return;
        foreach(var stateName in new[]{"Normal","Hot","Pressed"}) {
          var stateProp=style.GetType().GetProperty(stateName);
          var state=stateProp?.GetValue(style,null);if(state==null)continue;
          SetOptional(state,"BackColor",stateName=="Hot"?Surface2:Header);
          SetOptional(state,"ForeColor",Color.White);
          SetOptional(state,"FrameColor",Border);
          SetOptional(state,"FrameWidth",1f);
        }
      } catch {}
    }

    static void SetOptional(object obj,string name,object value) {
      try {
        var p=obj.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
        if(p!=null&&p.CanWrite)p.SetValue(obj,value,null);
      } catch {}
    }
  }
}