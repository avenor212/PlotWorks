Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

Public Module DrawableGeometry
    Public Function GetLocalGeometry(element As FrameworkElement) As Geometry
        Dim shape = TryCast(element, GeometricShape)
        If shape IsNot Nothing Then Return shape.LocalGeometry()

        Dim textBox = TryCast(element, TextBox)
        If textBox Is Nothing OrElse textBox.ActualWidth <= 0 OrElse textBox.ActualHeight <= 0 Then Return Nothing

        ' Bundled single-line fonts are drawn from their open-path glyph data: a plotter
        ' then traces each stroke once instead of following both sides of a hairline band.
        Dim singleLine = HersheyTextRenderer.TryBuildGeometry(
            textBox,
            textBox.Text,
            TextGeometryHelper.GetContentOrigin(textBox, New Point(0, 0)),
            textBox.TextAlignment)

        If singleLine IsNot Nothing Then Return singleLine

        Return TextGeometryHelper.BuildTextGeometry(textBox, textAlignment:=textBox.TextAlignment)
    End Function

    Public Function GetTransformedGeometry(element As FrameworkElement, relativeTo As UIElement) As Geometry
        If element Is Nothing OrElse relativeTo Is Nothing Then Return Nothing
        Dim geometry = GetLocalGeometry(element)
        If geometry Is Nothing Then Return Nothing

        Dim transform As GeneralTransform
        Try
            transform = element.TransformToVisual(relativeTo)
        Catch ex As InvalidOperationException
            Return Nothing
        Catch ex As ArgumentException
            Return Nothing
        End Try

        If geometry.IsFrozen Then geometry = geometry.Clone()
        Dim fitted = If(geometry.Transform?.Value, Matrix.Identity)
        fitted.Append(TransformMath.GeneralTransformToMatrix(transform))
        geometry.Transform = New MatrixTransform(fitted)
        Return geometry
    End Function
End Module
