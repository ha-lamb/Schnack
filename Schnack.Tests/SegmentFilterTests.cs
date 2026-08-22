using Schnack.Services.Internal;

namespace Schnack.Tests;

public class SegmentFilterTests
{
    private static SpeechSegment Seg(string text, float probability) => new(text, probability);

    [Fact]
    public void KeepsSegmentsAboveTheThreshold()
    {
        var result = SegmentFilter.Apply([Seg("Hallo Welt.", 0.95f)]);

        Assert.Equal("Hallo Welt.", result.Text);
        Assert.Empty(result.DroppedProbabilities);
    }

    [Fact]
    public void DropsSegmentsBelowTheThreshold()
    {
        var result = SegmentFilter.Apply([Seg("Vielen Dank.", 0.72f)]);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal([0.72f], result.DroppedProbabilities);
    }

    [Fact]
    public void KeepsTheOrderOfWhatSurvives()
    {
        var result = SegmentFilter.Apply(
        [
            Seg("Erster Satz. ", 0.96f),
            Seg("Vielen Dank.", 0.70f),
            Seg("Zweiter Satz.", 0.94f)
        ]);

        Assert.Equal("Erster Satz. Zweiter Satz.", result.Text);
        Assert.Single(result.DroppedProbabilities);
    }

    [Fact]
    public void ExactlyAtTheThreshold_IsKept()
    {
        var result = SegmentFilter.Apply([Seg("Grenzfall", SegmentFilter.MinProbability)]);

        Assert.Equal("Grenzfall", result.Text);
        Assert.Empty(result.DroppedProbabilities);
    }

    [Fact]
    public void AllSegmentsBelowThreshold_YieldsEmptyText()
    {
        // Kein Rückfall auf „dann eben alles behalten": Liegt alles darunter, wurde vermutlich
        // nicht gesprochen. Der Orchestrator meldet das als „keine Sprache erkannt" — ehrlicher,
        // als erfundenen Text einzufügen. Belegt am Fall „nur Raumrauschen", bei dem Whisper
        // die Vokabelliste selbst als Transkript zurückgab.
        var result = SegmentFilter.Apply([Seg("Aptean, TVN", 0.54f), Seg("Vielen Dank.", 0.65f)]);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(2, result.DroppedProbabilities.Count);
    }

    [Fact]
    public void EmptyInput_YieldsEmptyText()
    {
        var result = SegmentFilter.Apply([]);

        Assert.Equal(string.Empty, result.Text);
        Assert.Empty(result.DroppedProbabilities);
    }

    // ── Die Schwelle gegen die Messwerte absichern ─────────────────────────
    //
    // Gemessen mit large-v3-turbo an synthetischer Sprache mit Raumklang-Anhang. Verschiebt
    // jemand MinProbability, schlagen diese beiden Tests an, bevor echte Sprache verlorengeht.

    [Theory]
    [InlineData(0.9478f)]   // kürzester gemessener Wert echter Sprache
    [InlineData(0.9532f)]
    [InlineData(0.9591f)]   // leise gesprochen, auf ein Viertel gedämpft
    [InlineData(0.9920f)]
    public void MeasuredRealSpeech_StaysAboveTheThreshold(float probability)
    {
        Assert.True(probability >= SegmentFilter.MinProbability,
            $"Echte Sprache mit {probability} würde verworfen — die Schwelle ist zu hoch.");
    }

    [Theory]
    [InlineData(0.6465f)]   // "Vielen Dank." nach langem Rauschen
    [InlineData(0.7212f)]
    [InlineData(0.7346f)]
    [InlineData(0.7693f)]   // verfälschte Wiederholung eines echten Satzes
    [InlineData(0.7702f)]   // "Vielen Dank." trotz hoher MinProbability und Zeichenrate
    [InlineData(0.7793f)]   // Vokabelliste als Transkript, bei reinem Rauschen
    public void MeasuredHallucinations_StayBelowTheThreshold(float probability)
    {
        Assert.True(probability < SegmentFilter.MinProbability,
            $"Halluzination mit {probability} würde durchrutschen — die Schwelle ist zu niedrig.");
    }
}
