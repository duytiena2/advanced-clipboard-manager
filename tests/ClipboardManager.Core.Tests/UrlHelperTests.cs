using System.Linq;
using ClipboardManager.Core.Services;

namespace ClipboardManager.Core.Tests;

public class UrlHelperTests
{
    [Test]
    public void AnalyzeUrl_FacebookUrl_BreaksDownPathAndParametersCorrectly()
    {
        var url = "https://www.facebook.com/photo/?fbid=1548236474016892&set=a.628170352690180";
        var result = UrlHelper.AnalyzeUrl(url);

        Assert.True(result.IsValid);
        Assert.Equal("https", result.Scheme);
        Assert.Equal("www.facebook.com", result.Host);
        Assert.Equal("/photo/", result.Path);
        Assert.Equal(2, result.Parameters.Count);

        Assert.Equal("fbid", result.Parameters[0].Key);
        Assert.Equal("1548236474016892", result.Parameters[0].Value);
        Assert.False(result.Parameters[0].IsTracking);

        Assert.Equal("set", result.Parameters[1].Key);
        Assert.Equal("a.628170352690180", result.Parameters[1].Value);
        Assert.False(result.Parameters[1].IsTracking);
    }

    [Test]
    public void AnalyzeUrl_WithTrackingParams_DetectsTrackingAndCleansUrl()
    {
        var url = "https://example.com/item?id=42&utm_source=facebook&utm_medium=cpc&fbclid=IwAR2xyz";
        var result = UrlHelper.AnalyzeUrl(url);

        Assert.True(result.HasTrackingParameters);
        Assert.Equal(4, result.Parameters.Count);
        Assert.False(result.Parameters[0].IsTracking); // id
        Assert.True(result.Parameters[1].IsTracking);  // utm_source
        Assert.True(result.Parameters[2].IsTracking);  // utm_medium
        Assert.True(result.Parameters[3].IsTracking);  // fbclid

        Assert.Equal("https://example.com/item?id=42", result.CleanUrl);
    }

    [Test]
    public void ParseQueryParameters_DecodesUrlEncodedAndPlusCharacters()
    {
        var url = "https://search.com?q=l%E1%BA%ADp+tr%C3%ACnh+c%23&page=1";
        var parameters = UrlHelper.ParseQueryParameters(url);

        Assert.Equal(2, parameters.Count);
        Assert.Equal("q", parameters[0].Key);
        Assert.Equal("lập trình c#", parameters[0].Value);
        Assert.Equal("page", parameters[1].Key);
        Assert.Equal("1", parameters[1].Value);
    }

    [Test]
    public void AnalyzeUrl_NoQueryParams_ReturnsEmptyParameters()
    {
        var url = "https://github.com/duytiena2";
        var result = UrlHelper.AnalyzeUrl(url);

        Assert.Equal("https", result.Scheme);
        Assert.Equal("github.com", result.Host);
        Assert.Equal("/duytiena2", result.Path);
        Assert.Equal(0, result.Parameters.Count);
        Assert.False(result.HasTrackingParameters);
        Assert.Equal(url, result.CleanUrl);
    }

    [Test]
    public void GetCleanUrl_StripAllQueryParams_RemovesAll()
    {
        var url = "https://example.com/page?query=search&token=123#overview";
        var clean = UrlHelper.GetCleanUrl(url, stripAllQueryParams: true);

        Assert.Equal("https://example.com/page#overview", clean);
    }
}
