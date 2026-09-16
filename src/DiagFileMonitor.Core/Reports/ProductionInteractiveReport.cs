using System.Globalization;
using System.Text;
using DiagFileMonitor.Core.Production;

namespace DiagFileMonitor.Core.Reports;

public class ProductionInteractiveOptions
{
    /// <summary>
    /// Pull Zilla Slab, Nunito and IBM Plex Mono from Google. Off by default: a report emailed to a
    /// site has to read the same on a machine with no internet as it does here.
    /// </summary>
    public bool UseWebFonts { get; init; }

    public bool InternalUseOnly { get; init; } = true;

    /// <summary>Who the report was put together for, printed on the plate. Optional.</summary>
    public string PreparedFor { get; init; } = string.Empty;
}

/// <summary>
/// Report 3, as a page you can work rather than a page you read.
/// <para>
/// The figures are the same ones <see cref="ProductionReport"/> prints. What is different is that
/// the period (month, week, day, hour), the measure (panels, cube, lineal) and the shift model are
/// all changeable in the page, so a site can put its own roster in and watch the availability come
/// right instead of ringing us to ask for the report again.
/// </para>
/// <para>
/// Availability maths is worked twice: once in C# for the figures the page opens on, and once in
/// the page itself so an edited roster means something. That is duplication, and it is deliberate -
/// the alternative is a roster box that cannot change anything. The page checks its own answer
/// against the C# one on load and says so in the footer if the two ever drift apart.
/// </para>
/// </summary>
public static class ProductionInteractiveReport
{
    public static string Build(
        ProductionSummary summary,
        IReadOnlyList<PanelRecord> panels,
        string machineName = "",
        DateTime? preparedUtc = null,
        ProductionInteractiveOptions? options = null)
    {
        options ??= new ProductionInteractiveOptions();
        var prepared = preparedUtc ?? DateTime.UtcNow;

        var name = string.IsNullOrWhiteSpace(machineName) ? "Machine" : machineName.Trim();
        var serial = string.IsNullOrWhiteSpace(summary.SerialNumber) ? "serial unknown" : summary.SerialNumber;

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en-NZ\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{H(name)} - {H(serial)} - Production</title>");

        if (options.UseWebFonts)
        {
            sb.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.googleapis.com\">");
            sb.AppendLine("<link rel=\"preconnect\" href=\"https://fonts.gstatic.com\" crossorigin>");
            sb.AppendLine("<link href=\"https://fonts.googleapis.com/css2?family=Zilla+Slab:wght@500;600;700"
                          + "&family=Nunito:wght@400;600;700;800&family=IBM+Plex+Mono:wght@400;500;600"
                          + "&display=swap\" rel=\"stylesheet\">");
        }

        sb.AppendLine("<style>");
        sb.AppendLine(Css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine("<div class=\"topbar\"><div class=\"wrap\">");
        sb.AppendLine("<span>Spida Machinery &middot; Production reporting"
                      + (options.InternalUseOnly ? " &middot; Internal use only" : "") + "</span>");
        sb.AppendLine($"<span><b>{H(name)}</b> &middot; {H(serial)}</span>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("<header class=\"plate\"><div class=\"wrap\">");
        sb.AppendLine("<div class=\"plate-l\">");
        sb.AppendLine($"<div class=\"chev\">{H(name)}</div>");
        sb.AppendLine("<h1>Production Report</h1>");
        sb.AppendLine("<div class=\"h1rule\"></div>");
        sb.AppendLine("<div class=\"plate-meta\" id=\"plateMeta\"></div>");
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"plate-r\">");
        if (!string.IsNullOrWhiteSpace(options.PreparedFor))
            sb.AppendLine($"<div class=\"tt-for\">Prepared for <b>{H(options.PreparedFor)}</b></div>");
        sb.AppendLine($"<div class=\"tt-sub\">{H(string.IsNullOrWhiteSpace(summary.Site) ? "Site not recorded" : summary.Site)}"
                      + $"<br>Machine {H(serial)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div></header>");

        sb.AppendLine(Body);

