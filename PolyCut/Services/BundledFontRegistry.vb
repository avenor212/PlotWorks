Imports System.IO
Imports System.Windows.Media

''' <summary>
''' Resolves the single-line (stroke) fonts that ship with PolyCut.
'''
''' These fonts are generated from the public-domain Hershey vector fonts and copied next
''' to the executable under Resources\Fonts. WPF can only address them with a full
''' "directory\#Family" source string, which would end up in project files and break as
''' soon as the app moves. Project files therefore store a stable "bundled:Family" token
''' instead, and this module turns it back into a FontFamily at load time.
''' </summary>
Public Module BundledFontRegistry

    Private Const BundledPrefix As String = "bundled:"

    ''' <summary>Family names in the order they are offered in the font picker.</summary>
    Public Function FamilyNames() As String()
        Return New String() {
            "PolyCut Hershey Script Simplex",
            "PolyCut Hershey Script Complex",
            "PolyCut Hershey Cursive",
            "PolyCut Hershey Gothic English",
            "PolyCut Hershey Roman Simplex"
        }
    End Function

    Public Function FontsDirectory() As String
        Return IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Fonts")
    End Function

    ''' <summary>
    ''' Build a FontFamily for a bundled font, or Nothing when the file is missing or
    ''' unreadable. Only "directory\#Family" in string form resolves in WPF; the
    ''' (Uri, String) constructor silently produces a family with no glyph typeface.
    ''' </summary>
    Public Function TryCreate(familyName As String) As FontFamily
        Try
            Dim dir = FontsDirectory()
            If Not Directory.Exists(dir) Then Return Nothing

            Dim source = dir & IO.Path.DirectorySeparatorChar & "#" & familyName
            Dim family As New FontFamily(source)
            Dim typeface As New Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
            Dim glyphTypeface As GlyphTypeface = Nothing

            If typeface.TryGetGlyphTypeface(glyphTypeface) AndAlso glyphTypeface IsNot Nothing Then
                Return family
            End If
        Catch ex As Exception
            Debug.WriteLine("Bundled font failed: " & familyName & " - " & ex.Message)
        End Try

        Return Nothing
    End Function

    ''' <summary>Value to persist in a project file for the given font.</summary>
    Public Function ToStoredValue(family As FontFamily) As String
        If family Is Nothing OrElse String.IsNullOrWhiteSpace(family.Source) Then Return Nothing

        Dim source = family.Source
        Dim dir = FontsDirectory()

        If source.StartsWith(dir, StringComparison.OrdinalIgnoreCase) Then
            Dim hashIndex = source.LastIndexOf("#"c)
            If hashIndex >= 0 AndAlso hashIndex < source.Length - 1 Then
                Return BundledPrefix & source.Substring(hashIndex + 1)
            End If
        End If

        Return source
    End Function

    ''' <summary>Rebuild a FontFamily from a value written by <see cref="ToStoredValue"/>.</summary>
    Public Function FromStoredValue(stored As String, Optional fallback As String = "Calibri") As FontFamily
        If String.IsNullOrWhiteSpace(stored) Then Return New FontFamily(fallback)

        If stored.StartsWith(BundledPrefix, StringComparison.OrdinalIgnoreCase) Then
            Dim familyName = stored.Substring(BundledPrefix.Length)
            Dim bundled = TryCreate(familyName)
            If bundled IsNot Nothing Then Return bundled

            ' Font file moved or missing: still accept the plain family name so the
            ' project opens instead of failing.
            Return New FontFamily(familyName)
        End If

        Return New FontFamily(stored)
    End Function

End Module
