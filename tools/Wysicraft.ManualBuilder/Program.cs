// Builds Docs/Wysicraft-Manual.html: every wiki page in sidebar order, on one offline page with navigation.
// Usage: Wysicraft.ManualBuilder <wiki folder> <output .html> <version>
// Wiki links ([[Text|Page#section]]) become in-page links; images are embedded so the file works on its own.
// Exits with an error if any internal link points at a page or section that doesn't exist.
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

if (args.Length < 3) { Console.Error.WriteLine("Usage: Wysicraft.ManualBuilder <wiki folder> <output.html> <version>"); return 2; }
string wiki = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), version = args[2];
var wikiLink = new Regex(@"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]");
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

// Sidebar: bold lines are group headings, wiki links are pages (same rules as the website's Docs page).
var groups = new List<(string Title, List<(string Text, string Page)> Items)> { ("", new()) };
foreach (var raw in File.ReadAllLines(Path.Combine(wiki, "_Sidebar.md"))) {
    string line = raw.Trim();
    var matches = wikiLink.Matches(line);
    if (matches.Count > 0) { foreach (Match m in matches) groups[^1].Items.Add((m.Groups[1].Value.Trim(), Target(m).Page)); continue; }
    var heading = Regex.Match(line, @"^\*\*(.+)\*\*$");
    if (heading.Success) groups.Add((heading.Groups[1].Value, new()));
}
var pages = groups.SelectMany(g => g.Items.Select(i => i.Page)).Distinct().ToList();

var body = new StringBuilder();
var ids = new HashSet<string>();
var links = new List<(string Page, string Href)>();
foreach (string page in pages) {
    string file = Path.Combine(wiki, page + ".md");
    if (!File.Exists(file)) { Console.Error.WriteLine("Sidebar page missing: " + page); return 1; }
    // Links within the page ("[Profiler](#profiler)", the Glossary's letters) point at this page's own headings, which
    // the one-page manual prefixes with the page name; they're checked like any other link. (Done before the wiki
    // links below are turned into "#Page" links, so those aren't touched.)
    string source = Regex.Replace(File.ReadAllText(file), @"\]\(#([\w\-]+)\)", m => { string href = "#" + Anchor(page, m.Groups[1].Value); links.Add((page, href)); return "](" + href + ")"; });
    string markdown = wikiLink.Replace(source, m => { var t = Target(m); string href = "#" + Anchor(t.Page, t.Section); links.Add((page, href)); return "[" + m.Groups[1].Value.Trim() + "](" + href + ")"; });
    markdown = Regex.Replace(markdown, @"\]\(images/([^)\s]+)\)", m => "](" + DataUri(Path.Combine(wiki, "images", m.Groups[1].Value)) + ")");
    var document = Markdown.Parse(markdown, pipeline);
    foreach (var h in document.Descendants<HeadingBlock>()) {
        string id = h.Level == 1 ? page : Anchor(page, Slug(InlineText(h.Inline)));
        h.GetAttributes().Id = id; ids.Add(id);
    }
    foreach (var link in document.Descendants<LinkInline>())
        if (!link.IsImage && link.Url is string url && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
            link.GetAttributes().AddPropertyIfNotExist("target", "_blank");
            link.GetAttributes().AddPropertyIfNotExist("rel", "noopener");
        }
    body.Append("<section class=\"page\">").Append(document.ToHtml(pipeline)).Append("</section>\n");
}

var broken = links.Where(l => !ids.Contains(l.Href[1..])).Select(l => l.Page + " → " + l.Href).Distinct().ToList();
if (broken.Count > 0) { Console.Error.WriteLine("Broken manual links:\n  " + string.Join("\n  ", broken)); return 1; }

var nav = new StringBuilder();
foreach (var group in groups.Where(g => g.Items.Count > 0)) {
    nav.Append("<div class=\"group\">");
    if (group.Title.Length > 0) nav.Append("<p>").Append(WebUtility.HtmlEncode(group.Title)).Append("</p>");
    nav.Append("<ul>");
    foreach (var item in group.Items) nav.Append("<li><a href=\"#").Append(item.Page).Append("\">").Append(WebUtility.HtmlEncode(item.Text)).Append("</a></li>");
    nav.Append("</ul></div>");
}

string html = Template.Page
    .Replace("{{VERSION}}", WebUtility.HtmlEncode(version))
    .Replace("{{NAV}}", nav.ToString())
    .Replace("{{BODY}}", body.ToString());
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, html, new UTF8Encoding(false));
Console.WriteLine($"Manual: {pages.Count} pages, {ids.Count} anchors, {links.Count} links → {output}");
return 0;

(string Page, string Section) Target(Match m) {
    string target = (m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value).Trim().Replace(' ', '-');
    int hash = target.IndexOf('#');
    return hash < 0 ? (target, "") : (target[..hash], target[(hash + 1)..]);
}
static string Anchor(string page, string section) => section.Length == 0 ? page : page + "--" + section;
// GitHub-style heading anchors: lowercase, punctuation removed, spaces become hyphens.
static string Slug(string text) => Regex.Replace(Regex.Replace(text.ToLowerInvariant().Trim(), @"[^\w\- ]", ""), @"\s", "-");
static string InlineText(ContainerInline? inline) {
    var text = new StringBuilder();
    if (inline != null) foreach (var node in inline.Descendants<Inline>()) {
        if (node is LiteralInline literal) text.Append(literal.Content.ToString());
        else if (node is CodeInline code) text.Append(code.Content);
    }
    return text.ToString();
}
static string DataUri(string path) {
    if (!File.Exists(path)) throw new FileNotFoundException("Manual image missing", path);
    string type = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", _ => "application/octet-stream" };
    return "data:" + type + ";base64," + Convert.ToBase64String(File.ReadAllBytes(path));
}

