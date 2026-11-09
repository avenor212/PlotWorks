
Imports PolyCut.Shared

Public Class DrawingManager
    Private _currentShape As Shape
    Private _startPos As Point
    Private _drawingStroke As Brush = Brushes.Black
    Private _drawingFill As Brush = Brushes.Transparent

    Public Shared ReadOnly StrokeProperty As DependencyProperty = DependencyProperty.RegisterAttached("Stroke", GetType(Brush), GetType(DrawingManager), New FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits))
    Public Shared ReadOnly FillProperty As DependencyProperty = DependencyProperty.RegisterAttached("Fill", GetType(Brush), GetType(DrawingManager), New FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.Inherits))

    Public Shared Function GetStroke(element As DependencyObject) As Brush
        Return DirectCast(element.GetValue(StrokeProperty), Brush)
    End Function

    Public Shared Sub SetStroke(element As DependencyObject, value As Brush)
        element.SetValue(StrokeProperty, value)
    End Sub

    Public Shared Function GetFill(element As DependencyObject) As Brush
        Return DirectCast(element.GetValue(FillProperty), Brush)
    End Function

    Public Shared Sub SetFill(element As DependencyObject, value As Brush)
        element.SetValue(FillProperty, value)
    End Sub

    Public Shared Function HasPaint(brush As Brush) As Boolean
        Return brush IsNot Nothing AndAlso brush.Opacity > 0 AndAlso (TypeOf brush IsNot SolidColorBrush OrElse DirectCast(brush, SolidColorBrush).Color.A > 0)
    End Function

    Public Shared SNAPTOGRID As Boolean = False

    Public ReadOnly Property TextEditor As TextEditingController

    Public Sub New()
        TextEditor = New TextEditingController()
        AddHandler TextEditor.SessionFinished, AddressOf OnTextSessionFinished
    End Sub

    Private Sub OnTextSessionFinished(element As UIElement)
        RaiseEvent DrawingFinished(Me, element)
    End Sub

    Public Sub StartDrawing(mode As CanvasMode, startPoint As Point, pcanvas As PolyCanvas)
        TextEditor.CommitActiveText(pcanvas)
        _startPos = startPoint
        _drawingStroke = GetStroke(pcanvas)
        _drawingFill = GetFill(pcanvas)

        Select Case mode
            Case CanvasMode.Line
                _currentShape = CreateLine(startPoint)
            Case CanvasMode.Rectangle, CanvasMode.Ellipse, CanvasMode.RegistrationMark
                _currentShape = CreateShape(startPoint, mode)
            Case CanvasMode.Star
                _currentShape = CreateStar(startPoint)
            Case CanvasMode.Path
                _currentShape = CreatePen(startPoint)
            Case CanvasMode.Text
                'Drag a box first: the text box is created with that size and wraps inside it.
                _currentShape = CreateShape(startPoint, CanvasMode.Rectangle)
        End Select

        If _currentShape IsNot Nothing Then
            If mode <> CanvasMode.RegistrationMark Then
                _currentShape.Stroke = _drawingStroke
                _currentShape.Fill = If(mode = CanvasMode.Line, Brushes.Transparent, _drawingFill)
                _currentShape.StrokeThickness = 0.5
            End If
            pcanvas.Children.Add(_currentShape)
        End If
    End Sub



    Public Sub UpdateDrawing(mode As CanvasMode, currentPoint As Point, squareAspect As Boolean, snapToGrid As Boolean)
        If _currentShape Is Nothing Then Return
        DrawingManager.SNAPTOGRID = snapToGrid
        Select Case mode
            Case CanvasMode.Line
                UpdateLine(DirectCast(_currentShape, Line), currentPoint, squareAspect)
            Case CanvasMode.Rectangle, CanvasMode.RegistrationMark, CanvasMode.Ellipse, CanvasMode.Text
                UpdateRectShape(DirectCast(_currentShape, Shape), currentPoint, squareAspect)
            Case CanvasMode.Star
                UpdateStar(DirectCast(_currentShape, Path), currentPoint, squareAspect)
            Case CanvasMode.Path
                UpdatePen(DirectCast(_currentShape, Polyline), currentPoint)
        End Select
    End Sub

    Public Event DrawingFinished(sender As Object, element As UIElement)

    Public Sub FinishDrawing(mode As CanvasMode, pCanvas As PolyCanvas, ctextbox As TextBox)

        If mode = CanvasMode.Text Then
            If TextEditor.ActiveTextBox IsNot Nothing Then Return

            Dim boxWidth As Double = 0
            Dim boxHeight As Double = 0
            Dim boxPos As Point = _startPos

            If _currentShape IsNot Nothing Then
                Dim preview As Shape = DirectCast(_currentShape, Shape)
                boxWidth = preview.Width
                boxHeight = preview.Height

                Dim left = Canvas.GetLeft(preview)
                Dim top = Canvas.GetTop(preview)
                If Not Double.IsNaN(left) AndAlso Not Double.IsNaN(top) Then
                    boxPos = New Point(left, top)
                End If

                pCanvas.Children.Remove(preview)
                _currentShape = Nothing
            End If

            'Treat a simple click as "default text box" instead of a zero-size drag.
            If boxWidth < 8 OrElse boxHeight < 8 Then
                boxWidth = 160
                boxHeight = 48
            End If

            TextEditor.StartNewText(boxPos, TextCharacteristics.FromTextBox(ctextbox), pCanvas, boxWidth, boxHeight)
            If HasPaint(_drawingFill) Then TextEditor.ActiveTextBox.Foreground = _drawingFill
            Return
        End If


        If _currentShape Is Nothing Then Return

        Dim currentCursorPosition As Point = Mouse.GetPosition(pCanvas)

        If mode = CanvasMode.Line Then
            Dim line As Line = DirectCast(_currentShape, Line)
            _currentShape = FinaliseLine(line)

            If IsClick(_startPos, currentCursorPosition) Then
                pCanvas.Children.Remove(_currentShape)
                _currentShape = Nothing
                Return
            End If

        ElseIf mode = CanvasMode.Path Then
            Dim polyline As Polyline = DirectCast(_currentShape, Polyline)
            pCanvas.Children.Remove(polyline)
            _currentShape = FinalisePolyline(polyline)

            If polyline.Points.Count < 2 Then
                _currentShape = Nothing
                Return
            End If

        Else

            If IsClick(_startPos, currentCursorPosition) Then
                pCanvas.Children.Remove(_currentShape)
                _currentShape = Nothing
                Return
            End If
        End If


        pCanvas.Children.Remove(_currentShape)
        RaiseEvent DrawingFinished(Me, _currentShape)
        _currentShape = Nothing
    End Sub

    Public ReadOnly Property IsDrawing As Boolean
        Get
            Return _currentShape IsNot Nothing
        End Get
    End Property

    Public Sub CancelDrawing(pcanvas As PolyCanvas)
        TextEditor.CancelActiveText(pcanvas)
        If _currentShape Is Nothing Then Return
        If pcanvas IsNot Nothing AndAlso pcanvas.Children.Contains(_currentShape) Then
            pcanvas.Children.Remove(_currentShape)
        End If
        _currentShape = Nothing
    End Sub

    Private Shared Function IsClick(start As Point, current As Point) As Boolean
        Dim dx = current.X - start.X
        Dim dy = current.Y - start.Y
        Return dx * dx + dy * dy < 2
    End Function

    Private Function CreateLine(startPoint As Point) As Line
        Dim sp = ApplySnap(startPoint)
        Dim line As New Line With {
            .Stroke = Brushes.Black,
            .StrokeThickness = 0.5,
            .X1 = sp.X,
            .Y1 = sp.Y,
            .X2 = sp.X,
            .Y2 = sp.Y,
            .StrokeStartLineCap = PenLineCap.Square,
            .StrokeEndLineCap = PenLineCap.Square,
            .StrokeDashCap = PenLineCap.Square
        }

        Return line

    End Function

    Private Function CreatePen(startPoint As Point) As Polyline

        startPoint = ApplySnap(startPoint)

        Dim polyline As New Polyline With {
            .Stroke = Brushes.Black,
            .StrokeThickness = 0.5,
            .StrokeStartLineCap = PenLineCap.Round,
            .StrokeEndLineCap = PenLineCap.Round,
            .StrokeDashCap = PenLineCap.Round,
            .StrokeLineJoin = PenLineJoin.Round,
            .Points = New PointCollection() From {startPoint}
        }
        Return polyline
    End Function

    Private Function CreateShape(startPoint As Point, mode As CanvasMode) As Shape

        startPoint = ApplySnap(startPoint)

        Dim shape As Shape

        If mode = CanvasMode.Ellipse Then
            shape = New Ellipse
        Else
            shape = New Rectangle

            If mode = CanvasMode.RegistrationMark Then
                RegistrationMarkHelper.SetIsRegistrationMark(shape, True)
            End If
        End If

        Dim isRegistrationMark As Boolean = mode = CanvasMode.RegistrationMark

        shape.Stroke = If(isRegistrationMark, Brushes.Magenta, Brushes.Black)
        shape.StrokeThickness = 0.5
        shape.Fill = If(isRegistrationMark, Brushes.Magenta, Brushes.Transparent)
        shape.Width = 0
        shape.Height = 0
        shape.StrokeStartLineCap = PenLineCap.Round
        shape.StrokeEndLineCap = PenLineCap.Round
        shape.StrokeLineJoin = PenLineJoin.Round

        Canvas.SetLeft(shape, startPoint.X)
        Canvas.SetTop(shape, startPoint.Y)

        Return If(isRegistrationMark, shape, GeometricShape.FromShape(shape))
    End Function


    Private Function CreateStar(startPoint As Point) As Path

        startPoint = ApplySnap(startPoint)

        Dim path As New Path With {
            .Stroke = Brushes.Black,
            .StrokeThickness = 0.5,
            .Fill = Brushes.Transparent,
            .Data = CreateUnitStarGeometry(),
            .Stretch = Stretch.Fill,
            .Width = 0,
            .Height = 0
        }

        Canvas.SetLeft(path, startPoint.X)
        Canvas.SetTop(path, startPoint.Y)

        Return path
    End Function

    Private Sub UpdateStar(path As Path, currentPoint As Point, squareAspect As Boolean)

        Dim sp = ApplySnap(_startPos)
        Dim cp = ApplySnap(currentPoint)

        Dim w = Math.Abs(cp.X - sp.X)
        Dim h = Math.Abs(cp.Y - sp.Y)

        If squareAspect Then
            Dim size = Math.Max(w, h)
            path.Width = size
            path.Height = size
            Canvas.SetLeft(path, If(cp.X < sp.X, sp.X - size, sp.X))
            Canvas.SetTop(path, If(cp.Y < sp.Y, sp.Y - size, sp.Y))
        Else
            path.Width = w
            path.Height = h
            Canvas.SetLeft(path, Math.Min(cp.X, sp.X))
            Canvas.SetTop(path, Math.Min(cp.Y, sp.Y))
        End If

    End Sub

    'Five-pointed star in a 0..1 box; the Path is stretched to the drag rectangle.
    Private Shared Function CreateUnitStarGeometry() As PathGeometry

        Const cx As Double = 0.5
        Const cy As Double = 0.5
        Const outer As Double = 0.5
        Const inner As Double = 0.5 * 0.382

        Dim figure As New PathFigure()
        Dim first As Boolean = True

        For i As Integer = 0 To 9
            Dim radius As Double = If(i Mod 2 = 0, outer, inner)
            Dim angle As Double = -Math.PI / 2 + i * Math.PI / 5
            Dim pt As New Point(cx + Math.Cos(angle) * radius, cy + Math.Sin(angle) * radius)
            If first Then
                figure.StartPoint = pt
                first = False
            Else
                figure.Segments.Add(New LineSegment(pt, True))
            End If
        Next

        figure.IsClosed = True

        Dim geometry As New PathGeometry()
        geometry.Figures.Add(figure)
        Return geometry
    End Function


    Private Sub UpdateLine(line As Line, currentPoint As Point, squareAspect As Boolean)

        Dim newPoint As Point = currentPoint

        If squareAspect Then
            Dim dx = currentPoint.X - _startPos.X
            Dim dy = currentPoint.Y - _startPos.Y
            Dim angle = Math.Atan2(dy, dx) * (180 / Math.PI)
            Dim snappedAngle = Math.Round(angle / 45) * 45
            Dim length = Math.Sqrt(dx * dx + dy * dy)
            Dim snappedDx = Math.Cos(snappedAngle * (Math.PI / 180)) * length
            Dim snappedDy = Math.Sin(snappedAngle * (Math.PI / 180)) * length
            newPoint.X = _startPos.X + snappedDx
            newPoint.Y = _startPos.Y + snappedDy
        End If

        newPoint = ApplySnap(newPoint)

        line.X2 = newPoint.X
        line.Y2 = newPoint.Y

    End Sub

    Private Sub UpdatePen(polyline As Polyline, currentPoint As Point)

        currentPoint = ApplySnap(currentPoint)

        Dim lastPoint = polyline.Points(polyline.Points.Count - 1)
        If lastPoint <> currentPoint Then
            polyline.Points.Add(currentPoint)
        End If
    End Sub

    Private Sub UpdateRectShape(shape As Shape, currentPoint As Point, squareAspect As Boolean)

        Dim sp = ApplySnap(_startPos)
        Dim cp = ApplySnap(currentPoint)

        Dim x = Math.Min(cp.X, sp.X)
        Dim y = Math.Min(cp.Y, sp.Y)
        Dim w = Math.Abs(cp.X - sp.X)
        Dim h = Math.Abs(cp.Y - sp.Y)

        If squareAspect Then
            Dim size = Math.Max(w, h)
            shape.Width = size
            shape.Height = size
            Canvas.SetLeft(shape, If(cp.X < sp.X, sp.X - size, sp.X))
            Canvas.SetTop(shape, If(cp.Y < sp.Y, sp.Y - size, sp.Y))
        Else
            shape.Width = w
            shape.Height = h
            Canvas.SetLeft(shape, x)
            Canvas.SetTop(shape, y)
        End If
    End Sub

    ''' <summary>
    ''' Optional reference-point magnet. Receives a canvas point and returns the point to use;
    ''' returning the same point means "no snap".
    ''' </summary>
    Public Property ReferenceSnap As Func(Of Point, Point)

    Private Function ApplySnap(p As Point) As Point
        If ReferenceSnap IsNot Nothing Then
            Dim snapped As Point = ReferenceSnap(p)
            If snapped <> p Then Return snapped
        End If

        If Not SNAPTOGRID Then Return p

        Dim gd = PolyCanvas.GridDefinition
        Dim s = gd.Spacing
        Dim x = Math.Round((p.X - gd.InsetLeft) / s, MidpointRounding.AwayFromZero) * s + gd.InsetLeft
        Dim y = Math.Round((p.Y - gd.InsetTop) / s, MidpointRounding.AwayFromZero) * s + gd.InsetTop
        Return New Point(x, y)
    End Function

    Private Shared Function FinaliseLine(l As Line) As Line
        Dim negativeDirection As Boolean = l.X2 < l.X1 OrElse (l.X1 = l.X2 AndAlso l.Y2 < l.Y1)

        If negativeDirection Then
            Dim tempX As Double = l.X1
            Dim tempY As Double = l.Y1

            l.X1 = l.X2
            l.Y1 = l.Y2
            l.X2 = tempX
            l.Y2 = tempY

        End If

        Dim offsetX As Double = l.X1
        Dim offsetY As Double = Math.Min(l.Y1, l.Y2)

        l.X1 -= offsetX
        l.X2 -= offsetX
        l.Y1 -= offsetY
        l.Y2 -= offsetY

        Canvas.SetLeft(l, offsetX)
        Canvas.SetTop(l, offsetY)

        Return l

    End Function



    Private Shared Function FinalisePolyline(polyline As Polyline) As Path

        Dim isCtrlPressed As Boolean = Keyboard.IsKeyDown(Key.LeftCtrl) OrElse Keyboard.IsKeyDown(Key.RightCtrl)

        polyline.Points = RamerDouglasPeucker(polyline.Points, epsilon:=1.0)



        Dim minX As Double = polyline.Points.Min(Function(p) p.X)
        Dim minY As Double = polyline.Points.Min(Function(p) p.Y)

        Dim offsetX As Double = minX
        Dim offsetY As Double = minY
        For i As Integer = 0 To polyline.Points.Count - 1
            Dim point As Point = polyline.Points(i)
            polyline.Points(i) = New Point(point.X - offsetX, point.Y - offsetY)
        Next

        Dim path As Path = ConvertPolylineToBezierPath(polyline, smoothingFactor:=If(isCtrlPressed, 0, 0.1))
        If path Is Nothing Then Return Nothing
        path.Fill = polyline.Fill

        Dim bounds As Rect = path.Data.Bounds
        path.Width = bounds.Width
        path.Height = bounds.Height

        Canvas.SetLeft(path, offsetX + bounds.X)
        Canvas.SetTop(path, offsetY + bounds.Y)

        Return path

    End Function

    Private Shared Function ConvertPolylineToBezierPath(polyline As Polyline, smoothingFactor As Double) As Path
        If polyline.Points.Count < 2 Then
            Dim singlePoint As Point = polyline.Points(0)

            Dim spathFigure As New PathFigure With {
                .StartPoint = singlePoint,
                .IsClosed = False
            }

            Return CreatePath(spathFigure, polyline.Stroke, polyline.StrokeThickness)
        End If

        ' Check if the final segment is nearly at the start point
        Dim startPoint As Point = polyline.Points(0)
        Dim endPoint As Point = polyline.Points(polyline.Points.Count - 1)
        Dim deltaX As Double = endPoint.X - startPoint.X
        Dim deltaY As Double = endPoint.Y - startPoint.Y
        Dim distance As Double = Math.Sqrt(deltaX * deltaX + deltaY * deltaY)

        ' Close the path if the distance is below a threshold
        Dim closeThreshold As Double = 5.0 ' Adjust this value as needed
        Dim shouldClose = distance <= closeThreshold
        If shouldClose Then
            polyline.Points(polyline.Points.Count - 1) = startPoint
        End If

        ' Generate Bézier control points
        Dim bezierSegments = GenerateBezierControlPoints(polyline.Points, smoothingFactor)

        ' Create a PathFigure to hold the segments
        Dim pathFigure As New PathFigure With {
            .StartPoint = polyline.Points(0),
            .IsClosed = shouldClose
        }




        For Each segment In bezierSegments
            pathFigure.Segments.Add(segment)
        Next

        Return CreatePath(pathFigure, polyline.Stroke, polyline.StrokeThickness)
    End Function

    Private Shared Function CreatePath(figure As PathFigure, stroke As Brush, strokeThickness As Double) As Path
        Dim pathGeometry As New PathGeometry()
        pathGeometry.Figures.Add(figure)

        Return New Path With {
            .Stroke = stroke,
            .StrokeThickness = strokeThickness,
            .Data = pathGeometry
        }
    End Function

    Private Shared Function GenerateBezierControlPoints(points As PointCollection, smoothingFactor As Double) As List(Of BezierSegment)
        Dim bezierSegments As New List(Of BezierSegment)()

        If points.Count < 2 Then
            ' Not enough points to create Bézier curves
            Return bezierSegments
        End If

        For i As Integer = 0 To points.Count - 2
            Dim p0 As Point = If(i = 0, points(i), points(i - 1)) ' Previous point or current point for the first segment
            Dim p1 As Point = points(i) ' Current point
            Dim p2 As Point = points(i + 1) ' Next point
            Dim p3 As Point = If(i + 2 < points.Count, points(i + 2), points(i + 1)) ' Next-next point or next point for the last segment

            ' Calculate control points
            Dim cp1 As New Point(
                p1.X + (p2.X - p0.X) * smoothingFactor,
                p1.Y + (p2.Y - p0.Y) * smoothingFactor
            )

            Dim cp2 As New Point(
                p2.X - (p3.X - p1.X) * smoothingFactor,
                p2.Y - (p3.Y - p1.Y) * smoothingFactor
            )

            ' Create a Bézier segment
            bezierSegments.Add(New BezierSegment(cp1, cp2, p2, True))
        Next

        Return bezierSegments
    End Function

    Private Shared Function RamerDouglasPeucker(points As PointCollection, epsilon As Double) As PointCollection
        If points.Count < 3 Then
            ' If there are fewer than 3 points, return the original points
            Return points
        End If

        ' Find the point farthest from the line segment between the first and last points
        Dim firstPoint As Point = points(0)
        Dim lastPoint As Point = points(points.Count - 1)
        Dim maxDistance As Double = 0
        Dim index As Integer = 0

        For i As Integer = 1 To points.Count - 2
            Dim distance As Double = PerpendicularDistance(points(i), firstPoint, lastPoint)
            If distance > maxDistance Then
                maxDistance = distance
                index = i
            End If
        Next

        ' If the maximum distance is greater than the tolerance, recursively simplify
        If maxDistance > epsilon Then
            ' Recursively simplify the segments
            Dim leftSegment As PointCollection = RamerDouglasPeucker(New PointCollection(points.Take(index + 1)), epsilon)
            Dim rightSegment As PointCollection = RamerDouglasPeucker(New PointCollection(points.Skip(index)), epsilon)

            ' Combine the results, excluding the duplicate point at the junction
            Dim result As New PointCollection(leftSegment)
            result.RemoveAt(result.Count - 1)
            For Each point In rightSegment
                result.Add(point)
            Next

            Return result
        Else
            ' If the maximum distance is less than the tolerance, return the endpoints
            Return New PointCollection() From {firstPoint, lastPoint}
        End If
    End Function

    Private Shared Function PerpendicularDistance(point As Point, lineStart As Point, lineEnd As Point) As Double
        Dim dx As Double = lineEnd.X - lineStart.X
        Dim dy As Double = lineEnd.Y - lineStart.Y

        ' If the line segment is a point, return the distance to the point
        If dx = 0 AndAlso dy = 0 Then
            Return Math.Sqrt((point.X - lineStart.X) ^ 2 + (point.Y - lineStart.Y) ^ 2)
        End If

        ' Calculate the projection of the point onto the line
        Dim t As Double = ((point.X - lineStart.X) * dx + (point.Y - lineStart.Y) * dy) / (dx * dx + dy * dy)
        t = Math.Max(0, Math.Min(1, t)) ' Clamp t to the range [0, 1]

        ' Find the closest point on the line
        Dim closestPoint As New Point(lineStart.X + t * dx, lineStart.Y + t * dy)

        ' Return the distance from the point to the closest point on the line
        Return Math.Sqrt((point.X - closestPoint.X) ^ 2 + (point.Y - closestPoint.Y) ^ 2)
    End Function

End Class