        sb.AppendLine("<script>");
        sb.Append("var PAYLOAD = ");
        sb.Append(ProductionPayload.Build(summary, panels, name, prepared));
        sb.AppendLine(";");
        sb.AppendLine(Script);
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string H(string? value) => (value ?? string.Empty)
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private const string Css = """
:root{
  --ink:#425969; --ink2:#5E7382; --ink3:#8A9AA6;
  --concrete:#F1F4F6; --card:#FFFFFF; --rule:#D8E0E5;
  --blue:#009CDE; --blue-dk:#0077A8; --blue-lt:#00A4E3;
  --timber:#009CDE; --yellow:#F0A02A; --oxide:#C0392B; --steel:#7D9099;
  --good:#009CDE;
  --serif:'Zilla Slab',Georgia,serif;
  --disp:'Nunito',Arial,sans-serif;
  --mono:'IBM Plex Mono','Consolas',monospace;
  --body:Arial,Helvetica,sans-serif;
}
*{box-sizing:border-box}
html,body{margin:0;padding:0}
body{background:var(--concrete);color:var(--ink);font-family:var(--body);font-size:15px;line-height:1.5;
  -webkit-font-smoothing:antialiased;padding:0 0 64px}
.wrap{max-width:1180px;margin:0 auto;padding:0 20px}

.topbar{background:var(--ink);color:#C6D2DA;font-family:var(--mono);font-size:11px;letter-spacing:.13em;text-transform:uppercase}
.topbar .wrap{display:flex;flex-wrap:wrap;gap:14px;justify-content:space-between;padding-top:9px;padding-bottom:9px}
.topbar b{color:#fff;font-weight:500}

.plate{background:#fff;border-bottom:1px solid var(--rule)}
.plate .wrap{display:flex;align-items:flex-start;gap:26px;flex-wrap:wrap;padding-top:22px;padding-bottom:20px}
.plate-l{flex:1 1 300px;min-width:250px}
.chev{display:inline-flex;align-items:center;background:var(--blue-lt);color:#fff;font-family:var(--disp);font-weight:600;
  font-size:14px;padding:7px 12px 7px 14px;position:relative;margin-bottom:12px}
.chev::after{content:"";position:absolute;left:100%;top:0;bottom:0;width:16px;
  background:var(--blue-lt);clip-path:polygon(0 0,100% 50%,0 100%)}
h1{font-family:var(--serif);font-weight:600;font-size:clamp(26px,3.5vw,35px);line-height:1.05;letter-spacing:-.01em;
  margin:0 0 9px;color:var(--blue)}
.h1rule{width:112px;height:3px;background:var(--blue);margin-bottom:11px}
.plate-meta{font-family:var(--mono);font-size:11.5px;color:var(--ink3);line-height:1.7}
.plate-meta b{color:var(--ink);font-weight:500}
.plate-r{flex:0 0 auto;max-width:250px;text-align:right;padding-left:22px;border-left:1px solid var(--rule)}
.tt-for{font-family:var(--serif);font-size:15px;font-weight:500;color:var(--ink2);line-height:1.4}
.tt-for b{font-weight:700;color:var(--ink)}
.tt-sub{font-family:var(--mono);font-size:10px;letter-spacing:.11em;text-transform:uppercase;color:var(--ink3);
  margin-top:6px;line-height:1.65}
@media (max-width:900px){
  .plate-r{order:2;text-align:left;max-width:none;padding-left:0;border-left:none;
    border-top:1px solid var(--rule);padding-top:16px;width:100%}
}

footer{border-top:1.5px solid var(--rule);margin-top:44px;padding-top:16px;display:flex;flex-wrap:wrap;gap:10px;
  justify-content:space-between;font-family:var(--mono);font-size:11px;letter-spacing:.09em;text-transform:uppercase;color:var(--ink3)}
footer a{color:var(--blue);text-decoration:none;font-weight:500}

.tabs{display:flex;border:1.5px solid var(--ink);width:fit-content;margin-bottom:8px;background:var(--card)}
.tabs button{font-family:var(--disp);font-size:15px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;
  padding:9px 22px;background:none;border:none;border-right:1.5px solid var(--ink);cursor:pointer;color:var(--ink2)}
.tabs button:last-child{border-right:none}
.tabs button[aria-selected="true"]{background:var(--blue);color:#fff}
.tabs button:focus-visible{outline:3px solid var(--ink);outline-offset:-3px}
@media (max-width:520px){.tabs{width:100%}.tabs button{flex:1 1 0;padding:9px 6px;font-size:13px;letter-spacing:.04em}}
.tabnote{font-family:var(--mono);font-size:11.5px;color:var(--ink3);margin:0 0 22px}
.ctlbar{display:flex;flex-wrap:wrap;gap:16px;align-items:center;justify-content:space-between;margin-bottom:8px}
.metric{display:flex;border:1.5px solid var(--blue);background:var(--card);width:fit-content}
.metric button{font-family:var(--disp);font-size:13px;font-weight:700;text-transform:uppercase;letter-spacing:.07em;
  padding:8px 16px;background:none;border:none;border-right:1.5px solid var(--blue);cursor:pointer;color:var(--blue);
  display:flex;flex-direction:column;align-items:center;gap:1px;line-height:1.15}
.metric button:last-child{border-right:none}
.metric button small{font-family:var(--mono);font-size:9px;font-weight:400;letter-spacing:.1em;opacity:.7;text-transform:none}
.metric button[aria-pressed="true"]{background:var(--blue);color:#fff}
.metric button:focus-visible{outline:3px solid var(--ink);outline-offset:-3px}
.metric-lbl{font-family:var(--mono);font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--ink3);margin-bottom:5px}

section{margin-bottom:34px}
.eyebrow{font-family:var(--mono);font-size:10.5px;letter-spacing:.18em;text-transform:uppercase;
  color:var(--blue);font-weight:600;display:flex;align-items:center;gap:12px;margin-bottom:12px}
.eyebrow::after{content:"";flex:1;height:1px;background:var(--rule)}
.eyebrow::before{content:"";width:14px;height:3px;background:var(--blue);flex:none}
.sub{font-size:13.5px;color:var(--ink2);margin:-4px 0 14px;max-width:70ch}

.frame-box{background:var(--card);border:1.5px solid var(--ink);padding:18px 18px 10px}
.frame-box svg{display:block;width:100%;height:auto}
.legend{display:flex;flex-wrap:wrap;gap:16px;font-family:var(--mono);font-size:11px;color:var(--ink2);
  margin-top:12px;padding-top:10px;border-top:1px solid var(--rule)}
.legend i.sw{display:inline-block;width:13px;height:13px;margin-right:6px;vertical-align:-2px}
.legend i.hatch-b{background:repeating-linear-gradient(45deg,#A8B9C0 0 3px,#DCE4E8 3px 7px)}
.legend i.hatch-r{background:repeating-linear-gradient(45deg,#E09A8E 0 3px,#F7DED9 3px 7px)}

.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(178px,1fr));border:1.5px solid var(--ink);background:var(--card)}
.tile{padding:15px 16px 14px;border-right:1.5px solid var(--ink)}
.tile:last-child{border-right:none}
.tile .lbl{font-family:var(--mono);font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--ink2)}
.tile .val{font-family:var(--serif);font-weight:600;font-size:34px;line-height:1;margin:8px 0 4px;letter-spacing:-.02em;
  font-variant-numeric:tabular-nums;color:var(--ink)}
.tile .val small{font-size:15px;font-weight:600;color:var(--ink3);margin-left:3px}
.tile .foot{font-family:var(--mono);font-size:11px;color:var(--ink3)}
.tile.warn .val{color:var(--oxide)}
.tile.ok .val{color:var(--good)}

.rows{background:var(--card);border:1.5px solid var(--ink)}
.row{display:grid;grid-template-columns:96px 1fr 148px;gap:14px;align-items:center;padding:9px 16px;border-bottom:1px solid var(--rule)}
.row:last-child{border-bottom:none}
.row.click{cursor:pointer}
.row.click:hover{background:#F0F6F9}
.row .rlbl{font-family:var(--mono);font-size:12px;color:var(--ink2)}
.row .rval{font-family:var(--mono);font-size:12px;text-align:right;color:var(--ink)}
.track{position:relative;height:22px;background:#E4EBEE}
.bar{position:absolute;top:0;bottom:0;left:0;background:var(--steel)}
.bar.over{background:var(--oxide)}
.bar.mid{background:var(--yellow)}
.bar.tim{background:var(--timber)}
.bar.dim{background:#B6C4CB}
.tmark{position:absolute;top:-3px;bottom:-3px;width:2px;background:var(--ink)}

.tblwrap{overflow-x:auto;border:1.5px solid var(--ink);background:var(--card);max-height:560px}
table{border-collapse:collapse;width:100%;font-family:var(--mono);font-size:12px;white-space:nowrap}
th{background:var(--ink);color:var(--card);font-weight:500;text-align:right;padding:8px 11px;font-size:10px;
  letter-spacing:.11em;text-transform:uppercase;position:sticky;top:0}
th:first-child,td:first-child{text-align:left}
th.l,td.l{text-align:left}
td{padding:7px 11px;text-align:right;border-bottom:1px solid var(--rule);font-variant-numeric:tabular-nums}
tbody tr:last-child td{border-bottom:none}
tbody tr:hover{background:#F0F6F9}
.rsn{display:inline-block;font-family:var(--mono);font-size:10px;letter-spacing:.06em;text-transform:uppercase;
  padding:2px 6px;border:1px solid currentColor}
.r-stop{color:#C0392B}.r-nonail{color:#C0392B}.r-aband{color:#B0500F}.r-skip{color:#5E7382}

.fresh{font-family:var(--mono);font-size:11.5px;letter-spacing:.05em;text-transform:uppercase;border:1.5px solid var(--rule);
  border-left-width:5px;padding:9px 14px;margin-bottom:14px;color:var(--ink2);background:var(--card)}
.fresh.ok{border-left-color:var(--blue)}
.fresh.stale{border-left-color:var(--yellow);color:var(--ink)}
.fresh.bad{border-left-color:var(--oxide);color:var(--oxide);font-weight:600}
.fresh b{font-family:var(--disp);letter-spacing:0;text-transform:none;font-size:13px}
.notice{background:#E1F4FC;border:1.5px solid var(--blue);border-left-width:5px;padding:12px 16px;font-size:13.5px;
  margin-bottom:26px;color:var(--ink)}
.notice b{font-family:var(--disp);font-weight:800;font-size:15px}
.notice.warn{background:#FDF3E3;border-color:var(--yellow)}
.hidden{display:none}

.nav{margin-bottom:22px}
.nav-wk{display:flex;align-items:center;gap:10px;margin-bottom:9px;flex-wrap:wrap}
.nav-wk .arw{font-family:var(--mono);font-size:15px;line-height:1;border:1.5px solid var(--ink);background:var(--card);
  color:var(--ink);width:34px;height:34px;cursor:pointer;display:flex;align-items:center;justify-content:center}
.nav-wk .arw:hover{background:var(--ink);color:#fff}
.nav-wk .wklbl{font-family:var(--serif);font-weight:600;font-size:19px;letter-spacing:-.01em;min-width:210px}
.nav-wk .wksub{font-family:var(--mono);font-size:11px;color:var(--ink3);letter-spacing:.05em}
.nav-wk .jump{font-family:var(--disp);font-weight:700;font-size:12px;text-transform:uppercase;letter-spacing:.07em;
  border:1.5px solid var(--blue);background:#fff;color:var(--blue);padding:7px 13px;cursor:pointer;margin-left:auto}
.nav-wk .jump:hover{background:var(--blue);color:#fff}
.days{display:grid;grid-template-columns:repeat(7,1fr);border:1.5px solid var(--ink);background:var(--card)}
.days.wks{grid-template-columns:repeat(auto-fit,minmax(86px,1fr))}
.dchip{padding:9px 4px 8px;text-align:center;border:none;border-right:1px solid var(--rule);background:none;
  cursor:pointer;font-family:var(--mono);color:var(--ink);display:block;width:100%}
.dchip:last-child{border-right:none}
.dchip .dow{font-size:10px;letter-spacing:.13em;text-transform:uppercase;color:var(--ink3)}
.dchip .dnum{font-family:var(--serif);font-weight:600;font-size:21px;line-height:1.15;color:var(--ink)}
.dchip .dct{font-size:10.5px;color:var(--ink3)}
.dchip:hover:not([disabled]){background:#E9F5FB}
.dchip[disabled]{cursor:default;opacity:.34}
.dchip[aria-current="true"]{background:var(--blue)}
.dchip[aria-current="true"] .dow,.dchip[aria-current="true"] .dnum,.dchip[aria-current="true"] .dct{color:#fff}
.empty{border:1.5px dashed var(--steel);background:#F5F8FA;padding:26px 18px;text-align:center;color:var(--ink2);font-size:14px}

.shift{border:1.5px solid var(--ink);background:var(--card);margin-bottom:26px}
.shift-hd{display:flex;align-items:center;gap:12px;justify-content:space-between;padding:11px 16px;background:var(--ink);color:#fff;cursor:pointer}
.shift-hd h2{font-family:var(--serif);font-weight:600;font-size:15px;margin:0;letter-spacing:.03em}
.shift-hd .now{font-family:var(--mono);font-size:11px;color:#AEBCC3}
.shift-hd .shift-edit{font-family:var(--disp);font-weight:700;font-size:12px;letter-spacing:.08em;text-transform:uppercase;
  color:#fff;border:1.5px solid rgba(255,255,255,.55);padding:6px 15px;white-space:nowrap;flex:none}
.shift-hd:hover .shift-edit{background:var(--blue);border-color:var(--blue)}
.shift-bd{padding:16px}
.shift-grid{display:flex;flex-wrap:wrap;gap:22px;align-items:flex-end}
.fld{display:flex;flex-direction:column;gap:5px}
.fld label{font-family:var(--mono);font-size:10px;letter-spacing:.13em;text-transform:uppercase;color:var(--ink2)}
.fld input{font-family:var(--mono);font-size:13px;padding:7px 9px;border:1.5px solid var(--rule);background:#fff;color:var(--ink);width:112px}
.fld input[type=text]{width:104px}
.fld input[type=number]{width:74px}
.fld input:focus{outline:none;border-color:var(--blue)}
.breaks{margin-top:16px;padding-top:14px;border-top:1px solid var(--rule)}
.brk{display:flex;flex-wrap:wrap;gap:12px;align-items:flex-end;margin-bottom:10px}
.btn{font-family:var(--disp);font-weight:700;font-size:13px;text-transform:uppercase;letter-spacing:.07em;
  border:1.5px solid var(--ink);background:#fff;color:var(--ink);padding:8px 15px;cursor:pointer}
.btn:hover{background:var(--ink);color:#fff}
.btn.pri{background:var(--blue);border-color:var(--blue);color:#fff}
.btn.pri:hover{background:var(--blue-dk);border-color:var(--blue-dk)}
.btn.x{padding:8px 12px;border-color:var(--rule);color:var(--ink3)}
.btn.x:hover{background:var(--oxide);border-color:var(--oxide);color:#fff}
.shift-actions{margin-top:16px;padding-top:14px;border-top:1px solid var(--rule);display:flex;gap:10px;flex-wrap:wrap;align-items:center}
.shift-actions .hint{font-size:12.5px;color:var(--ink3)}
.ck{display:flex;align-items:center;gap:8px;font-family:var(--mono);font-size:12px;color:var(--ink2)}

.rate2{display:grid;grid-template-columns:1fr 1fr;border:1.5px solid var(--ink);background:var(--card)}
.rate2 > div{padding:17px 18px}
.rate2 .a{border-right:1.5px solid var(--ink);background:#E9F6FC}
.rate2 .lbl{font-family:var(--mono);font-size:10px;letter-spacing:.14em;text-transform:uppercase;color:var(--ink2)}
.rate2 .big{font-family:var(--serif);font-weight:600;font-size:44px;line-height:1;margin:9px 0 4px;letter-spacing:-.025em;
  font-variant-numeric:tabular-nums}
.rate2 .a .big{color:var(--blue)}
.rate2 .b .big{color:var(--ink2)}
.rate2 .big small{font-size:16px;font-weight:600;color:var(--ink3);margin-left:4px;letter-spacing:0}
.rate2 .foot{font-family:var(--mono);font-size:11px;color:var(--ink3);line-height:1.55}
.bestrun{border:1.5px solid var(--blue);border-left-width:5px;background:#E9F6FC;padding:15px 18px;margin-top:16px}
.bestrun .hd{font-family:var(--serif);font-weight:600;font-size:16px;color:var(--ink);margin-bottom:5px}
.bestrun .bd{font-family:var(--mono);font-size:12.5px;color:var(--ink2);line-height:1.65}
.bestrun .bd b{color:var(--blue);font-weight:600}
@media (max-width:640px){.rate2{grid-template-columns:1fr}.rate2 .a{border-right:none;border-bottom:1.5px solid var(--ink)}}

.split{display:flex;height:34px;border:1.5px solid var(--ink);background:var(--card);overflow:hidden}
.split div{display:flex;align-items:center;justify-content:center;font-family:var(--mono);font-size:11px;color:#fff;
  overflow:hidden;white-space:nowrap}
.split-key{display:flex;flex-wrap:wrap;gap:16px;font-family:var(--mono);font-size:11px;color:var(--ink2);margin-top:10px}
.split-key i{display:inline-block;width:11px;height:11px;margin-right:5px;vertical-align:-1px}
@media (max-width:660px){
  .row{grid-template-columns:76px 1fr;gap:9px}
  .row .rval{grid-column:2;text-align:left;padding-top:2px}
  .tiles{grid-template-columns:1fr 1fr}
  .tile{border-right:none;border-bottom:1.5px solid var(--ink)}
  .tile:nth-child(odd){border-right:1.5px solid var(--ink)}
}
@media (prefers-reduced-motion:no-preference){
  .stud{animation:rise .5s cubic-bezier(.2,.7,.3,1) backwards}
  @keyframes rise{from{transform:scaleY(0);opacity:0}}
}
""";

