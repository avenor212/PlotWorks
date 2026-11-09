Imports PolyCut.Shared

Public Class Tab_Configure
    Private ReadOnly _document As Document
    Private ReadOnly _preview As New List(Of (Wrapper As ContentControl, Brightness As Double, Opacity As Double))

    Public ReadOnly Property MainVM As MainViewModel

    Public Sub New()
        _document = Application.GetService(Of Document)()
        MainVM = Application.GetService(Of MainViewModel)()
        InitializeComponent()
        DataContext = Me

        ' Cutting is parked until the blade holder exists: the cut profile shares Z and
        ' speed with the drawing profile, so picking it would drive a blade at pen depth.
        Dim ui = Application.GetService(Of UIConfiguration)()
        If ui IsNot Nothing AndAlso Not ui.EnableCutting Then
            modeCut.Visibility = Visibility.Collapsed
        End If
    End Sub

    'Clicking a numeric field must not scroll the sidebar: the auto BringIntoView
    'made the panel jump so the next click landed on a different row.
    Private Sub ConfigureScrollViewer_RequestBringIntoView(sender As Object, e As RequestBringIntoViewEventArgs)
        e.Handled = True
    End Sub



    Private Sub ShadingThreshold_MouseEnter(sender As Object, e As MouseEventArgs)
        RestorePreview()
        For Each drawable In EnumerateDrawables(_document.Drawables)
            Dim wrapper = TryCast(drawable.DrawableElement?.Parent, ContentControl)
            If wrapper Is Nothing Then Continue For
            Dim brush = TryCast(drawable.Fill, SolidColorBrush)
            Dim color = If(brush Is Nothing, Colors.Black, brush.Color)
            Dim brightness = Math.Round((0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255, 3)
            _preview.Add((wrapper, brightness, wrapper.Opacity))
        Next
        RefreshPreview()
    End Sub

    Private Sub ShadingThreshold_TextChanged(sender As Object, e As RoutedEventArgs)
        If _preview.Count > 0 Then RefreshPreview()
    End Sub

    Private Sub ShadingThreshold_MouseLeave(sender As Object, e As MouseEventArgs)
        RestorePreview()
    End Sub

    Private Sub ShadingThreshold_Unloaded(sender As Object, e As RoutedEventArgs)
        RestorePreview()
    End Sub

    Private Sub RefreshPreview()
        Dim threshold As Double
        If Not Double.TryParse(ShadingThresholdControl.Text, threshold) Then Return
        For Each item In _preview
            item.Wrapper.Opacity = If(item.Brightness < threshold, item.Opacity * 0.2, item.Opacity)
        Next
    End Sub

    Private Sub RestorePreview()
        For Each item In _preview
            item.Wrapper.Opacity = item.Opacity
        Next
        _preview.Clear()
    End Sub

    Private Iterator Function EnumerateDrawables(items As IEnumerable(Of IDrawable)) As IEnumerable(Of IDrawable)
        For Each drawable In items
            Dim group = TryCast(drawable, NestedDrawableGroup)
            If group Is Nothing Then
                Yield drawable
            Else
                For Each child In group.DisplayChildren
                    Yield child
                Next
            End If
        Next
    End Function
End Class
