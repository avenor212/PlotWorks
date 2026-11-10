Imports System.IO
Imports System.Windows.Threading

Imports Microsoft.Extensions.Configuration
Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting

Imports Wpf.Ui
Imports Wpf.Ui.DependencyInjection

Partial Public Class Application
    Private Shared ReadOnly _host As IHost = Host.CreateDefaultBuilder() _
        .ConfigureAppConfiguration(Sub(context, configBuilder)
                                       ' Set base path using IConfigurationBuilder
                                       configBuilder.SetBasePath(Path.GetDirectoryName(AppContext.BaseDirectory))
                                   End Sub) _
        .ConfigureServices(Sub(context, services)

                               services.AddHostedService(Of ApplicationHostService)()

                               services.AddSingleton(Of CommandLineArgsService)()

                               ' Theme manipulation
                               services.AddSingleton(Of IThemeService, ThemeService)()

                               ' TaskBar manipulation
                               services.AddSingleton(Of ITaskBarService, TaskBarService)()
                               ' Service containing navigation, same as INavigationWindow... but without window

                               ' 不再注册 UpdateService：更新检查指向上游 PolyCut 仓库，
                               ' 对本分支无效，且会把用户导流到原版程序的 Releases 页。
                               ' 建好自有仓库后再按 rename-debt.md 的方案接回。

                               services.AddSingleton(Of INavigationService, NavigationService)()
                               services.AddSingleton(Of SnackbarService)()
                               services.AddSingleton(Of BambuA1Service)()
                               services.AddSingleton(Of IContentDialogService, ContentDialogService)()
                               services.AddSingleton(Of IVectorImportService, SVGImportService)()
                               services.AddSingleton(Of UndoRedoService)()
                               services.AddSingleton(Of PolyCut.RichCanvas.SelectionManager)()
                               services.AddSingleton(Of Document)()
                               services.AddSingleton(Of UIConfiguration)(Function(provider) SettingsHandler.GetUIConfiguration())
                               services.AddSingleton(Of ProjectSerializationService)()
                               services.AddSingleton(Of ClipboardService)()
                               services.AddSingleton(Of VTracerService)()
                               services.AddSingleton(Of LocalisationService)()

                               ' Main window with navigation
                               services.AddNavigationViewPageProvider()
                               services.AddSingleton(Of INavigationWindow, MainWindow)()
                               services.AddSingleton(Of MainViewModel)()
                               services.AddSingleton(Of MainWindow)()
                               services.AddTransient(Of PrinterConfig)()

                               ' Views and ViewModels
                               services.AddSingleton(Of SVGPage)()
                               services.AddSingleton(Of SVGPageViewModel)()

                               services.AddSingleton(Of MonitorPage)()

                               services.AddSingleton(Of ExportPage)()
                               services.AddSingleton(Of ExportViewModel)()

                               services.AddSingleton(Of PreviewPage)()

                               services.AddSingleton(Of SettingsPage)()
                               services.AddSingleton(Of SettingsPageViewModel)()

                               services.AddTransient(Of BitmapToSVGWindow)()
                               services.AddTransient(Of BitmapToSVGWindowViewModel)()

                           End Sub) _
        .Build()

    Public Shared Function GetService(Of T As Class)() As T
        Return TryCast(_host.Services.GetService(GetType(T)), T)
    End Function

    Private Shadows Async Sub OnStartup(sender As Object, e As StartupEventArgs)

        Await _host.StartAsync()

    End Sub

    Private Shadows Async Sub OnExit(sender As Object, e As ExitEventArgs)

        Await _host.StopAsync()
        _host.Dispose()
    End Sub

    Private Sub OnDispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs)
        ' An unhandled exception used to close the window with no message, losing the
        ' canvas. Write the failure somewhere readable and keep the app alive so the
        ' user can still save whatever they had.
        Dim logPath As String = Nothing
        Try
            Dim dir = IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SettingsHandler.AppDataFolderName)
            IO.Directory.CreateDirectory(dir)
            logPath = IO.Path.Combine(dir, "crash.log")
            IO.File.AppendAllText(logPath,
                DateTime.Now.ToString("s") & "  " & e.Exception.ToString() & Environment.NewLine & Environment.NewLine)
        Catch
            ' Logging must never mask the original failure.
        End Try

        ' Snapshot the document before showing the dialog: if the state is already
        ' half-broken, the next launch can still get the user's work back.
        Dim recoveryFailure As String = Nothing
        Dim recovered As Boolean = RecoveryStore.TrySave(recoveryFailure)

        ' Record the recovery attempt next to the exception, so a failure can be diagnosed
        ' afterwards instead of only being announced once and forgotten.
        If logPath IsNot Nothing Then
            Try
                IO.File.AppendAllText(logPath,
                    "    recovery: " & If(recovered, "saved to " & RecoveryStore.FilePath(),
                                         "failed - " & If(String.IsNullOrWhiteSpace(recoveryFailure), "unknown reason", recoveryFailure)) &
                    Environment.NewLine & Environment.NewLine)
            Catch
            End Try
        End If

        Try
            Dim detail = If(logPath IsNot Nothing, "详细信息已写入：" & logPath, "详细信息写入失败。")
            Dim recoveryNote = If(recovered,
                                  "当前内容已自动保存，重新启动 PlotWorks 时会提示恢复。" & Environment.NewLine & Environment.NewLine,
                                  "自动保存失败（" & If(String.IsNullOrWhiteSpace(recoveryFailure), "原因未知", recoveryFailure) &
                                  "）。请立即用 文件 → 保存 手动保存当前内容，否则关闭后无法找回。" & Environment.NewLine & Environment.NewLine)
            System.Windows.MessageBox.Show(
                "PlotWorks 遇到一个内部错误，但已阻止程序关闭，避免丢失当前内容。" & Environment.NewLine & Environment.NewLine &
                e.Exception.Message & Environment.NewLine & Environment.NewLine &
                detail & Environment.NewLine & Environment.NewLine &
                recoveryNote &
                "请先保存当前项目，再继续操作。",
                "PlotWorks 出现错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning)
        Catch
        End Try

        e.Handled = True
    End Sub
End Class