    private const string Body = """
<div class="wrap">
  <div class="fresh" id="freshBar"></div>
  <div class="notice" id="notice"></div>

  <div class="shift">
    <div class="shift-hd" id="shiftHd" role="button" tabindex="0">
      <h2>Shift and breaks</h2>
      <span class="now" id="shiftNow"></span>
      <span class="shift-edit">Edit</span>
    </div>
    <div class="shift-bd hidden" id="shiftBd">
      <div class="shift-grid">
        <div class="fld"><label for="sStart">Shift starts</label><input type="time" id="sStart"></div>
        <div class="fld"><label for="sEnd">Shift ends</label><input type="time" id="sEnd"></div>
        <div class="fld"><label for="sStop">Stop threshold (min)</label><input type="number" id="sStop" min="1" max="180" step="1"></div>
        <div class="fld" style="flex:1;min-width:210px">
          <span style="font-family:var(--mono);font-size:12px;color:var(--ink3);line-height:1.5">
            A gap longer than the threshold, once breaks and off-shift time are taken out, counts as an unplanned stop.
          </span>
        </div>
      </div>
      <div class="breaks">
        <label style="font-family:var(--mono);font-size:10px;letter-spacing:.13em;text-transform:uppercase;color:var(--ink2);display:block;margin-bottom:10px">Scheduled breaks</label>
        <div id="brkList"></div>
        <button class="btn" id="btnAddBreak">+ Add break</button>
      </div>
      <div class="shift-actions">
        <label class="ck"><input type="checkbox" id="sIgnore"> Ignore the shift altogether &mdash; report output only, no availability</label>
      </div>
      <div class="shift-actions">
        <button class="btn pri" id="btnApplyShift">Apply</button>
        <button class="btn" id="btnResetShift">Reset</button>
        <span class="hint">Applies to every day loaded. Set these to the site's real roster and the availability figures come right.</span>
      </div>
    </div>
  </div>

  <div class="ctlbar">
    <div>
      <div class="metric-lbl">Period</div>
      <div class="tabs" role="tablist">
        <button role="tab" id="tab-month" aria-selected="false" data-view="month">Month</button>
        <button role="tab" id="tab-week" aria-selected="false" data-view="week">Week</button>
        <button role="tab" id="tab-day" aria-selected="true" data-view="day">Day</button>
        <button role="tab" id="tab-hour" aria-selected="false" data-view="hour">Hour</button>
      </div>
    </div>
    <div>
      <div class="metric-lbl">Measure output as</div>
      <div class="metric" role="group" aria-label="Output measure">
        <button id="met-panels" aria-pressed="true"  data-met="panels">Panels<small>completed</small></button>
        <button id="met-cube"   aria-pressed="false" data-met="cube">Cube<small>m&sup3;</small></button>
        <button id="met-lineal" aria-pressed="false" data-met="lineal">Lineal<small>metres</small></button>
      </div>
    </div>
  </div>
  <p class="tabnote" id="tabnote"></p>

  <div class="nav">
    <div class="nav-wk">
      <button class="arw" id="arwPrev" aria-label="Previous">&#8249;</button>
      <button class="arw" id="arwNext" aria-label="Next">&#8250;</button>
      <div>
        <div class="wklbl" id="wkLbl"></div>
        <div class="wksub" id="wkSub"></div>
      </div>
      <button class="jump" id="btnLatest">Latest data</button>
    </div>
    <div class="days" id="dayChips" role="group" aria-label="Days"></div>
  </div>

  <div class="empty hidden" id="emptyState"></div>

  <div id="view-month" class="hidden">
    <section>
      <div class="eyebrow" id="ebMonth">Month totals</div>
      <p class="sub" id="moSub"></p>
      <div class="tiles" id="moTiles"></div>
    </section>
    <section id="secMoSkip" class="hidden">
      <div class="eyebrow">Panels the machine did not build</div>
      <p class="sub" id="moSkipSub"></p>
      <div class="tblwrap"><table id="moSkipTbl"></table></div>
    </section>
    <section>
      <div class="eyebrow" id="ebMoWeek">Output by week</div>
      <p class="sub">Each bar is one week of the month. Click a week to open it.</p>
      <div class="rows" id="moWeekRows"></div>
    </section>
    <section>
      <div class="eyebrow" id="ebMoDay">Output by day</div>
      <p class="sub">Every day in the month, including the ones that produced nothing. Click a day to open it.</p>
      <div class="rows" id="moDayRows"></div>
    </section>
    <section id="secMoAvail">
      <div class="eyebrow">Availability by week</div>
      <p class="sub">Running time as a share of rostered time, once breaks come out. Worked out day by day and then added up, so a short week is not flattered.</p>
      <div class="rows" id="moAvail"></div>
    </section>
    <section>
      <div class="eyebrow">Against the month before</div>
      <p class="sub" id="moPrevSub"></p>
      <div class="bestrun" id="moPrev"></div>
    </section>
  </div>

  <div id="view-week" class="hidden">
    <section>
      <div class="eyebrow" id="ebWeek">Output by day</div>
      <p class="sub">Click a day to open it.</p>
      <div class="rows" id="weekRows"></div>
    </section>
    <section>
      <div class="eyebrow">Week totals</div>
      <div class="tiles" id="weekTiles"></div>
    </section>
    <section id="secWeekAvail">
      <div class="eyebrow">Availability by day</div>
      <p class="sub">Share of rostered run time the machine was actually producing, after breaks and off-shift hours are taken out.</p>
      <div class="rows" id="weekAvail"></div>
    </section>
    <section id="secWeekSkip" class="hidden">
      <div class="eyebrow">Panels the machine did not build</div>
      <p class="sub" id="weekSkipSub"></p>
      <div class="tblwrap"><table id="weekSkipTbl"></table></div>
    </section>
  </div>

  <div id="view-day">
    <section>
      <div class="eyebrow">The day, drawn as a wall</div>
      <p class="sub" id="frameSub"></p>
      <div class="frame-box">
        <svg id="frameSvg" viewBox="0 0 1100 344" role="img" aria-label="Timeline of completed panels across the day"></svg>
        <div class="legend">
          <span><i class="sw" style="background:var(--blue)"></i>At or under the usual build time</span>
          <span><i class="sw" style="background:var(--yellow)"></i>Slower than usual</span>
          <span><i class="sw" style="background:var(--oxide)"></i>Twice the usual or worse</span>
          <span><i class="sw hatch-b"></i>Scheduled break</span>
          <span><i class="sw hatch-r"></i>Unplanned stop</span>
          <span><i class="sw" style="background:#E8EDF0;border:1px solid var(--rule)"></i>Off shift</span>
          <span><i class="sw" style="background:#fff;border:1.2px dashed #BAC6CD"></i>Stepped past &mdash; not counted</span>
          <span><i class="sw" style="background:#fff;border:1.6px dashed var(--oxide)"></i>Went wrong</span>
        </div>
      </div>
    </section>
    <section>
      <div class="eyebrow">Day totals</div>
      <div class="tiles" id="dayTiles"></div>
    </section>
    <section id="secDaySkip" class="hidden">
      <div class="eyebrow">Panels the machine did not build</div>
      <p class="sub" id="daySkipSub"></p>
      <div class="tblwrap"><table id="daySkipTbl"></table></div>
    </section>
    <section id="secSplit">
      <div class="eyebrow">Where the shift went</div>
      <p class="sub" id="splitSub"></p>
      <div class="split" id="splitBar"></div>
      <div class="split-key">
        <span><i style="background:var(--blue)"></i>Running</span>
        <span><i style="background:var(--steel)"></i>Scheduled breaks</span>
        <span><i style="background:var(--oxide)"></i>Unplanned stops</span>
        <span><i style="background:var(--yellow)"></i>Start-up and tail</span>
      </div>
    </section>
    <section id="secRate">
      <div class="eyebrow" id="ebRate">Production rate</div>
      <p class="sub" id="rateSub"></p>
      <div class="rate2">
        <div class="a">
          <div class="lbl">While running</div>
          <div class="big" id="rateRun"></div>
          <div class="foot" id="rateRunFoot"></div>
        </div>
        <div class="b">
          <div class="lbl">Across the whole shift</div>
          <div class="big" id="rateShift"></div>
          <div class="foot" id="rateShiftFoot"></div>
        </div>
      </div>
      <div class="bestrun" id="bestRun"></div>
    </section>
    <section id="secStops">
      <div class="eyebrow">Unplanned stops</div>
      <p class="sub" id="stopSub"></p>
      <div class="rows" id="stopRows"></div>
    </section>
    <section>
      <div class="eyebrow">Every panel</div>
      <p class="sub">Build minutes are what the machine logged, never worked back from the clock.</p>
      <div class="tblwrap"><table id="panelTbl"></table></div>
    </section>
  </div>

  <div id="view-hour" class="hidden">
    <section>
      <div class="eyebrow" id="ebHour">Output per hour</div>
      <p class="sub" id="hourSub"></p>
      <div class="rows" id="hourOut"></div>
    </section>
    <section>
      <div class="eyebrow" id="ebHourRate">Rate per running hour</div>
      <p class="sub">Built only from clean cycles &mdash; one panel finished, the next started straight after with no stop in between. Each cycle counts whole in the hour it finished, so the output and the minutes always cover the same work. Hours with too little clean running to rate fairly are left blank rather than guessed.</p>
      <div class="rows" id="hourRate"></div>
    </section>
    <section>
      <div class="eyebrow">Build time per hour</div>
      <p class="sub">Minutes the machine logged as building, against the hour it finished in. The rest of the hour was waiting, breaks or off shift.</p>
      <div class="rows" id="hourBuild"></div>
    </section>
  </div>

  <footer>
    <span id="footLeft">Spida Machinery</span>
    <span id="footMeta"></span>
    <a href="https://www.spida.com">www.spida.com</a>
  </footer>
</div>
""";

