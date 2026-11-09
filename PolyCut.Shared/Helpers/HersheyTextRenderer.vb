Imports System.IO
Imports System.Reflection
Imports System.Text.Json
Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media

''' <summary>
''' Draws text with the bundled Hershey single-line fonts as open polylines.
'''
''' Routing text through a TrueType glyph would produce a closed outline - a hairline
''' band - and every plotter stroke would then be traced twice (once for each side).
''' The .jhf data behind these fonts is already a set of open polylines, so this module
''' builds the geometry straight from it: one stroke in, one path out.
''' </summary>
Public Module HersheyTextRenderer

    Private Const BundledFamilyPrefix As String = "PolyCut Hershey"

    Friend Class GlyphData
        Public Property Left As Double
        Public Property Right As Double
        Public Property Strokes As List(Of List(Of Point))
    End Class

    Friend Class FontData
        Public Property Name As String
        Public Property Baseline As Double = 9
        Public Property UnitsPerEm As Double = 30
        Public Property Glyphs As Dictionary(Of Integer, GlyphData)
    End Class

    ''' <summary>
    ''' Centre lines of the common Chinese characters, in a 1024-unit em with the
    ''' baseline at y = 0 and y pointing up. Shared by every bundled font: the
    ''' Hershey sets only cover ASCII, and a Chinese glyph has no single-stroke
    ''' form in them, so the hand-written stroke medians come from here.
    ''' </summary>
    Private Const HanziUnitsPerEm As Double = 1024

    ''' <summary>Where a normal Han glyph's bottom sits in its 1024-unit em. The stroke
    ''' data runs 0 (top of the box) to 1024, with the ink of a typical character ending
    ''' around 900. Anchor on that, not on 0, so a Chinese character rests on the same
    ''' baseline as the Latin letters next to it - otherwise the whole glyph hangs a full
    ''' em BELOW the baseline and every mixed line shows the English floating high.</summary>
    Private Const HanziBaseline As Double = 900
    Private Const HanziResourceName As String = "hanzi-strokes.json"

    Private _fonts As Dictionary(Of String, FontData)
    Private _hanzi As Dictionary(Of Integer, List(Of List(Of Point)))
    Private ReadOnly _loadLock As New Object()

    ''' <summary>
    ''' True when the font family belongs to the bundled single-line set.
    ''' </summary>
    Public Function IsSingleLineFont(family As FontFamily) As Boolean
        If family Is Nothing OrElse String.IsNullOrWhiteSpace(family.Source) Then Return False
        Return family.Source.IndexOf(BundledFamilyPrefix, StringComparison.OrdinalIgnoreCase) >= 0
    End Function

    ''' <summary>
    ''' Build open-path text geometry in the TextBox's own coordinate space, or Nothing
    ''' when the font is not a bundled single-line font (the caller then falls back to
    ''' the normal outline renderer).
    ''' </summary>
    Public Function TryBuildGeometry(textBox As TextBox, text As String, origin As Point,
                                     alignment As TextAlignment) As Geometry
        If textBox Is Nothing OrElse String.IsNullOrEmpty(text) Then Return Nothing
        If Not IsSingleLineFont(textBox.FontFamily) Then Return Nothing

        Dim font = GetFontData(textBox.FontFamily)
        If font Is Nothing Then Return Nothing

        Dim ppd = TextGeometryHelper.GetPixelsPerDip(textBox)
        Dim scale As Double = textBox.FontSize / font.UnitsPerEm
        If scale <= 0 Then Return Nothing

        ' Line metrics come from the real formatter so wrapped/large text lines up with
        ' what the TextBox shows on screen.
        Dim probe = TextGeometryHelper.CreateFormattedText(textBox, "Mg", ppd)
        If probe Is Nothing Then Return Nothing
        Dim lineHeight As Double = probe.Height

        ' The probe measures this font's own line box. CJK falls back to a system font
        ' with a taller line box, so a mixed run would plot the Chinese rows closer
        ' together than they appear on screen and the lines would overlap.
        If ContainsWideChar(text) Then
            lineHeight = Math.Max(lineHeight, textBox.FontSize * 1.7)
        End If

        Dim boxWidth As Double = textBox.ActualWidth
        If Double.IsNaN(boxWidth) OrElse boxWidth <= 0 Then boxWidth = textBox.Width

        Dim group As New PathGeometry()
        Dim paragraphs = text.Replace(vbCr, "").Split(CChar(vbLf))
        Dim spaceAdvance As Double = 0

        Dim spaceGlyph As GlyphData = Nothing
        If font.Glyphs.TryGetValue(32, spaceGlyph) Then spaceAdvance = (spaceGlyph.Right - spaceGlyph.Left) * scale
        If spaceAdvance <= 0 Then spaceAdvance = 8 * scale

        ' Chinese is a full-width glyph: one em per character, drawn from the stroke
        ' medians rather than from this font's (ASCII-only) glyph table.
        Dim hanziScale As Double = textBox.FontSize / HanziUnitsPerEm
        Dim hanziAdvance As Double = textBox.FontSize
        Dim hanzi As Dictionary(Of Integer, List(Of List(Of Point))) = Nothing
        If ContainsWideChar(text) Then hanzi = GetHanziData()

        ' A CJK character with no stroke data must not be skipped: the glyph would
        ' silently vanish from the plate (a name would lose a character). Fall back to
        ' the outline renderer for the whole box so every character is at least present.
        If hanzi IsNot Nothing Then
            For Each ch In text
                Dim c = AscW(ch)
                If c >= &H3400 AndAlso c <= &H9FFF AndAlso Not hanzi.ContainsKey(c) Then
                    Return Nothing
                End If
            Next
        End If

        ' The TextBox wraps by default, so the plotted text must wrap at the same width
        ' (measured with our own advances) or it would run outside the box.
        Dim wrapWidth As Double = Double.PositiveInfinity
        If textBox.TextWrapping = TextWrapping.Wrap Then
            Dim inner = boxWidth - textBox.BorderThickness.Left - textBox.BorderThickness.Right -
                        textBox.Padding.Left - textBox.Padding.Right
            If inner > 0 AndAlso Not Double.IsNaN(inner) Then wrapWidth = inner
        End If

        Dim allLines As New List(Of String)
        For Each paragraph In paragraphs
            allLines.AddRange(WrapLine(textBox, paragraph, ppd, wrapWidth))
        Next

        For lineIndex = 0 To allLines.Count - 1
            Dim lineText = allLines(lineIndex)
            Dim measureText = If(lineText.Length = 0, " ", lineText)
            Dim lineText2 = TextGeometryHelper.CreateFormattedText(textBox, measureText, ppd)
            If lineText2 Is Nothing Then Continue For

            Dim baselineY As Double = origin.Y + lineIndex * lineHeight + lineText2.Baseline
            Dim lineWidth As Double = lineText2.WidthIncludingTrailingWhitespace
            Dim startX As Double = origin.X

            Select Case alignment
                Case TextAlignment.Center
                    If Not Double.IsNaN(boxWidth) AndAlso boxWidth > 0 Then startX += (boxWidth - lineWidth) / 2
                Case TextAlignment.Right
                    If Not Double.IsNaN(boxWidth) AndAlso boxWidth > 0 Then startX += boxWidth - lineWidth
            End Select

            Dim penX As Double = startX

            For Each ch In lineText
                Dim code = AscW(ch)
                Dim glyph As GlyphData = Nothing

                ' Accented Latin (è ǎ ō ǘ ǐ ǔ - every pinyin tone letter) is not in the
                ' Hershey sets. Split it into its base letter plus a combining mark and
                ' draw the mark as a short stroke above the letter, otherwise a pinyin
                ' annotation silently loses its tone.
                Dim baseCode As Integer = code
                Dim accentMark As Integer = 0
                Dim secondMark As Integer = 0
                If code > 127 Then
                    TryDecomposeLatin(code, baseCode, accentMark, secondMark)
                End If

                ' A wide (CJK) character takes a full em and is plotted from the
                ' shared stroke medians. Punctuation with no median still advances a
                ' full em so the line keeps its shape instead of collapsing.
                If baseCode > 127 Then
                    Dim hanziStrokes As List(Of List(Of Point)) = Nothing
                    If hanzi IsNot Nothing Then hanzi.TryGetValue(baseCode, hanziStrokes)

                    ' No stroke data for this character (and no tone decomposition got here).
                    ' Dropping it would lose a character silently, so decline and let the
                    ' caller draw the whole box as outlines.
                    If hanziStrokes Is Nothing Then Return Nothing

                    For Each hanziStroke In hanziStrokes
                        If hanziStroke Is Nothing OrElse hanziStroke.Count < 2 Then Continue For

                        Dim hzFigure As New PathFigure With {
                            .IsClosed = False,
                            .IsFilled = True,
                            .StartPoint = New Point(penX + hanziStroke(0).X * hanziScale,
                                                    baselineY + (hanziStroke(0).Y - HanziBaseline) * hanziScale)
                        }
                        For i = 1 To hanziStroke.Count - 1
                            hzFigure.Segments.Add(New LineSegment(
                                New Point(penX + hanziStroke(i).X * hanziScale,
                                          baselineY + (hanziStroke(i).Y - HanziBaseline) * hanziScale), True))
                        Next
                        group.Figures.Add(hzFigure)
                    Next

                    penX += hanziAdvance
                    Continue For
                End If

                If Not font.Glyphs.TryGetValue(baseCode, glyph) OrElse glyph Is Nothing Then
                    penX += spaceAdvance
                    Continue For
                End If

                For Each stroke In glyph.Strokes
                    If stroke Is Nothing OrElse stroke.Count < 2 Then Continue For

                    ' IsFilled must stay True: WPF's GetFlattenedPathGeometry drops
                    ' unfilled figures, which would silently produce an empty toolpath.
                    ' IsClosed=False is what keeps a stroke to a single pass.
                    Dim figure As New PathFigure With {
                        .IsClosed = False,
                        .IsFilled = True,
                        .StartPoint = ToDevice(stroke(0), penX, baselineY, glyph, font, scale)
                    }

                    For i = 1 To stroke.Count - 1
                        figure.Segments.Add(New LineSegment(ToDevice(stroke(i), penX, baselineY, glyph, font, scale), True))
                    Next

                    group.Figures.Add(figure)
                Next

                Dim glyphAdvance = (glyph.Right - glyph.Left) * scale

                If accentMark <> 0 Then
                    ' Sit the tone mark just above whichever letter it belongs to. A fixed
                    ' offset floated far above short letters like e, which have no ascender.
                    Dim topY As Double = baselineY
                    For Each stroke In glyph.Strokes
                        For Each p In stroke
                            Dim deviceY = baselineY + (p.Y - font.Baseline) * scale
                            If deviceY < topY Then topY = deviceY
                        Next
                    Next

                    Dim centerX = penX + glyphAdvance / 2
                    ' 0.06 em left a visible gap at plate sizes (1.4 mm at 24 mm type) and
                    ' read as a floating dash rather than a tone mark on the letter.
                    Dim bottomY = topY - textBox.FontSize * 0.03

                    If accentMark = &H308 Then
                        ' ü: the two dots are the only mark.
                        AddAccentMark(group, accentMark, centerX, bottomY, textBox.FontSize)
                        If secondMark <> 0 Then
                            AddAccentMark(group, secondMark, centerX,
                                          bottomY - textBox.FontSize * 0.12, textBox.FontSize)
                        End If
                    ElseIf secondMark = &H308 Then
                        ' ǚ ǜ ǘ: the dots hug the letter and the tone mark rides above them,
                        ' matching how pinyin is normally typeset.
                        AddAccentMark(group, &H308, centerX, bottomY, textBox.FontSize)
                        AddAccentMark(group, accentMark, centerX,
                                      bottomY - textBox.FontSize * 0.12, textBox.FontSize)
                    Else
                        AddAccentMark(group, accentMark, centerX, bottomY, textBox.FontSize)
                    End If
                End If

                penX += glyphAdvance
            Next
        Next

        If group.Figures.Count = 0 Then
            Return Nothing
        End If
        group.Freeze()
        Return group
    End Function

    ''' <summary>Splits one paragraph into lines no wider than the box. Widths are measured
    ''' with the same FormattedText the TextBox lays out with, so the plotted line breaks
    ''' land where the user saw them, including CJK fallback glyphs the Hershey fonts have
    ''' no advance for.</summary>
    Private Function WrapLine(textBox As TextBox, text As String, ppd As Double,
                              maxWidth As Double) As List(Of String)
        Dim result As New List(Of String)
        If Double.IsNaN(maxWidth) OrElse Double.IsPositiveInfinity(maxWidth) OrElse maxWidth <= 0 Then
            result.Add(text)
            Return result
        End If

        Dim current As New Text.StringBuilder()

        For Each ch In text
            Dim trial = current.ToString() & ch
            If current.Length > 0 AndAlso MeasureWidth(textBox, trial, ppd) > maxWidth Then
                result.Add(current.ToString())
                current.Clear()
            End If

            current.Append(ch)
        Next

        result.Add(current.ToString())
        Return result
    End Function

    Private Function MeasureWidth(textBox As TextBox, s As String, ppd As Double) As Double
        If String.IsNullOrEmpty(s) Then Return 0
        Dim ft = TextGeometryHelper.CreateFormattedText(textBox, s, ppd)
        If ft Is Nothing Then Return 0
        Return ft.WidthIncludingTrailingWhitespace
    End Function

    ''' <summary>Split an accented Latin letter into its ASCII base letter and the
    ''' combining marks (at most one tone mark plus a diaeresis), so pinyin tone marks can
    ''' be plotted. ǚ ǜ ǘ ǖ decompose to three code points (letter + tone + diaeresis) and
    ''' used to fall through here and vanish from the plate entirely.</summary>
    Private Function TryDecomposeLatin(code As Integer, ByRef baseCode As Integer,
                                       ByRef mark As Integer, ByRef secondMark As Integer) As Boolean
        baseCode = code
        mark = 0
        secondMark = 0

        If code < 128 OrElse code > &H24F Then Return False

        Try
            Dim decomposed = ChrW(code).ToString().Normalize(System.Text.NormalizationForm.FormD)
            If decomposed.Length < 2 Then Return False

            Dim b = AscW(decomposed(0))
            If b >= 128 Then Return False

            For i = 1 To decomposed.Length - 1
                Dim m = AscW(decomposed(i))
                If m < &H300 OrElse m > &H36F Then Return False
                If mark = 0 Then
                    mark = m
                ElseIf secondMark = 0 Then
                    secondMark = m
                End If
            Next

            If mark = 0 Then Return False

            baseCode = b
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Draw a tone mark above a letter. Marks are short open strokes, so they
    ''' plot as a single pass like everything else. <paramref name="bottomY"/> is the
    ''' lowest point any part of the mark may reach: acute, grave and caron all slant
    ''' downwards, so anchoring them by their top instead buried the low end inside the
    ''' letter they annotate.</summary>
    Private Sub AddAccentMark(group As PathGeometry, code As Integer, centerX As Double, bottomY As Double, size As Double)
        Dim w = size * 0.16
        Dim h = size * 0.11
        Dim y = bottomY

        Dim pts As Point()
        Select Case code
            Case &H304     ' macron (first tone): a flat bar
                pts = {New Point(centerX - w, y), New Point(centerX + w, y)}
            Case &H301     ' acute (second tone): rises to the right
                pts = {New Point(centerX - w, y), New Point(centerX + w, y - h * 1.6)}
            Case &H300     ' grave (fourth tone): falls to the right
                pts = {New Point(centerX - w, y - h * 1.6), New Point(centerX + w, y)}
            Case &H30C     ' caron (third tone): a v opening upwards
                pts = {New Point(centerX - w, y - h), New Point(centerX, y), New Point(centerX + w, y - h)}
            Case &H308     ' diaeresis (ü)
                pts = Nothing
                AddAccentDot(group, centerX - w * 0.5, y)
                AddAccentDot(group, centerX + w * 0.5, y)
            Case Else
                Return
        End Select

        If pts Is Nothing OrElse pts.Length < 2 Then Return

        Dim figure As New PathFigure With {
            .IsClosed = False,
            .IsFilled = True,
            .StartPoint = pts(0)
        }
        For i = 1 To pts.Length - 1
            figure.Segments.Add(New LineSegment(pts(i), True))
        Next
        group.Figures.Add(figure)
    End Sub

    Private Sub AddAccentDot(group As PathGeometry, centerX As Double, y As Double)
        Dim r = 0.6
        Dim figure As New PathFigure With {
            .IsClosed = False,
            .IsFilled = True,
            .StartPoint = New Point(centerX - r, y)
        }
        figure.Segments.Add(New LineSegment(New Point(centerX + r, y), True))
        group.Figures.Add(figure)
    End Sub

    Private Function ToDevice(p As Point, penX As Double, baselineY As Double, glyph As GlyphData,
                              font As FontData, scale As Double) As Point
        Return New Point(
            penX + (p.X - glyph.Left) * scale,
            baselineY + (p.Y - font.Baseline) * scale)
    End Function

    Private Function GetFontData(family As FontFamily) As FontData
        EnsureLoaded()
        If _fonts Is Nothing Then Return Nothing

        Dim source = family.Source
        If source Is Nothing Then Return Nothing

        Dim hashIndex = source.LastIndexOf("#"c)
        Dim name = If(hashIndex >= 0 AndAlso hashIndex < source.Length - 1, source.Substring(hashIndex + 1), source)

        Dim data As FontData = Nothing
        If _fonts.TryGetValue(name, data) Then Return data
        Return Nothing
    End Function

    Private Sub EnsureLoaded()
        If _fonts IsNot Nothing Then Return

        SyncLock _loadLock
            If _fonts IsNot Nothing Then Return

            Dim loaded As New Dictionary(Of String, FontData)(StringComparer.OrdinalIgnoreCase)

            Try
                Dim asm = GetType(HersheyTextRenderer).Assembly
                Dim resourceName = asm.GetManifestResourceNames().
                    FirstOrDefault(Function(n) n.EndsWith("hershey-glyphs.json", StringComparison.OrdinalIgnoreCase))

                If resourceName IsNot Nothing Then
                    Using stream = asm.GetManifestResourceStream(resourceName)
                        Using reader As New StreamReader(stream)
                            Dim json = reader.ReadToEnd()
                            Dim doc = JsonDocument.Parse(json)

                            For Each fontElement In doc.RootElement.GetProperty("fonts").EnumerateArray()
                                Dim font As New FontData With {
                                    .Name = fontElement.GetProperty("name").GetString(),
                                    .Baseline = fontElement.GetProperty("baseline").GetDouble(),
                                    .UnitsPerEm = fontElement.GetProperty("unitsPerEm").GetDouble(),
                                    .Glyphs = New Dictionary(Of Integer, GlyphData)()
                                }

                                For Each glyphProperty In fontElement.GetProperty("glyphs").EnumerateObject()
                                    Dim code As Integer
                                    If Not Integer.TryParse(glyphProperty.Name, code) Then Continue For

                                    Dim strokes As New List(Of List(Of Point))()
                                    For Each strokeElement In glyphProperty.Value.GetProperty("strokes").EnumerateArray()
                                        Dim points As New List(Of Point)()
                                        For Each pointElement In strokeElement.EnumerateArray()
                                            points.Add(New Point(pointElement(0).GetDouble(), pointElement(1).GetDouble()))
                                        Next
                                        If points.Count >= 2 Then strokes.Add(points)
                                    Next

                                    font.Glyphs(code) = New GlyphData With {
                                        .Left = glyphProperty.Value.GetProperty("left").GetDouble(),
                                        .Right = glyphProperty.Value.GetProperty("right").GetDouble(),
                                        .Strokes = strokes
                                    }
                                Next

                                If font.Name IsNot Nothing Then loaded(font.Name) = font
                            Next
                        End Using
                    End Using
                Else
                    Debug.WriteLine("hershey-glyphs.json was not embedded")
                End If
            Catch ex As Exception
                Debug.WriteLine("Failed to load Hershey glyph data: " & ex.Message)
            End Try

            _fonts = loaded
        End SyncLock
    End Sub

    ''' <summary>True when the string holds a character outside ASCII, i.e. one that
    ''' needs the wide-glyph path.</summary>
    Private Function ContainsWideChar(text As String) As Boolean
        For Each ch In text
            If AscW(ch) > 127 Then Return True
        Next
        Return False
    End Function

    ''' <summary>Stroke medians for the common Chinese characters, loaded once from the
    ''' embedded table. Returns an empty (never Nothing) map on failure so the caller
    ''' simply drops the character instead of throwing.</summary>
    Private Function GetHanziData() As Dictionary(Of Integer, List(Of List(Of Point)))
        If _hanzi IsNot Nothing Then Return _hanzi

        SyncLock _loadLock
            If _hanzi IsNot Nothing Then Return _hanzi

            Dim loaded As New Dictionary(Of Integer, List(Of List(Of Point)))()

            Try
                Dim asm = GetType(HersheyTextRenderer).Assembly
                Dim resourceName = asm.GetManifestResourceNames().
                    FirstOrDefault(Function(n) n.EndsWith(HanziResourceName, StringComparison.OrdinalIgnoreCase))

                If resourceName IsNot Nothing Then
                    Using stream = asm.GetManifestResourceStream(resourceName)
                        Using reader As New StreamReader(stream)
                            Dim doc = JsonDocument.Parse(reader.ReadToEnd())
                            For Each glyphProperty In doc.RootElement.GetProperty("glyphs").EnumerateObject()
                                Dim code As Integer
                                If Not Integer.TryParse(glyphProperty.Name, code) Then Continue For

                                Dim strokes As New List(Of List(Of Point))()
                                For Each strokeElement In glyphProperty.Value.GetProperty("strokes").EnumerateArray()
                                    Dim points As New List(Of Point)()
                                    For Each pointElement In strokeElement.EnumerateArray()
                                        points.Add(New Point(pointElement(0).GetDouble(), pointElement(1).GetDouble()))
                                    Next
                                    If points.Count >= 2 Then strokes.Add(points)
                                Next
                                If strokes.Count > 0 Then loaded(code) = strokes
                            Next
                        End Using
                    End Using
                Else
                    Debug.WriteLine("hanzi-strokes.json was not embedded")
                End If
            Catch ex As Exception
                Debug.WriteLine("Failed to load Chinese stroke data: " & ex.Message)
            End Try

            _hanzi = loaded
        End SyncLock

        Return _hanzi
    End Function

    ''' <summary>Largest font size at which <paramref name="text"/> still fits
    ''' <paramref name="maxWidth"/> without wrapping, measured with the same formatter the
    ''' plotter uses. Twenty name plates have wildly different lengths, so picking a size
    ''' that keeps the longest one on a single line is the difference between "Li Ming"
    ''' fitting its box and "Wang Xinyi" spilling over the edge.</summary>
    Public Function FitFontSizeToWidth(source As TextBox, text As String, maxWidth As Double) As Double
        If source Is Nothing Then Return 0
        If String.IsNullOrEmpty(text) OrElse maxWidth <= 0 Then Return source.FontSize

        ' A detached probe carries the same typeface but a size we can vary freely; the
        ' real TextBox belongs to the canvas and must not be disturbed while measuring.
        Dim probe As New TextBox With {
            .FontFamily = source.FontFamily,
            .FontStyle = source.FontStyle,
            .FontWeight = source.FontWeight,
            .FontStretch = source.FontStretch,
            .FlowDirection = source.FlowDirection,
            .TextWrapping = TextWrapping.NoWrap
        }
        Dim ppd = TextGeometryHelper.GetPixelsPerDip(source)

        ' Multi-line text has to satisfy the width on every paragraph, not just the first.
        Dim paragraphs = text.Replace(vbCr, "").Split(CChar(vbLf))

        ' Never grow the text: the button says "fit inside the box", and a user who set 18 pt
        ' does not expect it to jump to 50 pt just because the box is roomy.
        Dim lo As Double = 0.5
        Dim hi As Double = Math.Max(source.FontSize, 0.5)

        For i = 0 To 23
            Dim mid = (lo + hi) / 2
            probe.FontSize = mid
            If Fits(probe, paragraphs, ppd, maxWidth) Then
                lo = mid
            Else
                hi = mid
            End If
        Next

        Return lo
    End Function

    Private Function Fits(probe As TextBox, paragraphs As String(), ppd As Double, maxWidth As Double) As Boolean
        For Each paragraph In paragraphs
            If paragraph.Length = 0 Then Continue For
            If MeasureWidth(probe, paragraph, ppd) > maxWidth Then Return False
        Next
        Return True
    End Function

    ''' <summary>Characters in <paramref name="text"/> that the single-line renderer cannot
    ''' draw. A text box holding any of them falls back to outline glyphs as a whole, so
    ''' the caller can name the offending characters instead of leaving the user to wonder
    ''' why the line art turned into double lines. Most often these are GBK-extended
    ''' characters (珺 玥 犇 ...) that have no open stroke-median data.</summary>
    Public Function FindMissingGlyphs(text As String) As List(Of Char)
        Dim missing As New List(Of Char)
        If String.IsNullOrEmpty(text) Then Return missing

        Dim hanzi = GetHanziData()
        Dim seen As New HashSet(Of Integer)

        For Each ch In text
            Dim code = AscW(ch)
            If code <= 127 Then Continue For
            If Not seen.Add(code) Then Continue For

            ' An accented Latin letter becomes its ASCII base plus a drawn tone mark.
            Dim baseCode = code, mark = 0, secondMark = 0
            If TryDecomposeLatin(code, baseCode, mark, secondMark) Then Continue For

            If hanzi.ContainsKey(code) Then Continue For

            missing.Add(ch)
        Next

        Return missing
    End Function

End Module
