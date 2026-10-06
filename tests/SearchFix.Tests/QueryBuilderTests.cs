using Azure.Search.Documents.Models;
using SearchFix.Runner;

namespace SearchFix.Tests;

public class CurrentQueryBuilderTests
{
    [Theory]
    [InlineData("ALLOY 5082", "/.*ALLOY 5082.*/|ALLOY 5082")]
    [InlineData("PART-0033", "/.*\"PART-0033\".*/|\"PART-0033\"")]
    [InlineData("\"ALLOY 5082\"", "/.*\\\"ALLOY 5082\\\".*/|\\\"ALLOY 5082\\\"")]
    public void Reproduces_the_examples_from_the_brief(string input, string expected) =>
        Assert.Equal(expected, CurrentQueryBuilder.EncodeSpecialCharacters(input));

    [Fact]
    public void Default_mode_is_Any_and_AND_keyword_switches_to_All()
    {
        Assert.Equal(SearchMode.Any, new CurrentQueryBuilder().Build("ring bearing").Mode);
        var q = new CurrentQueryBuilder().Build("ring AND bearing");
        Assert.Equal(SearchMode.All, q.Mode);
        Assert.DoesNotContain("AND ", q.Text);
    }
}

public class Step2QueryBuilderTests
{
    [Theory]
    [InlineData("", "*")]
    [InlineData("   ", "*")]
    [InlineData("ALLOY 5082", "ALLOY 5082")]
    [InlineData("PART-0022", "PART-0022")]                      // separators pass through
    [InlineData("000_081_835", "000_081_835")]
    [InlineData("O10043.nc", "O10043.nc")]
    [InlineData("\"ALLOY 5082\"", "\"ALLOY 5082\"")]            // phrase preserved
    [InlineData("ST 80\"62", "ST 8062")]                        // unbalanced quote stripped
    [InlineData("\"", "*")]                                     // lone quote → empty → match-all
    [InlineData("a+b (c) [d] x/y", "a\\+b \\(c\\) \\[d\\] x\\/y")]
    [InlineData("-0033 !x", "\\-0033 \\!x")]                    // leading NOT operators escaped
    [InlineData("ring bearing", "ring bearing")]
    [InlineData("\"BAT-1032-2224\u200B\"", "\"BAT-1032-2224\"")]   // zero-width space inside quotes dropped
    [InlineData("M-486300-1/4", "M-486300-1\\/4")]                // '/' would open a regex
    public void Encode_decision_table(string input, string expected) =>
        Assert.Equal(expected, Step2QueryBuilder.Encode(input).Length == 0 ? "*" : Step2QueryBuilder.Encode(input));

    [Fact]
    public void Default_mode_is_All_and_OR_keyword_switches_to_Any()
    {
        Assert.Equal(SearchMode.All, new Step2QueryBuilder().Build("ring bearing").Mode);
        var q = new Step2QueryBuilder().Build("ring OR bearing");
        Assert.Equal(SearchMode.Any, q.Mode);
        Assert.Equal("ring bearing", q.Text);
    }

    [Fact]
    public void Empty_input_becomes_match_all()
    {
        Assert.Equal("*", new Step2QueryBuilder().Build("").Text);
        Assert.Equal("*", new Step2QueryBuilder().Build("\"").Text);
    }
}

public class Step3QueryBuilderTests
{
    [Fact]
    public void Single_identifier_hits_exact_normalized_prefix_and_the_text_field()
    {
        var q = new Step3QueryBuilder().Build("000-081");
        Assert.Equal("content_exact:\"000-081\"^10 OR content_norm:\"000-081\"^5 OR content_prefix:\"000-081\"^2 OR content:(000-081)", q.Text);
        Assert.Equal(SearchMode.All, q.Mode);
        Assert.Equal("identifiers", q.ScoringProfile);
    }

    [Fact]
    public void Bare_type_code_is_exact_match_only()
    {
        var q = new Step3QueryBuilder().Build("RCP");
        Assert.Equal("content_exact:\"RCP\"", q.Text);
        Assert.Contains("content_prefix", new Step3QueryBuilder().Build("ITM7").Text);
        Assert.Contains("content:(", new Step3QueryBuilder().Build("ring").Text);   // 4 letters: a word, not a code
    }

    [Fact]
    public void Document_name_with_other_separators_matches_as_a_whole_normalized_phrase()
    {
        var q = new Step3QueryBuilder().Build("Setup Guide DOC.1009.2063.19 NovaTurn 65.xlsx");
        Assert.Contains("content_norm:\"Setup Guide DOC.1009.2063.19 NovaTurn 65.xlsx\"^5", q.Text);
    }

    [Fact]
    public void Identifier_plus_words_keeps_the_identifier_as_its_own_exact_clause()
    {
        var q = new Step3QueryBuilder().Build("91260A27A04-01-00 seal ring");
        Assert.Contains("content_exact:\"91260A27A04-01-00 seal ring\"^10", q.Text);
        Assert.Contains("content_exact:\"91260A27A04-01-00\"^8 OR content_norm:\"91260A27A04-01-00\"^4", q.Text);
        Assert.EndsWith("content:(91260A27A04-01-00 seal ring)", q.Text);
        Assert.DoesNotContain("content_prefix", q.Text);
    }

    [Fact]
    public void Quotes_zero_width_spaces_and_backslashes_never_reach_the_phrase_clauses()
    {
        var q = new Step3QueryBuilder().Build("ST 80\"62\u200B");
        Assert.StartsWith("content_exact:\"ST 8062\"", q.Text);
        Assert.DoesNotContain("\u200B", q.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Normalize_matches_the_index_char_filter()
    {
        Assert.Equal("setupguidedoc1059241369orionmill1200xlsx", Step3QueryBuilder.Normalize("Setup Guide DOC_1059_2413_69 OrionMill 1200.xlsx"));
        Assert.Equal("itm772314.6".Replace(".", ""), Step3QueryBuilder.Normalize("ITM-7723-14.6\u200B"));
    }
}
