Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Reflection
Imports System.Text
Imports System.Text.Json

Imports CommunityToolkit.Mvvm.ComponentModel

Imports PolyCut.Core

Imports SharpVectors.Renderers

Public Class SettingsHandler : Inherits ObservableObject

    Public Shared Property SemanticVersion As NuGet.Versioning.NuGetVersion = New NuGet.Versioning.NuGetVersion(1, 0, 0)

    Public Shared ReadOnly Property Version As String
        Get
            Return ToFriendlyVersion(SemanticVersion)
        End Get
    End Property

    ''' <summary>Single source of truth for the per-user data folder name.</summary>
    Public Const AppDataFolderName As String = "PlotWorks"

    Public Shared Property DataFolder As IO.DirectoryInfo = New IO.DirectoryInfo(IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataFolderName))

    Public Shared Property SettingsJSONFile As IO.FileInfo

    Public Shared Property ConfigurationSettings As ConfigurationsSettings = New ConfigurationsSettings
    Public Shared Property PrinterSettings As PrinterSettings = New PrinterSettings
    Public Shared Property UISettings As UISettings = New UISettings
    Public Shared Property WeedingBoxConfiguration As New WeedingBoxConfiguration()

    Shared Async Function InitialiseSettings(truePortable As Boolean) As Task

        If truePortable Then DataFolder = New IO.DirectoryInfo(IO.Path.Combine(IO.Path.GetDirectoryName(AppContext.BaseDirectory), ".PlotWorksData"))

        ' 便携模式的数据本来就在程序目录里，不去动 AppData 里的旧配置
        If Not truePortable Then MigrateLegacyDataFolder()

        If Not DataFolder.Exists Then DataFolder.Create()
        Await PrinterSettings.InitialiseSettings(Of Printer)("PolyCut", $"{NameOf(Printer)}s")
        Await ConfigurationSettings.InitialiseSettings(Of ProcessorConfiguration)("PolyCut", $"{NameOf(ProcessorConfiguration)}s")
        Await UISettings.InitialiseSettings(Of UIConfiguration)("PolyCut", $"UIConfiguration")
        WeedingBoxConfiguration = Await LoadToolConfiguration(Of WeedingBoxConfiguration)()

        SettingsJSONFile = New IO.FileInfo(IO.Path.Combine(DataFolder.FullName, "settings.json"))

        If Not SettingsJSONFile.Exists Then Await SettingsJSONFile.Create().DisposeAsync()

        Dim languagesFolderPath As String = IO.Path.Combine(SettingsHandler.DataFolder.FullName, "Localisation")
        If Not IO.Path.Exists(languagesFolderPath) Then
            IO.Directory.CreateDirectory(languagesFolderPath)
        End If

        GenerateEV()

    End Function

    ''' <summary>
    ''' 把上游 PolyCut 的配置目录整份搬到当前数据目录。
    ''' 旧版保存在 %LocalAppData%\IridiumIO\PolyCut，本分支改成了 %LocalAppData%\PlotWorks
    ''' （见 rename-debt.md）。少了这一步，从旧版升级上来的用户会丢掉打印机配置
    ''' （IP / 访问码 / 序列号）、界面语言和工具配置，必须全部重填。
    ''' 只在目标目录为空时拷贝，且只补齐缺失文件，不覆盖已有内容。
    ''' </summary>
    Private Shared Sub MigrateLegacyDataFolder()
        Try
            Dim legacyPath As String = IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IridiumIO", "PolyCut")
            If Not IO.Directory.Exists(legacyPath) Then Return

            ' 目标里已经有文件，说明用户已经在新版里用过，不动它
            If DataFolder.Exists AndAlso
               IO.Directory.GetFileSystemEntries(DataFolder.FullName, "*", IO.SearchOption.AllDirectories).Length > 0 Then Return

            For Each sourceFile As String In IO.Directory.GetFiles(legacyPath, "*", IO.SearchOption.AllDirectories)
                Dim relativePath As String = IO.Path.GetRelativePath(legacyPath, sourceFile)
                Dim targetFile As String = IO.Path.Combine(DataFolder.FullName, relativePath)
                Dim targetDirectory As String = IO.Path.GetDirectoryName(targetFile)
                If Not String.IsNullOrEmpty(targetDirectory) Then IO.Directory.CreateDirectory(targetDirectory)
                If Not IO.File.Exists(targetFile) Then IO.File.Copy(sourceFile, targetFile)
            Next
        Catch ex As Exception
            ' 迁移失败不能挡住启动：数据目录会按默认值重建
            System.Diagnostics.Debug.WriteLine($"Legacy settings migration failed: {ex.Message}")
        End Try
    End Sub

    Public Shared Async Function LoadToolConfiguration(Of T As {SettingsBase, ISaveable, New})() As Task(Of T)
        Dim defaults As New T()
        defaults.SettingsFolder = Directory.CreateDirectory(IO.Path.Combine(DataFolder.FullName, "ToolsConfiguration"))
        Dim path = IO.Path.Combine(defaults.SettingsFolder.FullName, defaults.Name & ".json")
        Dim configuration = If(File.Exists(path), defaults.GetValue(Of T)(path), defaults)
        configuration.SettingsFolder = defaults.SettingsFolder
        Await configuration.SetValue(configuration.Name, configuration)
        Return configuration
    End Function

    Public Shared Function ToFriendlyVersion(semVer As NuGet.Versioning.NuGetVersion) As String
        Dim sb As New StringBuilder
        sb.Append(semVer.Major).Append("."c).Append(semVer.Minor).Append("."c).Append(semVer.Patch)
        If Not String.IsNullOrEmpty(semVer.Release) Then
            sb.Append(" "c).Append(semVer.ReleaseLabels(0))
            If semVer.ReleaseLabels.Count > 1 Then sb.Append(" "c).Append(semVer.ReleaseLabels(1))
        End If
        Return sb.ToString()
    End Function

    Private Shared Async Sub GenerateEV()


        Dim exepath As String = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName

        Dim EV1 = Environment.GetEnvironmentVariable("IridiumIO", EnvironmentVariableTarget.User)

        If EV1 Is Nothing Then
            Await Task.Run(Sub() Environment.SetEnvironmentVariable("IridiumIO", IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IridiumIO"), EnvironmentVariableTarget.User))
        End If

    End Sub

    Private Shared Function GetCollection(Of T)(handler As ISettingsService) As ObservableCollection(Of T)
        Dim collection As New ObservableCollection(Of T)

        Dim files = handler.SettingsFiles

        For Each file In files
            collection.Add(handler.GetValue(Of T)(file.FullName))
        Next

        Return collection

    End Function

    Shared Function GetPrinters() As ObservableCollection(Of Printer)
        Return GetCollection(Of Printer)(PrinterSettings)
    End Function
    Shared Async Sub WritePrinter(printer As Printer)
        printer.ClampWorkingArea()
        Await PrinterSettings.SetValue(printer.Name, printer)
    End Sub

    Shared Sub DeletePrinter(printer As Printer)
        PrinterSettings.DeleteValue(printer.Name)
    End Sub

    Shared Function GetConfigurations() As ObservableCollection(Of ProcessorConfiguration)
        Return GetCollection(Of ProcessorConfiguration)(ConfigurationSettings)
    End Function

    Shared Async Sub WriteConfiguration(Configuration As ProcessorConfiguration)
        Await ConfigurationSettings.SetValue(Configuration.Name, Configuration)
    End Sub

    Shared Function GetUIConfiguration() As UIConfiguration
        Return UISettings.GetValue(Of UIConfiguration)(IO.Path.Combine(UISettings.SettingsFolder.FullName, $"UIConfiguration.json"))
    End Function

    Shared Async Function WriteUIConfiguration(Configuration As UIConfiguration) As Task
        Await UISettings.SetValue(Configuration.Name, Configuration)
    End Function

End Class
