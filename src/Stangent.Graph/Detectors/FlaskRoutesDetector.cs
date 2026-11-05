using Stangent.Graph.Extraction;

namespace Stangent.Graph.Detectors;

/// <summary>
/// Flask's routes (D127): `@app.route`, blueprints with their `url_prefix`, `methods=`, `add_url_rule` and class views. The Python extractor
/// works them out from the text, this turns each into an endpoint and a `routes-to` line. ✅ when the object is provably a Flask app or a
/// registered blueprint; 🟡 when the path rests on an import or a blueprint not seen registered. What can't be proved is ❓, recorded by the extractor.
/// Never a guess at a URL: a part the text doesn't settle is `{prefix}`, and a route whose URL is built at runtime has no endpoint.
/// </summary>
public sealed class FlaskRoutesDetector : IDetector
{
    public const string DetectorName = "flask-routes";
    public string Name => DetectorName;

    public void Extract(ExtractContext ctx) { } // facts come from the Python extractor

    public void Link(LinkContext ctx)
    {
        foreach (var fact in ctx.Facts(Name).Where(f => f.Kind == "endpoint"))
        {
            var id = RoutesDetector.EndpointId(fact["assembly"], fact["verb"], fact["path"]);
            ctx.AddNode(new Node(id, NodeKind.Endpoint, $"{fact["verb"]} {fact["path"]}", fact["project"], fact.File, fact.Line, fact.Line));
            ctx.AddEdge(id, fact.Owner, EdgeKind.RoutesTo, fact["label"] == "0" ? Label.Certain : Label.Guessed, Name);
        }
    }
}