static class Template
{
    // Dark theme matching the editor. No external resources, so the manual works offline.
    public const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width,initial-scale=1" />
<title>Arcadia Studio {{VERSION}} manual</title>
<style>
  :root{--bg:#1d2025;--panel:#25282e;--line:#3a3f48;--text:#e2e6ed;--muted:#9aa4b2;--accent:#3794ff;--code:#181b20}
  *{box-sizing:border-box}
  html{scroll-behavior:smooth}
  body{margin:0;background:var(--bg);color:var(--text);font:15px/1.6 "Segoe UI",system-ui,sans-serif}
  a{color:var(--accent);text-decoration:none} a:hover{text-decoration:underline}
  header{position:sticky;top:0;z-index:5;display:flex;align-items:center;justify-content:space-between;gap:16px;padding:12px 24px;background:#16191e;border-bottom:1px solid var(--line)}
  header strong{font-size:1.05rem} header span{color:var(--muted);font-size:.85rem}
  .layout{display:grid;grid-template-columns:260px minmax(0,1fr);max-width:1280px;margin:0 auto}
  nav{position:sticky;top:53px;height:calc(100vh - 53px);overflow:auto;padding:18px 16px;border-right:1px solid var(--line);background:var(--panel)}
  nav input{width:100%;padding:8px 10px;margin-bottom:12px;border-radius:6px;border:1px solid var(--line);background:var(--code);color:var(--text);font:inherit}
  nav p{margin:14px 0 4px;color:var(--accent);font-size:.72rem;font-weight:700;letter-spacing:.1em;text-transform:uppercase}
  nav ul{list-style:none;margin:0;padding:0}
  nav li a{display:block;padding:4px 10px;border-radius:5px;color:var(--muted);font-size:.9rem}
  nav li a:hover{background:#2f343c;color:var(--text);text-decoration:none}
  nav li a.current{background:#094771;color:#fff}
  main{padding:10px 44px 60px;min-width:0}
  .page{padding:26px 0 30px;border-bottom:1px solid var(--line)}
  h1{font-size:2rem;margin:0 0 14px;scroll-margin-top:70px}
  h2{font-size:1.4rem;margin:30px 0 10px;scroll-margin-top:70px}
  h3{font-size:1.1rem;margin:22px 0 8px;scroll-margin-top:70px}
  h4{margin:18px 0 6px}
  p,ul,ol{margin:0 0 12px} li{margin:3px 0}
  code{font:13px Consolas,monospace;background:var(--code);border:1px solid var(--line);border-radius:4px;padding:1px 5px}
  pre{background:var(--code);border:1px solid var(--line);border-radius:6px;padding:14px 16px;overflow:auto}
  pre code{border:0;padding:0}
  table{border-collapse:collapse;margin:0 0 16px;width:100%;display:block;overflow-x:auto}
  th,td{border:1px solid var(--line);padding:7px 10px;text-align:left;vertical-align:top}
  th{background:var(--panel)}
  blockquote{margin:0 0 14px;padding:8px 14px;border-left:3px solid var(--accent);background:var(--panel)}
  img{max-width:100%;height:auto;border:1px solid var(--line);border-radius:6px}
  @media(max-width:860px){.layout{grid-template-columns:1fr} nav{position:static;height:auto;border-right:0;border-bottom:1px solid var(--line)} main{padding:10px 18px 40px}}
  @media print{header,nav{display:none}.layout{display:block}main{padding:0}body{background:#fff;color:#000}a{color:#000}.page{page-break-after:always}}
</style>
</head>
<body>
<header><strong>Arcadia Studio manual</strong><span>Version {{VERSION}} · Minecraft 1.21.1 / NeoForge · works offline</span></header>
<div class="layout">
<nav aria-label="Manual pages"><input id="filter" type="search" placeholder="Filter pages…" aria-label="Filter pages" />{{NAV}}</nav>
<main>
{{BODY}}
</main>
</div>
<script>
  // Filter the page list, and highlight the page currently on screen.
  const filter = document.getElementById("filter");
  filter.addEventListener("input", () => {
    const q = filter.value.trim().toLowerCase();
    document.querySelectorAll("nav .group").forEach(g => {
      let shown = 0;
      g.querySelectorAll("li").forEach(li => { const m = !q || li.textContent.toLowerCase().includes(q); li.hidden = !m; if (m) shown++; });
      g.hidden = shown === 0;
    });
  });
  const links = new Map([...document.querySelectorAll("nav li a")].map(a => [a.getAttribute("href").slice(1), a]));
  const observer = new IntersectionObserver(entries => {
    for (const e of entries) if (e.isIntersecting) {
      links.forEach(a => a.classList.remove("current"));
      const a = links.get(e.target.id); if (a) a.classList.add("current");
    }
  }, { rootMargin: "-60px 0px -70% 0px" });
  document.querySelectorAll(".page > h1[id]").forEach(h => observer.observe(h));
</script>
</body>
</html>
""";
}
