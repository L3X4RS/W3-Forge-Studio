using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace W3Forge {
  public sealed class TextPrompt : Window {
    readonly TextBox input;
    public string Value { get { return input.Text.Trim(); } }

    public TextPrompt(string caption, string label, string value="") {
      Title = caption + " | W3 Forge Studio";
      Width = 500;
      Height = 245;
      MinWidth = 500;
      MinHeight = 245;
      ResizeMode = ResizeMode.NoResize;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      WindowStyling.Apply(this);

      var root = new Grid { Background = Ui.Brush("#0E1013") };
      Ui.Rows(root, Ui.Px(64), Ui.Star(), Ui.Px(62));

      var header = Ui.Box("#15181C", "#2A3038");
      var ht = Ui.Text(caption.ToUpperInvariant(), 12, "#D8DDE4");
      ht.FontWeight = FontWeights.SemiBold;
      ht.Margin = new Thickness(22,0,22,0);
      header.Child = ht;
      Ui.Put(root, header, 0);

      var body = new StackPanel { Margin = new Thickness(22,18,22,18) };
      var lab = Ui.Text(label, 11, "#9EA7B3");
      lab.Margin = new Thickness(1,0,0,8);
      body.Children.Add(lab);
      input = new TextBox { Text = value, Height = 40 };
      body.Children.Add(input);
      Ui.Put(root, body, 1);

      var footer = Ui.Box("#121519", "#272D35");
      var actions = Ui.H();
      actions.HorizontalAlignment = HorizontalAlignment.Right;
      actions.Margin = new Thickness(18,0,18,0);
      actions.Children.Add(Ui.Button("Cancel", delegate { DialogResult=false; Close(); }, "FlatButton"));
      actions.Children.Add(Ui.Button("Confirm", delegate { DialogResult=true; Close(); }, "PrimaryButton", 92));
      footer.Child = actions;
      Ui.Put(root, footer, 2);

      Content = root;
      Loaded += delegate { input.Focus(); input.CaretIndex=input.Text.Length; };
    }
  }

  public sealed class ProjectDialog : Window {
    readonly TextBox name;
    readonly TextBox folder;
    readonly TextBox game;

    public string ProjectName { get { return name.Text.Trim(); } }
    public string Folder { get { return folder.Text.Trim(); } }
    public string GameBin { get { return game.Text.Trim(); } }

    public ProjectDialog(Preferences prefs) {
      Title = "Create Project | W3 Forge Studio";
      Width = 780;
      Height = 565;
      MinWidth = 720;
      MinHeight = 540;
      ResizeMode = ResizeMode.NoResize;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      WindowStyling.Apply(this);

      var root = new Grid { Background = Ui.Brush("#0E1013") };
      Ui.Rows(root, Ui.Px(112), Ui.Star(), Ui.Px(74));

      var hero = Ui.Box("#13161A", "#292F37");
      var titleStack = Ui.V();
      titleStack.Margin = new Thickness(30,0,30,0);
      titleStack.VerticalAlignment = VerticalAlignment.Center;
      var title = Ui.Text("Create new project", 24, "#F5F6F8");
      title.FontWeight = FontWeights.SemiBold;
      titleStack.Children.Add(title);
      var subtitle = Ui.Text("Native REDengine 3 project. No ModKit or REDkit is required to build.", 11, "#89939F");
      subtitle.Margin = new Thickness(1,5,0,0);
      titleStack.Children.Add(subtitle);
      hero.Child = titleStack;
      Ui.Put(root, hero, 0);

      var body = new Grid { Margin = new Thickness(30,22,30,20) };
      Ui.Rows(body, Ui.Px(82), Ui.Px(82), Ui.Px(82));
      name = Field(body, "PROJECT NAME", "MyWitcherMod", 0, null);
      folder = Field(body, "SAVE LOCATION", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "W3 Forge Mod Projects"), 1, true);
      game = Field(body, "THE WITCHER 3 REMASTERED  |  BIN\\X64_DX12", prefs.GameBin, 2, false);
      Ui.Put(root, body, 1);

      var footer = Ui.Box("#121519", "#292F37");
      var footerGrid = new Grid { Margin = new Thickness(30,0,30,0) };
      Ui.Cols(footerGrid, Ui.Star(), Ui.Px(120), Ui.Px(12), Ui.Px(152));

      var hint = Ui.Text("Native Build creates the bundles and metadata internally.", 10, "#737D89");
      Ui.Put(footerGrid, hint, 0, 0);

      var cancel = Ui.Button("Cancel", delegate { DialogResult=false; Close(); }, "FlatButton");
      cancel.Margin = new Thickness(0);
      Ui.Put(footerGrid, cancel, 0, 1);

      var create = Ui.Button("CREATE PROJECT", delegate {
        if(ProjectName.Length == 0) {
          MessageBox.Show(this, "Enter a project name.", "Create Project", MessageBoxButton.OK, MessageBoxImage.Information);
          name.Focus();
          return;
        }
        if(ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) {
          MessageBox.Show(this, "The project name contains invalid characters.", "Create Project", MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }
        if(Folder.Length == 0) {
          MessageBox.Show(this, "Choose a save location.", "Create Project", MessageBoxButton.OK, MessageBoxImage.Information);
          return;
        }
        DialogResult = true;
        Close();
      }, "PrimaryButton");
      create.Margin = new Thickness(0);
      Ui.Put(footerGrid, create, 0, 3);

      footer.Child = footerGrid;
      Ui.Put(root, footer, 2);

      Content = root;
      Loaded += delegate { name.Focus(); name.CaretIndex=name.Text.Length; };
    }

    TextBox Field(Grid parent, string caption, string value, int row, bool? folderChoice) {
      var block = new Grid();
      Ui.Rows(block, Ui.Px(23), Ui.Px(42));
      Ui.Cols(block, Ui.Star(), Ui.Px(12), Ui.Px(104));

      var label = Ui.Text(caption, 10, "#98A2AE");
      label.FontWeight = FontWeights.SemiBold;
      Ui.Put(block, label, 0, 0, 1, 3);

      var input = new TextBox { Text = value ?? "", Height = 40 };
      Ui.Put(block, input, 1, 0);

      if(folderChoice.HasValue) {
        var browse = Ui.Button("Browse", delegate {
          using(var d = new System.Windows.Forms.FolderBrowserDialog()) {
            d.Description = folderChoice == true
              ? "Choose where W3 Forge Studio should save this project"
              : "Choose The Witcher 3 bin\\x64_dx12 folder";
            if(Directory.Exists(input.Text)) d.SelectedPath = input.Text;
            if(d.ShowDialog() == System.Windows.Forms.DialogResult.OK) input.Text = d.SelectedPath;
          }
        }, "ToolbarButton", 98);
        browse.Margin = new Thickness(0);
        Ui.Put(block, browse, 1, 2);
      } else {
        Grid.SetColumnSpan(input, 3);
      }

      Ui.Put(parent, block, row);
      return input;
    }
  }

  public sealed class SettingsDialog : Window {
    readonly TextBox game;
    public string GameBin { get { return game.Text.Trim(); } }

    public SettingsDialog(Preferences prefs) {
      Title = "Settings | W3 Forge Studio";
      Width = 730;
      Height = 345;
      MinWidth = 700;
      MinHeight = 345;
      ResizeMode = ResizeMode.NoResize;
      WindowStartupLocation = WindowStartupLocation.CenterOwner;
      WindowStyling.Apply(this);

      var root = new Grid { Background = Ui.Brush("#0E1013") };
      Ui.Rows(root, Ui.Px(92), Ui.Star(), Ui.Px(72));

      var header = Ui.Box("#13161A", "#292F37");
      var hs = Ui.V();
      hs.Margin = new Thickness(28,0,28,0);
      hs.VerticalAlignment = VerticalAlignment.Center;
      var title = Ui.Text("Game path", 22, "#F5F6F8");
      title.FontWeight = FontWeights.SemiBold;
      hs.Children.Add(title);
      var desc = Ui.Text("The native packer is built in. Only the installed game path is required.", 11, "#89939F");
      desc.Margin = new Thickness(0,5,0,0);
      hs.Children.Add(desc);
      header.Child = hs;
      Ui.Put(root, header, 0);

      var body = new Grid { Margin = new Thickness(28,25,28,20) };
      Ui.Rows(body, Ui.Px(90));
      game = PathField(body, "THE WITCHER 3 REMASTERED  |  BIN\\X64_DX12", prefs.GameBin, 0);
      Ui.Put(root, body, 1);

      var footer = Ui.Box("#121519", "#292F37");
      var actions = Ui.H();
      actions.HorizontalAlignment = HorizontalAlignment.Right;
      actions.Margin = new Thickness(24,0,24,0);
      actions.Children.Add(Ui.Button("Cancel", delegate { DialogResult=false; Close(); }, "FlatButton"));
      actions.Children.Add(Ui.Button("SAVE SETTINGS", delegate { DialogResult=true; Close(); }, "PrimaryButton", 130));
      footer.Child = actions;
      Ui.Put(root, footer, 2);
      Content = root;
    }

    TextBox PathField(Grid parent, string caption, string value, int row) {
      var g = new Grid();
      Ui.Rows(g, Ui.Px(23), Ui.Px(42));
      Ui.Cols(g, Ui.Star(), Ui.Px(12), Ui.Px(104));
      var label = Ui.Text(caption, 10, "#98A2AE");
      label.FontWeight = FontWeights.SemiBold;
      Ui.Put(g, label, 0, 0, 1, 3);
      var t = new TextBox { Text = value ?? "", Height = 40 };
      Ui.Put(g, t, 1, 0);
      var browse = Ui.Button("Browse", delegate {
        using(var d = new System.Windows.Forms.FolderBrowserDialog()) {
          d.Description = "Choose The Witcher 3 bin\\x64_dx12 folder";
          if(Directory.Exists(t.Text)) d.SelectedPath = t.Text;
          if(d.ShowDialog() == System.Windows.Forms.DialogResult.OK) t.Text = d.SelectedPath;
        }
      }, "ToolbarButton", 98);
      browse.Margin = new Thickness(0);
      Ui.Put(g, browse, 1, 2);
      Ui.Put(parent, g, row);
      return t;
    }
  }
}