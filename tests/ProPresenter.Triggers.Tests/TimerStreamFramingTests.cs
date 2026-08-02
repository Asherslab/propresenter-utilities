using ProPresenter.Api;

namespace ProPresenter.Triggers.Tests;

/// <summary>
/// Framing of the chunked timer stream. ProPresenter separates documents with a blank line but does
/// not terminate the last one, so a delimiter-based split strands the most recent tick — which is the
/// normal state of affairs when no timer is running.
/// </summary>
public class TimerStreamFramingTests
{
    private const string Doc = """[{"id":{"uuid":"a","name":"T","index":0},"time":"00:05:00","state":"stopped"}]""";

    [Fact]
    public void Finds_a_single_document_with_no_trailing_separator()
    {
        var end = TimerStream.FindDocumentEnd(Doc, out var start);

        Assert.Equal(0, start);
        Assert.Equal(Doc.Length, end);
    }

    [Fact]
    public void Finds_the_first_of_several_documents()
    {
        var buffer = Doc + "\n\n" + Doc;

        var end = TimerStream.FindDocumentEnd(buffer, out var start);

        Assert.Equal(0, start);
        Assert.Equal(Doc, buffer[start..end]);
    }

    [Fact]
    public void Skips_leading_whitespace_left_by_a_previous_separator()
    {
        var buffer = "\n\n" + Doc;

        var end = TimerStream.FindDocumentEnd(buffer, out var start);

        Assert.Equal(2, start);
        Assert.Equal(Doc, buffer[start..end]);
    }

    [Theory]
    [InlineData("""[{"id":""")]
    [InlineData("""[{"id":{"uuid":"a"}""")]
    [InlineData("")]
    [InlineData("   ")]
    public void Reports_an_incomplete_document(string partial)
    {
        Assert.Equal(-1, TimerStream.FindDocumentEnd(partial, out _));
    }

    [Fact]
    public void Brackets_inside_strings_do_not_end_the_document()
    {
        // A timer named "]" would otherwise truncate the document.
        var tricky = """[{"name":"] not the end [","time":"00:00:01"}]""";

        var end = TimerStream.FindDocumentEnd(tricky, out var start);

        Assert.Equal(tricky.Length, end);
        Assert.Equal(tricky, tricky[start..end]);
    }

    [Fact]
    public void Escaped_quotes_inside_strings_are_handled()
    {
        var tricky = """[{"name":"a \" ] b","time":"00:00:01"}]""";

        Assert.Equal(tricky.Length, TimerStream.FindDocumentEnd(tricky, out _));
    }

    [Fact]
    public void A_document_split_across_chunks_is_only_returned_once_complete()
    {
        var head = Doc[..40];
        Assert.Equal(-1, TimerStream.FindDocumentEnd(head, out _));

        Assert.Equal(Doc.Length, TimerStream.FindDocumentEnd(head + Doc[40..], out _));
    }
}
