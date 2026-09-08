using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

public sealed class ClientConfigWindow : Window
{
    private readonly int _port;
    private string _currentClient = ClientConfigGenerator.ClientVsCode;
    private string _currentScope = ClientConfigGenerator.ScopeGlobal;

    private readonly List<Button> _clientButtons = new();
    private RadioButton _rbGlobal = null!;
    private RadioButton _rbLocal = null!;
    private TextBox _txtPath = null!;
    private TextBox _txtSample = null!;
    private TextBlock _lblNotes = null!;
    private Button _btnCopyJson = null!;
    private DispatcherTimer? _resetCopyTimer;

    public ClientConfigWindow(int port = ClientConfigGenerator.DefaultPort)
    {
        _port = port;
        InitializeComponent();
        UpdateView();
    }

    private void InitializeComponent()
    {
        Title = "VsDebugMcp - 客户端接入配置指引 (Client Configuration Guide)";
        Width = 740;
        Height = 630;
        MinWidth = 660;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei, sans-serif");

        var mainGrid = new Grid
        {
            Margin = new Thickness(16)
        };
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Header
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: IDE Selector
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Scope Selector
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Target Path
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4: Sample JSON Box
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 5: Notes & Tip
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 6: Footer Actions

        // 0: Header Card
        var headerBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock
        {
            Text = "VsDebugMcp",
            FontWeight = FontWeights.Bold,
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214))
        };
        var subText = new TextBlock
        {
            Text = "  客户端接入指南 (MCP Onboarding)",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleText);
        titleStack.Children.Add(subText);
        Grid.SetColumn(titleStack, 0);

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var statusIndicator = new TextBlock
        {
            Text = "● 运行中",
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(78, 201, 176)),
            Margin = new Thickness(0, 0, 12, 0)
        };
        var portText = new TextBlock
        {
            Text = $"监听端口: {_port} (http://127.0.0.1:{_port})",
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200))
        };
        statusStack.Children.Add(statusIndicator);
        statusStack.Children.Add(portText);
        Grid.SetColumn(statusStack, 1);

        headerGrid.Children.Add(titleStack);
        headerGrid.Children.Add(statusStack);
        headerBorder.Child = headerGrid;
        Grid.SetRow(headerBorder, 0);
        mainGrid.Children.Add(headerBorder);

        // 1: IDE Selector
        var clientPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var clientLabel = new TextBlock
        {
            Text = "选择目标客户端 (Select IDE / Agent):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        clientPanel.Children.Add(clientLabel);

        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var clients = new[]
        {
            (ClientConfigGenerator.ClientVsCode, "VS Code"),
            (ClientConfigGenerator.ClientCursor, "Cursor"),
            (ClientConfigGenerator.ClientClaude, "Claude Desktop"),
            (ClientConfigGenerator.ClientAntigravity, "Antigravity"),
            (ClientConfigGenerator.ClientCodex, "Codex")
        };

        foreach (var (id, name) in clients)
        {
            var btn = new Button
            {
                Content = name,
                Tag = id,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12
            };
            btn.Click += OnClientButtonClick;
            _clientButtons.Add(btn);
            btnPanel.Children.Add(btn);
        }
        clientPanel.Children.Add(btnPanel);
        Grid.SetRow(clientPanel, 1);
        mainGrid.Children.Add(clientPanel);

        // 2: Scope Selector
        var scopePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var scopeLabel = new TextBlock
        {
            Text = "生效范围 (Scope):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        _rbGlobal = new RadioButton
        {
            Content = "全局配置 (Global / User)",
            IsChecked = true,
            GroupName = "ScopeGroup",
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        _rbGlobal.Checked += (_, _) => { _currentScope = ClientConfigGenerator.ScopeGlobal; UpdateView(); };

        _rbLocal = new RadioButton
        {
            Content = "本地/项目配置 (Local / Workspace)",
            GroupName = "ScopeGroup",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        _rbLocal.Checked += (_, _) => { _currentScope = ClientConfigGenerator.ScopeLocal; UpdateView(); };

        scopePanel.Children.Add(scopeLabel);
        scopePanel.Children.Add(_rbGlobal);
        scopePanel.Children.Add(_rbLocal);
        Grid.SetRow(scopePanel, 2);
        mainGrid.Children.Add(scopePanel);

        // 3: Target Path
        var pathPanel = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        pathPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        pathPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var pathLabel = new TextBlock
        {
            Text = "推荐保存路径 (Target File Path):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        Grid.SetRow(pathLabel, 0);
        pathPanel.Children.Add(pathLabel);

        var pathRow = new Grid();
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _txtPath = new TextBox
        {
            IsReadOnly = true,
            Padding = new Thickness(8, 5, 8, 5),
            Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
            Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12
        };
        Grid.SetColumn(_txtPath, 0);

        var btnCopyPath = new Button
        {
            Content = "复制路径",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 52)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 74)),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnCopyPath.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_txtPath.Text))
            {
                Clipboard.SetText(_txtPath.Text);
                btnCopyPath.Content = "已复制 ✓";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, _) => { btnCopyPath.Content = "复制路径"; timer.Stop(); };
                timer.Start();
            }
        };
        Grid.SetColumn(btnCopyPath, 1);

        pathRow.Children.Add(_txtPath);
        pathRow.Children.Add(btnCopyPath);
        Grid.SetRow(pathRow, 1);
        pathPanel.Children.Add(pathRow);

        Grid.SetRow(pathPanel, 3);
        mainGrid.Children.Add(pathPanel);

        // 4: Sample JSON Box
        var sampleGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        sampleGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sampleGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var sampleLabel = new TextBlock
        {
            Text = "配置样本 (Sample JSON):",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220))
        };
        Grid.SetRow(sampleLabel, 0);
        sampleGrid.Children.Add(sampleLabel);

        _txtSample = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66)),
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 12,
            Padding = new Thickness(10)
        };
        Grid.SetRow(_txtSample, 1);
        sampleGrid.Children.Add(_txtSample);

        Grid.SetRow(sampleGrid, 4);
        mainGrid.Children.Add(sampleGrid);

        // 5: Notes & Tip
        var notesPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        _lblNotes = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var tipText = new TextBlock
        {
            Text = "安全提示：本插件绝不自动修改或覆盖您的任何已有文件。请点击下方按钮复制配置后，手动粘贴至目标文件并保存。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            FontSize = 11
        };
        notesPanel.Children.Add(_lblNotes);
        notesPanel.Children.Add(tipText);
        Grid.SetRow(notesPanel, 5);
        mainGrid.Children.Add(notesPanel);

        // 6: Footer Actions
        var footerGrid = new Grid();
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _btnCopyJson = new Button
        {
            Content = "复制配置到剪贴板 (Copy to Clipboard)",
            FontWeight = FontWeights.Bold,
            FontSize = 13,
            Padding = new Thickness(20, 8, 20, 8),
            Background = new SolidColorBrush(Color.FromRgb(14, 99, 156)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 119, 187)),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _btnCopyJson.Click += OnCopyJsonClick;
        Grid.SetColumn(_btnCopyJson, 0);

        var btnClose = new Button
        {
            Content = "关闭",
            Padding = new Thickness(18, 8, 18, 8),
            Background = new SolidColorBrush(Color.FromRgb(50, 50, 52)),
            Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 74)),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btnClose.Click += (_, _) => Close();
        Grid.SetColumn(btnClose, 1);

        footerGrid.Children.Add(_btnCopyJson);
        footerGrid.Children.Add(btnClose);
        Grid.SetRow(footerGrid, 6);
        mainGrid.Children.Add(footerGrid);

        Content = mainGrid;
    }

    private void OnClientButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string clientId)
        {
            _currentClient = clientId;
            UpdateView();
        }
    }

    private void UpdateView()
    {
        // 1. Update button styles
        foreach (var btn in _clientButtons)
        {
            bool isSelected = (string)btn.Tag == _currentClient;
            if (isSelected)
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(14, 99, 156));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(17, 119, 187));
                btn.FontWeight = FontWeights.Bold;
            }
            else
            {
                btn.Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(210, 210, 210));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(62, 62, 66));
                btn.FontWeight = FontWeights.Normal;
            }
        }

        // 2. Claude Desktop doesn't support local scope
        if (_currentClient == ClientConfigGenerator.ClientClaude)
        {
            _rbLocal.IsEnabled = false;
            if (_currentScope == ClientConfigGenerator.ScopeLocal)
            {
                _currentScope = ClientConfigGenerator.ScopeGlobal;
                _rbGlobal.IsChecked = true;
            }
        }
        else
        {
            _rbLocal.IsEnabled = true;
        }

        // 3. Generate item
        var items = ClientConfigGenerator.Generate(_currentClient, _currentScope, _port);
        var item = items.FirstOrDefault();

        if (item != null)
        {
            _txtPath.Text = item.RecommendedPath;
            _txtSample.Text = item.SampleJson;
            _lblNotes.Text = item.Notes;
            _btnCopyJson.IsEnabled = item.IsSupported;
        }
        else
        {
            _txtPath.Text = string.Empty;
            _txtSample.Text = string.Empty;
            _lblNotes.Text = string.Empty;
            _btnCopyJson.IsEnabled = false;
        }
    }

    private void OnCopyJsonClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_txtSample.Text)) return;

        Clipboard.SetText(_txtSample.Text);

        _btnCopyJson.Content = "已复制到剪贴板 ✓";
        _btnCopyJson.Background = new SolidColorBrush(Color.FromRgb(34, 139, 34));

        _resetCopyTimer?.Stop();
        _resetCopyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.0) };
        _resetCopyTimer.Tick += (_, _) =>
        {
            _btnCopyJson.Content = "复制配置到剪贴板 (Copy to Clipboard)";
            _btnCopyJson.Background = new SolidColorBrush(Color.FromRgb(14, 99, 156));
            _resetCopyTimer.Stop();
        };
        _resetCopyTimer.Start();
    }
}
