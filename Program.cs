using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;

class Program
{
    static async Task Main(string[] args)
    {
        string[] ssaList = File.ReadAllLines("ssa_ids.txt")
            .Select(id => id.Trim().ToLowerInvariant())
            .ToArray();

        var knownSerials = File.ReadAllLines("serials.txt")
            .Select(s => s.Trim())
            .ToHashSet();

        using HttpClient client = new HttpClient();

        string dateFolder = Path.Combine("rapports", DateTime.Now.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dateFolder);

        var htmlEntries = new List<string>();

        foreach (var ssaId in ssaList)
        {
            string url = $"https://cert-portal.siemens.com/productcert/txt/{ssaId}.txt";
            Console.WriteLine($"Analyse de {ssaId}...");
            string remoteText;

            try
            {
                remoteText = await client.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur pour {ssaId} : {ex.Message}");
                continue;
            }

            // 🔍 Extraction de la date de publication
            string publishedDate = "";
            foreach (var line in remoteText.Split('\n'))
            {
                if (line.StartsWith("Publication Date", StringComparison.OrdinalIgnoreCase))
                {
                    publishedDate = line.Split(':').Last().Trim();
                    break;
                }
            }

            var found = new List<string>();
            var fullMatches = new List<string>();

            foreach (var serial in knownSerials)
            {
                if (remoteText.Contains(serial, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(serial);
                    var lines = remoteText.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                    var blockLines = new List<string>();
                    bool capture = false;

                    foreach (var line in lines)
                    {
                        if (line.Contains(serial, StringComparison.OrdinalIgnoreCase))
                        {
                            capture = true;
                        }

                        if (capture)
                        {
                            if (line.TrimStart().StartsWith("* ") && blockLines.Count > 0)
                                break;

                            blockLines.Add(line);
                        }
                    }

                    fullMatches.Add(string.Join(Environment.NewLine, blockLines));
                }
            }

            if (found.Any())
            {
                string rapportTxtPath = Path.Combine(dateFolder, $"rapport_{ssaId}.txt");
                using StreamWriter writer = new StreamWriter(rapportTxtPath);
                writer.WriteLine($"Rapport de correspondance pour : {ssaId}");
                writer.WriteLine($"Date de publication : {publishedDate}");
                writer.WriteLine($"Date de génération : {DateTime.Now}");
                writer.WriteLine("Correspondances trouvées :");
                for (int i = 0; i < found.Count; i++)
                    writer.WriteLine($" - {found[i]}{fullMatches[i]}");

                string rapportHtmlPath = Path.Combine(dateFolder, $"rapport_{ssaId}.html");
                using StreamWriter htmlWriter = new StreamWriter(rapportHtmlPath);
                htmlWriter.WriteLine("<html><head><meta charset='utf-8'><style>body{font-family:sans-serif;} pre{background:#f4f4f4;padding:1em;border:1px solid #ccc;}</style></head><body>");
                htmlWriter.WriteLine($"<h2>Rapport de correspondance pour : {ssaId.ToUpperInvariant()}</h2>");
                htmlWriter.WriteLine($"<p>📆 Publication : {publishedDate}<br>🕓 Généré le : {DateTime.Now}</p>");
                htmlWriter.WriteLine("<h3>Correspondances trouvées :</h3>");
                for (int i = 0; i < found.Count; i++)
                {
                    htmlWriter.WriteLine($"<h4>{found[i]}</h4>");
                    htmlWriter.WriteLine("<pre>" + System.Net.WebUtility.HtmlEncode(fullMatches[i]) + "</pre>");
                }
                htmlWriter.WriteLine("</body></html>");

                string relativePath = Path.Combine(dateFolder, $"rapport_{ssaId}.html").Replace("\\", "/");
                htmlEntries.Add($"{ssaId.ToUpperInvariant()}|{relativePath}|{string.Join(", ", found)}|{publishedDate}");

                Console.WriteLine($" Rapport écrit dans : {rapportTxtPath}");
            }
            else
            {
                Console.WriteLine("Aucun numéro de série trouvé");
            }
        }

        // Regroupement par SSA unique → garde la dernière version si relancé
        var allEntries = new Dictionary<string, (string dateKey, string href, string serials, string published)>();

        foreach (var entry in htmlEntries)
        {
            var parts = entry.Split('|');
            string ssa = parts[0];
            string href = parts[1];
            string serials = parts[2];
            string published = parts.Length > 3 ? parts[3] : "";
            string dateFolderName = href.Split('/')[1]; // ex: 2025-05-14

            allEntries[ssa] = (dateFolderName, href, serials, published);
        }

        string indexHtmlPath = "index.html";
        using StreamWriter indexWriter = new StreamWriter(indexHtmlPath);
        indexWriter.WriteLine(@"
<!DOCTYPE html>
<html lang='fr'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1'>
    <title>Rapports Siemens</title>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css' rel='stylesheet'>
</head>
<body class='bg-light'>
    <div class='container mt-5'>
        <h1 class='mb-4 text-center'>📋 Rapports Siemens par date</h1>
");

        var groupedByDate = allEntries.Values
            .GroupBy(e => e.dateKey)
            .OrderByDescending(g => g.Key);

        foreach (var group in groupedByDate)
        {
            string dateFr = DateTime.Parse(group.Key).ToString("dd/MM/yy");
            indexWriter.WriteLine($"<div class='card mb-3'><div class='card-header fw-bold'>📅 {dateFr}</div><ul class='list-group list-group-flush'>");

            foreach (var (dateKey, href, serials, published) in group.OrderBy(x => x.href))
            {
                string ssa = Path.GetFileNameWithoutExtension(href).Replace("rapport_", "").ToUpperInvariant();
                indexWriter.WriteLine($@"
<li class='list-group-item'>
  <a href='{href}' target='_blank'>{ssa}</a><br>
  <small class='text-muted'>📆 Publié le : {published}<br>🔢 Séries : {serials}</small>
</li>");
            }

            indexWriter.WriteLine("</ul></div>");
        }

        indexWriter.WriteLine("</div></body></html>");
    }
}
