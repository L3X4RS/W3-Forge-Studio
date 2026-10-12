using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
namespace W3Forge {
 public static class Ui {
  public static Brush Brush(string color)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
  public static Thickness M(double n)=>new Thickness(n);
  public static Thickness M(double x,double y)=>new Thickness(x,y,x,y);
  public static TextBlock Text(string s,double size=13,string color="#E9EDF2",FontWeightsPlaceholder unused=null) {
    return new TextBlock{Text=s,FontSize=size,Foreground=Brush(color),FontFamily=new FontFamily("Inter, Segoe UI Variable, Segoe UI"),VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
  }
  public static Border Box(string color,string stroke="#00000000",double radius=0,Thickness? pad=null){
    return new Border{Background=Brush(color),BorderBrush=Brush(stroke),BorderThickness=stroke=="#00000000"?new Thickness(0):new Thickness(1),CornerRadius=new CornerRadius(radius),Padding=pad??new Thickness(0)};
  }
  public static Button Button(string text,Action click,string style=null,double minWidth=0) {
    var b=new Button{Content=text,MinWidth=minWidth,Margin=new Thickness(0,0,8,0)};
    if(!String.IsNullOrWhiteSpace(style)&&Application.Current.Resources.Contains(style))b.Style=(Style)Application.Current.Resources[style];
    b.Click+=(s,e)=>click();
    return b;
  }
  public static void Rows(Grid grid,params GridLength[] sizes){foreach(var n in sizes)grid.RowDefinitions.Add(new RowDefinition{Height=n});}
  public static void Cols(Grid grid,params GridLength[] sizes){foreach(var n in sizes)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=n});}
  public static GridLength Px(double n)=>new GridLength(n);
  public static GridLength Star(double n=1)=>new GridLength(n,GridUnitType.Star);
  public static void Put(Grid grid,UIElement child,int row,int col=0,int rowSpan=1,int colSpan=1){
    Grid.SetRow(child,row);Grid.SetColumn(child,col);Grid.SetRowSpan(child,rowSpan);Grid.SetColumnSpan(child,colSpan);grid.Children.Add(child);
  }
  public static StackPanel H(double spacing=0){return new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};}
  public static StackPanel V(){return new StackPanel{Orientation=Orientation.Vertical};}
  public static void Space(StackPanel panel,double n){panel.Children.Add(new Border{Width=n,Height=n});}
  public static Border Separator(bool vertical=false){
    return new Border{Background=Brush("#353940"),Width=vertical?1:Double.NaN,Height=vertical?Double.NaN:1,Margin=vertical?new Thickness(8,5,12,5):new Thickness(0,8,0,8)};
  }
 }
 public sealed class FontWeightsPlaceholder {}
}