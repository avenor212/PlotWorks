Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports PolyCut.Shared

''' <summary>
''' Offscreen harness for the single-line text renderer. It builds the same open-path
''' geometry the plotter consumes, without a window or a live ComfyUI, so tone marks and
''' line spacing can be measured directly instead of being inferred from a G-code count.
''' </summary>
Module Program

    ' The app copies its bundled TTFs next to its own executable. This harness runs from
    ' tools\verify-text\bin, so fall back to the main project's build output.
    Private ReadOnly FontsDir As String = ResolveFontsDir()

    Private Function ResolveFontsDir() As String
        Dim candidates = New String() {
            IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Fonts"),
            IO.Path.GetFullPath(IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..\..\..\..\..\PolyCut\bin\Debug\net10.0-windows\Resources\Fonts"))
        }
        For Each c In candidates
            If IO.Directory.Exists(c) Then Return c
        Next
        Return candidates(0)
    End Function

    <STAThread>
    Sub Main()
        Console.OutputEncoding = Encoding.UTF8

        Dim family = ResolveFamily()
        If family Is Nothing Then Return

        Console.WriteLine("font: " & family.Source)
        Console.WriteLine()

        Dim size = 24.0  ' 68 pt at 1 unit = 1 mm

        Console.WriteLine("=== A. tone marks, one character at a time (24 mm) ===")
        Dim cases = New String() {"e", "é", "è", "ě", "ē", "ō", "ó", "ü", "ǎ", "ǚ",
                                  "e é è ě ō", "yuè", "lǚ", "nǐ hǎo"}
        For Each c In cases
            Report(Function(tb) HersheyTextRenderer.TryBuildGeometry(tb, c, New Point(0, 0), TextAlignment.Left),
                   family, size, c)
        Next

        Console.WriteLine()
        Console.WriteLine("=== B. line spacing, explicit newlines mixed with wrapping (24 mm, 100 mm box) ===")
        TestLineSpacing(family, size)

        Console.WriteLine()
        Console.WriteLine("=== C. line spacing, English only (24 mm, 100 mm box) ===")
        TestLineSpacingEn(family, size)

        Console.WriteLine()
        Console.WriteLine("=== D. characters with no single-stroke data ===")
        For Each sample In New String() {"李明你好", "珺玥犇", "李珺明", "yuè nǐ hǎo", "abc 123"}
            Dim missing = HersheyTextRenderer.FindMissingGlyphs(sample)
            Dim names = String.Join(" ", missing.Select(Function(c) c & "(U+" & AscW(c).ToString("X4") & ")"))
            Console.WriteLine(String.Format("  {0,-14} -> {1}", Quote(sample),
                                            If(missing.Count = 0, "all drawable", names)))
        Next

        Console.WriteLine()
        Console.WriteLine("=== E. fit-to-box (text must not exceed the given width) ===")
        TestFit(family, 100)
        TestFit(family, 60)
    End Sub

    ''' <summary>Ask the renderer for the largest size that keeps the text on one line,
    ''' then prove the claim: at that size it must fit, and 5% larger must not.</summary>
    Private Sub TestFit(family As FontFamily, boxWidth As Double)
        Console.WriteLine($"  box width {boxWidth} mm")
        For Each sample In New String() {"Li Ming", "Wang Xinyi", "李明", "陈伟 ♥ 林静"}
            Dim tb = MakeBox(family, 24, boxWidth)
            Dim fitted = HersheyTextRenderer.FitFontSizeToWidth(tb, sample, boxWidth)

            Dim atFitted = MeasureAt(family, sample, fitted)
            Dim atOver = MeasureAt(family, sample, fitted * 1.05)

            ' The fitter never grows the text, so "already fits" is a pass, not a miss.
            Dim verdict As String
            If atFitted > boxWidth + 0.01 Then
                verdict = "OVERFLOW"
            ElseIf Math.Abs(fitted - 24.0) < 0.01 Then
                verdict = "unchanged (already fits)"
            ElseIf atOver > boxWidth Then
                verdict = "shrunk"
            Else
                verdict = "CHECK"
            End If
            Console.WriteLine(String.Format("    {0,-12} -> {1,6} mm  (at {2} mm: {3} mm wide; +5%: {4} mm)  {5}",
                                            Quote(sample), Fmt(fitted), Fmt(fitted), Fmt(atFitted), Fmt(atOver), verdict))
        Next
    End Sub

    Private Function MeasureAt(family As FontFamily, text As String, size As Double) As Double
        If size <= 0 Then Return 0
        Dim probe As New TextBox With {
            .FontFamily = family,
            .FontSize = size,
            .TextWrapping = TextWrapping.NoWrap
        }
        Dim ft = TextGeometryHelper.CreateFormattedText(probe, text)
        If ft Is Nothing Then Return 0
        Return ft.WidthIncludingTrailingWhitespace
    End Function

    Private Function ResolveFamily() As FontFamily
        If Not IO.Directory.Exists(FontsDir) Then
            Console.WriteLine("fonts dir missing: " & FontsDir)
            Return Nothing
        End If
        Dim source = FontsDir & IO.Path.DirectorySeparatorChar & "#PolyCut Hershey Roman Simplex"
        Return New FontFamily(source)
    End Function

    Private Function MakeBox(family As FontFamily, size As Double, width As Double) As TextBox
        Dim tb As New TextBox With {
            .FontFamily = family,
            .FontSize = size,
            .Width = width,
            .Padding = New Thickness(0),
            .BorderThickness = New Thickness(0)
        }
        Return tb
    End Function

    Private Sub Report(build As Func(Of TextBox, Geometry), family As FontFamily, size As Double, label As String)
        Dim tb = MakeBox(family, size, 400)
        Dim g = build(tb)

        If g Is Nothing Then
            Console.WriteLine(String.Format("  {0,-12} -> nothing (renderer declined)", Quote(label)))
            Return
        End If

        Dim pg = TryCast(g, PathGeometry)
        If pg Is Nothing Then
            Console.WriteLine(String.Format("  {0,-12} -> not a PathGeometry", Quote(label)))
            Return
        End If

        Dim all = Collect(pg)
        Console.WriteLine(String.Format("  {0,-12} -> {1} stroke(s), X {2}..{3}, Y {4}..{5}",
                                        Quote(label), all.Count,
                                        Fmt(all.Min(Function(b) b.X0)), Fmt(all.Max(Function(b) b.X1)),
                                        Fmt(all.Min(Function(b) b.Y0)), Fmt(all.Max(Function(b) b.Y1))))
        For i = 0 To all.Count - 1
            Dim b = all(i)
            Dim ends = If(b.Points <= 3, "  " & b.Ends, "")
            Console.WriteLine(String.Format("        #{0}: X {1}..{2}  Y {3}..{4}  ({5} pt){6}",
                                            i, Fmt(b.X0), Fmt(b.X1), Fmt(b.Y0), Fmt(b.Y1), b.Points, ends))
        Next
    End Sub

    Private Class Box
        Public X0, X1, Y0, Y1 As Double
        Public Points As Integer
        Public Ends As String
    End Class

    Private Function Collect(pg As PathGeometry) As List(Of Box)
        Dim result As New List(Of Box)()
        For Each fig In pg.Figures
            Dim pts As New List(Of Point)()
            pts.Add(fig.StartPoint)
            For Each seg In fig.Segments
                Dim ls = TryCast(seg, LineSegment)
                If ls IsNot Nothing Then pts.Add(ls.Point)
            Next
            If pts.Count = 0 Then Continue For
            Dim b As New Box With {
                .X0 = pts.Min(Function(p) p.X), .X1 = pts.Max(Function(p) p.X),
                .Y0 = pts.Min(Function(p) p.Y), .Y1 = pts.Max(Function(p) p.Y),
                .Points = pts.Count,
                .Ends = String.Join(" > ", pts.Select(Function(p) String.Format("({0},{1})", Fmt(p.X), Fmt(p.Y))))
            }
            result.Add(b)
        Next
        Return result
    End Function

    ''' <summary>Group strokes into visual rows by their vertical extent and report the
    ''' gap between consecutive rows. Wrapped rows and explicitly broken rows must measure
    ''' the same, otherwise a mixed paragraph falls apart on paper.</summary>
    Private Sub TestLineSpacing(family As FontFamily, size As Double)
        Dim tb = MakeBox(family, size, 100)
        tb.TextWrapping = TextWrapping.Wrap

        Dim text = "生字卡第一行内容很长需要折行才能放得下" & vbLf &
                   "第二行短" & vbLf &
                   "第三行也很长需要折行才能放得下继续写更多内容"
        RunSpacing(tb, text)
    End Sub

    Private Sub TestLineSpacingEn(family As FontFamily, size As Double)
        Dim tb = MakeBox(family, size, 100)
        tb.TextWrapping = TextWrapping.Wrap

        Dim text = "The quick brown fox jumps over the lazy dog again" & vbCrLf &
                   "Short line" & vbCrLf &
                   "Pack my box with five dozen liquor jugs for the trip"
        RunSpacing(tb, text)
    End Sub

    Private Sub RunSpacing(tb As TextBox, text As String)
        Console.WriteLine("  text: " & Quote(text.Replace(vbCrLf, "\n").Replace(vbLf, "\n")))

        Dim g = HersheyTextRenderer.TryBuildGeometry(tb, text, New Point(0, 0), TextAlignment.Left)
        Dim pg = TryCast(g, PathGeometry)
        If pg Is Nothing Then
            Console.WriteLine("    -> nothing")
            Return
        End If

        Dim boxes = Collect(pg)
        If boxes.Count = 0 Then
            Console.WriteLine("    -> no strokes")
            Return
        End If

        ' Rows are assigned against the baseline the renderer itself steps by, rather
        ' than by clustering stroke bounding boxes: letters on one line differ in height
        ' by a whole em (ascenders, descenders), which split a single row into several.
        Dim probe = TextGeometryHelper.CreateFormattedText(tb, "Mg")
        Dim lineHeight As Double = probe.Height
        If ContainsNonAscii(text) Then lineHeight = Math.Max(lineHeight, tb.FontSize * 1.7)

        Dim maxBottom = boxes.Max(Function(b) b.Y1)
        Dim baselines As New List(Of Double)()
        Dim yNext = probe.Baseline
        Do While yNext <= maxBottom + tb.FontSize
            baselines.Add(yNext)
            yNext += lineHeight
        Loop

        Dim rowsData As New List(Of List(Of Box))()
        For i = 0 To baselines.Count - 1
            rowsData.Add(New List(Of Box)())
        Next

        For Each b In boxes
            Dim best = 0
            For i = 1 To baselines.Count - 1
                If Math.Abs(b.Y1 - baselines(i)) < Math.Abs(b.Y1 - baselines(best)) Then best = i
            Next
            rowsData(best).Add(b)
        Next

        ' Drop trail-off rows that collected nothing.
        rowsData.RemoveAll(Function(r) r.Count = 0)

        Console.WriteLine(String.Format("    {0} stroke(s), {1} row(s), line height {2} mm",
                                        boxes.Count, rowsData.Count, Fmt(lineHeight)))
        For i = 0 To rowsData.Count - 1
            Dim r = rowsData(i)
            Dim top = r.Min(Function(b) b.Y0)
            Dim bottom = r.Max(Function(b) b.Y1)
            Dim note = ""
            If i > 0 Then
                note = String.Format("  | baseline-to-baseline {0} mm",
                                     Fmt(baselines(i) - baselines(i - 1)))
            End If
            Console.WriteLine(String.Format("      row {0}: {1} stroke(s), Y {2}..{3}{4}",
                                            i, r.Count, Fmt(top), Fmt(bottom), note))
        Next
    End Sub

    Private Function ContainsNonAscii(text As String) As Boolean
        For Each ch In text
            If AscW(ch) > 127 Then Return True
        Next
        Return False
    End Function

    Private Function Quote(s As String) As String
        Return """" & s & """"
    End Function

    Private Function Fmt(v As Double) As String
        Return v.ToString("0.0", CultureInfo.InvariantCulture)
    End Function

End Module
