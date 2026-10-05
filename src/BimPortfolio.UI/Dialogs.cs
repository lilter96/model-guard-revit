using BimPortfolio.Presentation;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;
using Microsoft.Win32;
namespace BimPortfolio.UI;
// Views contain only layout/bindings. Validation and actions live in view models.
public static class Dialogs
{
    static Window Window(string title, IntPtr owner, double width, double height)
    {
        var window = new Window { Title = title, Width = width, Height = height, MinWidth = 480, MinHeight = 350, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brushes.White };
        new WindowInteropHelper(window).Owner = owner; return window;
    }
    public static bool Settings(SettingsViewModel vm, IntPtr owner)
    {
        var window = Window(Strings.Get(vm.IsRoute ? "TitleRoute" : "TitleAudit"), owner, 660, 540); window.DataContext = vm;
        var panel = new StackPanel { Margin = new Thickness(24) };
        if (vm.IsRoute) { Field(panel, "MaxAttachment", "Attachment"); Field(panel, "Reserve", "Reserve"); Field(panel, "Allowance", "Allowance"); Field(panel, "MaxCable", "CableLimit"); }
        else
        {
            Field(panel, "RequiredParameters", "Required"); Field(panel, "MarkPrefix", "Prefix");
            Field(panel, "ProfilePath", "ProfilePath");
            panel.Children.Add(Button("BrowseProfile", vm.BrowseProfile));
            vm.BrowseProfileRequested += () =>
            {
                var file = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
                if (file.ShowDialog(window) == true) vm.ProfilePath = file.FileName;
            };
        }
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) }; error.SetBinding(TextBlock.TextProperty, "Error"); panel.Children.Add(error);
        panel.Children.Add(Button("Continue", vm.Accept)); window.Content = panel; vm.Accepted += () => window.DialogResult = true;
        return window.ShowDialog() == true;
    }
    static void Field(Panel panel, string label, string property)
    {
        panel.Children.Add(new TextBlock { Text = Strings.Get(label), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 4) });
        var box = new TextBox { Padding = new Thickness(6) }; box.SetBinding(TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); panel.Children.Add(box);
    }
    static Button Button(string key, ICommand command) => new Button { Content = Strings.Get(key), Command = command, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 8) };
    public static ReportAction Report(ReportViewModel vm, IntPtr owner, bool route = false, bool preview = false)
    {
        var window = Window(Strings.Get(route ? "TitleRoute" : "TitleAudit"), owner, 1080, 660); window.DataContext = vm;
        var dock = new DockPanel { Margin = new Thickness(20) };
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16), Padding = new Thickness(16), Background = new SolidColorBrush(Color.FromRgb(24, 35, 46)), Foreground = Brushes.White }; summary.SetBinding(TextBlock.TextProperty, "Summary"); DockPanel.SetDock(summary, Dock.Top); dock.Children.Add(summary);
        var actions = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom);
        if (preview) actions.Children.Add(Button("Apply", vm.Apply));
        else { actions.Children.Add(Button(route ? "Save" : "Preview", route ? vm.Save : vm.Preview)); actions.Children.Add(Button("Export", vm.Export)); }
        if (route && !preview) { actions.Children.Add(Button("Html", vm.Html)); actions.Children.Add(Button("Schematic", vm.Schematic)); actions.Children.Add(Button("Highlight", vm.Highlight)); }
        actions.Children.Add(Button("Select", vm.Select)); var close = new Button { Content = Strings.Get("Close"), Padding = new Thickness(12, 7, 12, 7) }; close.Click += (_, __) => window.Close(); actions.Children.Add(close); dock.Children.Add(actions);
        if (!preview)
        {
            var searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(searchPanel, Dock.Top);
            Field(searchPanel, "Search", "Search"); dock.Children.Add(searchPanel);
        }
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, CanUserAddRows = false, AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(244, 247, 250)), GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeaderWidth = 0 };
        grid.MouseDoubleClick += (_, __) => { if (vm.Select.CanExecute(null)) vm.Select.Execute(null); };
        grid.Columns.Add(Column("Id", "UniqueId", 330)); grid.Columns.Add(Column("Code", "Code", 230)); grid.Columns.Add(Column("Detail", "Detail", 300)); if (route) grid.Columns.Add(Column("Length", "Length", 120));
        grid.SetBinding(ItemsControl.ItemsSourceProperty, "FilteredRows"); grid.SetBinding(DataGrid.SelectedItemProperty, new Binding("Selected") { Mode = BindingMode.TwoWay }); dock.Children.Add(grid);
        window.Content = dock; vm.Finished += () => window.Close(); window.ShowDialog(); return vm.Action;
    }
    static DataGridTextColumn Column(string key, string property, double width) => new DataGridTextColumn { Header = Strings.Get(key), Binding = new Binding(property), Width = width };
}