    private const string Script = """
(function(){
'use strict';

var D = PAYLOAD;
var MET = 'panels';
var VIEW = 'day';
var MAXGAP = 16 * 60;
var MONF = ['January','February','March','April','May','June','July','August','September','October','November','December'];
var DOWF = ['Mon','Tue','Wed','Thu','Fri','Sat','Sun'];

function clone(o){ return JSON.parse(JSON.stringify(o)); }
function $(id){ return document.getElementById(id); }
function esc(s){ return String(s==null?'':s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
function pad(n){ return (n<10?'0':'')+n; }
function toMin(s){ var p=String(s).split(':'); return (+p[0])*60 + (+p[1]||0); }
function hhmm(m){ m=Math.round(m); return pad(Math.floor(m/60)%24)+':'+pad(((m%60)+60)%60); }
function n0(v){ return Math.round(v).toLocaleString('en-NZ'); }
function mins(v){ v=Math.round(v); return v>=60 ? Math.floor(v/60)+'h '+pad(v%60)+'m' : v+' min'; }
function pct(v){ return v==null ? '—' : v.toFixed(0)+'%'; }
function sumOf(list,k){ return list.reduce(function(t,p){ return t+(p[k]||0); }, 0); }

var SHIFT = {
  ignored: !!D.shift.ignored,
  start: D.shift.start,
  end: D.shift.end,
  stopMin: D.shift.stopMin,
  breaks: D.shift.breaks.map(function(b){ return {n:b.n, s:b.f, e:b.t}; })
};
var SHIFT0 = clone(SHIFT);

/* ---------- calendar ---------- */
var DAY0 = (function(){ var p=D.day0.split('-'); return new Date(+p[0], +p[1]-1, +p[2]); })();
function dayNum(d){ return Math.round(Date.UTC(d.getFullYear(), d.getMonth(), d.getDate())/86400000); }
var DAY0N = dayNum(DAY0);
function dateOf(i){ return new Date(DAY0.getFullYear(), DAY0.getMonth(), DAY0.getDate()+i); }
function idxOf(d){ return dayNum(d) - DAY0N; }
function dayLbl(i){ var d=dateOf(i); return DOWF[(d.getDay()+6)%7]+' '+d.getDate()+' '+MONF[d.getMonth()].slice(0,3); }
function dayFull(i){ var d=dateOf(i); return DOWF[(d.getDay()+6)%7]+' '+d.getDate()+' '+MONF[d.getMonth()]+' '+d.getFullYear(); }
function weekStart(i){ var d=dateOf(i); return i - ((d.getDay()+6)%7); }
function monthOf(i){ var d=dateOf(i); return {y:d.getFullYear(), m:d.getMonth()}; }

/* ---------- the panels ---------- */
function byMinute(a,b){ return a.m-b.m; }
var P = D.panels.map(function(a){
  return {d:a[0], m:a[1], o:a[2], cube:a[3], lin:a[4], build:a[5], name:a[6], why:a[7]};
});
var BYDAY = {};
P.forEach(function(p){
  var b = BYDAY[p.d] || (BYDAY[p.d] = {done:[], skip:[]});
  (p.o === 0 ? b.done : b.skip).push(p);
});
Object.keys(BYDAY).forEach(function(k){ BYDAY[k].done.sort(byMinute); BYDAY[k].skip.sort(byMinute); });

/* What C# worked out for the shift the report shipped with, kept for the check on load. */
var CS = D.days.map(function(a){
  return {i:a[0], panels:a[1], cube:a[2], lin:a[3], stepped:a[4], faults:a[5],
          planned:a[6], run:a[7], stop:a[8], startup:a[9], tail:a[10], stops:a[11]};
});
var LAST = CS.length ? CS[CS.length-1].i : 0;

/* The usual build time for this machine, used only to colour the wall. Median, so one
   400 minute panel with a missing stop event does not drag the whole day red. */
var MEDBUILD = (function(){
  var v = P.filter(function(p){ return p.o===0 && p.build>0; })
           .map(function(p){ return p.build; }).sort(function(a,b){ return a-b; });
  return v.length ? v[Math.floor(v.length/2)] : 0;
})();
function buildClass(b){
  if(MEDBUILD<=0 || b<=0) return 'tim';
  var r = b/MEDBUILD;
  return r<=1.25 ? 'tim' : (r<2 ? 'mid' : 'over');
}

/* ---------- shift maths, the same rules as the C# side ---------- */
function breakWindows(){
  var s=toMin(SHIFT.start), e=toMin(SHIFT.end), out=[];
  SHIFT.breaks.forEach(function(b){
    var a=Math.max(toMin(b.s), s), z=Math.min(toMin(b.e), e);
    if(z>a) out.push([a, z, b.n]);
  });
  return out.sort(function(x,y){ return x[0]-y[0]; });
}
function breakTotal(){ return breakWindows().reduce(function(t,w){ return t+(w[1]-w[0]); }, 0); }
function plannedPerDay(){
  if(SHIFT.ignored) return 0;
  return Math.max(0, toMin(SHIFT.end) - toMin(SHIFT.start) - breakTotal());
}
/* Minutes between two moments that were never rostered production: off shift at either
   end, plus every scheduled break that falls inside. Taking the break out of the middle
   of a gap is the only reading that survives both a long stoppage starting at lunch and
   lunch itself. */
function nonProd(a,b){
  if(b<=a) return 0;
  var s=toMin(SHIFT.start), e=toMin(SHIFT.end);
  var outside = Math.max(0, Math.min(b,s)-a) + Math.max(0, b-Math.max(a,e));
  var inBrk = breakWindows().reduce(function(t,w){
    return t + Math.max(0, Math.min(b,w[1]) - Math.max(a,w[0]));
  }, 0);
  return outside + inBrk;
}
function prodMin(a,b){ return Math.max(0, (b-a) - nonProd(a,b)); }
function productiveSpans(a,b){
  var s=toMin(SHIFT.start), e=toMin(SHIFT.end);
  var lo=Math.max(a,s), hi=Math.min(b,e);
  if(hi<=lo) return [];
  var out=[], cur=lo;
  breakWindows().forEach(function(w){
    if(w[1]<=lo || w[0]>=hi) return;
    if(w[0]>cur) out.push([cur, Math.min(w[0],hi)]);
    cur = Math.max(cur, Math.min(w[1], hi));
  });
  if(cur<hi) out.push([cur,hi]);
  return out.filter(function(g){ return g[1] > g[0] + 0.01; });
}
function shapeOf(done){
  if(SHIFT.ignored || !done.length)
    return {planned:0, run:0, stop:0, stops:[], startup:0, tail:0, brk:0};
  var s=toMin(SHIFT.start), e=toMin(SHIFT.end);
  var planned = plannedPerDay();
  var first = done[0].m, last = done[done.length-1].m;
  var startup = prodMin(s, Math.min(first, e));
  var tail = prodMin(Math.max(last, s), e);
  var stops = [], total = 0;
  for(var i=1;i<done.length;i++){
    var g = prodMin(done[i-1].m, done[i].m);
    if(g <= SHIFT.stopMin) continue;
    var v = Math.min(g, MAXGAP);
    total += v;
    stops.push({from:done[i-1].m, to:done[i].m, min:v});
  }
  return {planned:planned, run:Math.max(0, planned-startup-tail-total), stop:total,
          stops:stops, startup:startup, tail:tail, brk:breakTotal()};
}

/* ---------- day, week and month roll-ups ---------- */
var STATS = [];
function recompute(){
  STATS = [];
  for(var i=0;i<=LAST;i++){
    var b = BYDAY[i] || {done:[], skip:[]};
    STATS[i] = {
      i:i, done:b.done, skip:b.skip,
      panels:b.done.length,
      cube:sumOf(b.done,'cube'), lin:sumOf(b.done,'lin'), build:sumOf(b.done,'build'),
      stepped:b.skip.filter(function(p){ return p.o===1; }).length,
      faults:b.skip.filter(function(p){ return p.o>=2; }).length,
      sh:shapeOf(b.done)
    };
  }
}
function statsFor(idxs){
  return idxs.filter(function(i){ return i>=0 && i<=LAST; }).map(function(i){ return STATS[i]; });
}
function roll(list){
  var r = {panels:0,cube:0,lin:0,build:0,stepped:0,faults:0,planned:0,run:0,stop:0,
           startup:0,tail:0,stops:0,brk:0,days:0,daysOut:0,skip:[],done:[]};
  list.forEach(function(s){
    r.panels+=s.panels; r.cube+=s.cube; r.lin+=s.lin; r.build+=s.build;
    r.stepped+=s.stepped; r.faults+=s.faults;
    r.planned+=s.sh.planned; r.run+=s.sh.run; r.stop+=s.sh.stop;
    r.startup+=s.sh.startup; r.tail+=s.sh.tail; r.stops+=s.sh.stops.length; r.brk+=s.sh.brk;
    r.days++; if(s.panels>0) r.daysOut++;
    r.skip = r.skip.concat(s.skip); r.done = r.done.concat(s.done);
  });
  r.avail = r.planned>0 ? r.run/r.planned*100 : null;
  return r;
}
function monthDays(mo){
  var out=[], d=new Date(mo.y, mo.m, 1);
  while(d.getMonth()===mo.m){ out.push(idxOf(d)); d.setDate(d.getDate()+1); }
  return out;
}
function monthWeeks(mo){
  var seen={}, out=[];
  monthDays(mo).forEach(function(i){
    var w = weekStart(i);
    if(!seen[w]){ seen[w]=1; out.push(w); }
  });
  return out;
}
function weekDaysOf(ws){ var o=[]; for(var k=0;k<7;k++) o.push(ws+k); return o; }

/* ---------- measures ---------- */
var MEAS = {
  panels:{lbl:'Panels', unit:'', of:function(s){ return s.panels; }, one:function(){ return 1; },
          fmt:function(v){ return n0(v); }},
  cube:  {lbl:'Cube', unit:'m³', of:function(s){ return s.cube; }, one:function(p){ return p.cube; },
          fmt:function(v){ return v.toFixed(2); }},
  lineal:{lbl:'Lineal', unit:'m', of:function(s){ return s.lin; }, one:function(p){ return p.lin; },
          fmt:function(v){ return n0(v); }}
};
function M(){ return MEAS[MET]; }
function allThree(r){
  return n0(r.panels)+' panels · '+r.cube.toFixed(2)+' m³ · '+n0(r.lin)+' m';
}

/* ---------- small renderers ---------- */
function bars(el, items, maxV){
  el.innerHTML = items.map(function(it){
    var w = maxV>0 ? Math.max(it.v>0?1.2:0, it.v/maxV*100) : 0;
    var tp = (it.target!=null && maxV>0) ? Math.min(100, it.target/maxV*100) : null;
    return '<div class="row'+(it.go?' click':'')+'"'+(it.go?' data-go="'+it.go+'"':'')+'>'
      + '<div class="rlbl">'+it.label+'</div>'
      + '<div class="track"><div class="bar '+(it.cls||'tim')+'" style="width:'+w.toFixed(2)+'%"></div>'
      + (tp!=null ? '<div class="tmark" style="left:'+tp.toFixed(2)+'%"></div>' : '')
      + '</div><div class="rval">'+it.right+'</div></div>';
  }).join('') || '<div class="row"><div class="rlbl">—</div><div class="rval">nothing here</div></div>';
}
function tiles(el, list){
  el.innerHTML = list.map(function(t){
    return '<div class="tile '+(t.cls||'')+'"><div class="lbl">'+t.lbl+'</div>'
      + '<div class="val">'+t.val+(t.unit?'<small>'+t.unit+'</small>':'')+'</div>'
      + '<div class="foot">'+(t.foot||'')+'</div></div>';
  }).join('');
}
var RSN = {1:['r-skip','Stepped past'], 2:['r-stop','Operator stopped it'],
           3:['r-nonail','Ran, nailed nothing'], 4:['r-aband','Abandoned part way']};
function skipTable(el, list, withDate){
  if(!list.length){ el.innerHTML=''; return; }
  el.innerHTML = '<thead><tr>'+(withDate?'<th class="l">Date</th>':'')
    + '<th class="l">Time</th><th class="l">Panel</th><th>Lineal m</th><th>Cube m³</th>'
    + '<th>Build min</th><th class="l">What happened</th><th class="l">Why it reads that way</th></tr></thead><tbody>'
    + list.slice().sort(function(a,b){ return (a.d-b.d)||(a.m-b.m); }).map(function(p){
        var r = RSN[p.o] || ['r-skip','Not built'];
        return '<tr>'+(withDate?'<td class="l">'+dayLbl(p.d)+'</td>':'')
          + '<td class="l">'+hhmm(p.m)+'</td><td class="l">'+esc(p.name)+'</td>'
          + '<td>'+p.lin.toFixed(2)+'</td><td>'+p.cube.toFixed(2)+'</td><td>'+p.build.toFixed(1)+'</td>'
          + '<td class="l"><span class="rsn '+r[0]+'">'+r[1]+'</span></td>'
          + '<td class="l">'+esc(p.why)+'</td></tr>';
      }).join('') + '</tbody>';
}
function panelTable(el, done){
  if(!done.length){ el.innerHTML='<tbody><tr><td class="l">No panels completed on this day.</td></tr></tbody>'; return; }
  var prev = null;
  el.innerHTML = '<thead><tr><th class="l">Finished</th><th class="l">Panel</th><th>Lineal m</th>'
    + '<th>Cube m³</th><th>Build min</th><th>Since previous</th></tr></thead><tbody>'
    + done.map(function(p){
        var since = prev==null ? '' : Math.round(p.m - prev);
        prev = p.m;
        return '<tr><td class="l">'+hhmm(p.m)+'</td><td class="l">'+esc(p.name)+'</td>'
          + '<td>'+p.lin.toFixed(2)+'</td><td>'+p.cube.toFixed(2)+'</td>'
          + '<td>'+p.build.toFixed(1)+'</td><td>'+since+'</td></tr>';
      }).join('') + '</tbody>';
}
function skipLine(list){
  var st = list.filter(function(p){ return p.o===1; });
  var fl = list.filter(function(p){ return p.o>=2; });
  var out = '';
  if(st.length)
    out += '<b>'+n0(st.length)+' stepped past</b> — advanced on the HMI without being built, '
        + sumOf(st,'lin').toFixed(1)+' m and '+sumOf(st,'cube').toFixed(2)+' m³ sent to the machine '
        + 'and made somewhere else. Routine here rather than a fault, and left out of every figure above. ';
  out += fl.length
    ? '<b>'+n0(fl.length)+' went wrong</b> and '+(fl.length===1?'is':'are')+' listed below.'
    : 'Nothing went wrong.';
  return out;
}

/* ---------- the wall ---------- */
function drawFrame(st){
  var svg = $('frameSvg');
  var done = st.done, sk = st.skip;
  if(!done.length && !sk.length){ svg.innerHTML=''; return; }

  var W=1100, H=344, L=44, R=22, RAIL_T=8, RAIL_B=26, RAIL_LBL=40, TOP=56, BOT=272;
  var live = !SHIFT.ignored;
  var sS = live ? toMin(SHIFT.start) : 0;
  var sE = live ? toMin(SHIFT.end) : 1440;

  var all = done.concat(sk).slice().sort(byMinute);
  var first = all[0].m, last = all[all.length-1].m;
  var h0 = Math.max(0, Math.min(Math.floor(sS/60), Math.floor(first/60)));
  var h1 = Math.min(24, Math.max(Math.ceil(sE/60), Math.floor(last/60)+1));
  if(h1 <= h0) h1 = Math.min(24, h0+1);
  var span = (h1-h0)*60;
  var xm = function(m){ return L + ((m - h0*60)/span)*(W-L-R); };

  var maxV = Math.max.apply(null, done.map(function(p){ return M().one(p); }).concat([0.0001]));
  var yT = TOP+11, hH = BOT-TOP-11;
  var s = '';

  s += '<defs>'
    + '<pattern id="brk" width="7" height="7" patternTransform="rotate(45)" patternUnits="userSpaceOnUse">'
    + '<rect width="7" height="7" fill="#DCE4E8"/><line x1="0" y1="0" x2="0" y2="7" stroke="#8FA5AE" stroke-width="3.4"/></pattern>'
    + '<pattern id="stp" width="7" height="7" patternTransform="rotate(45)" patternUnits="userSpaceOnUse">'
    + '<rect width="7" height="7" fill="#F7DED9"/><line x1="0" y1="0" x2="0" y2="7" stroke="#DE8E80" stroke-width="3.4"/></pattern>'
    + '</defs>';

  if(live){
    var ra = xm(Math.max(sS, h0*60)), rb = xm(Math.min(sE, h1*60));
    s += '<rect x="'+ra+'" y="'+RAIL_T+'" width="'+Math.max(0,rb-ra)+'" height="'+(RAIL_B-RAIL_T)+'" fill="#E1F0F7"/>';
    var shiftLab = 'Shift '+SHIFT.start+'–'+SHIFT.end;
    s += '<text x="'+L+'" y="'+RAIL_LBL+'" font-family="monospace" font-size="11" fill="#0077A8">'+shiftLab+'</text>';

    /* The rail gets crowded on a day that runs well wide of the roster, so a label is
       only drawn where there is clear room for it. Every block keeps its tooltip. */
    var labEnd = L + shiftLab.length*6.4 + 12;
    breakWindows().forEach(function(w){
      var xa = xm(w[0]), xb = Math.max(xm(w[1]), xa+4), cx = (xa+xb)/2;
      s += '<rect x="'+xa+'" y="'+RAIL_T+'" width="'+(xb-xa)+'" height="'+(RAIL_B-RAIL_T)+'" fill="#56666E">'
         + '<title>'+esc(w[2])+' '+hhmm(w[0])+'–'+hhmm(w[1])+'</title></rect>';
      var half = (w[2]+' 00:00–00:00').length*3.2;
      if(cx-half < labEnd || cx+half > W-R) return;
      labEnd = cx + half + 12;
      s += '<text x="'+cx+'" y="'+RAIL_LBL+'" text-anchor="middle" font-family="monospace" font-size="11" fill="#425969">'
         + '<tspan font-weight="600">'+esc(w[2])+'</tspan> '+hhmm(w[0])+'–'+hhmm(w[1])+'</text>';
    });

    if(sS > h0*60){
      s += '<rect x="'+L+'" y="'+yT+'" width="'+(xm(sS)-L)+'" height="'+hH+'" fill="#E8EDF0"/>';
      s += '<text x="'+((L+xm(sS))/2)+'" y="'+(BOT-8)+'" text-anchor="middle" font-family="monospace" font-size="10.5" fill="#8A9AA6">off shift</text>';
    }
    if(sE < h1*60){
      var xe = xm(sE), xr = L+(W-L-R);
      s += '<rect x="'+xe+'" y="'+yT+'" width="'+(xr-xe)+'" height="'+hH+'" fill="#E8EDF0"/>';
      s += '<text x="'+((xe+xr)/2)+'" y="'+(BOT-8)+'" text-anchor="middle" font-family="monospace" font-size="10.5" fill="#8A9AA6">off shift</text>';
    }

    /* Stops are drawn only over rostered run time, so a break inside a stop splits it
       into a before-part and an after-part instead of swallowing it whole. */
    st.sh.stops.forEach(function(stop){
      var segs = productiveSpans(stop.from, stop.to);
      if(!segs.length) return;
      var tip = 'Unplanned stop '+hhmm(stop.from)+'–'+hhmm(stop.to)+' · '
              + Math.round(stop.min)+' min of run time lost'
              + (segs.length>1 ? ', split by a break into '
                 + segs.map(function(g){ return Math.round(g[1]-g[0])+' min'; }).join(' + ') : '');
      segs.forEach(function(g,k){
        var xa = xm(g[0]), xb = xm(g[1]);
        if(k===0) xa += 7;
        if(k===segs.length-1) xb -= 7;
        if(xb <= xa+3) return;
        var w = xb-xa, cx = (xa+xb)/2, mn = Math.round(g[1]-g[0]);
        s += '<rect x="'+xa+'" y="'+yT+'" width="'+w+'" height="'+hH+'" fill="url(#stp)"><title>'+esc(tip)+'</title></rect>';
        var lab = w>=88 ? mn+' min stop' : (w>=40 ? mn+' min' : '');
        if(lab) s += '<text x="'+cx+'" y="'+((TOP+BOT)/2)+'" text-anchor="middle" font-family="monospace" font-size="12" fill="#A33124">'+lab+'</text>';
      });
    });

    breakWindows().forEach(function(w){
      var xa = xm(w[0]), xb = Math.max(xm(w[1]), xa+3);
      s += '<rect x="'+xa+'" y="'+yT+'" width="'+(xb-xa)+'" height="'+hH+'" fill="url(#brk)"/>';
      s += '<rect x="'+xa+'" y="'+yT+'" width="'+(xb-xa)+'" height="'+hH+'" fill="none" stroke="#7E939C" stroke-width="1">'
         + '<title>'+esc(w[2])+' '+hhmm(w[0])+'–'+hhmm(w[1])+'</title></rect>';
    });
  }

  /* Panels the machine did not build: outlined, so they stay visible without counting. */
  sk.forEach(function(p){
    var bad = p.o>=2, cx = xm(p.m), hgt = BOT-TOP-(bad?86:118);
    s += '<rect x="'+(cx-4)+'" y="'+(BOT-hgt)+'" width="8" height="'+hgt+'" fill="#fff" stroke="'+(bad?'#C0392B':'#BAC6CD')+'"'
       + ' stroke-width="'+(bad?1.8:1.2)+'" stroke-dasharray="4 3"><title>'+hhmm(p.m)+' · panel '+esc(p.name)
       + ' · '+esc((RSN[p.o]||['','not built'])[1])+' — '+esc(p.why)
       + '. Not counted in any figure on this page.</title></rect>';
  });

  var flat = MET === 'panels';
  /* A busy day puts 120 panels across the same wall a quiet one puts 20 on, so the studs
     thin out rather than running into each other. */
  var wdt = Math.max(3, Math.min(9, ((W-L-R)/Math.max(done.length,1))*0.7));
  var perStud = Math.min(40, 1200/Math.max(done.length,1));
  done.forEach(function(p,i){
    var cx = xm(p.m);
    var hgt = flat ? (BOT-TOP-62) : 24 + (M().one(p)/maxV)*(BOT-TOP-50);
    var cls = buildClass(p.build);
    var col = cls==='tim' ? '#009CDE' : (cls==='mid' ? '#F0A02A' : '#C0392B');
    s += '<g class="stud" style="transform-origin:'+cx+'px '+BOT+'px;animation-delay:'+(i*perStud).toFixed(0)+'ms">'
       + '<rect x="'+(cx-wdt/2)+'" y="'+(BOT-hgt)+'" width="'+wdt.toFixed(2)+'" height="'+hgt+'" fill="'+col+'" stroke="#425969" stroke-width="'+(wdt>=6?1:0.6)+'"/>'
       + '<title>'+hhmm(p.m)+' · panel '+esc(p.name)+' · '+p.lin.toFixed(2)+' m · '+p.cube.toFixed(2)+' m³'
       + ' · built in '+p.build.toFixed(1)+' min'
       + (MEDBUILD>0 ? ' against a usual '+MEDBUILD.toFixed(1)+' min' : '')
       + '</title></g>';
  });

  s += '<rect x="'+(L-14)+'" y="'+TOP+'" width="'+(W-L-R+28)+'" height="11" fill="#425969"/>';
  s += '<rect x="'+(L-14)+'" y="'+BOT+'" width="'+(W-L-R+28)+'" height="11" fill="#425969"/>';

  if(live){
    [[sS,'shift start'],[sE,'shift end']].forEach(function(pair){
      var m = pair[0];
      if(m < h0*60 || m > h1*60) return;
      s += '<line x1="'+xm(m)+'" y1="'+RAIL_T+'" x2="'+xm(m)+'" y2="'+(BOT+11)+'" stroke="#009CDE" stroke-width="2" stroke-dasharray="5 4"/>';
      s += '<text x="'+(xm(m)+5)+'" y="'+(TOP+26)+'" font-family="monospace" font-size="10.5" fill="#0077A8">'+pair[1]+'</text>';
    });
  }

  var stepHr = Math.max(1, Math.ceil((h1-h0)/14));
  for(var hh=h0; hh<=h1; hh+=stepHr){
    var px = xm(hh*60);
    s += '<line x1="'+px+'" y1="'+(BOT+11)+'" x2="'+px+'" y2="'+(BOT+18)+'" stroke="#A8B5BB" stroke-width="1.5"/>';
    s += '<text x="'+px+'" y="'+(BOT+34)+'" text-anchor="middle" font-family="monospace" font-size="12" fill="#5E7382">'+pad(hh)+':00</text>';
  }

  var capt = MET==='panels' ? 'ONE STUD PER PANEL'
           : (MET==='cube' ? 'STUD HEIGHT = PANEL CUBE' : 'STUD HEIGHT = PANEL LENGTH');
  s += '<text x="'+(L-14)+'" y="'+(BOT+54)+'" font-family="monospace" font-size="10.5" letter-spacing="1.6" fill="#8A9AA6">'+capt+'</text>';
  if(sk.length)
    s += '<text x="'+(L-14+250)+'" y="'+(BOT+54)+'" font-family="monospace" font-size="10.5" letter-spacing="1.6" fill="#8A9AA6">'
       + 'DASHED = NOT BUILT, NOT COUNTED · RED DASHED = WENT WRONG</text>';

  svg.setAttribute('viewBox', '0 0 '+W+' '+H);
  svg.innerHTML = s;
}

/* ---------- day ---------- */
function renderDay(){
  var st = STATS[SEL_DAY] || {done:[], skip:[], panels:0, cube:0, lin:0, build:0, stepped:0, faults:0,
                              sh:{planned:0,run:0,stop:0,stops:[],startup:0,tail:0,brk:0}};
  var r = roll([st]);
  drawFrame(st);

  $('frameSub').innerHTML = 'Each stud is one completed panel, stood where it finished. '
    + (MET==='panels' ? 'Every stud the same height — one panel, one stud. '
       : MET==='cube' ? 'Stud height is the panel cube in m³. '
       : 'Stud height is the panel length in metres. ')
    + (SHIFT.ignored
        ? 'No shift model, so no break or stop shading is drawn.'
        : 'Grey is off shift, hatched blue is a scheduled break, hatched red is an unplanned stop. A break falling inside a stop is drawn over the top of it, so the stop shows as a before-part and an after-part.')
    + (st.skip.length ? ' Hollow dashed studs are panels the machine was sent and did not build — grey where they were stepped past, red where something went wrong. Neither counts towards anything.' : '');

  var avail = r.avail;
  tiles($('dayTiles'), [
    {lbl:M().lbl+' out', val:M().fmt(M().of(st)), unit:M().unit, foot:allThree(r)},
    {lbl:'Availability', val:SHIFT.ignored?'—':pct(avail).replace('%',''), unit:SHIFT.ignored?'':'%',
     foot:SHIFT.ignored ? 'no shift model set' : mins(r.run)+' running of '+mins(r.planned)+' rostered',
     cls:SHIFT.ignored?'':(avail!=null && avail<70?'warn':'ok')},
    {lbl:'Unplanned stops', val:SHIFT.ignored?'—':Math.round(r.stop), unit:SHIFT.ignored?'':'min',
     foot:SHIFT.ignored?'needs a shift model':r.stops+' stop'+(r.stops===1?'':'s')+' over '+SHIFT.stopMin+' min',
     cls:(!SHIFT.ignored && r.stop>45)?'warn':''},
    {lbl:'Start-up to first panel', val:SHIFT.ignored?'—':Math.round(r.startup), unit:SHIFT.ignored?'':'min',
     foot:(st.done.length?'first out '+hhmm(st.done[0].m):'nothing built')+(SHIFT.ignored?'':' · shift '+SHIFT.start),
     cls:(!SHIFT.ignored && r.startup>30)?'warn':''},
    {lbl:'After last panel', val:SHIFT.ignored?'—':Math.round(r.tail), unit:SHIFT.ignored?'':'min',
     foot:(st.done.length?'last out '+hhmm(st.done[st.done.length-1].m):'nothing built')+(SHIFT.ignored?'':' · ends '+SHIFT.end),
     cls:(!SHIFT.ignored && r.tail>45)?'warn':''},
    {lbl:'Stepped past', val:n0(st.stepped), unit:'',
     foot: st.stepped
        ? Math.round(st.stepped/(st.panels+st.skip.length)*100)+'% of what the machine was sent'
        : 'nothing skipped on the HMI'},
    {lbl:'Went wrong', val:n0(st.faults), unit:'',
     foot: st.faults ? 'listed below' : 'nothing went wrong', cls: st.faults?'warn':'ok'}
  ]);

  var dsec = $('secDaySkip');
  if(st.skip.length){
    dsec.classList.remove('hidden');
    $('daySkipSub').innerHTML = skipLine(st.skip);
    skipTable($('daySkipTbl'), st.skip.filter(function(p){ return p.o>=2; }), false);
  } else { dsec.classList.add('hidden'); $('daySkipTbl').innerHTML=''; }

  var sp = $('secSplit');
  if(SHIFT.ignored || r.planned<=0){
    sp.classList.add('hidden');
  } else {
    sp.classList.remove('hidden');
    var seg = [
      {v:r.run, c:'var(--blue)', n:'Running'},
      {v:r.brk, c:'var(--steel)', n:'Scheduled breaks'},
      {v:r.stop, c:'var(--oxide)', n:'Unplanned stops'},
      {v:r.startup+r.tail, c:'var(--yellow)', n:'Start-up and tail'}
    ];
    var tot = seg.reduce(function(a,b){ return a+b.v; }, 0) || 1;
    $('splitSub').textContent = 'The rostered day, minute by minute. Breaks are shown for scale; they are '
      + 'already out of the rostered time availability is measured against.';
    $('splitBar').innerHTML = seg.filter(function(x){ return x.v>0.5; }).map(function(x){
      return '<div style="width:'+(x.v/tot*100).toFixed(2)+'%;background:'+x.c+'" title="'+x.n+': '
        + Math.round(x.v)+' min">'+(x.v/tot>0.11 ? Math.round(x.v)+' min' : '')+'</div>';
    }).join('');
  }

  var rsec = $('secRate');
  if(SHIFT.ignored || r.planned<=0){
    rsec.classList.add('hidden');
  } else {
    rsec.classList.remove('hidden');
    var per = MET==='panels' ? 'panels' : (MET==='cube' ? 'm³' : 'm');
    var out = M().of(st);
    var runRate = r.run>0 ? out/(r.run/60) : null;
    var shiftRate = r.planned>0 ? out/(r.planned/60) : null;
    $('ebRate').textContent = 'Production rate — '+M().lbl.toLowerCase()+' an hour';
    $('rateSub').textContent = 'The first figure is what the machine does when it is going. The second is '
      + 'what the site actually gets out of the day. The gap between them is the stops.';
    $('rateRun').innerHTML = (runRate==null?'—':runRate.toFixed(MET==='cube'?2:1))+'<small>'+per+'/h</small>';
    $('rateRunFoot').textContent = mins(r.run)+' of running time';
    $('rateShift').innerHTML = (shiftRate==null?'—':shiftRate.toFixed(MET==='cube'?2:1))+'<small>'+per+'/h</small>';
    $('rateShiftFoot').textContent = mins(r.planned)+' rostered, breaks already out';

    var best = bestRun(st.done);
    $('bestRun').innerHTML = best
      ? '<div class="hd">Best unbroken run of the day</div><div class="bd"><b>'+best.count+' panels</b> between '
        + hhmm(best.from)+' and '+hhmm(best.to)+', no stop over '+SHIFT.stopMin+' min — <b>'
        + best.rate.toFixed(1)+' panels an hour</b>. That is what this machine can do when nothing gets in its way.</div>'
      : '<div class="hd">Best unbroken run of the day</div><div class="bd">Not enough consecutive panels to call one.</div>';
  }

  var stopSec = $('secStops');
  if(SHIFT.ignored){ stopSec.classList.add('hidden'); }
  else {
    stopSec.classList.remove('hidden');
    $('stopSub').textContent = st.sh.stops.length
      ? 'Every gap longer than '+SHIFT.stopMin+' min of rostered run time, breaks already taken out of the middle.'
      : 'No gap on this day was longer than '+SHIFT.stopMin+' min of rostered run time.';
    var mx = Math.max.apply(null, st.sh.stops.map(function(s){ return s.min; }).concat([1]));
    bars($('stopRows'), st.sh.stops.map(function(s){
      return {label:hhmm(s.from)+'–'+hhmm(s.to), v:s.min,
              cls:s.min>60?'over':(s.min>30?'mid':''), right:Math.round(s.min)+' min'};
    }), mx);
  }

  panelTable($('panelTbl'), st.done);
}

function bestRun(done){
  if(done.length < 3) return null;
  var best = null, startAt = 0;
  for(var i=1; i<=done.length; i++){
    var broken = i===done.length || prodMin(done[i-1].m, done[i].m) > SHIFT.stopMin;
    if(!broken) continue;
    var count = i - startAt;
    var from = done[startAt].m, to = done[i-1].m;
    var span = prodMin(from, to);
    if(count >= 3 && span > 0){
      var rate = count/(span/60);
      if(!best || count > best.count) best = {count:count, from:from, to:to, rate:rate};
    }
    startAt = i;
  }
  return best;
}

/* ---------- hour ---------- */
function renderHour(){
  var st = STATS[SEL_DAY];
  if(!st){ return; }
  var done = st.done;

  $('ebHour').textContent = 'Output per hour — '+M().lbl.toLowerCase()+', '+dayFull(SEL_DAY);
  $('hourSub').textContent = 'Each panel counts in the hour it finished. '
    + (done.length ? 'Hours outside '+hhmm(done[0].m)+' to '+hhmm(done[done.length-1].m)+' are left off.' : '');

  var h0 = 24, h1 = 0;
  done.concat(st.skip).forEach(function(p){
    h0 = Math.min(h0, Math.floor(p.m/60));
    h1 = Math.max(h1, Math.floor(p.m/60));
  });
  if(h0 > h1){ h0 = 0; h1 = 0; }

  var out = [], rate = [], build = [];
  var maxOut = 0, maxRate = 0, maxBuild = 0;

  /* Clean cycles: one panel finished, the next started straight after with no stop in
     between. The cycle is counted whole in the hour it finished, so the output and the
     minutes always cover the same work. */
  var cycle = {};
  for(var i=1;i<done.length;i++){
    /* Raw elapsed, not rostered: a gap that only looks short because a break or the end
       of the shift was taken out of the middle is not one panel following another. */
    var raw = done[i].m - done[i-1].m;
    if(raw > SHIFT.stopMin) continue;
    var h = Math.floor(done[i].m/60);
    var c = cycle[h] || (cycle[h] = {n:0, min:0});
    c.n += 1;
    c.min += raw;
  }

  for(var hh=h0; hh<=h1; hh++){
    var inHour = done.filter(function(p){ return Math.floor(p.m/60)===hh; });
    var v = inHour.reduce(function(t,p){ return t + M().one(p); }, 0);
    maxOut = Math.max(maxOut, v);
    out.push({label:pad(hh)+':00', v:v, cls:'tim',
              right: M().fmt(v)+' '+(M().unit||'panels')
                     + (inHour.length && MET!=='panels' ? ' · '+inHour.length+' panels' : '')});

    var c = cycle[hh];
    var rv = (c && c.min >= 10) ? c.n/(c.min/60) : null;
    if(rv!=null) maxRate = Math.max(maxRate, rv);
    rate.push({label:pad(hh)+':00', v:rv||0, cls:rv==null?'dim':'tim',
               right: rv==null ? 'too little clean running' : rv.toFixed(1)+' panels/h over '+Math.round(c.min)+' min'});

    var bv = inHour.reduce(function(t,p){ return t + p.build; }, 0);
    maxBuild = Math.max(maxBuild, bv);
    build.push({label:pad(hh)+':00', v:bv, cls: bv>60 ? 'mid' : 'tim',
                right: bv.toFixed(0)+' of 60 min'});
  }

  bars($('hourOut'), out, maxOut);
  bars($('hourRate'), rate, maxRate);
  bars($('hourBuild'), build, Math.max(maxBuild, 60));
  $('ebHourRate').textContent = 'Rate per running hour';
}

/* ---------- week ---------- */
function renderWeek(){
  var days = statsFor(weekDaysOf(SEL_WEEK));
  var r = roll(days);

  $('ebWeek').textContent = 'Output by day — '+M().lbl.toLowerCase();
  var mx = Math.max.apply(null, days.map(function(s){ return M().of(s); }).concat([0.0001]));
  bars($('weekRows'), days.map(function(s){
    return {label:dayLbl(s.i), v:M().of(s), cls:'tim', go:'day:'+s.i,
            right: s.panels ? M().fmt(M().of(s))+' '+(M().unit||'panels') : 'nothing built'};
  }), mx);

  tiles($('weekTiles'), [
    {lbl:M().lbl+' out', val:M().fmt(M().of(r)), unit:M().unit, foot:allThree(r)},
    {lbl:'Days that produced', val:r.daysOut, unit:'of '+r.days, foot:'calendar days in the week'},
    {lbl:'Availability', val:SHIFT.ignored?'—':pct(r.avail).replace('%',''), unit:SHIFT.ignored?'':'%',
     foot:SHIFT.ignored?'no shift model set':mins(r.run)+' of '+mins(r.planned)+' rostered',
     cls:SHIFT.ignored?'':(r.avail!=null && r.avail<70?'warn':'ok')},
    {lbl:'Stepped past', val:n0(r.stepped), unit:'', foot:'advanced on the HMI, not built'},
    {lbl:'Went wrong', val:n0(r.faults), unit:'', foot:r.faults?'listed below':'nothing went wrong',
     cls:r.faults?'warn':'ok'}
  ]);

  var av = $('secWeekAvail');
  if(SHIFT.ignored){ av.classList.add('hidden'); }
  else {
    av.classList.remove('hidden');
    bars($('weekAvail'), days.map(function(s){
      var a = s.sh.planned>0 ? s.sh.run/s.sh.planned*100 : 0;
      return {label:dayLbl(s.i), v:a, cls: a>=80?'tim':(a>=60?'mid':(a>0?'over':'dim')), go:'day:'+s.i,
              right: s.sh.planned>0 ? a.toFixed(0)+'% · '+mins(s.sh.run)+' running' : 'nothing built'};
    }), 100);
  }

  var sk = $('secWeekSkip');
  if(r.skip.length){
    sk.classList.remove('hidden');
    $('weekSkipSub').innerHTML = skipLine(r.skip);
    skipTable($('weekSkipTbl'), r.skip.filter(function(p){ return p.o>=2; }), true);
  } else { sk.classList.add('hidden'); $('weekSkipTbl').innerHTML=''; }
}

/* ---------- month ---------- */
function renderMonth(){
  var idxs = monthDays(SEL_MONTH);
  var days = statsFor(idxs);
  var r = roll(days);

  $('ebMonth').textContent = MONF[SEL_MONTH.m]+' '+SEL_MONTH.y+' totals';
  $('moSub').textContent = r.daysOut+' of '+idxs.length+' calendar days produced something.';

  tiles($('moTiles'), [
    {lbl:M().lbl+' out', val:M().fmt(M().of(r)), unit:M().unit, foot:allThree(r)},
    {lbl:'A production day', val: r.daysOut ? M().fmt(M().of(r)/r.daysOut) : '—', unit:M().unit,
     foot:'averaged over the '+r.daysOut+' days that produced'},
    {lbl:'Availability', val:SHIFT.ignored?'—':pct(r.avail).replace('%',''), unit:SHIFT.ignored?'':'%',
     foot:SHIFT.ignored?'no shift model set':mins(r.run)+' of '+mins(r.planned)+' rostered',
     cls:SHIFT.ignored?'':(r.avail!=null && r.avail<70?'warn':'ok')},
    {lbl:'Unplanned stops', val:SHIFT.ignored?'—':n0(r.stops), unit:'',
     foot:SHIFT.ignored?'needs a shift model':mins(r.stop)+' lost in total'},
    {lbl:'Stepped past', val:n0(r.stepped), unit:'', foot:'advanced on the HMI, not built'},
    {lbl:'Went wrong', val:n0(r.faults), unit:'',
     foot: (r.panels+r.stepped+r.faults)>0
        ? (r.faults/(r.panels+r.stepped+r.faults)*100).toFixed(2)+'% of everything that closed'
        : 'nothing closed', cls:r.faults?'warn':'ok'}
  ]);

  var sk = $('secMoSkip');
  if(r.skip.length){
    sk.classList.remove('hidden');
    $('moSkipSub').innerHTML = skipLine(r.skip);
    skipTable($('moSkipTbl'), r.skip.filter(function(p){ return p.o>=2; }), true);
  } else { sk.classList.add('hidden'); $('moSkipTbl').innerHTML=''; }

  var weeks = monthWeeks(SEL_MONTH);
  var wr = weeks.map(function(w){
    var inMonth = weekDaysOf(w).filter(function(i){
      var d = dateOf(i);
      return d.getMonth()===SEL_MONTH.m && d.getFullYear()===SEL_MONTH.y;
    });
    return {w:w, r:roll(statsFor(inMonth)), n:inMonth.length};
  });

  $('ebMoWeek').textContent = 'Output by week — '+M().lbl.toLowerCase();
  var mxw = Math.max.apply(null, wr.map(function(x){ return M().of(x.r); }).concat([0.0001]));
  bars($('moWeekRows'), wr.map(function(x){
    return {label:'w/c '+dayLbl(x.w).replace(/^\w+ /,''), v:M().of(x.r), cls:'tim', go:'week:'+x.w,
            right: M().fmt(M().of(x.r))+' '+(M().unit||'panels')+' · '+x.r.daysOut+'/'+x.n+' days'};
  }), mxw);

  $('ebMoDay').textContent = 'Output by day — '+M().lbl.toLowerCase();
  var mxd = Math.max.apply(null, days.map(function(s){ return M().of(s); }).concat([0.0001]));
  bars($('moDayRows'), days.map(function(s){
    return {label:dayLbl(s.i), v:M().of(s), cls: s.panels?'tim':'dim', go:'day:'+s.i,
            right: s.panels ? M().fmt(M().of(s))+' '+(M().unit||'panels') : 'nothing built'};
  }), mxd);

  var av = $('secMoAvail');
  if(SHIFT.ignored){ av.classList.add('hidden'); }
  else {
    av.classList.remove('hidden');
    bars($('moAvail'), wr.map(function(x){
      var a = x.r.planned>0 ? x.r.avail : 0;
      return {label:'w/c '+dayLbl(x.w).replace(/^\w+ /,''), v:a,
              cls: a>=80?'tim':(a>=60?'mid':(a>0?'over':'dim')), go:'week:'+x.w,
              right: x.r.planned>0 ? a.toFixed(0)+'% · '+mins(x.r.run)+' running' : 'nothing built'};
    }), 100);
  }

  var prevMo = SEL_MONTH.m===0 ? {y:SEL_MONTH.y-1, m:11} : {y:SEL_MONTH.y, m:SEL_MONTH.m-1};
  var prev = roll(statsFor(monthDays(prevMo)));
  $('moPrevSub').textContent = 'Same measure, month against month. A month with fewer working days in it will '
    + 'show less output without the machine having done anything differently, so the per-production-day figure '
    + 'is the fairer one.';
  if(prev.days===0 || prev.panels===0){
    $('moPrev').innerHTML = '<div class="hd">'+MONF[prevMo.m]+' '+prevMo.y+'</div>'
      + '<div class="bd">Nothing stored for the month before, so there is nothing to compare against.</div>';
  } else {
    var now = M().of(r), was = M().of(prev);
    var d = was>0 ? (now-was)/was*100 : null;
    var nowPer = r.daysOut ? now/r.daysOut : 0, wasPer = prev.daysOut ? was/prev.daysOut : 0;
    var dPer = wasPer>0 ? (nowPer-wasPer)/wasPer*100 : null;
    $('moPrev').innerHTML = '<div class="hd">'+MONF[SEL_MONTH.m]+' '+SEL_MONTH.y+' against '+MONF[prevMo.m]+' '+prevMo.y+'</div>'
      + '<div class="bd">Total out <b>'+M().fmt(now)+'</b> against '+M().fmt(was)
      + (d==null?'':' — <b>'+(d>=0?'+':'')+d.toFixed(1)+'%</b>')
      + '.<br>On a production day <b>'+M().fmt(nowPer)+'</b> against '+M().fmt(wasPer)
      + (dPer==null?'':' — <b>'+(dPer>=0?'+':'')+dPer.toFixed(1)+'%</b>')
      + '.<br>Days that produced <b>'+r.daysOut+'</b> against '+prev.daysOut+'.'
      + (SHIFT.ignored ? '' : '<br>Availability <b>'+pct(r.avail)+'</b> against '+pct(prev.avail)+'.')
      + '</div>';
  }
}

/* ---------- navigation ---------- */
var SEL_DAY = (function(){
  for(var i=LAST;i>=0;i--){ if(BYDAY[i] && BYDAY[i].done.length) return i; }
  return LAST;
})();
var SEL_WEEK = weekStart(SEL_DAY);
var SEL_MONTH = monthOf(SEL_DAY);

function drawNav(){
  var chips = $('dayChips');
  if(VIEW === 'month'){
    $('wkLbl').textContent = MONF[SEL_MONTH.m]+' '+SEL_MONTH.y;
    var idxs = monthDays(SEL_MONTH), have = statsFor(idxs);
    $('wkSub').textContent = have.length+' day(s) stored · click a week to open it';
    chips.className = 'days wks';
    chips.innerHTML = monthWeeks(SEL_MONTH).map(function(w){
      var inMonth = weekDaysOf(w).filter(function(i){
        var d = dateOf(i);
        return d.getMonth()===SEL_MONTH.m && d.getFullYear()===SEL_MONTH.y;
      });
      var rr = roll(statsFor(inMonth));
      return '<button class="dchip" data-go="week:'+w+'"'+(rr.days?'':' disabled')+'>'
        + '<span class="dow">w/c</span><span class="dnum">'+dateOf(w).getDate()+'</span>'
        + '<span class="dct">'+(rr.days ? M().fmt(M().of(rr)) : '—')+'</span></button>';
    }).join('');
  } else {
    var a = SEL_WEEK, b = SEL_WEEK+6;
    $('wkLbl').textContent = 'Week of '+dateOf(a).getDate()+' '+MONF[dateOf(a).getMonth()].slice(0,3)+' '+dateOf(a).getFullYear();
    $('wkSub').textContent = dayLbl(a)+' – '+dayLbl(b);
    chips.className = 'days';
    chips.innerHTML = weekDaysOf(SEL_WEEK).map(function(i){
      var s = (i>=0 && i<=LAST) ? STATS[i] : null;
      var d = dateOf(i);
      return '<button class="dchip" data-go="day:'+i+'"'+(s?'':' disabled')
        + (i===SEL_DAY && VIEW!=='month' ? ' aria-current="true"' : '')+'>'
        + '<span class="dow">'+DOWF[(d.getDay()+6)%7]+'</span>'
        + '<span class="dnum">'+d.getDate()+'</span>'
        + '<span class="dct">'+(s ? (s.panels ? M().fmt(M().of(s)) : '0') : '—')+'</span></button>';
    }).join('');
  }
}

function step(n){
  if(VIEW === 'month'){
    var d = new Date(SEL_MONTH.y, SEL_MONTH.m + n, 1);
    SEL_MONTH = {y:d.getFullYear(), m:d.getMonth()};
  } else {
    SEL_WEEK += n*7;
    var pick = weekDaysOf(SEL_WEEK).filter(function(i){ return i>=0 && i<=LAST && STATS[i].panels>0; });
    if(!pick.length) pick = weekDaysOf(SEL_WEEK).filter(function(i){ return i>=0 && i<=LAST; });
    SEL_DAY = pick.length ? pick[0] : SEL_DAY;
  }
  render();
}
function jumpLatest(){
  for(var i=LAST;i>=0;i--){
    if(BYDAY[i] && BYDAY[i].done.length){ SEL_DAY=i; break; }
  }
  SEL_WEEK = weekStart(SEL_DAY);
  SEL_MONTH = monthOf(SEL_DAY);
  render();
}
function goTo(token){
  var bits = String(token).split(':'), what = bits[0], n = +bits[1];
  if(what === 'day'){
    SEL_DAY = n; SEL_WEEK = weekStart(n); SEL_MONTH = monthOf(n);
    if(VIEW === 'month' || VIEW === 'week') setView('day'); else render();
  } else if(what === 'week'){
    SEL_WEEK = n;
    var pick = weekDaysOf(n).filter(function(i){ return i>=0 && i<=LAST && STATS[i].panels>0; });
    if(pick.length) SEL_DAY = pick[0];
    setView('week');
  }
}

/* ---------- shift editor ---------- */
function drawBreaks(){
  $('brkList').innerHTML = SHIFT.breaks.map(function(b,i){
    return '<div class="brk">'
      + '<div class="fld"><label>Name</label><input type="text" data-brk="n" data-i="'+i+'" value="'+esc(b.n)+'"></div>'
      + '<div class="fld"><label>From</label><input type="time" data-brk="s" data-i="'+i+'" value="'+esc(b.s)+'"></div>'
      + '<div class="fld"><label>To</label><input type="time" data-brk="e" data-i="'+i+'" value="'+esc(b.e)+'"></div>'
      + '<button class="btn x" data-delbrk="'+i+'">Remove</button></div>';
  }).join('') || '<p style="font-family:var(--mono);font-size:12px;color:var(--ink3);margin:0 0 10px">No breaks set.</p>';
}
function readBreaks(){
  var got = [];
  SHIFT.breaks.forEach(function(b,i){ got[i] = {n:b.n, s:b.s, e:b.e}; });
  Array.prototype.forEach.call($('brkList').querySelectorAll('[data-brk]'), function(el){
    var i = +el.getAttribute('data-i');
    if(got[i]) got[i][el.getAttribute('data-brk')] = el.value;
  });
  SHIFT.breaks = got.filter(function(b){ return b && toMin(b.e) > toMin(b.s); });
}
function shiftSummary(){
  if(SHIFT.ignored) return 'Shift ignored — output only, no availability';
  return SHIFT.start+'–'+SHIFT.end+' · '+Math.round(plannedPerDay())+' min rostered · '
    + SHIFT.breaks.length+' break'+(SHIFT.breaks.length===1?'':'s')+' · stop over '+SHIFT.stopMin+' min';
}
function applyShift(){
  readBreaks();
  SHIFT.start = $('sStart').value || SHIFT.start;
  SHIFT.end = $('sEnd').value || SHIFT.end;
  SHIFT.stopMin = Math.max(1, +$('sStop').value || SHIFT.stopMin);
  SHIFT.ignored = $('sIgnore').checked;
  if(toMin(SHIFT.end) <= toMin(SHIFT.start)) SHIFT.end = '23:59';
  recompute(); drawBreaks(); render();
}
function resetShift(){
  SHIFT = clone(SHIFT0);
  fillShiftFields(); recompute(); render();
}
function fillShiftFields(){
  $('sStart').value = SHIFT.start;
  $('sEnd').value = SHIFT.end;
  $('sStop').value = SHIFT.stopMin;
  $('sIgnore').checked = SHIFT.ignored;
  drawBreaks();
}

/* ---------- header, footer and the honesty notes ---------- */
function drawHeader(){
  var lastDate = dateOf(LAST), firstDate = dateOf(0);
  $('plateMeta').innerHTML =
    '<b>'+esc(D.serial)+'</b>'+(D.site?' · '+esc(D.site):'')+'<br>'
    + 'Production '+firstDate.getDate()+' '+MONF[firstDate.getMonth()].slice(0,3)+' '+firstDate.getFullYear()
    + ' to '+lastDate.getDate()+' '+MONF[lastDate.getMonth()].slice(0,3)+' '+lastDate.getFullYear()+'<br>'
    + 'Prepared '+esc(D.prepared);

  var ageDays = Math.round((dayNum(new Date()) - dayNum(lastDate)));
  var bar = $('freshBar');
  bar.className = 'fresh ' + (ageDays<=7 ? 'ok' : (ageDays<=30 ? 'stale' : 'bad'));
  bar.innerHTML = '<b>Data runs to '+dayFull(LAST)+'</b> — '
    + (ageDays<=0 ? 'today' : ageDays+' day'+(ageDays===1?'':'s')+' ago')
    + '. Anything after that is not in this report.';

  $('footLeft').textContent = 'Spida Machinery · ' + D.name + ' · ' + D.serial;
  $('footMeta').textContent = 'Prepared ' + D.prepared;
}
function drawNotice(){
  var whole = roll(statsFor(function(){ var a=[]; for(var i=0;i<=LAST;i++) a.push(i); return a; }()));
  var parts = [];

  parts.push('<b>How to read this.</b> Panel counts, cube and lineal metres are measured from the '
    + 'machine’s own production log. Availability is not measured — it rests on the shift model '
    + 'above, which you can change. Two sites on different shift models are not comparable.');

  if(D.superseded)
    parts.push(n0(D.superseded)+' panel start(s) never closed because a different panel name started. '
      + 'Panel names on these machines are reused labels rather than unique identifiers, so those are '
      + 'the operator moving around the HMI, not abandoned panels. They are left out of every figure here.');

  if(D.counterOffDays)
    parts.push(D.counterOffDays+' day(s) had no fastener counter reporting at all. On those days a zero '
      + 'fastener count says nothing, so build time alone decides whether a panel was made.');

  /* The page does its own availability maths so an edited roster means something. This
     checks that answer against the one C# produced for the shift the report shipped with. */
  if(!SHIFT0.ignored){
    var csPlanned = CS.reduce(function(t,d){ return t+d.planned; }, 0);
    var csRun = CS.reduce(function(t,d){ return t+d.run; }, 0);
    var csAvail = csPlanned>0 ? csRun/csPlanned*100 : null;
    if(csAvail!=null && whole.avail!=null && Math.abs(csAvail - whole.avail) > 1){
      parts.push('<b>Check this.</b> The page works out '+whole.avail.toFixed(1)+'% availability for the '
        + 'shift it shipped with, where the analysis behind it works out '+csAvail.toFixed(1)+'%. '
        + 'Those should agree. Tell support the machine and the date range.');
    }
  }

  $('notice').innerHTML = parts.join('<br><br>');
  $('notice').className = 'notice';
  $('shiftNow').textContent = shiftSummary();
}

/* ---------- view plumbing ---------- */
var VIEWNOTE = {
  month: 'A whole month at a time. Every day is in the strip, including the ones that produced nothing — a day at zero while the site was working is the thing worth finding.',
  week:  'One week at a time. Click any day to open it.',
  day:   'One day at a time, drawn the way the machine worked it.',
  hour:  'The selected day broken into hours. Rates are built only from clean cycles, so output and minutes always cover the same work.'
};
function setView(v){
  VIEW = v;
  ['month','week','day','hour'].forEach(function(k){
    $('tab-'+k).setAttribute('aria-selected', k===v ? 'true' : 'false');
    $('view-'+k).classList.toggle('hidden', k!==v);
  });
  render();
}
function setMetric(k){
  MET = k;
  ['panels','cube','lineal'].forEach(function(m){
    $('met-'+m).setAttribute('aria-pressed', m===k ? 'true' : 'false');
  });
  render();
}
function render(){
  $('tabnote').textContent = VIEWNOTE[VIEW];
  drawNav();
  drawNotice();

  var empty = $('emptyState'), hasAny = false;
  if(VIEW === 'month') hasAny = statsFor(monthDays(SEL_MONTH)).length > 0;
  else if(VIEW === 'week') hasAny = statsFor(weekDaysOf(SEL_WEEK)).length > 0;
  else hasAny = SEL_DAY >= 0 && SEL_DAY <= LAST;

  $('view-'+VIEW).classList.toggle('hidden', !hasAny);
  empty.classList.toggle('hidden', hasAny);
  if(!hasAny){
    empty.textContent = 'Nothing stored for this period. Use the arrows, or Latest data.';
    return;
  }

  if(VIEW === 'month') renderMonth();
  else if(VIEW === 'week') renderWeek();
  else if(VIEW === 'day') renderDay();
  else renderHour();
}

/* ---------- wiring ---------- */
document.addEventListener('click', function(e){
  var go = e.target.closest ? e.target.closest('[data-go]') : null;
  if(go && !go.disabled){ goTo(go.getAttribute('data-go')); return; }
  var del = e.target.closest ? e.target.closest('[data-delbrk]') : null;
  if(del){ readBreaks(); SHIFT.breaks.splice(+del.getAttribute('data-delbrk'), 1); drawBreaks(); return; }
  var tab = e.target.closest ? e.target.closest('[data-view]') : null;
  if(tab){ setView(tab.getAttribute('data-view')); return; }
  var met = e.target.closest ? e.target.closest('[data-met]') : null;
  if(met){ setMetric(met.getAttribute('data-met')); return; }
});
$('arwPrev').addEventListener('click', function(){ step(-1); });
$('arwNext').addEventListener('click', function(){ step(1); });
$('btnLatest').addEventListener('click', jumpLatest);
$('btnApplyShift').addEventListener('click', applyShift);
$('btnResetShift').addEventListener('click', resetShift);
$('btnAddBreak').addEventListener('click', function(){
  readBreaks();
  SHIFT.breaks.push({n:'Break', s:'10:00', e:'10:15'});
  drawBreaks();
});
$('shiftHd').addEventListener('click', function(){ $('shiftBd').classList.toggle('hidden'); });
$('shiftHd').addEventListener('keydown', function(e){
  if(e.key==='Enter' || e.key===' '){ e.preventDefault(); $('shiftBd').classList.toggle('hidden'); }
});
document.addEventListener('keydown', function(e){
  if(e.target && /INPUT|SELECT|TEXTAREA/.test(e.target.tagName)) return;
  if(e.key === 'ArrowLeft'){ step(-1); }
  else if(e.key === 'ArrowRight'){ step(1); }
});

recompute();
fillShiftFields();
drawHeader();
setView('day');
})();
""";
}
