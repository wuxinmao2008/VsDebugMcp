using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using VsDebugMcp.Protocol;

namespace VsDebugMcp_Vsix;

public partial class ClientConfigWindow : DialogWindow
{
    private readonly int _port;
    private string _currentClient = ClientConfigGenerator.ClientVsCode;
    private string _currentScope = ClientConfigGenerator.ScopeGlobal;
    private DispatcherTimer? _resetCopyJsonTimer;
    private DispatcherTimer? _resetCopyPathTimer;

    public ClientConfigWindow(int port = ClientConfigGenerator.DefaultPort)
    {
        _port = port;
        InitializeComponent();
        TxtPort.Text = $"监听端口: {_port}";
        UpdateView();
    }

    private void OnClientSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded && sender != RbVsCode) return;

        if (RbVsCode?.IsChecked == true) _currentClient = ClientConfigGenerator.ClientVsCode;
        else if (RbCursor?.IsChecked == true) _currentClient = ClientConfigGenerator.ClientCursor;
        else if (RbClaude?.IsChecked == true) _currentClient = ClientConfigGenerator.ClientClaude;
        else if (RbAntigravity?.IsChecked == true) _currentClient = ClientConfigGenerator.ClientAntigravity;
        else if (RbCodex?.IsChecked == true) _currentClient = ClientConfigGenerator.ClientCodex;

        UpdateView();
    }

    private void OnScopeChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;

        if (RbScopeLocal?.IsChecked == true)
        {
            _currentScope = ClientConfigGenerator.ScopeLocal;
        }
        else
        {
            _currentScope = ClientConfigGenerator.ScopeGlobal;
        }

        UpdateView();
    }

    private void UpdateView()
    {
        if (TxtPath == null || TxtSample == null || LblNotes == null || BtnCopyJson == null)
            return;

        // Claude Desktop 不支持局部/项目配置
        if (_currentClient == ClientConfigGenerator.ClientClaude)
        {
            RbScopeLocal.IsEnabled = false;
            if (_currentScope == ClientConfigGenerator.ScopeLocal)
            {
                _currentScope = ClientConfigGenerator.ScopeGlobal;
                RbScopeGlobal.IsChecked = true;
            }
        }
        else
        {
            RbScopeLocal.IsEnabled = true;
        }

        var items = ClientConfigGenerator.Generate(_currentClient, _currentScope, _port);
        var item = items.FirstOrDefault();

        if (item != null)
        {
            TxtPath.Text = item.RecommendedPath;
            TxtSample.Text = item.SampleJson;
            LblNotes.Text = string.IsNullOrWhiteSpace(item.Notes)
                ? "提示：将上述配置合并到目标客户端的 mcpServers 节点中保存即可。"
                : $"提示：{item.Notes}";
            BtnCopyJson.IsEnabled = item.IsSupported;
            BtnCopyPath.IsEnabled = !string.IsNullOrWhiteSpace(item.RecommendedPath);
            BtnOpenFolder.IsEnabled = !string.IsNullOrWhiteSpace(item.RecommendedPath);
        }
        else
        {
            TxtPath.Text = string.Empty;
            TxtSample.Text = string.Empty;
            LblNotes.Text = string.Empty;
            BtnCopyJson.IsEnabled = false;
            BtnCopyPath.IsEnabled = false;
            BtnOpenFolder.IsEnabled = false;
        }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtPath.Text)) return;

        try
        {
            Clipboard.SetText(TxtPath.Text);
            BtnCopyPath.Content = "已复制 ✓";
            _resetCopyPathTimer?.Stop();
            _resetCopyPathTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _resetCopyPathTimer.Tick += (_, _) =>
            {
                BtnCopyPath.Content = "复制路径";
                _resetCopyPathTimer.Stop();
            };
            _resetCopyPathTimer.Start();
        }
        catch
        {
            // Clipboard access fallback
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        var targetPath = TxtPath.Text;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        try
        {
            var dir = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath) : targetPath;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                dir = Path.GetDirectoryName(dir);
            }

            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch
        {
            // Ignore explorer open failures
        }
    }

    private void OnCopyJsonClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtSample.Text)) return;

        try
        {
            Clipboard.SetText(TxtSample.Text);
            BtnCopyJson.Content = "已复制到剪贴板 ✓";

            _resetCopyJsonTimer?.Stop();
            _resetCopyJsonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            _resetCopyJsonTimer.Tick += (_, _) =>
            {
                BtnCopyJson.Content = "复制配置";
                _resetCopyJsonTimer.Stop();
            };
            _resetCopyJsonTimer.Start();
        }
        catch
        {
            // Clipboard access fallback
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
