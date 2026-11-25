namespace GameVault.App.Artwork;

/// <summary>Which search results are shown (issue #58).</summary>
internal enum WebCoverFilter { CoverShape, BigEnough, Everything }

/// <summary>
/// Google's result page carries, for every tile, the size and address of the original picture. These scripts read that (and nothing else) so only results that can
/// become a cover are listed, labelled with their size, and one click picks the original instead of its small thumbnail. When the page does not
/// carry the details (Google changes its page, or results loaded later), tiles stay visible and the older click-then-preview steps still work.
/// </summary>
internal static class WebCoverResults
{
    internal const int MinimumWidth = 600;
    internal const int MinimumHeight = 900;
    internal const double NarrowestShape = 0.6;      // a game cover is 2:3 (0.667); the crop trims only a little within this range
    internal const double WidestShape = 0.75;

    internal static bool Fits(int width, int height) => width >= MinimumWidth && height >= MinimumHeight && height > width;
    internal static bool IsCoverShape(int width, int height) => Fits(width, height) && width >= height * NarrowestShape && width <= height * WidestShape;
    internal static bool Shows(WebCoverFilter filter, int width, int height) => filter switch
    { WebCoverFilter.CoverShape => IsCoverShape(width, height), WebCoverFilter.BigEnough => Fits(width, height), _ => true };

    /// <summary>
    /// JavaScript for a function <c>original(id)</c> giving <c>{source,width,height,thumbnail}</c> of a result from the page's own data, or null. Each entry in
    /// that data is <c>"id",["thumbnail",height,width],["original",height,width]</c>.
    /// </summary>
    internal const string LookupScript =
        "const original = id => { if (!/^[\\w-]{6,40}$/.test(id || '')) return null; " +
        "const entry = new RegExp('\"' + id + '\",\\\\[\"(https:[^\"]+)\",\\\\d+,\\\\d+\\\\],\\\\[\"(https:[^\"]+)\",(\\\\d+),(\\\\d+)\\\\]'); " +
        "for (const script of document.scripts) { const text = script.textContent; if (!text || text.indexOf('\"' + id + '\"') < 0) continue; const found = entry.exec(text); " +
        "if (found) { try { const source = JSON.parse('\"' + found[2] + '\"'); const thumbnail = JSON.parse('\"' + found[1] + '\"'); " +
        "return source.length <= 8192 ? { source, width: Number(found[4]), height: Number(found[3]), thumbnail } : null; } catch { return null; } } } return null; }; ";

    /// <summary>
    /// Shows only the results that pass the filter, in a plain grid laid over Google's page (moving Google's own tiles breaks its layout), each labelled with the
    /// original's size. "All results", or a page without the details, shows Google's page as it is. Returns how many are shown and hidden.
    /// </summary>
    internal static string FilterScript(WebCoverFilter filter)
    {
        var test = filter switch
        {
            WebCoverFilter.CoverShape => $"w >= {MinimumWidth} && h >= {MinimumHeight} && h > w && w >= h * 0.6 && w <= h * 0.75",
            WebCoverFilter.BigEnough => $"w >= {MinimumWidth} && h >= {MinimumHeight} && h > w",
            _ => "true"
        };
        var everything = filter == WebCoverFilter.Everything ? "true" : "false";
        return "(() => { " + LookupScript +
            "const shows = (w, h) => " + test + "; const old = document.getElementById('gv-grid'); " +
            "if (location.pathname !== '/search' || !document.body) { old?.remove(); return { shown: 0, hidden: 0 }; } " +
            "const seen = new Set(); const fitting = []; let hidden = 0; " +
            "for (const tile of document.querySelectorAll('div[data-docid][data-lpage]:not([data-gv])')) { const id = tile.getAttribute('data-docid'); if (seen.has(id)) continue; seen.add(id); " +
            "const info = original(id); if (!info) continue; if (shows(info.width, info.height)) fitting.push({ id, page: tile.getAttribute('data-lpage'), info }); else hidden++; } " +
            "const counts = { shown: fitting.length, hidden }; " +
            "if (" + everything + " || fitting.length === 0) { old?.remove(); return counts; } " +
            "const key = fitting.map(f => f.id).join(); if (old && old.getAttribute('data-key') === key) return counts; old?.remove(); " +
            "const grid = document.createElement('div'); grid.id = 'gv-grid'; grid.setAttribute('data-key', key); " +
            "grid.style.cssText = 'position:fixed;inset:0;z-index:2147483647;overflow:auto;background:#1f1f1f;padding:14px;display:grid;grid-template-columns:repeat(auto-fill,minmax(170px,1fr));gap:14px;align-content:start;align-items:start'; " +
            "for (const f of fitting) { const card = document.createElement('div'); card.setAttribute('data-docid', f.id); card.setAttribute('data-lpage', f.page || ''); card.setAttribute('data-gv', '1'); " +
            "card.style.cssText = 'cursor:pointer;color:#eee;font:12px sans-serif;text-align:center'; " +
            "const picture = document.createElement('img'); picture.src = f.info.thumbnail; picture.alt = ''; picture.style.cssText = 'width:100%;border-radius:8px;display:block;aspect-ratio:' + f.info.width + '/' + f.info.height + ';object-fit:cover;background:#333'; " +
            "const label = document.createElement('div'); label.textContent = f.info.width + ' \\u00d7 ' + f.info.height; label.style.cssText = 'padding:4px 0'; " +
            "card.appendChild(picture); card.appendChild(label); grid.appendChild(card); } " +
            "document.body.appendChild(grid); return counts; })()";
    }
}
