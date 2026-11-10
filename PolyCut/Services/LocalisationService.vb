Imports LazyTranslate

Public Class LocalisationService

    Public Shared Async Function InitializeAsync() As Task
        Await LazyTranslate.InitialiseAsync(New LocalisationOptions With {
            .SourceCulture = "en-AU",
            .InitialCulture = Application.GetService(Of UIConfiguration)().Language,
            .CatalogueDirectory = IO.Path.Combine(SettingsHandler.DataFolder.FullName, "Localisation"),
            .ResourceAssembly = GetType(Application).Assembly,
            .ApplicationName = "PolyCut",
            .ProgramVersion = SettingsHandler.SemanticVersion.ToNormalizedString(),
            .OutputMissingTranslationsToDebug = False
        })
    End Function

    Public Shared Async Function LoadLanguage(languageCode As String) As Task(Of Boolean)
        Dim success = L.TryLoadLanguage(languageCode)

        If success Then
            Dim configuration = Application.GetService(Of UIConfiguration)()
            configuration.Language = languageCode
            Await SettingsHandler.WriteUIConfiguration(configuration)
        Else
            Application.GetService(Of SnackbarService).GenerateError("Language Load Error".LT(), "Failed to load language: {0}".LTF(languageCode))
        End If

        Return success
    End Function

    ''' <summary>
    ''' 重新加载当前语言文件。
    ''' 只读本地目录与内嵌资源，<b>不联网</b>：上游的语言库属于原版 PolyCut，
    ''' 对本分支的界面无效，拉到本地会把 zh-CN 的词条覆盖成上游版本
    ''' （见 A1-README 1.7 与 rename-debt.md「自动更新 / 语言包地址」）。
    ''' 等自有仓库建好、语言文件托管过去之后，再在这里接回远程检查。
    ''' </summary>
    Public Shared Function CheckForLanguageUpdate() As Task(Of Boolean)
        Return Task.FromResult(True)
    End Function
End Class
