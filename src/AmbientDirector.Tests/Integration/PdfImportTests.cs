using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AmbientDirector.Tests.Support;
using Xunit;

namespace AmbientDirector.Tests.Integration;

/// <summary>
/// The PDF page → image endpoints (issue #88) end to end through the booted app: upload → thumbnail → import
/// → serve, plus the coded 400s (unreadable PDF, consumed temp, out-of-range page). Runs on ubuntu in CI, so
/// it is also the linux-x64 proof that the PDFium + SkiaSharp natives flow and render.
/// </summary>
[Collection("integration")]
public class PdfImportTests
{
    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] pdf)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "handout.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/images/pdf/upload") { Content = form };
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Upload_thumb_import_serve_roundtrip()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        // Upload a 2-page PDF → { id, pages: 2 }.
        var upload = await UploadAsync(client, TestPdf.Create(pageCount: 2));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var uploaded = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var id = uploaded.GetProperty("id").GetString()!;
        Assert.Equal(2, uploaded.GetProperty("pages").GetInt32());

        // Page thumbnail streams as a JPEG.
        var thumb = await client.GetAsync($"/images/pdf/{id}/thumb/1");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/jpeg", thumb.Content.Headers.ContentType?.MediaType);

        // Import page 2 → a single { page, id } entry.
        var import = await client.PostAsJsonAsync($"/images/pdf/{id}/import", new { pages = new[] { 2 } });
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var pages = await import.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, pages.GetArrayLength());
        Assert.Equal(2, pages[0].GetProperty("page").GetInt32());
        var storedId = pages[0].GetProperty("id").GetString()!;

        // The imported page is now an ordinary stored image, served by GET /images/{name}.
        var served = await client.GetAsync($"/images/{storedId}");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.StartsWith("image/", served.Content.Headers.ContentType?.MediaType ?? "");

        // The temp PDF was consumed on import → re-importing the same id is a clean "upload again" 400.
        var again = await client.PostAsJsonAsync($"/images/pdf/{id}/import", new { pages = new[] { 2 } });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var problem = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.pdf.notFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Garbage_upload_is_400_with_a_stable_code()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var response = await UploadAsync(client, "definitely not a pdf"u8.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("application/problem+json", response.Content.Headers.ContentType!.ToString());
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.pdf.invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Importing_an_out_of_range_page_is_400()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();

        var upload = await UploadAsync(client, TestPdf.Create(pageCount: 2));
        var id = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var response = await client.PostAsJsonAsync($"/images/pdf/{id}/import", new { pages = new[] { 99 } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("error.pdf.pageOutOfRange", problem.GetProperty("code").GetString());
    }
}
