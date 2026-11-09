Public Class FontPickerComboBox
    Inherits UserControl

    Public Event DropDownClosed As EventHandler

    Public Sub New()
        InitializeComponent()
        AddHandler FontComboBox.DropDownClosed, AddressOf OnFontComboBoxDropDownClosed
        FontComboBox.ItemsSource = BuildFontList()
    End Sub

    ''' <summary>
    ''' Bundled single-line fonts first, then every installed system font. The bundled
    ''' entries are Hershey vector fonts, so text plots as one line rather than a filled
    ''' outline.
    ''' </summary>
    Private Function BuildFontList() As IList(Of FontFamily)
        Dim list As New List(Of FontFamily)

        For Each bundledName In BundledFontRegistry.FamilyNames()
            Dim family = BundledFontRegistry.TryCreate(bundledName)
            If family IsNot Nothing Then list.Add(family)
        Next

        For Each systemFont In Fonts.SystemFontFamilies
            list.Add(systemFont)
        Next

        Return list
    End Function

    Private Sub OnFontComboBoxDropDownClosed(sender As Object, e As EventArgs)
        RaiseEvent DropDownClosed(Me, e)
    End Sub

    Public Shared ReadOnly SelectedFontProperty As DependencyProperty =
        DependencyProperty.Register("SelectedFont", GetType(FontFamily), GetType(FontPickerComboBox), New PropertyMetadata(Nothing))

    Public Property SelectedFont As FontFamily
        Get
            Return CType(GetValue(SelectedFontProperty), FontFamily)
        End Get
        Set(value As FontFamily)
            SetValue(SelectedFontProperty, value)
        End Set
    End Property
End Class
