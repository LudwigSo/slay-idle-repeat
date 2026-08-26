using Shouldly;
using SlayIdleRepeat.Server.Endpoints;
using Xunit;

namespace SlayIdleRepeat.Server.Tests.Endpoints;

/// <summary>
/// The <c>GET /config</c> contract (14 §10): the source's document verbatim, and the headers the
/// client's caching half (M5-15/M16) relies on.
/// </summary>
public sealed class RemoteConfigRequestHandlerTests
{
    private const string Document = "{\n  \"pvpEnabled\": false\n}";

    [Fact]
    public void The_answer_is_200_with_the_document_verbatim()
    {
        var reply = RemoteConfigRequestHandler.Handle(Document);

        reply.StatusCode.ShouldBe(200, "the document always exists — absence renders the identity upstream");
        reply.Body.ShouldBe(Document, "every client must receive the operator's exact bytes");
    }

    [Fact]
    public void The_answer_declares_json_utf8_and_the_authored_six_hour_cache()
    {
        var reply = RemoteConfigRequestHandler.Handle(Document);

        reply.ContentType.ShouldBe("application/json; charset=utf-8");
        reply.CacheControl.ShouldBe(
            "public, max-age=21600", "21600 s is the authored 6 h of 14 §10 — the client-cache half's contract");
    }
}
