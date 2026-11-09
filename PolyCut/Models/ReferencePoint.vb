Imports CommunityToolkit.Mvvm.ComponentModel

'A physical reference point captured from the printer. Coordinates are stored in canvas
'space (origin top-left, Y down) so the designer can place the marker directly.
Public Class ReferencePoint
    Inherits ObservableObject

    <ObservableProperty>
    <NotifyPropertyChangedFor(NameOf(Label))>
    Private _Number As Integer

    <ObservableProperty>
    <NotifyPropertyChangedFor(NameOf(Label))>
    Private _X As Double

    <ObservableProperty>
    <NotifyPropertyChangedFor(NameOf(Label))>
    Private _Y As Double

    Public ReadOnly Property Label As String
        Get
            Return String.Format(Globalization.CultureInfo.InvariantCulture, "#{0}    X {1:F1}    Y {2:F1}", Number, X, Y)
        End Get
    End Property

    Public Sub New()
    End Sub

    Public Sub New(x As Double, y As Double)
        Me.X = x
        Me.Y = y
    End Sub

End Class
